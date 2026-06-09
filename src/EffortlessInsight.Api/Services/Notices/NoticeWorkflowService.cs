using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Result of a status transition validation.
/// </summary>
public record StatusTransitionResult
{
    public bool IsAllowed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresReason { get; init; }

    public static StatusTransitionResult Allowed(bool requiresReason = false) => new()
    {
        IsAllowed = true,
        RequiresReason = requiresReason
    };

    public static StatusTransitionResult Denied(string errorCode, string errorMessage) => new()
    {
        IsAllowed = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Service for managing notice status transitions following defined workflow rules.
/// </summary>
public interface INoticeWorkflowService
{
    /// <summary>
    /// Validates if a status transition is allowed.
    /// </summary>
    StatusTransitionResult ValidateTransition(string currentStatus, string newStatus);

    /// <summary>
    /// Gets all allowed transitions from the current status.
    /// </summary>
    IReadOnlyList<string> GetAllowedTransitions(string currentStatus);

    /// <summary>
    /// Checks if a transition requires a reason.
    /// </summary>
    bool RequiresReason(string currentStatus, string newStatus);

    /// <summary>
    /// Gets the next automatic status after processing completes successfully.
    /// </summary>
    string GetStatusAfterProcessing(bool success);

    /// <summary>
    /// Determines the initial status for a newly uploaded notice.
    /// </summary>
    string GetInitialStatus();

    /// <summary>
    /// Determines the priority based on notice characteristics.
    /// </summary>
    string CalculatePriority(
        string? noticeType,
        string? noticeCategory,
        DateOnly? responseDeadline,
        decimal? totalDemand);
}

/// <summary>
/// Implementation of notice workflow service.
/// </summary>
public class NoticeWorkflowService : INoticeWorkflowService
{
    private readonly ILogger<NoticeWorkflowService> _logger;

    /// <summary>
    /// Defines valid status transitions.
    /// Key: current status, Value: list of allowed next statuses
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Initial upload
        [NoticeStatus.Uploaded] = [NoticeStatus.Processing, NoticeStatus.Failed],

        // Processing
        [NoticeStatus.Processing] = [NoticeStatus.Analyzed, NoticeStatus.Failed],

        // Analyzed - user can start working, archive, or close
        [NoticeStatus.Analyzed] = [NoticeStatus.InProgress, NoticeStatus.Archived, NoticeStatus.Closed],

        // In Progress - can complete or go back to analyzed
        [NoticeStatus.InProgress] = [NoticeStatus.Responded, NoticeStatus.Analyzed],

        // Responded - can close or need more work
        [NoticeStatus.Responded] = [NoticeStatus.Closed, NoticeStatus.InProgress],

        // Closed - can only archive
        [NoticeStatus.Closed] = [NoticeStatus.Archived],

        // Failed - can retry or abandon
        [NoticeStatus.Failed] = [NoticeStatus.Processing, NoticeStatus.Archived],

        // Archived - can restore
        [NoticeStatus.Archived] = [NoticeStatus.Analyzed]
    };

    /// <summary>
    /// Transitions that require a reason.
    /// </summary>
    private static readonly HashSet<(string From, string To)> TransitionsRequiringReason =
    [
        (NoticeStatus.Analyzed, NoticeStatus.Closed),     // Closing without response
        (NoticeStatus.InProgress, NoticeStatus.Analyzed), // Going back
        (NoticeStatus.Responded, NoticeStatus.InProgress), // Reopening
        (NoticeStatus.Failed, NoticeStatus.Archived),      // Abandoning
        (NoticeStatus.Archived, NoticeStatus.Analyzed)     // Restoring
    ];

    /// <summary>
    /// Notice types that are considered high priority.
    /// </summary>
    private static readonly HashSet<string> HighPriorityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DRC-01", "DRC-07", "DRC-13", "DRC-22", // Demand & Recovery
        "REG-17", "REG-19", "REG-23", "REG-27", // Registration cancellation
        "DGGI"   // Investigation
    };

    /// <summary>
    /// Notice categories that are critical.
    /// </summary>
    private static readonly HashSet<string> CriticalCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "investigation", "demand", "recovery"
    };

    public NoticeWorkflowService(ILogger<NoticeWorkflowService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public StatusTransitionResult ValidateTransition(string currentStatus, string newStatus)
    {
        // Normalize
        currentStatus = currentStatus.ToLowerInvariant();
        newStatus = newStatus.ToLowerInvariant();

        // Same status is a no-op
        if (currentStatus == newStatus)
        {
            return StatusTransitionResult.Allowed();
        }

        // Check if current status exists
        if (!AllowedTransitions.TryGetValue(currentStatus, out var allowedNext))
        {
            return StatusTransitionResult.Denied(
                "INVALID_CURRENT_STATUS",
                $"Unknown status '{currentStatus}'");
        }

        // Check if new status is valid
        if (!NoticeStatus.IsValid(newStatus))
        {
            return StatusTransitionResult.Denied(
                "INVALID_NEW_STATUS",
                $"Unknown status '{newStatus}'");
        }

        // Check if transition is allowed
        if (!allowedNext.Contains(newStatus, StringComparer.OrdinalIgnoreCase))
        {
            var allowedList = string.Join(", ", allowedNext);
            return StatusTransitionResult.Denied(
                "INVALID_TRANSITION",
                $"Cannot transition from '{currentStatus}' to '{newStatus}'. Allowed transitions: {allowedList}");
        }

        // Check if reason is required
        var requiresReason = RequiresReason(currentStatus, newStatus);

        return StatusTransitionResult.Allowed(requiresReason);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllowedTransitions(string currentStatus)
    {
        if (AllowedTransitions.TryGetValue(currentStatus.ToLowerInvariant(), out var allowed))
        {
            return allowed;
        }

        return [];
    }

    /// <inheritdoc />
    public bool RequiresReason(string currentStatus, string newStatus)
    {
        return TransitionsRequiringReason.Contains(
            (currentStatus.ToLowerInvariant(), newStatus.ToLowerInvariant()));
    }

    /// <inheritdoc />
    public string GetStatusAfterProcessing(bool success)
    {
        return success ? NoticeStatus.Analyzed : NoticeStatus.Failed;
    }

    /// <inheritdoc />
    public string GetInitialStatus()
    {
        return NoticeStatus.Uploaded;
    }

    /// <inheritdoc />
    public string CalculatePriority(
        string? noticeType,
        string? noticeCategory,
        DateOnly? responseDeadline,
        decimal? totalDemand)
    {
        var score = 0;

        // Category-based scoring
        if (!string.IsNullOrEmpty(noticeCategory) &&
            CriticalCategories.Contains(noticeCategory))
        {
            score += 4;
        }

        // Notice type scoring
        if (!string.IsNullOrEmpty(noticeType) &&
            HighPriorityTypes.Contains(noticeType))
        {
            score += 3;
        }

        // Deadline scoring
        if (responseDeadline.HasValue)
        {
            var daysRemaining = (responseDeadline.Value.ToDateTime(TimeOnly.MinValue) - DateTime.UtcNow).TotalDays;

            if (daysRemaining < 0)
            {
                // Overdue
                score += 5;
            }
            else if (daysRemaining <= 3)
            {
                score += 4;
            }
            else if (daysRemaining <= 7)
            {
                score += 3;
            }
            else if (daysRemaining <= 15)
            {
                score += 2;
            }
            else if (daysRemaining <= 30)
            {
                score += 1;
            }
        }

        // Amount scoring (in INR)
        if (totalDemand.HasValue)
        {
            if (totalDemand >= 1000000) // 10 lakh+
            {
                score += 3;
            }
            else if (totalDemand >= 500000) // 5 lakh+
            {
                score += 2;
            }
            else if (totalDemand >= 100000) // 1 lakh+
            {
                score += 1;
            }
        }

        // Map score to priority
        return score switch
        {
            >= 8 => NoticePriority.Critical,
            >= 5 => NoticePriority.High,
            >= 2 => NoticePriority.Medium,
            _ => NoticePriority.Low
        };
    }
}

/// <summary>
/// Extension methods for Notice status operations.
/// </summary>
public static class NoticeStatusExtensions
{
    /// <summary>
    /// Checks if the notice is in a terminal state.
    /// </summary>
    public static bool IsTerminalStatus(this Notice notice)
    {
        return notice.Status is NoticeStatus.Closed or NoticeStatus.Archived;
    }

    /// <summary>
    /// Checks if the notice is being processed.
    /// </summary>
    public static bool IsProcessing(this Notice notice)
    {
        return notice.Status is NoticeStatus.Uploaded or NoticeStatus.Processing;
    }

    /// <summary>
    /// Checks if the notice is actionable (requires user action).
    /// </summary>
    public static bool IsActionable(this Notice notice)
    {
        return notice.Status is NoticeStatus.Analyzed or NoticeStatus.InProgress or NoticeStatus.Failed;
    }

    /// <summary>
    /// Checks if the notice processing has failed.
    /// </summary>
    public static bool HasFailed(this Notice notice)
    {
        return notice.Status == NoticeStatus.Failed ||
               notice.ProcessingStatus == NoticeProcessingStatus.Failed;
    }

    /// <summary>
    /// Calculates days remaining until deadline.
    /// </summary>
    public static int? GetDaysRemaining(this Notice notice)
    {
        var effectiveDeadline = notice.ExtendedDeadline ?? notice.ResponseDeadline;
        if (!effectiveDeadline.HasValue)
            return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var remaining = effectiveDeadline.Value.DayNumber - today.DayNumber;
        return remaining;
    }

    /// <summary>
    /// Checks if the notice is overdue.
    /// </summary>
    public static bool IsOverdue(this Notice notice)
    {
        var daysRemaining = notice.GetDaysRemaining();
        return daysRemaining.HasValue && daysRemaining.Value < 0;
    }
}
