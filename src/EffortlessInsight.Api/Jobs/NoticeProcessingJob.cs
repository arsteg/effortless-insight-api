using System.Diagnostics;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
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
        ILogger<NoticeProcessingJob> logger)
    {
        _db = db;
        _aiService = aiService;
        _storageService = storageService;
        _workflowService = workflowService;
        _auditService = auditService;
        _backgroundJobs = backgroundJobs;
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

        // Update status to processing
        notice.Status = NoticeStatus.Processing;
        notice.ProcessingStatus = NoticeProcessingStatus.Processing;
        notice.ProcessingStartedAt = DateTime.UtcNow;
        notice.ProcessingAttempts++;
        notice.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Generate presigned URL for AI service to access the file
            var downloadResult = await _storageService.GenerateDownloadUrlAsync(
                notice.FileUrl,
                notice.FileName,
                expiryMinutes: 30,
                cancellationToken);

            // Call AI service
            var result = await _aiService.ProcessNoticeAsync(noticeId, downloadResult.Url);

            stopwatch.Stop();

            if (result.Success && result.Report != null)
            {
                await HandleSuccessAsync(notice, result, stopwatch.ElapsedMilliseconds, cancellationToken);
            }
            else
            {
                await HandleFailureAsync(notice, result.Error ?? "Unknown error", stopwatch.ElapsedMilliseconds, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing notice {NoticeId}", noticeId);
            await HandleFailureAsync(notice, ex.Message, stopwatch.ElapsedMilliseconds, cancellationToken);
        }
    }

    private async Task HandleSuccessAsync(
        Notice notice,
        DTOs.AiProcessingResult result,
        long processingTimeMs,
        CancellationToken cancellationToken)
    {
        var report = result.Report!;

        // Update notice with extracted metadata
        notice.NoticeType = report.Metadata.NoticeType;
        notice.NoticeCategory = report.Metadata.NoticeCategory;
        notice.NoticeNumber = report.Metadata.NoticeNumber ?? notice.NoticeNumber;
        notice.Gstin = report.Metadata.Gstin ?? notice.Gstin;
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

            // TODO: Send notification to user about failed processing
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
/// </summary>
public static class NoticeProcessingStatus
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Retrying = "retrying";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
