using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.Collaboration;

/// <summary>
/// Service for sending collaboration-related notifications
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Notify users when they are assigned to a task
    /// </summary>
    Task NotifyTaskAssignedAsync(NoticeTask task, IEnumerable<Guid> assigneeIds, Guid assignedById);

    /// <summary>
    /// Notify assignees when a task status changes
    /// </summary>
    Task NotifyTaskStatusChangedAsync(NoticeTask task, string previousStatus, Guid changedById);

    /// <summary>
    /// Notify the task creator when a task is completed
    /// </summary>
    Task NotifyTaskCompletedAsync(NoticeTask task, Guid completedById);

    /// <summary>
    /// Notify a user when they receive a document request
    /// </summary>
    Task NotifyDocumentRequestedAsync(DocumentRequest request);

    /// <summary>
    /// Notify the requester when a document is submitted
    /// </summary>
    Task NotifyDocumentSubmittedAsync(DocumentRequest request);

    /// <summary>
    /// Notify the submitter when their document is reviewed
    /// </summary>
    Task NotifyDocumentReviewedAsync(DocumentRequest request, bool isApproved);

    /// <summary>
    /// Send a document request reminder
    /// </summary>
    Task SendDocumentRequestReminderAsync(DocumentRequest request);

    /// <summary>
    /// Notify users when they are mentioned in a comment
    /// </summary>
    Task NotifyMentionAsync(Comment comment, IEnumerable<Guid> mentionedUserIds);

    /// <summary>
    /// Notify the comment author when someone replies to their comment
    /// </summary>
    Task NotifyCommentReplyAsync(Comment reply, Comment parentComment);

    /// <summary>
    /// Notify overdue tasks (typically called from a background job)
    /// </summary>
    Task NotifyOverdueTasksAsync();

    /// <summary>
    /// Notify overdue document requests (typically called from a background job)
    /// </summary>
    Task NotifyOverdueDocumentRequestsAsync();
}
