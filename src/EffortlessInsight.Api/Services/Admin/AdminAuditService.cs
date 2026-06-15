using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Admin;

public class AdminAuditService : IAdminAuditService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(
        ApplicationDbContext dbContext,
        ILogger<AdminAuditService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAsync(
        Guid adminUserId,
        string action,
        string targetType,
        string? targetId,
        string? description = null,
        Dictionary<string, object>? details = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? sessionId = null,
        string outcome = "success",
        string? errorMessage = null,
        int? durationMs = null)
    {
        try
        {
            var auditLog = new AdminAuditLog
            {
                AdminUserId = adminUserId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Description = description,
                Details = details ?? new Dictionary<string, object>(),
                Outcome = outcome,
                ErrorMessage = errorMessage,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                SessionId = sessionId,
                DurationMs = durationMs,
                RequestId = GetRequestId()
            };

            _dbContext.AdminAuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Admin audit: {Action} on {TargetType}/{TargetId} by admin {AdminId} - {Outcome}",
                action, targetType, targetId, adminUserId, outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write admin audit log: {Action} on {TargetType}/{TargetId}",
                action, targetType, targetId);
            // Don't throw - audit logging should not break the main operation
        }
    }

    public async Task<AdminAuditSearchResult> SearchAsync(AdminAuditSearchRequest request)
    {
        var query = _dbContext.AdminAuditLogs
            .Include(a => a.AdminUser)
            .AsQueryable();

        // Apply filters
        if (request.AdminUserId.HasValue)
        {
            query = query.Where(a => a.AdminUserId == request.AdminUserId.Value);
        }

        if (!string.IsNullOrEmpty(request.Action))
        {
            query = query.Where(a => a.Action.Contains(request.Action));
        }

        if (!string.IsNullOrEmpty(request.TargetType))
        {
            query = query.Where(a => a.TargetType == request.TargetType);
        }

        if (!string.IsNullOrEmpty(request.TargetId))
        {
            query = query.Where(a => a.TargetId == request.TargetId);
        }

        if (!string.IsNullOrEmpty(request.Outcome))
        {
            query = query.Where(a => a.Outcome == request.Outcome);
        }

        if (request.StartDate.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            query = query.Where(a => a.CreatedAt <= request.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            query = query.Where(a =>
                (a.Description != null && a.Description.ToLower().Contains(term)) ||
                a.Action.ToLower().Contains(term) ||
                (a.TargetId != null && a.TargetId.ToLower().Contains(term)) ||
                a.AdminUser.Name.ToLower().Contains(term) ||
                a.AdminUser.Email.ToLower().Contains(term));
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = request.SortBy.ToLower() switch
        {
            "action" => request.SortDescending
                ? query.OrderByDescending(a => a.Action)
                : query.OrderBy(a => a.Action),
            "adminuser" => request.SortDescending
                ? query.OrderByDescending(a => a.AdminUser.Name)
                : query.OrderBy(a => a.AdminUser.Name),
            "targettype" => request.SortDescending
                ? query.OrderByDescending(a => a.TargetType)
                : query.OrderBy(a => a.TargetType),
            _ => request.SortDescending
                ? query.OrderByDescending(a => a.CreatedAt)
                : query.OrderBy(a => a.CreatedAt)
        };

        // Apply pagination
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => MapToDto(a))
            .ToListAsync();

        return new AdminAuditSearchResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize)
        };
    }

    public async Task<AdminAuditLog?> GetByIdAsync(Guid id)
    {
        return await _dbContext.AdminAuditLogs
            .Include(a => a.AdminUser)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<AdminAuditLogDto>> GetByAdminUserAsync(Guid adminUserId, int limit = 50)
    {
        return await _dbContext.AdminAuditLogs
            .Include(a => a.AdminUser)
            .Where(a => a.AdminUserId == adminUserId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => MapToDto(a))
            .ToListAsync();
    }

    public async Task<List<AdminAuditLogDto>> GetByTargetAsync(string targetType, string targetId, int limit = 50)
    {
        return await _dbContext.AdminAuditLogs
            .Include(a => a.AdminUser)
            .Where(a => a.TargetType == targetType && a.TargetId == targetId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => MapToDto(a))
            .ToListAsync();
    }

    public async Task<byte[]> ExportToCsvAsync(AdminAuditSearchRequest request)
    {
        // Remove pagination for export
        request = request with { Page = 1, PageSize = 10000 };

        var result = await SearchAsync(request);

        var sb = new StringBuilder();

        // Header row
        sb.AppendLine("ID,Admin User,Email,Action,Target Type,Target ID,Description,Outcome,IP Address,Created At");

        // Data rows
        foreach (var item in result.Items)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(item.Id.ToString()),
                EscapeCsv(item.AdminUserName),
                EscapeCsv(item.AdminUserEmail),
                EscapeCsv(item.Action),
                EscapeCsv(item.TargetType),
                EscapeCsv(item.TargetId),
                EscapeCsv(item.Description),
                EscapeCsv(item.Outcome),
                EscapeCsv(item.IpAddress),
                EscapeCsv(item.CreatedAt.ToString("O"))
            ));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static AdminAuditLogDto MapToDto(AdminAuditLog audit) => new()
    {
        Id = audit.Id,
        AdminUserId = audit.AdminUserId,
        AdminUserName = audit.AdminUser?.Name ?? "Unknown",
        AdminUserEmail = audit.AdminUser?.Email ?? "Unknown",
        Action = audit.Action,
        TargetType = audit.TargetType,
        TargetId = audit.TargetId,
        Description = audit.Description,
        Details = audit.Details,
        Outcome = audit.Outcome,
        ErrorMessage = audit.ErrorMessage,
        IpAddress = audit.IpAddress,
        UserAgent = audit.UserAgent,
        DurationMs = audit.DurationMs,
        CreatedAt = audit.CreatedAt
    };

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static string? GetRequestId()
    {
        // In a real implementation, get this from the current HTTP context
        return null;
    }
}
