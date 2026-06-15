using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs.Admin;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for logging and querying admin audit logs.
/// </summary>
public interface IAdminAuditService
{
    /// <summary>
    /// Log an admin action.
    /// </summary>
    Task LogAsync(
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
        int? durationMs = null);

    /// <summary>
    /// Search audit logs with filters.
    /// </summary>
    Task<AdminAuditSearchResult> SearchAsync(AdminAuditSearchRequest request);

    /// <summary>
    /// Get audit log by ID.
    /// </summary>
    Task<AdminAuditLog?> GetByIdAsync(Guid id);

    /// <summary>
    /// Get audit logs for a specific admin user.
    /// </summary>
    Task<List<AdminAuditLogDto>> GetByAdminUserAsync(Guid adminUserId, int limit = 50);

    /// <summary>
    /// Get audit logs for a specific target.
    /// </summary>
    Task<List<AdminAuditLogDto>> GetByTargetAsync(string targetType, string targetId, int limit = 50);

    /// <summary>
    /// Export audit logs to CSV.
    /// </summary>
    Task<byte[]> ExportToCsvAsync(AdminAuditSearchRequest request);
}

// ============================================================================
// DTOs for Audit Service
// ============================================================================

public record AdminAuditSearchRequest
{
    public Guid? AdminUserId { get; init; }
    public string? Action { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? Outcome { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string? SearchTerm { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string SortBy { get; init; } = "createdAt";
    public bool SortDescending { get; init; } = true;
}

public record AdminAuditSearchResult
{
    public List<AdminAuditLogDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

public record AdminAuditLogDto
{
    public Guid Id { get; init; }
    public Guid AdminUserId { get; init; }
    public string AdminUserName { get; init; } = string.Empty;
    public string AdminUserEmail { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string? TargetId { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object> Details { get; init; } = new();
    public string Outcome { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public int? DurationMs { get; init; }
    public DateTime CreatedAt { get; init; }
}
