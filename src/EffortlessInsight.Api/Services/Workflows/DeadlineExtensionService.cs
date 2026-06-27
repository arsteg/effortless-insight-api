using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Workflows;

/// <summary>
/// Interface for deadline extension management.
/// GAP-WF-006: Deadline Extension Integration
/// </summary>
public interface IDeadlineExtensionService
{
    /// <summary>
    /// Requests a deadline extension for a notice.
    /// </summary>
    Task<DeadlineExtension> RequestExtensionAsync(
        Guid noticeId,
        Guid deadlineId,
        int additionalDays,
        string reason,
        Guid requestedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Approves a deadline extension request.
    /// </summary>
    Task<DeadlineExtension> ApproveExtensionAsync(
        Guid extensionId,
        Guid approvedBy,
        string? comments,
        CancellationToken ct = default);

    /// <summary>
    /// Rejects a deadline extension request.
    /// </summary>
    Task<DeadlineExtension> RejectExtensionAsync(
        Guid extensionId,
        Guid rejectedBy,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all pending extension requests for an organization.
    /// </summary>
    Task<List<DeadlineExtension>> GetPendingExtensionsAsync(
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a deadline extension by ID.
    /// </summary>
    Task<DeadlineExtension?> GetExtensionByIdAsync(
        Guid extensionId,
        CancellationToken ct = default);
}

/// <summary>
/// Implementation of deadline extension management.
/// GAP-WF-006: Deadline Extension Integration
/// </summary>
public class DeadlineExtensionService : IDeadlineExtensionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DeadlineExtensionService> _logger;

    public DeadlineExtensionService(
        ApplicationDbContext context,
        ILogger<DeadlineExtensionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DeadlineExtension> RequestExtensionAsync(
        Guid noticeId,
        Guid deadlineId,
        int additionalDays,
        string reason,
        Guid requestedBy,
        CancellationToken ct = default)
    {
        // Validate the deadline exists and belongs to the notice
        var deadline = await _context.NoticeDeadlines
            .Include(d => d.Notice)
            .FirstOrDefaultAsync(d => d.Id == deadlineId && d.NoticeId == noticeId, ct);

        if (deadline == null)
        {
            throw new ArgumentException($"Deadline {deadlineId} not found for notice {noticeId}");
        }

        // Calculate new deadline
        var previousDeadline = deadline.EffectiveDeadline;
        var newDeadline = previousDeadline.AddDays(additionalDays);
        var now = DateTime.UtcNow;

        // Create extension request
        var extension = new DeadlineExtension
        {
            NoticeDeadlineId = deadlineId,
            NoticeId = noticeId,
            PreviousDeadline = previousDeadline,
            NewDeadline = newDeadline,
            DaysExtended = additionalDays,
            Reason = reason,
            ExtensionType = ExtensionTypes.Internal,
            Status = ExtensionStatuses.Pending,
            RequestedById = requestedBy,
            RequestedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.DeadlineExtensions.Add(extension);

        // Record in workflow history if workflow exists
        var workflowInstance = await _context.NoticeWorkflowInstances
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, ct);

        if (workflowInstance != null)
        {
            var historyEntry = new WorkflowHistory
            {
                WorkflowInstanceId = workflowInstance.Id,
                NoticeId = noticeId,
                EventType = WorkflowHistoryEventTypes.DeadlineExtensionRequested,
                PerformedById = requestedBy,
                Description = $"Deadline extension requested: {additionalDays} days for {deadline.DeadlineType}",
                Reason = reason,
                EventData = new Dictionary<string, object>
                {
                    ["extensionId"] = extension.Id.ToString(),
                    ["deadlineId"] = deadlineId.ToString(),
                    ["deadlineType"] = deadline.DeadlineType,
                    ["previousDeadline"] = previousDeadline.ToString("O"),
                    ["requestedNewDeadline"] = newDeadline.ToString("O"),
                    ["additionalDays"] = additionalDays
                },
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.WorkflowHistories.Add(historyEntry);
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deadline extension requested for notice {NoticeId}, deadline {DeadlineId}: {AdditionalDays} days",
            noticeId, deadlineId, additionalDays);

        return extension;
    }

    /// <inheritdoc />
    public async Task<DeadlineExtension> ApproveExtensionAsync(
        Guid extensionId,
        Guid approvedBy,
        string? comments,
        CancellationToken ct = default)
    {
        var extension = await _context.DeadlineExtensions
            .Include(e => e.NoticeDeadline)
            .Include(e => e.Notice)
            .FirstOrDefaultAsync(e => e.Id == extensionId, ct);

        if (extension == null)
        {
            throw new ArgumentException($"Extension {extensionId} not found");
        }

        if (extension.Status != ExtensionStatuses.Pending)
        {
            throw new InvalidOperationException($"Extension is already {extension.Status}");
        }

        var now = DateTime.UtcNow;

        // Update extension status
        extension.Status = ExtensionStatuses.Approved;
        extension.ReviewedById = approvedBy;
        extension.ReviewedAt = now;
        extension.ReviewNotes = comments;
        extension.UpdatedAt = now;

        // Update the actual deadline
        var deadline = extension.NoticeDeadline;
        deadline.EffectiveDeadline = extension.NewDeadline;
        deadline.Status = DeadlineStatuses.Extended;
        deadline.UpdatedAt = now;

        // Update workflow instance SLA if exists
        var workflowInstance = await _context.NoticeWorkflowInstances
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.NoticeId == extension.NoticeId && i.Status == WorkflowInstanceStatuses.Active, ct);

        if (workflowInstance != null)
        {
            // Adjust SLA deadline based on extension
            if (workflowInstance.SlaDeadline.HasValue)
            {
                workflowInstance.SlaDeadline = workflowInstance.SlaDeadline.Value.AddDays(extension.DaysExtended);
                workflowInstance.UpdatedAt = now;

                // Recalculate SLA status
                if (workflowInstance.CurrentStage?.SlaHours.HasValue == true)
                {
                    var totalSlaMinutes = workflowInstance.CurrentStage.SlaHours.Value * 60;
                    var elapsedMinutes = (now - workflowInstance.StageEnteredAt).TotalMinutes;
                    var percentConsumed = (int)Math.Min(Math.Round(elapsedMinutes / totalSlaMinutes * 100), 999);
                    workflowInstance.SlaPercentConsumed = percentConsumed;

                    // Update SLA status based on new percentage
                    var warningPercent = workflowInstance.CurrentStage.SlaWarningPercent;
                    workflowInstance.SlaStatus = percentConsumed switch
                    {
                        >= 100 => WorkflowSlaStatuses.Breached,
                        >= 90 => WorkflowSlaStatuses.AtRisk,
                        _ when percentConsumed >= warningPercent => WorkflowSlaStatuses.Warning,
                        _ => WorkflowSlaStatuses.OnTrack
                    };
                }
            }

            // Record approval in workflow history
            var historyEntry = new WorkflowHistory
            {
                WorkflowInstanceId = workflowInstance.Id,
                NoticeId = extension.NoticeId,
                EventType = WorkflowHistoryEventTypes.DeadlineExtensionApproved,
                PerformedById = approvedBy,
                Description = $"Deadline extension approved: {extension.DaysExtended} days for {deadline.DeadlineType}",
                Reason = comments,
                EventData = new Dictionary<string, object>
                {
                    ["extensionId"] = extensionId.ToString(),
                    ["deadlineId"] = extension.NoticeDeadlineId.ToString(),
                    ["deadlineType"] = deadline.DeadlineType,
                    ["previousDeadline"] = extension.PreviousDeadline.ToString("O"),
                    ["newDeadline"] = extension.NewDeadline.ToString("O"),
                    ["daysExtended"] = extension.DaysExtended,
                    ["newSlaDeadline"] = workflowInstance.SlaDeadline?.ToString("O") ?? "N/A"
                },
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.WorkflowHistories.Add(historyEntry);
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deadline extension {ExtensionId} approved for notice {NoticeId}",
            extensionId, extension.NoticeId);

        return extension;
    }

    /// <inheritdoc />
    public async Task<DeadlineExtension> RejectExtensionAsync(
        Guid extensionId,
        Guid rejectedBy,
        string reason,
        CancellationToken ct = default)
    {
        var extension = await _context.DeadlineExtensions
            .Include(e => e.NoticeDeadline)
            .Include(e => e.Notice)
            .FirstOrDefaultAsync(e => e.Id == extensionId, ct);

        if (extension == null)
        {
            throw new ArgumentException($"Extension {extensionId} not found");
        }

        if (extension.Status != ExtensionStatuses.Pending)
        {
            throw new InvalidOperationException($"Extension is already {extension.Status}");
        }

        var now = DateTime.UtcNow;

        // Update extension status
        extension.Status = ExtensionStatuses.Rejected;
        extension.ReviewedById = rejectedBy;
        extension.ReviewedAt = now;
        extension.ReviewNotes = reason;
        extension.UpdatedAt = now;

        // Record rejection in workflow history
        var workflowInstance = await _context.NoticeWorkflowInstances
            .FirstOrDefaultAsync(i => i.NoticeId == extension.NoticeId && i.Status == WorkflowInstanceStatuses.Active, ct);

        if (workflowInstance != null)
        {
            var historyEntry = new WorkflowHistory
            {
                WorkflowInstanceId = workflowInstance.Id,
                NoticeId = extension.NoticeId,
                EventType = WorkflowHistoryEventTypes.DeadlineExtensionRejected,
                PerformedById = rejectedBy,
                Description = $"Deadline extension rejected: {extension.DaysExtended} days for {extension.NoticeDeadline.DeadlineType}",
                Reason = reason,
                EventData = new Dictionary<string, object>
                {
                    ["extensionId"] = extensionId.ToString(),
                    ["deadlineId"] = extension.NoticeDeadlineId.ToString(),
                    ["deadlineType"] = extension.NoticeDeadline.DeadlineType,
                    ["requestedDays"] = extension.DaysExtended
                },
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.WorkflowHistories.Add(historyEntry);
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deadline extension {ExtensionId} rejected for notice {NoticeId}: {Reason}",
            extensionId, extension.NoticeId, reason);

        return extension;
    }

    /// <inheritdoc />
    public async Task<List<DeadlineExtension>> GetPendingExtensionsAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        return await _context.DeadlineExtensions
            .Include(e => e.Notice)
            .Include(e => e.NoticeDeadline)
            .Include(e => e.RequestedBy)
            .Where(e =>
                e.Notice.OrganizationId == organizationId &&
                e.Status == ExtensionStatuses.Pending)
            .OrderByDescending(e => e.RequestedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<DeadlineExtension?> GetExtensionByIdAsync(
        Guid extensionId,
        CancellationToken ct = default)
    {
        return await _context.DeadlineExtensions
            .Include(e => e.Notice)
            .Include(e => e.NoticeDeadline)
            .Include(e => e.RequestedBy)
            .Include(e => e.ReviewedBy)
            .FirstOrDefaultAsync(e => e.Id == extensionId, ct);
    }
}

/// <summary>
/// Additional workflow history event types for deadline extensions.
/// </summary>
public static class WorkflowHistoryEventTypesExtension
{
    public const string DeadlineExtensionRequested = "deadline_extension_requested";
    public const string DeadlineExtensionApproved = "deadline_extension_approved";
    public const string DeadlineExtensionRejected = "deadline_extension_rejected";
}
