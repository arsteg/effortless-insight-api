using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Features.Workflows.Dtos;
using EffortlessInsight.Api.Services.Collaboration;
using EffortlessInsight.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Features.Workflows.Services;

/// <summary>
/// Core workflow engine service implementation.
/// </summary>
public class WorkflowEngineService : IWorkflowEngineService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WorkflowEngineService> _logger;
    private readonly INotificationEngineService _notificationService;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly ITaskService _taskService;
    private readonly IAssignmentEngineService _assignmentEngine;
    private const int MaxBulkOperationSize = 50;
    private const int MaxAutoTransitionDepth = 5; // Prevent infinite loops

    public WorkflowEngineService(
        ApplicationDbContext context,
        ILogger<WorkflowEngineService> logger,
        INotificationEngineService notificationService,
        IConditionEvaluator conditionEvaluator,
        ITaskService taskService,
        IAssignmentEngineService assignmentEngine)
    {
        _context = context;
        _logger = logger;
        _notificationService = notificationService;
        _conditionEvaluator = conditionEvaluator;
        _taskService = taskService;
        _assignmentEngine = assignmentEngine;
    }

    #region Workflow Instance Management

    public async Task<TransitionResult> StartWorkflowAsync(
        StartWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("StartWorkflowAsync: Step 1 - Looking for notice {NoticeId}", request.NoticeId);

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

            _logger.LogInformation("StartWorkflowAsync: Step 2 - Checking for existing workflow");

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

            _logger.LogInformation("StartWorkflowAsync: Step 3 - Getting template");

            // Get template (ignore query filters to allow system templates with null OrganizationId)
            var templateId = request.WorkflowTemplateId ?? WorkflowTemplateSeeder.DefaultTemplateId;
            var template = await _context.WorkflowTemplates
                .IgnoreQueryFilters()
                .Include(t => t.Stages.Where(s => s.DeletedAt == null))
                .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive && t.DeletedAt == null, cancellationToken);

            if (template == null)
            {
                _logger.LogWarning("StartWorkflowAsync: Template {TemplateId} not found", templateId);
                return new TransitionResult
                {
                    Success = false,
                    Message = "Workflow template not found",
                    Errors = ["The specified workflow template does not exist or is not active"]
                };
            }

            _logger.LogInformation("StartWorkflowAsync: Step 4 - Found template {TemplateName} with {StageCount} stages",
                template.Name, template.Stages.Count);

            // Get start stage
            var startStage = template.Stages.OrderBy(s => s.StageOrder).FirstOrDefault(s => s.StageType == WorkflowStageTypes.Start);
            if (startStage == null)
            {
                _logger.LogWarning("StartWorkflowAsync: No start stage found in template");
                return new TransitionResult
                {
                    Success = false,
                    Message = "Invalid workflow template",
                    Errors = ["Workflow template does not have a start stage"]
                };
            }

            _logger.LogInformation("StartWorkflowAsync: Step 5 - Creating workflow instance");

            var now = DateTime.UtcNow;

            // Create workflow instance
            var instance = new NoticeWorkflowInstance
            {
                NoticeId = request.NoticeId,
                WorkflowTemplateId = template.Id,
                TemplateVersionUsed = template.Version,
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

            _logger.LogInformation("StartWorkflowAsync: Step 6 - Creating history entry");

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

            _logger.LogInformation("StartWorkflowAsync: Step 7 - Saving to database");

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("StartWorkflowAsync: Step 8 - Getting instance DTO");

            var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

            _logger.LogInformation("StartWorkflowAsync: Success - Workflow started for notice {NoticeId}", request.NoticeId);

            return new TransitionResult
            {
                Success = true,
                Message = "Workflow started successfully",
                Instance = instanceDto
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartWorkflowAsync: Exception occurred for notice {NoticeId}: {Message}",
                request.NoticeId, ex.Message);
            throw;
        }
    }

    public async Task<WorkflowInstanceDto?> GetWorkflowInstanceAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        // First get the instance
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.CurrentStage)
            .Include(i => i.AssignedTo)
            .Where(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (instance == null)
            return null;

        // Load template separately with IgnoreQueryFilters to support system templates
        var template = await _context.WorkflowTemplates
            .IgnoreQueryFilters()
            .Include(t => t.Stages.Where(s => s.DeletedAt == null))
            .FirstOrDefaultAsync(t => t.Id == instance.WorkflowTemplateId && t.DeletedAt == null, cancellationToken);

        instance.WorkflowTemplate = template!;

        return MapToInstanceDto(instance);
    }

    public async Task<WorkflowInstanceDto?> GetWorkflowInstanceByIdAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        // First get the instance
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.CurrentStage)
            .Include(i => i.AssignedTo)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance == null)
            return null;

        // Load template separately with IgnoreQueryFilters to support system templates
        var template = await _context.WorkflowTemplates
            .IgnoreQueryFilters()
            .Include(t => t.Stages.Where(s => s.DeletedAt == null))
            .FirstOrDefaultAsync(t => t.Id == instance.WorkflowTemplateId && t.DeletedAt == null, cancellationToken);

        instance.WorkflowTemplate = template!;

        return MapToInstanceDto(instance);
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
            .Include(i => i.Notice)
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

        // GAP-WF-005: Validate stage permissions before allowing transition
        if (instance.CurrentStage != null)
        {
            var permissionResult = await ValidateStagePermissionsAsync(
                instance.CurrentStage, userId, instance.Notice.OrganizationId, cancellationToken);
            if (!permissionResult.IsValid)
            {
                return new TransitionResult
                {
                    Success = false,
                    Message = "Insufficient permissions",
                    Errors = [permissionResult.Error!]
                };
            }
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

        // GAP-WF-008: Auto-create task when entering a task stage or when AutoCreateTask is enabled
        await TryAutoCreateTaskForStageAsync(instance, targetStage, userId, cancellationToken);

        // GAP-WF-004: Auto-assign based on assignment rules if no manual assignment
        if (!instance.AssignedToId.HasValue && targetStage.StageType != WorkflowStageTypes.End)
        {
            var autoAssigneeId = await _assignmentEngine.DetermineAssigneeAsync(
                instance, targetStage, cancellationToken);

            if (autoAssigneeId.HasValue)
            {
                instance.AssignedToId = autoAssigneeId;
                instance.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Auto-assigned workflow for notice {NoticeId} to user {UserId} at stage {StageKey}",
                    noticeId, autoAssigneeId.Value, targetStage.StageKey);
            }
        }

        // Evaluate auto-transitions for the new stage
        var autoTransitionResult = await EvaluateAutoTransitionsAsync(
            instance, targetStage, userId, 0, cancellationToken);

        if (autoTransitionResult != null)
        {
            // Auto-transition occurred, return that result
            return autoTransitionResult;
        }

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = $"Successfully transitioned to {targetStage.Name}",
            Instance = instanceDto
        };
    }

    /// <summary>
    /// Evaluates and executes auto-transitions based on stage rules and notice conditions.
    /// </summary>
    private async Task<TransitionResult?> EvaluateAutoTransitionsAsync(
        NoticeWorkflowInstance instance,
        WorkflowStage currentStage,
        Guid userId,
        int depth,
        CancellationToken cancellationToken)
    {
        // Prevent infinite loops
        if (depth >= MaxAutoTransitionDepth)
        {
            _logger.LogWarning(
                "Max auto-transition depth reached for notice {NoticeId} at stage {StageKey}",
                instance.NoticeId, currentStage.StageKey);
            return null;
        }

        // Check if stage has auto-transition rules
        if (currentStage.AutoTransitionRules == null || currentStage.AutoTransitionRules.Count == 0)
            return null;

        // Load the notice to evaluate conditions
        var notice = await _context.Notices
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == instance.NoticeId, cancellationToken);

        if (notice == null)
            return null;

        // Evaluate each auto-transition rule in order
        foreach (var rule in currentStage.AutoTransitionRules)
        {
            var condition = rule.Condition;

            // If condition is empty, it always matches
            var conditionMatches = string.IsNullOrWhiteSpace(condition.Field) ||
                                   _conditionEvaluator.Evaluate(condition, notice);

            if (conditionMatches)
            {
                // Handle delayed transitions
                if (rule.DelayMinutes > 0)
                {
                    _logger.LogInformation(
                        "Auto-transition delayed by {DelayMinutes} minutes for notice {NoticeId} to stage {TargetStage}",
                        rule.DelayMinutes, instance.NoticeId, rule.TargetStage);

                    // Schedule delayed transition via Hangfire
                    var transitionTime = TimeSpan.FromMinutes(rule.DelayMinutes);
                    Hangfire.BackgroundJob.Schedule<IWorkflowEngineService>(
                        service => service.ExecuteDelayedTransitionAsync(
                            instance.NoticeId,
                            rule.TargetStage,
                            instance.Id,
                            Guid.Empty, // System-triggered
                            default),
                        transitionTime);

                    _logger.LogInformation(
                        "Scheduled delayed transition for notice {NoticeId} to stage {TargetStage} in {DelayMinutes} minutes",
                        instance.NoticeId, rule.TargetStage, rule.DelayMinutes);

                    return null;
                }

                // Find target stage
                var targetStage = instance.WorkflowTemplate.Stages
                    .FirstOrDefault(s => s.StageKey == rule.TargetStage);

                if (targetStage == null)
                {
                    _logger.LogWarning(
                        "Auto-transition target stage {TargetStage} not found for notice {NoticeId}",
                        rule.TargetStage, instance.NoticeId);
                    continue;
                }

                // Perform the auto-transition
                var now = DateTime.UtcNow;
                var previousStageKey = instance.CurrentStageKey;
                var timeInPreviousStage = (int)(now - instance.StageEnteredAt).TotalMinutes;

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
                    instance.CompletionOutcome = "auto_completed";
                }

                // Create history entry for auto-transition
                var historyEntry = new WorkflowHistory
                {
                    WorkflowInstanceId = instance.Id,
                    NoticeId = instance.NoticeId,
                    EventType = WorkflowHistoryEventTypes.StageTransition,
                    FromStageKey = previousStageKey,
                    ToStageKey = targetStage.StageKey,
                    PerformedById = null, // System-initiated
                    PerformedBySystem = "AutoTransition",
                    Description = $"Auto-transitioned from '{previousStageKey}' to '{targetStage.StageKey}' based on condition: {condition.Field} {condition.Operator} {condition.Value}",
                    TimeInStageMinutes = timeInPreviousStage,
                    SlaStatusAtEvent = instance.SlaStatus,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.WorkflowHistories.Add(historyEntry);

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Auto-transitioned workflow for notice {NoticeId} from {FromStage} to {ToStage}",
                    instance.NoticeId, previousStageKey, targetStage.StageKey);

                // Recursively evaluate auto-transitions for the new stage
                var recursiveResult = await EvaluateAutoTransitionsAsync(
                    instance, targetStage, userId, depth + 1, cancellationToken);

                if (recursiveResult != null)
                    return recursiveResult;

                var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);
                return new TransitionResult
                {
                    Success = true,
                    Message = $"Auto-transitioned to {targetStage.Name}",
                    Instance = instanceDto
                };
            }
        }

        return null;
    }

    /// <summary>
    /// GAP-WF-008: Auto-creates a task when entering a task stage or when AutoCreateTask is enabled.
    /// </summary>
    private async Task TryAutoCreateTaskForStageAsync(
        NoticeWorkflowInstance instance,
        WorkflowStage stage,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Check if this stage should auto-create a task
        if (stage.StageType != WorkflowStageTypes.Task && !stage.AutoCreateTask)
        {
            return;
        }

        try
        {
            // Build task details from template or stage metadata
            string taskTitle;
            string? taskDescription = null;
            string taskPriority = TaskPriorityValues.Medium;
            decimal? estimatedHours = null;
            DateTime? dueDate = null;

            // If a task template is specified, load it
            if (stage.TaskTemplateId.HasValue)
            {
                var template = await _context.TaskTemplates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == stage.TaskTemplateId.Value && t.IsActive, cancellationToken);

                if (template != null)
                {
                    taskTitle = template.DefaultTitle;
                    taskDescription = template.DefaultDescription;
                    taskPriority = template.DefaultPriority;
                    estimatedHours = template.DefaultEstimatedHours;
                }
                else
                {
                    _logger.LogWarning(
                        "Task template {TemplateId} not found or inactive for stage {StageKey}",
                        stage.TaskTemplateId, stage.StageKey);
                    taskTitle = stage.Name;
                }
            }
            else
            {
                // Use stage metadata or stage name for task details
                taskTitle = stage.Metadata?.TryGetValue("taskTitle", out var titleObj) == true
                    ? titleObj?.ToString() ?? stage.Name
                    : stage.Name;

                if (stage.Metadata?.TryGetValue("taskDescription", out var descObj) == true)
                {
                    taskDescription = descObj?.ToString();
                }

                if (stage.Metadata?.TryGetValue("taskPriority", out var priorityObj) == true)
                {
                    var priorityStr = priorityObj?.ToString();
                    if (!string.IsNullOrEmpty(priorityStr) && TaskPriorityValues.IsValid(priorityStr))
                    {
                        taskPriority = priorityStr;
                    }
                }
            }

            // Set due date based on SLA if available
            if (stage.SlaHours.HasValue)
            {
                dueDate = DateTime.UtcNow.AddHours(stage.SlaHours.Value);
            }

            // Determine assignees - use workflow assignee if available
            var assignees = new List<Guid>();
            if (instance.AssignedToId.HasValue)
            {
                assignees.Add(instance.AssignedToId.Value);
            }
            else
            {
                // Fall back to the user performing the transition
                assignees.Add(userId);
            }

            // Create the task
            var createTaskDto = new CreateTaskDto(
                Title: taskTitle,
                Description: taskDescription,
                Assignees: assignees,
                AssignedTeamId: null,
                Priority: taskPriority,
                DueDate: dueDate,
                EstimatedHours: estimatedHours,
                Labels: ["workflow-task", $"stage:{stage.StageKey}"],
                ParentTaskId: null,
                TemplateId: stage.TaskTemplateId
            );

            var createdTask = await _taskService.CreateTaskAsync(
                instance.NoticeId,
                createTaskDto,
                userId
            );

            _logger.LogInformation(
                "Auto-created task {TaskId} for notice {NoticeId} on entering stage {StageKey}",
                createdTask.Id, instance.NoticeId, stage.StageKey);
        }
        catch (Exception ex)
        {
            // Log error but don't fail the transition
            _logger.LogError(ex,
                "Failed to auto-create task for notice {NoticeId} on stage {StageKey}",
                instance.NoticeId, stage.StageKey);
        }
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

    /// <summary>
    /// GAP-WF-005: Validates that the user has the required permissions to transition from the current stage.
    /// Checks stage.Metadata for "minRole" or "allowedRoles" permission requirements.
    /// </summary>
    /// <param name="stage">The workflow stage to validate permissions for.</param>
    /// <param name="userId">The user attempting the transition.</param>
    /// <param name="organizationId">The organization context for role lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple indicating if permissions are valid and any error message.</returns>
    private async Task<(bool IsValid, string? Error)> ValidateStagePermissionsAsync(
        WorkflowStage stage,
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // If no metadata or no permission requirements, allow the transition
        if (stage.Metadata == null || stage.Metadata.Count == 0)
        {
            return (true, null);
        }

        // Check if stage has permission requirements
        var hasMinRole = stage.Metadata.TryGetValue("minRole", out var minRoleObj);
        var hasAllowedRoles = stage.Metadata.TryGetValue("allowedRoles", out var allowedRolesObj);

        if (!hasMinRole && !hasAllowedRoles)
        {
            return (true, null);
        }

        // Load the user's role from the organization membership
        var membership = await _context.OrganizationMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.UserId == userId &&
                m.OrganizationId == organizationId &&
                m.Status == "active",
                cancellationToken);

        if (membership == null)
        {
            return (false, "User is not a member of this organization");
        }

        var userRole = membership.Role.ToLowerInvariant();

        // Define role hierarchy (higher index = more permissions)
        var roleHierarchy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "viewer", 0 },
            { "member", 1 },
            { "ca", 2 },
            { "manager", 3 },
            { "admin", 4 },
            { "owner", 5 }
        };

        // Check allowedRoles first (more specific)
        if (hasAllowedRoles && allowedRolesObj != null)
        {
            var allowedRoles = ParseAllowedRoles(allowedRolesObj);
            if (allowedRoles.Count > 0)
            {
                if (!allowedRoles.Contains(userRole, StringComparer.OrdinalIgnoreCase))
                {
                    return (false, $"User role '{userRole}' is not in the allowed roles for this stage: {string.Join(", ", allowedRoles)}");
                }
                return (true, null);
            }
        }

        // Check minRole (minimum required role level)
        if (hasMinRole && minRoleObj != null)
        {
            var minRole = minRoleObj.ToString()?.ToLowerInvariant() ?? "";

            if (!roleHierarchy.TryGetValue(minRole, out var requiredLevel))
            {
                _logger.LogWarning("Unknown minRole '{MinRole}' in stage metadata", minRole);
                return (true, null); // Unknown role - allow by default
            }

            if (!roleHierarchy.TryGetValue(userRole, out var userLevel))
            {
                _logger.LogWarning("Unknown user role '{UserRole}'", userRole);
                return (false, $"Unknown user role: {userRole}");
            }

            if (userLevel < requiredLevel)
            {
                return (false, $"Minimum role required: '{minRole}'. User has role: '{userRole}'");
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Parses allowedRoles from metadata value which can be a string array, JsonElement, or comma-separated string.
    /// </summary>
    private static List<string> ParseAllowedRoles(object allowedRolesObj)
    {
        var roles = new List<string>();

        if (allowedRolesObj is List<string> roleList)
        {
            roles.AddRange(roleList);
        }
        else if (allowedRolesObj is string[] roleArray)
        {
            roles.AddRange(roleArray);
        }
        else if (allowedRolesObj is System.Text.Json.JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in jsonElement.EnumerateArray())
                {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        roles.Add(item.GetString() ?? "");
                    }
                }
            }
            else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var rolesStr = jsonElement.GetString() ?? "";
                roles.AddRange(rolesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }
        else if (allowedRolesObj is string rolesString)
        {
            roles.AddRange(rolesString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return roles;
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

        if (instance == null)
        {
            _logger.LogWarning("GetAvailableTransitions: No active workflow instance found for NoticeId={NoticeId}", noticeId);
            return [];
        }

        if (instance.CurrentStage == null)
        {
            _logger.LogWarning("GetAvailableTransitions: CurrentStage is null for NoticeId={NoticeId}, CurrentStageId={CurrentStageId}",
                noticeId, instance.CurrentStageId);
            return [];
        }

        var allowedKeys = instance.CurrentStage.AllowedTransitions;
        _logger.LogInformation("GetAvailableTransitions: NoticeId={NoticeId}, CurrentStage={StageKey}, AllowedTransitions={AllowedTransitions}, TemplateStagesCount={StagesCount}",
            noticeId, instance.CurrentStageKey, string.Join(",", allowedKeys), instance.WorkflowTemplate.Stages.Count);

        var result = instance.WorkflowTemplate.Stages
            .Where(s => allowedKeys.Contains(s.StageKey, StringComparer.OrdinalIgnoreCase))
            .Select(MapToStageDto)
            .ToList();

        _logger.LogInformation("GetAvailableTransitions: Returning {Count} transitions for NoticeId={NoticeId}", result.Count, noticeId);
        return result;
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

    #region Delayed Transitions

    /// <summary>
    /// Executes a delayed workflow transition scheduled by Hangfire.
    /// </summary>
    public async Task<TransitionResult> ExecuteDelayedTransitionAsync(
        Guid noticeId,
        string targetStageKey,
        Guid workflowInstanceId,
        Guid triggeredByUserId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing delayed transition for notice {NoticeId} to stage {TargetStage}",
            noticeId, targetStageKey);

        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
            .ThenInclude(t => t.Stages)
            .Include(i => i.CurrentStage)
            .Include(i => i.Notice)
            .FirstOrDefaultAsync(i => i.Id == workflowInstanceId && i.Status == WorkflowInstanceStatuses.Active, cancellationToken);

        if (instance == null)
        {
            _logger.LogWarning(
                "Delayed transition skipped - workflow instance {InstanceId} no longer active for notice {NoticeId}",
                workflowInstanceId, noticeId);
            return new TransitionResult
            {
                Success = false,
                Message = "Workflow instance no longer active",
                Errors = ["The workflow instance may have been completed, paused, or cancelled"]
            };
        }

        // Find target stage
        var targetStage = instance.WorkflowTemplate.Stages.FirstOrDefault(s => s.StageKey == targetStageKey);
        if (targetStage == null)
        {
            _logger.LogWarning(
                "Delayed transition target stage {TargetStage} not found for notice {NoticeId}",
                targetStageKey, noticeId);
            return new TransitionResult
            {
                Success = false,
                Message = "Target stage not found",
                Errors = [$"Stage '{targetStageKey}' does not exist in the workflow template"]
            };
        }

        // Perform the transition
        var previousStageKey = instance.CurrentStageKey;
        var now = DateTime.UtcNow;
        var timeInPreviousStage = (int)(now - instance.StageEnteredAt).TotalMinutes;

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
            instance.CompletionOutcome = "delayed_auto_completed";
        }

        // Create history entry
        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = WorkflowHistoryEventTypes.StageTransition,
            FromStageKey = previousStageKey,
            ToStageKey = targetStage.StageKey,
            PerformedBySystem = "DelayedTransitionScheduler",
            PerformedById = triggeredByUserId != Guid.Empty ? triggeredByUserId : null,
            Description = $"Delayed auto-transition from '{previousStageKey}' to '{targetStage.StageKey}'",
            Reason = "Scheduled delayed transition",
            TimeInStageMinutes = timeInPreviousStage,
            SlaStatusAtEvent = instance.SlaStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.WorkflowHistories.Add(historyEntry);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Delayed transition completed for notice {NoticeId} from {FromStage} to {ToStage}",
            noticeId, previousStageKey, targetStage.StageKey);

        // Process any actions for the new stage (entry actions, auto-assign, etc.)
        await TryAutoCreateTaskForStageAsync(instance, targetStage, triggeredByUserId, cancellationToken);

        return new TransitionResult
        {
            Success = true,
            Message = $"Delayed transition completed to stage '{targetStage.Name}'"
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

                // Execute escalation actions
                await ExecuteEscalationActionsAsync(instance, rule, cancellationToken);

                _logger.LogWarning("SLA Escalation triggered for notice {NoticeId}: {RuleName} at {Percent}%",
                    instance.NoticeId, rule.Name, instance.SlaPercentConsumed);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteEscalationActionsAsync(
        NoticeWorkflowInstance instance,
        WorkflowEscalationRule rule,
        CancellationToken cancellationToken)
    {
        foreach (var action in rule.Actions)
        {
            try
            {
                switch (action.Type.ToLowerInvariant())
                {
                    case "notify":
                        await ExecuteNotifyActionAsync(instance, rule, action, cancellationToken);
                        break;

                    case "flag":
                        await ExecuteFlagActionAsync(instance, action, cancellationToken);
                        break;

                    case "reassign":
                        await ExecuteReassignActionAsync(instance, action, cancellationToken);
                        break;

                    default:
                        _logger.LogWarning(
                            "Unknown escalation action type '{Type}' for rule {RuleName}",
                            action.Type, rule.Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to execute escalation action {Type} for notice {NoticeId}",
                    action.Type, instance.NoticeId);
            }
        }
    }

    private async Task ExecuteNotifyActionAsync(
        NoticeWorkflowInstance instance,
        WorkflowEscalationRule rule,
        EscalationAction action,
        CancellationToken cancellationToken)
    {
        var targetUserIds = new List<Guid>();

        // Determine who to notify based on target
        switch (action.Target?.ToLowerInvariant())
        {
            case "assignee":
                if (instance.AssignedToId.HasValue)
                {
                    targetUserIds.Add(instance.AssignedToId.Value);
                }
                break;

            case "manager":
                // Get organization managers for this notice
                var notice = await _context.Notices
                    .FirstOrDefaultAsync(n => n.Id == instance.NoticeId, cancellationToken);
                if (notice != null)
                {
                    var managers = await _context.OrganizationMembers
                        .Where(m => m.OrganizationId == notice.OrganizationId &&
                                   (m.Role == "admin" || m.Role == "manager"))
                        .Select(m => m.UserId)
                        .ToListAsync(cancellationToken);
                    targetUserIds.AddRange(managers);
                }
                break;

            case "admin":
                // Get organization admins
                var noticeForAdmin = await _context.Notices
                    .FirstOrDefaultAsync(n => n.Id == instance.NoticeId, cancellationToken);
                if (noticeForAdmin != null)
                {
                    var admins = await _context.OrganizationMembers
                        .Where(m => m.OrganizationId == noticeForAdmin.OrganizationId &&
                                   (m.Role == "admin" || m.Role == "owner"))
                        .Select(m => m.UserId)
                        .ToListAsync(cancellationToken);
                    targetUserIds.AddRange(admins);
                }
                break;

            default:
                _logger.LogWarning("Unknown notification target '{Target}'", action.Target);
                return;
        }

        // Send notifications to all targets
        foreach (var userId in targetUserIds.Distinct())
        {
            var notificationData = new Dictionary<string, object>
            {
                ["noticeId"] = instance.NoticeId.ToString(),
                ["ruleName"] = rule.Name,
                ["triggerPercent"] = rule.TriggerPercent,
                ["slaPercent"] = instance.SlaPercentConsumed,
                ["slaStatus"] = instance.SlaStatus,
                ["currentStage"] = instance.CurrentStageKey
            };

            await _notificationService.SendAsync(
                new SendNotificationRequest(
                    userId,
                    action.Template ?? "workflow_escalation",
                    notificationData
                ),
                cancellationToken
            );

            _logger.LogInformation(
                "Sent escalation notification to user {UserId} for notice {NoticeId}",
                userId, instance.NoticeId);
        }
    }

    private async Task ExecuteFlagActionAsync(
        NoticeWorkflowInstance instance,
        EscalationAction action,
        CancellationToken cancellationToken)
    {
        // Update notice with escalation flag
        var notice = await _context.Notices
            .FirstOrDefaultAsync(n => n.Id == instance.NoticeId, cancellationToken);

        if (notice != null)
        {
            // Add escalation flag to notice metadata or tags
            var flagValue = action.Value ?? "escalated";
            if (!notice.Tags.Contains(flagValue))
            {
                notice.Tags.Add(flagValue);
                notice.UpdatedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "Added flag '{Flag}' to notice {NoticeId}",
                    flagValue, instance.NoticeId);
            }
        }
    }

    private async Task ExecuteReassignActionAsync(
        NoticeWorkflowInstance instance,
        EscalationAction action,
        CancellationToken cancellationToken)
    {
        // Reassign to manager if current assignee hasn't resolved
        if (action.Target?.ToLowerInvariant() == "manager")
        {
            var notice = await _context.Notices
                .FirstOrDefaultAsync(n => n.Id == instance.NoticeId, cancellationToken);

            if (notice != null)
            {
                // Find a manager in the organization
                var manager = await _context.OrganizationMembers
                    .Where(m => m.OrganizationId == notice.OrganizationId &&
                               m.Role == "manager" &&
                               m.UserId != instance.AssignedToId)
                    .Select(m => m.UserId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (manager != default)
                {
                    var previousAssignee = instance.AssignedToId;
                    instance.AssignedToId = manager;
                    instance.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Reassigned notice {NoticeId} from {PreviousAssignee} to manager {NewAssignee} due to escalation",
                        instance.NoticeId, previousAssignee, manager);

                    // Notify the new assignee
                    await _notificationService.SendAsync(
                        new SendNotificationRequest(
                            manager,
                            "workflow_reassigned",
                            new Dictionary<string, object>
                            {
                                ["noticeId"] = instance.NoticeId.ToString(),
                                ["reason"] = "SLA escalation"
                            }
                        ),
                        cancellationToken
                    );
                }
            }
        }
    }

    #endregion

    #region Parallel Execution

    public async Task<List<StageInstanceDto>> GetActiveStageInstancesAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.StageInstances.Where(si => si.Status == StageInstanceStatuses.Active))
                .ThenInclude(si => si.Stage)
            .Include(i => i.StageInstances)
                .ThenInclude(si => si.AssignedTo)
            .Where(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (instance == null)
            return [];

        return instance.StageInstances
            .Where(si => si.Status == StageInstanceStatuses.Active)
            .Select(si => MapToStageInstanceDto(si))
            .ToList();
    }

    public async Task<TransitionResult> CompleteStageInstanceAsync(
        Guid noticeId,
        Guid stageInstanceId,
        CompleteStageInstanceRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Stages)
            .Include(i => i.StageInstances)
                .ThenInclude(si => si.Stage)
            .Where(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (instance == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "No active workflow found",
                Errors = ["No active workflow exists for this notice"]
            };
        }

        var stageInstance = instance.StageInstances.FirstOrDefault(si => si.Id == stageInstanceId);
        if (stageInstance == null || stageInstance.Status != StageInstanceStatuses.Active)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "Stage instance not found or not active",
                Errors = ["The specified stage instance does not exist or is not active"]
            };
        }

        var now = DateTime.UtcNow;
        var timeInStage = (int)(now - stageInstance.EnteredAt).TotalMinutes;

        // Complete the stage instance
        stageInstance.Status = StageInstanceStatuses.Completed;
        stageInstance.CompletedAt = now;
        stageInstance.Outcome = request.Outcome ?? "completed";
        stageInstance.TimeSpentMinutes = timeInStage;
        stageInstance.UpdatedAt = now;

        // Create history entry
        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = WorkflowHistoryEventTypes.StageTransition,
            FromStageKey = stageInstance.StageKey,
            ToStageKey = request.TargetStageKey,
            PerformedById = userId,
            Description = $"Completed parallel stage '{stageInstance.StageKey}' with outcome '{stageInstance.Outcome}'",
            Reason = request.Reason,
            TimeInStageMinutes = timeInStage,
            EventData = request.Metadata,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.WorkflowHistories.Add(historyEntry);

        // Check if we need to handle join logic
        var result = await HandleParallelJoinAsync(instance, stageInstance, request, userId, now, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Completed stage instance {StageInstanceId} for notice {NoticeId} with outcome {Outcome}",
            stageInstanceId, noticeId, stageInstance.Outcome);

        return result;
    }

    public async Task<TransitionResult> ForkWorkflowAsync(
        Guid noticeId,
        ForkWorkflowRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (request.TargetStageKeys.Count < 2)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "Invalid fork request",
                Errors = ["Fork requires at least 2 target stages"]
            };
        }

        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Stages)
            .Include(i => i.StageInstances)
            .Where(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (instance == null)
        {
            return new TransitionResult
            {
                Success = false,
                Message = "No active workflow found",
                Errors = ["No active workflow exists for this notice"]
            };
        }

        // Validate all target stages exist
        var allStages = instance.WorkflowTemplate.Stages.ToDictionary(s => s.StageKey);
        foreach (var targetKey in request.TargetStageKeys)
        {
            if (!allStages.ContainsKey(targetKey))
            {
                return new TransitionResult
                {
                    Success = false,
                    Message = "Invalid target stage",
                    Errors = [$"Stage '{targetKey}' does not exist in the workflow template"]
                };
            }
        }

        var now = DateTime.UtcNow;
        var branchIndex = 0;

        // Create stage instances for each parallel branch
        foreach (var targetKey in request.TargetStageKeys)
        {
            var targetStage = allStages[targetKey];
            var branchId = $"branch_{Guid.NewGuid():N}";
            branchIndex++;

            var stageInstance = new WorkflowStageInstance
            {
                WorkflowInstanceId = instance.Id,
                StageId = targetStage.Id,
                StageKey = targetStage.StageKey,
                BranchId = branchId,
                Status = StageInstanceStatuses.Active,
                EnteredAt = now,
                SlaDeadline = targetStage.SlaHours.HasValue ? now.AddHours(targetStage.SlaHours.Value) : null,
                SlaStatus = WorkflowSlaStatuses.OnTrack,
                SlaPercentConsumed = 0,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Apply branch-specific assignment if provided
            if (request.BranchAssignments?.TryGetValue(targetKey, out var assignment) == true)
            {
                stageInstance.AssignedToId = assignment.AssignToUserId;
                stageInstance.AssignedRole = assignment.AssignToRole;
            }

            _context.WorkflowStageInstances.Add(stageInstance);
        }

        // Update instance to reflect parallel state
        instance.HasParallelStages = true;
        instance.ActiveBranchCount = request.TargetStageKeys.Count;
        instance.UpdatedAt = now;

        // Create history entry
        var historyEntry = new WorkflowHistory
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            EventType = "workflow_forked",
            FromStageKey = instance.CurrentStageKey,
            PerformedById = userId,
            Description = $"Workflow forked into {request.TargetStageKeys.Count} parallel branches: {string.Join(", ", request.TargetStageKeys)}",
            Reason = request.Reason,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.WorkflowHistories.Add(historyEntry);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Forked workflow for notice {NoticeId} into {BranchCount} parallel branches",
            noticeId, request.TargetStageKeys.Count);

        var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);
        return new TransitionResult
        {
            Success = true,
            Message = $"Workflow forked into {request.TargetStageKeys.Count} parallel branches",
            Instance = instanceDto
        };
    }

    public async Task<ParallelBranchStatusDto?> GetParallelBranchStatusAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var instance = await _context.NoticeWorkflowInstances
            .Include(i => i.WorkflowTemplate)
                .ThenInclude(t => t.Stages)
            .Include(i => i.StageInstances)
                .ThenInclude(si => si.Stage)
            .Include(i => i.StageInstances)
                .ThenInclude(si => si.AssignedTo)
            .Where(i => i.NoticeId == noticeId && i.Status == WorkflowInstanceStatuses.Active)
            .FirstOrDefaultAsync(cancellationToken);

        if (instance == null || !instance.HasParallelStages)
            return null;

        var activeInstances = instance.StageInstances
            .Where(si => si.Status == StageInstanceStatuses.Active)
            .ToList();

        var completedInstances = instance.StageInstances
            .Where(si => si.Status == StageInstanceStatuses.Completed && si.BranchId != null)
            .ToList();

        var allBranchIds = instance.StageInstances
            .Where(si => si.BranchId != null)
            .Select(si => si.BranchId!)
            .Distinct()
            .ToList();

        var branches = activeInstances
            .Where(si => si.BranchId != null)
            .Select(si => new BranchStatusDto
            {
                BranchId = si.BranchId!,
                CurrentStageKey = si.StageKey,
                CurrentStageName = si.Stage?.Name ?? si.StageKey,
                Status = si.Status,
                AssignedToId = si.AssignedToId,
                AssignedToName = si.AssignedTo?.Name,
                SlaDeadline = si.SlaDeadline,
                SlaStatus = si.SlaStatus
            })
            .ToList();

        // Find next synchronization point
        SynchronizationPointDto? syncPoint = null;
        var syncStage = instance.WorkflowTemplate.Stages
            .Where(s => s.IsSynchronizationPoint)
            .OrderBy(s => s.StageOrder)
            .FirstOrDefault();

        if (syncStage != null)
        {
            var requiredBranches = syncStage.JoinType == WorkflowJoinTypes.All
                ? allBranchIds.Count
                : syncStage.MinBranchesToComplete ?? 1;

            syncPoint = new SynchronizationPointDto
            {
                StageKey = syncStage.StageKey,
                StageName = syncStage.Name,
                JoinType = syncStage.JoinType ?? WorkflowJoinTypes.All,
                MinBranchesToComplete = syncStage.MinBranchesToComplete,
                CompletedBranches = completedInstances.Count,
                RequiredBranches = requiredBranches,
                IsReady = completedInstances.Count >= requiredBranches
            };
        }

        return new ParallelBranchStatusDto
        {
            WorkflowInstanceId = instance.Id,
            NoticeId = noticeId,
            HasParallelStages = true,
            ActiveBranchCount = activeInstances.Count,
            CompletedBranchCount = completedInstances.Count,
            TotalBranchCount = allBranchIds.Count,
            Branches = branches,
            NextSyncPoint = syncPoint
        };
    }

    private async Task<TransitionResult> HandleParallelJoinAsync(
        NoticeWorkflowInstance instance,
        WorkflowStageInstance completedStageInstance,
        CompleteStageInstanceRequest request,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Check if there are other active branches
        var activeBranches = instance.StageInstances
            .Where(si => si.Status == StageInstanceStatuses.Active && si.Id != completedStageInstance.Id)
            .ToList();

        // Find the sync point stage
        var syncStage = instance.WorkflowTemplate.Stages
            .FirstOrDefault(s => s.IsSynchronizationPoint);

        if (syncStage == null)
        {
            // No sync point - if all branches complete, workflow completes
            if (activeBranches.Count == 0)
            {
                instance.HasParallelStages = false;
                instance.ActiveBranchCount = 0;

                // If target stage is specified, transition to it
                if (!string.IsNullOrEmpty(request.TargetStageKey))
                {
                    var targetStage = instance.WorkflowTemplate.Stages.FirstOrDefault(s => s.StageKey == request.TargetStageKey);
                    if (targetStage != null)
                    {
                        instance.CurrentStageKey = targetStage.StageKey;
                        instance.CurrentStageId = targetStage.Id;
                        instance.StageEnteredAt = now;
                    }
                }
            }
            else
            {
                instance.ActiveBranchCount = activeBranches.Count;
            }

            var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);
            return new TransitionResult
            {
                Success = true,
                Message = activeBranches.Count > 0
                    ? $"Stage completed. {activeBranches.Count} branches still active."
                    : "All parallel branches completed.",
                Instance = instanceDto
            };
        }

        // Handle join based on join type
        var completedBranches = instance.StageInstances
            .Where(si => si.Status == StageInstanceStatuses.Completed && si.BranchId != null)
            .ToList();

        var allBranchIds = instance.StageInstances
            .Where(si => si.BranchId != null)
            .Select(si => si.BranchId!)
            .Distinct()
            .ToList();

        var requiredBranches = syncStage.JoinType == WorkflowJoinTypes.All
            ? allBranchIds.Count
            : syncStage.MinBranchesToComplete ?? 1;

        if (completedBranches.Count >= requiredBranches)
        {
            // Join condition met - transition to sync stage
            if (syncStage.JoinType == WorkflowJoinTypes.Any && activeBranches.Count > 0)
            {
                // Cancel remaining branches
                foreach (var branch in activeBranches)
                {
                    branch.Status = StageInstanceStatuses.Cancelled;
                    branch.CompletedAt = now;
                    branch.Outcome = "cancelled_by_join";
                    branch.UpdatedAt = now;
                }
            }

            instance.HasParallelStages = false;
            instance.ActiveBranchCount = 0;
            instance.CurrentStageKey = syncStage.StageKey;
            instance.CurrentStageId = syncStage.Id;
            instance.StageEnteredAt = now;
            instance.SlaDeadline = syncStage.SlaHours.HasValue ? now.AddHours(syncStage.SlaHours.Value) : null;
            instance.SlaStatus = WorkflowSlaStatuses.OnTrack;
            instance.SlaPercentConsumed = 0;

            // Create history entry for join
            var joinHistory = new WorkflowHistory
            {
                WorkflowInstanceId = instance.Id,
                NoticeId = instance.NoticeId,
                EventType = "workflow_joined",
                ToStageKey = syncStage.StageKey,
                PerformedById = userId,
                Description = $"Parallel branches joined at '{syncStage.Name}' (join type: {syncStage.JoinType})",
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.WorkflowHistories.Add(joinHistory);

            var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);
            return new TransitionResult
            {
                Success = true,
                Message = $"Branches joined at '{syncStage.Name}'",
                Instance = instanceDto
            };
        }
        else
        {
            instance.ActiveBranchCount = activeBranches.Count;
            var instanceDto = await GetWorkflowInstanceByIdAsync(instance.Id, cancellationToken);
            return new TransitionResult
            {
                Success = true,
                Message = $"Stage completed. Waiting for {requiredBranches - completedBranches.Count} more branch(es) to complete.",
                Instance = instanceDto
            };
        }
    }

    private static StageInstanceDto MapToStageInstanceDto(WorkflowStageInstance si)
    {
        return new StageInstanceDto
        {
            Id = si.Id,
            WorkflowInstanceId = si.WorkflowInstanceId,
            StageId = si.StageId,
            StageKey = si.StageKey,
            StageName = si.Stage?.Name ?? si.StageKey,
            BranchId = si.BranchId,
            Status = si.Status,
            EnteredAt = si.EnteredAt,
            CompletedAt = si.CompletedAt,
            SlaDeadline = si.SlaDeadline,
            SlaStatus = si.SlaStatus,
            SlaPercentConsumed = si.SlaPercentConsumed,
            AssignedToId = si.AssignedToId,
            AssignedToName = si.AssignedTo?.Name,
            AssignedRole = si.AssignedRole,
            Outcome = si.Outcome,
            TimeSpentMinutes = si.TimeSpentMinutes,
            AllowedTransitions = si.Stage?.AllowedTransitions ?? []
        };
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
