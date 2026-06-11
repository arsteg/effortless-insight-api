using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Features.Workflows.Dtos;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Features.Workflows.Services;

/// <summary>
/// Core workflow engine service implementation.
/// </summary>
public class WorkflowEngineService : IWorkflowEngineService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WorkflowEngineService> _logger;
    private const int MaxBulkOperationSize = 50;

    public WorkflowEngineService(
        ApplicationDbContext context,
        ILogger<WorkflowEngineService> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Workflow Instance Management

    public async Task<TransitionResult> StartWorkflowAsync(
        StartWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var notice = await _context.Notices
            .FirstOrDefaultAsync(n => n.Id == request.NoticeId, cancellationToken);

        if (notice == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "Notice not found",
                Errors = ["Notice with the specified ID does not exist"]
            };
        }

        // Check if workflow already exists
        var existingInstance = await _context.NoticeWorkflowInstances
            .FirstOrDefaultAsync(i => i.NoticeId == request.NoticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (existingInstance != null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "Workflow already exists",
                Errors = ["An active workflow already exists for this notice"]
            };
        }

        // Get template
        var templateId = request.WorkflowTemplateId ?? WorkflowTemplateSeeder.DefaultTemplateId;
        var template = await _context.WorkflowTemplates
            .Include(t => t.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, cancellationToken);

        if (template == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "Workflow template not found",
                Errors = ["The specified workflow template does not exist or is not active"]
            };
        }

        // Get start stage
        var startStage = template.Stages.FirstOrDefault(s => s.StageType == WorkflowStageTypes.Start);
        if (startStage == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "Invalid workflow template",
                Errors = ["Workflow template does not have a start stage"]
            };
        }

        var now = DateTime.UtcNow;

        // Create workflow instance
        var instance = new NoticeWorkflowInstance
        {
            NoticeId = request.NoticeId,
            WorkflowTemplateId = template.Id,
            CurrentStageKey = startStage.StageKey,
            CurrentStageId = startStage.Id,
            StageEnteredAt = now,
            SlaDeadline = startStage.SlaHours.HasValue ? now.AddHours(startStage.SlaHours.Value) : null,
            SlaStatus = WorkflowSlaStatuses.OnTrack,
            SlaPercentConsumed = 0,
            AssignedToId = request.AssignToUserId,
            AssignedRole = request.AssignToRole,
            Status = WorkflowInstanceStatuses.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.NoticeWorkflowInstances.Add(instance);

        // Create history entry
        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = request.NoticeId,
            EventType = WorkflowHistoryEventTypes.WorkflowStarted,
            ToStageKey = startStage.StageKey,
            PerformedById = userId,
            Description = $"Workflow started with template '{template.Name}'",
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.WorkflowHistories.Add(historyEntry);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Started workflow for notice {NoticeId} with template {TemplateId}",
            request.NoticeId, template.Id);

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = "Workflow started successfully",
            Instance = instanceDto
        };
    }

    public async Task<WorkflowInstanceDto?> GetWorkflowInstanceAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.Stages)
            .Include(i => i.CurrentStage)
            .Include(i => i.AssignedTo)
            .Where(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active)
            .FirstOrDefaultAsync(cancellationToken);

        return instance == null ? null : MapToInstanceDto(instance);
    }

    public async Task<WorkflowInstanceDto?> GetWorkflowInstanceByIdAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.Stages)
            .Include(i => i.CurrentStage)
            .Include(i => i.AssignedTo)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        return instance == null ? null : MapToInstanceDto(instance);
    }

    public async Task<Dictionary<Guid, WorkflowInstanceSummaryDto>> GetWorkflowInstancesForNoticesAsync(
        IEnumerable<Guid> noticeIds,
        CancellationToken cancellationToken = default)
    {
        var instances = await _context.NoticeWorkflowInstances
            .Include(i => i.CurrentStage)
            .Include(i => i.AssignedTo)
            .Where(i => noticeIds.Contains(i.NoticeId) && i.Status == WorkflowInstanceStatuses.Active)
            .ToListAsync(cancellationToken);

        return instances.ToDictionary(
            i => i.NoticeId,
            i => new WorkflowInstanceSummaryDto
            {
                Id = i.Id,
                NoticeId = i.NoticeId,
                CurrentStageKey = i.CurrentStageKey,
                CurrentStageName = i.CurrentStage?.Name ?? i.CurrentStageKey,
                SlaStatus = i.SlaStatus,
                SlaPercentConsumed = i.SlaPercentConsumed,
                SlaDeadline = i.SlaDeadline,
                AssignedToName = i.AssignedTo != null ? i.AssignedTo.Name : null,
                Status = i.Status
            });
    }

    #endregion

    #region Stage Transitions

    public async Task<TransitionResult> TransitionStageAsync(
        Guid noticeId,
        TransitionStageRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.Stages)
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "No active workflow found",
                Errors = ["No active workflow exists for this notice"]
            };
        }

        // Validate transition
        var (isValid, error) = ValidateTransition(instance, request.TargetStageKey);
        if (!isValid)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "Invalid transition",
                Errors = [error!]
            };
        }

        var targetStage = instance.WorkflowTemplate.Stages.First(s => s.StageKey == request.TargetStageKey);
        var previousStageKey = instance.CurrentStageKey;
        var now = DateTime.UtcNow;
        var timeInPreviousStage = (int)(now - instance.StageEnteredAt).TotalMinutes;

        // Update instance
        instance.CurrentStageKey = targetStage.StageKey;
        instance.CurrentStageId = targetStage.Id;
        instance.StageEnteredAt = now;
        instance.SlaDeadline = targetStage.SlaHours.HasValue ? now.AddHours(targetStage.SlaHours.Value) : null;
        instance.SlaStatus = WorkflowSlaStatuses.OnTrack;
        instance.SlaPercentConsumed = 0;
        instance.TransitionCount++;
        instance.TotalTimeMinutes += timeInPreviousStage;
        instance.UpdatedAt = now;

        // Check if this is an end stage
        if (targetStage.StageType == WorkflowStageTypes.End)
        {
            instance.Status = WorkflowInstanceStatuses.Completed;
            instance.CompletedAt = now;
            instance.CompletionOutcome = "completed";
        }

        // Create history entry
        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = WorkflowHistoryEventTypes.StageTransition,
            FromStageKey = previousStageKey,
            ToStageKey = targetStage.StageKey,
            PerformedById = userId,
            Description = $"Transitioned from '{previousStageKey}' to '{targetStage.StageKey}'",
            Reason = request.Reason,
            TimeInStageMinutes = timeInPreviousStage,
            SlaStatusAtEvent = instance.SlaStatus,
            EventData = request.Metadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.WorkflowHistories.Add(historyEntry);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Transitioned workflow for notice {NoticeId} from {FromStage} to {ToStage}",
            noticeId, previousStageKey, targetStage.StageKey);

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = $"Successfully transitioned to {targetStage.Name}",
            Instance = instanceDto
        };
    }

    public async Task<(bool IsValid, string? Error)> ValidateTransitionAsync(
        Guid noticeId,
        string targetStageKey,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.Stages)
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance == null)
        {
            return (false, "No active workflow found for this notice");
        }

        return ValidateTransition(instance, targetStageKey);
    }

    private static (bool IsValid, string? Error) ValidateTransition(
        NoticeWorkflowInstance instance,
        string targetStageKey)
    {
        if (instance.Status != WorkflowInstanceStatuses.Active)
        {
            return (false, "Workflow is not active");
        }

        if (instance.CurrentStage == null)
        {
            return (false, "Current stage not found");
        }

        var allowedTransitions = instance.CurrentStage.AllowedTransitions;
        if (!allowedTransitions.Contains(targetStageKey, StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"Transition from '{instance.CurrentStageKey}' to '{targetStageKey}' is not allowed");
        }

        var targetStage = instance.WorkflowTemplate.Stages.FirstOrDefault(s =>
            s.StageKey.Equals(targetStageKey, StringComparison.OrdinalIgnoreCase));

        if (targetStage == null)
        {
            return (false, $"Target stage '{targetStageKey}' does not exist in the workflow");
        }

        return (true, null);
    }

    public async Task<List<WorkflowStageDto>> GetAvailableTransitionsAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.Stages)
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance?.CurrentStage == null)
        {
            return [];
        }

        var allowedKeys = instance.CurrentStage.AllowedTransitions;
        return instance.WorkflowTemplate.Stages
            .Where(s => allowedKeys.Contains(s.StageKey, StringComparer.OrdinalIgnoreCase))
            .Select(MapToStageDto)
            .ToList();
    }

    public async Task<BulkTransitionResult> BulkTransitionAsync(
        BulkTransitionRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (request.NoticeIds.Count > MaxBulkOperationSize)
        {
            return new BulkTransitionResult
            {
                TotalRequested = request.NoticeIds.Count,
                SuccessCount = 0,
                FailureCount = request.NoticeIds.Count,
                Results = [new BulkItemResult
                {
                    NoticeId = Guid.Empty,
                    Success = false,
                    Error = $"Bulk operation limited to {MaxBulkOperationSize} notices"
                }]
            };
        }

        var results = new List<BulkItemResult>();

        foreach (var noticeId in request.NoticeIds)
        {
            var transitionRequest = new TransitionStageRequest
            {
                TargetStageKey = request.TargetStageKey,
                Reason = request.Reason
            };

            var result = await TransitionStageAsync(noticeId, transitionRequest, userId, cancellationToken);

            results.Add(new BulkItemResult
            {
                NoticeId = noticeId,
                Success = result.Success,
                Message = result.Message,
                Error = result.Errors?.FirstOrDefault()
            });
        }

        return new BulkTransitionResult
        {
            TotalRequested = request.NoticeIds.Count,
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success),
            Results = results
        };
    }

    #endregion

    #region Assignment

    public async Task<TransitionResult> AssignWorkflowAsync(
        Guid noticeId,
        AssignWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.AssignedTo)
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "No active workflow found",
                Errors = ["No active workflow exists for this notice"]
            };
        }

        var now = DateTime.UtcNow;
        var previousAssigneeId = instance.AssignedToId;

        instance.PreviousAssigneeId = previousAssigneeId;
        instance.AssignedToId = request.AssignToUserId;
        instance.AssignedRole = request.AssignToRole;
        instance.UpdatedAt = now;

        // Create history entry
        var eventType = previousAssigneeId.HasValue
            ? WorkflowHistoryEventTypes.Reassignment
            : WorkflowHistoryEventTypes.Assignment;

        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = eventType,
            PerformedById = userId,
            PreviousAssigneeId = previousAssigneeId,
            NewAssigneeId = request.AssignToUserId,
            Description = request.AssignToUserId.HasValue
                ? $"Assigned to user"
                : $"Assigned to role: {request.AssignToRole}",
            Reason = request.Reason,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.WorkflowHistories.Add(historyEntry);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Assigned workflow for notice {NoticeId} to {AssigneeId} / {Role}",
            noticeId, request.AssignToUserId, request.AssignToRole);

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = "Workflow assigned successfully",
            Instance = instanceDto
        };
    }

    public async Task<BulkTransitionResult> BulkAssignAsync(
        BulkAssignRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (request.NoticeIds.Count > MaxBulkOperationSize)
        {
            return new BulkTransitionResult
            {
                TotalRequested = request.NoticeIds.Count,
                SuccessCount = 0,
                FailureCount = request.NoticeIds.Count,
                Results = [new BulkItemResult
                {
                    NoticeId = Guid.Empty,
                    Success = false,
                    Error = $"Bulk operation limited to {MaxBulkOperationSize} notices"
                }]
            };
        }

        var results = new List<BulkItemResult>();

        foreach (var noticeId in request.NoticeIds)
        {
            var assignRequest = new AssignWorkflowRequest
            {
                AssignToUserId = request.AssignToUserId,
                AssignToRole = request.AssignToRole,
                Reason = request.Reason
            };

            var result = await AssignWorkflowAsync(noticeId, assignRequest, userId, cancellationToken);

            results.Add(new BulkItemResult
            {
                NoticeId = noticeId,
                Success = result.Success,
                Message = result.Message,
                Error = result.Errors?.FirstOrDefault()
            });
        }

        return new BulkTransitionResult
        {
            TotalRequested = request.NoticeIds.Count,
            SuccessCount = results.Count(r => r.Success),
            FailureCount = results.Count(r => !r.Success),
            Results = results
        };
    }

    #endregion

    #region Workflow Control

    public async Task<TransitionResult> PauseWorkflowAsync(
        Guid noticeId,
        PauseWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "No active workflow found",
                Errors = ["No active workflow exists for this notice"]
            };
        }

        var now = DateTime.UtcNow;

        instance.Status = WorkflowInstanceStatuses.Paused;
        instance.SlaStatus = WorkflowSlaStatuses.Paused;
        instance.UpdatedAt = now;

        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = WorkflowHistoryEventTypes.WorkflowPaused,
            PerformedById = userId,
            Description = "Workflow paused",
            Reason = request.Reason,
            SlaStatusAtEvent = instance.SlaStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.WorkflowHistories.Add(historyEntry);

        await _context.SaveChangesAsync(cancellationToken);

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = "Workflow paused successfully",
            Instance = instanceDto
        };
    }

    public async Task<TransitionResult> ResumeWorkflowAsync(
        Guid noticeId,
        ResumeWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Paused, cancellationToken);

        if (instance == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "No paused workflow found",
                Errors = ["No paused workflow exists for this notice"]
            };
        }

        var now = DateTime.UtcNow;

        instance.Status = WorkflowInstanceStatuses.Active;
        instance.SlaStatus = WorkflowSlaStatuses.OnTrack;
        // Reset SLA deadline when resuming
        if (instance.CurrentStage?.SlaHours.HasValue == true)
        {
            instance.SlaDeadline = now.AddHours(instance.CurrentStage.SlaHours.Value);
        }
        instance.SlaPercentConsumed = 0;
        instance.UpdatedAt = now;

        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = WorkflowHistoryEventTypes.WorkflowResumed,
            PerformedById = userId,
            Description = "Workflow resumed",
            Reason = request.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.WorkflowHistories.Add(historyEntry);

        await _context.SaveChangesAsync(cancellationToken);

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = "Workflow resumed successfully",
            Instance = instanceDto
        };
    }

    public async Task<TransitionResult> CancelWorkflowAsync(
        Guid noticeId,
        string reason,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId &&
                (i.Status == WorkflowInstanceStatuses.Active || i.Status == WorkflowInstanceStatuses.Paused),
                cancellationToken);

        if (instance == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "No active or paused workflow found",
                Errors = ["No workflow that can be cancelled exists for this notice"]
            };
        }

        var now = DateTime.UtcNow;

        instance.Status = WorkflowInstanceStatuses.Cancelled;
        instance.CompletedAt = now;
        instance.CompletionOutcome = "cancelled";
        instance.UpdatedAt = now;

        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = WorkflowHistoryEventTypes.WorkflowCancelled,
            PerformedById = userId,
            Description = "Workflow cancelled",
            Reason = reason,
            SlaStatusAtEvent = instance.SlaStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.WorkflowHistories.Add(historyEntry);

        await _context.SaveChangesAsync(cancellationToken);

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = "Workflow cancelled successfully",
            Instance = instanceDto
        };
    }

    #endregion

    #region History and Progress

    public async Task<List<WorkflowHistoryDto>> GetWorkflowHistoryAsync(
        Guid noticeId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.WorkflowHistories
            .Include(h => h.PerformedBy)
            .Where(h => h.NoticeId == noticeId)
            .OrderByDescending(h => h.CreatedAt);

        if (limit.HasValue)
        {
            query = (IOrderedQueryable<WorkflowHistory>)query.Take(limit.Value);
        }

        var history = await query.ToListAsync(cancellationToken);

        return history.Select(h => new WorkflowHistoryDto
        {
            Id = h.Id,
            EventType = h.EventType,
            FromStageKey = h.FromStageKey,
            ToStageKey = h.ToStageKey,
            PerformedById = h.PerformedById,
            PerformedByName = h.PerformedBy != null ? h.PerformedBy.Name : null,
            PerformedBySystem = h.PerformedBySystem,
            Description = h.Description,
            Reason = h.Reason,
            TimeInStageMinutes = h.TimeInStageMinutes,
            SlaStatusAtEvent = h.SlaStatusAtEvent,
            CreatedAt = h.CreatedAt
        }).ToList();
    }

    public async Task<WorkflowProgressDto?> GetWorkflowProgressAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.Stages.OrderBy(s => s.StageOrder))
            .Include(i => i.History)
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance == null)
        {
            return null;
        }

        var stages = instance.WorkflowTemplate.Stages.ToList();
        var currentStageIndex = stages.FindIndex(s => s.StageKey == instance.CurrentStageKey);

        // Build stage transition history
        var stageHistory = instance.History
            .Where(h => h.EventType == WorkflowHistoryEventTypes.StageTransition)
            .OrderBy(h => h.CreatedAt)
            .ToList();

        var stageInfoList = stages.Select((stage, index) =>
        {
            var isCompleted = index < currentStageIndex;
            var isCurrent = index == currentStageIndex;

            var enteredAt = stageHistory
                .Where(h => h.ToStageKey == stage.StageKey)
                .Select(h => (DateTime?)h.CreatedAt)
                .FirstOrDefault();

            var exitedAt = stageHistory
                .Where(h => h.FromStageKey == stage.StageKey)
                .Select(h => (DateTime?)h.CreatedAt)
                .FirstOrDefault();

            return new WorkflowStageInfo
            {
                StageKey = stage.StageKey,
                Name = stage.Name,
                StageType = stage.StageType,
                Color = stage.Color,
                Icon = stage.Icon,
                SlaHours = stage.SlaHours,
                IsCurrentStage = isCurrent,
                IsCompleted = isCompleted,
                EnteredAt = enteredAt ?? (isCurrent ? instance.StageEnteredAt : null),
                ExitedAt = exitedAt,
                TimeInStageMinutes = stageHistory
                    .Where(h => h.FromStageKey == stage.StageKey)
                    .Select(h => h.TimeInStageMinutes)
                    .FirstOrDefault()
            };
        }).ToList();

        var completedStages = stageInfoList.Count(s => s.IsCompleted);
        var totalStages = stageInfoList.Count(s => s.StageType != WorkflowStageTypes.End);
        var progressPercent = totalStages > 0 ? (decimal)completedStages / totalStages * 100 : 0;

        return new WorkflowProgressDto
        {
            NoticeId = noticeId,
            WorkflowInstanceId = instance.Id,
            CurrentStageKey = instance.CurrentStageKey,
            Stages = stageInfoList,
            CompletedStages = completedStages,
            TotalStages = totalStages,
            ProgressPercent = Math.Round(progressPercent, 1)
        };
    }

    #endregion

    #region SLA Management

    public async Task<SlaStatusDto?> GetSlaStatusAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .FirstOrDefaultAsync(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance == null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        int? hoursRemaining = null;
        int? minutesRemaining = null;

        if (instance.SlaDeadline.HasValue)
        {
            var remaining = instance.SlaDeadline.Value - now;
            if (remaining.TotalMinutes > 0)
            {
                hoursRemaining = (int)remaining.TotalHours;
                minutesRemaining = (int)remaining.TotalMinutes % 60;
            }
        }

        return new SlaStatusDto
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            CurrentStageKey = instance.CurrentStageKey,
            Status = instance.SlaStatus,
            PercentConsumed = instance.SlaPercentConsumed,
            Deadline = instance.SlaDeadline,
            HoursRemaining = hoursRemaining,
            MinutesRemaining = minutesRemaining,
            IsBreached = instance.SlaStatus == WorkflowSlaStatuses.Breached,
            IsAtRisk = instance.SlaStatus == WorkflowSlaStatuses.AtRisk,
            IsWarning = instance.SlaStatus == WorkflowSlaStatuses.Warning
        };
    }

    public async Task UpdateSlaStatusesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var activeInstances = await _context.NoticeWorkflowInstances
            .Include(i => i.CurrentStage)
            .Where(i => i.Status == WorkflowInstanceStatuses.Active && i.SlaDeadline.HasValue)
            .ToListAsync(cancellationToken);

        foreach (var instance in activeInstances)
        {
            if (!instance.SlaDeadline.HasValue || instance.CurrentStage == null)
                continue;

            var slaHours = instance.CurrentStage.SlaHours ?? 0;
            if (slaHours == 0)
                continue;

            var totalSlaMinutes = slaHours * 60;
            var elapsedMinutes = (now - instance.StageEnteredAt).TotalMinutes;
            var percentConsumed = (int)Math.Min(Math.Round(elapsedMinutes / totalSlaMinutes * 100), 999);

            instance.SlaPercentConsumed = percentConsumed;

            var warningPercent = instance.CurrentStage.SlaWarningPercent;

            instance.SlaStatus = percentConsumed switch
            {
                >= 100 => WorkflowSlaStatuses.Breached,
                >= 90 => WorkflowSlaStatuses.AtRisk,
                _ when percentConsumed >= warningPercent => WorkflowSlaStatuses.Warning,
                _ => WorkflowSlaStatuses.OnTrack
            };

            instance.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated SLA statuses for {Count} active workflow instances", activeInstances.Count);
    }

    public async Task ProcessEscalationsAsync(CancellationToken cancellationToken = default)
    {
        // Get instances that need escalation
        var instancesToEscalate = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.EscalationRules)
            .Where(i => i.Status == WorkflowInstanceStatuses.Active &&
                       (i.SlaStatus == WorkflowSlaStatuses.Warning ||
                        i.SlaStatus == WorkflowSlaStatuses.AtRisk ||
                        i.SlaStatus == WorkflowSlaStatuses.Breached))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var instance in instancesToEscalate)
        {
            var applicableRules = instance.WorkflowTemplate.EscalationRules
                .Where(r => r.TriggerPercent <= instance.SlaPercentConsumed)
                .OrderByDescending(r => r.TriggerPercent)
                .ToList();

            foreach (var rule in applicableRules)
            {
                // Check if this escalation was already triggered
                var alreadyTriggered = await _context.WorkflowHistories
                    .AnyAsync(h => h.WorkflowInstanceId == instance.Id &&
                                   h.EventType == WorkflowHistoryEventTypes.Escalation &&
                                   h.EventData != null &&
                                   EF.Functions.JsonContains(h.EventData, new { ruleId = rule.Id.ToString() }),
                              cancellationToken);

                if (alreadyTriggered)
                    continue;

                // Create escalation history entry
                var historyEntry = new WorkflowHistory
                {
                    WorkflowInstanceId = instance.Id,
                    NoticeId = instance.NoticeId,
                    EventType = WorkflowHistoryEventTypes.Escalation,
                    PerformedBySystem = "SLA_MONITOR",
                    Description = $"Escalation triggered: {rule.Name} ({rule.TriggerPercent}%)",
                    SlaStatusAtEvent = instance.SlaStatus,
                    EventData = new Dictionary<string, object>
                    {
                        ["ruleId"] = rule.Id.ToString(),
                        ["ruleName"] = rule.Name,
                        ["triggerPercent"] = rule.TriggerPercent,
                        ["actualPercent"] = instance.SlaPercentConsumed
                    },
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.WorkflowHistories.Add(historyEntry);

                // TODO: Execute escalation actions (notifications, etc.)
                // This will be implemented in the notification service

                _logger.LogWarning("SLA Escalation triggered for notice {NoticeId}: {RuleName} at {Percent}%",
                    instance.NoticeId, rule.Name, instance.SlaPercentConsumed);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Templates

    public async Task<List<WorkflowTemplateSummaryDto>> GetAvailableTemplatesAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var templates = await _context.WorkflowTemplates
            .Include(t => t.Stages)
            .Where(t => t.IsActive && (t.IsSystem || t.OrganizationId == organizationId))
            .ToListAsync(cancellationToken);

        var templateIds = templates.Select(t => t.Id).ToList();
        var instanceCounts = await _context.NoticeWorkflowInstances
            .Where(i => templateIds.Contains(i.WorkflowTemplateId) && i.Status == WorkflowInstanceStatuses.Active)
            .GroupBy(i => i.WorkflowTemplateId)
            .Select(g => new { TemplateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TemplateId, x => x.Count, cancellationToken);

        return templates.Select(t => new WorkflowTemplateSummaryDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Version = t.Version,
            IsSystem = t.IsSystem,
            IsActive = t.IsActive,
            StageCount = t.Stages.Count,
            ActiveInstanceCount = instanceCounts.GetValueOrDefault(t.Id, 0)
        }).ToList();
    }

    public async Task<WorkflowTemplateDto?> GetTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var template = await _context.WorkflowTemplates
            .Include(t => t.Stages.OrderBy(s => s.StageOrder))
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        return template == null ? null : MapToTemplateDto(template);
    }

    public async Task<WorkflowTemplateDto?> GetDefaultTemplateAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetTemplateAsync(WorkflowTemplateSeeder.DefaultTemplateId, cancellationToken);
    }

    #endregion

    #region Mapping Helpers

    private static WorkflowInstanceDto MapToInstanceDto(NoticeWorkflowInstance instance)
    {
        return new WorkflowInstanceDto
        {
            Id = instance.Id,
            NoticeId = instance.NoticeId,
            WorkflowTemplateId = instance.WorkflowTemplateId,
            WorkflowTemplateName = instance.WorkflowTemplate?.Name ?? string.Empty,
            CurrentStageKey = instance.CurrentStageKey,
            CurrentStageName = instance.CurrentStage?.Name ?? instance.CurrentStageKey,
            StageEnteredAt = instance.StageEnteredAt,
            SlaDeadline = instance.SlaDeadline,
            SlaStatus = instance.SlaStatus,
            SlaPercentConsumed = instance.SlaPercentConsumed,
            AssignedToId = instance.AssignedToId,
            AssignedToName = instance.AssignedTo != null
                ? instance.AssignedTo.Name
                : null,
            AssignedRole = instance.AssignedRole,
            Status = instance.Status,
            CompletedAt = instance.CompletedAt,
            CompletionOutcome = instance.CompletionOutcome,
            TotalTimeMinutes = instance.TotalTimeMinutes,
            SlaBreachCount = instance.SlaBreachCount,
            TransitionCount = instance.TransitionCount,
            AvailableTransitions = instance.CurrentStage?.AllowedTransitions ?? [],
            CreatedAt = instance.CreatedAt
        };
    }

    private static WorkflowTemplateDto MapToTemplateDto(WorkflowTemplate template)
    {
        return new WorkflowTemplateDto
        {
            Id = template.Id,
            OrganizationId = template.OrganizationId,
            Name = template.Name,
            Description = template.Description,
            Version = template.Version,
            IsSystem = template.IsSystem,
            IsActive = template.IsActive,
            ApplicableNoticeTypes = template.ApplicableNoticeTypes,
            Stages = template.Stages.Select(MapToStageDto).ToList(),
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }

    private static WorkflowStageDto MapToStageDto(WorkflowStage stage)
    {
        return new WorkflowStageDto
        {
            Id = stage.Id,
            StageKey = stage.StageKey,
            Name = stage.Name,
            Description = stage.Description,
            StageType = stage.StageType,
            StageOrder = stage.StageOrder,
            SlaHours = stage.SlaHours,
            SlaWarningPercent = stage.SlaWarningPercent,
            Color = stage.Color,
            Icon = stage.Icon,
            AllowedTransitions = stage.AllowedTransitions
        };
    }

    #endregion
}
