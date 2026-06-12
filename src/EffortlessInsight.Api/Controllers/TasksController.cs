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
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        ITaskService taskService,
        ICurrentOrganizationService orgService,
        ILogger<TasksController> logger)
    {
        _taskService = taskService;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
}
