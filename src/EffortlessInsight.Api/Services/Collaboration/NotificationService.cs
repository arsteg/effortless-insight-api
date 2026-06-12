using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Collaboration;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext dbContext,
        IEmailService emailService,
        ILogger<NotificationService> logger)
    {
        _dbContext = dbContext;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task NotifyTaskAssignedAsync(NoticeTask task, IEnumerable<Guid> assigneeIds, Guid assignedById)
    {
        try
        {
            var assignees = await _dbContext.Users
                .Where(u => assigneeIds.Contains(u.Id))
                .ToListAsync();

            var assigner = await _dbContext.Users.FindAsync(assignedById);
            var notice = await _dbContext.Notices.FindAsync(task.NoticeId);

            foreach (var assignee in assignees)
            {
                if (string.IsNullOrEmpty(assignee.Email)) continue;

                var subject = $"You've been assigned a task: {task.Title}";
                var body = BuildTaskAssignedEmail(task, notice!, assigner!, assignee);

                await _emailService.SendAsync(assignee.Email, subject, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task assigned notification for task {TaskId}", task.Id);
        }
    }

    public async Task NotifyTaskStatusChangedAsync(NoticeTask task, string previousStatus, Guid changedById)
    {
        try
        {
            var assigneeIds = await _dbContext.TaskAssignees
                .Where(ta => ta.TaskId == task.Id)
                .Select(ta => ta.UserId)
                .ToListAsync();

            var assignees = await _dbContext.Users
                .Where(u => assigneeIds.Contains(u.Id) && u.Id != changedById)
                .ToListAsync();

            var changer = await _dbContext.Users.FindAsync(changedById);
            var notice = await _dbContext.Notices.FindAsync(task.NoticeId);

            foreach (var assignee in assignees)
            {
                if (string.IsNullOrEmpty(assignee.Email)) continue;

                var subject = $"Task status changed: {task.Title}";
                var body = BuildTaskStatusChangedEmail(task, previousStatus, notice!, changer!, assignee);

                await _emailService.SendAsync(assignee.Email, subject, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task status changed notification for task {TaskId}", task.Id);
        }
    }

    public async Task NotifyTaskCompletedAsync(NoticeTask task, Guid completedById)
    {
        try
        {
            if (task.CreatedById == completedById) return;

            var creator = await _dbContext.Users.FindAsync(task.CreatedById);
            var completer = await _dbContext.Users.FindAsync(completedById);
            var notice = await _dbContext.Notices.FindAsync(task.NoticeId);

            if (creator == null || string.IsNullOrEmpty(creator.Email)) return;

            var subject = $"Task completed: {task.Title}";
            var body = BuildTaskCompletedEmail(task, notice!, completer!, creator);

            await _emailService.SendAsync(creator.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task completed notification for task {TaskId}", task.Id);
        }
    }

    public async Task NotifyDocumentRequestedAsync(DocumentRequest request)
    {
        try
        {
            var requestedFrom = await _dbContext.Users.FindAsync(request.RequestedFromId);
            var requestedBy = await _dbContext.Users.FindAsync(request.RequestedById);
            var notice = await _dbContext.Notices.FindAsync(request.NoticeId);

            if (requestedFrom == null || string.IsNullOrEmpty(requestedFrom.Email)) return;

            var subject = $"Document Request: {request.Title}";
            var body = BuildDocumentRequestedEmail(request, notice!, requestedBy!, requestedFrom);

            await _emailService.SendAsync(requestedFrom.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send document requested notification for request {RequestId}", request.Id);
        }
    }

    public async Task NotifyDocumentSubmittedAsync(DocumentRequest request)
    {
        try
        {
            var requester = await _dbContext.Users.FindAsync(request.RequestedById);
            var submitter = await _dbContext.Users.FindAsync(request.RequestedFromId);
            var notice = await _dbContext.Notices.FindAsync(request.NoticeId);

            if (requester == null || string.IsNullOrEmpty(requester.Email)) return;

            var subject = $"Document submitted: {request.Title}";
            var body = BuildDocumentSubmittedEmail(request, notice!, submitter!, requester);

            await _emailService.SendAsync(requester.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send document submitted notification for request {RequestId}", request.Id);
        }
    }

    public async Task NotifyDocumentReviewedAsync(DocumentRequest request, bool isApproved)
    {
        try
        {
            var submitter = await _dbContext.Users.FindAsync(request.RequestedFromId);
            var reviewer = request.ReviewedById.HasValue
                ? await _dbContext.Users.FindAsync(request.ReviewedById.Value)
                : null;
            var notice = await _dbContext.Notices.FindAsync(request.NoticeId);

            if (submitter == null || string.IsNullOrEmpty(submitter.Email)) return;

            var subject = isApproved
                ? $"Document approved: {request.Title}"
                : $"Document needs resubmission: {request.Title}";
            var body = BuildDocumentReviewedEmail(request, notice!, reviewer!, submitter, isApproved);

            await _emailService.SendAsync(submitter.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send document reviewed notification for request {RequestId}", request.Id);
        }
    }

    public async Task SendDocumentRequestReminderAsync(DocumentRequest request)
    {
        try
        {
            var requestedFrom = await _dbContext.Users.FindAsync(request.RequestedFromId);
            var requestedBy = await _dbContext.Users.FindAsync(request.RequestedById);
            var notice = await _dbContext.Notices.FindAsync(request.NoticeId);

            if (requestedFrom == null || string.IsNullOrEmpty(requestedFrom.Email)) return;

            var daysRemaining = request.DueDate.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
            var urgency = daysRemaining <= 0 ? "OVERDUE" : daysRemaining <= 3 ? "Urgent" : "";

            var subject = $"{urgency} Reminder: Document Request - {request.Title}".Trim();
            var body = BuildDocumentReminderEmail(request, notice!, requestedBy!, requestedFrom, daysRemaining);

            await _emailService.SendAsync(requestedFrom.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send document request reminder for request {RequestId}", request.Id);
        }
    }

    public async Task NotifyMentionAsync(Comment comment, IEnumerable<Guid> mentionedUserIds)
    {
        try
        {
            var mentionedUsers = await _dbContext.Users
                .Where(u => mentionedUserIds.Contains(u.Id) && u.Id != comment.UserId)
                .ToListAsync();

            var author = await _dbContext.Users.FindAsync(comment.UserId);
            var notice = await _dbContext.Notices.FindAsync(comment.NoticeId);

            foreach (var user in mentionedUsers)
            {
                if (string.IsNullOrEmpty(user.Email)) continue;

                var subject = $"{author?.Name ?? "Someone"} mentioned you in a comment";
                var body = BuildMentionEmail(comment, notice!, author!, user);

                await _emailService.SendAsync(user.Email, subject, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send mention notification for comment {CommentId}", comment.Id);
        }
    }

    public async Task NotifyCommentReplyAsync(Comment reply, Comment parentComment)
    {
        try
        {
            if (reply.UserId == parentComment.UserId) return;

            var parentAuthor = await _dbContext.Users.FindAsync(parentComment.UserId);
            var replyAuthor = await _dbContext.Users.FindAsync(reply.UserId);
            var notice = await _dbContext.Notices.FindAsync(reply.NoticeId);

            if (parentAuthor == null || string.IsNullOrEmpty(parentAuthor.Email)) return;

            var subject = $"{replyAuthor?.Name ?? "Someone"} replied to your comment";
            var body = BuildReplyEmail(reply, parentComment, notice!, replyAuthor!, parentAuthor);

            await _emailService.SendAsync(parentAuthor.Email, subject, body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reply notification for comment {CommentId}", reply.Id);
        }
    }

    public async Task NotifyOverdueTasksAsync()
    {
        try
        {
            var overdueTasks = await _dbContext.Tasks
                .Include(t => t.Assignees)
                    .ThenInclude(a => a.User)
                .Include(t => t.Notice)
                .Where(t => t.DueDate.HasValue
                    && t.DueDate.Value < DateTime.UtcNow
                    && t.Status != TaskStatusValues.Done
                    && t.Status != TaskStatusValues.Archived)
                .ToListAsync();

            foreach (var task in overdueTasks)
            {
                foreach (var assignee in task.Assignees)
                {
                    if (string.IsNullOrEmpty(assignee.User?.Email)) continue;

                    var subject = $"OVERDUE: Task \"{task.Title}\" is past due";
                    var body = BuildOverdueTaskEmail(task, assignee.User);

                    await _emailService.SendAsync(assignee.User.Email, subject, body);
                }
            }

            _logger.LogInformation("Sent overdue notifications for {Count} tasks", overdueTasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send overdue task notifications");
        }
    }

    public async Task NotifyOverdueDocumentRequestsAsync()
    {
        try
        {
            var overdueRequests = await _dbContext.DocumentRequests
                .Include(r => r.RequestedFrom)
                .Include(r => r.RequestedBy)
                .Include(r => r.Notice)
                .Where(r => r.DueDate < DateOnly.FromDateTime(DateTime.UtcNow)
                    && r.Status != DocumentRequestStatus.Fulfilled
                    && r.Status != DocumentRequestStatus.Cancelled)
                .ToListAsync();

            foreach (var request in overdueRequests)
            {
                if (string.IsNullOrEmpty(request.RequestedFrom?.Email)) continue;

                var subject = $"OVERDUE: Document request \"{request.Title}\" is past due";
                var body = BuildOverdueDocumentRequestEmail(request);

                await _emailService.SendAsync(request.RequestedFrom.Email, subject, body);
            }

            _logger.LogInformation("Sent overdue notifications for {Count} document requests", overdueRequests.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send overdue document request notifications");
        }
    }

    #region Email Templates

    private static string BuildTaskAssignedEmail(NoticeTask task, Notice notice, ApplicationUser assigner, ApplicationUser assignee)
    {
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>You've been assigned a task</h2>
    <p>Hi {assignee.Name},</p>
    <p>{assigner.Name} has assigned you to the following task:</p>
    <div style='background: #f5f5f5; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <h3 style='margin-top: 0;'>{task.Title}</h3>
        {(string.IsNullOrEmpty(task.Description) ? "" : $"<p>{task.Description}</p>")}
        <p><strong>Priority:</strong> {task.Priority}</p>
        {(task.DueDate.HasValue ? $"<p><strong>Due Date:</strong> {task.DueDate.Value:MMMM d, yyyy}</p>" : "")}
        <p><strong>Notice:</strong> {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildTaskStatusChangedEmail(NoticeTask task, string previousStatus, Notice notice, ApplicationUser changer, ApplicationUser assignee)
    {
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>Task status updated</h2>
    <p>Hi {assignee.Name},</p>
    <p>{changer.Name} has updated the status of the following task:</p>
    <div style='background: #f5f5f5; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <h3 style='margin-top: 0;'>{task.Title}</h3>
        <p><strong>Status changed:</strong> {previousStatus} → {task.Status}</p>
        <p><strong>Notice:</strong> {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildTaskCompletedEmail(NoticeTask task, Notice notice, ApplicationUser completer, ApplicationUser creator)
    {
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>Task completed</h2>
    <p>Hi {creator.Name},</p>
    <p>Good news! {completer.Name} has completed the following task:</p>
    <div style='background: #e8f5e9; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <h3 style='margin-top: 0;'>{task.Title}</h3>
        {(string.IsNullOrEmpty(task.CompletionNote) ? "" : $"<p><strong>Completion Note:</strong> {task.CompletionNote}</p>")}
        {(task.ActualHours.HasValue ? $"<p><strong>Actual Hours:</strong> {task.ActualHours.Value}</p>" : "")}
        <p><strong>Notice:</strong> {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildDocumentRequestedEmail(DocumentRequest request, Notice notice, ApplicationUser requester, ApplicationUser requestedFrom)
    {
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>Document Request</h2>
    <p>Hi {requestedFrom.Name},</p>
    <p>{requester.Name} has requested the following document from you:</p>
    <div style='background: #f5f5f5; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <h3 style='margin-top: 0;'>{request.Title}</h3>
        <p>{request.Description}</p>
        <p><strong>Due Date:</strong> {request.DueDate:MMMM d, yyyy}</p>
        <p><strong>Priority:</strong> {request.Priority}</p>
        {(request.AcceptedFormats?.Count > 0 ? $"<p><strong>Accepted Formats:</strong> {string.Join(", ", request.AcceptedFormats).ToUpper()}</p>" : "")}
        <p><strong>Notice:</strong> {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Please upload the requested document at your earliest convenience.</p>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildDocumentSubmittedEmail(DocumentRequest request, Notice notice, ApplicationUser submitter, ApplicationUser requester)
    {
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>Document Submitted</h2>
    <p>Hi {requester.Name},</p>
    <p>{submitter.Name} has submitted a document for your request:</p>
    <div style='background: #e3f2fd; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <h3 style='margin-top: 0;'>{request.Title}</h3>
        <p><strong>Notice:</strong> {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Please review the submitted document.</p>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildDocumentReviewedEmail(DocumentRequest request, Notice notice, ApplicationUser? reviewer, ApplicationUser submitter, bool isApproved)
    {
        var statusColor = isApproved ? "#e8f5e9" : "#fff3e0";
        var statusText = isApproved ? "Approved" : "Needs Resubmission";

        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>Document Review Result</h2>
    <p>Hi {submitter.Name},</p>
    <p>Your document submission has been reviewed:</p>
    <div style='background: {statusColor}; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <h3 style='margin-top: 0;'>{request.Title}</h3>
        <p><strong>Status:</strong> {statusText}</p>
        {(string.IsNullOrEmpty(request.ReviewNote) ? "" : $"<p><strong>Reviewer Note:</strong> {request.ReviewNote}</p>")}
        <p><strong>Notice:</strong> {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    {(isApproved ? "" : "<p>Please review the feedback and resubmit your document.</p>")}
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildDocumentReminderEmail(DocumentRequest request, Notice notice, ApplicationUser requester, ApplicationUser requestedFrom, int daysRemaining)
    {
        var urgencyColor = daysRemaining <= 0 ? "#ffebee" : daysRemaining <= 3 ? "#fff3e0" : "#f5f5f5";
        var urgencyText = daysRemaining <= 0 ? $"OVERDUE by {Math.Abs(daysRemaining)} days" : $"{daysRemaining} days remaining";

        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>Document Request Reminder</h2>
    <p>Hi {requestedFrom.Name},</p>
    <p>This is a reminder about a pending document request:</p>
    <div style='background: {urgencyColor}; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <h3 style='margin-top: 0;'>{request.Title}</h3>
        <p>{request.Description}</p>
        <p><strong>Due Date:</strong> {request.DueDate:MMMM d, yyyy} ({urgencyText})</p>
        <p><strong>Requested by:</strong> {requester.Name}</p>
        <p><strong>Notice:</strong> {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Please upload the requested document as soon as possible.</p>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildMentionEmail(Comment comment, Notice notice, ApplicationUser author, ApplicationUser mentionedUser)
    {
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>You were mentioned in a comment</h2>
    <p>Hi {mentionedUser.Name},</p>
    <p>{author.Name} mentioned you in a comment:</p>
    <div style='background: #f5f5f5; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <p style='font-style: italic;'>&quot;{comment.Content}&quot;</p>
        <p style='font-size: 12px; color: #666;'>Notice: {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildReplyEmail(Comment reply, Comment parentComment, Notice notice, ApplicationUser replyAuthor, ApplicationUser parentAuthor)
    {
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2>New reply to your comment</h2>
    <p>Hi {parentAuthor.Name},</p>
    <p>{replyAuthor.Name} replied to your comment:</p>
    <div style='background: #f5f5f5; padding: 16px; border-radius: 8px; margin: 16px 0;'>
        <p style='font-size: 12px; color: #666;'>Your comment:</p>
        <p style='font-style: italic;'>&quot;{parentComment.Content}&quot;</p>
        <hr style='border: 1px solid #ddd; margin: 12px 0;'>
        <p style='font-size: 12px; color: #666;'>Reply:</p>
        <p style='font-style: italic;'>&quot;{reply.Content}&quot;</p>
        <p style='font-size: 12px; color: #666; margin-top: 12px;'>Notice: {notice.NoticeType} - {notice.NoticeNumber}</p>
    </div>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildOverdueTaskEmail(NoticeTask task, ApplicationUser assignee)
    {
        var daysOverdue = (DateTime.UtcNow - task.DueDate!.Value).Days;
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2 style='color: #d32f2f;'>Overdue Task Alert</h2>
    <p>Hi {assignee.Name},</p>
    <p>The following task is overdue:</p>
    <div style='background: #ffebee; padding: 16px; border-radius: 8px; margin: 16px 0; border-left: 4px solid #d32f2f;'>
        <h3 style='margin-top: 0;'>{task.Title}</h3>
        <p><strong>Due Date:</strong> {task.DueDate.Value:MMMM d, yyyy}</p>
        <p><strong>Days Overdue:</strong> {daysOverdue}</p>
        <p><strong>Priority:</strong> {task.Priority}</p>
    </div>
    <p>Please complete this task as soon as possible.</p>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    private static string BuildOverdueDocumentRequestEmail(DocumentRequest request)
    {
        var daysOverdue = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - request.DueDate.DayNumber;
        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <h2 style='color: #d32f2f;'>Overdue Document Request Alert</h2>
    <p>Hi {request.RequestedFrom?.Name},</p>
    <p>The following document request is overdue:</p>
    <div style='background: #ffebee; padding: 16px; border-radius: 8px; margin: 16px 0; border-left: 4px solid #d32f2f;'>
        <h3 style='margin-top: 0;'>{request.Title}</h3>
        <p>{request.Description}</p>
        <p><strong>Due Date:</strong> {request.DueDate:MMMM d, yyyy}</p>
        <p><strong>Days Overdue:</strong> {daysOverdue}</p>
        <p><strong>Priority:</strong> {request.Priority}</p>
    </div>
    <p>Please submit the requested document as soon as possible.</p>
    <p>Best regards,<br>EffortlessInsight Team</p>
</body>
</html>";
    }

    #endregion
}
