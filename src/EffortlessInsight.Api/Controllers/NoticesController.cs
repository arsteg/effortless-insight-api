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
    private readonly IZipProcessingService _zipProcessingService;
    private readonly INoticeWorkflowService _workflowService;
    private readonly INoticeResponseDraftService _responseDraftService;
    private readonly IAiServiceClient _aiServiceClient;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly ILogger<NoticesController> _logger;

    public NoticesController(
        INoticeServiceExtended noticeService,
        IZipProcessingService zipProcessingService,
        INoticeWorkflowService workflowService,
        INoticeResponseDraftService responseDraftService,
        IAiServiceClient aiServiceClient,
        ICurrentOrganizationService currentOrg,
        ILogger<NoticesController> logger)
    {
        _noticeService = noticeService;
        _zipProcessingService = zipProcessingService;
        _workflowService = workflowService;
        _responseDraftService = responseDraftService;
        _aiServiceClient = aiServiceClient;
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
    /// Create a notice manually without file upload
    /// </summary>
    [HttpPost("manual")]
    [ProducesResponseType(typeof(ApiResponse<NoticeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateManualNotice(
        [FromBody] CreateManualNoticeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            // Check permission
            if (!_currentOrg.HasPermission("notices.upload"))
            {
                return Forbid();
            }

            var notice = await _noticeService.CreateManualNoticeAsync(
                request,
                orgId,
                userId,
                cancellationToken);

            // Map to DTO
            var dto = MapToDto(notice);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<NoticeDto>(true, dto));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("GSTIN"))
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_GSTIN", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create manual notice");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "CREATE_FAILED", "An unexpected error occurred"));
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

    /// <summary>
    /// Batch upload multiple notice files
    /// </summary>
    [HttpPost("upload/batch")]
    [RequestSizeLimit(262_144_000)] // 250 MB total for batch
    [ProducesResponseType(typeof(ApiResponse<BatchUploadResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BatchUpload(
        [FromForm] BatchUploadRequest request,
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

            if (request.Files == null || request.Files.Count == 0)
            {
                return BadRequest(new ApiErrorResponse(false, "FILES_REQUIRED", "No files were uploaded"));
            }

            const int maxFiles = 20;
            if (request.Files.Count > maxFiles)
            {
                return BadRequest(new ApiErrorResponse(false, "TOO_MANY_FILES",
                    $"Maximum {maxFiles} files allowed per batch upload"));
            }

            var results = new List<BatchUploadItemResult>();
            var successCount = 0;
            var failureCount = 0;

            foreach (var file in request.Files)
            {
                try
                {
                    if (file.Length == 0)
                    {
                        results.Add(new BatchUploadItemResult(
                            FileName: file.FileName,
                            Success: false,
                            NoticeId: null,
                            ErrorCode: "EMPTY_FILE",
                            ErrorMessage: "File is empty"));
                        failureCount++;
                        continue;
                    }

                    using var stream = file.OpenReadStream();
                    var result = await _noticeService.UploadAsync(
                        stream,
                        file.FileName,
                        file.ContentType,
                        orgId,
                        userId,
                        request.Gstin,
                        request.Tags,
                        cancellationToken);

                    if (result.Success)
                    {
                        results.Add(new BatchUploadItemResult(
                            FileName: file.FileName,
                            Success: true,
                            NoticeId: result.NoticeId,
                            ErrorCode: null,
                            ErrorMessage: null,
                            Status: result.Status,
                            DuplicateWarning: result.DuplicateWarning != null
                                ? new DuplicateWarningDto(
                                    IsPotentialDuplicate: true,
                                    SimilarNoticeId: result.DuplicateWarning.ExistingNoticeId,
                                    SimilarNoticeNumber: result.DuplicateWarning.ExistingNoticeNumber,
                                    SimilarityScore: result.DuplicateWarning.SimilarityScore,
                                    UploadedAt: result.DuplicateWarning.UploadedAt)
                                : null));
                        successCount++;
                    }
                    else
                    {
                        results.Add(new BatchUploadItemResult(
                            FileName: file.FileName,
                            Success: false,
                            NoticeId: null,
                            ErrorCode: result.ErrorCode,
                            ErrorMessage: result.ErrorMessage));
                        failureCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload file {FileName} in batch", file.FileName);
                    results.Add(new BatchUploadItemResult(
                        FileName: file.FileName,
                        Success: false,
                        NoticeId: null,
                        ErrorCode: "UPLOAD_FAILED",
                        ErrorMessage: "An unexpected error occurred"));
                    failureCount++;
                }
            }

            var response = new BatchUploadResponse(
                TotalFiles: request.Files.Count,
                SuccessCount: successCount,
                FailureCount: failureCount,
                Results: results);

            return StatusCode(StatusCodes.Status202Accepted,
                new ApiResponse<BatchUploadResponse>(true, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process batch upload");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "BATCH_UPLOAD_FAILED", "An unexpected error occurred during batch upload"));
        }
    }

    /// <summary>
    /// Upload a ZIP file containing multiple notices
    /// </summary>
    [HttpPost("upload/zip")]
    [RequestSizeLimit(524_288_000)] // 500 MB for ZIP files
    [ProducesResponseType(typeof(ApiResponse<ZipUploadResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UploadZip(
        [FromForm] ZipUploadRequest request,
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

            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new ApiErrorResponse(false, "FILE_REQUIRED", "No file was uploaded"));
            }

            // Validate ZIP file
            using var stream = request.File.OpenReadStream();
            var validationResult = _zipProcessingService.ValidateZipFile(stream, request.File.FileName);

            if (!validationResult.IsValid)
            {
                return BadRequest(new ApiErrorResponse(false,
                    validationResult.ErrorCode ?? "INVALID_ZIP",
                    validationResult.ErrorMessage ?? "Invalid ZIP file"));
            }

            // Process the ZIP file
            stream.Position = 0;
            var result = await _zipProcessingService.ProcessZipUploadAsync(
                stream,
                request.File.FileName,
                orgId,
                userId,
                request.Gstin,
                request.Tags,
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(new ApiErrorResponse(false,
                    result.ErrorCode ?? "PROCESSING_FAILED",
                    result.ErrorMessage ?? "Failed to process ZIP file"));
            }

            var response = new ZipUploadResponse(
                ZipFileName: result.ZipFileName,
                TotalFilesInZip: result.TotalFilesInZip,
                ProcessedCount: result.ProcessedCount,
                SuccessCount: result.SuccessCount,
                FailureCount: result.FailureCount,
                SkippedCount: result.SkippedCount,
                Results: result.Results.Select(r => new ZipUploadItemResult(
                    FileName: r.FileName,
                    FullPath: r.FullPath,
                    Success: r.Success,
                    NoticeId: r.NoticeId,
                    Status: r.Status,
                    ErrorCode: r.ErrorCode,
                    ErrorMessage: r.ErrorMessage,
                    IsDuplicate: r.IsDuplicate
                )).ToList());

            return StatusCode(StatusCodes.Status202Accepted,
                new ApiResponse<ZipUploadResponse>(true, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process ZIP upload");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "ZIP_UPLOAD_FAILED", "An unexpected error occurred during ZIP upload"));
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
                FileUrl: a.FileUrl,
                FileSize: a.FileSize,
                FileType: a.FileType,
                DocumentType: a.DocumentType,
                Description: a.Description,
                Version: a.Version,
                IsCurrentVersion: a.IsCurrentVersion,
                HasPreviousVersions: a.PreviousVersionId.HasValue,
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
                FileUrl: attachment.FileUrl,
                FileSize: attachment.FileSize,
                FileType: attachment.FileType,
                DocumentType: attachment.DocumentType,
                Description: attachment.Description,
                Version: attachment.Version,
                IsCurrentVersion: attachment.IsCurrentVersion,
                HasPreviousVersions: attachment.PreviousVersionId.HasValue,
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

    /// <summary>
    /// Upload a new version of an attachment
    /// </summary>
    [HttpPost("{noticeId:guid}/attachments/{attachmentId:guid}/versions")]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadAttachmentVersion(
        Guid noticeId,
        Guid attachmentId,
        [FromForm] UploadNewVersionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new ApiErrorResponse(false, "VALIDATION_ERROR", "File is required"));
            }

            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            using var stream = request.File.OpenReadStream();
            var attachment = await _noticeService.UploadNewAttachmentVersionAsync(
                attachmentId,
                noticeId,
                orgId,
                userId,
                stream,
                request.File.FileName,
                request.File.ContentType,
                request.VersionNote,
                cancellationToken);

            var dto = new AttachmentDto(
                Id: attachment.Id,
                FileName: attachment.FileName,
                FileUrl: attachment.FileUrl,
                FileSize: attachment.FileSize,
                FileType: attachment.FileType,
                DocumentType: attachment.DocumentType,
                Description: attachment.Description,
                Version: attachment.Version,
                IsCurrentVersion: attachment.IsCurrentVersion,
                HasPreviousVersions: attachment.PreviousVersionId.HasValue,
                CreatedAt: attachment.CreatedAt);

            return CreatedAtAction(
                nameof(GetAttachmentVersionHistory),
                new { noticeId, attachmentId = attachment.Id },
                new ApiResponse<AttachmentDto>(true, dto));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload new version for attachment {AttachmentId}", attachmentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get version history for an attachment
    /// </summary>
    [HttpGet("{noticeId:guid}/attachments/{attachmentId:guid}/versions")]
    [ProducesResponseType(typeof(ApiResponse<AttachmentVersionHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachmentVersionHistory(
        Guid noticeId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var result = await _noticeService.GetAttachmentVersionHistoryAsync(
                attachmentId, noticeId, orgId, cancellationToken);

            return Ok(new ApiResponse<AttachmentVersionHistoryResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get version history for attachment {AttachmentId}", attachmentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    // NOTE: Legacy comment endpoints removed - use CommentsController instead
    // The duplicate routes were causing AmbiguousMatchException

    // NOTE: Legacy task endpoints removed - use TasksController instead
    // The duplicate routes were causing AmbiguousMatchException

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
    /// Generate an AI-powered auto-draft response for a notice.
    /// Uses the notice content and AI analysis to generate a professional response draft.
    /// </summary>
    /// <remarks>
    /// Rate limited to 10 requests per minute per organization.
    /// The draft is generated based on:
    /// - Notice metadata (type, dates, amounts)
    /// - OCR extracted content
    /// - AI analysis report (if available)
    /// - User-specified tone and language preferences
    /// </remarks>
    [HttpPost("{noticeId:guid}/responses/auto-draft")]
    [ProducesResponseType(typeof(ApiResponse<AutoDraftResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateAutoDraft(
        Guid noticeId,
        [FromBody] AutoDraftRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orgId = GetCurrentOrganizationId();
            var userId = GetCurrentUserId();

            if (!_currentOrg.HasPermission("notices.edit"))
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    new ApiErrorResponse(false, "FORBIDDEN", "You don't have permission to generate auto-drafts"));
            }

            // Build options from request (service will sanitize and validate)
            var options = request != null
                ? new AutoDraftOptions
                {
                    Tone = request.Tone ?? AutoDraftTone.Formal,
                    Language = request.Language ?? AutoDraftLanguage.English,
                    PointsToAddress = request.PointsToAddress,
                    AdditionalInstructions = request.AdditionalInstructions
                }
                : null;

            var result = await _responseDraftService.GenerateAutoDraftAsync(
                noticeId, orgId, userId, options, cancellationToken);

            if (!result.Success)
            {
                return result.ErrorCode switch
                {
                    "NOTICE_NOT_FOUND" => NotFound(new ApiErrorResponse(false, result.ErrorCode, result.ErrorMessage!)),
                    "RATE_LIMIT_EXCEEDED" => StatusCode(StatusCodes.Status429TooManyRequests,
                        new ApiErrorResponse(false, result.ErrorCode, result.ErrorMessage!)),
                    "TIMEOUT" => StatusCode(StatusCodes.Status504GatewayTimeout,
                        new ApiErrorResponse(false, result.ErrorCode, result.ErrorMessage!)),
                    "NO_CONTENT" or "VALIDATION_ERROR" or "EMPTY_RESPONSE" =>
                        BadRequest(new ApiErrorResponse(false, result.ErrorCode, result.ErrorMessage!)),
                    _ => StatusCode(StatusCodes.Status500InternalServerError,
                        new ApiErrorResponse(false, result.ErrorCode ?? "INTERNAL_ERROR", result.ErrorMessage ?? "An unexpected error occurred"))
                };
            }

            var response = new AutoDraftResponseDto(
                DraftContent: result.DraftContent!,
                Metadata: new AutoDraftMetadataDto(
                    Model: result.Metadata!.Model,
                    ProcessingTimeMs: result.Metadata.ProcessingTimeMs,
                    NoticeType: result.Metadata.NoticeType,
                    InputTokens: result.Metadata.InputTokens,
                    OutputTokens: result.Metadata.OutputTokens,
                    EstimatedCost: result.Metadata.EstimatedCost
                )
            );

            return Ok(new ApiResponse<AutoDraftResponseDto>(true, response));
        }
        catch (OperationCanceledException)
        {
            // Request was cancelled by client
            return StatusCode(499, new ApiErrorResponse(false, "REQUEST_CANCELLED", "Request was cancelled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate auto-draft for notice {NoticeId}", noticeId);
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

    #region Export

    /// <summary>
    /// Export notices to CSV, Excel, or PDF
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromQuery] NoticeFilterDto filter,
        [FromQuery] string format = "csv",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.view"))
            {
                return Forbid();
            }

            var validFormats = new[] { "csv", "xlsx", "pdf" };
            if (!validFormats.Contains(format.ToLower()))
            {
                return BadRequest(new ApiErrorResponse(false, "INVALID_FORMAT",
                    "Format must be one of: csv, xlsx, pdf"));
            }

            var orgId = GetCurrentOrganizationId();
            var result = await _noticeService.GetListAsync(orgId, filter, cancellationToken);
            var notices = result.Items.Select(MapToDto).ToList();

            var (fileBytes, contentType, fileName) = format.ToLower() switch
            {
                "csv" => GenerateCsv(notices),
                "xlsx" => GenerateExcel(notices),
                "pdf" => GeneratePdf(notices),
                _ => throw new InvalidOperationException("Invalid format")
            };

            return File(fileBytes, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export notices");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    private (byte[] FileBytes, string ContentType, string FileName) GenerateCsv(List<NoticeDto> notices)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Notice Number,Type,Category,GSTIN,Status,Priority,Issue Date,Response Deadline,Tax Amount,Penalty Amount,Risk Score,Assigned To");

        foreach (var notice in notices)
        {
            sb.AppendLine($"\"{notice.NoticeNumber ?? ""}\",\"{notice.NoticeType ?? ""}\",\"{notice.NoticeCategory ?? ""}\",\"{notice.Gstin ?? ""}\",\"{notice.Status}\",\"{notice.Priority}\",\"{notice.IssueDate?.ToString("yyyy-MM-dd") ?? ""}\",\"{notice.ResponseDeadline?.ToString("yyyy-MM-dd") ?? ""}\",{notice.TaxAmount ?? 0},{notice.PenaltyAmount ?? 0},{notice.RiskScore ?? 0},\"{notice.AssignedToName ?? ""}\"");
        }

        return (System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"notices-export-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    private (byte[] FileBytes, string ContentType, string FileName) GenerateExcel(List<NoticeDto> notices)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Notices");

        // Headers
        var headers = new[] { "Notice Number", "Type", "Category", "GSTIN", "Status", "Priority",
            "Issue Date", "Response Deadline", "Tax Amount", "Penalty Amount", "Risk Score", "Assigned To" };
        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
            worksheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        // Data
        for (int row = 0; row < notices.Count; row++)
        {
            var notice = notices[row];
            worksheet.Cell(row + 2, 1).Value = notice.NoticeNumber ?? "";
            worksheet.Cell(row + 2, 2).Value = notice.NoticeType ?? "";
            worksheet.Cell(row + 2, 3).Value = notice.NoticeCategory ?? "";
            worksheet.Cell(row + 2, 4).Value = notice.Gstin ?? "";
            worksheet.Cell(row + 2, 5).Value = notice.Status;
            worksheet.Cell(row + 2, 6).Value = notice.Priority;
            worksheet.Cell(row + 2, 7).Value = notice.IssueDate?.ToString("yyyy-MM-dd") ?? "";
            worksheet.Cell(row + 2, 8).Value = notice.ResponseDeadline?.ToString("yyyy-MM-dd") ?? "";
            worksheet.Cell(row + 2, 9).Value = (double)(notice.TaxAmount ?? 0);
            worksheet.Cell(row + 2, 10).Value = (double)(notice.PenaltyAmount ?? 0);
            worksheet.Cell(row + 2, 11).Value = notice.RiskScore ?? 0;
            worksheet.Cell(row + 2, 12).Value = notice.AssignedToName ?? "";
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"notices-export-{DateTime.UtcNow:yyyy-MM-dd}.xlsx");
    }

    private (byte[] FileBytes, string ContentType, string FileName) GeneratePdf(List<NoticeDto> notices)
    {
        // Use a dedicated PDF generator class to avoid namespace conflicts
        var pdfGenerator = new Services.Reporting.NoticesPdfExportGenerator();
        var pdfBytes = pdfGenerator.Generate(notices);
        return (pdfBytes, "application/pdf", $"notices-export-{DateTime.UtcNow:yyyy-MM-dd}.pdf");
    }

    #endregion

    #region SLA Rules

    /// <summary>
    /// Get the SLA and priority calculation rules
    /// </summary>
    [HttpGet("sla-rules")]
    [ProducesResponseType(typeof(ApiResponse<SlaRulesDto>), StatusCodes.Status200OK)]
    public IActionResult GetSlaRules()
    {
        var rules = _workflowService.GetSlaRules();
        return Ok(new ApiResponse<SlaRulesDto>(true, rules));
    }

    /// <summary>
    /// Calculate priority for given parameters with detailed breakdown
    /// </summary>
    [HttpPost("calculate-priority")]
    [ProducesResponseType(typeof(ApiResponse<PriorityCalculationResult>), StatusCodes.Status200OK)]
    public IActionResult CalculatePriority([FromBody] CalculatePriorityRequest request)
    {
        var result = _workflowService.CalculatePriorityWithDetails(
            request.NoticeType,
            request.NoticeCategory,
            request.ResponseDeadline,
            request.TotalDemand);

        return Ok(new ApiResponse<PriorityCalculationResult>(true, result));
    }

    #endregion

    #region Relationships

    /// <summary>
    /// Get all relationships for a notice
    /// </summary>
    [HttpGet("{noticeId:guid}/relationships")]
    [ProducesResponseType(typeof(ApiResponse<NoticeRelationshipsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRelationships(Guid noticeId, CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.view"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();
            var result = await _noticeService.GetRelationshipsAsync(noticeId, orgId, cancellationToken);

            return Ok(new ApiResponse<NoticeRelationshipsResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "NOTICE_NOT_FOUND")
        {
            return NotFound(new ApiErrorResponse(false, "NOTICE_NOT_FOUND", "Notice not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get notice relationships for {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Create a relationship between two notices
    /// </summary>
    [HttpPost("{noticeId:guid}/relationships")]
    [ProducesResponseType(typeof(ApiResponse<NoticeRelationshipDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateRelationship(
        Guid noticeId,
        [FromBody] CreateNoticeRelationshipRequest request,
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

            var result = await _noticeService.CreateRelationshipAsync(
                noticeId, orgId, userId, request, cancellationToken);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<NoticeRelationshipDto>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "SOURCE_NOTICE_NOT_FOUND")
        {
            return NotFound(new ApiErrorResponse(false, "SOURCE_NOTICE_NOT_FOUND", "Source notice not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "TARGET_NOTICE_NOT_FOUND")
        {
            return NotFound(new ApiErrorResponse(false, "TARGET_NOTICE_NOT_FOUND", "Target notice not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_LINK_TO_SELF")
        {
            return BadRequest(new ApiErrorResponse(false, "CANNOT_LINK_TO_SELF", "Cannot create relationship to the same notice"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "RELATIONSHIP_EXISTS")
        {
            return BadRequest(new ApiErrorResponse(false, "RELATIONSHIP_EXISTS", "This relationship already exists"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_RELATIONSHIP_TYPE", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create notice relationship for {NoticeId}", noticeId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Delete a notice relationship
    /// </summary>
    [HttpDelete("{noticeId:guid}/relationships/{relationshipId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRelationship(
        Guid noticeId,
        Guid relationshipId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.edit"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();

            await _noticeService.DeleteRelationshipAsync(relationshipId, noticeId, orgId, cancellationToken);

            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message == "RELATIONSHIP_NOT_FOUND")
        {
            return NotFound(new ApiErrorResponse(false, "RELATIONSHIP_NOT_FOUND", "Relationship not found"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete notice relationship {RelationshipId}", relationshipId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #endregion

    #region Similar Notices

    /// <summary>
    /// Get AI-detected similar notices from the same organization
    /// </summary>
    [HttpGet("{noticeId:guid}/similar")]
    [ProducesResponseType(typeof(ApiResponse<SimilarNoticeDto[]>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSimilarNotices(
        Guid noticeId,
        [FromQuery] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_currentOrg.HasPermission("notices.view"))
            {
                return Forbid();
            }

            var orgId = GetCurrentOrganizationId();

            // Verify the notice exists and belongs to this organization
            var notice = await _noticeService.GetByIdAsync(noticeId, orgId, cancellationToken);
            if (notice == null)
            {
                return NotFound(new ApiErrorResponse(false, "NOTICE_NOT_FOUND", "Notice not found"));
            }

            // Get similar notices from AI service
            var similarNotices = await _aiServiceClient.FindSimilarNoticesAsync(noticeId, limit);

            // Enrich with notice details from database
            var enrichedNotices = new List<SimilarNoticeDto>();
            foreach (var similar in similarNotices)
            {
                var similarNotice = await _noticeService.GetByIdAsync(similar.NoticeId, orgId, cancellationToken);
                if (similarNotice != null)
                {
                    enrichedNotices.Add(new SimilarNoticeDto(
                        Id: similarNotice.Id,
                        NoticeNumber: similarNotice.NoticeNumber,
                        NoticeType: similarNotice.NoticeType,
                        Status: similarNotice.Status,
                        SimilarityScore: (decimal)similar.SimilarityScore,
                        Summary: similar.Summary ?? similarNotice.AiReport?.SummaryEn,
                        ResponseDeadline: similarNotice.ResponseDeadline
                    ));
                }
            }

            return Ok(new ApiResponse<SimilarNoticeDto[]>(true, enrichedNotices.ToArray()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get similar notices for {NoticeId}", noticeId);
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
                .ToDictionary(kvp => kvp.Key, kvp => ConvertToInt(kvp.Value)),
            ModelUsed: report.ModelUsed,
            ProcessingTimeMs: report.ProcessingTimeMs,
            CreatedAt: report.CreatedAt);
    }

    /// <summary>
    /// Helper to convert object (potentially JsonElement) to int
    /// </summary>
    private static int ConvertToInt(object value)
    {
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number
                ? jsonElement.GetInt32()
                : int.TryParse(jsonElement.GetString(), out var parsed) ? parsed : 0;
        }
        return Convert.ToInt32(value);
    }

    /// <summary>
    /// Helper to convert object (potentially JsonElement) to nullable int
    /// </summary>
    private static int? ConvertToNullableInt(object? value)
    {
        if (value == null) return null;
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
            return jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number
                ? jsonElement.GetInt32()
                : int.TryParse(jsonElement.GetString(), out var parsed) ? parsed : null;
        }
        return Convert.ToInt32(value);
    }

    /// <summary>
    /// Helper to convert object (potentially JsonElement) to bool
    /// </summary>
    private static bool ConvertToBool(object value)
    {
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind == System.Text.Json.JsonValueKind.True ||
                   (jsonElement.ValueKind == System.Text.Json.JsonValueKind.String &&
                    bool.TryParse(jsonElement.GetString(), out var parsed) && parsed);
        }
        return Convert.ToBoolean(value);
    }

    /// <summary>
    /// Helper to convert object (potentially JsonElement) to string
    /// </summary>
    private static string ConvertToString(object? value)
    {
        if (value == null) return "";
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind == System.Text.Json.JsonValueKind.Null
                ? ""
                : jsonElement.ToString();
        }
        return value.ToString() ?? "";
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
                        Priority: dict.TryGetValue("priority", out var p) ? ConvertToInt(p) : index + 1,
                        Action: dict.TryGetValue("action", out var a) ? ConvertToString(a) : "",
                        Description: dict.TryGetValue("description", out var d) ? ConvertToString(d) : "",
                        DueInDays: dict.TryGetValue("due_in_days", out var days) ? ConvertToNullableInt(days) : null,
                        AssigneeSuggestion: dict.TryGetValue("assignee_suggestion", out var s) ? ConvertToString(s) : null);
                }
                if (item is System.Text.Json.JsonElement jsonItem && jsonItem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    return new ActionItemDto(
                        Priority: jsonItem.TryGetProperty("priority", out var p) ? p.GetInt32() : index + 1,
                        Action: jsonItem.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "",
                        Description: jsonItem.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                        DueInDays: jsonItem.TryGetProperty("dueInDays", out var days) && days.ValueKind != System.Text.Json.JsonValueKind.Null ? days.GetInt32() : null,
                        AssigneeSuggestion: jsonItem.TryGetProperty("assigneeSuggestion", out var s) ? s.GetString() : null);
                }
                return new ActionItemDto(index + 1, item.ToString()!, "", null, null);
            }).ToList();
        }
        // Handle case where items is a JsonElement array
        if (actionItems.TryGetValue("items", out var jsonItems) && jsonItems is System.Text.Json.JsonElement jsonArray && jsonArray.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return jsonArray.EnumerateArray().Select((item, index) => new ActionItemDto(
                Priority: item.TryGetProperty("priority", out var p) ? p.GetInt32() : index + 1,
                Action: item.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "",
                Description: item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                DueInDays: item.TryGetProperty("dueInDays", out var days) && days.ValueKind != System.Text.Json.JsonValueKind.Null ? days.GetInt32() : null,
                AssigneeSuggestion: item.TryGetProperty("assigneeSuggestion", out var s) ? s.GetString() : null
            )).ToList();
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
                        Document: dict.TryGetValue("document", out var d) ? ConvertToString(d) : "",
                        Mandatory: dict.TryGetValue("mandatory", out var m) && ConvertToBool(m));
                }
                if (item is System.Text.Json.JsonElement jsonItem && jsonItem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    return new RequiredDocumentDto(
                        Document: jsonItem.TryGetProperty("document", out var d) ? d.GetString() ?? "" : "",
                        Mandatory: jsonItem.TryGetProperty("mandatory", out var m) && m.GetBoolean());
                }
                return new RequiredDocumentDto(item.ToString()!, true);
            }).ToList();
        }
        // Handle case where items is a JsonElement array
        if (documents.TryGetValue("items", out var jsonItems) && jsonItems is System.Text.Json.JsonElement jsonArray && jsonArray.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return jsonArray.EnumerateArray().Select(item => new RequiredDocumentDto(
                Document: item.TryGetProperty("document", out var d) ? d.GetString() ?? "" : "",
                Mandatory: item.TryGetProperty("mandatory", out var m) && m.GetBoolean()
            )).ToList();
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
                        Section: dict.TryGetValue("section", out var s) ? ConvertToString(s) : "",
                        Description: dict.TryGetValue("description", out var d) ? ConvertToString(d) : "");
                }
                if (item is System.Text.Json.JsonElement jsonItem && jsonItem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    return new LegalReferenceDto(
                        Section: jsonItem.TryGetProperty("section", out var s) ? s.GetString() ?? "" : "",
                        Description: jsonItem.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "");
                }
                return new LegalReferenceDto(item.ToString()!, "");
            }).ToList();
        }
        // Handle case where items is a JsonElement array
        if (references.TryGetValue("items", out var jsonItems) && jsonItems is System.Text.Json.JsonElement jsonArray && jsonArray.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return jsonArray.EnumerateArray().Select(item => new LegalReferenceDto(
                Section: item.TryGetProperty("section", out var s) ? s.GetString() ?? "" : "",
                Description: item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""
            )).ToList();
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

public record BatchUploadRequest
{
    public IFormFileCollection? Files { get; init; }
    public string? Gstin { get; init; }
    public List<string>? Tags { get; init; }
}

public record BatchUploadResponse(
    int TotalFiles,
    int SuccessCount,
    int FailureCount,
    List<BatchUploadItemResult> Results);

public record BatchUploadItemResult(
    string FileName,
    bool Success,
    Guid? NoticeId,
    string? ErrorCode,
    string? ErrorMessage,
    string? Status = null,
    DuplicateWarningDto? DuplicateWarning = null);

public record ZipUploadRequest
{
    public IFormFile? File { get; init; }
    public string? Gstin { get; init; }
    public string[]? Tags { get; init; }
}

public record ZipUploadResponse(
    string ZipFileName,
    int TotalFilesInZip,
    int ProcessedCount,
    int SuccessCount,
    int FailureCount,
    int SkippedCount,
    List<ZipUploadItemResult> Results);

public record ZipUploadItemResult(
    string FileName,
    string FullPath,
    bool Success,
    Guid? NoticeId,
    string? Status,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsDuplicate);

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

// AutoDraftRequest, AutoDraftResponseDto, AutoDraftMetadataDto moved to DTOs/NoticeDtos.cs

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

public record CalculatePriorityRequest(
    string? NoticeType,
    string? NoticeCategory,
    DateOnly? ResponseDeadline,
    decimal? TotalDemand);

#endregion
