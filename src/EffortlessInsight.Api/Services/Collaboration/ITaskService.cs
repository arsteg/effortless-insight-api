using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Collaboration;

public interface ITaskService
{
    // Task CRUD
    Task<TaskDetailDto> CreateTaskAsync(Guid noticeId, CreateTaskDto dto, Guid userId);
    Task<TaskDetailDto> GetTaskByIdAsync(Guid taskId, Guid userId);
    Task<TaskListResponseDto> GetTasksForNoticeAsync(Guid noticeId, Guid userId, string? status = null, Guid? assignee = null, string? priority = null, bool includeSubtasks = true);
    Task<TaskDetailDto> UpdateTaskAsync(Guid taskId, UpdateTaskDto dto, Guid userId);
    Task DeleteTaskAsync(Guid taskId, Guid userId);

    // My Tasks
    Task<MyTasksResponseDto> GetMyTasksAsync(Guid userId, string? status = null, string? priority = null, string? dueWithin = null, int page = 1, int pageSize = 20);

    // Task Templates
    Task<TaskTemplateDto> CreateTaskTemplateAsync(CreateTaskTemplateDto dto, Guid organizationId, Guid userId);
    Task<List<TaskTemplateDto>> GetTaskTemplatesAsync(Guid organizationId, string? noticeType = null);
    Task DeleteTaskTemplateAsync(Guid templateId, Guid organizationId);

    // Utility
    Task<bool> CanUserAccessTaskAsync(Guid taskId, Guid userId);
}
