using System.Security.Claims;
using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services;

/// <summary>
/// Service for creating audit trail records of all significant actions.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs an audit event
    /// </summary>
    Task LogAsync(string action, string? entityType = null, Guid? entityId = null,
        object? oldValues = null, object? newValues = null);

    /// <summary>
    /// Logs an audit event with full context
    /// </summary>
    Task LogAsync(AuditLogEntry entry);
}

/// <summary>
/// Audit log entry for creating audit records
/// </summary>
public class AuditLogEntry
{
    public required string Action { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public object? OldValues { get; set; }
    public object? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class AuditServiceImpl : IAuditService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditServiceImpl> _logger;

    public AuditServiceImpl(
        ApplicationDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditServiceImpl> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(string action, string? entityType = null, Guid? entityId = null,
        object? oldValues = null, object? newValues = null)
    {
        await LogAsync(new AuditLogEntry
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues,
            NewValues = newValues
        });
    }

    public async Task LogAsync(AuditLogEntry entry)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // Get user ID from context if not provided
            var userId = entry.UserId;
            if (!userId.HasValue && httpContext?.User != null)
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;
                if (Guid.TryParse(userIdClaim, out var parsedUserId))
                {
                    userId = parsedUserId;
                }
            }

            // Get organization ID from context if not provided
            var organizationId = entry.OrganizationId;
            if (!organizationId.HasValue && httpContext?.User != null)
            {
                var orgIdClaim = httpContext.User.FindFirst("org_id")?.Value;
                if (Guid.TryParse(orgIdClaim, out var parsedOrgId))
                {
                    organizationId = parsedOrgId;
                }
            }

            // Get IP address
            var ipAddress = entry.IpAddress;
            if (string.IsNullOrEmpty(ipAddress) && httpContext != null)
            {
                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                ipAddress = !string.IsNullOrEmpty(forwardedFor)
                    ? forwardedFor.Split(',')[0].Trim()
                    : httpContext.Connection.RemoteIpAddress?.ToString();
            }

            // Get user agent
            var userAgent = entry.UserAgent;
            if (string.IsNullOrEmpty(userAgent) && httpContext != null)
            {
                userAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault();
            }

            var auditLog = new AuditLog
            {
                Action = entry.Action,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                UserId = userId,
                OrganizationId = organizationId,
                OldValues = ConvertToDictionary(entry.OldValues),
                NewValues = ConvertToDictionary(entry.NewValues),
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Metadata = entry.Metadata
            };

            _dbContext.AuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync();

            _logger.LogDebug("Audit log created: {Action} on {EntityType} {EntityId} by user {UserId}",
                entry.Action, entry.EntityType, entry.EntityId, userId);
        }
        catch (Exception ex)
        {
            // Never let audit logging failures break the main operation
            _logger.LogError(ex, "Failed to create audit log for action {Action}", entry.Action);
        }
    }

    private static Dictionary<string, object>? ConvertToDictionary(object? value)
    {
        if (value == null) return null;

        if (value is Dictionary<string, object> dict)
            return dict;

        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch
        {
            return new Dictionary<string, object> { ["value"] = value.ToString() ?? "" };
        }
    }
}
