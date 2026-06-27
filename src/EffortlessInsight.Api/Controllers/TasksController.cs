using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Collaboration;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EffortlessInsight.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1")]
public class TasksController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly ITimeTrackingService _timeTrackingService;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        ITaskService taskService,
        ITimeTrackingService timeTrackingService,
        ICurrentOrganizationService orgService,
        ILogger<TasksController> logger)
    {
        _taskService = taskService;
        _timeTrackingService = timeTrackingService;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue("sub")!);

    // ==========================================================================
    // Notice-scoped Task Endpoints
    // ==========================================================================

    /// <summary>
    /// Get all tasks for a notice
    /// </summary>
    [HttpGet("notices/{noticeId:guid}/tasks")]
    [ProducesResponseType(typeof(TaskListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTasksForNotice(
        Guid noticeId,
        [FromQuery] string? status = null,
        [FromQuery] Guid? assignee = null,
        [FromQuery] string? priority = null,
        [FromQuery] bool includeSubtasks = true)
    {
        try
        {
            var result = await _taskService.GetTasksForNoticeAsync(
                noticeId, GetUserId(), status, assignee, priority, includeSubtasks);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            // Return 404 instead of 403 to prevent information leakage
            return NotFound(new { error = "Notice not found" });
        }
    }

    /// <summary>
    /// Create a new task for a notice
    /// </summary>
    [HttpPost("notices/{noticeId:guid}/tasks")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTask(Guid noticeId, [FromBody] CreateTaskDto dto)
    {
        try
        {
            var result = await _taskService.CreateTaskAsync(noticeId, dto, GetUserId());
            return CreatedAtAction(nameof(GetTaskById), new { taskId = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Task CRUD Endpoints
    // ==========================================================================

    /// <summary>
    /// Get a task by ID
    /// </summary>
    [HttpGet("tasks/{taskId:guid}")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskById(Guid taskId)
    {
        try
        {
            var result = await _taskService.GetTaskByIdAsync(taskId, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update a task
    /// </summary>
    [HttpPatch("tasks/{taskId:guid}")]
    [ProducesResponseType(typeof(TaskDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTask(Guid taskId, [FromBody] UpdateTaskDto dto)
    {
        try
        {
            var result = await _taskService.UpdateTaskAsync(taskId, dto, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a task
    /// </summary>
    [HttpDelete("tasks/{taskId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTask(Guid taskId)
    {
        try
        {
            await _taskService.DeleteTaskAsync(taskId, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // My Tasks Endpoint
    // ==========================================================================

    /// <summary>
    /// Get current user's tasks across all notices
    /// </summary>
    [HttpGet("tasks/my")]
    [ProducesResponseType(typeof(MyTasksResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTasks(
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? dueWithin = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _taskService.GetMyTasksAsync(
            GetUserId(), status, priority, dueWithin, page, pageSize);
        return Ok(result);
    }

    // ==========================================================================
    // Task Template Endpoints
    // ==========================================================================

    /// <summary>
    /// Get task templates
    /// </summary>
    [HttpGet("task-templates")]
    [ProducesResponseType(typeof(List<TaskTemplateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaskTemplates([FromQuery] string? noticeType = null)
    {
        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
        var result = await _taskService.GetTaskTemplatesAsync(orgId, noticeType);
        return Ok(result);
    }

    /// <summary>
    /// Create a task template
    /// </summary>
    [HttpPost("task-templates")]
    [ProducesResponseType(typeof(TaskTemplateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTaskTemplate([FromBody] CreateTaskTemplateDto dto)
    {
        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
        var result = await _taskService.CreateTaskTemplateAsync(dto, orgId, GetUserId());
        return CreatedAtAction(nameof(GetTaskTemplates), result);
    }

    /// <summary>
    /// Delete a task template
    /// </summary>
    [HttpDelete("task-templates/{templateId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteTaskTemplate(Guid templateId)
    {
        try
        {
            var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
            await _taskService.DeleteTaskTemplateAsync(templateId, orgId);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Task Dependency Endpoints (GAP-TASK-001)
    // ==========================================================================

    /// <summary>
    /// Get all dependencies for a task
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/dependencies")]
    [ProducesResponseType(typeof(List<TaskDependencyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskDependencies(Guid taskId)
    {
        try
        {
            var dependencies = await _taskService.GetDependenciesAsync(taskId, GetUserId());
            return Ok(dependencies);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Add a dependency to a task
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/dependencies")]
    [ProducesResponseType(typeof(TaskDependencyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddTaskDependency(Guid taskId, [FromBody] CreateTaskDependencyDto dto)
    {
        try
        {
            var type = dto.Type ?? "blocks";
            var dependency = await _taskService.AddDependencyAsync(taskId, dto.DependsOnTaskId, type, GetUserId());
            return CreatedAtAction(nameof(GetTaskDependencies), new { taskId }, dependency);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Remove a dependency from a task
    /// </summary>
    [HttpDelete("tasks/{taskId:guid}/dependencies/{dependsOnId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveTaskDependency(Guid taskId, Guid dependsOnId)
    {
        try
        {
            await _taskService.RemoveDependencyAsync(taskId, dependsOnId, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get tasks that are blocking this task from starting
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/blocking")]
    [ProducesResponseType(typeof(List<TaskSummaryInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBlockingTasks(Guid taskId)
    {
        try
        {
            var blockingTasks = await _taskService.GetBlockingTasksAsync(taskId, GetUserId());
            return Ok(blockingTasks);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Task Reminder Endpoints (GAP-TASK-002)
    // ==========================================================================

    /// <summary>
    /// Get all reminders for a task
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/reminders")]
    [ProducesResponseType(typeof(List<TaskReminderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskReminders(Guid taskId)
    {
        try
        {
            var reminders = await _taskService.GetRemindersAsync(taskId, GetUserId());
            return Ok(reminders);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create a reminder for a task
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/reminders")]
    [ProducesResponseType(typeof(TaskReminderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTaskReminder(Guid taskId, [FromBody] CreateTaskReminderDto dto)
    {
        try
        {
            var reminder = await _taskService.CreateReminderAsync(taskId, dto, GetUserId());
            return CreatedAtAction(nameof(GetTaskReminders), new { taskId }, reminder);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a task reminder
    /// </summary>
    [HttpDelete("tasks/{taskId:guid}/reminders/{reminderId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTaskReminder(Guid taskId, Guid reminderId)
    {
        try
        {
            await _taskService.DeleteReminderAsync(taskId, reminderId, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ==========================================================================
    // Task Attachment Endpoints (GAP-TASK-004)
    // ==========================================================================

    /// <summary>
    /// Get all attachments for a task
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/attachments")]
    [ProducesResponseType(typeof(List<TaskAttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskAttachments(Guid taskId)
    {
        try
        {
            var attachments = await _taskService.GetAttachmentsAsync(taskId, GetUserId());
            return Ok(attachments);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Upload an attachment to a task
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/attachments")]
    [ProducesResponseType(typeof(TaskAttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [RequestSizeLimit(52_428_800)] // 50MB
    public async Task<IActionResult> UploadTaskAttachment(
        Guid taskId,
        IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        try
        {
            using var stream = file.OpenReadStream();
            var attachment = await _taskService.AddAttachmentAsync(
                taskId,
                stream,
                file.FileName,
                file.ContentType,
                GetUserId());

            _logger.LogInformation(
                "User {UserId} uploaded attachment to task {TaskId}: {FileName}",
                GetUserId(), taskId, file.FileName);

            return CreatedAtAction(
                nameof(GetTaskAttachments),
                new { taskId },
                attachment);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound(new { error = "Task not found" });
        }
    }

    /// <summary>
    /// Get download URL for a task attachment
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/attachments/{attachmentId:guid}/download")]
    [ProducesResponseType(typeof(AttachmentDownloadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentDownloadUrl(Guid taskId, Guid attachmentId)
    {
        try
        {
            var downloadUrl = await _taskService.GetAttachmentDownloadUrlAsync(
                taskId, attachmentId, GetUserId());

            return Ok(new AttachmentDownloadResponse(downloadUrl, DateTime.UtcNow.AddMinutes(15)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a task attachment
    /// </summary>
    [HttpDelete("tasks/{taskId:guid}/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteTaskAttachment(Guid taskId, Guid attachmentId)
    {
        try
        {
            await _taskService.DeleteAttachmentAsync(taskId, attachmentId, GetUserId());

            _logger.LogInformation(
                "User {UserId} deleted attachment {AttachmentId} from task {TaskId}",
                GetUserId(), attachmentId, taskId);

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }
    // ==========================================================================
    // Task Time Tracking Endpoints (GAP-TASK-003)
    // ==========================================================================

    /// <summary>
    /// Get all time entries for a task
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/time-entries")]
    [ProducesResponseType(typeof(TimeEntriesResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimeEntries(Guid taskId)
    {
        try
        {
            var entries = await _timeTrackingService.GetTimeEntriesAsync(taskId, default);
            var totalHours = await _timeTrackingService.GetTotalHoursAsync(taskId, default);
            var activeTimer = await _timeTrackingService.GetActiveTimerAsync(taskId, GetUserId(), default);

            var result = new TimeEntriesResponseDto(
                Entries: entries.Select(e => new TimeEntryDto(
                    e.Id,
                    e.TaskId,
                    e.UserId,
                    e.User?.Name ?? "Unknown",
                    e.Date,
                    e.Hours,
                    e.Description,
                    e.IsBillable,
                    e.StartTime,
                    e.EndTime,
                    e.IsTimerRunning,
                    e.CreatedAt
                )).ToList(),
                TotalHours: totalHours,
                ActiveTimerId: activeTimer?.Id
            );

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Log time manually for a task
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/time-entries")]
    [ProducesResponseType(typeof(TimeEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LogTime(Guid taskId, [FromBody] CreateTimeEntryDto dto)
    {
        try
        {
            var entry = await _timeTrackingService.LogTimeAsync(
                taskId,
                GetUserId(),
                dto.Hours,
                dto.Date,
                dto.Description,
                dto.IsBillable ?? true,
                default);

            var result = new TimeEntryDto(
                entry.Id,
                entry.TaskId,
                entry.UserId,
                null,
                entry.Date,
                entry.Hours,
                entry.Description,
                entry.IsBillable,
                entry.StartTime,
                entry.EndTime,
                entry.IsTimerRunning,
                entry.CreatedAt
            );

            return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Start a timer for a task
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/timer/start")]
    [ProducesResponseType(typeof(TimeEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartTimer(Guid taskId)
    {
        try
        {
            var entry = await _timeTrackingService.StartTimerAsync(taskId, GetUserId(), default);

            var result = new TimeEntryDto(
                entry.Id,
                entry.TaskId,
                entry.UserId,
                null,
                entry.Date,
                entry.Hours,
                entry.Description,
                entry.IsBillable,
                entry.StartTime,
                entry.EndTime,
                entry.IsTimerRunning,
                entry.CreatedAt
            );

            return CreatedAtAction(nameof(GetTimeEntries), new { taskId }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Stop a running timer
    /// </summary>
    [HttpPost("tasks/{taskId:guid}/timer/{entryId:guid}/stop")]
    [ProducesResponseType(typeof(TimeEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StopTimer(Guid taskId, Guid entryId)
    {
        try
        {
            var entry = await _timeTrackingService.StopTimerAsync(entryId, default);

            var result = new TimeEntryDto(
                entry.Id,
                entry.TaskId,
                entry.UserId,
                null,
                entry.Date,
                entry.Hours,
                entry.Description,
                entry.IsBillable,
                entry.StartTime,
                entry.EndTime,
                entry.IsTimerRunning,
                entry.CreatedAt
            );

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update a time entry
    /// </summary>
    [HttpPatch("tasks/{taskId:guid}/time-entries/{entryId:guid}")]
    [ProducesResponseType(typeof(TimeEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTimeEntry(
        Guid taskId,
        Guid entryId,
        [FromBody] UpdateTimeEntryDto dto)
    {
        try
        {
            var entry = await _timeTrackingService.UpdateTimeEntryAsync(entryId, GetUserId(), dto, default);

            var result = new TimeEntryDto(
                entry.Id,
                entry.TaskId,
                entry.UserId,
                null,
                entry.Date,
                entry.Hours,
                entry.Description,
                entry.IsBillable,
                entry.StartTime,
                entry.EndTime,
                entry.IsTimerRunning,
                entry.CreatedAt
            );

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a time entry
    /// </summary>
    [HttpDelete("tasks/{taskId:guid}/time-entries/{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteTimeEntry(Guid taskId, Guid entryId)
    {
        try
        {
            await _timeTrackingService.DeleteTimeEntryAsync(entryId, GetUserId(), default);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get active timer for current user on a task
    /// </summary>
    [HttpGet("tasks/{taskId:guid}/timer/active")]
    [ProducesResponseType(typeof(TimeEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActiveTimer(Guid taskId)
    {
        try
        {
            var entry = await _timeTrackingService.GetActiveTimerAsync(taskId, GetUserId(), default);

            if (entry == null)
            {
                return NotFound(new { error = "No active timer" });
            }

            var result = new TimeEntryDto(
                entry.Id,
                entry.TaskId,
                entry.UserId,
                null,
                entry.Date,
                entry.Hours,
                entry.Description,
                entry.IsBillable,
                entry.StartTime,
                entry.EndTime,
                entry.IsTimerRunning,
                entry.CreatedAt
            );

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}

// Supporting DTOs
public record AttachmentDownloadResponse(string DownloadUrl, DateTime ExpiresAt);

// Time Entry DTOs
public record TimeEntriesResponseDto(
    List<TimeEntryDto> Entries,
    decimal TotalHours,
    Guid? ActiveTimerId);

public record TimeEntryDto(
    Guid Id,
    Guid TaskId,
    Guid UserId,
    string? UserName,
    DateOnly Date,
    decimal Hours,
    string? Description,
    bool IsBillable,
    DateTime? StartTime,
    DateTime? EndTime,
    bool IsTimerRunning,
    DateTime CreatedAt);

public record CreateTimeEntryDto(
    decimal Hours,
    DateOnly Date,
    string? Description = null,
    bool? IsBillable = true);
