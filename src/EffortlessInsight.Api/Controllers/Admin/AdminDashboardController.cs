using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin dashboard controller for metrics and monitoring.
/// </summary>
[Route("api/v1/admin/dashboard")]
[Authorize(Policy = "AdminAuthenticated")]
public class AdminDashboardController : AdminControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminDashboardController(
        ApplicationDbContext dbContext,
        ILogger<AdminDashboardController> logger)
        : base(logger)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get dashboard metrics for the specified period.
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(DashboardMetricsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMetrics([FromQuery] string period = "24h")
    {
        var (startDate, endDate) = GetPeriodDates(period);

        var userMetrics = await GetUserMetricsAsync(startDate, endDate);
        var orgMetrics = await GetOrganizationMetricsAsync(startDate, endDate);
        var noticeMetrics = await GetNoticeMetricsAsync(startDate, endDate);
        var revenueMetrics = await GetRevenueMetricsAsync(startDate, endDate);

        return Success(new DashboardMetricsResponse
        {
            Period = new PeriodInfo { Start = startDate, End = endDate },
            Users = userMetrics,
            Organizations = orgMetrics,
            Notices = noticeMetrics,
            Revenue = revenueMetrics,
            Health = await GetSystemHealthAsync()
        });
    }

    /// <summary>
    /// Get system health status.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(SystemHealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth()
    {
        var health = await GetSystemHealthForFrontendAsync();
        return Success(health);
    }

    /// <summary>
    /// Get active alerts.
    /// </summary>
    [HttpGet("alerts")]
    [ProducesResponseType(typeof(AlertsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlerts([FromQuery] string? status = null, [FromQuery] string? priority = null, [FromQuery] int limit = 10)
    {
        var query = _dbContext.SystemAlerts.AsQueryable();

        // Filter by status if provided, otherwise show active/acknowledged
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }
        else
        {
            query = query.Where(a => a.Status == AlertStatus.Active || a.Status == AlertStatus.Acknowledged);
        }

        // Filter by priority if provided
        if (!string.IsNullOrEmpty(priority))
        {
            query = query.Where(a => a.Priority == priority);
        }

        var totalCount = await query.CountAsync();

        var alerts = await query
            .OrderByDescending(a => a.Priority == AlertPriority.Critical)
            .ThenByDescending(a => a.Priority == AlertPriority.High)
            .ThenByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new SystemAlertDto
            {
                Id = a.Id,
                AlertType = a.AlertType,
                Category = a.Category,
                Title = a.Title,
                Description = a.Description,
                Priority = a.Priority,
                Status = a.Status,
                CreatedAt = a.CreatedAt,
                AcknowledgedAt = a.AcknowledgedAt
            })
            .ToListAsync();

        return Success(new AlertsResponse
        {
            Alerts = alerts,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Get recent admin activity.
    /// </summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(List<RecentActivityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentActivity([FromQuery] int limit = 20)
    {
        var activities = await _dbContext.AdminAuditLogs
            .Include(a => a.AdminUser)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new RecentActivityDto
            {
                Id = a.Id,
                AdminUserName = a.AdminUser != null ? a.AdminUser.Name : "Unknown",
                Action = a.Action,
                TargetType = a.TargetType,
                TargetId = a.TargetId,
                Description = a.Description,
                Outcome = a.Outcome ?? "success",
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Success(activities);
    }

    /// <summary>
    /// Acknowledge an alert.
    /// </summary>
    [HttpPost("alerts/{alertId}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeAlert(Guid alertId)
    {
        var alert = await _dbContext.SystemAlerts.FindAsync(alertId);
        if (alert == null)
        {
            return NotFoundResponse("Alert not found");
        }

        if (alert.Status != AlertStatus.Active)
        {
            return Error("Alert is not in active status", "INVALID_STATUS");
        }

        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedById = CurrentAdminId;
        alert.AcknowledgedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Success<object?>(null, "Alert acknowledged");
    }

    /// <summary>
    /// Resolve an alert.
    /// </summary>
    [HttpPost("alerts/{alertId}/resolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveAlert(Guid alertId, [FromBody] ResolveAlertRequest request)
    {
        var alert = await _dbContext.SystemAlerts.FindAsync(alertId);
        if (alert == null)
        {
            return NotFoundResponse("Alert not found");
        }

        alert.Status = AlertStatus.Resolved;
        alert.ResolvedById = CurrentAdminId;
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolutionNotes = request.Notes;
        await _dbContext.SaveChangesAsync();

        return Success<object?>(null, "Alert resolved");
    }

    // Private helper methods

    private static (DateTime Start, DateTime End) GetPeriodDates(string period)
    {
        var end = DateTime.UtcNow;
        var start = period switch
        {
            "1h" => end.AddHours(-1),
            "24h" => end.AddHours(-24),
            "7d" => end.AddDays(-7),
            "30d" => end.AddDays(-30),
            "90d" => end.AddDays(-90),
            _ => end.AddHours(-24)
        };
        return (start, end);
    }

    private async Task<UserMetrics> GetUserMetricsAsync(DateTime startDate, DateTime endDate)
    {
        var totalUsers = await _dbContext.Users.CountAsync();
        var newUsers = await _dbContext.Users.CountAsync(u => u.CreatedAt >= startDate && u.CreatedAt <= endDate);
        var activeUsers = await _dbContext.Users.CountAsync(u => u.LastLoginAt >= startDate);

        var previousStart = startDate.AddDays(-(endDate - startDate).Days);
        var previousNewUsers = await _dbContext.Users.CountAsync(u => u.CreatedAt >= previousStart && u.CreatedAt < startDate);

        var growth = previousNewUsers > 0
            ? Math.Round((double)(newUsers - previousNewUsers) / previousNewUsers * 100, 1)
            : 0;

        return new UserMetrics
        {
            Total = totalUsers,
            Active = activeUsers,
            New = newUsers,
            Growth = growth
        };
    }

    private async Task<OrganizationMetrics> GetOrganizationMetricsAsync(DateTime startDate, DateTime endDate)
    {
        var totalOrgs = await _dbContext.Organizations.CountAsync(o => o.DeletedAt == null);
        var newOrgs = await _dbContext.Organizations.CountAsync(o => o.CreatedAt >= startDate && o.CreatedAt <= endDate && o.DeletedAt == null);

        // Active organizations = organizations with notices uploaded in the period
        var activeOrgs = await _dbContext.Notices
            .Where(n => n.CreatedAt >= startDate)
            .Select(n => n.OrganizationId)
            .Distinct()
            .CountAsync();

        return new OrganizationMetrics
        {
            Total = totalOrgs,
            Active = activeOrgs,
            New = newOrgs
        };
    }

    private async Task<NoticeMetrics> GetNoticeMetricsAsync(DateTime startDate, DateTime endDate)
    {
        var total = await _dbContext.Notices.CountAsync(n => n.CreatedAt >= startDate && n.CreatedAt <= endDate);
        var processing = await _dbContext.Notices.CountAsync(n => n.ProcessingStatus == "processing");
        var completed = await _dbContext.Notices.CountAsync(n => n.ProcessingStatus == "completed" && n.CreatedAt >= startDate);
        var failed = await _dbContext.Notices.CountAsync(n => n.ProcessingStatus == "failed" && n.CreatedAt >= startDate);

        // Calculate average processing time for completed notices
        // For PostgreSQL, we calculate this in memory since DateDiffSecond is SQL Server specific
        var completedNotices = await _dbContext.Notices
            .Where(n => n.ProcessingStatus == "completed" && n.CreatedAt >= startDate && n.ProcessingCompletedAt != null)
            .Select(n => new { n.CreatedAt, n.ProcessingCompletedAt })
            .ToListAsync();

        var avgProcessingTime = completedNotices.Count > 0
            ? completedNotices.Average(n => (n.ProcessingCompletedAt!.Value - n.CreatedAt).TotalSeconds)
            : 0;

        return new NoticeMetrics
        {
            Total = total,
            Processing = processing,
            Completed = completed,
            Failed = failed,
            AvgProcessingTimeSeconds = Math.Round(avgProcessingTime, 1)
        };
    }

    private async Task<RevenueMetrics> GetRevenueMetricsAsync(DateTime startDate, DateTime endDate)
    {
        // Calculate MRR from active subscriptions
        var activeSubscriptions = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == "active")
            .Include(s => s.Plan)
            .ToListAsync();

        var mrr = activeSubscriptions.Sum(s =>
        {
            var basePricing = s.BillingCycle == "monthly"
                ? (s.Plan?.PricingMonthly ?? 0)
                : (s.Plan?.PricingAnnually ?? 0) / 12;

            var seatPricing = s.SeatsAdditional * (s.BillingCycle == "monthly"
                ? (s.Plan?.PerSeatMonthly ?? 0)
                : (s.Plan?.PerSeatAnnually ?? 0) / 12);

            return basePricing + seatPricing;
        });

        var arr = mrr * 12;

        // Calculate growth (simplified - comparing to previous period)
        var previousStart = startDate.AddDays(-(endDate - startDate).Days);
        var currentRevenue = await _dbContext.Payments
            .Where(p => p.Status == "captured" && p.CreatedAt >= startDate && p.CreatedAt <= endDate)
            .SumAsync(p => p.Amount);

        var previousRevenue = await _dbContext.Payments
            .Where(p => p.Status == "captured" && p.CreatedAt >= previousStart && p.CreatedAt < startDate)
            .SumAsync(p => p.Amount);

        var growth = previousRevenue > 0
            ? Math.Round((double)(currentRevenue - previousRevenue) / previousRevenue * 100, 1)
            : 0;

        // Calculate churn (simplified)
        var cancelledInPeriod = await _dbContext.BillingSubscriptions
            .CountAsync(s => s.CancelledAt >= startDate && s.CancelledAt <= endDate);

        var totalAtStart = await _dbContext.BillingSubscriptions
            .CountAsync(s => s.CreatedAt < startDate && (s.CancelledAt == null || s.CancelledAt >= startDate));

        var churn = totalAtStart > 0
            ? Math.Round((double)cancelledInPeriod / totalAtStart * 100, 1)
            : 0;

        // Calculate refunds in the period (sum of refunded payment amounts)
        var refunds = await _dbContext.Payments
            .Where(p => p.Status == "refunded" && p.UpdatedAt >= startDate && p.UpdatedAt <= endDate)
            .SumAsync(p => p.Amount);

        return new RevenueMetrics
        {
            Mrr = mrr,
            Arr = arr,
            Collected = currentRevenue,
            Refunds = refunds,
            Growth = growth,
            Churn = churn
        };
    }

    private async Task<SystemHealthResponse> GetSystemHealthAsync()
    {
        // In production, these would be actual health checks
        // For now, return mock healthy status
        return await Task.FromResult(new SystemHealthResponse
        {
            ApiGateway = new ServiceHealth { Status = "healthy", Latency = 45 },
            MainApi = new ServiceHealth { Status = "healthy", Latency = 120 },
            AiService = new ServiceHealth { Status = "healthy", Latency = 850 },
            Database = new ServiceHealth { Status = "healthy", Latency = 15 },
            Redis = new ServiceHealth { Status = "healthy", Latency = 2 },
            S3 = new ServiceHealth { Status = "healthy", Latency = null }
        });
    }

    private async Task<SystemHealthFrontendResponse> GetSystemHealthForFrontendAsync()
    {
        // In production, these would be actual health checks
        var components = new List<HealthComponent>
        {
            new() { Name = "API Gateway", Status = "healthy", LatencyMs = 45 },
            new() { Name = "Main API", Status = "healthy", LatencyMs = 120 },
            new() { Name = "AI Service", Status = "healthy", LatencyMs = 850 },
            new() { Name = "Database", Status = "healthy", LatencyMs = 15 },
            new() { Name = "Redis Cache", Status = "healthy", LatencyMs = 2 },
            new() { Name = "S3 Storage", Status = "healthy", LatencyMs = null }
        };

        // Determine overall status based on component statuses
        var overallStatus = "healthy";
        if (components.Any(c => c.Status == "down"))
            overallStatus = "critical";
        else if (components.Any(c => c.Status == "degraded"))
            overallStatus = "degraded";

        return await Task.FromResult(new SystemHealthFrontendResponse
        {
            Status = overallStatus,
            Components = components,
            LastCheckedAt = DateTime.UtcNow
        });
    }
}

// DTOs

public record DashboardMetricsResponse
{
    public PeriodInfo Period { get; init; } = new();
    public UserMetrics Users { get; init; } = new();
    public OrganizationMetrics Organizations { get; init; } = new();
    public NoticeMetrics Notices { get; init; } = new();
    public RevenueMetrics Revenue { get; init; } = new();
    public SystemHealthResponse Health { get; init; } = new();
}

public record PeriodInfo
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}

public record UserMetrics
{
    public int Total { get; init; }
    public int Active { get; init; }
    public int New { get; init; }
    public double Growth { get; init; }
}

public record OrganizationMetrics
{
    public int Total { get; init; }
    public int Active { get; init; }
    public int New { get; init; }
}

public record NoticeMetrics
{
    public int Total { get; init; }
    public int Processing { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public double AvgProcessingTimeSeconds { get; init; }
}

public record RevenueMetrics
{
    public decimal Mrr { get; init; }
    public decimal Arr { get; init; }
    public decimal Collected { get; init; }
    public decimal Refunds { get; init; }
    public double Growth { get; init; }
    public double Churn { get; init; }
}

public record SystemHealthResponse
{
    public ServiceHealth ApiGateway { get; init; } = new();
    public ServiceHealth MainApi { get; init; } = new();
    public ServiceHealth AiService { get; init; } = new();
    public ServiceHealth Database { get; init; } = new();
    public ServiceHealth Redis { get; init; } = new();
    public ServiceHealth S3 { get; init; } = new();
}

public record ServiceHealth
{
    public string Status { get; init; } = "healthy";
    public int? Latency { get; init; }
}

public record SystemHealthFrontendResponse
{
    public string Status { get; init; } = "healthy";
    public List<HealthComponent> Components { get; init; } = new();
    public DateTime LastCheckedAt { get; init; }
}

public record HealthComponent
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = "healthy";
    public int? LatencyMs { get; init; }
    public string? Message { get; init; }
}

public record AlertsResponse
{
    public List<SystemAlertDto> Alerts { get; init; } = new();
    public int TotalCount { get; init; }
}

public record SystemAlertDto
{
    public Guid Id { get; init; }
    public string AlertType { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
}

public record RecentActivityDto
{
    public Guid Id { get; init; }
    public string AdminUserName { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string? TargetId { get; init; }
    public string? Description { get; init; }
    public string Outcome { get; init; } = "success";
    public DateTime CreatedAt { get; init; }
}

public record ResolveAlertRequest
{
    public string? Notes { get; init; }
}
