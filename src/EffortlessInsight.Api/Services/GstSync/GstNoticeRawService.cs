using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// Service for managing raw GST notices.
/// </summary>
public class GstNoticeRawService : IGstNoticeRawService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageServiceExtended _storageService;
    private readonly IGstSyncNotificationService _notificationService;
    private readonly ILogger<GstNoticeRawService> _logger;

    public GstNoticeRawService(
        ApplicationDbContext context,
        IFileStorageServiceExtended storageService,
        IGstSyncNotificationService notificationService,
        ILogger<GstNoticeRawService> logger)
    {
        _context = context;
        _storageService = storageService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<List<GstNoticeRawDto>> GetNoticesAsync(Guid clientId, bool? imported = null, CancellationToken cancellationToken = default)
    {
        var query = _context.GstNoticesRaw
            .Where(n => n.GstClientId == clientId);

        if (imported.HasValue)
        {
            query = query.Where(n => n.ImportedToNotices == imported.Value);
        }

        var notices = await query
            .OrderByDescending(n => n.IssueDate)
            .ThenByDescending(n => n.FirstSyncedAt)
            .ToListAsync(cancellationToken);

        return notices.Select(MapToDto).ToList();
    }

    public async Task<List<GstNoticeRawDto>> GetNoticesByOrganizationAsync(Guid organizationId, bool? imported = null, CancellationToken cancellationToken = default)
    {
        var query = _context.GstNoticesRaw
            .Where(n => n.OrganizationId == organizationId);

        if (imported.HasValue)
        {
            query = query.Where(n => n.ImportedToNotices == imported.Value);
        }

        var notices = await query
            .OrderByDescending(n => n.IssueDate)
            .ThenByDescending(n => n.FirstSyncedAt)
            .ToListAsync(cancellationToken);

        return notices.Select(MapToDto).ToList();
    }

    public async Task<GstNoticeRawDto?> GetNoticeByIdAsync(Guid noticeId, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);

        return notice == null ? null : MapToDto(notice);
    }

    public async Task<ImportNoticesResult> ImportNoticesAsync(Guid organizationId, Guid userId, ImportNoticesRequest request, CancellationToken cancellationToken = default)
    {
        var result = new ImportNoticesResult();
        var imported = new List<ImportedNoticeInfo>();
        var errors = new List<string>();
        var alreadyImportedCount = 0;
        var failedCount = 0;

        foreach (var noticeId in request.NoticeIds)
        {
            try
            {
                var rawNotice = await _context.GstNoticesRaw
                    .FirstOrDefaultAsync(n => n.Id == noticeId && n.OrganizationId == organizationId, cancellationToken);

                if (rawNotice == null)
                {
                    errors.Add($"Notice {noticeId} not found.");
                    failedCount++;
                    continue;
                }

                if (rawNotice.ImportedToNotices)
                {
                    alreadyImportedCount++;
                    continue;
                }

                // Create a new Notice in the main Notices table
                var notice = new Data.Entities.Notice
                {
                    OrganizationId = organizationId,
                    UploadedById = userId,
                    NoticeType = rawNotice.NoticeType,
                    NoticeCategory = rawNotice.NoticeCategory,
                    NoticeNumber = rawNotice.ReferenceNumber ?? rawNotice.PortalNoticeId,
                    Gstin = rawNotice.Gstin,
                    IssueDate = rawNotice.IssueDate,
                    ResponseDeadline = rawNotice.DueDate,
                    TaxAmount = rawNotice.TaxAmount,
                    InterestAmount = rawNotice.InterestAmount,
                    PenaltyAmount = rawNotice.PenaltyAmount,
                    FinancialYear = rawNotice.FinancialYear,
                    Section = rawNotice.SectionRule,
                    IssuingOfficer = rawNotice.OfficerName,
                    OfficerDesignation = rawNotice.OfficerDesignation,
                    Jurisdiction = rawNotice.Jurisdiction,
                    Status = Data.Entities.NoticeStatus.Uploaded,
                    ProcessingStatus = Data.Entities.NoticeProcessingStatus.Completed,
                    Priority = DeterminePriority(rawNotice),
                    Source = Data.Entities.NoticeSource.GstnPortal,
                    GstnNoticeId = rawNotice.PortalNoticeId,
                    // Assignment
                    AssignedToId = request.AssignToUserId,
                    AssignedById = request.AssignToUserId.HasValue ? userId : null,
                    AssignedAt = request.AssignToUserId.HasValue ? DateTime.UtcNow : null,
                    // Set required file fields with placeholders (no actual file from sync)
                    FileUrl = rawNotice.PdfS3Key ?? $"gst-sync-import/{rawNotice.Id}",
                    FileName = $"GST_Notice_{rawNotice.NoticeType}_{rawNotice.PortalNoticeId}.pdf",
                    FileSize = rawNotice.PdfSizeBytes ?? 0,
                    FileMimeType = "application/pdf",
                    Metadata = new Dictionary<string, object>
                    {
                        ["gst_sync_notice_id"] = rawNotice.Id.ToString(),
                        ["portal_status"] = rawNotice.StatusOnPortal ?? "",
                        ["tax_period"] = rawNotice.TaxPeriod ?? "",
                        ["demand_amount"] = rawNotice.DemandAmount?.ToString() ?? ""
                    }
                };

                _context.Notices.Add(notice);

                // Update raw notice with import info
                rawNotice.ImportedToNotices = true;
                rawNotice.ImportedNoticeId = notice.Id;
                rawNotice.ImportedAt = DateTime.UtcNow;

                imported.Add(new ImportedNoticeInfo(rawNotice.Id, notice.Id));
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to import notice {noticeId}: {ex.Message}");
                _logger.LogError(ex, "Failed to import notice {NoticeId}", noticeId);
                failedCount++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Imported {Count} notices for organization {OrganizationId}",
            imported.Count, organizationId);

        // Send import completion notification
        if (imported.Count > 0 || failedCount > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _notificationService.NotifyImportCompletedAsync(userId, imported.Count, failedCount, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send import completion notification to user {UserId}", userId);
                }
            }, CancellationToken.None);
        }

        return new ImportNoticesResult
        {
            Imported = imported.Count,
            AlreadyImported = alreadyImportedCount,
            Failed = failedCount,
            ImportedNotices = imported,
            Errors = errors
        };
    }

    public async Task<PdfUploadUrlResponse> GetPdfUploadUrlAsync(Guid organizationId, GetPdfUploadUrlRequest request, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .FirstOrDefaultAsync(n => n.Id == request.NoticeId && n.OrganizationId == organizationId, cancellationToken);

        if (notice == null)
        {
            throw new InvalidOperationException("Notice not found.");
        }

        // Get presigned URL from storage service
        // Note: The storage service generates its own S3 key path internally
        var presignedUrl = await _storageService.GenerateUploadUrlAsync(
            organizationId,
            notice.Id,
            request.FileName,
            "application/pdf",
            request.FileSize,
            cancellationToken);

        return new PdfUploadUrlResponse
        {
            UploadUrl = presignedUrl.Url,
            S3Key = presignedUrl.Key,
            ExpiresAt = presignedUrl.ExpiresAt
        };
    }

    public async Task<bool> ConfirmPdfUploadAsync(Guid organizationId, ConfirmPdfUploadRequest request, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .FirstOrDefaultAsync(n => n.Id == request.NoticeId && n.OrganizationId == organizationId, cancellationToken);

        if (notice == null)
        {
            return false;
        }

        notice.PdfS3Key = request.S3Key;
        notice.PdfSizeBytes = request.FileSize;
        notice.PdfDownloadedAt = DateTime.UtcNow;
        notice.PdfAvailable = true;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Confirmed PDF upload for notice {NoticeId}", notice.Id);

        return true;
    }

    private static string DeterminePriority(GstNoticeRaw notice)
    {
        // Determine priority based on notice type and due date
        if (notice.DueDate.HasValue)
        {
            var daysUntilDue = (DateTime.SpecifyKind(notice.DueDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - DateTime.UtcNow).Days;
            if (daysUntilDue <= 3)
                return "critical";
            if (daysUntilDue <= 7)
                return "high";
            if (daysUntilDue <= 15)
                return "medium";
        }

        // Priority based on notice type
        if (notice.NoticeType.StartsWith("DRC", StringComparison.OrdinalIgnoreCase))
            return "high"; // Demand and Recovery

        if (notice.NoticeType.StartsWith("ASMT", StringComparison.OrdinalIgnoreCase))
            return "medium"; // Assessment

        return "normal";
    }

    public async Task<UpcomingDueDatesResponse> GetUpcomingDueDatesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureLimit = today.AddDays(14); // 14 days in the future

        // Get notices with due dates that are overdue or coming up in the next 14 days
        var notices = await _context.GstNoticesRaw
            .Include(n => n.GstClient)
            .Where(n => n.OrganizationId == organizationId)
            .Where(n => n.DueDate.HasValue && n.DueDate.Value <= futureLimit)
            .Where(n => !n.ImportedToNotices) // Only show unimported notices
            .OrderBy(n => n.DueDate)
            .Take(50) // Limit to prevent overwhelming notifications
            .ToListAsync(cancellationToken);

        var result = notices.Select(n =>
        {
            var daysUntilDue = n.DueDate!.Value.DayNumber - today.DayNumber;
            return new UpcomingDueDateNotice
            {
                Id = n.Id,
                Gstin = n.Gstin,
                GstClientId = n.GstClientId,
                ClientName = n.GstClient?.TradeName ?? n.GstClient?.LegalName,
                NoticeType = n.NoticeType,
                DueDate = n.DueDate!.Value,
                DemandAmount = n.DemandAmount,
                DaysUntilDue = daysUntilDue,
                IsOverdue = daysUntilDue < 0
            };
        }).ToList();

        return new UpcomingDueDatesResponse
        {
            Notices = result,
            TotalCount = result.Count
        };
    }

    private static GstNoticeRawDto MapToDto(GstNoticeRaw notice)
    {
        return new GstNoticeRawDto
        {
            Id = notice.Id,
            GstClientId = notice.GstClientId,
            Gstin = notice.Gstin,
            PortalNoticeId = notice.PortalNoticeId,
            ReferenceNumber = notice.ReferenceNumber,
            NoticeType = notice.NoticeType,
            NoticeCategory = notice.NoticeCategory,
            IssueDate = notice.IssueDate,
            DueDate = notice.DueDate,
            StatusOnPortal = notice.StatusOnPortal,
            DemandAmount = notice.DemandAmount,
            TaxAmount = notice.TaxAmount,
            InterestAmount = notice.InterestAmount,
            PenaltyAmount = notice.PenaltyAmount,
            TaxPeriod = notice.TaxPeriod,
            FinancialYear = notice.FinancialYear,
            SectionRule = notice.SectionRule,
            OfficerName = notice.OfficerName,
            OfficerDesignation = notice.OfficerDesignation,
            Jurisdiction = notice.Jurisdiction,
            PdfAvailable = notice.PdfAvailable,
            PdfS3Key = notice.PdfS3Key,
            PdfSizeBytes = notice.PdfSizeBytes,
            ImportedToNotices = notice.ImportedToNotices,
            ImportedNoticeId = notice.ImportedNoticeId,
            FirstSyncedAt = notice.FirstSyncedAt,
            LastSyncedAt = notice.LastSyncedAt,
            SyncCount = notice.SyncCount
        };
    }
}
