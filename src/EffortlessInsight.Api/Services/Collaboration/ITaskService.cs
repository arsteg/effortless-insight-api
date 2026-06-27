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

    // Task Dependencies (GAP-TASK-001)
    Task<TaskDependencyDto> AddDependencyAsync(Guid taskId, Guid dependsOnTaskId, string type, Guid userId);
    Task RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, Guid userId);
    Task<List<TaskDependencyDto>> GetDependenciesAsync(Guid taskId, Guid userId);
    Task<List<TaskSummaryInfoDto>> GetBlockingTasksAsync(Guid taskId, Guid userId);

    // Task Reminders (GAP-TASK-002)
    Task<TaskReminderDto> CreateReminderAsync(Guid taskId, CreateTaskReminderDto dto, Guid userId);
    Task<List<TaskReminderDto>> GetRemindersAsync(Guid taskId, Guid userId);
    Task DeleteReminderAsync(Guid taskId, Guid reminderId, Guid userId);

    // Task Attachments (GAP-TASK-004)
    Task<TaskAttachmentDto> AddAttachmentAsync(Guid taskId, Stream fileStream, string fileName, string contentType, Guid userId);
    Task<List<TaskAttachmentDto>> GetAttachmentsAsync(Guid taskId, Guid userId);
    Task<string> GetAttachmentDownloadUrlAsync(Guid taskId, Guid attachmentId, Guid userId);
    Task DeleteAttachmentAsync(Guid taskId, Guid attachmentId, Guid userId);

    // Utility
    Task<bool> CanUserAccessTaskAsync(Guid taskId, Guid userId);
}
