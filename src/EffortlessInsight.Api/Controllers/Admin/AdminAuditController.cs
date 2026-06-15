using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for audit logs.
/// </summary>
[Authorize(Policy = "AdminAuthenticated")]
public class AdminAuditController : AdminControllerBase
{
    private readonly IAdminAuditService _auditService;
    private readonly ApplicationDbContext _dbContext;

    public AdminAuditController(
        IAdminAuditService auditService,
        ApplicationDbContext dbContext,
        ILogger<AdminAuditController> logger)
        : base(logger)
    {
        _auditService = auditService;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Search admin audit logs.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminAuditSearchResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAuditLogs([FromQuery] AuditSearchRequest request)
    {
        if (!HasPermission(AdminPermissions.AuditView))
        {
            return Forbid();
        }

        var searchRequest = new AdminAuditSearchRequest
        {
            AdminUserId = request.AdminUserId,
            Action = request.Action,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            Outcome = request.Outcome,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            SearchTerm = request.Search,
            Page = request.Page,
            PageSize = request.PageSize,
            SortBy = request.SortBy ?? "createdAt",
            SortDescending = request.SortDesc
        };

        var result = await _auditService.SearchAsync(searchRequest);
        return Success(result);
    }

    /// <summary>
    /// Get audit log details.
    /// </summary>
    [HttpGet("{auditId:guid}")]
    [ProducesResponseType(typeof(AdminAuditLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuditLog(Guid auditId)
    {
        if (!HasPermission(AdminPermissions.AuditView))
        {
            return Forbid();
        }

        var audit = await _auditService.GetByIdAsync(auditId);
        if (audit == null)
        {
            return NotFoundResponse("Audit log not found");
        }

        return Success(new AdminAuditLogDto
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
        });
    }

    /// <summary>
    /// Get audit logs for a specific admin user.
    /// </summary>
    [HttpGet("by-admin/{adminId:guid}")]
    [ProducesResponseType(typeof(List<AdminAuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByAdmin(Guid adminId, [FromQuery] int limit = 50)
    {
        if (!HasPermission(AdminPermissions.AuditView))
        {
            return Forbid();
        }

        var logs = await _auditService.GetByAdminUserAsync(adminId, limit);
        return Success(logs);
    }

    /// <summary>
    /// Get audit logs for a specific target.
    /// </summary>
    [HttpGet("by-target/{targetType}/{targetId}")]
    [ProducesResponseType(typeof(List<AdminAuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByTarget(string targetType, string targetId, [FromQuery] int limit = 50)
    {
        if (!HasPermission(AdminPermissions.AuditView))
        {
            return Forbid();
        }

        var logs = await _auditService.GetByTargetAsync(targetType, targetId, limit);
        return Success(logs);
    }

    /// <summary>
    /// Export audit logs to CSV.
    /// </summary>
    [HttpPost("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAuditLogs([FromBody] AuditExportRequest request)
    {
        if (!HasPermission(AdminPermissions.AuditExport))
        {
            return Forbid();
        }

        var searchRequest = new AdminAuditSearchRequest
        {
            AdminUserId = request.AdminUserId,
            Action = request.Action,
            TargetType = request.TargetType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Page = 1,
            PageSize = 10000
        };

        var csvBytes = await _auditService.ExportToCsvAsync(searchRequest);

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.AuditExported,
            AuditTargetTypes.AuditLog,
            null,
            "Audit logs exported",
            new Dictionary<string, object>
            {
                ["filters"] = new { request.AdminUserId, request.Action, request.TargetType, request.StartDate, request.EndDate }
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return File(csvBytes, "text/csv", $"audit_logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    /// <summary>
    /// Get audit action types for filtering.
    /// </summary>
    [HttpGet("actions")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActionTypes()
    {
        var actions = await _dbContext.AdminAuditLogs
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();

        return Success(actions);
    }

    /// <summary>
    /// Get audit target types for filtering.
    /// </summary>
    [HttpGet("target-types")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTargetTypes()
    {
        var targetTypes = await _dbContext.AdminAuditLogs
            .Select(a => a.TargetType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        return Success(targetTypes);
    }

    /// <summary>
    /// Get audit statistics.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(AuditStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats([FromQuery] string period = "7d")
    {
        if (!HasPermission(AdminPermissions.AuditView))
        {
            return Forbid();
        }

        var (startDate, endDate) = GetPeriodDates(period);

        var totalActions = await _dbContext.AdminAuditLogs
            .CountAsync(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate);

        var actionsByType = await _dbContext.AdminAuditLogs
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
            .GroupBy(a => a.Action)
            .Select(g => new ActionCountDto { Action = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var actionsByAdmin = await _dbContext.AdminAuditLogs
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
            .Include(a => a.AdminUser)
            .GroupBy(a => new { a.AdminUserId, AdminName = a.AdminUser != null ? a.AdminUser.Name : "Unknown" })
            .Select(g => new AdminActionCountDto { AdminId = g.Key.AdminUserId, AdminName = g.Key.AdminName, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var failedActions = await _dbContext.AdminAuditLogs
            .CountAsync(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate && a.Outcome == AuditOutcomes.Failure);

        return Success(new AuditStatsResponse
        {
            Period = new PeriodInfo { Start = startDate, End = endDate },
            TotalActions = totalActions,
            FailedActions = failedActions,
            ActionsByType = actionsByType,
            ActionsByAdmin = actionsByAdmin
        });
    }

    private static (DateTime Start, DateTime End) GetPeriodDates(string period)
    {
        var end = DateTime.UtcNow;
        var start = period switch
        {
            "24h" => end.AddHours(-24),
            "7d" => end.AddDays(-7),
            "30d" => end.AddDays(-30),
            "90d" => end.AddDays(-90),
            _ => end.AddDays(-7)
        };
        return (start, end);
    }
}

// DTOs

public record AuditSearchRequest
{
    public Guid? AdminUserId { get; init; }
    public string? Action { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? Outcome { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? SortBy { get; init; }
    public bool SortDesc { get; init; } = true;
}

public record AuditExportRequest
{
    public Guid? AdminUserId { get; init; }
    public string? Action { get; init; }
    public string? TargetType { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

public record AuditStatsResponse
{
    public PeriodInfo Period { get; init; } = new();
    public int TotalActions { get; init; }
    public int FailedActions { get; init; }
    public List<ActionCountDto> ActionsByType { get; init; } = [];
    public List<AdminActionCountDto> ActionsByAdmin { get; init; } = [];
}

public record ActionCountDto
{
    public string Action { get; init; } = string.Empty;
    public int Count { get; init; }
}

public record AdminActionCountDto
{
    public Guid AdminId { get; init; }
    public string AdminName { get; init; } = string.Empty;
    public int Count { get; init; }
}
