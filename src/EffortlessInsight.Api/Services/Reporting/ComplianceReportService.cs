using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Reporting;

// =============================================================================
// INTERFACES
// =============================================================================

/// <summary>
/// Service for generating compliance reports (GAP-RPT-007).
/// </summary>
public interface IComplianceReportService
{
    /// <summary>
    /// Generates a deadline compliance report showing notices with deadlines met vs missed.
    /// </summary>
    Task<ComplianceReport> GenerateDeadlineComplianceReportAsync(
        Guid orgId, DateRange range, CancellationToken ct);

    /// <summary>
    /// Generates an SLA compliance report showing workflow stage adherence.
    /// </summary>
    Task<ComplianceReport> GenerateSlaComplianceReportAsync(
        Guid orgId, DateRange range, CancellationToken ct);

    /// <summary>
    /// Generates an audit report with all audit log events formatted for compliance review.
    /// </summary>
    Task<ComplianceReport> GenerateAuditReportAsync(
        Guid orgId, DateRange range, CancellationToken ct);
}

// =============================================================================
// DTOs
// =============================================================================

/// <summary>
/// Represents a compliance report.
/// </summary>
public record ComplianceReport
{
    public required string ReportType { get; init; }
    public required DateRange Period { get; init; }
    public required ComplianceSummary Summary { get; init; }
    public required List<ComplianceItem> Items { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public Guid OrganizationId { get; init; }
}

/// <summary>
/// Summary statistics for compliance reports.
/// </summary>
public record ComplianceSummary
{
    public int TotalItems { get; init; }
    public int CompliantItems { get; init; }
    public int NonCompliantItems { get; init; }
    public decimal ComplianceRate { get; init; }
    public Dictionary<string, int> BreakdownByCategory { get; init; } = [];
    public Dictionary<string, int> BreakdownByStatus { get; init; } = [];
    public List<ComplianceMetric> AdditionalMetrics { get; init; } = [];
}

/// <summary>
/// Additional metric for compliance summary.
/// </summary>
public record ComplianceMetric(string Name, string Value, string? Unit = null);

/// <summary>
/// Individual compliance item.
/// </summary>
public record ComplianceItem
{
    public Guid Id { get; init; }
    public required string ItemType { get; init; }
    public required string Description { get; init; }
    public required string Status { get; init; }
    public bool IsCompliant { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? DaysOverdue { get; init; }
    public string? AssignedTo { get; init; }
    public Guid? AssignedToId { get; init; }
    public Dictionary<string, object>? Details { get; init; }
}

// =============================================================================
// IMPLEMENTATION
// =============================================================================

public class ComplianceReportService : IComplianceReportService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ComplianceReportService> _logger;

    public ComplianceReportService(
        ApplicationDbContext context,
        ILogger<ComplianceReportService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ComplianceReport> GenerateDeadlineComplianceReportAsync(
        Guid orgId, DateRange range, CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating deadline compliance report for org {OrgId} from {Start} to {End}",
            orgId, range.StartDate, range.EndDate);

        var startDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Get notices with deadlines in the period
        var notices = await _context.Notices
            .Where(n => n.OrganizationId == orgId)
            .Where(n => n.ResponseDeadline.HasValue)
            .Where(n => n.CreatedAt >= startDateTime && n.CreatedAt <= endDateTime)
            .Include(n => n.AssignedTo)
            .Select(n => new
            {
                n.Id,
                n.NoticeNumber,
                n.NoticeType,
                n.NoticeCategory,
                n.Status,
                n.Priority,
                n.ResponseDeadline,
                n.ExtendedDeadline,
                n.CreatedAt,
                n.UpdatedAt,
                AssignedToName = n.AssignedTo != null ? n.AssignedTo.Name : null,
                AssignedToId = n.AssignedToId
            })
            .ToListAsync(ct);

        // Get deadline records for more details
        var noticeIds = notices.Select(n => n.Id).ToList();
        var deadlines = await _context.NoticeDeadlines
            .Where(d => noticeIds.Contains(d.NoticeId))
            .Select(d => new
            {
                d.NoticeId,
                d.DeadlineType,
                d.EffectiveDeadline,
                d.Status
            })
            .ToListAsync(ct);

        var items = new List<ComplianceItem>();
        var compliantCount = 0;
        var nonCompliantCount = 0;
        var byCategory = new Dictionary<string, int>();
        var byStatus = new Dictionary<string, int>();

        foreach (var notice in notices)
        {
            var effectiveDeadline = notice.ExtendedDeadline ?? notice.ResponseDeadline!.Value;
            var isClosed = notice.Status == NoticeStatus.Closed || notice.Status == NoticeStatus.Responded;
            var completedDate = isClosed && notice.UpdatedAt.HasValue ? DateOnly.FromDateTime(notice.UpdatedAt.Value) : (DateOnly?)null;

            // Determine compliance
            bool isCompliant;
            int? daysOverdue = null;

            if (isClosed)
            {
                // Closed notices: compliant if closed before deadline
                isCompliant = completedDate!.Value <= effectiveDeadline;
                if (!isCompliant)
                {
                    daysOverdue = completedDate.Value.DayNumber - effectiveDeadline.DayNumber;
                }
            }
            else
            {
                // Open notices: compliant if deadline not passed
                isCompliant = effectiveDeadline >= today;
                if (!isCompliant)
                {
                    daysOverdue = today.DayNumber - effectiveDeadline.DayNumber;
                }
            }

            if (isCompliant) compliantCount++;
            else nonCompliantCount++;

            // Track by category
            var category = notice.NoticeCategory ?? "uncategorized";
            byCategory[category] = byCategory.GetValueOrDefault(category) + 1;
            byStatus[notice.Status] = byStatus.GetValueOrDefault(notice.Status) + 1;

            items.Add(new ComplianceItem
            {
                Id = notice.Id,
                ItemType = "notice_deadline",
                Description = $"{notice.NoticeType ?? "Notice"} #{notice.NoticeNumber ?? notice.Id.ToString()[..8]}",
                Status = notice.Status,
                IsCompliant = isCompliant,
                DueDate = effectiveDeadline.ToDateTime(TimeOnly.MinValue),
                CompletedAt = completedDate?.ToDateTime(TimeOnly.MinValue),
                DaysOverdue = daysOverdue,
                AssignedTo = notice.AssignedToName,
                AssignedToId = notice.AssignedToId,
                Details = new Dictionary<string, object>
                {
                    ["noticeType"] = notice.NoticeType ?? "unknown",
                    ["priority"] = notice.Priority,
                    ["category"] = category,
                    ["wasExtended"] = notice.ExtendedDeadline.HasValue
                }
            });
        }

        var totalItems = notices.Count;
        var complianceRate = totalItems > 0 ? Math.Round((decimal)compliantCount / totalItems * 100, 2) : 100m;

        // Calculate additional metrics
        var avgDaysOverdue = items
            .Where(i => i.DaysOverdue.HasValue && i.DaysOverdue > 0)
            .Select(i => i.DaysOverdue!.Value)
            .DefaultIfEmpty(0)
            .Average();

        var criticalOverdue = items.Count(i =>
            !i.IsCompliant &&
            i.Details?.GetValueOrDefault("priority")?.ToString() == NoticePriority.Critical);

        return new ComplianceReport
        {
            ReportType = "deadline_compliance",
            Period = range,
            OrganizationId = orgId,
            Summary = new ComplianceSummary
            {
                TotalItems = totalItems,
                CompliantItems = compliantCount,
                NonCompliantItems = nonCompliantCount,
                ComplianceRate = complianceRate,
                BreakdownByCategory = byCategory,
                BreakdownByStatus = byStatus,
                AdditionalMetrics =
                [
                    new("Average Days Overdue", Math.Round(avgDaysOverdue, 1).ToString(), "days"),
                    new("Critical Overdue", criticalOverdue.ToString()),
                    new("Extended Deadlines", items.Count(i => i.Details?.GetValueOrDefault("wasExtended") is true).ToString())
                ]
            },
            Items = items.OrderBy(i => i.IsCompliant).ThenByDescending(i => i.DaysOverdue).ToList()
        };
    }

    public async Task<ComplianceReport> GenerateSlaComplianceReportAsync(
        Guid orgId, DateRange range, CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating SLA compliance report for org {OrgId} from {Start} to {End}",
            orgId, range.StartDate, range.EndDate);

        var startDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);

        // Get workflow SLA metrics for the period
        var slaMetrics = await _context.WorkflowSlaMetrics
            .Where(m => m.OrganizationId == orgId)
            .Where(m => m.PeriodStart >= startDateTime && m.PeriodEnd <= endDateTime)
            .Include(m => m.WorkflowTemplate)
            .ToListAsync(ct);

        // Get workflow stage instances to analyze individual SLA compliance
        var stageInstances = await _context.WorkflowStageInstances
            .Where(si => si.WorkflowInstance.Notice.OrganizationId == orgId)
            .Where(si => si.EnteredAt >= startDateTime && si.EnteredAt <= endDateTime)
            .Include(si => si.WorkflowStage)
            .Include(si => si.WorkflowInstance)
                .ThenInclude(wi => wi.Notice)
            .Include(si => si.AssignedTo)
            .ToListAsync(ct);

        var items = new List<ComplianceItem>();
        var compliantCount = 0;
        var nonCompliantCount = 0;
        var byCategory = new Dictionary<string, int>();
        var byStatus = new Dictionary<string, int>();

        foreach (var stage in stageInstances)
        {
            var slaDurationHours = stage.WorkflowStage?.SlaHours ?? 0;
            if (slaDurationHours == 0) continue; // No SLA defined

            var enteredAt = stage.EnteredAt;
            var exitedAt = stage.CompletedAt ?? DateTime.UtcNow;
            var actualHours = (exitedAt - enteredAt).TotalHours;

            var isCompliant = actualHours <= slaDurationHours;
            var hoursOverdue = isCompliant ? 0 : actualHours - slaDurationHours;

            if (isCompliant) compliantCount++;
            else nonCompliantCount++;

            var stageKey = stage.WorkflowStage?.StageKey ?? "unknown";
            byCategory[stageKey] = byCategory.GetValueOrDefault(stageKey) + 1;
            byStatus[stage.Status] = byStatus.GetValueOrDefault(stage.Status) + 1;

            items.Add(new ComplianceItem
            {
                Id = stage.Id,
                ItemType = "workflow_sla",
                Description = $"Stage '{stage.WorkflowStage?.Name ?? stageKey}' for Notice #{stage.WorkflowInstance?.Notice?.NoticeNumber ?? stage.WorkflowInstanceId.ToString()[..8]}",
                Status = stage.Status,
                IsCompliant = isCompliant,
                DueDate = enteredAt.AddHours(slaDurationHours),
                CompletedAt = stage.CompletedAt,
                DaysOverdue = hoursOverdue > 24 ? (int)(hoursOverdue / 24) : null,
                AssignedTo = stage.AssignedTo?.Name,
                AssignedToId = stage.AssignedToId,
                Details = new Dictionary<string, object>
                {
                    ["stageKey"] = stageKey,
                    ["slaDurationHours"] = slaDurationHours,
                    ["actualHours"] = Math.Round(actualHours, 2),
                    ["hoursOverSla"] = Math.Round(hoursOverdue, 2),
                    ["noticeId"] = stage.WorkflowInstance?.NoticeId ?? Guid.Empty
                }
            });
        }

        var totalItems = items.Count;
        var complianceRate = totalItems > 0 ? Math.Round((decimal)compliantCount / totalItems * 100, 2) : 100m;

        // Calculate additional metrics from aggregated data
        var aggregateSlaMetCount = slaMetrics.Sum(m => m.SlaMetCount);
        var aggregateSlaBreachedCount = slaMetrics.Sum(m => m.SlaBreachedCount);
        var avgProcessingTime = slaMetrics.Count > 0
            ? slaMetrics.Average(m => m.AverageProcessingTimeMinutes)
            : 0;

        return new ComplianceReport
        {
            ReportType = "sla_compliance",
            Period = range,
            OrganizationId = orgId,
            Summary = new ComplianceSummary
            {
                TotalItems = totalItems,
                CompliantItems = compliantCount,
                NonCompliantItems = nonCompliantCount,
                ComplianceRate = complianceRate,
                BreakdownByCategory = byCategory,
                BreakdownByStatus = byStatus,
                AdditionalMetrics =
                [
                    new("Aggregate SLA Met", aggregateSlaMetCount.ToString()),
                    new("Aggregate SLA Breached", aggregateSlaBreachedCount.ToString()),
                    new("Avg Processing Time", Math.Round(avgProcessingTime, 1).ToString(), "minutes"),
                    new("Total Escalations", slaMetrics.Sum(m => m.EscalationCount).ToString())
                ]
            },
            Items = items.OrderBy(i => i.IsCompliant).ThenByDescending(i => i.DaysOverdue).ToList()
        };
    }

    public async Task<ComplianceReport> GenerateAuditReportAsync(
        Guid orgId, DateRange range, CancellationToken ct)
    {
        _logger.LogInformation(
            "Generating audit report for org {OrgId} from {Start} to {End}",
            orgId, range.StartDate, range.EndDate);

        var startDateTime = range.StartDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = range.EndDate.ToDateTime(TimeOnly.MaxValue);

        // Get audit log entries for the period
        var auditLogs = await _context.AuditLogs
            .Where(a => a.OrganizationId == orgId)
            .Where(a => a.CreatedAt >= startDateTime && a.CreatedAt <= endDateTime)
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10000) // Limit for performance
            .ToListAsync(ct);

        var items = new List<ComplianceItem>();
        var byCategory = new Dictionary<string, int>();
        var byStatus = new Dictionary<string, int>();

        // Group actions by category
        var sensitiveActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "user.login",
            "user.logout",
            "user.password_change",
            "user.role_change",
            "user.2fa_enabled",
            "user.2fa_disabled",
            "organization.settings_updated",
            "organization.member_added",
            "organization.member_removed",
            "notice.deleted",
            "notice.status_changed",
            "approval.approved",
            "approval.rejected",
            "data.exported"
        };

        foreach (var log in auditLogs)
        {
            var isSensitive = sensitiveActions.Contains(log.Action);
            var actionCategory = GetActionCategory(log.Action);

            byCategory[actionCategory] = byCategory.GetValueOrDefault(actionCategory) + 1;
            byStatus[log.Action] = byStatus.GetValueOrDefault(log.Action) + 1;

            items.Add(new ComplianceItem
            {
                Id = log.Id,
                ItemType = "audit_event",
                Description = FormatAuditDescription(log),
                Status = isSensitive ? "sensitive" : "normal",
                IsCompliant = true, // Audit events are records, not compliance items per se
                CompletedAt = log.CreatedAt,
                AssignedTo = log.User?.Name,
                AssignedToId = log.UserId,
                Details = new Dictionary<string, object>
                {
                    ["action"] = log.Action,
                    ["category"] = actionCategory,
                    ["entityType"] = log.EntityType ?? "none",
                    ["entityId"] = log.EntityId?.ToString() ?? "none",
                    ["ipAddress"] = log.IpAddress ?? "unknown",
                    ["userAgent"] = log.UserAgent ?? "unknown",
                    ["isSensitive"] = isSensitive,
                    ["oldValues"] = log.OldValues ?? new Dictionary<string, object>(),
                    ["newValues"] = log.NewValues ?? new Dictionary<string, object>()
                }
            });
        }

        var totalItems = auditLogs.Count;
        var sensitiveCount = items.Count(i => i.Status == "sensitive");

        // Count unique users and actions
        var uniqueUsers = auditLogs.Where(a => a.UserId.HasValue).Select(a => a.UserId).Distinct().Count();
        var uniqueActions = auditLogs.Select(a => a.Action).Distinct().Count();
        var uniqueIps = auditLogs.Where(a => !string.IsNullOrEmpty(a.IpAddress))
            .Select(a => a.IpAddress).Distinct().Count();

        return new ComplianceReport
        {
            ReportType = "audit",
            Period = range,
            OrganizationId = orgId,
            Summary = new ComplianceSummary
            {
                TotalItems = totalItems,
                CompliantItems = totalItems, // All audit events are logged = compliant
                NonCompliantItems = 0,
                ComplianceRate = 100m,
                BreakdownByCategory = byCategory,
                BreakdownByStatus = new Dictionary<string, int>
                {
                    ["normal"] = totalItems - sensitiveCount,
                    ["sensitive"] = sensitiveCount
                },
                AdditionalMetrics =
                [
                    new("Unique Users", uniqueUsers.ToString()),
                    new("Unique Actions", uniqueActions.ToString()),
                    new("Unique IP Addresses", uniqueIps.ToString()),
                    new("Sensitive Actions", sensitiveCount.ToString()),
                    new("Actions Per Day", totalItems > 0
                        ? Math.Round((decimal)totalItems / Math.Max((range.EndDate.DayNumber - range.StartDate.DayNumber + 1), 1), 1).ToString()
                        : "0")
                ]
            },
            Items = items
        };
    }

    private static string GetActionCategory(string action)
    {
        var parts = action.Split('.');
        return parts.Length > 0 ? parts[0].ToLowerInvariant() : "other";
    }

    private static string FormatAuditDescription(AuditLog log)
    {
        var userName = log.User?.Name ?? "System";
        var action = log.Action.Replace("_", " ").Replace(".", ": ");

        if (log.EntityType != null && log.EntityId.HasValue)
        {
            return $"{userName} performed {action} on {log.EntityType} {log.EntityId.Value.ToString()[..8]}";
        }

        return $"{userName} performed {action}";
    }
}
