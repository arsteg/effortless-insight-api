using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Collaboration;

public class TaskService : ITaskService
{
    private readonly ApplicationDbContext _context;
    private readonly IActivityService _activityService;
    private readonly INotificationService _notificationService;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        ApplicationDbContext context,
        IActivityService activityService,
        INotificationService notificationService,
        ICurrentOrganizationService orgService,
        ILogger<TaskService> logger)
    {
        _context = context;
        _activityService = activityService;
        _notificationService = notificationService;
        _orgService = orgService;
        _logger = logger;
    }

    public async Task<TaskDetailDto> CreateTaskAsync(Guid noticeId, CreateTaskDto dto, Guid userId)
    {
        var notice = await _context.Notices
            .FirstOrDefaultAsync(n => n.Id == noticeId)
            ?? throw new KeyNotFoundException("Notice not found");

        // Verify parent task if specified
        NoticeTask? parentTask = null;
        if (dto.ParentTaskId.HasValue)
        {
            parentTask = await _context.Tasks
                .FirstOrDefaultAsync(t => t.Id == dto.ParentTaskId.Value && t.NoticeId == noticeId);
            if (parentTask == null)
                throw new InvalidOperationException("Parent task not found or belongs to different notice");

            // Check for circular dependency (can't be subtask of own subtask)
            if (await HasCircularDependency(dto.ParentTaskId.Value, noticeId))
                throw new InvalidOperationException("Cannot create circular task dependency");
        }

        // Apply template if specified
        TaskTemplate? template = null;
        if (dto.TemplateId.HasValue)
        {
            template = await _context.TaskTemplates
                .FirstOrDefaultAsync(t => t.Id == dto.TemplateId.Value && t.IsActive);
        }

        // Validate team assignment if specified
        Team? assignedTeam = null;
        if (dto.AssignedTeamId.HasValue)
        {
            assignedTeam = await _context.Set<Team>()
                .Include(t => t.Members)
                .FirstOrDefaultAsync(t => t.Id == dto.AssignedTeamId.Value &&
                                          t.OrganizationId == notice.OrganizationId &&
                                          t.IsActive);
            if (assignedTeam == null)
                throw new InvalidOperationException("Team not found or not active");
        }

        var task = new NoticeTask
        {
            NoticeId = noticeId,
            ParentTaskId = dto.ParentTaskId,
            Title = dto.Title,
            Description = dto.Description ?? template?.DefaultDescription,
            Priority = dto.Priority ?? template?.DefaultPriority ?? TaskPriorityValues.Medium,
            Status = TaskStatusValues.Todo,
            DueDate = dto.DueDate,
            EstimatedHours = dto.EstimatedHours ?? template?.DefaultEstimatedHours,
            Labels = dto.Labels ?? template?.DefaultLabels,
            TemplateId = dto.TemplateId,
            AssignedTeamId = dto.AssignedTeamId,
            CreatedById = userId
        };

        _context.Tasks.Add(task);

        // Handle assignees - combine explicit assignees with team members
        var assigneeIds = dto.Assignees?.ToList() ?? new List<Guid>();

        // If team is assigned, add team members as assignees
        if (assignedTeam != null)
        {
            var teamMemberIds = assignedTeam.Members.Select(m => m.UserId).ToList();
            assigneeIds = assigneeIds.Union(teamMemberIds).ToList();
        }

        // Default to creator if no assignees
        if (assigneeIds.Count == 0)
        {
            assigneeIds.Add(userId);
        }

        if (assigneeIds.Count > 20)
            throw new InvalidOperationException("Maximum 20 assignees allowed per task (including team members)");

        foreach (var assigneeId in assigneeIds.Distinct())
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == assigneeId);
            if (!userExists)
                throw new InvalidOperationException($"User {assigneeId} not found");

            // Determine if this assignment came from a team
            Guid? teamIdForAssignee = assignedTeam?.Members.Any(m => m.UserId == assigneeId) == true
                ? assignedTeam.Id
                : null;

            _context.TaskAssignees.Add(new TaskAssignee
            {
                TaskId = task.Id,
                UserId = assigneeId,
                TeamId = teamIdForAssignee,
                AssignedAt = DateTime.UtcNow,
                AssignedById = userId
            });
        }

        // Also set legacy AssignedToId for backward compatibility
        task.AssignedToId = assigneeIds.First();

        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            notice.OrganizationId,
            noticeId,
            ActivityTypes.TaskCreated,
            userId,
            new Dictionary<string, object>
            {
                ["taskId"] = task.Id,
                ["taskTitle"] = task.Title,
                ["assigneeIds"] = assigneeIds,
                ["priority"] = task.Priority
            },
            $"created task \"{task.Title}\""
        );

        // Log assignment activity if assigned to others
        var otherAssignees = assigneeIds.Where(id => id != userId).ToList();
        if (otherAssignees.Any())
        {
            await _activityService.LogActivityAsync(
                notice.OrganizationId,
                noticeId,
                ActivityTypes.TaskAssigned,
                userId,
                new Dictionary<string, object>
                {
                    ["taskId"] = task.Id,
                    ["taskTitle"] = task.Title,
                    ["assigneeIds"] = otherAssignees
                },
                $"assigned task \"{task.Title}\""
            );

            // Send notification to assignees (fire and forget)
            _ = _notificationService.NotifyTaskAssignedAsync(task, otherAssignees, userId);

            // Queue WhatsApp notifications for assignees
            foreach (var assigneeId in otherAssignees)
            {
                try
                {
                    WhatsAppJobsExtensions.QueueTaskAssignmentNotification(task.Id, userId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to queue WhatsApp task assignment notification for task {TaskId}", task.Id);
                }
            }
        }

        return await GetTaskByIdAsync(task.Id, userId);
    }

    public async Task<TaskDetailDto> GetTaskByIdAsync(Guid taskId, Guid userId)
    {
        // Verify user has access to this task
        if (!await CanUserAccessTaskAsync(taskId, userId))
        {
            throw new KeyNotFoundException("Task not found");
        }

        var task = await _context.Tasks
            .Include(t => t.Assignees).ThenInclude(a => a.User)
            .Include(t => t.Assignees).ThenInclude(a => a.Team)
            .Include(t => t.AssignedTeam).ThenInclude(team => team!.Members)
            .Include(t => t.CreatedBy)
            .Include(t => t.CompletedBy)
            .Include(t => t.Subtasks).ThenInclude(s => s.Assignees).ThenInclude(a => a.User)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new KeyNotFoundException("Task not found");

        return MapToDetailDto(task);
    }

    public async Task<TaskListResponseDto> GetTasksForNoticeAsync(
        Guid noticeId,
        Guid userId,
        string? status = null,
        Guid? assignee = null,
        string? priority = null,
        bool includeSubtasks = true)
    {
        // Verify user has access to this notice's organization
        var notice = await _context.Notices
            .FirstOrDefaultAsync(n => n.Id == noticeId)
            ?? throw new KeyNotFoundException("Notice not found");

        var isMember = await _context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == notice.OrganizationId && m.UserId == userId);

        if (!isMember)
        {
            throw new UnauthorizedAccessException("You do not have access to this notice");
        }

        var query = _context.Tasks
            .Include(t => t.Assignees).ThenInclude(a => a.User)
            .Include(t => t.Assignees).ThenInclude(a => a.Team)
            .Include(t => t.AssignedTeam).ThenInclude(team => team!.Members)
            .Include(t => t.CreatedBy)
            .Include(t => t.CompletedBy)
            .Where(t => t.NoticeId == noticeId);

        // Only get top-level tasks (subtasks are included via navigation)
        if (!includeSubtasks)
        {
            query = query.Where(t => t.ParentTaskId == null);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(t => t.Status == status);
        }

        if (assignee.HasValue)
        {
            query = query.Where(t => t.Assignees.Any(a => a.UserId == assignee.Value));
        }

        if (!string.IsNullOrEmpty(priority))
        {
            query = query.Where(t => t.Priority == priority);
        }

        var tasks = await query
            .OrderByDescending(t => t.Priority == TaskPriorityValues.Critical)
            .ThenByDescending(t => t.Priority == TaskPriorityValues.High)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        var allTasks = includeSubtasks
            ? tasks.Where(t => t.ParentTaskId == null).ToList()
            : tasks;

        var taskDtos = allTasks.Select(t => MapToDto(t, tasks)).ToList();

        var summary = new TaskSummaryDto(
            Total: tasks.Count,
            Todo: tasks.Count(t => t.Status == TaskStatusValues.Todo),
            InProgress: tasks.Count(t => t.Status == TaskStatusValues.InProgress),
            Done: tasks.Count(t => t.Status == TaskStatusValues.Done),
            Blocked: tasks.Count(t => t.Status == TaskStatusValues.Blocked),
            OnHold: tasks.Count(t => t.Status == TaskStatusValues.OnHold),
            Overdue: tasks.Count(t => IsOverdue(t))
        );

        return new TaskListResponseDto(taskDtos, summary);
    }

    public async Task<TaskDetailDto> UpdateTaskAsync(Guid taskId, UpdateTaskDto dto, Guid userId)
    {
        var task = await _context.Tasks
            .Include(t => t.Notice)
            .Include(t => t.Assignees).ThenInclude(a => a.User)
            .Include(t => t.Assignees).ThenInclude(a => a.Team)
            .Include(t => t.AssignedTeam)
            .FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new KeyNotFoundException("Task not found");

        var oldStatus = task.Status;
        var changes = new Dictionary<string, object>();

        if (dto.Title != null)
        {
            changes["title"] = new { from = task.Title, to = dto.Title };
            task.Title = dto.Title;
        }

        if (dto.Description != null)
        {
            task.Description = dto.Description;
        }

        if (dto.Priority != null)
        {
            changes["priority"] = new { from = task.Priority, to = dto.Priority };
            task.Priority = dto.Priority;
        }

        if (dto.DueDate.HasValue)
        {
            task.DueDate = dto.DueDate;
        }

        if (dto.EstimatedHours.HasValue)
        {
            task.EstimatedHours = dto.EstimatedHours;
        }

        if (dto.ActualHours.HasValue)
        {
            task.ActualHours = dto.ActualHours;
        }

        if (dto.Labels != null)
        {
            task.Labels = dto.Labels;
        }

        // Handle status transition
        if (dto.Status != null && dto.Status != task.Status)
        {
            await ValidateStatusTransitionWithDependenciesAsync(task, dto.Status);
            task.Status = dto.Status;
            changes["status"] = new { from = oldStatus, to = dto.Status };

            if (dto.Status == TaskStatusValues.Done)
            {
                task.CompletedAt = DateTime.UtcNow;
                task.CompletedById = userId;
                task.CompletionNote = dto.CompletionNote;
            }
            else if (oldStatus == TaskStatusValues.Done)
            {
                // Reopening task
                task.CompletedAt = null;
                task.CompletedById = null;
                task.CompletionNote = null;
            }
        }

        // Handle team assignment changes
        Team? newAssignedTeam = null;
        if (dto.ClearTeamAssignment == true)
        {
            task.AssignedTeamId = null;
            changes["teamCleared"] = true;
        }
        else if (dto.AssignedTeamId.HasValue)
        {
            newAssignedTeam = await _context.Set<Team>()
                .Include(t => t.Members)
                .FirstOrDefaultAsync(t => t.Id == dto.AssignedTeamId.Value &&
                                          t.OrganizationId == task.Notice.OrganizationId &&
                                          t.IsActive);
            if (newAssignedTeam == null)
                throw new InvalidOperationException("Team not found or not active");

            task.AssignedTeamId = dto.AssignedTeamId;
            changes["assignedTeam"] = new { id = newAssignedTeam.Id, name = newAssignedTeam.Name };
        }

        // Handle assignee changes
        if (dto.Assignees != null || newAssignedTeam != null)
        {
            var currentAssigneeIds = task.Assignees.Select(a => a.UserId).ToHashSet();
            var newAssigneeIds = dto.Assignees?.Distinct().ToList() ?? new List<Guid>();

            // If team is assigned, add team members
            if (newAssignedTeam != null)
            {
                var teamMemberIds = newAssignedTeam.Members.Select(m => m.UserId).ToList();
                newAssigneeIds = newAssigneeIds.Union(teamMemberIds).ToList();
            }

            if (newAssigneeIds.Count > 20)
                throw new InvalidOperationException("Maximum 20 assignees allowed per task (including team members)");

            // Remove old assignees not in new list
            var toRemove = task.Assignees.Where(a => !newAssigneeIds.Contains(a.UserId)).ToList();
            foreach (var assignee in toRemove)
            {
                _context.TaskAssignees.Remove(assignee);
            }

            // Add new assignees
            var toAdd = newAssigneeIds.Where(id => !currentAssigneeIds.Contains(id)).ToList();
            foreach (var assigneeId in toAdd)
            {
                var userExists = await _context.Users.AnyAsync(u => u.Id == assigneeId);
                if (!userExists)
                    throw new InvalidOperationException($"User {assigneeId} not found");

                // Determine if this assignment came from a team
                Guid? teamIdForAssignee = newAssignedTeam?.Members.Any(m => m.UserId == assigneeId) == true
                    ? newAssignedTeam.Id
                    : null;

                _context.TaskAssignees.Add(new TaskAssignee
                {
                    TaskId = task.Id,
                    UserId = assigneeId,
                    TeamId = teamIdForAssignee,
                    AssignedAt = DateTime.UtcNow,
                    AssignedById = userId
                });
            }

            // Update legacy field
            task.AssignedToId = newAssigneeIds.FirstOrDefault();

            if (toAdd.Any())
            {
                changes["newAssignees"] = toAdd;

                // Queue WhatsApp notifications for new assignees
                foreach (var assigneeId in toAdd)
                {
                    try
                    {
                        WhatsAppJobsExtensions.QueueTaskAssignmentNotification(task.Id, userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to queue WhatsApp task assignment notification for task {TaskId}", task.Id);
                    }
                }
            }
        }

        task.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Log appropriate activity and send notifications
        if (dto.Status == TaskStatusValues.Done && oldStatus != TaskStatusValues.Done)
        {
            var duration = task.CompletedAt.HasValue && task.CreatedAt != default
                ? (task.CompletedAt.Value - task.CreatedAt).TotalHours
                : (double?)null;

            var activityData = new Dictionary<string, object>
            {
                ["taskId"] = task.Id,
                ["taskTitle"] = task.Title
            };
            if (duration.HasValue)
            {
                activityData["duration"] = FormatDuration(duration.Value);
            }
            if (task.ActualHours.HasValue)
            {
                activityData["actualHours"] = task.ActualHours.Value;
            }

            await _activityService.LogActivityAsync(
                task.Notice.OrganizationId,
                task.NoticeId,
                ActivityTypes.TaskCompleted,
                userId,
                activityData,
                $"completed task \"{task.Title}\""
            );

            // Send notification to task creator (fire and forget)
            _ = _notificationService.NotifyTaskCompletedAsync(task, userId);
        }
        else if (changes.ContainsKey("status"))
        {
            await _activityService.LogActivityAsync(
                task.Notice.OrganizationId,
                task.NoticeId,
                ActivityTypes.TaskStatusChanged,
                userId,
                new Dictionary<string, object>
                {
                    ["taskId"] = task.Id,
                    ["taskTitle"] = task.Title,
                    ["fromStatus"] = oldStatus,
                    ["toStatus"] = task.Status
                },
                $"changed task \"{task.Title}\" status from {oldStatus} to {task.Status}"
            );

            // Send notification to assignees (fire and forget)
            _ = _notificationService.NotifyTaskStatusChangedAsync(task, oldStatus, userId);
        }
        else if (changes.Any())
        {
            await _activityService.LogActivityAsync(
                task.Notice.OrganizationId,
                task.NoticeId,
                ActivityTypes.TaskUpdated,
                userId,
                new Dictionary<string, object>
                {
                    ["taskId"] = task.Id,
                    ["taskTitle"] = task.Title,
                    ["changes"] = changes
                },
                $"updated task \"{task.Title}\""
            );
        }

        return await GetTaskByIdAsync(task.Id, userId);
    }

    public async Task DeleteTaskAsync(Guid taskId, Guid userId)
    {
        var task = await _context.Tasks
            .Include(t => t.Notice)
            .Include(t => t.Assignees)
            .FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new KeyNotFoundException("Task not found");

        task.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Task {TaskId} deleted by user {UserId}", taskId, userId);
    }

    public async Task<MyTasksResponseDto> GetMyTasksAsync(
        Guid userId,
        string? status = null,
        string? priority = null,
        string? dueWithin = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _context.Tasks
            .Include(t => t.Assignees).ThenInclude(a => a.Team)
            .Include(t => t.AssignedTeam)
            .Include(t => t.Notice).ThenInclude(n => n.Organization)
            .Where(t => t.Assignees.Any(a => a.UserId == userId));

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrEmpty(priority))
        {
            query = query.Where(t => t.Priority == priority);
        }

        if (!string.IsNullOrEmpty(dueWithin))
        {
            var now = DateTime.UtcNow;
            DateTime? endDate = dueWithin.ToLower() switch
            {
                "today" => now.Date.AddDays(1),
                "week" => now.Date.AddDays(7),
                "month" => now.Date.AddMonths(1),
                _ => null
            };

            if (endDate.HasValue)
            {
                query = query.Where(t => t.DueDate.HasValue && t.DueDate <= endDate);
            }
        }

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var tasks = await query
            .OrderByDescending(t => t.Priority == TaskPriorityValues.Critical)
            .ThenByDescending(t => t.Priority == TaskPriorityValues.High)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var taskDtos = tasks.Select(t => new MyTaskDto(
            Id: t.Id,
            Title: t.Title,
            Notice: new MyTaskNoticeDto(
                Id: t.NoticeId,
                Number: t.Notice.NoticeNumber,
                Type: t.Notice.NoticeType,
                Organization: new MyTaskOrganizationDto(
                    Id: t.Notice.OrganizationId,
                    Name: t.Notice.Organization.Name
                )
            ),
            Status: t.Status,
            Priority: t.Priority,
            DueDate: t.DueDate,
            IsOverdue: IsOverdue(t)
        )).ToList();

        return new MyTasksResponseDto(
            Tasks: taskDtos,
            Pagination: new PaginationDto(page, pageSize, totalItems, totalPages)
        );
    }

    public async Task<TaskTemplateDto> CreateTaskTemplateAsync(CreateTaskTemplateDto dto, Guid organizationId, Guid userId)
    {
        var template = new TaskTemplate
        {
            OrganizationId = organizationId,
            Name = dto.Name,
            Description = dto.Description,
            DefaultTitle = dto.DefaultTitle,
            DefaultDescription = dto.DefaultDescription,
            DefaultPriority = dto.DefaultPriority ?? TaskPriorityValues.Medium,
            DefaultEstimatedHours = dto.DefaultEstimatedHours,
            DefaultLabels = dto.DefaultLabels,
            ApplicableNoticeTypes = dto.ApplicableNoticeTypes ?? new List<string> { "*" },
            IsActive = true,
            CreatedById = userId
        };

        _context.TaskTemplates.Add(template);
        await _context.SaveChangesAsync();

        return MapToTemplateDto(template);
    }

    public async Task<List<TaskTemplateDto>> GetTaskTemplatesAsync(Guid organizationId, string? noticeType = null)
    {
        var query = _context.TaskTemplates
            .Where(t => t.IsActive && (t.OrganizationId == null || t.OrganizationId == organizationId));

        if (!string.IsNullOrEmpty(noticeType))
        {
            query = query.Where(t =>
                t.ApplicableNoticeTypes == null ||
                t.ApplicableNoticeTypes.Contains("*") ||
                t.ApplicableNoticeTypes.Contains(noticeType));
        }

        var templates = await query
            .OrderBy(t => t.Name)
            .ToListAsync();

        return templates.Select(MapToTemplateDto).ToList();
    }

    public async Task DeleteTaskTemplateAsync(Guid templateId, Guid organizationId)
    {
        var template = await _context.TaskTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("Template not found");

        template.DeletedAt = DateTime.UtcNow;
        template.IsActive = false;
        await _context.SaveChangesAsync();
    }

    public async Task<bool> CanUserAccessTaskAsync(Guid taskId, Guid userId)
    {
        var task = await _context.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null) return false;

        // Check if user is member of the organization
        var isMember = await _context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == task.Notice.OrganizationId && m.UserId == userId);

        return isMember;
    }

    // Private helpers

    private static void ValidateStatusTransition(string currentStatus, string newStatus)
    {
        var validTransitions = new Dictionary<string, string[]>
        {
            [TaskStatusValues.Todo] = new[] { TaskStatusValues.InProgress, TaskStatusValues.Blocked, TaskStatusValues.OnHold, TaskStatusValues.Done },
            [TaskStatusValues.InProgress] = new[] { TaskStatusValues.Todo, TaskStatusValues.Done, TaskStatusValues.Blocked, TaskStatusValues.OnHold },
            [TaskStatusValues.Done] = new[] { TaskStatusValues.Todo, TaskStatusValues.InProgress, TaskStatusValues.Archived },
            [TaskStatusValues.Blocked] = new[] { TaskStatusValues.Todo, TaskStatusValues.InProgress, TaskStatusValues.OnHold },
            [TaskStatusValues.OnHold] = new[] { TaskStatusValues.Todo, TaskStatusValues.InProgress, TaskStatusValues.Blocked },
            [TaskStatusValues.Archived] = Array.Empty<string>()
        };

        if (!validTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new InvalidOperationException($"Invalid status transition from {currentStatus} to {newStatus}");
        }
    }

    private async Task<bool> HasCircularDependency(Guid parentTaskId, Guid noticeId)
    {
        // Simple check - prevent deep nesting (max 3 levels)
        var depth = 0;
        var currentId = parentTaskId;

        while (currentId != Guid.Empty && depth < 5)
        {
            var parent = await _context.Tasks
                .Where(t => t.Id == currentId)
                .Select(t => t.ParentTaskId)
                .FirstOrDefaultAsync();

            if (!parent.HasValue) break;
            currentId = parent.Value;
            depth++;
        }

        return depth >= 3; // Max 3 levels of nesting
    }

    private static bool IsOverdue(NoticeTask task)
    {
        return task.DueDate.HasValue &&
               task.DueDate < DateTime.UtcNow &&
               task.Status != TaskStatusValues.Done &&
               task.Status != TaskStatusValues.Archived;
    }

    private static string FormatDuration(double hours)
    {
        var ts = TimeSpan.FromHours(hours);
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours}h";
        return $"{ts.Hours}h {ts.Minutes}m";
    }

    private static TaskDto MapToDto(NoticeTask task, List<NoticeTask>? allTasks = null)
    {
        var subtasks = allTasks?.Where(t => t.ParentTaskId == task.Id).ToList() ?? new List<NoticeTask>();

        return new TaskDto(
            Id: task.Id,
            NoticeId: task.NoticeId,
            Title: task.Title,
            Description: task.Description,
            Status: task.Status,
            Priority: task.Priority,
            DueDate: task.DueDate,
            EstimatedHours: task.EstimatedHours,
            ActualHours: task.ActualHours,
            IsOverdue: IsOverdue(task),
            Assignees: task.Assignees.Select(a => new TaskAssigneeDto(
                Id: a.UserId,
                Name: a.User?.Name ?? "Unknown",
                Email: a.User?.Email,
                AvatarUrl: a.User?.AvatarUrl,
                AssignedAt: a.AssignedAt,
                TeamId: a.TeamId,
                TeamName: a.Team?.Name
            )).ToList(),
            AssignedTeam: task.AssignedTeam != null ? new TaskTeamDto(
                Id: task.AssignedTeam.Id,
                Name: task.AssignedTeam.Name,
                Color: task.AssignedTeam.Color,
                Icon: task.AssignedTeam.Icon,
                MemberCount: task.AssignedTeam.Members?.Count ?? 0
            ) : null,
            Labels: task.Labels,
            ParentTaskId: task.ParentTaskId,
            SubtaskCount: subtasks.Count,
            SubtasksCompleted: subtasks.Count(s => s.Status == TaskStatusValues.Done),
            CreatedBy: new TaskUserDto(
                Id: task.CreatedById,
                Name: task.CreatedBy?.Name ?? "Unknown",
                AvatarUrl: task.CreatedBy?.AvatarUrl
            ),
            CreatedAt: task.CreatedAt,
            UpdatedAt: task.UpdatedAt,
            CompletedAt: task.CompletedAt,
            CompletedBy: task.CompletedBy != null ? new TaskUserDto(
                Id: task.CompletedBy.Id,
                Name: task.CompletedBy.Name,
                AvatarUrl: task.CompletedBy.AvatarUrl
            ) : null,
            CompletionNote: task.CompletionNote
        );
    }

    private static TaskDetailDto MapToDetailDto(NoticeTask task)
    {
        return new TaskDetailDto(
            Id: task.Id,
            NoticeId: task.NoticeId,
            Title: task.Title,
            Description: task.Description,
            Status: task.Status,
            Priority: task.Priority,
            DueDate: task.DueDate,
            EstimatedHours: task.EstimatedHours,
            ActualHours: task.ActualHours,
            IsOverdue: IsOverdue(task),
            Assignees: task.Assignees.Select(a => new TaskAssigneeDto(
                Id: a.UserId,
                Name: a.User?.Name ?? "Unknown",
                Email: a.User?.Email,
                AvatarUrl: a.User?.AvatarUrl,
                AssignedAt: a.AssignedAt,
                TeamId: a.TeamId,
                TeamName: a.Team?.Name
            )).ToList(),
            AssignedTeam: task.AssignedTeam != null ? new TaskTeamDto(
                Id: task.AssignedTeam.Id,
                Name: task.AssignedTeam.Name,
                Color: task.AssignedTeam.Color,
                Icon: task.AssignedTeam.Icon,
                MemberCount: task.AssignedTeam.Members?.Count ?? 0
            ) : null,
            Labels: task.Labels,
            ParentTaskId: task.ParentTaskId,
            Subtasks: task.Subtasks?.Select(s => MapToDto(s)).ToList(),
            CreatedBy: new TaskUserDto(
                Id: task.CreatedById,
                Name: task.CreatedBy?.Name ?? "Unknown",
                AvatarUrl: task.CreatedBy?.AvatarUrl
            ),
            CreatedAt: task.CreatedAt,
            UpdatedAt: task.UpdatedAt,
            CompletedAt: task.CompletedAt,
            CompletedBy: task.CompletedBy != null ? new TaskUserDto(
                Id: task.CompletedBy.Id,
                Name: task.CompletedBy.Name,
                AvatarUrl: task.CompletedBy.AvatarUrl
            ) : null,
            CompletionNote: task.CompletionNote,
            Attachments: task.Attachments?.Select(a => new AttachmentDto(
                Id: a.Id,
                FileName: a.FileName,
                FileUrl: a.FileUrl,
                FileSize: a.FileSize,
                FileType: a.FileType,
                DocumentType: a.DocumentType,
                Description: a.Description
            )).ToList()
        );
    }

    private static TaskTemplateDto MapToTemplateDto(TaskTemplate template)
    {
        return new TaskTemplateDto(
            Id: template.Id,
            OrganizationId: template.OrganizationId,
            Name: template.Name,
            Description: template.Description,
            DefaultTitle: template.DefaultTitle,
            DefaultDescription: template.DefaultDescription,
            DefaultPriority: template.DefaultPriority,
            DefaultEstimatedHours: template.DefaultEstimatedHours,
            DefaultLabels: template.DefaultLabels,
            ApplicableNoticeTypes: template.ApplicableNoticeTypes,
            IsActive: template.IsActive,
            CreatedAt: template.CreatedAt
        );
    }

    // =============================================================================
    // GAP-TASK-001: Task Dependencies
    // =============================================================================

    public async Task<TaskDependencyDto> AddDependencyAsync(Guid taskId, Guid dependsOnTaskId, string type, Guid userId)
    {
        if (taskId == dependsOnTaskId)
            throw new InvalidOperationException("A task cannot depend on itself");

        if (!TaskDependencyType.IsValid(type))
            throw new InvalidOperationException($"Invalid dependency type: {type}. Valid types are: {string.Join(", ", TaskDependencyType.All)}");

        var task = await _context.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new KeyNotFoundException("Task not found");

        var dependsOnTask = await _context.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t => t.Id == dependsOnTaskId)
            ?? throw new KeyNotFoundException("Dependency task not found");

        // Both tasks must belong to the same notice
        if (task.NoticeId != dependsOnTask.NoticeId)
            throw new InvalidOperationException("Tasks must belong to the same notice");

        // Check for existing dependency
        var existingDependency = await _context.TaskDependencies
            .AnyAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId);

        if (existingDependency)
            throw new InvalidOperationException("This dependency already exists");

        // Check for circular dependency
        if (await WouldCreateCircularDependency(taskId, dependsOnTaskId))
            throw new InvalidOperationException("This dependency would create a circular dependency");

        var dependency = new TaskDependency
        {
            TaskId = taskId,
            DependsOnTaskId = dependsOnTaskId,
            DependencyType = type
        };

        _context.TaskDependencies.Add(dependency);
        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            task.Notice.OrganizationId,
            task.NoticeId,
            "task_dependency_added",
            userId,
            new Dictionary<string, object>
            {
                ["taskId"] = taskId,
                ["taskTitle"] = task.Title,
                ["dependsOnTaskId"] = dependsOnTaskId,
                ["dependsOnTaskTitle"] = dependsOnTask.Title,
                ["dependencyType"] = type
            },
            $"added dependency: \"{task.Title}\" depends on \"{dependsOnTask.Title}\""
        );

        return MapToDependencyDto(dependency, dependsOnTask);
    }

    public async Task RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, Guid userId)
    {
        var dependency = await _context.TaskDependencies
            .Include(d => d.Task).ThenInclude(t => t.Notice)
            .Include(d => d.DependsOnTask)
            .FirstOrDefaultAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId)
            ?? throw new KeyNotFoundException("Dependency not found");

        dependency.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            dependency.Task.Notice.OrganizationId,
            dependency.Task.NoticeId,
            "task_dependency_removed",
            userId,
            new Dictionary<string, object>
            {
                ["taskId"] = taskId,
                ["taskTitle"] = dependency.Task.Title,
                ["dependsOnTaskId"] = dependsOnTaskId,
                ["dependsOnTaskTitle"] = dependency.DependsOnTask.Title
            },
            $"removed dependency: \"{dependency.Task.Title}\" no longer depends on \"{dependency.DependsOnTask.Title}\""
        );
    }

    public async Task<List<TaskDependencyDto>> GetDependenciesAsync(Guid taskId, Guid userId)
    {
        if (!await CanUserAccessTaskAsync(taskId, userId))
            throw new KeyNotFoundException("Task not found");

        var dependencies = await _context.TaskDependencies
            .Include(d => d.DependsOnTask)
            .Where(d => d.TaskId == taskId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();

        return dependencies.Select(d => MapToDependencyDto(d, d.DependsOnTask)).ToList();
    }

    public async Task<List<TaskSummaryInfoDto>> GetBlockingTasksAsync(Guid taskId, Guid userId)
    {
        if (!await CanUserAccessTaskAsync(taskId, userId))
            throw new KeyNotFoundException("Task not found");

        // Get all "blocks" type dependencies where the dependent task is not done
        var blockingTasks = await _context.TaskDependencies
            .Include(d => d.DependsOnTask)
            .Where(d => d.TaskId == taskId)
            .Where(d => d.DependencyType == TaskDependencyType.Blocks)
            .Where(d => d.DependsOnTask.Status != TaskStatusValues.Done &&
                       d.DependsOnTask.Status != TaskStatusValues.Archived &&
                       d.DependsOnTask.Status != TaskStatusValues.Cancelled)
            .Select(d => d.DependsOnTask)
            .ToListAsync();

        return blockingTasks.Select(t => new TaskSummaryInfoDto(
            Id: t.Id,
            Title: t.Title,
            Status: t.Status,
            Priority: t.Priority,
            DueDate: t.DueDate,
            IsOverdue: IsOverdue(t)
        )).ToList();
    }

    private async Task<bool> WouldCreateCircularDependency(Guid taskId, Guid dependsOnTaskId)
    {
        // Check if dependsOnTask (or any of its transitive dependencies) depends on taskId
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(dependsOnTaskId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (visited.Contains(currentId))
                continue;

            visited.Add(currentId);

            if (currentId == taskId)
                return true; // Circular dependency found

            // Get all tasks that currentId depends on
            var nextDependencies = await _context.TaskDependencies
                .Where(d => d.TaskId == currentId)
                .Select(d => d.DependsOnTaskId)
                .ToListAsync();

            foreach (var nextId in nextDependencies)
            {
                if (!visited.Contains(nextId))
                    queue.Enqueue(nextId);
            }
        }

        return false;
    }

    private static TaskDependencyDto MapToDependencyDto(TaskDependency dependency, NoticeTask dependsOnTask)
    {
        return new TaskDependencyDto(
            Id: dependency.Id,
            TaskId: dependency.TaskId,
            DependsOnTaskId: dependency.DependsOnTaskId,
            DependsOnTask: new TaskSummaryInfoDto(
                Id: dependsOnTask.Id,
                Title: dependsOnTask.Title,
                Status: dependsOnTask.Status,
                Priority: dependsOnTask.Priority,
                DueDate: dependsOnTask.DueDate,
                IsOverdue: IsOverdue(dependsOnTask)
            ),
            DependencyType: dependency.DependencyType,
            CreatedAt: dependency.CreatedAt
        );
    }

    // Validate status transition - updated for dependency blocking
    private async Task ValidateStatusTransitionWithDependenciesAsync(NoticeTask task, string newStatus)
    {
        // First validate normal status transition
        ValidateStatusTransition(task.Status, newStatus);

        // If transitioning to in_progress, check for blocking dependencies
        if (newStatus == TaskStatusValues.InProgress)
        {
            var blockingTasks = await _context.TaskDependencies
                .Include(d => d.DependsOnTask)
                .Where(d => d.TaskId == task.Id)
                .Where(d => d.DependencyType == TaskDependencyType.Blocks)
                .Where(d => d.DependsOnTask.Status != TaskStatusValues.Done &&
                           d.DependsOnTask.Status != TaskStatusValues.Archived &&
                           d.DependsOnTask.Status != TaskStatusValues.Cancelled)
                .Select(d => d.DependsOnTask.Title)
                .ToListAsync();

            if (blockingTasks.Any())
            {
                throw new InvalidOperationException(
                    $"Cannot start this task. It is blocked by incomplete tasks: {string.Join(", ", blockingTasks)}");
            }
        }
    }

    // =============================================================================
    // GAP-TASK-002: Task Reminders
    // =============================================================================

    public async Task<TaskReminderDto> CreateReminderAsync(Guid taskId, CreateTaskReminderDto dto, Guid userId)
    {
        var task = await _context.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new KeyNotFoundException("Task not found");

        if (!task.DueDate.HasValue)
            throw new InvalidOperationException("Cannot create reminder for a task without a due date");

        if (dto.DaysBeforeDue < 0)
            throw new InvalidOperationException("Days before due must be non-negative");

        if (dto.DaysBeforeDue > 365)
            throw new InvalidOperationException("Days before due cannot exceed 365");

        // Check for duplicate reminder
        var existingReminder = await _context.TaskReminders
            .AnyAsync(r => r.TaskId == taskId && r.DaysBeforeDue == dto.DaysBeforeDue);

        if (existingReminder)
            throw new InvalidOperationException($"A reminder for {dto.DaysBeforeDue} days before due already exists");

        var reminder = new TaskReminder
        {
            TaskId = taskId,
            DaysBeforeDue = dto.DaysBeforeDue,
            IsSent = false,
            CreatedById = userId
        };

        _context.TaskReminders.Add(reminder);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created reminder for task {TaskId}: {DaysBeforeDue} days before due", taskId, dto.DaysBeforeDue);

        return MapToReminderDto(reminder);
    }

    public async Task<List<TaskReminderDto>> GetRemindersAsync(Guid taskId, Guid userId)
    {
        if (!await CanUserAccessTaskAsync(taskId, userId))
            throw new KeyNotFoundException("Task not found");

        var reminders = await _context.TaskReminders
            .Where(r => r.TaskId == taskId)
            .OrderBy(r => r.DaysBeforeDue)
            .ToListAsync();

        return reminders.Select(MapToReminderDto).ToList();
    }

    public async Task DeleteReminderAsync(Guid taskId, Guid reminderId, Guid userId)
    {
        var reminder = await _context.TaskReminders
            .FirstOrDefaultAsync(r => r.Id == reminderId && r.TaskId == taskId)
            ?? throw new KeyNotFoundException("Reminder not found");

        reminder.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Deleted reminder {ReminderId} for task {TaskId}", reminderId, taskId);
    }

    private static TaskReminderDto MapToReminderDto(TaskReminder reminder)
    {
        return new TaskReminderDto(
            Id: reminder.Id,
            TaskId: reminder.TaskId,
            DaysBeforeDue: reminder.DaysBeforeDue,
            IsSent: reminder.IsSent,
            SentAt: reminder.SentAt,
            CreatedAt: reminder.CreatedAt
        );
    }

    // ==========================================================================
    // Task Attachments (GAP-TASK-004)
    // ==========================================================================

    public async Task<TaskAttachmentDto> AddAttachmentAsync(
        Guid taskId,
        Stream fileStream,
        string fileName,
        string contentType,
        Guid userId)
    {
        var task = await _context.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new KeyNotFoundException("Task not found");

        if (!await CanUserAccessTaskAsync(taskId, userId))
            throw new UnauthorizedAccessException("Access denied");

        // Validate file
        var maxFileSize = 50 * 1024 * 1024; // 50MB
        if (fileStream.Length > maxFileSize)
            throw new InvalidOperationException("File size exceeds maximum allowed (50MB)");

        var allowedTypes = new[] {
            "application/pdf",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "image/jpeg",
            "image/png",
            "image/gif",
            "text/plain",
            "application/zip",
            "application/x-zip-compressed"
        };

        if (!allowedTypes.Contains(contentType.ToLowerInvariant()))
            throw new InvalidOperationException($"File type '{contentType}' is not allowed");

        // Generate storage path
        var attachmentId = Guid.NewGuid();
        var extension = Path.GetExtension(fileName);
        var storagePath = $"{task.Notice.OrganizationId}/tasks/{taskId}/attachments/{attachmentId}{extension}";

        // For now, store path - in production, upload to S3 via IFileStorageService
        var fileUrl = storagePath;

        // Compute file hash
        fileStream.Position = 0;
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(fileStream);
        var fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var attachment = new Attachment
        {
            Id = attachmentId,
            TaskId = taskId,
            UploadedById = userId,
            FileName = SanitizeFileName(fileName),
            FileUrl = fileUrl,
            FileSize = (int)fileStream.Length,
            FileType = contentType,
            FileHash = fileHash,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Attachments.Add(attachment);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Added attachment {AttachmentId} to task {TaskId}: {FileName}",
            attachment.Id, taskId, fileName);

        return MapToAttachmentDto(attachment, userId);
    }

    public async Task<List<TaskAttachmentDto>> GetAttachmentsAsync(Guid taskId, Guid userId)
    {
        if (!await CanUserAccessTaskAsync(taskId, userId))
            throw new KeyNotFoundException("Task not found");

        var attachments = await _context.Attachments
            .Include(a => a.UploadedBy)
            .Where(a => a.TaskId == taskId && a.DeletedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return attachments.Select(a => MapToAttachmentDto(a, userId)).ToList();
    }

    public async Task<string> GetAttachmentDownloadUrlAsync(Guid taskId, Guid attachmentId, Guid userId)
    {
        if (!await CanUserAccessTaskAsync(taskId, userId))
            throw new KeyNotFoundException("Task not found");

        var attachment = await _context.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId && a.DeletedAt == null)
            ?? throw new KeyNotFoundException("Attachment not found");

        // In production, generate pre-signed URL from S3
        // For now, return the storage path
        return attachment.FileUrl;
    }

    public async Task DeleteAttachmentAsync(Guid taskId, Guid attachmentId, Guid userId)
    {
        var attachment = await _context.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId && a.DeletedAt == null)
            ?? throw new KeyNotFoundException("Attachment not found");

        // Only uploader or task creator can delete
        var task = await _context.Tasks.FindAsync(taskId);
        if (attachment.UploadedById != userId && task?.CreatedById != userId)
        {
            // Check if user is admin/manager
            var membership = await _context.OrganizationMembers
                .FirstOrDefaultAsync(m => m.UserId == userId && m.IsActive);

            if (membership == null || (membership.Role != "admin" && membership.Role != "manager" && membership.Role != "owner"))
                throw new UnauthorizedAccessException("Only the uploader, task creator, or admin can delete attachments");
        }

        attachment.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Deleted attachment {AttachmentId} from task {TaskId}",
            attachmentId, taskId);
    }

    private static TaskAttachmentDto MapToAttachmentDto(Attachment attachment, Guid currentUserId)
    {
        return new TaskAttachmentDto(
            Id: attachment.Id,
            FileName: attachment.FileName,
            FileUrl: attachment.FileUrl,
            FileSize: attachment.FileSize,
            FileType: attachment.FileType,
            DocumentType: null,
            Description: null,
            UploadedBy: new TaskAttachmentUserDto(
                Id: attachment.UploadedById,
                Name: attachment.UploadedBy?.Name ?? "Unknown",
                AvatarUrl: null
            ),
            CreatedAt: attachment.CreatedAt
        );
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Length > 200 ? sanitized[..200] : sanitized;
    }
}
