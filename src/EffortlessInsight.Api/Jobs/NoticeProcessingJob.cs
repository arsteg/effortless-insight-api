using System.Diagnostics;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Collaboration;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Storage;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background job for processing notices with AI.
/// Handles OCR, data extraction, risk analysis, and report generation.
/// </summary>
public class NoticeProcessingJob : INoticeProcessingJob
{
    private readonly ApplicationDbContext _db;
    private readonly IAiServiceClient _aiService;
    private readonly IFileStorageServiceExtended _storageService;
    private readonly INoticeWorkflowService _workflowService;
    private readonly IAuditService _auditService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly INotificationService _notificationService;
    private readonly INoticeBroadcastService _broadcastService;
    private readonly ILogger<NoticeProcessingJob> _logger;

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    ];

    public NoticeProcessingJob(
        ApplicationDbContext db,
        IAiServiceClient aiService,
        IFileStorageServiceExtended storageService,
        INoticeWorkflowService workflowService,
        IAuditService auditService,
        IBackgroundJobClient backgroundJobs,
        INotificationService notificationService,
        INoticeBroadcastService broadcastService,
        ILogger<NoticeProcessingJob> logger)
    {
        _db = db;
        _aiService = aiService;
        _storageService = storageService;
        _workflowService = workflowService;
        _auditService = auditService;
        _backgroundJobs = backgroundJobs;
        _notificationService = notificationService;
        _broadcastService = broadcastService;
        _logger = logger;
    }

    /// <summary>
    /// Process a notice with AI analysis.
    /// </summary>
    [AutomaticRetry(Attempts = 0)] // We handle retries manually for more control
    [Queue("default")]
    public async Task ProcessAsync(Guid noticeId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting AI processing for notice {NoticeId}", noticeId);

        var notice = await _db.Notices
            .Include(n => n.AiReport)
            .FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);

        if (notice == null)
        {
            _logger.LogWarning("Notice {NoticeId} not found for processing", noticeId);
            return;
        }

        // Check if already processed
        if (notice.Status == NoticeStatus.Analyzed ||
            notice.ProcessingStatus == NoticeProcessingStatus.Completed)
        {
            _logger.LogInformation("Notice {NoticeId} already processed, skipping", noticeId);
            return;
        }

        // A recovered handover job may already be queued. Only one worker may
        // transition a queued/failed notice into processing (optimistic concurrency).
        if (notice.ProcessingStatus != NoticeProcessingStatus.Queued &&
            notice.ProcessingStatus != NoticeProcessingStatus.Retrying &&
            notice.ProcessingStatus != NoticeProcessingStatus.Failed) return;

        // Update status to OCR processing (first stage)
        notice.Status = NoticeStatus.Processing;
        notice.ProcessingStatus = NoticeProcessingStatus.OcrProcessing;
        notice.ProcessingStartedAt = DateTime.UtcNow;
        notice.ProcessingAttempts++;
        notice.UpdatedAt = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation("Notice {NoticeId} processing was claimed or ownership changed", noticeId);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Generate presigned URL for AI service to access the file
            var downloadResult = await _storageService.GenerateDownloadUrlAsync(
                notice.FileUrl,
                notice.FileName,
                expiryMinutes: 30,
                cancellationToken);

            // Update status to extracting (file URL ready, AI will extract entities)
            await UpdateProcessingStatusAsync(notice, NoticeProcessingStatus.Extracting, cancellationToken);

            // Short delay to ensure status is visible to polling clients
            await Task.Delay(100, cancellationToken);

            // Update status to classifying (about to call AI for classification)
            await UpdateProcessingStatusAsync(notice, NoticeProcessingStatus.Classifying, cancellationToken);

            // Call AI service (this does the actual processing)
            var result = await _aiService.ProcessNoticeAsync(noticeId, downloadResult.Url, notice.OrganizationId);

            stopwatch.Stop();

            if (result.Success && result.Report != null)
            {
                try
                {
                    // Update status to analyzing (processing AI results)
                    await UpdateProcessingStatusAsync(notice, NoticeProcessingStatus.Analyzing, cancellationToken);

                    await HandleSuccessAsync(notice, result, stopwatch.ElapsedMilliseconds, cancellationToken);
                }
                catch (Exception successEx)
                {
                    _logger.LogError(successEx, "Error in HandleSuccessAsync for notice {NoticeId}", noticeId);
                    throw;
                }
            }
            else
            {
                _logger.LogWarning(
                    "AI service returned unsuccessful result for notice {NoticeId}. Success={Success}, HasReport={HasReport}, Error={Error}",
                    noticeId, result.Success, result.Report != null, result.Error);
                await HandleFailureAsync(notice, result.Error ?? "Unknown error", stopwatch.ElapsedMilliseconds, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing notice {NoticeId}. Exception type: {ExceptionType}, Message: {Message}",
                noticeId, ex.GetType().Name, ex.Message);

            try
            {
                // A failed routing transaction can leave ownership/GSTIN changes
                // tracked in memory. Persist failure against the last committed row.
                _db.ChangeTracker.Clear();
                notice = await _db.Notices.IgnoreQueryFilters().FirstOrDefaultAsync(n => n.Id == noticeId && n.DeletedAt == null, cancellationToken);
                if (notice == null || notice.ProcessingStatus == NoticeProcessingStatus.Completed
                    || notice.ProcessingStatus == NoticeProcessingStatus.Queued) return;
                await HandleFailureAsync(notice, ex.Message, stopwatch.ElapsedMilliseconds, cancellationToken);
            }
            catch (Exception failureEx)
            {
                _logger.LogError(failureEx, "HandleFailureAsync also failed for notice {NoticeId}", noticeId);
                // Re-throw the original exception
                throw ex;
            }
        }
    }

    /// <summary>
    /// Updates the processing status and saves to database.
    /// </summary>
    private async Task UpdateProcessingStatusAsync(
        Notice notice,
        string status,
        CancellationToken cancellationToken)
    {
        notice.ProcessingStatus = status;
        notice.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Notice {NoticeId} processing status updated to {Status}",
            notice.Id, status);
    }

    private async Task HandleSuccessAsync(
        Notice notice,
        DTOs.AiProcessingResult result,
        long processingTimeMs,
        CancellationToken cancellationToken)
    {
        var report = result.Report!;

        var detectedGstin = report.Metadata.Gstin?.Trim().ToUpperInvariant();
        var isCaWorkspace = await _db.OrganizationMembers.AnyAsync(m => m.OrganizationId == notice.OrganizationId
            && m.Role == "owner" && m.User.IsCA && m.Status == "active" && m.DeletedAt == null, cancellationToken);
        if (isCaWorkspace && !string.IsNullOrWhiteSpace(detectedGstin) && !new Services.Organizations.GstinValidatorService(_db).Validate(detectedGstin).IsValid)
            throw new InvalidOperationException("GSTIN_REVIEW_REQUIRED: The detected GSTIN is invalid. Review the document and correct its GSTIN.");
        if (isCaWorkspace && string.IsNullOrWhiteSpace(detectedGstin) && string.IsNullOrWhiteSpace(notice.Gstin))
            throw new InvalidOperationException("GSTIN_REVIEW_REQUIRED: No GSTIN could be identified. Select the client GSTIN and retry processing.");

        if (notice.CaProspectClientId != null && !string.IsNullOrWhiteSpace(notice.Gstin)
            && !string.IsNullOrWhiteSpace(report.Metadata.Gstin)
            && !string.Equals(notice.Gstin.Trim(), report.Metadata.Gstin.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The document GSTIN does not match the selected client GSTIN. Review the document before processing it again.");

        // Update notice with extracted metadata
        notice.NoticeType = report.Metadata.NoticeType;
        notice.NoticeCategory = report.Metadata.NoticeCategory;
        notice.NoticeNumber = report.Metadata.NoticeNumber ?? notice.NoticeNumber;
        notice.Gstin = string.IsNullOrWhiteSpace(detectedGstin) ? notice.Gstin : detectedGstin;

        // Link notice to CA prospect client after GSTIN extraction
        if (notice.CaProspectClientId == null && !string.IsNullOrEmpty(notice.Gstin))
        {
            var uploader = await _db.Users.FindAsync(notice.UploadedById);
            if (uploader?.IsCA == true)
            {
                var gstinHash = ICrossOrgNoticeVisibilityService.ComputeGstinHash(notice.Gstin);
                var stagingClient = await _db.CaProspectClients
                    .FirstOrDefaultAsync(p =>
                        p.CaUserId == notice.UploadedById &&
                        p.GstinHash == gstinHash &&
                        p.Status == "staging" &&
                        p.DeletedAt == null,
                        cancellationToken);

                if (stagingClient != null)
                {
                    notice.CaProspectClientId = stagingClient.Id;
                    _logger.LogInformation(
                        "Linked notice {NoticeId} to CA prospect client {ProspectClientId} after GSTIN extraction",
                        notice.Id, stagingClient.Id);
                }
            }
        }

        notice.IssueDate = report.Metadata.IssueDate;
        notice.ResponseDeadline = report.Metadata.ResponseDeadline;
        notice.TaxAmount = report.Metadata.TaxAmount;
        notice.PenaltyAmount = report.Metadata.PenaltyAmount;
        notice.InterestAmount = report.Metadata.InterestAmount;
        notice.PeriodFrom = report.Metadata.PeriodFrom;
        notice.PeriodTo = report.Metadata.PeriodTo;
        notice.IssuingAuthority = report.Metadata.IssuingAuthority;

        // Calculate priority based on extracted data
        notice.Priority = _workflowService.CalculatePriority(
            notice.NoticeType,
            notice.NoticeCategory,
            notice.ResponseDeadline,
            notice.TaxAmount + notice.PenaltyAmount + notice.InterestAmount);

        // Update processing status
        notice.Status = _workflowService.GetStatusAfterProcessing(true);
        notice.ProcessingStatus = NoticeProcessingStatus.Completed;
        notice.ProcessingCompletedAt = DateTime.UtcNow;
        notice.ProcessingError = null;
        notice.UpdatedAt = DateTime.UtcNow;

        // Create or update AI report
        if (notice.AiReport == null)
        {
            notice.AiReport = new NoticeAiReport
            {
                NoticeId = notice.Id,
                ReportVersion = 1
            };
            _db.NoticeAiReports.Add(notice.AiReport);
        }
        else
        {
            notice.AiReport.ReportVersion++;
        }

        notice.AiReport.RiskScore = report.RiskScore;
        notice.AiReport.RiskLevel = report.RiskLevel;
        notice.AiReport.SummaryEn = report.SummaryEn;
        notice.AiReport.SummaryHi = report.SummaryHi;
        notice.AiReport.PlainEnglish = report.PlainEnglish;
        notice.AiReport.ActionItems = SerializeList(report.ActionItems);
        notice.AiReport.RequiredDocuments = SerializeList(report.RequiredDocuments);
        notice.AiReport.LegalReferences = SerializeList(report.LegalReferences);
        notice.AiReport.ConfidenceScores = report.ConfidenceScores?.ToDictionary(
            kvp => kvp.Key,
            kvp => (object)kvp.Value);
        notice.AiReport.ProcessingTimeMs = (int)processingTimeMs;
        notice.AiReport.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        // Broadcast real-time update to connected clients
        try
        {
            await _broadcastService.BroadcastStatusUpdateAsync(notice, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast status update for notice {NoticeId}", notice.Id);
        }

        // Audit log
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "notice.processed",
            EntityType = "Notice",
            EntityId = notice.Id,
            OrganizationId = notice.OrganizationId,
            NewValues = new Dictionary<string, object>
            {
                ["risk_score"] = report.RiskScore,
                ["risk_level"] = report.RiskLevel,
                ["processing_time_ms"] = processingTimeMs,
                ["notice_type"] = notice.NoticeType ?? "unknown"
            }
        });

        _logger.LogInformation(
            "Notice {NoticeId} processed successfully. Risk: {RiskLevel} ({RiskScore}), Time: {ProcessingTimeMs}ms",
            notice.Id, report.RiskLevel, report.RiskScore, processingTimeMs);

        // Send success notification to uploader
        try
        {
            await _notificationService.NotifyNoticeProcessingCompleteAsync(notice);
        }
        catch (Exception ex)
        {
            // Don't fail the job if notification fails
            _logger.LogWarning(ex, "Failed to send processing complete notification for notice {NoticeId}", notice.Id);
        }

        // Send WhatsApp alert for high-risk notices
        if (notice.Priority?.ToLower() == "high" || report.RiskLevel?.ToLower() == "high")
        {
            try
            {
                WhatsAppJobsExtensions.QueueHighRiskAlert(notice.Id);
                _logger.LogInformation(
                    "Queued WhatsApp high-risk alert for notice {NoticeId}",
                    notice.Id);
            }
            catch (Exception ex)
            {
                // Don't fail the job if WhatsApp notification fails
                _logger.LogWarning(ex, "Failed to queue WhatsApp high-risk alert for notice {NoticeId}", notice.Id);
            }
        }
    }

    private async Task HandleFailureAsync(
        Notice notice,
        string error,
        long processingTimeMs,
        CancellationToken cancellationToken)
    {
        notice.ProcessingError = error;
        notice.ProcessingCompletedAt = DateTime.UtcNow;
        notice.UpdatedAt = DateTime.UtcNow;

        if (notice.ProcessingAttempts < MaxRetries)
        {
            // Schedule retry with exponential backoff
            var delay = RetryDelays[Math.Min(notice.ProcessingAttempts - 1, RetryDelays.Length - 1)];
            notice.ProcessingStatus = NoticeProcessingStatus.Retrying;

            await _db.SaveChangesAsync(cancellationToken);

            // Broadcast retry update to connected clients
            try
            {
                await _broadcastService.BroadcastStatusUpdateAsync(notice, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast retry update for notice {NoticeId}", notice.Id);
            }

            _backgroundJobs.Schedule<INoticeProcessingJob>(
                job => job.ProcessAsync(notice.Id, CancellationToken.None),
                delay);

            _logger.LogWarning(
                "Notice {NoticeId} processing failed (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}. Error: {Error}",
                notice.Id, notice.ProcessingAttempts, MaxRetries, delay, error);
        }
        else
        {
            // Max retries exhausted
            notice.Status = _workflowService.GetStatusAfterProcessing(false);
            notice.ProcessingStatus = NoticeProcessingStatus.Failed;

            await _db.SaveChangesAsync(cancellationToken);

            // Broadcast failure update to connected clients
            try
            {
                await _broadcastService.BroadcastStatusUpdateAsync(notice, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast failure update for notice {NoticeId}", notice.Id);
            }

            // Audit log
            await _auditService.LogAsync(new AuditLogEntry
            {
                Action = "notice.processing_failed",
                EntityType = "Notice",
                EntityId = notice.Id,
                OrganizationId = notice.OrganizationId,
                NewValues = new Dictionary<string, object>
                {
                    ["error"] = error,
                    ["attempts"] = notice.ProcessingAttempts,
                    ["processing_time_ms"] = processingTimeMs
                }
            });

            _logger.LogError(
                "Notice {NoticeId} processing failed after {Attempts} attempts. Error: {Error}",
                notice.Id, notice.ProcessingAttempts, error);

            // Send failure notification to uploader
            try
            {
                await _notificationService.NotifyNoticeProcessingFailedAsync(
                    notice, error, notice.ProcessingAttempts);
            }
            catch (Exception ex)
            {
                // Don't fail if notification fails - job already failed
                _logger.LogWarning(ex, "Failed to send processing failed notification for notice {NoticeId}", notice.Id);
            }
        }
    }

    private static Dictionary<string, object>? SerializeList<T>(List<T>? items)
    {
        if (items == null || items.Count == 0)
            return null;

        return new Dictionary<string, object>
        {
            ["items"] = items.Cast<object>().ToList()
        };
    }
}

/// <summary>
/// Processing status constants for notice AI processing.
/// These statuses are displayed to the user during the upload flow.
/// </summary>
public static class NoticeProcessingStatus
{
    /// <summary>Notice is queued for processing</summary>
    public const string Queued = "queued";

    /// <summary>OCR is extracting text from the document</summary>
    public const string OcrProcessing = "ocr_processing";

    /// <summary>Extracting key entities (dates, amounts, GSTIN)</summary>
    public const string Extracting = "extracting";

    /// <summary>Classifying notice type and category</summary>
    public const string Classifying = "classifying";

    /// <summary>Running AI analysis and generating report</summary>
    public const string Analyzing = "analyzing";

    /// <summary>Processing completed successfully</summary>
    public const string Completed = "completed";

    /// <summary>Processing failed, will retry</summary>
    public const string Retrying = "retrying";

    /// <summary>Processing failed after all retries</summary>
    public const string Failed = "failed";

    /// <summary>Legacy status for backwards compatibility</summary>
    public const string Processing = "processing";
}
