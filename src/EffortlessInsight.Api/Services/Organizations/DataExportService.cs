using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Storage;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Request for data export specifying what data to include
/// </summary>
public record DataExportRequest
{
    public bool IncludeNotices { get; init; } = true;
    public bool IncludeMembers { get; init; } = true;
    public bool IncludeAuditLogs { get; init; } = true;
    public bool IncludeTasks { get; init; } = true;
    public bool IncludeWorkflowHistory { get; init; } = true;
    public bool IncludeComments { get; init; } = true;
    public bool IncludeDocumentRequests { get; init; } = true;
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public string Format { get; init; } = "json"; // json, csv, zip
}

/// <summary>
/// Result of initiating a data export request
/// </summary>
public record DataExportResult(
    Guid ExportId,
    string Status,
    string Message,
    DateTime EstimatedCompletionTime
);

/// <summary>
/// Status of an ongoing or completed data export
/// </summary>
public record DataExportStatus(
    Guid ExportId,
    string Status,
    string? FileUrl,
    long? FileSizeBytes,
    DateTime? ExpiresAt,
    DateTime? CompletedAt,
    string? Error,
    Dictionary<string, object>? Summary
);

/// <summary>
/// Service for exporting organization data in various formats
/// </summary>
public interface IDataExportService
{
    /// <summary>
    /// Requests a new data export for an organization
    /// </summary>
    Task<DataExportResult> RequestExportAsync(Guid orgId, DataExportRequest request, Guid requestedBy, CancellationToken ct);

    /// <summary>
    /// Gets the status of an export request
    /// </summary>
    Task<DataExportStatus> GetExportStatusAsync(Guid exportId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Downloads an export file (returns a stream)
    /// </summary>
    Task<Stream> DownloadExportAsync(Guid exportId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Gets a pre-signed download URL for an export
    /// </summary>
    Task<string> GetDownloadUrlAsync(Guid exportId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Processes an export job (called by background worker)
    /// </summary>
    Task ProcessExportJobAsync(Guid exportId, CancellationToken ct);

    /// <summary>
    /// Lists all exports for an organization
    /// </summary>
    Task<List<DataExportStatus>> ListExportsAsync(Guid orgId, Guid userId, CancellationToken ct);
}

/// <summary>
/// Implementation of data export service
/// </summary>
public class DataExportService : IDataExportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAmazonS3 _s3Client;
    private readonly S3StorageOptions _s3Options;
    private readonly IEmailService _emailService;
    private readonly IAuditService _auditService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<DataExportService> _logger;

    private const int ExportExpiryDays = 7;
    private const int MaxExportsPerDay = 5;

    public DataExportService(
        ApplicationDbContext dbContext,
        IAmazonS3 s3Client,
        IOptions<S3StorageOptions> s3Options,
        IEmailService emailService,
        IAuditService auditService,
        IBackgroundJobClient backgroundJobs,
        ILogger<DataExportService> logger)
    {
        _dbContext = dbContext;
        _s3Client = s3Client;
        _s3Options = s3Options.Value;
        _emailService = emailService;
        _auditService = auditService;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    public async Task<DataExportResult> RequestExportAsync(Guid orgId, DataExportRequest request, Guid requestedBy, CancellationToken ct)
    {
        // Validate membership and permissions
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == requestedBy && m.Status == "active", ct)
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        if (membership.Role != "owner" && membership.Role != "admin")
        {
            throw new UnauthorizedAccessException("ADMIN_REQUIRED");
        }

        // Check rate limit (max exports per day)
        var today = DateTime.UtcNow.Date;
        var exportsToday = await _dbContext.Set<DataExport>()
            .CountAsync(e => e.OrganizationId == orgId && e.CreatedAt >= today, ct);

        if (exportsToday >= MaxExportsPerDay)
        {
            throw new InvalidOperationException("EXPORT_RATE_LIMIT_EXCEEDED");
        }

        // Create export record
        var export = new DataExport
        {
            OrganizationId = orgId,
            RequestedById = requestedBy,
            Status = "pending",
            Format = request.Format.ToLowerInvariant(),
            Options = new Dictionary<string, object>
            {
                ["include_notices"] = request.IncludeNotices,
                ["include_members"] = request.IncludeMembers,
                ["include_audit_logs"] = request.IncludeAuditLogs,
                ["include_tasks"] = request.IncludeTasks,
                ["include_workflow_history"] = request.IncludeWorkflowHistory,
                ["include_comments"] = request.IncludeComments,
                ["include_document_requests"] = request.IncludeDocumentRequests,
                ["from_date"] = request.FromDate?.ToString("yyyy-MM-dd") ?? "",
                ["to_date"] = request.ToDate?.ToString("yyyy-MM-dd") ?? ""
            }
        };

        _dbContext.Set<DataExport>().Add(export);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Data export {ExportId} requested for organization {OrgId} by user {UserId}",
            export.Id, orgId, requestedBy);

        // Queue background job
        _backgroundJobs.Enqueue<IDataExportService>(s => s.ProcessExportJobAsync(export.Id, CancellationToken.None));

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "data_export.requested",
            EntityType = "DataExport",
            EntityId = export.Id,
            UserId = requestedBy,
            OrganizationId = orgId,
            NewValues = new
            {
                Format = request.Format,
                request.IncludeNotices,
                request.IncludeMembers,
                request.IncludeAuditLogs,
                request.IncludeTasks
            }
        });

        return new DataExportResult(
            ExportId: export.Id,
            Status: "pending",
            Message: "Export request queued. You will be notified when it's ready.",
            EstimatedCompletionTime: DateTime.UtcNow.AddMinutes(15)
        );
    }

    public async Task<DataExportStatus> GetExportStatusAsync(Guid exportId, Guid userId, CancellationToken ct)
    {
        var export = await _dbContext.Set<DataExport>()
            .FirstOrDefaultAsync(e => e.Id == exportId, ct)
            ?? throw new KeyNotFoundException("EXPORT_NOT_FOUND");

        // Validate access
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == export.OrganizationId && m.UserId == userId && m.Status == "active", ct)
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        // Generate fresh download URL if completed
        string? fileUrl = null;
        if (export.Status == "completed" && !string.IsNullOrEmpty(export.FileKey))
        {
            fileUrl = await GenerateDownloadUrlAsync(export.FileKey);
        }

        return new DataExportStatus(
            ExportId: export.Id,
            Status: export.Status,
            FileUrl: fileUrl,
            FileSizeBytes: export.FileSizeBytes,
            ExpiresAt: export.ExpiresAt,
            CompletedAt: export.CompletedAt,
            Error: export.Error,
            Summary: export.Summary
        );
    }

    public async Task<Stream> DownloadExportAsync(Guid exportId, Guid userId, CancellationToken ct)
    {
        var export = await _dbContext.Set<DataExport>()
            .FirstOrDefaultAsync(e => e.Id == exportId, ct)
            ?? throw new KeyNotFoundException("EXPORT_NOT_FOUND");

        // Validate access
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == export.OrganizationId && m.UserId == userId && m.Status == "active", ct)
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        if (export.Status != "completed")
        {
            throw new InvalidOperationException("EXPORT_NOT_READY");
        }

        if (export.ExpiresAt < DateTime.UtcNow)
        {
            throw new InvalidOperationException("EXPORT_EXPIRED");
        }

        if (string.IsNullOrEmpty(export.FileKey))
        {
            throw new InvalidOperationException("EXPORT_FILE_MISSING");
        }

        var response = await _s3Client.GetObjectAsync(_s3Options.BucketName, export.FileKey, ct);
        return response.ResponseStream;
    }

    public async Task<string> GetDownloadUrlAsync(Guid exportId, Guid userId, CancellationToken ct)
    {
        var export = await _dbContext.Set<DataExport>()
            .FirstOrDefaultAsync(e => e.Id == exportId, ct)
            ?? throw new KeyNotFoundException("EXPORT_NOT_FOUND");

        // Validate access
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == export.OrganizationId && m.UserId == userId && m.Status == "active", ct)
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        if (export.Status != "completed")
        {
            throw new InvalidOperationException("EXPORT_NOT_READY");
        }

        if (export.ExpiresAt < DateTime.UtcNow)
        {
            throw new InvalidOperationException("EXPORT_EXPIRED");
        }

        if (string.IsNullOrEmpty(export.FileKey))
        {
            throw new InvalidOperationException("EXPORT_FILE_MISSING");
        }

        return await GenerateDownloadUrlAsync(export.FileKey);
    }

    public async Task<List<DataExportStatus>> ListExportsAsync(Guid orgId, Guid userId, CancellationToken ct)
    {
        // Validate access
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == userId && m.Status == "active", ct)
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        var exports = await _dbContext.Set<DataExport>()
            .Where(e => e.OrganizationId == orgId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        var results = new List<DataExportStatus>();
        foreach (var export in exports)
        {
            string? fileUrl = null;
            if (export.Status == "completed" && !string.IsNullOrEmpty(export.FileKey) && export.ExpiresAt > DateTime.UtcNow)
            {
                fileUrl = await GenerateDownloadUrlAsync(export.FileKey);
            }

            results.Add(new DataExportStatus(
                ExportId: export.Id,
                Status: export.Status,
                FileUrl: fileUrl,
                FileSizeBytes: export.FileSizeBytes,
                ExpiresAt: export.ExpiresAt,
                CompletedAt: export.CompletedAt,
                Error: export.Error,
                Summary: export.Summary
            ));
        }

        return results;
    }

    public async Task ProcessExportJobAsync(Guid exportId, CancellationToken ct)
    {
        var export = await _dbContext.Set<DataExport>()
            .Include(e => e.Organization)
            .Include(e => e.RequestedBy)
            .FirstOrDefaultAsync(e => e.Id == exportId, ct);

        if (export == null)
        {
            _logger.LogWarning("Export {ExportId} not found", exportId);
            return;
        }

        if (export.Status != "pending")
        {
            _logger.LogWarning("Export {ExportId} is not in pending status: {Status}", exportId, export.Status);
            return;
        }

        try
        {
            export.Status = "processing";
            export.ProcessingStartedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Processing export {ExportId} for organization {OrgId}", exportId, export.OrganizationId);

            // Build export data
            var exportData = await BuildExportDataAsync(export, ct);

            // Generate file based on format
            var (fileContent, fileExtension) = export.Format switch
            {
                "csv" => await GenerateCsvExportAsync(exportData, ct),
                "zip" => await GenerateZipExportAsync(exportData, ct),
                _ => await GenerateJsonExportAsync(exportData, ct)
            };

            // Upload to S3
            var fileKey = $"exports/{export.OrganizationId}/{export.Id}.{fileExtension}";
            using var memoryStream = new MemoryStream(fileContent);
            var uploadRequest = new PutObjectRequest
            {
                BucketName = _s3Options.BucketName,
                Key = fileKey,
                InputStream = memoryStream,
                ContentType = GetContentType(fileExtension),
                Metadata =
                {
                    ["export-id"] = export.Id.ToString(),
                    ["organization-id"] = export.OrganizationId.ToString(),
                    ["requested-by"] = export.RequestedById.ToString()
                }
            };
            await _s3Client.PutObjectAsync(uploadRequest, ct);

            // Update export record
            export.Status = "completed";
            export.FileKey = fileKey;
            export.FileSizeBytes = fileContent.Length;
            export.ExpiresAt = DateTime.UtcNow.AddDays(ExportExpiryDays);
            export.CompletedAt = DateTime.UtcNow;
            export.Summary = new Dictionary<string, object>
            {
                ["notices_count"] = exportData.Notices?.Count ?? 0,
                ["members_count"] = exportData.Members?.Count ?? 0,
                ["audit_logs_count"] = exportData.AuditLogs?.Count ?? 0,
                ["tasks_count"] = exportData.Tasks?.Count ?? 0,
                ["workflow_history_count"] = exportData.WorkflowHistory?.Count ?? 0
            };

            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Export {ExportId} completed successfully. File size: {Size} bytes",
                exportId, fileContent.Length);

            // Send notification email
            try
            {
                await _emailService.SendTemplateAsync(export.RequestedBy.Email!, "data_export_ready", new Dictionary<string, object>
                {
                    ["organization_name"] = export.Organization.Name,
                    ["export_id"] = export.Id.ToString(),
                    ["file_size"] = FormatFileSize(fileContent.Length),
                    ["expires_at"] = export.ExpiresAt?.ToString("MMM dd, yyyy") ?? "",
                    ["download_url"] = $"/api/v1/organizations/{export.OrganizationId}/exports/{export.Id}/download"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send export ready notification for export {ExportId}", exportId);
            }

            // Audit logging
            await _auditService.LogAsync(new AuditLogEntry
            {
                Action = "data_export.completed",
                EntityType = "DataExport",
                EntityId = export.Id,
                UserId = export.RequestedById,
                OrganizationId = export.OrganizationId,
                NewValues = new
                {
                    FileSizeBytes = export.FileSizeBytes,
                    export.ExpiresAt,
                    export.Summary
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export {ExportId} failed", exportId);

            export.Status = "failed";
            export.Error = ex.Message;
            await _dbContext.SaveChangesAsync(ct);

            // Audit logging
            await _auditService.LogAsync(new AuditLogEntry
            {
                Action = "data_export.failed",
                EntityType = "DataExport",
                EntityId = export.Id,
                UserId = export.RequestedById,
                OrganizationId = export.OrganizationId,
                NewValues = new { Error = ex.Message }
            });
        }
    }

    private async Task<ExportData> BuildExportDataAsync(DataExport export, CancellationToken ct)
    {
        var options = export.Options ?? new Dictionary<string, object>();
        var orgId = export.OrganizationId;

        // Parse date filters
        DateOnly? fromDate = null;
        DateOnly? toDate = null;
        if (options.TryGetValue("from_date", out var fromStr) && !string.IsNullOrEmpty(fromStr?.ToString()))
        {
            DateOnly.TryParse(fromStr.ToString(), out var fd);
            fromDate = fd;
        }
        if (options.TryGetValue("to_date", out var toStr) && !string.IsNullOrEmpty(toStr?.ToString()))
        {
            DateOnly.TryParse(toStr.ToString(), out var td);
            toDate = td;
        }

        var data = new ExportData
        {
            OrganizationId = orgId,
            ExportedAt = DateTime.UtcNow,
            Format = export.Format
        };

        // Export notices
        if (GetBoolOption(options, "include_notices"))
        {
            var noticesQuery = _dbContext.Notices
                .IgnoreQueryFilters()
                .Where(n => n.OrganizationId == orgId && n.DeletedAt == null);

            if (fromDate.HasValue)
                noticesQuery = noticesQuery.Where(n => DateOnly.FromDateTime(n.CreatedAt) >= fromDate.Value);
            if (toDate.HasValue)
                noticesQuery = noticesQuery.Where(n => DateOnly.FromDateTime(n.CreatedAt) <= toDate.Value);

            data.Notices = await noticesQuery
                .Select(n => new NoticeExport
                {
                    Id = n.Id,
                    Type = n.NoticeType,
                    DepartmentCode = n.DepartmentCode,
                    Gstin = n.Gstin,
                    FinancialYear = n.FinancialYear,
                    Section = n.Section,
                    DueDate = n.DueDate.HasValue ? DateOnly.FromDateTime(n.DueDate.Value) : null,
                    Status = n.Status,
                    Priority = n.Priority,
                    Summary = n.Summary,
                    CreatedAt = n.CreatedAt
                })
                .ToListAsync(ct);
        }

        // Export members
        if (GetBoolOption(options, "include_members"))
        {
            data.Members = await _dbContext.OrganizationMembers
                .Where(m => m.OrganizationId == orgId)
                .Include(m => m.User)
                .Select(m => new MemberExport
                {
                    Id = m.Id,
                    UserId = m.UserId,
                    Email = m.User.Email!,
                    Name = m.User.Name,
                    Role = m.Role,
                    Status = m.Status,
                    IsExternal = m.IsExternal,
                    JoinedAt = m.JoinedAt
                })
                .ToListAsync(ct);
        }

        // Export audit logs
        if (GetBoolOption(options, "include_audit_logs"))
        {
            var auditQuery = _dbContext.AuditLogs
                .Where(a => a.OrganizationId == orgId);

            if (fromDate.HasValue)
                auditQuery = auditQuery.Where(a => DateOnly.FromDateTime(a.CreatedAt) >= fromDate.Value);
            if (toDate.HasValue)
                auditQuery = auditQuery.Where(a => DateOnly.FromDateTime(a.CreatedAt) <= toDate.Value);

            data.AuditLogs = await auditQuery
                .OrderByDescending(a => a.CreatedAt)
                .Take(10000) // Limit audit logs
                .Select(a => new AuditLogExport
                {
                    Id = a.Id,
                    Action = a.Action,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    UserId = a.UserId,
                    IpAddress = a.IpAddress,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync(ct);
        }

        // Export tasks
        if (GetBoolOption(options, "include_tasks"))
        {
            var tasksQuery = _dbContext.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.Notice.OrganizationId == orgId && t.DeletedAt == null);

            if (fromDate.HasValue)
                tasksQuery = tasksQuery.Where(t => DateOnly.FromDateTime(t.CreatedAt) >= fromDate.Value);
            if (toDate.HasValue)
                tasksQuery = tasksQuery.Where(t => DateOnly.FromDateTime(t.CreatedAt) <= toDate.Value);

            data.Tasks = await tasksQuery
                .Select(t => new TaskExport
                {
                    Id = t.Id,
                    NoticeId = t.NoticeId,
                    Title = t.Title,
                    Description = t.Description,
                    Status = t.Status,
                    Priority = t.Priority,
                    DueDate = t.DueDate.HasValue ? DateOnly.FromDateTime(t.DueDate.Value) : null,
                    CreatedAt = t.CreatedAt,
                    CompletedAt = t.CompletedAt
                })
                .ToListAsync(ct);
        }

        // Export workflow history
        if (GetBoolOption(options, "include_workflow_history"))
        {
            var workflowQuery = _dbContext.WorkflowHistories
                .IgnoreQueryFilters()
                .Where(w => w.Notice.OrganizationId == orgId && w.DeletedAt == null);

            if (fromDate.HasValue)
                workflowQuery = workflowQuery.Where(w => DateOnly.FromDateTime(w.CreatedAt) >= fromDate.Value);
            if (toDate.HasValue)
                workflowQuery = workflowQuery.Where(w => DateOnly.FromDateTime(w.CreatedAt) <= toDate.Value);

            data.WorkflowHistory = await workflowQuery
                .Select(w => new WorkflowHistoryExport
                {
                    Id = w.Id,
                    NoticeId = w.NoticeId,
                    EventType = w.EventType,
                    FromStage = w.FromStage,
                    ToStage = w.ToStage,
                    PerformedById = w.PerformedById,
                    Comment = w.Comment,
                    CreatedAt = w.CreatedAt
                })
                .ToListAsync(ct);
        }

        return data;
    }

    private static bool GetBoolOption(Dictionary<string, object> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
            return false;

        return value switch
        {
            bool b => b,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            JsonElement je when je.ValueKind == JsonValueKind.False => false,
            string s => bool.TryParse(s, out var result) && result,
            _ => false
        };
    }

    private static Task<(byte[], string)> GenerateJsonExportAsync(ExportData data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return Task.FromResult((Encoding.UTF8.GetBytes(json), "json"));
    }

    private static async Task<(byte[], string)> GenerateCsvExportAsync(ExportData data, CancellationToken ct)
    {
        using var memoryStream = new MemoryStream();
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

        // Notices CSV
        if (data.Notices?.Any() == true)
        {
            var entry = archive.CreateEntry("notices.csv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteLineAsync("Id,Type,DepartmentCode,Gstin,FinancialYear,Section,DueDate,Status,Priority,Summary,CreatedAt");
            foreach (var n in data.Notices)
            {
                await writer.WriteLineAsync($"\"{n.Id}\",\"{Escape(n.Type)}\",\"{Escape(n.DepartmentCode)}\",\"{Escape(n.Gstin)}\",\"{Escape(n.FinancialYear)}\",\"{Escape(n.Section)}\",\"{n.DueDate:yyyy-MM-dd}\",\"{Escape(n.Status)}\",\"{Escape(n.Priority)}\",\"{Escape(n.Summary)}\",\"{n.CreatedAt:yyyy-MM-dd HH:mm:ss}\"");
            }
        }

        // Members CSV
        if (data.Members?.Any() == true)
        {
            var entry = archive.CreateEntry("members.csv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteLineAsync("Id,UserId,Email,Name,Role,Status,IsExternal,JoinedAt");
            foreach (var m in data.Members)
            {
                await writer.WriteLineAsync($"\"{m.Id}\",\"{m.UserId}\",\"{Escape(m.Email)}\",\"{Escape(m.Name)}\",\"{Escape(m.Role)}\",\"{Escape(m.Status)}\",\"{m.IsExternal}\",\"{m.JoinedAt:yyyy-MM-dd HH:mm:ss}\"");
            }
        }

        // Audit logs CSV
        if (data.AuditLogs?.Any() == true)
        {
            var entry = archive.CreateEntry("audit_logs.csv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteLineAsync("Id,Action,EntityType,EntityId,UserId,IpAddress,CreatedAt");
            foreach (var a in data.AuditLogs)
            {
                await writer.WriteLineAsync($"\"{a.Id}\",\"{Escape(a.Action)}\",\"{Escape(a.EntityType)}\",\"{a.EntityId}\",\"{a.UserId}\",\"{Escape(a.IpAddress)}\",\"{a.CreatedAt:yyyy-MM-dd HH:mm:ss}\"");
            }
        }

        // Tasks CSV
        if (data.Tasks?.Any() == true)
        {
            var entry = archive.CreateEntry("tasks.csv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteLineAsync("Id,NoticeId,Title,Description,Status,Priority,DueDate,CreatedAt,CompletedAt");
            foreach (var t in data.Tasks)
            {
                await writer.WriteLineAsync($"\"{t.Id}\",\"{t.NoticeId}\",\"{Escape(t.Title)}\",\"{Escape(t.Description)}\",\"{Escape(t.Status)}\",\"{Escape(t.Priority)}\",\"{t.DueDate:yyyy-MM-dd}\",\"{t.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{t.CompletedAt:yyyy-MM-dd HH:mm:ss}\"");
            }
        }

        // Workflow history CSV
        if (data.WorkflowHistory?.Any() == true)
        {
            var entry = archive.CreateEntry("workflow_history.csv");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteLineAsync("Id,NoticeId,EventType,FromStage,ToStage,PerformedById,Comment,CreatedAt");
            foreach (var w in data.WorkflowHistory)
            {
                await writer.WriteLineAsync($"\"{w.Id}\",\"{w.NoticeId}\",\"{Escape(w.EventType)}\",\"{Escape(w.FromStage)}\",\"{Escape(w.ToStage)}\",\"{w.PerformedById}\",\"{Escape(w.Comment)}\",\"{w.CreatedAt:yyyy-MM-dd HH:mm:ss}\"");
            }
        }

        archive.Dispose();
        return (memoryStream.ToArray(), "zip");
    }

    private static async Task<(byte[], string)> GenerateZipExportAsync(ExportData data, CancellationToken ct)
    {
        using var memoryStream = new MemoryStream();
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

        // Add JSON file
        var (jsonBytes, _) = await GenerateJsonExportAsync(data, ct);
        var jsonEntry = archive.CreateEntry("export.json");
        await using (var stream = jsonEntry.Open())
        {
            await stream.WriteAsync(jsonBytes, ct);
        }

        // Add CSV files
        var (csvBytes, _) = await GenerateCsvExportAsync(data, ct);
        // Extract CSVs from the zip and add individually
        using var csvArchive = new ZipArchive(new MemoryStream(csvBytes), ZipArchiveMode.Read);
        foreach (var csvEntry in csvArchive.Entries)
        {
            var newEntry = archive.CreateEntry($"csv/{csvEntry.Name}");
            await using var destStream = newEntry.Open();
            await using var srcStream = csvEntry.Open();
            await srcStream.CopyToAsync(destStream, ct);
        }

        archive.Dispose();
        return (memoryStream.ToArray(), "zip");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
    }

    private async Task<string> GenerateDownloadUrlAsync(string fileKey)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _s3Options.BucketName,
            Key = fileKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddHours(1)
        };

        return await _s3Client.GetPreSignedURLAsync(request);
    }

    private static string GetContentType(string extension) => extension switch
    {
        "json" => "application/json",
        "csv" => "text/csv",
        "zip" => "application/zip",
        _ => "application/octet-stream"
    };

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} GB";
    }
}

// Export data models
internal class ExportData
{
    public Guid OrganizationId { get; set; }
    public DateTime ExportedAt { get; set; }
    public string Format { get; set; } = "json";
    public List<NoticeExport>? Notices { get; set; }
    public List<MemberExport>? Members { get; set; }
    public List<AuditLogExport>? AuditLogs { get; set; }
    public List<TaskExport>? Tasks { get; set; }
    public List<WorkflowHistoryExport>? WorkflowHistory { get; set; }
}

internal class NoticeExport
{
    public Guid Id { get; set; }
    public string? Type { get; set; }
    public string? DepartmentCode { get; set; }
    public string? Gstin { get; set; }
    public string? FinancialYear { get; set; }
    public string? Section { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class MemberExport
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsExternal { get; set; }
    public DateTime JoinedAt { get; set; }
}

internal class AuditLogExport
{
    public Guid Id { get; set; }
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? UserId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class TaskExport
{
    public Guid Id { get; set; }
    public Guid NoticeId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

internal class WorkflowHistoryExport
{
    public Guid Id { get; set; }
    public Guid NoticeId { get; set; }
    public string? EventType { get; set; }
    public string? FromStage { get; set; }
    public string? ToStage { get; set; }
    public Guid? PerformedById { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}
