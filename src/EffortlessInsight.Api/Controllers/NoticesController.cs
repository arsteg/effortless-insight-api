using System.Security.Claims;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

[ApiController]
[Route("api/v1/notices")]
[Authorize]
public class NoticesController : ControllerBase
{
    private readonly INoticeServiceExtended _noticeService;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly ILogger<NoticesController> _logger;

    public NoticesController(
        INoticeServiceExtended noticeService,
        ICurrentOrganizationService currentOrg,
        ILogger<NoticesController> logger)
    {
        _noticeService = noticeService;
        _currentOrg = currentOrg;
        _logger = logger;
    }

    #region Upload Endpoints

    /// <summary>
    /// Upload a notice file
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(26_214_400)] // 25 MB
    [ProducesResponseType(typeof(ApiResponse<NoticeUploadResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadNoticeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission (not viewer)
            if (!_currentOrg.HasPermission("notices.upload"))
            {
                return Forbid();
            }

            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new ApiErrorResponse(false, "FILE_REQUIRED", "No file was uploaded"));
            }

            using var stream = request.File.OpenReadStream();
            var result = await _noticeService.UploadAsync(
                stream,
                request.File.FileName,
                request.File.ContentType,
                orgId,
                userId,
                request.Gstin,
                request.Tags,
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(new ApiErrorResponse(false, result.ErrorCode!, result.ErrorMessage!));
            }

            var response = new NoticeUploadResponse(
                NoticeId: result.NoticeId!.Value,
                FileName: result.FileName!,
                FileSize: result.FileSize,
                Status: result.Status!,
                ProcessingJobId: result.ProcessingJobId,
                EstimatedCompletionSeconds: result.EstimatedCompletionSeconds,
                DuplicateWarning: result.DuplicateWarning != null
                    ? new DuplicateWarningDto(
                        IsPotentialDuplicate: true,
                        SimilarNoticeId: result.DuplicateWarning.ExistingNoticeId,
                        SimilarNoticeNumber: result.DuplicateWarning.ExistingNoticeNumber,
                        SimilarityScore: result.DuplicateWarning.SimilarityScore,
                        UploadedAt: result.DuplicateWarning.UploadedAt)
                    : null,
                CreatedAt: result.CreatedAt);

            return StatusCode(StatusCodes.Status202Accepted,
                new ApiResponse<NoticeUploadResponse>(true, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload notice");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "UPLOAD_FAILED", "An unexpected error occurred during upload"));
        }
    }

    /// <summary>
    /// Generate a pre-signed URL for direct upload to S3
    /// </summary>
    [HttpPost("upload/presigned")]
    [ProducesResponseType(typeof(ApiResponse<PresignedUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPresignedUploadUrl(
        [FromBody] PresignedUploadRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();

            if (!_currentOrg.HasPermission("notices.upload"))
            {
                return Forbid();
            }

            var result = await _noticeService.GenerateUploadUrlAsync(
                request.FileName,
                request.ContentType,
                request.ContentLength,
                orgId,
                cancellationToken);

            return Ok(new ApiResponse<PresignedUploadResponse>(true, new PresignedUploadResponse(
                Url: result.Url,
                Key: result.Key,
                ExpiresAt: result.ExpiresAt,
                RequiredHeaders: result.RequiredHeaders)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION_ERROR", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate pre-signed URL");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Confirm a pre-signed upload and create the notice record
    /// </summary>
    [HttpPost("upload/confirm")]
    [ProducesResponseType(typeof(ApiResponse<NoticeUploadResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmUpload(
        [FromBody] ConfirmUploadRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            if (!_currentOrg.HasPermission("notices.upload"))
            {
                return Forbid();
            }

            var result = await _noticeService.ConfirmUploadAsync(
                request.S3Key,
                request.FileName,
                request.ContentType,
                request.FileSize,
                request.FileHash,
                orgId,
                userId,
                request.Gstin,
                request.Tags,
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(new ApiErrorResponse(false, result.ErrorCode!, result.ErrorMessage!));
            }

            var response = new NoticeUploadResponse(
                NoticeId: result.NoticeId!.Value,
                FileName: result.FileName!,
                FileSize: result.FileSize,
                Status: result.Status!,
                ProcessingJobId: result.ProcessingJobId,
                EstimatedCompletionSeconds: result.EstimatedCompletionSeconds,
                DuplicateWarning: result.DuplicateWarning != null
                    ? new DuplicateWarningDto(
                        IsPotentialDuplicate: true,
                        SimilarNoticeId: result.DuplicateWarning.ExistingNoticeId,
                        SimilarNoticeNumber: result.DuplicateWarning.ExistingNoticeNumber,
                        SimilarityScore: result.DuplicateWarning.SimilarityScore,
                        UploadedAt: result.DuplicateWarning.UploadedAt)
                    : null,
                CreatedAt: result.CreatedAt);

            return StatusCode(StatusCodes.Status202Accepted,
                new ApiResponse<NoticeUploadResponse>(true, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm upload");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Notice CRUD

    /// <summary>
    /// Get notices with filtering and pagination
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<NoticeListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] NoticeFilterDto filter,
        [FromQuery] bool includeAggregations = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var result = await _noticeService.GetListAsync(orgId, filter, cancellationToken);

            var notices = result.Items.Select(MapToDto).ToList();

            NoticeAggregationsDto? aggregations = null;
            if (includeAggregations)
            {
                var stats = await _noticeService.GetStatisticsAsync(orgId, cancellationToken);
                aggregations = new NoticeAggregationsDto(
                    ByStatus: stats.ByStatus,
                    ByPriority: stats.ByPriority,
                    OverdueCount: stats.OverdueCount,
                    DueThisWeek: stats.DueThisWeek);
            }

            return Ok(new ApiResponse<NoticeListResponse>(true, new NoticeListResponse(
                Notices: notices,
                TotalCount: result.TotalCount,
                Page: result.Page,
                PageSize: result.PageSize,
                TotalPages: result.TotalPages,
                Aggregations: aggregations)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get notices");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get notice by ID
    /// </summary>
    [HttpGet("{noticeId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<NoticeDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var notice = await _noticeService.GetByIdAsync(noticeId, orgId, cancellationToken);

            if (notice == null)
            {
                return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
            }

            return Ok(new ApiResponse<NoticeDetailDto>(true, MapToDetailDto(notice)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Update notice status
    /// </summary>
    [HttpPut("{noticeId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<NoticeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid noticeId,
        [FromBody] UpdateNoticeStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            var notice = await _noticeService.UpdateStatusAsync(
                noticeId, orgId, request.Status, userId, request.Reason, cancellationToken);

            return Ok(new ApiResponse<NoticeDto>(true, MapToDto(notice)));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_TRANSITION", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update notice status {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Assign notice to a team member
    /// </summary>
    [HttpPut("{noticeId:guid}/assign")]
    [Authorize(Policy = "RequireManager")]
    [ProducesResponseType(typeof(ApiResponse<NoticeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Assign(
        Guid noticeId,
        [FromBody] AssignNoticeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            var notice = await _noticeService.AssignAsync(
                noticeId, orgId, request.AssigneeId, userId, cancellationToken);

            return Ok(new ApiResponse<NoticeDto>(true, MapToDto(notice)));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not an active member"))
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_ASSIGNEE", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Delete a notice (soft delete)
    /// </summary>
    [HttpDelete("{noticeId:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid noticeId,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            await _noticeService.DeleteAsync(noticeId, orgId, userId, reason, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Update notice details
    /// </summary>
    [HttpPut("{noticeId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<NoticeDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid noticeId,
        [FromBody] UpdateNoticeDetailsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            var updateDto = new Services.Notices.UpdateNoticeDetailsDto(
                NoticeNumber: request.NoticeNumber,
                NoticeType: request.NoticeType,
                NoticeCategory: request.NoticeCategory,
                Gstin: request.Gstin,
                IssueDate: request.IssueDate,
                ResponseDeadline: request.ResponseDeadline,
                ExtendedDeadline: request.ExtendedDeadline,
                TaxAmount: request.TaxAmount,
                PenaltyAmount: request.PenaltyAmount,
                InterestAmount: request.InterestAmount,
                PeriodFrom: request.PeriodFrom,
                PeriodTo: request.PeriodTo,
                IssuingAuthority: request.IssuingAuthority,
                Priority: request.Priority,
                Tags: request.Tags);

            var notice = await _noticeService.UpdateAsync(
                noticeId, orgId, userId, updateDto, cancellationToken);

            return Ok(new ApiResponse<NoticeDetailDto>(true, MapToDetailDto(notice)));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION_ERROR", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Attachments

    /// <summary>
    /// Get attachments for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/attachments")]
    [ProducesResponseType(typeof(ApiResponse<List<AttachmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachments(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var attachments = await _noticeService.GetAttachmentsAsync(noticeId, orgId, cancellationToken);

            var dtos = attachments.Select(a => new AttachmentDto(
                Id: a.Id,
                FileName: a.FileName,
                FileSize: a.FileSize,
                FileType: a.FileType,
                DocumentType: a.DocumentType,
                Description: a.Description,
                UploadedById: a.UploadedById,
                UploadedByName: a.UploadedBy?.Name,
                CreatedAt: a.CreatedAt)).ToList();

            return Ok(new ApiResponse<List<AttachmentDto>>(true, dtos));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get attachments for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Upload an attachment to a notice
    /// </summary>
    [HttpPost("{noticeId:guid}/attachments")]
    [RequestSizeLimit(26_214_400)] // 25 MB
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddAttachment(
        Guid noticeId,
        [FromForm] AddAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new ApiErrorResponse(false, "FILE_REQUIRED", "No file was uploaded"));
            }

            using var stream = request.File.OpenReadStream();
            var attachment = await _noticeService.AddAttachmentAsync(
                noticeId,
                orgId,
                userId,
                stream,
                request.File.FileName,
                request.File.ContentType,
                request.DocumentType,
                request.Description,
                cancellationToken);

            var dto = new AttachmentDto(
                Id: attachment.Id,
                FileName: attachment.FileName,
                FileSize: attachment.FileSize,
                FileType: attachment.FileType,
                DocumentType: attachment.DocumentType,
                Description: attachment.Description,
                UploadedById: attachment.UploadedById,
                UploadedByName: null,
                CreatedAt: attachment.CreatedAt);

            return StatusCode(StatusCodes.Status201Created, new ApiResponse<AttachmentDto>(true, dto));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION_ERROR", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add attachment to notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Delete an attachment
    /// </summary>
    [HttpDelete("{noticeId:guid}/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAttachment(
        Guid noticeId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            await _noticeService.DeleteAttachmentAsync(attachmentId, noticeId, orgId, userId, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete attachment {AttachmentId}", attachmentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get download URL for an attachment
    /// </summary>
    [HttpGet("{noticeId:guid}/attachments/{attachmentId:guid}/download")]
    [ProducesResponseType(typeof(ApiResponse<DownloadUrlResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentDownloadUrl(
        Guid noticeId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var result = await _noticeService.GetAttachmentDownloadUrlAsync(
                attachmentId, noticeId, orgId, cancellationToken);

            return Ok(new ApiResponse<DownloadUrlResponse>(true, new DownloadUrlResponse(
                Url: result.Url,
                ExpiresAt: result.ExpiresAt)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get download URL for attachment {AttachmentId}", attachmentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Comments

    /// <summary>
    /// Get comments for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/comments")]
    [ProducesResponseType(typeof(ApiResponse<List<CommentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetComments(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var comments = await _noticeService.GetCommentsAsync(noticeId, orgId, cancellationToken);

            var dtos = comments.Select(MapCommentToDto).ToList();

            return Ok(new ApiResponse<List<CommentDto>>(true, dtos));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get comments for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Add a comment to a notice
    /// </summary>
    [HttpPost("{noticeId:guid}/comments")]
    [ProducesResponseType(typeof(ApiResponse<CommentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(
        Guid noticeId,
        [FromBody] AddCommentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            var comment = await _noticeService.AddCommentAsync(
                noticeId,
                orgId,
                userId,
                request.Content,
                request.IsInternal,
                request.ParentId,
                cancellationToken);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<CommentDto>(true, MapCommentToDto(comment)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add comment to notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Delete a comment
    /// </summary>
    [HttpDelete("{noticeId:guid}/comments/{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteComment(
        Guid noticeId,
        Guid commentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            await _noticeService.DeleteCommentAsync(commentId, noticeId, orgId, userId, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete comment {CommentId}", commentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Tasks

    /// <summary>
    /// Get tasks for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/tasks")]
    [ProducesResponseType(typeof(ApiResponse<List<NoticeTaskDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTasks(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var tasks = await _noticeService.GetTasksAsync(noticeId, orgId, cancellationToken);

            var dtos = tasks.Select(MapTaskToDto).ToList();

            return Ok(new ApiResponse<List<NoticeTaskDto>>(true, dtos));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tasks for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Create a task for a notice
    /// </summary>
    [HttpPost("{noticeId:guid}/tasks")]
    [Authorize(Policy = "RequireManager")]
    [ProducesResponseType(typeof(ApiResponse<NoticeTaskDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTask(
        Guid noticeId,
        [FromBody] CreateNoticeTaskRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            var taskDto = new Services.Notices.CreateTaskDto(
                Title: request.Title,
                Description: request.Description,
                DueDate: request.DueDate,
                Priority: request.Priority ?? "medium",
                AssignedToId: request.AssignedToId);

            var task = await _noticeService.CreateTaskAsync(
                noticeId, orgId, userId, taskDto, cancellationToken);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<NoticeTaskDto>(true, MapTaskToDto(task)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION_ERROR", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create task for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Update a task
    /// </summary>
    [HttpPut("{noticeId:guid}/tasks/{taskId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<NoticeTaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTask(
        Guid noticeId,
        Guid taskId,
        [FromBody] UpdateNoticeTaskRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            var updateDto = new Services.Notices.UpdateTaskDto(
                Title: request.Title,
                Description: request.Description,
                DueDate: request.DueDate,
                Priority: request.Priority,
                Status: request.Status,
                AssignedToId: request.AssignedToId);

            var task = await _noticeService.UpdateTaskAsync(
                taskId, noticeId, orgId, userId, updateDto, cancellationToken);

            return Ok(new ApiResponse<NoticeTaskDto>(true, MapTaskToDto(task)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION_ERROR", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update task {TaskId}", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Delete a task
    /// </summary>
    [HttpDelete("{noticeId:guid}/tasks/{taskId:guid}")]
    [Authorize(Policy = "RequireManager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTask(
        Guid noticeId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();

            await _noticeService.DeleteTaskAsync(taskId, noticeId, orgId, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete task {TaskId}", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region File Operations

    /// <summary>
    /// Get download URL for notice file
    /// </summary>
    [HttpGet("{noticeId:guid}/download")]
    [ProducesResponseType(typeof(ApiResponse<DownloadUrlResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDownloadUrl(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var result = await _noticeService.GetDownloadUrlAsync(noticeId, orgId, cancellationToken);

            return Ok(new ApiResponse<DownloadUrlResponse>(true, new DownloadUrlResponse(
                Url: result.Url,
                ExpiresAt: result.ExpiresAt)));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get download URL for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region AI Processing

    /// <summary>
    /// Get AI report for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/report")]
    [ProducesResponseType(typeof(ApiResponse<NoticeAiReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReport(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();

            // First verify the notice belongs to this org
            var notice = await _noticeService.GetByIdAsync(noticeId, orgId, cancellationToken);
            if (notice == null)
            {
                return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
            }

            var report = await _noticeService.GetReportAsync(noticeId);
            if (report == null)
            {
                return NotFound(new ApiErrorResponse(false, "REPORT_NOT_FOUND",
                    "AI report not yet available. Check processing status."));
            }

            return Ok(new ApiResponse<NoticeAiReportDto>(true, MapReportToDto(report)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI report for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Retry AI processing for a failed notice
    /// </summary>
    [HttpPost("{noticeId:guid}/report/retry")]
    [ProducesResponseType(typeof(ApiResponse<ProcessingRetryResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryProcessing(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var jobId = await _noticeService.RetryProcessingAsync(noticeId, orgId, cancellationToken);

            return StatusCode(StatusCodes.Status202Accepted,
                new ApiResponse<ProcessingRetryResponse>(true, new ProcessingRetryResponse(
                    NoticeId: noticeId,
                    JobId: jobId,
                    Status: "queued")));
        }
        catch (InvalidOperationException ex) when (ex.Message == "Notice not found")
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Notice not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Maximum processing attempts"))
        {
            return BadRequest(new ApiErrorResponse(false, "MAX_RETRIES_EXCEEDED", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry processing for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Responses

    /// <summary>
    /// Get all responses for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/responses")]
    [ProducesResponseType(typeof(ApiResponse<List<NoticeResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResponses(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.view"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();
            var responses = await _noticeService.GetResponsesAsync(noticeId, orgId, cancellationToken);
            var dtos = responses.Select(MapResponseToDto).ToList();
            return Ok(new ApiResponse<List<NoticeResponseDto>>(true, dtos));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get responses for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get the latest response for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/responses/latest")]
    [ProducesResponseType(typeof(ApiResponse<NoticeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatestResponse(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.view"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();
            var response = await _noticeService.GetLatestResponseAsync(noticeId, orgId, cancellationToken);

            if (response == null)
            {
                return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "No response found for this notice"));
            }

            return Ok(new ApiResponse<NoticeResponseDto>(true, MapResponseToDto(response)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest response for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Save or update a response draft
    /// </summary>
    [HttpPost("{noticeId:guid}/responses/draft")]
    [ProducesResponseType(typeof(ApiResponse<NoticeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveResponseDraft(
        Guid noticeId,
        [FromBody] SaveDraftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            if (!_currentOrg.HasPermission("notices.edit"))
            {
                return Forbid();
            }

            var response = await _noticeService.SaveResponseDraftAsync(
                noticeId, orgId, userId, request.DraftContent, cancellationToken);

            return Ok(new ApiResponse<NoticeResponseDto>(true, MapResponseToDto(response)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save draft for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Submit a response for review
    /// </summary>
    [HttpPost("{noticeId:guid}/responses/{responseId:guid}/submit-for-review")]
    [ProducesResponseType(typeof(ApiResponse<NoticeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitForReview(
        Guid noticeId,
        Guid responseId,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            if (!_currentOrg.HasPermission("notices.edit"))
            {
                return Forbid();
            }

            var response = await _noticeService.SubmitForReviewAsync(
                responseId, noticeId, orgId, userId, cancellationToken);

            return Ok(new ApiResponse<NoticeResponseDto>(true, MapResponseToDto(response)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_OPERATION", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit response {ResponseId} for review", responseId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Approve a response
    /// </summary>
    [HttpPost("{noticeId:guid}/responses/{responseId:guid}/approve")]
    [ProducesResponseType(typeof(ApiResponse<NoticeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveResponse(
        Guid noticeId,
        Guid responseId,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Only admin/manager can approve
            if (!_currentOrg.HasPermission("notices.approve"))
            {
                return Forbid();
            }

            var response = await _noticeService.ApproveResponseAsync(
                responseId, noticeId, orgId, userId, cancellationToken);

            return Ok(new ApiResponse<NoticeResponseDto>(true, MapResponseToDto(response)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_OPERATION", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve response {ResponseId}", responseId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Mark a response as submitted to the authority
    /// </summary>
    [HttpPost("{noticeId:guid}/responses/{responseId:guid}/mark-submitted")]
    [ProducesResponseType(typeof(ApiResponse<NoticeResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsSubmitted(
        Guid noticeId,
        Guid responseId,
        [FromBody] MarkSubmittedRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            if (!_currentOrg.HasPermission("notices.edit"))
            {
                return Forbid();
            }

            var response = await _noticeService.MarkAsSubmittedAsync(
                responseId, noticeId, orgId, userId,
                request.SubmissionReference, request.SubmissionProofUrl,
                cancellationToken);

            return Ok(new ApiResponse<NoticeResponseDto>(true, MapResponseToDto(response)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_OPERATION", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark response {ResponseId} as submitted", responseId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Reminders

    /// <summary>
    /// Get reminders for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/reminders")]
    [ProducesResponseType(typeof(ApiResponse<List<ReminderDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReminders(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.view"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();
            var reminders = await _noticeService.GetRemindersAsync(noticeId, orgId, cancellationToken);
            var dtos = reminders.Select(MapReminderToDto).ToList();
            return Ok(new ApiResponse<List<ReminderDto>>(true, dtos));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get reminders for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Create a reminder for a notice
    /// </summary>
    [HttpPost("{noticeId:guid}/reminders")]
    [ProducesResponseType(typeof(ApiResponse<ReminderDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateReminder(
        Guid noticeId,
        [FromBody] CreateReminderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.edit"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            var reminderDto = new CreateReminderDto(
                request.ReminderType,
                request.RemindAt,
                request.DaysBefore);

            var reminder = await _noticeService.CreateReminderAsync(
                noticeId, orgId, userId, reminderDto, cancellationToken);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<ReminderDto>(true, MapReminderToDto(reminder)));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_OPERATION", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create reminder for notice {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Delete a reminder
    /// </summary>
    [HttpDelete("{noticeId:guid}/reminders/{reminderId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteReminder(
        Guid noticeId,
        Guid reminderId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.edit"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();

            await _noticeService.DeleteReminderAsync(reminderId, noticeId, orgId, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete reminder {ReminderId} from notice {NoticeId}", reminderId, noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Get notice statistics for dashboard
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(ApiResponse<NoticeStatisticsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatistics(CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.view"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();
            var stats = await _noticeService.GetStatisticsAsync(orgId, cancellationToken);

            var dto = new NoticeStatisticsDto(
                ByStatus: stats.ByStatus,
                ByPriority: stats.ByPriority,
                OverdueCount: stats.OverdueCount,
                DueThisWeek: stats.DueThisWeek,
                DueThisMonth: stats.DueThisMonth,
                TotalDemandAmount: stats.TotalDemandAmount,
                TotalCount: stats.TotalCount);

            return Ok(new ApiResponse<NoticeStatisticsDto>(true, dto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get notice statistics");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Helpers

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in token");
        }

        return userId;
    }

    private Guid GetCurrentOrganizationId()
    {
        var orgId = _currentOrg.OrganizationId;
        if (!orgId.HasValue)
        {
            throw new InvalidOperationException("No organization context");
        }
        return orgId.Value;
    }

    private static NoticeDto MapToDto(Notice notice)
    {
        var daysRemaining = notice.GetDaysRemaining();

        return new NoticeDto(
            Id: notice.Id,
            NoticeType: notice.NoticeType,
            NoticeCategory: notice.NoticeCategory,
            NoticeNumber: notice.NoticeNumber,
            Gstin: notice.Gstin,
            IssueDate: notice.IssueDate,
            ResponseDeadline: notice.ResponseDeadline,
            DaysRemaining: daysRemaining,
            TaxAmount: notice.TaxAmount,
            PenaltyAmount: notice.PenaltyAmount,
            Status: notice.Status,
            Priority: notice.Priority,
            RiskScore: notice.AiReport?.RiskScore,
            RiskLevel: notice.AiReport?.RiskLevel,
            SummaryEn: notice.AiReport?.SummaryEn,
            AssignedToId: notice.AssignedToId,
            AssignedToName: notice.AssignedTo?.Name,
            CreatedAt: notice.CreatedAt);
    }

    private static NoticeDetailDto MapToDetailDto(Notice notice)
    {
        var daysRemaining = notice.GetDaysRemaining();

        return new NoticeDetailDto(
            Id: notice.Id,
            NoticeType: notice.NoticeType,
            NoticeCategory: notice.NoticeCategory,
            NoticeNumber: notice.NoticeNumber,
            Gstin: notice.Gstin,
            IssueDate: notice.IssueDate,
            ResponseDeadline: notice.ResponseDeadline,
            ExtendedDeadline: notice.ExtendedDeadline,
            DaysRemaining: daysRemaining,
            TaxAmount: notice.TaxAmount,
            PenaltyAmount: notice.PenaltyAmount,
            InterestAmount: notice.InterestAmount,
            PeriodFrom: notice.PeriodFrom,
            PeriodTo: notice.PeriodTo,
            IssuingAuthority: notice.IssuingAuthority,
            Status: notice.Status,
            Priority: notice.Priority,
            FileUrl: notice.FileUrl,
            ProcessingStatus: notice.ProcessingStatus,
            Tags: notice.Tags,
            AiReport: notice.AiReport != null ? MapReportToDto(notice.AiReport) : null,
            AssignedToId: notice.AssignedToId,
            AssignedToName: notice.AssignedTo?.Name,
            CreatedAt: notice.CreatedAt,
            UpdatedAt: notice.UpdatedAt);
    }

    private static NoticeAiReportDto MapReportToDto(NoticeAiReport report)
    {
        return new NoticeAiReportDto(
            Id: report.Id,
            RiskScore: report.RiskScore,
            RiskLevel: report.RiskLevel,
            SummaryEn: report.SummaryEn,
            SummaryHi: report.SummaryHi,
            PlainEnglish: report.PlainEnglish,
            ActionItems: report.ActionItems != null
                ? MapActionItems(report.ActionItems)
                : null,
            RequiredDocuments: report.RequiredDocuments != null
                ? MapRequiredDocuments(report.RequiredDocuments)
                : null,
            LegalReferences: report.LegalReferences != null
                ? MapLegalReferences(report.LegalReferences)
                : null,
            ConfidenceScores: report.ConfidenceScores?
                .ToDictionary(kvp => kvp.Key, kvp => Convert.ToInt32(kvp.Value)),
            ModelUsed: report.ModelUsed,
            ProcessingTimeMs: report.ProcessingTimeMs,
            CreatedAt: report.CreatedAt);
    }

    private static List<ActionItemDto>? MapActionItems(Dictionary<string, object> actionItems)
    {
        if (actionItems.TryGetValue("items", out var items) && items is IEnumerable<object> list)
        {
            return list.Select((item, index) =>
            {
                if (item is Dictionary<string, object> dict)
                {
                    return new ActionItemDto(
                        Priority: dict.TryGetValue("priority", out var p) ? Convert.ToInt32(p) : index + 1,
                        Action: dict.TryGetValue("action", out var a) ? a.ToString()! : "",
                        Description: dict.TryGetValue("description", out var d) ? d.ToString()! : "",
                        DueInDays: dict.TryGetValue("due_in_days", out var days) ? Convert.ToInt32(days) : null,
                        AssigneeSuggestion: dict.TryGetValue("assignee_suggestion", out var s) ? s.ToString() : null);
                }
                return new ActionItemDto(index + 1, item.ToString()!, "", null, null);
            }).ToList();
        }
        return null;
    }

    private static List<RequiredDocumentDto>? MapRequiredDocuments(Dictionary<string, object> documents)
    {
        if (documents.TryGetValue("items", out var items) && items is IEnumerable<object> list)
        {
            return list.Select(item =>
            {
                if (item is Dictionary<string, object> dict)
                {
                    return new RequiredDocumentDto(
                        Document: dict.TryGetValue("document", out var d) ? d.ToString()! : "",
                        Mandatory: dict.TryGetValue("mandatory", out var m) && Convert.ToBoolean(m));
                }
                return new RequiredDocumentDto(item.ToString()!, true);
            }).ToList();
        }
        return null;
    }

    private static List<LegalReferenceDto>? MapLegalReferences(Dictionary<string, object> references)
    {
        if (references.TryGetValue("items", out var items) && items is IEnumerable<object> list)
        {
            return list.Select(item =>
            {
                if (item is Dictionary<string, object> dict)
                {
                    return new LegalReferenceDto(
                        Section: dict.TryGetValue("section", out var s) ? s.ToString()! : "",
                        Description: dict.TryGetValue("description", out var d) ? d.ToString()! : "");
                }
                return new LegalReferenceDto(item.ToString()!, "");
            }).ToList();
        }
        return null;
    }

    private static CommentDto MapCommentToDto(Data.Entities.Comment comment)
    {
        return new CommentDto(
            Id: comment.Id,
            Content: comment.Content,
            IsInternal: comment.IsInternal,
            ParentId: comment.ParentId,
            UserId: comment.UserId,
            UserName: comment.User?.Name,
            Replies: comment.Replies?.Where(r => r.DeletedAt == null)
                .Select(MapCommentToDto).ToList() ?? [],
            CreatedAt: comment.CreatedAt);
    }

    private static NoticeTaskDto MapTaskToDto(Data.Entities.NoticeTask task)
    {
        return new NoticeTaskDto(
            Id: task.Id,
            Title: task.Title,
            Description: task.Description,
            DueDate: task.DueDate,
            Priority: task.Priority,
            Status: task.Status,
            AssignedToId: task.AssignedToId,
            AssignedToName: task.AssignedTo?.Name,
            CreatedById: task.CreatedById,
            CreatedByName: task.CreatedBy?.Name,
            CompletedAt: task.CompletedAt,
            CompletedById: task.CompletedById,
            CreatedAt: task.CreatedAt);
    }

    private static NoticeResponseDto MapResponseToDto(Data.Entities.NoticeResponse response)
    {
        return new NoticeResponseDto(
            Id: response.Id,
            NoticeId: response.NoticeId,
            DraftContent: response.DraftContent,
            FinalContent: response.FinalContent,
            Status: response.Status,
            Version: response.Version,
            SubmissionReference: response.SubmissionReference,
            SubmissionProofUrl: response.SubmissionProofUrl,
            SubmittedAt: response.SubmittedAt,
            CreatedById: response.CreatedById,
            CreatedByName: response.CreatedBy?.Name,
            ApprovedById: response.ApprovedById,
            ApprovedByName: response.ApprovedBy?.Name,
            CreatedAt: response.CreatedAt,
            UpdatedAt: response.UpdatedAt);
    }

    private static ReminderDto MapReminderToDto(Data.Entities.DeadlineReminder reminder)
    {
        return new ReminderDto(
            Id: reminder.Id,
            NoticeId: reminder.NoticeId,
            ReminderType: reminder.ReminderType,
            RemindAt: reminder.RemindAt,
            DaysBefore: reminder.DaysBefore,
            IsSent: reminder.IsSent,
            SentAt: reminder.SentAt,
            UserId: reminder.UserId,
            UserName: reminder.User?.Name,
            CreatedAt: reminder.CreatedAt);
    }

    #endregion
}

#region Request/Response DTOs

public record UploadNoticeRequest
{
    public IFormFile? File { get; init; }
    public string? Gstin { get; init; }
    public List<string>? Tags { get; init; }
}

public record PresignedUploadRequest(
    string FileName,
    string ContentType,
    long ContentLength);

public record PresignedUploadResponse(
    string Url,
    string Key,
    DateTime ExpiresAt,
    Dictionary<string, string> RequiredHeaders);

public record ConfirmUploadRequest(
    string S3Key,
    string FileName,
    string ContentType,
    int FileSize,
    string FileHash,
    string? Gstin = null,
    List<string>? Tags = null);

public record NoticeUploadResponse(
    Guid NoticeId,
    string FileName,
    int FileSize,
    string Status,
    string? ProcessingJobId,
    int EstimatedCompletionSeconds,
    DuplicateWarningDto? DuplicateWarning,
    DateTime CreatedAt);

public record DuplicateWarningDto(
    bool IsPotentialDuplicate,
    Guid? SimilarNoticeId,
    string? SimilarNoticeNumber,
    decimal SimilarityScore,
    DateTime? UploadedAt);

public record NoticeListResponse(
    List<NoticeDto> Notices,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    NoticeAggregationsDto? Aggregations = null);

public record NoticeAggregationsDto(
    Dictionary<string, int> ByStatus,
    Dictionary<string, int> ByPriority,
    int OverdueCount,
    int DueThisWeek);

public record UpdateNoticeStatusRequest(
    string Status,
    string? Reason = null);

public record AssignNoticeRequest(Guid AssigneeId);

public record DownloadUrlResponse(
    string Url,
    DateTime ExpiresAt);

public record ProcessingRetryResponse(
    Guid NoticeId,
    string JobId,
    string Status);

// Phase C DTOs

public record UpdateNoticeDetailsRequest(
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

public record AttachmentDto(
    Guid Id,
    string FileName,
    int? FileSize,
    string? FileType,
    string? DocumentType,
    string? Description,
    Guid UploadedById,
    string? UploadedByName,
    DateTime CreatedAt);

public record AddAttachmentRequest
{
    public IFormFile? File { get; init; }
    public string? DocumentType { get; init; }
    public string? Description { get; init; }
}

public record CommentDto(
    Guid Id,
    string Content,
    bool IsInternal,
    Guid? ParentId,
    Guid UserId,
    string? UserName,
    List<CommentDto> Replies,
    DateTime CreatedAt);

public record AddCommentRequest(
    string Content,
    bool IsInternal = false,
    Guid? ParentId = null);

public record NoticeTaskDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime? DueDate,
    string Priority,
    string Status,
    Guid? AssignedToId,
    string? AssignedToName,
    Guid CreatedById,
    string? CreatedByName,
    DateTime? CompletedAt,
    Guid? CompletedById,
    DateTime CreatedAt);

public record CreateNoticeTaskRequest(
    string Title,
    string? Description = null,
    DateTime? DueDate = null,
    string? Priority = null,
    Guid? AssignedToId = null);

public record UpdateNoticeTaskRequest(
    string? Title = null,
    string? Description = null,
    DateTime? DueDate = null,
    string? Priority = null,
    string? Status = null,
    Guid? AssignedToId = null);

// Phase D DTOs

public record NoticeResponseDto(
    Guid Id,
    Guid NoticeId,
    string? DraftContent,
    string? FinalContent,
    string Status,
    int Version,
    string? SubmissionReference,
    string? SubmissionProofUrl,
    DateTime? SubmittedAt,
    Guid CreatedById,
    string? CreatedByName,
    Guid? ApprovedById,
    string? ApprovedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record SaveDraftRequest(string DraftContent);

public record MarkSubmittedRequest(
    string? SubmissionReference = null,
    string? SubmissionProofUrl = null);

public record ReminderDto(
    Guid Id,
    Guid NoticeId,
    string ReminderType,
    DateTime RemindAt,
    int? DaysBefore,
    bool IsSent,
    DateTime? SentAt,
    Guid UserId,
    string? UserName,
    DateTime CreatedAt);

public record CreateReminderRequest(
    string ReminderType,
    DateTime RemindAt,
    int? DaysBefore = null);

public record NoticeStatisticsDto(
    Dictionary<string, int> ByStatus,
    Dictionary<string, int> ByPriority,
    int OverdueCount,
    int DueThisWeek,
    int DueThisMonth,
    decimal TotalDemandAmount,
    int TotalCount);

#endregion
