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

    public async Task NotifyNoticeProcessingCompleteAsync(Notice notice)
    {
        try
        {
            var uploader = await _dbContext.Users.FindAsync(notice.UploadedById);
            if (uploader == null || string.IsNullOrEmpty(uploader.Email))
            {
                _logger.LogWarning("Cannot send processing complete notification - uploader not found or no email for notice {NoticeId}", notice.Id);
                return;
            }

            var riskLevel = notice.AiReport?.RiskLevel ?? "Unknown";
            var riskScore = notice.AiReport?.RiskScore ?? 0;

            var subject = $"Notice processed: {notice.NoticeType ?? "GST Notice"} - {notice.NoticeNumber ?? notice.FileName}";
            var body = BuildNoticeProcessingCompleteEmail(notice, uploader, riskLevel, riskScore);

            await _emailService.SendAsync(uploader.Email, subject, body);

            _logger.LogInformation("Sent processing complete notification for notice {NoticeId} to {Email}", notice.Id, uploader.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send processing complete notification for notice {NoticeId}", notice.Id);
        }
    }

    public async Task NotifyNoticeProcessingFailedAsync(Notice notice, string error, int attempts)
    {
        try
        {
            var uploader = await _dbContext.Users.FindAsync(notice.UploadedById);
            if (uploader == null || string.IsNullOrEmpty(uploader.Email))
            {
                _logger.LogWarning("Cannot send processing failed notification - uploader not found or no email for notice {NoticeId}", notice.Id);
                return;
            }

            var subject = $"Notice processing failed: {notice.FileName}";
            var body = BuildNoticeProcessingFailedEmail(notice, uploader, error, attempts);

            await _emailService.SendAsync(uploader.Email, subject, body);

            _logger.LogInformation("Sent processing failed notification for notice {NoticeId} to {Email}", notice.Id, uploader.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send processing failed notification for notice {NoticeId}", notice.Id);
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

    private static string BuildNoticeProcessingCompleteEmail(Notice notice, ApplicationUser uploader, string riskLevel, int riskScore)
    {
        var riskColor = riskLevel.ToLower() switch
        {
            "low" => "#4caf50",
            "medium" => "#ff9800",
            "high" => "#f44336",
            "critical" => "#d32f2f",
            _ => "#9e9e9e"
        };

        var deadline = notice.ResponseDeadline.HasValue
            ? $"<p><strong>Response Deadline:</strong> {notice.ResponseDeadline.Value:MMMM d, yyyy}</p>"
            : "";

        var totalAmount = (notice.TaxAmount ?? 0) + (notice.PenaltyAmount ?? 0) + (notice.InterestAmount ?? 0);
        var amountSection = totalAmount > 0
            ? $"<p><strong>Total Demand:</strong> ₹{totalAmount:N2}</p>"
            : "";

        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;'>
        <h1 style='color: white; margin: 0; font-size: 24px;'>EffortlessInsight</h1>
    </div>
    <div style='background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;'>
        <h2 style='color: #333; margin-top: 0;'>Notice Processing Complete</h2>
        <p>Hi {uploader.Name},</p>
        <p>Your notice has been successfully processed by our AI system. Here's a summary:</p>

        <div style='background: #f5f5f5; padding: 20px; border-radius: 8px; margin: 20px 0;'>
            <h3 style='margin-top: 0;'>{notice.NoticeType ?? "GST Notice"}</h3>
            <p><strong>Notice Number:</strong> {notice.NoticeNumber ?? "N/A"}</p>
            <p><strong>File:</strong> {notice.FileName}</p>
            {(notice.Gstin != null ? $"<p><strong>GSTIN:</strong> {notice.Gstin}</p>" : "")}
            {(notice.IssueDate.HasValue ? $"<p><strong>Issue Date:</strong> {notice.IssueDate.Value:MMMM d, yyyy}</p>" : "")}
            {deadline}
            {amountSection}
        </div>

        <div style='background: {riskColor}15; padding: 16px; border-radius: 8px; margin: 20px 0; border-left: 4px solid {riskColor};'>
            <p style='margin: 0;'><strong>Risk Assessment:</strong>
                <span style='color: {riskColor}; font-weight: bold; text-transform: uppercase;'>{riskLevel}</span>
                <span style='color: #666;'> (Score: {riskScore}/100)</span>
            </p>
        </div>

        <p>Log in to EffortlessInsight to view the full AI analysis, recommended actions, and required documents.</p>

        <div style='text-align: center; margin: 30px 0;'>
            <a href='#' style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;'>
                View Notice Details
            </a>
        </div>

        <hr style='border: none; border-top: 1px solid #e0e0e0; margin: 30px 0;'>
        <p style='color: #666; font-size: 14px;'>Best regards,<br>EffortlessInsight Team</p>
    </div>
</body>
</html>";
    }

    private static string BuildNoticeProcessingFailedEmail(Notice notice, ApplicationUser uploader, string error, int attempts)
    {
        var sanitizedError = error.Length > 200 ? error[..200] + "..." : error;

        return $@"
<!DOCTYPE html>
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;'>
        <h1 style='color: white; margin: 0; font-size: 24px;'>EffortlessInsight</h1>
    </div>
    <div style='background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;'>
        <h2 style='color: #d32f2f; margin-top: 0;'>Notice Processing Failed</h2>
        <p>Hi {uploader.Name},</p>
        <p>Unfortunately, we were unable to process your notice after {attempts} attempts. Our team has been notified and will investigate.</p>

        <div style='background: #ffebee; padding: 20px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #d32f2f;'>
            <h3 style='margin-top: 0; color: #d32f2f;'>Processing Error</h3>
            <p><strong>File:</strong> {notice.FileName}</p>
            <p><strong>Uploaded:</strong> {notice.CreatedAt:MMMM d, yyyy 'at' h:mm tt}</p>
            <p><strong>Error:</strong> <code style='background: #fff; padding: 2px 6px; border-radius: 4px;'>{sanitizedError}</code></p>
        </div>

        <h3>What you can do:</h3>
        <ul>
            <li><strong>Check the file quality:</strong> Ensure the document is clear and readable</li>
            <li><strong>Re-upload the notice:</strong> Try uploading the file again</li>
            <li><strong>Contact support:</strong> If the issue persists, reach out to our support team</li>
        </ul>

        <div style='background: #fff3e0; padding: 16px; border-radius: 8px; margin: 20px 0;'>
            <p style='margin: 0;'><strong>Tip:</strong> For best results, upload clear PDF files or high-quality images. Avoid blurry scans or heavily compressed images.</p>
        </div>

        <div style='text-align: center; margin: 30px 0;'>
            <a href='#' style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;'>
                Try Re-uploading
            </a>
        </div>

        <hr style='border: none; border-top: 1px solid #e0e0e0; margin: 30px 0;'>
        <p style='color: #666; font-size: 14px;'>
            If you need assistance, please contact our support team.<br><br>
            Best regards,<br>EffortlessInsight Team
        </p>
    </div>
</body>
</html>";
    }

    #endregion
}
