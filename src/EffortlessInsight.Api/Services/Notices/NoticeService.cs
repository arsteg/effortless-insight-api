using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Storage;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Result of notice upload operation.
/// </summary>
public record NoticeUploadResult
{
    public bool Success { get; init; }
    public Guid? NoticeId { get; init; }
    public string? FileName { get; init; }
    public int FileSize { get; init; }
    public string? Status { get; init; }
    public string? ProcessingJobId { get; init; }
    public int EstimatedCompletionSeconds { get; init; }
    public DuplicateCheckResult? DuplicateWarning { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }

    public static NoticeUploadResult Succeeded(
        Guid noticeId,
        string fileName,
        int fileSize,
        string status,
        string? jobId,
        DuplicateCheckResult? duplicateWarning = null) => new()
    {
        Success = true,
        NoticeId = noticeId,
        FileName = fileName,
        FileSize = fileSize,
        Status = status,
        ProcessingJobId = jobId,
        EstimatedCompletionSeconds = 60,
        DuplicateWarning = duplicateWarning,
        CreatedAt = DateTime.UtcNow
    };

    public static NoticeUploadResult Failed(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Extended notice service interface with full upload and management operations.
/// </summary>
public interface INoticeServiceExtended : INoticeService
{
    /// <summary>
    /// Uploads a notice file with validation and duplicate detection.
    /// </summary>
    Task<NoticeUploadResult> UploadAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        Guid organizationId,
        Guid userId,
        string? gstin = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a pre-signed URL for direct upload.
    /// </summary>
    Task<PresignedUploadResult> GenerateUploadUrlAsync(
        string fileName,
        string contentType,
        long contentLength,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms a pre-signed upload and creates the notice record.
    /// </summary>
    Task<NoticeUploadResult> ConfirmUploadAsync(
        string s3Key,
        string fileName,
        string contentType,
        int fileSize,
        string fileHash,
        Guid organizationId,
        Guid userId,
        string? gstin = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a notice manually without file upload.
    /// </summary>
    Task<Notice> CreateManualNoticeAsync(
        CreateManualNoticeRequest request,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a notice by ID with organization validation.
    /// </summary>
    Task<Notice?> GetByIdAsync(Guid noticeId, Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets notices with filtering and pagination.
    /// </summary>
    Task<PagedResult<Notice>> GetListAsync(
        Guid organizationId,
        NoticeFilterDto filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates notice status with workflow validation.
    /// </summary>
    Task<Notice> UpdateStatusAsync(
        Guid noticeId,
        Guid organizationId,
        string newStatus,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a notice to a team member.
    /// </summary>
    Task<Notice> AssignAsync(
        Guid noticeId,
        Guid organizationId,
        Guid assigneeId,
        Guid assignedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft deletes a notice.
    /// </summary>
    Task DeleteAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for duplicate notices based on file hash.
    /// </summary>
    Task<DuplicateCheckResult> CheckDuplicateAsync(
        string fileHash,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the download URL for a notice file.
    /// </summary>
    Task<PresignedDownloadResult> GetDownloadUrlAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries AI processing for a failed notice.
    /// </summary>
    Task<string> RetryProcessingAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates notice details.
    /// </summary>
    Task<Notice> UpdateAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        UpdateNoticeDetailsDto update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets AI report for a notice.
    /// </summary>
    Task<NoticeAiReport?> GetReportAsync(Guid noticeId);

    #region Attachments

    /// <summary>
    /// Gets attachments for a notice.
    /// </summary>
    Task<List<Attachment>> GetAttachmentsAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads an attachment to a notice.
    /// </summary>
    Task<Attachment> AddAttachmentAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        Stream fileStream,
        string fileName,
        string? contentType,
        string? documentType,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an attachment.
    /// </summary>
    Task DeleteAttachmentAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets download URL for an attachment.
    /// </summary>
    Task<PresignedDownloadResult> GetAttachmentDownloadUrlAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a new version of an existing attachment.
    /// </summary>
    Task<Attachment> UploadNewAttachmentVersionAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        Stream fileStream,
        string fileName,
        string? contentType,
        string? versionNote,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets version history for an attachment.
    /// </summary>
    Task<AttachmentVersionHistoryResponse> GetAttachmentVersionHistoryAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Comments

    /// <summary>
    /// Gets comments for a notice.
    /// </summary>
    Task<List<Comment>> GetCommentsAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a comment to a notice.
    /// </summary>
    Task<Comment> AddCommentAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string content,
        bool isInternal = false,
        Guid? parentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a comment.
    /// </summary>
    Task DeleteCommentAsync(
        Guid commentId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Tasks

    /// <summary>
    /// Gets tasks for a notice.
    /// </summary>
    Task<List<NoticeTask>> GetTasksAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a task for a notice.
    /// </summary>
    Task<NoticeTask> CreateTaskAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CreateTaskDto task,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a task.
    /// </summary>
    Task<NoticeTask> UpdateTaskAsync(
        Guid taskId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        UpdateTaskDto update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a task.
    /// </summary>
    Task DeleteTaskAsync(
        Guid taskId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Responses

    /// <summary>
    /// Gets responses for a notice.
    /// </summary>
    Task<List<NoticeResponse>> GetResponsesAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest response for a notice.
    /// </summary>
    Task<NoticeResponse?> GetLatestResponseAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a response draft.
    /// </summary>
    Task<NoticeResponse> SaveResponseDraftAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string draftContent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a response for review.
    /// </summary>
    Task<NoticeResponse> SubmitForReviewAsync(
        Guid responseId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a response.
    /// </summary>
    Task<NoticeResponse> ApproveResponseAsync(
        Guid responseId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a response as submitted to the authority.
    /// </summary>
    Task<NoticeResponse> MarkAsSubmittedAsync(
        Guid responseId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string? submissionReference,
        string? submissionProofUrl,
        CancellationToken cancellationToken = default);

    #endregion

    #region Reminders

    /// <summary>
    /// Gets reminders for a notice.
    /// </summary>
    Task<List<DeadlineReminder>> GetRemindersAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a reminder for a notice deadline.
    /// </summary>
    Task<DeadlineReminder> CreateReminderAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CreateReminderDto reminder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a reminder.
    /// </summary>
    Task DeleteReminderAsync(
        Guid reminderId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Statistics

    /// <summary>
    /// Gets notice statistics for dashboard.
    /// </summary>
    Task<NoticeStatistics> GetStatisticsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Relationships

    /// <summary>
    /// Gets all relationships for a notice.
    /// </summary>
    Task<NoticeRelationshipsResponse> GetRelationshipsAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a relationship between two notices.
    /// </summary>
    Task<NoticeRelationshipDto> CreateRelationshipAsync(
        Guid sourceNoticeId,
        Guid organizationId,
        Guid userId,
        CreateNoticeRelationshipRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a notice relationship.
    /// </summary>
    Task DeleteRelationshipAsync(
        Guid relationshipId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// DTO for creating a reminder.
/// </summary>
public record CreateReminderDto(
    string ReminderType,
    DateTime RemindAt,
    int? DaysBefore = null);

/// <summary>
/// Notice statistics for dashboard.
/// </summary>
public record NoticeStatistics(
    Dictionary<string, int> ByStatus,
    Dictionary<string, int> ByPriority,
    int OverdueCount,
    int DueThisWeek,
    int DueThisMonth,
    decimal TotalDemandAmount,
    int TotalCount);

/// <summary>
/// DTO for updating a notice.
/// </summary>
public record UpdateNoticeDetailsDto(
    string? NoticeNumber = null,
    string? NoticeType = null,
    string? NoticeCategory = null,
    string? Gstin = null,
    DateOnly? IssueDate = null,
    DateOnly? ResponseDeadline = null,
    DateOnly? ExtendedDeadline = null,
    decimal? TaxAmount = null,
    decimal? PenaltyAmount = null,
    decimal? InterestAmount = null,
    DateOnly? PeriodFrom = null,
    DateOnly? PeriodTo = null,
    string? IssuingAuthority = null,
    string? Priority = null,
    List<string>? Tags = null);


/// <summary>
/// Implementation of the notice service.
/// </summary>
public class NoticeServiceImpl : INoticeServiceExtended
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageServiceExtended _storageService;
    private readonly IFileValidationService _validationService;
    private readonly INoticeWorkflowService _workflowService;
    private readonly IAuditService _auditService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly ILogger<NoticeServiceImpl> _logger;

    private const int MaxProcessingAttempts = 3;

    public NoticeServiceImpl(
        ApplicationDbContext db,
        IFileStorageServiceExtended storageService,
        IFileValidationService validationService,
        INoticeWorkflowService workflowService,
        IAuditService auditService,
        IBackgroundJobClient backgroundJobs,
        ILogger<NoticeServiceImpl> logger)
    {
        _db = db;
        _storageService = storageService;
        _validationService = validationService;
        _workflowService = workflowService;
        _auditService = auditService;
        _backgroundJobs = backgroundJobs;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NoticeUploadResult> UploadAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        Guid organizationId,
        Guid userId,
        string? gstin = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        // Validate file
        var validationResult = await _validationService.ValidateAsync(
            fileStream, fileName, contentType, cancellationToken);

        if (!validationResult.IsValid)
        {
            return NoticeUploadResult.Failed(
                validationResult.ErrorCode!,
                validationResult.ErrorMessage!);
        }

        // Reset stream position for upload
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        // Check for duplicates
        var duplicateCheck = await CheckDuplicateAsync(
            validationResult.FileHash!, organizationId, cancellationToken);

        // Validate GSTIN if provided
        Guid? gstinId = null;
        if (!string.IsNullOrEmpty(gstin))
        {
            var orgGstin = await _db.OrganizationGstins
                .FirstOrDefaultAsync(g =>
                    g.OrganizationId == organizationId &&
                    g.Gstin == gstin.ToUpperInvariant() &&
                    g.DeletedAt == null,
                    cancellationToken);

            if (orgGstin == null)
            {
                return NoticeUploadResult.Failed(
                    "INVALID_GSTIN",
                    $"GSTIN {gstin} not found in organization");
            }

            gstinId = orgGstin.Id;
        }

        // Create notice record
        var notice = new Notice
        {
            OrganizationId = organizationId,
            UploadedById = userId,
            FileName = validationResult.SanitizedFileName!,
            FileSize = (int)validationResult.FileSize,
            FileMimeType = validationResult.DetectedMimeType,
            FileHash = validationResult.FileHash,
            FileUrl = string.Empty, // Will be set after upload
            Status = _workflowService.GetInitialStatus(),
            ProcessingStatus = NoticeProcessingStatus.Queued,
            Priority = NoticePriority.Medium,
            Gstin = gstin?.ToUpperInvariant(),
            GstinId = gstinId,
            Tags = tags
        };

        // Upload to S3
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var s3Key = _storageService.GetNoticeKey(organizationId, notice.Id, extension);

        try
        {
            var uploadedKey = await _storageService.UploadAsync(
                fileStream, s3Key, validationResult.DetectedMimeType!);
            notice.FileUrl = uploadedKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload notice file to S3");
            return NoticeUploadResult.Failed(
                "UPLOAD_FAILED",
                "Failed to upload file to storage");
        }

        // Save notice to database
        _db.Notices.Add(notice);
        await _db.SaveChangesAsync(cancellationToken);

        // Queue AI processing
        var jobId = _backgroundJobs.Enqueue<INoticeProcessingJob>(
            job => job.ProcessAsync(notice.Id, cancellationToken));

        // Update notice with job ID (optional tracking)
        notice.Metadata ??= [];
        notice.Metadata["processing_job_id"] = jobId;
        await _db.SaveChangesAsync(cancellationToken);

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.uploaded",
            EntityType = "Notice",
            EntityId = notice.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new Dictionary<string, object>
            {
                ["file_name"] = notice.FileName,
                ["file_size"] = notice.FileSize,
                ["gstin"] = gstin ?? "not_specified"
            }
        });

        _logger.LogInformation(
            "Notice {NoticeId} uploaded by user {UserId} in org {OrgId}, file: {FileName}",
            notice.Id, userId, organizationId, notice.FileName);

        return NoticeUploadResult.Succeeded(
            notice.Id,
            notice.FileName,
            notice.FileSize,
            notice.Status,
            jobId,
            duplicateCheck.IsPotentialDuplicate ? duplicateCheck : null);
    }

    /// <inheritdoc />
    public async Task<PresignedUploadResult> GenerateUploadUrlAsync(
        string fileName,
        string contentType,
        long contentLength,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Validate metadata (without content)
        var validationResult = _validationService.ValidateMetadata(fileName, contentType, contentLength);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException(validationResult.ErrorMessage);
        }

        // Generate a temporary notice ID for the S3 key
        var tempNoticeId = Guid.NewGuid();

        return await _storageService.GenerateUploadUrlAsync(
            organizationId,
            tempNoticeId,
            fileName,
            contentType,
            contentLength,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NoticeUploadResult> ConfirmUploadAsync(
        string s3Key,
        string fileName,
        string contentType,
        int fileSize,
        string fileHash,
        Guid organizationId,
        Guid userId,
        string? gstin = null,
        List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        // Verify the file exists in S3
        var uploadExists = await _storageService.ConfirmUploadAsync(s3Key, cancellationToken);
        if (!uploadExists)
        {
            return NoticeUploadResult.Failed(
                "UPLOAD_NOT_FOUND",
                "Upload not found or expired");
        }

        // Check for duplicates
        var duplicateCheck = await CheckDuplicateAsync(fileHash, organizationId, cancellationToken);

        // Validate GSTIN if provided
        Guid? gstinId = null;
        if (!string.IsNullOrEmpty(gstin))
        {
            var orgGstin = await _db.OrganizationGstins
                .FirstOrDefaultAsync(g =>
                    g.OrganizationId == organizationId &&
                    g.Gstin == gstin.ToUpperInvariant() &&
                    g.DeletedAt == null,
                    cancellationToken);

            if (orgGstin == null)
            {
                return NoticeUploadResult.Failed(
                    "INVALID_GSTIN",
                    $"GSTIN {gstin} not found in organization");
            }

            gstinId = orgGstin.Id;
        }

        // Extract notice ID from S3 key
        // Key format: {org_id}/notices/{notice_id}/original.{ext}
        var keyParts = s3Key.Split('/');
        var noticeIdStr = keyParts.Length >= 3 ? keyParts[2] : null;
        var noticeId = Guid.TryParse(noticeIdStr, out var parsed) ? parsed : Guid.NewGuid();

        // Create notice record
        var notice = new Notice
        {
            Id = noticeId,
            OrganizationId = organizationId,
            UploadedById = userId,
            FileName = _validationService.SanitizeFileName(fileName),
            FileSize = fileSize,
            FileMimeType = contentType,
            FileHash = fileHash,
            FileUrl = s3Key,
            Status = _workflowService.GetInitialStatus(),
            ProcessingStatus = NoticeProcessingStatus.Queued,
            Priority = NoticePriority.Medium,
            Gstin = gstin?.ToUpperInvariant(),
            GstinId = gstinId,
            Tags = tags
        };

        _db.Notices.Add(notice);
        await _db.SaveChangesAsync(cancellationToken);

        // Queue AI processing
        var jobId = _backgroundJobs.Enqueue<INoticeProcessingJob>(
            job => job.ProcessAsync(notice.Id, cancellationToken));

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.uploaded",
            EntityType = "Notice",
            EntityId = notice.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new Dictionary<string, object>
            {
                ["file_name"] = notice.FileName,
                ["file_size"] = notice.FileSize,
                ["upload_method"] = "presigned"
            }
        });

        return NoticeUploadResult.Succeeded(
            notice.Id,
            notice.FileName,
            notice.FileSize,
            notice.Status,
            jobId,
            duplicateCheck.IsPotentialDuplicate ? duplicateCheck : null);
    }

    /// <inheritdoc />
    public async Task<Notice> CreateManualNoticeAsync(
        CreateManualNoticeRequest request,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Validate GSTIN
        var orgGstin = await _db.OrganizationGstins
            .FirstOrDefaultAsync(g =>
                g.OrganizationId == organizationId &&
                g.Gstin == request.Gstin.ToUpperInvariant() &&
                g.DeletedAt == null,
                cancellationToken);

        if (orgGstin == null)
        {
            throw new InvalidOperationException($"GSTIN {request.Gstin} not found in organization");
        }

        // Calculate priority if not provided
        var priority = request.Priority ?? _workflowService.CalculatePriority(
            request.NoticeType,
            request.NoticeCategory,
            request.ResponseDeadline,
            (request.TaxAmount ?? 0) + (request.PenaltyAmount ?? 0) + (request.InterestAmount ?? 0));

        // Create notice record
        var noticeId = Guid.NewGuid();
        var notice = new Notice
        {
            Id = noticeId,
            OrganizationId = organizationId,
            UploadedById = userId,
            Gstin = request.Gstin.ToUpperInvariant(),
            GstinId = orgGstin.Id,
            NoticeNumber = request.NoticeNumber,
            NoticeType = request.NoticeType,
            NoticeCategory = request.NoticeCategory,
            NoticeSubCategory = request.NoticeSubCategory,
            IssueDate = request.IssueDate,
            ResponseDeadline = request.ResponseDeadline,
            HearingDate = request.HearingDate,
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            TaxAmount = request.TaxAmount,
            PenaltyAmount = request.PenaltyAmount,
            InterestAmount = request.InterestAmount,
            IssuingAuthority = request.IssuingAuthority,
            Notes = request.Subject,
            Priority = priority,
            Tags = request.Tags,
            AssignedToId = request.AssignedToId,
            AssignedById = request.AssignedToId.HasValue ? userId : null,
            AssignedAt = request.AssignedToId.HasValue ? DateTime.UtcNow : null,
            // Manual entry - placeholder file values
            FileName = $"manual_entry_{noticeId:N}.txt",
            FileUrl = $"manual://{noticeId}",
            FileSize = 0,
            // Manual entry - no AI processing needed
            Status = NoticeStatus.Analyzed,
            ProcessingStatus = NoticeProcessingStatus.Completed,
            IsManualEntry = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Notices.Add(notice);
        await _db.SaveChangesAsync(cancellationToken);

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.created_manual",
            EntityType = "Notice",
            EntityId = notice.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new Dictionary<string, object>
            {
                ["gstin"] = notice.Gstin ?? "",
                ["notice_number"] = notice.NoticeNumber ?? "",
                ["notice_type"] = notice.NoticeType ?? "",
                ["is_manual_entry"] = true
            }
        });

        _logger.LogInformation(
            "Manual notice created: {NoticeId} for org {OrganizationId} by user {UserId}",
            notice.Id, organizationId, userId);

        return notice;
    }

    /// <inheritdoc />
    public async Task<DuplicateCheckResult> CheckDuplicateAsync(
        string fileHash,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(fileHash))
        {
            return DuplicateCheckResult.NoDuplicate();
        }

        var existingNotice = await _db.Notices
            .Where(n =>
                n.OrganizationId == organizationId &&
                n.FileHash == fileHash &&
                n.DeletedAt == null)
            .Select(n => new { n.Id, n.NoticeNumber, n.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (existingNotice == null)
        {
            return DuplicateCheckResult.NoDuplicate();
        }

        return DuplicateCheckResult.Duplicate(
            existingNotice.Id,
            existingNotice.NoticeNumber,
            existingNotice.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<Notice?> GetByIdAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Notices
            .Include(n => n.AiReport)
            .Include(n => n.UploadedBy)
            .Include(n => n.AssignedTo)
            .Include(n => n.GstinNavigation)
            .Where(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<Notice>> GetListAsync(
        Guid organizationId,
        NoticeFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Notices
            .Include(n => n.AiReport)
            .Include(n => n.AssignedTo)
            .Where(n => n.OrganizationId == organizationId && n.DeletedAt == null);

        // Apply filters
        if (!string.IsNullOrEmpty(filter.Status))
        {
            var statuses = filter.Status.Split(',', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(n => statuses.Contains(n.Status));
        }

        if (!string.IsNullOrEmpty(filter.Priority))
        {
            var priorities = filter.Priority.Split(',', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(n => priorities.Contains(n.Priority));
        }

        if (!string.IsNullOrEmpty(filter.NoticeType))
        {
            query = query.Where(n => n.NoticeType == filter.NoticeType);
        }

        if (!string.IsNullOrEmpty(filter.Gstin))
        {
            query = query.Where(n => n.Gstin == filter.Gstin.ToUpperInvariant());
        }

        if (filter.DeadlineFrom.HasValue)
        {
            query = query.Where(n => n.ResponseDeadline >= filter.DeadlineFrom.Value);
        }

        if (filter.DeadlineTo.HasValue)
        {
            query = query.Where(n => n.ResponseDeadline <= filter.DeadlineTo.Value);
        }

        // Full-text search with WebSearchToTsQuery for advanced query syntax
        // Supports: "exact phrase", -excluded, OR operator, prefix:*
        if (!string.IsNullOrEmpty(filter.Search))
        {
            var searchTerm = filter.Search.Trim();

            // Build comprehensive search vector from multiple fields
            // Weighted: A (highest) = notice identifiers, B = metadata, C = content, D = tags
            query = query.Where(n =>
                EF.Functions.ToTsVector("english",
                    // Primary identifiers (high relevance)
                    (n.NoticeNumber ?? "") + " " +
                    (n.Gstin ?? "") + " " +
                    // Notice metadata
                    (n.NoticeType ?? "") + " " +
                    (n.NoticeCategory ?? "") + " " +
                    (n.NoticeSubCategory ?? "") + " " +
                    (n.IssuingAuthority ?? "") + " " +
                    (n.IssuingOfficer ?? "") + " " +
                    (n.Jurisdiction ?? "") + " " +
                    // Content
                    (n.OcrText ?? "") + " " +
                    (n.Notes ?? "") + " " +
                    // Tags (joined as space-separated)
                    (n.Tags != null ? string.Join(" ", n.Tags) : ""))
                .Matches(EF.Functions.WebSearchToTsQuery("english", searchTerm)));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply sorting
        query = filter.SortBy?.ToLowerInvariant() switch
        {
            "deadline" => filter.SortDesc
                ? query.OrderByDescending(n => n.ResponseDeadline)
                : query.OrderBy(n => n.ResponseDeadline),
            "amount" => filter.SortDesc
                ? query.OrderByDescending(n => n.TaxAmount)
                : query.OrderBy(n => n.TaxAmount),
            "priority" => filter.SortDesc
                ? query.OrderByDescending(n => n.Priority)
                : query.OrderBy(n => n.Priority),
            "status" => filter.SortDesc
                ? query.OrderByDescending(n => n.Status)
                : query.OrderBy(n => n.Status),
            _ => filter.SortDesc
                ? query.OrderByDescending(n => n.CreatedAt)
                : query.OrderBy(n => n.CreatedAt)
        };

        // Apply pagination
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var items = await query
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResult<Notice>(items, totalCount, page, pageSize, totalPages);
    }

    /// <inheritdoc />
    public async Task<Notice> UpdateStatusAsync(
        Guid noticeId,
        Guid organizationId,
        string newStatus,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .FirstOrDefaultAsync(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Notice not found");

        var oldStatus = notice.Status;

        // Validate transition
        var transitionResult = _workflowService.ValidateTransition(notice.Status, newStatus);
        if (!transitionResult.IsAllowed)
        {
            throw new InvalidOperationException(transitionResult.ErrorMessage);
        }

        // Check if reason is required
        if (transitionResult.RequiresReason && string.IsNullOrEmpty(reason))
        {
            throw new InvalidOperationException(
                $"Reason is required for transition from '{oldStatus}' to '{newStatus}'");
        }

        // Update status
        notice.Status = newStatus;
        notice.UpdatedAt = DateTime.UtcNow;

        // Store reason in metadata if provided
        if (!string.IsNullOrEmpty(reason))
        {
            notice.Metadata ??= [];
            notice.Metadata["last_status_reason"] = reason;
            notice.Metadata["last_status_changed_by"] = userId;
            notice.Metadata["last_status_changed_at"] = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.status_changed",
            EntityType = "Notice",
            EntityId = noticeId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new Dictionary<string, object> { ["status"] = oldStatus },
            NewValues = new Dictionary<string, object>
            {
                ["status"] = newStatus,
                ["reason"] = reason ?? "not_provided"
            }
        });

        _logger.LogInformation(
            "Notice {NoticeId} status changed from {OldStatus} to {NewStatus} by user {UserId}",
            noticeId, oldStatus, newStatus, userId);

        return notice;
    }

    /// <inheritdoc />
    public async Task<Notice> AssignAsync(
        Guid noticeId,
        Guid organizationId,
        Guid assigneeId,
        Guid assignedByUserId,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .FirstOrDefaultAsync(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Notice not found");

        // Verify assignee is a member of the organization
        var isMember = await _db.OrganizationMembers
            .AnyAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == assigneeId &&
                m.Status == "active" &&
                m.DeletedAt == null &&
                (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow),
                cancellationToken);

        if (!isMember)
        {
            throw new InvalidOperationException("Assignee is not an active member of this organization");
        }

        var previousAssigneeId = notice.AssignedToId;

        // Update assignment
        notice.AssignedToId = assigneeId;
        notice.AssignedById = assignedByUserId;
        notice.AssignedAt = DateTime.UtcNow;
        notice.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.assigned",
            EntityType = "Notice",
            EntityId = noticeId,
            UserId = assignedByUserId,
            OrganizationId = organizationId,
            OldValues = previousAssigneeId.HasValue
                ? new Dictionary<string, object> { ["assigned_to"] = previousAssigneeId.Value }
                : null,
            NewValues = new Dictionary<string, object> { ["assigned_to"] = assigneeId }
        });

        // TODO: Send notification to assignee
        // _backgroundJobs.Enqueue<INotificationJob>(job => job.SendAssignmentNotification(noticeId, assigneeId));

        _logger.LogInformation(
            "Notice {NoticeId} assigned to user {AssigneeId} by user {AssignedById}",
            noticeId, assigneeId, assignedByUserId);

        return notice;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .FirstOrDefaultAsync(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Notice not found");

        // Soft delete
        notice.DeletedAt = DateTime.UtcNow;
        notice.DeletedById = userId;
        notice.DeletionReason = reason;
        notice.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.deleted",
            EntityType = "Notice",
            EntityId = noticeId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new Dictionary<string, object>
            {
                ["notice_number"] = notice.NoticeNumber ?? "unknown",
                ["status"] = notice.Status
            },
            NewValues = new Dictionary<string, object>
            {
                ["reason"] = reason ?? "not_provided"
            }
        });

        _logger.LogInformation(
            "Notice {NoticeId} deleted by user {UserId}, reason: {Reason}",
            noticeId, userId, reason ?? "none");
    }

    /// <inheritdoc />
    public async Task<PresignedDownloadResult> GetDownloadUrlAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .Where(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.DeletedAt == null)
            .Select(n => new { n.FileUrl, n.FileName })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Notice not found");

        return await _storageService.GenerateDownloadUrlAsync(
            notice.FileUrl,
            notice.FileName,
            15, // 15-minute expiry
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> RetryProcessingAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .FirstOrDefaultAsync(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Notice not found");

        if (notice.ProcessingAttempts >= MaxProcessingAttempts)
        {
            throw new InvalidOperationException(
                $"Maximum processing attempts ({MaxProcessingAttempts}) exceeded");
        }

        // Reset processing status
        notice.ProcessingStatus = NoticeProcessingStatus.Queued;
        notice.ProcessingError = null;
        notice.ProcessingStartedAt = null;
        notice.ProcessingCompletedAt = null;
        notice.Status = NoticeStatus.Processing;
        notice.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Queue processing job
        var jobId = _backgroundJobs.Enqueue<INoticeProcessingJob>(
            job => job.ProcessAsync(notice.Id, cancellationToken));

        return jobId;
    }

    #region INoticeService implementation (legacy interface)

    public Task<Notice> CreateAsync(CreateNoticeDto dto, Guid userId)
    {
        throw new NotImplementedException("Use UploadAsync instead");
    }

    Task<Notice?> INoticeService.GetByIdAsync(Guid id)
    {
        // This method doesn't have org context - should not be used directly
        return _db.Notices
            .Include(n => n.AiReport)
            .FirstOrDefaultAsync(n => n.Id == id && n.DeletedAt == null);
    }

    public Task<PagedResult<Notice>> GetByOrganizationAsync(Guid organizationId, NoticeFilterDto filter)
    {
        return GetListAsync(organizationId, filter);
    }

    public Task<Notice> UpdateAsync(Guid id, UpdateNoticeDto dto)
    {
        throw new NotImplementedException("Use specific update methods instead");
    }

    Task INoticeService.DeleteAsync(Guid id)
    {
        throw new NotImplementedException("Use DeleteAsync with organization context");
    }

    public async Task<NoticeAiReport?> GetReportAsync(Guid noticeId)
    {
        return await _db.NoticeAiReports
            .FirstOrDefaultAsync(r => r.NoticeId == noticeId);
    }

    public Task TriggerAiProcessingAsync(Guid noticeId)
    {
        _backgroundJobs.Enqueue<INoticeProcessingJob>(
            job => job.ProcessAsync(noticeId, CancellationToken.None));
        return Task.CompletedTask;
    }

    #endregion

    #region Update Notice

    /// <inheritdoc />
    public async Task<Notice> UpdateAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        UpdateNoticeDetailsDto update,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .FirstOrDefaultAsync(n =>
                n.Id == noticeId &&
                n.OrganizationId == organizationId &&
                n.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Notice not found");

        var oldValues = new Dictionary<string, object>();
        var newValues = new Dictionary<string, object>();

        // Update fields if provided
        if (update.NoticeNumber != null && update.NoticeNumber != notice.NoticeNumber)
        {
            oldValues["notice_number"] = notice.NoticeNumber ?? "";
            notice.NoticeNumber = update.NoticeNumber;
            newValues["notice_number"] = update.NoticeNumber;
        }

        if (update.NoticeType != null && update.NoticeType != notice.NoticeType)
        {
            oldValues["notice_type"] = notice.NoticeType ?? "";
            notice.NoticeType = update.NoticeType;
            newValues["notice_type"] = update.NoticeType;
        }

        if (update.NoticeCategory != null && update.NoticeCategory != notice.NoticeCategory)
        {
            oldValues["notice_category"] = notice.NoticeCategory ?? "";
            notice.NoticeCategory = update.NoticeCategory;
            newValues["notice_category"] = update.NoticeCategory;
        }

        if (update.Gstin != null && update.Gstin != notice.Gstin)
        {
            oldValues["gstin"] = notice.Gstin ?? "";
            notice.Gstin = update.Gstin;
            newValues["gstin"] = update.Gstin;
        }

        if (update.IssueDate.HasValue && update.IssueDate != notice.IssueDate)
        {
            oldValues["issue_date"] = notice.IssueDate?.ToString() ?? "";
            notice.IssueDate = update.IssueDate;
            newValues["issue_date"] = update.IssueDate.Value.ToString();
        }

        if (update.ResponseDeadline.HasValue && update.ResponseDeadline != notice.ResponseDeadline)
        {
            oldValues["response_deadline"] = notice.ResponseDeadline?.ToString() ?? "";
            notice.ResponseDeadline = update.ResponseDeadline;
            newValues["response_deadline"] = update.ResponseDeadline.Value.ToString();

            // Recalculate priority
            notice.Priority = _workflowService.CalculatePriority(
                notice.NoticeType, notice.NoticeCategory, notice.ResponseDeadline,
                notice.TaxAmount + notice.PenaltyAmount + notice.InterestAmount);
        }

        if (update.ExtendedDeadline.HasValue)
        {
            oldValues["extended_deadline"] = notice.ExtendedDeadline?.ToString() ?? "";
            notice.ExtendedDeadline = update.ExtendedDeadline;
            newValues["extended_deadline"] = update.ExtendedDeadline.Value.ToString();
        }

        if (update.TaxAmount.HasValue && update.TaxAmount != notice.TaxAmount)
        {
            oldValues["tax_amount"] = notice.TaxAmount;
            notice.TaxAmount = update.TaxAmount.Value;
            newValues["tax_amount"] = update.TaxAmount.Value;
        }

        if (update.PenaltyAmount.HasValue && update.PenaltyAmount != notice.PenaltyAmount)
        {
            oldValues["penalty_amount"] = notice.PenaltyAmount;
            notice.PenaltyAmount = update.PenaltyAmount.Value;
            newValues["penalty_amount"] = update.PenaltyAmount.Value;
        }

        if (update.InterestAmount.HasValue && update.InterestAmount != notice.InterestAmount)
        {
            oldValues["interest_amount"] = notice.InterestAmount;
            notice.InterestAmount = update.InterestAmount.Value;
            newValues["interest_amount"] = update.InterestAmount.Value;
        }

        if (update.PeriodFrom.HasValue && update.PeriodFrom != notice.PeriodFrom)
        {
            oldValues["period_from"] = notice.PeriodFrom?.ToString() ?? "";
            notice.PeriodFrom = update.PeriodFrom;
            newValues["period_from"] = update.PeriodFrom.Value.ToString();
        }

        if (update.PeriodTo.HasValue && update.PeriodTo != notice.PeriodTo)
        {
            oldValues["period_to"] = notice.PeriodTo?.ToString() ?? "";
            notice.PeriodTo = update.PeriodTo;
            newValues["period_to"] = update.PeriodTo.Value.ToString();
        }

        if (update.IssuingAuthority != null && update.IssuingAuthority != notice.IssuingAuthority)
        {
            oldValues["issuing_authority"] = notice.IssuingAuthority ?? "";
            notice.IssuingAuthority = update.IssuingAuthority;
            newValues["issuing_authority"] = update.IssuingAuthority;
        }

        if (update.Priority != null && update.Priority != notice.Priority)
        {
            if (!NoticePriority.IsValid(update.Priority))
            {
                throw new InvalidOperationException($"Invalid priority: {update.Priority}");
            }
            oldValues["priority"] = notice.Priority ?? "";
            notice.Priority = update.Priority;
            newValues["priority"] = update.Priority;
        }

        if (update.Tags != null)
        {
            oldValues["tags"] = string.Join(",", notice.Tags ?? []);
            notice.Tags = update.Tags;
            newValues["tags"] = string.Join(",", update.Tags);
        }

        notice.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Audit log if there were changes
        if (newValues.Count > 0)
        {
            await _auditService.LogAsync(new AuditLogEntry
            {
                Action = "notice.updated",
                EntityType = "Notice",
                EntityId = noticeId,
                UserId = userId,
                OrganizationId = organizationId,
                OldValues = oldValues,
                NewValues = newValues
            });
        }

        _logger.LogInformation("Notice {NoticeId} updated by user {UserId}", noticeId, userId);

        return notice;
    }

    #endregion

    #region Attachments

    /// <inheritdoc />
    public async Task<List<Attachment>> GetAttachmentsAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        return await _db.Attachments
            .Include(a => a.UploadedBy)
            .Where(a => a.NoticeId == noticeId && a.DeletedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Attachment> AddAttachmentAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        Stream fileStream,
        string fileName,
        string? contentType,
        string? documentType,
        string? description,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        // Validate file
        var validation = await _validationService.ValidateAsync(
            fileStream, fileName, contentType, cancellationToken);

        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.ErrorMessage ?? "Invalid file");
        }

        // Generate attachment ID and upload to S3
        var attachmentId = Guid.NewGuid();

        // Reset stream position if possible
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        var fileUrl = await _storageService.UploadAttachmentAsync(
            organizationId,
            noticeId,
            attachmentId,
            fileStream,
            validation.SanitizedFileName!,
            validation.DetectedMimeType ?? contentType ?? "application/octet-stream",
            cancellationToken);

        var attachment = new Attachment
        {
            Id = attachmentId,
            NoticeId = noticeId,
            UploadedById = userId,
            FileName = validation.SanitizedFileName!,
            FileUrl = fileUrl,
            FileSize = (int)validation.FileSize,
            FileType = validation.DetectedMimeType,
            FileHash = validation.FileHash,
            DocumentType = documentType,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Attachment {AttachmentId} added to notice {NoticeId} by user {UserId}",
            attachmentId, noticeId, userId);

        return attachment;
    }

    /// <inheritdoc />
    public async Task DeleteAttachmentAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _db.Attachments
            .Include(a => a.Notice)
            .FirstOrDefaultAsync(a =>
                a.Id == attachmentId &&
                a.NoticeId == noticeId &&
                a.Notice!.OrganizationId == organizationId &&
                a.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Attachment not found");

        // Soft delete
        attachment.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Attachment {AttachmentId} deleted from notice {NoticeId} by user {UserId}",
            attachmentId, noticeId, userId);
    }

    /// <inheritdoc />
    public async Task<PresignedDownloadResult> GetAttachmentDownloadUrlAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _db.Attachments
            .Include(a => a.Notice)
            .FirstOrDefaultAsync(a =>
                a.Id == attachmentId &&
                a.NoticeId == noticeId &&
                a.Notice!.OrganizationId == organizationId &&
                a.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Attachment not found");

        return await _storageService.GenerateDownloadUrlAsync(
            attachment.FileUrl,
            attachment.FileName,
            15,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Attachment> UploadNewAttachmentVersionAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        Stream fileStream,
        string fileName,
        string? contentType,
        string? versionNote,
        CancellationToken cancellationToken = default)
    {
        // Get existing attachment
        var existingAttachment = await _db.Attachments
            .Include(a => a.Notice)
            .FirstOrDefaultAsync(a =>
                a.Id == attachmentId &&
                a.NoticeId == noticeId &&
                a.Notice!.OrganizationId == organizationId &&
                a.IsCurrentVersion &&
                a.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Attachment not found or is not the current version");

        // Determine original attachment ID (for grouping versions)
        var originalAttachmentId = existingAttachment.OriginalAttachmentId ?? existingAttachment.Id;

        // Upload new file
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        var fileBytes = memoryStream.ToArray();

        var storagePath = $"attachments/{organizationId}/{noticeId}/{Guid.NewGuid()}/{fileName}";
        var fileUrl = await _storageService.UploadAsync(
            new MemoryStream(fileBytes),
            storagePath,
            contentType ?? "application/octet-stream");

        // Compute file hash
        var fileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileBytes)).ToLowerInvariant();

        // Mark existing attachment as not current
        existingAttachment.IsCurrentVersion = false;
        existingAttachment.UpdatedAt = DateTime.UtcNow;

        // Create new version
        var newVersion = new Attachment
        {
            Id = Guid.NewGuid(),
            NoticeId = noticeId,
            ResponseId = existingAttachment.ResponseId,
            TaskId = existingAttachment.TaskId,
            UploadedById = userId,
            FileName = fileName,
            FileUrl = fileUrl,
            FileSize = fileBytes.Length,
            FileType = contentType ?? existingAttachment.FileType,
            DocumentType = existingAttachment.DocumentType,
            Description = existingAttachment.Description,
            FileHash = fileHash,
            Version = existingAttachment.Version + 1,
            PreviousVersionId = existingAttachment.Id,
            IsCurrentVersion = true,
            VersionNote = versionNote,
            OriginalAttachmentId = originalAttachmentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Attachments.Add(newVersion);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created version {Version} of attachment {OriginalId} for notice {NoticeId} by user {UserId}",
            newVersion.Version, originalAttachmentId, noticeId, userId);

        return newVersion;
    }

    /// <inheritdoc />
    public async Task<AttachmentVersionHistoryResponse> GetAttachmentVersionHistoryAsync(
        Guid attachmentId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // First get the attachment to determine the original attachment ID
        var attachment = await _db.Attachments
            .Include(a => a.Notice)
            .FirstOrDefaultAsync(a =>
                a.Id == attachmentId &&
                a.NoticeId == noticeId &&
                a.Notice!.OrganizationId == organizationId &&
                a.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Attachment not found");

        // Get the original attachment ID (root of version chain)
        var originalId = attachment.OriginalAttachmentId ?? attachment.Id;

        // Get all versions (original + all that reference it)
        var allVersions = await _db.Attachments
            .Include(a => a.UploadedBy)
            .Where(a =>
                a.NoticeId == noticeId &&
                a.DeletedAt == null &&
                (a.Id == originalId || a.OriginalAttachmentId == originalId))
            .OrderByDescending(a => a.Version)
            .ToListAsync(cancellationToken);

        var currentVersion = allVersions.FirstOrDefault(v => v.IsCurrentVersion);

        return new AttachmentVersionHistoryResponse
        {
            AttachmentId = originalId,
            CurrentVersionId = currentVersion?.Id ?? attachmentId,
            TotalVersions = allVersions.Count,
            Versions = allVersions.Select(v => new AttachmentVersionDto
            {
                Id = v.Id,
                Version = v.Version,
                FileName = v.FileName,
                FileSize = v.FileSize,
                FileType = v.FileType,
                FileHash = v.FileHash,
                VersionNote = v.VersionNote,
                IsCurrentVersion = v.IsCurrentVersion,
                UploadedById = v.UploadedById,
                UploadedByName = v.UploadedBy?.Name,
                CreatedAt = v.CreatedAt
            }).ToList()
        };
    }

    #endregion

    #region Comments

    /// <inheritdoc />
    public async Task<List<Comment>> GetCommentsAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        return await _db.Comments
            .Include(c => c.User)
            .Include(c => c.Replies.Where(r => r.DeletedAt == null))
                .ThenInclude(r => r.User)
            .Where(c => c.NoticeId == noticeId && c.ParentId == null && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Comment> AddCommentAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string content,
        bool isInternal = false,
        Guid? parentId = null,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        // If reply, verify parent exists
        if (parentId.HasValue)
        {
            var parentExists = await _db.Comments.AnyAsync(c =>
                c.Id == parentId &&
                c.NoticeId == noticeId &&
                c.DeletedAt == null,
                cancellationToken);

            if (!parentExists)
            {
                throw new InvalidOperationException("Parent comment not found");
            }
        }

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            NoticeId = noticeId,
            UserId = userId,
            ParentId = parentId,
            Content = content,
            IsInternal = isInternal,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);

        // Load user for response
        await _db.Entry(comment).Reference(c => c.User).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "Comment {CommentId} added to notice {NoticeId} by user {UserId}",
            comment.Id, noticeId, userId);

        return comment;
    }

    /// <inheritdoc />
    public async Task DeleteCommentAsync(
        Guid commentId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var comment = await _db.Comments
            .Include(c => c.Notice)
            .FirstOrDefaultAsync(c =>
                c.Id == commentId &&
                c.NoticeId == noticeId &&
                c.Notice!.OrganizationId == organizationId &&
                c.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Comment not found");

        // Only allow owner or admin to delete
        // For simplicity, we'll just soft delete
        comment.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Comment {CommentId} deleted from notice {NoticeId} by user {UserId}",
            commentId, noticeId, userId);
    }

    #endregion

    #region Tasks

    /// <inheritdoc />
    public async Task<List<NoticeTask>> GetTasksAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        return await _db.Tasks
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .Where(t => t.NoticeId == noticeId && t.DeletedAt == null)
            .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NoticeTask> CreateTaskAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CreateTaskDto taskDto,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        // Get first assignee from the list (legacy single-assignee support)
        var assigneeId = taskDto.Assignees?.FirstOrDefault();

        // Validate assignee if provided
        if (assigneeId.HasValue)
        {
            var isMember = await _db.OrganizationMembers.AnyAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == assigneeId.Value &&
                m.Status == "active" &&
                m.DeletedAt == null,
                cancellationToken);

            if (!isMember)
            {
                throw new InvalidOperationException("Assignee is not an active member of this organization");
            }
        }

        var task = new NoticeTask
        {
            Id = Guid.NewGuid(),
            NoticeId = noticeId,
            CreatedById = userId,
            AssignedToId = assigneeId,
            Title = taskDto.Title,
            Description = taskDto.Description,
            DueDate = taskDto.DueDate,
            Priority = taskDto.Priority ?? "medium",
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);

        // Load navigation properties
        await _db.Entry(task).Reference(t => t.CreatedBy).LoadAsync(cancellationToken);
        if (task.AssignedToId.HasValue)
        {
            await _db.Entry(task).Reference(t => t.AssignedTo).LoadAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Task {TaskId} created for notice {NoticeId} by user {UserId}",
            task.Id, noticeId, userId);

        return task;
    }

    /// <inheritdoc />
    public async Task<NoticeTask> UpdateTaskAsync(
        Guid taskId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        UpdateTaskDto update,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t =>
                t.Id == taskId &&
                t.NoticeId == noticeId &&
                t.Notice!.OrganizationId == organizationId &&
                t.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Task not found");

        if (update.Title != null)
        {
            task.Title = update.Title;
        }

        if (update.Description != null)
        {
            task.Description = update.Description;
        }

        if (update.DueDate.HasValue)
        {
            task.DueDate = update.DueDate;
        }

        if (update.Priority != null)
        {
            task.Priority = update.Priority;
        }

        if (update.Status != null)
        {
            var oldStatus = task.Status;
            task.Status = update.Status;

            // If marking as completed
            if (update.Status == "completed" && oldStatus != "completed")
            {
                task.CompletedAt = DateTime.UtcNow;
                task.CompletedById = userId;
            }
            else if (update.Status != "completed")
            {
                task.CompletedAt = null;
                task.CompletedById = null;
            }
        }

        // Get first assignee from the list (legacy single-assignee support)
        var newAssigneeId = update.Assignees?.FirstOrDefault();
        if (newAssigneeId.HasValue)
        {
            // Validate assignee
            var isMember = await _db.OrganizationMembers.AnyAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == newAssigneeId.Value &&
                m.Status == "active" &&
                m.DeletedAt == null,
                cancellationToken);

            if (!isMember)
            {
                throw new InvalidOperationException("Assignee is not an active member of this organization");
            }

            task.AssignedToId = newAssigneeId;
        }

        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Load navigation properties
        await _db.Entry(task).Reference(t => t.CreatedBy).LoadAsync(cancellationToken);
        if (task.AssignedToId.HasValue)
        {
            await _db.Entry(task).Reference(t => t.AssignedTo).LoadAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Task {TaskId} updated for notice {NoticeId} by user {UserId}",
            taskId, noticeId, userId);

        return task;
    }

    /// <inheritdoc />
    public async Task DeleteTaskAsync(
        Guid taskId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t =>
                t.Id == taskId &&
                t.NoticeId == noticeId &&
                t.Notice!.OrganizationId == organizationId &&
                t.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Task not found");

        task.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Task {TaskId} deleted from notice {NoticeId}", taskId, noticeId);
    }

    #endregion

    #region Responses

    /// <inheritdoc />
    public async Task<List<NoticeResponse>> GetResponsesAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        return await _db.NoticeResponses
            .Include(r => r.CreatedBy)
            .Include(r => r.ApprovedBy)
            .Where(r => r.NoticeId == noticeId && r.DeletedAt == null)
            .OrderByDescending(r => r.Version)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NoticeResponse?> GetLatestResponseAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        return await _db.NoticeResponses
            .Include(r => r.CreatedBy)
            .Include(r => r.ApprovedBy)
            .Where(r => r.NoticeId == noticeId && r.DeletedAt == null)
            .OrderByDescending(r => r.Version)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NoticeResponse> SaveResponseDraftAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string draftContent,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        // Find existing draft response or create new one
        var existingDraft = await _db.NoticeResponses
            .Where(r => r.NoticeId == noticeId && r.Status == "draft" && r.DeletedAt == null)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingDraft != null)
        {
            // Update existing draft
            existingDraft.DraftContent = draftContent;
            existingDraft.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await _db.Entry(existingDraft).Reference(r => r.CreatedBy).LoadAsync(cancellationToken);

            _logger.LogInformation(
                "Response draft {ResponseId} updated for notice {NoticeId} by user {UserId}",
                existingDraft.Id, noticeId, userId);

            return existingDraft;
        }

        // Get max version
        var maxVersion = await _db.NoticeResponses
            .Where(r => r.NoticeId == noticeId && r.DeletedAt == null)
            .MaxAsync(r => (int?)r.Version, cancellationToken) ?? 0;

        // Create new draft
        var response = new NoticeResponse
        {
            Id = Guid.NewGuid(),
            NoticeId = noticeId,
            CreatedById = userId,
            DraftContent = draftContent,
            Status = "draft",
            Version = maxVersion + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.NoticeResponses.Add(response);
        await _db.SaveChangesAsync(cancellationToken);

        await _db.Entry(response).Reference(r => r.CreatedBy).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "Response draft {ResponseId} created for notice {NoticeId} by user {UserId}",
            response.Id, noticeId, userId);

        return response;
    }

    /// <inheritdoc />
    public async Task<NoticeResponse> SubmitForReviewAsync(
        Guid responseId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var response = await _db.NoticeResponses
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r =>
                r.Id == responseId &&
                r.NoticeId == noticeId &&
                r.Notice!.OrganizationId == organizationId &&
                r.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Response not found");

        if (response.Status != "draft")
        {
            throw new InvalidOperationException($"Cannot submit response in status '{response.Status}' for review");
        }

        if (string.IsNullOrWhiteSpace(response.DraftContent))
        {
            throw new InvalidOperationException("Cannot submit empty draft for review");
        }

        var oldStatus = response.Status;
        response.Status = "review";
        response.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "response.submitted_for_review",
            EntityType = "NoticeResponse",
            EntityId = responseId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new Dictionary<string, object> { ["status"] = oldStatus },
            NewValues = new Dictionary<string, object> { ["status"] = "review" }
        });

        await _db.Entry(response).Reference(r => r.CreatedBy).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "Response {ResponseId} submitted for review by user {UserId}",
            responseId, userId);

        return response;
    }

    /// <inheritdoc />
    public async Task<NoticeResponse> ApproveResponseAsync(
        Guid responseId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var response = await _db.NoticeResponses
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r =>
                r.Id == responseId &&
                r.NoticeId == noticeId &&
                r.Notice!.OrganizationId == organizationId &&
                r.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Response not found");

        if (response.Status != "review")
        {
            throw new InvalidOperationException($"Cannot approve response in status '{response.Status}'");
        }

        var oldStatus = response.Status;
        response.Status = "approved";
        response.ApprovedById = userId;
        response.FinalContent = response.DraftContent; // Copy draft to final
        response.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "response.approved",
            EntityType = "NoticeResponse",
            EntityId = responseId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new Dictionary<string, object> { ["status"] = oldStatus },
            NewValues = new Dictionary<string, object> { ["status"] = "approved", ["approved_by"] = userId }
        });

        await _db.Entry(response).Reference(r => r.CreatedBy).LoadAsync(cancellationToken);
        await _db.Entry(response).Reference(r => r.ApprovedBy).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "Response {ResponseId} approved by user {UserId}",
            responseId, userId);

        return response;
    }

    /// <inheritdoc />
    public async Task<NoticeResponse> MarkAsSubmittedAsync(
        Guid responseId,
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        string? submissionReference,
        string? submissionProofUrl,
        CancellationToken cancellationToken = default)
    {
        var response = await _db.NoticeResponses
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r =>
                r.Id == responseId &&
                r.NoticeId == noticeId &&
                r.Notice!.OrganizationId == organizationId &&
                r.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Response not found");

        if (response.Status != "approved")
        {
            throw new InvalidOperationException($"Cannot mark response in status '{response.Status}' as submitted");
        }

        var oldStatus = response.Status;
        var oldNoticeStatus = response.Notice!.Status;

        response.Status = "submitted";
        response.SubmittedAt = DateTime.UtcNow;
        response.SubmissionReference = submissionReference;
        response.SubmissionProofUrl = submissionProofUrl;
        response.UpdatedAt = DateTime.UtcNow;

        // Also update notice status to responded
        response.Notice.Status = NoticeStatus.Responded;
        response.Notice.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Audit log for response submission
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "response.submitted",
            EntityType = "NoticeResponse",
            EntityId = responseId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new Dictionary<string, object> { ["status"] = oldStatus },
            NewValues = new Dictionary<string, object>
            {
                ["status"] = "submitted",
                ["submission_reference"] = submissionReference ?? "",
                ["submitted_at"] = response.SubmittedAt!.Value
            }
        });

        // Audit log for notice status change
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.status_changed",
            EntityType = "Notice",
            EntityId = noticeId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new Dictionary<string, object> { ["status"] = oldNoticeStatus },
            NewValues = new Dictionary<string, object> { ["status"] = NoticeStatus.Responded }
        });

        await _db.Entry(response).Reference(r => r.CreatedBy).LoadAsync(cancellationToken);
        await _db.Entry(response).Reference(r => r.ApprovedBy).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "Response {ResponseId} marked as submitted with reference '{SubmissionReference}' by user {UserId}",
            responseId, submissionReference, userId);

        return response;
    }

    #endregion

    #region Reminders

    /// <inheritdoc />
    public async Task<List<DeadlineReminder>> GetRemindersAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        return await _db.DeadlineReminders
            .Include(r => r.User)
            .Where(r => r.NoticeId == noticeId && r.DeletedAt == null)
            .OrderBy(r => r.RemindAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DeadlineReminder> CreateReminderAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        CreateReminderDto reminderDto,
        CancellationToken cancellationToken = default)
    {
        // Verify notice belongs to organization
        var noticeExists = await _db.Notices.AnyAsync(n =>
            n.Id == noticeId &&
            n.OrganizationId == organizationId &&
            n.DeletedAt == null,
            cancellationToken);

        if (!noticeExists)
        {
            throw new InvalidOperationException("Notice not found");
        }

        // Validate reminder type
        var validTypes = new[] { "email", "sms", "push", "whatsapp" };
        if (!validTypes.Contains(reminderDto.ReminderType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid reminder type. Must be one of: {string.Join(", ", validTypes)}");
        }

        var reminder = new DeadlineReminder
        {
            Id = Guid.NewGuid(),
            NoticeId = noticeId,
            UserId = userId,
            ReminderType = reminderDto.ReminderType.ToLowerInvariant(),
            RemindAt = reminderDto.RemindAt,
            DaysBefore = reminderDto.DaysBefore,
            IsSent = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.DeadlineReminders.Add(reminder);
        await _db.SaveChangesAsync(cancellationToken);

        await _db.Entry(reminder).Reference(r => r.User).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "Reminder {ReminderId} created for notice {NoticeId} by user {UserId}, remind at {RemindAt}",
            reminder.Id, noticeId, userId, reminderDto.RemindAt);

        return reminder;
    }

    /// <inheritdoc />
    public async Task DeleteReminderAsync(
        Guid reminderId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var reminder = await _db.DeadlineReminders
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r =>
                r.Id == reminderId &&
                r.NoticeId == noticeId &&
                r.Notice!.OrganizationId == organizationId &&
                r.DeletedAt == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Reminder not found");

        reminder.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reminder {ReminderId} deleted from notice {NoticeId}", reminderId, noticeId);
    }

    #endregion

    #region Statistics

    /// <inheritdoc />
    public async Task<NoticeStatistics> GetStatisticsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        // Calculate end of week (Sunday). DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
        // If today is Sunday (0), we want to add 0 days to get this Sunday
        // If today is Monday (1), we want to add 6 days to get next Sunday
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        var endOfWeek = today.AddDays(daysUntilSunday == 0 ? 7 : daysUntilSunday); // Next Sunday
        var endOfMonth = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var notices = _db.Notices
            .Where(n => n.OrganizationId == organizationId && n.DeletedAt == null);

        // Count by status
        var byStatus = await notices
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        // Count by priority
        var byPriority = await notices
            .GroupBy(n => n.Priority)
            .Select(g => new { Priority = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Priority, g => g.Count, cancellationToken);

        // Overdue count (deadline passed, status not closed/archived/responded)
        var overdueCount = await notices
            .Where(n => n.ResponseDeadline.HasValue &&
                        n.ResponseDeadline < today &&
                        n.Status != NoticeStatus.Closed &&
                        n.Status != NoticeStatus.Archived &&
                        n.Status != NoticeStatus.Responded)
            .CountAsync(cancellationToken);

        // Due this week
        var dueThisWeek = await notices
            .Where(n => n.ResponseDeadline.HasValue &&
                        n.ResponseDeadline >= today &&
                        n.ResponseDeadline <= endOfWeek &&
                        n.Status != NoticeStatus.Closed &&
                        n.Status != NoticeStatus.Archived &&
                        n.Status != NoticeStatus.Responded)
            .CountAsync(cancellationToken);

        // Due this month
        var dueThisMonth = await notices
            .Where(n => n.ResponseDeadline.HasValue &&
                        n.ResponseDeadline >= today &&
                        n.ResponseDeadline <= endOfMonth &&
                        n.Status != NoticeStatus.Closed &&
                        n.Status != NoticeStatus.Archived &&
                        n.Status != NoticeStatus.Responded)
            .CountAsync(cancellationToken);

        // Total demand amount (sum of TaxAmount + PenaltyAmount + InterestAmount)
        var totalDemand = await notices
            .SumAsync(n =>
                (n.TaxAmount ?? 0) + (n.PenaltyAmount ?? 0) + (n.InterestAmount ?? 0),
                cancellationToken);

        // Total count
        var totalCount = await notices.CountAsync(cancellationToken);

        return new NoticeStatistics(
            byStatus,
            byPriority,
            overdueCount,
            dueThisWeek,
            dueThisMonth,
            totalDemand,
            totalCount);
    }

    #endregion

    #region Relationships

    public async Task<NoticeRelationshipsResponse> GetRelationshipsAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var notice = await _db.Notices
            .FirstOrDefaultAsync(n => n.Id == noticeId && n.OrganizationId == organizationId, cancellationToken);

        if (notice == null)
        {
            throw new InvalidOperationException("NOTICE_NOT_FOUND");
        }

        var outgoing = await _db.NoticeRelationships
            .Include(r => r.SourceNotice)
            .Include(r => r.TargetNotice)
            .Include(r => r.CreatedBy)
            .Where(r => r.SourceNoticeId == noticeId)
            .Select(r => MapToRelationshipDto(r))
            .ToListAsync(cancellationToken);

        var incoming = await _db.NoticeRelationships
            .Include(r => r.SourceNotice)
            .Include(r => r.TargetNotice)
            .Include(r => r.CreatedBy)
            .Where(r => r.TargetNoticeId == noticeId)
            .Select(r => MapToRelationshipDto(r))
            .ToListAsync(cancellationToken);

        return new NoticeRelationshipsResponse(outgoing, incoming);
    }

    public async Task<NoticeRelationshipDto> CreateRelationshipAsync(
        Guid sourceNoticeId,
        Guid organizationId,
        Guid userId,
        CreateNoticeRelationshipRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate relationship type
        if (!NoticeRelationshipType.IsValid(request.RelationshipType))
        {
            throw new ArgumentException($"Invalid relationship type: {request.RelationshipType}");
        }

        // Verify source notice exists and belongs to org
        var sourceNotice = await _db.Notices
            .FirstOrDefaultAsync(n => n.Id == sourceNoticeId && n.OrganizationId == organizationId, cancellationToken);

        if (sourceNotice == null)
        {
            throw new InvalidOperationException("SOURCE_NOTICE_NOT_FOUND");
        }

        // Verify target notice exists and belongs to same org
        var targetNotice = await _db.Notices
            .FirstOrDefaultAsync(n => n.Id == request.TargetNoticeId && n.OrganizationId == organizationId, cancellationToken);

        if (targetNotice == null)
        {
            throw new InvalidOperationException("TARGET_NOTICE_NOT_FOUND");
        }

        // Cannot link to self
        if (sourceNoticeId == request.TargetNoticeId)
        {
            throw new InvalidOperationException("CANNOT_LINK_TO_SELF");
        }

        // Check for existing relationship of same type
        var existingRelationship = await _db.NoticeRelationships
            .FirstOrDefaultAsync(r =>
                r.SourceNoticeId == sourceNoticeId &&
                r.TargetNoticeId == request.TargetNoticeId &&
                r.RelationshipType == request.RelationshipType,
                cancellationToken);

        if (existingRelationship != null)
        {
            throw new InvalidOperationException("RELATIONSHIP_EXISTS");
        }

        var relationship = new NoticeRelationship
        {
            SourceNoticeId = sourceNoticeId,
            TargetNoticeId = request.TargetNoticeId,
            RelationshipType = request.RelationshipType,
            Note = request.Note,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.NoticeRelationships.Add(relationship);
        await _db.SaveChangesAsync(cancellationToken);

        // Reload with navigation properties
        await _db.Entry(relationship).Reference(r => r.SourceNotice).LoadAsync(cancellationToken);
        await _db.Entry(relationship).Reference(r => r.TargetNotice).LoadAsync(cancellationToken);
        await _db.Entry(relationship).Reference(r => r.CreatedBy).LoadAsync(cancellationToken);

        _logger.LogInformation(
            "Created notice relationship: {SourceId} -> {TargetId} ({Type})",
            sourceNoticeId, request.TargetNoticeId, request.RelationshipType);

        return MapToRelationshipDto(relationship);
    }

    public async Task DeleteRelationshipAsync(
        Guid relationshipId,
        Guid noticeId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var relationship = await _db.NoticeRelationships
            .Include(r => r.SourceNotice)
            .FirstOrDefaultAsync(r =>
                r.Id == relationshipId &&
                (r.SourceNoticeId == noticeId || r.TargetNoticeId == noticeId),
                cancellationToken);

        if (relationship == null)
        {
            throw new InvalidOperationException("RELATIONSHIP_NOT_FOUND");
        }

        // Verify the notice belongs to the organization
        if (relationship.SourceNotice.OrganizationId != organizationId)
        {
            throw new InvalidOperationException("RELATIONSHIP_NOT_FOUND");
        }

        _db.NoticeRelationships.Remove(relationship);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted notice relationship: {RelationshipId}", relationshipId);
    }

    private static NoticeRelationshipDto MapToRelationshipDto(NoticeRelationship r)
    {
        return new NoticeRelationshipDto(
            r.Id,
            r.SourceNoticeId,
            r.TargetNoticeId,
            r.RelationshipType,
            r.Note,
            new NoticeRelationshipNoticeDto(
                r.SourceNotice.Id,
                r.SourceNotice.NoticeNumber,
                r.SourceNotice.NoticeType,
                r.SourceNotice.Gstin,
                r.SourceNotice.Status,
                r.SourceNotice.ResponseDeadline),
            new NoticeRelationshipNoticeDto(
                r.TargetNotice.Id,
                r.TargetNotice.NoticeNumber,
                r.TargetNotice.NoticeType,
                r.TargetNotice.Gstin,
                r.TargetNotice.Status,
                r.TargetNotice.ResponseDeadline),
            r.CreatedBy?.Name ?? "Unknown",
            r.CreatedAt);
    }

    #endregion
}

/// <summary>
/// Interface for notice processing background job.
/// </summary>
public interface INoticeProcessingJob
{
    Task ProcessAsync(Guid noticeId, CancellationToken cancellationToken);
}
