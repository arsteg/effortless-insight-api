using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Service for fetching and processing notices from the GST portal.
/// </summary>
public class GstnNoticeService : IGstnNoticeService
{
    private readonly ApplicationDbContext _context;
    private readonly IGspClient _gspClient;
    private readonly IGstnAuthService _authService;
    private readonly IFileStorageService _fileStorage;
    private readonly INotificationEngineService _notificationService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly GstnOptions _options;
    private readonly ILogger<GstnNoticeService> _logger;

    public GstnNoticeService(
        ApplicationDbContext context,
        IGspClient gspClient,
        IGstnAuthService authService,
        IFileStorageService fileStorage,
        INotificationEngineService notificationService,
        IBackgroundJobClient backgroundJobs,
        IOptions<GstnOptions> options,
        ILogger<GstnNoticeService> logger)
    {
        _context = context;
        _gspClient = gspClient;
        _authService = authService;
        _fileStorage = fileStorage;
        _notificationService = notificationService;
        _backgroundJobs = backgroundJobs;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GstnSyncResult> SyncNoticesAsync(
        Guid connectionId,
        GstnSyncOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GstnSyncOptions();

        var connection = await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
                .ThenInclude(g => g.Organization)
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection == null)
        {
            return new GstnSyncResult(
                Success: false,
                SyncLogId: null,
                NoticesFound: 0,
                NoticesImported: 0,
                NoticesSkipped: 0,
                NoticesFailed: 0,
                ErrorCode: "CONNECTION_NOT_FOUND",
                ErrorMessage: "Connection not found",
                ImportedNoticeIds: null
            );
        }

        if (connection.Status != GstnConnectionStatus.Connected)
        {
            return new GstnSyncResult(
                Success: false,
                SyncLogId: null,
                NoticesFound: 0,
                NoticesImported: 0,
                NoticesSkipped: 0,
                NoticesFailed: 0,
                ErrorCode: "NOT_CONNECTED",
                ErrorMessage: $"Connection status is {connection.Status}, not connected",
                ImportedNoticeIds: null
            );
        }

        // Create sync log entry
        var syncLog = new GstnSyncLog
        {
            GstnConnectionId = connectionId,
            SyncType = options.SyncType,
            TriggerSource = options.TriggerSource,
            TriggeredById = options.TriggeredById,
            Status = GstnSyncStatus.InProgress,
            StartedAt = DateTime.UtcNow
        };
        _context.GstnSyncLogs.Add(syncLog);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            // Get valid access token
            var accessToken = await _authService.GetValidAccessTokenAsync(connectionId, cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                return await CompleteSyncWithError(
                    syncLog,
                    "TOKEN_INVALID",
                    "Failed to get valid access token",
                    cancellationToken);
            }

            // Determine date range
            var toDate = options.ToDate ?? DateTime.UtcNow;
            var fromDate = options.FromDate ??
                connection.LastSyncAt?.AddHours(-1) ?? // Overlap by 1 hour to catch edge cases
                DateTime.UtcNow.AddDays(-_options.InitialSyncDaysBack);

            syncLog.SyncPeriodFrom = fromDate;
            syncLog.SyncPeriodTo = toDate;

            // Fetch all notices from GSP with pagination
            var allNotices = new List<GspNotice>();
            string? pageToken = null;
            var maxToFetch = options.MaxNotices ?? _options.MaxNoticesPerSync;
            string? lastCorrelationId = null;

            do
            {
                var fetchResult = await _gspClient.FetchNoticesAsync(
                    accessToken,
                    connection.OrganizationGstin.Gstin,
                    fromDate,
                    toDate,
                    pageToken,
                    cancellationToken);

                if (!fetchResult.Success)
                {
                    // If we already fetched some notices, continue with what we have
                    if (allNotices.Count > 0)
                    {
                        _logger.LogWarning(
                            "Pagination failed after fetching {Count} notices, continuing with partial results",
                            allNotices.Count);
                        break;
                    }

                    return await CompleteSyncWithError(
                        syncLog,
                        fetchResult.ErrorCode ?? "FETCH_FAILED",
                        fetchResult.ErrorMessage ?? "Failed to fetch notices from portal",
                        cancellationToken);
                }

                allNotices.AddRange(fetchResult.Notices);
                lastCorrelationId = fetchResult.CorrelationId;
                pageToken = fetchResult.HasMore ? fetchResult.NextPageToken : null;

                _logger.LogDebug(
                    "Fetched page with {Count} notices, total so far: {Total}, hasMore: {HasMore}",
                    fetchResult.Notices.Count,
                    allNotices.Count,
                    fetchResult.HasMore);

                // Stop if we've reached the max
                if (allNotices.Count >= maxToFetch)
                {
                    _logger.LogInformation(
                        "Reached max notices limit ({Max}), stopping pagination",
                        maxToFetch);
                    break;
                }

            } while (pageToken != null);

            syncLog.NoticesFound = allNotices.Count;
            syncLog.GspCorrelationId = lastCorrelationId;

            _logger.LogInformation(
                "Fetched {Count} notices from GSTN portal for connection {ConnectionId}",
                allNotices.Count,
                connectionId);

            // Batch check for existing notices to avoid N+1 queries
            var noticesToProcess = allNotices.Take(maxToFetch).ToList();
            var noticeIdsToCheck = noticesToProcess.Select(n => n.NoticeId).ToList();
            var existingNoticeIds = await GetExistingGstnNoticeIdsAsync(
                connection.OrganizationGstin.OrganizationId,
                noticeIdsToCheck,
                cancellationToken);

            // Process each notice
            var importedIds = new List<Guid>();
            var skipped = 0;
            var failed = 0;
            var processed = 0;
            var totalToProcess = noticesToProcess.Count;

            foreach (var gspNotice in noticesToProcess)
            {
                try
                {
                    // Check for duplicates using pre-fetched set
                    if (existingNoticeIds.Contains(gspNotice.NoticeId))
                    {
                        skipped++;
                        processed++;
                        continue;
                    }

                    // Create notice
                    var notice = await CreateNoticeFromGspAsync(
                        connection,
                        gspNotice,
                        lastCorrelationId,
                        cancellationToken);

                    importedIds.Add(notice.Id);

                    // Queue document download if available
                    if (gspNotice.HasDocument)
                    {
                        _backgroundJobs.Enqueue<IGstnNoticeService>(
                            svc => svc.DownloadNoticeDocumentAsync(notice.Id, CancellationToken.None));
                    }

                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import notice {NoticeId}", gspNotice.NoticeId);
                    failed++;
                    processed++;
                }

                // Log progress every 10 notices
                if (processed % 10 == 0)
                {
                    _logger.LogDebug(
                        "Sync progress for connection {ConnectionId}: {Processed}/{Total} notices processed",
                        connectionId,
                        processed,
                        totalToProcess);
                }
            }

            // Complete sync log
            syncLog.Status = failed > 0 ? GstnSyncStatus.CompletedWithWarnings : GstnSyncStatus.Completed;
            syncLog.NoticesImported = importedIds.Count;
            syncLog.NoticesSkipped = skipped;
            syncLog.NoticesFailed = failed;
            syncLog.ImportedNoticeIds = importedIds;
            syncLog.CompletedAt = DateTime.UtcNow;
            syncLog.DurationMs = (long)(syncLog.CompletedAt.Value - syncLog.StartedAt).TotalMilliseconds;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Sync completed for connection {ConnectionId}: {Imported} imported, {Skipped} skipped, {Failed} failed",
                connectionId,
                importedIds.Count,
                skipped,
                failed);

            // Send notification if new notices were imported
            if (importedIds.Count > 0)
            {
                await NotifyNewNoticesAsync(connection, importedIds.Count, cancellationToken);
            }

            return new GstnSyncResult(
                Success: true,
                SyncLogId: syncLog.Id,
                NoticesFound: allNotices.Count,
                NoticesImported: importedIds.Count,
                NoticesSkipped: skipped,
                NoticesFailed: failed,
                ErrorCode: null,
                ErrorMessage: null,
                ImportedNoticeIds: importedIds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for connection {ConnectionId}", connectionId);
            return await CompleteSyncWithError(
                syncLog,
                "SYNC_ERROR",
                ex.Message,
                cancellationToken);
        }
    }

    public async Task<GstnDocumentDownloadResult> DownloadNoticeDocumentAsync(
        Guid noticeId,
        CancellationToken cancellationToken = default)
    {
        var notice = await _context.Notices
            .Include(n => n.GstinNavigation)
                .ThenInclude(g => g!.GstnConnection)
            .FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);

        if (notice == null)
        {
            return new GstnDocumentDownloadResult(
                Success: false,
                FileUrl: null,
                FileName: null,
                FileSize: null,
                ErrorCode: "NOTICE_NOT_FOUND",
                ErrorMessage: "Notice not found"
            );
        }

        if (string.IsNullOrEmpty(notice.GstnNoticeId))
        {
            return new GstnDocumentDownloadResult(
                Success: false,
                FileUrl: null,
                FileName: null,
                FileSize: null,
                ErrorCode: "NOT_GSTN_NOTICE",
                ErrorMessage: "This notice was not fetched from GSTN portal"
            );
        }

        var connection = notice.GstinNavigation?.GstnConnection;
        if (connection == null)
        {
            return new GstnDocumentDownloadResult(
                Success: false,
                FileUrl: null,
                FileName: null,
                FileSize: null,
                ErrorCode: "NO_CONNECTION",
                ErrorMessage: "No GSTN connection found for this GSTIN"
            );
        }

        // Get valid access token
        var accessToken = await _authService.GetValidAccessTokenAsync(connection.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return new GstnDocumentDownloadResult(
                Success: false,
                FileUrl: null,
                FileName: null,
                FileSize: null,
                ErrorCode: "TOKEN_INVALID",
                ErrorMessage: "Failed to get valid access token"
            );
        }

        // Download document from GSP
        // Note: GstinNavigation is guaranteed non-null here because we already verified
        // connection (from GstinNavigation.GstnConnection) is not null above
        var gstin = notice.Gstin ?? notice.GstinNavigation?.Gstin;
        if (string.IsNullOrEmpty(gstin))
        {
            return new GstnDocumentDownloadResult(
                Success: false,
                FileUrl: null,
                FileName: null,
                FileSize: null,
                ErrorCode: "NO_GSTIN",
                ErrorMessage: "Notice has no GSTIN reference"
            );
        }

        var downloadResult = await _gspClient.DownloadNoticeDocumentAsync(
            accessToken,
            gstin,
            notice.GstnNoticeId,
            cancellationToken);

        if (!downloadResult.Success || downloadResult.Content == null)
        {
            return new GstnDocumentDownloadResult(
                Success: false,
                FileUrl: null,
                FileName: null,
                FileSize: null,
                ErrorCode: downloadResult.ErrorCode ?? "DOWNLOAD_FAILED",
                ErrorMessage: downloadResult.ErrorMessage ?? "Failed to download document"
            );
        }

        // Validate document size to prevent memory issues
        if (downloadResult.Content.Length > _options.MaxDocumentSizeBytes)
        {
            _logger.LogWarning(
                "Document for notice {NoticeId} exceeds size limit: {Size} bytes > {MaxSize} bytes",
                noticeId,
                downloadResult.Content.Length,
                _options.MaxDocumentSizeBytes);

            return new GstnDocumentDownloadResult(
                Success: false,
                FileUrl: null,
                FileName: null,
                FileSize: downloadResult.Content.Length,
                ErrorCode: "DOCUMENT_TOO_LARGE",
                ErrorMessage: $"Document size ({downloadResult.Content.Length / 1024 / 1024}MB) exceeds maximum allowed ({_options.MaxDocumentSizeBytes / 1024 / 1024}MB)"
            );
        }

        // Upload to our storage
        using var stream = new MemoryStream(downloadResult.Content);
        var fileName = downloadResult.FileName ?? $"notice_{notice.GstnNoticeId}.pdf";
        var contentType = downloadResult.ContentType ?? "application/pdf";

        var fileUrl = await _fileStorage.UploadAsync(stream, fileName, contentType);

        // Update notice
        notice.FileUrl = fileUrl;
        notice.FileName = fileName;
        notice.FileSize = downloadResult.Content.Length;
        notice.FileMimeType = contentType;
        notice.IsDocumentArchived = true;
        notice.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Downloaded and stored document for notice {NoticeId}: {FileName}",
            noticeId,
            fileName);

        return new GstnDocumentDownloadResult(
            Success: true,
            FileUrl: fileUrl,
            FileName: fileName,
            FileSize: downloadResult.Content.Length,
            ErrorCode: null,
            ErrorMessage: null
        );
    }

    public async Task<List<GstnSyncLogDto>> GetSyncLogsAsync(
        Guid connectionId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        var logs = await _context.GstnSyncLogs
            .Include(l => l.TriggeredBy)
            .Where(l => l.GstnConnectionId == connectionId)
            .OrderByDescending(l => l.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return logs.Select(l => new GstnSyncLogDto(
            Id: l.Id,
            SyncType: l.SyncType,
            Status: l.Status,
            TriggerSource: l.TriggerSource,
            StartedAt: l.StartedAt,
            CompletedAt: l.CompletedAt,
            DurationMs: l.DurationMs,
            NoticesFound: l.NoticesFound,
            NoticesImported: l.NoticesImported,
            NoticesSkipped: l.NoticesSkipped,
            NoticesFailed: l.NoticesFailed,
            ErrorMessage: l.ErrorMessage,
            TriggeredByName: l.TriggeredBy?.Name
        )).ToList();
    }

    public async Task<bool> NoticeExistsAsync(
        Guid organizationId,
        string gstnNoticeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Notices
            .AnyAsync(n =>
                n.OrganizationId == organizationId &&
                n.GstnNoticeId == gstnNoticeId &&
                n.DeletedAt == null,
                cancellationToken);
    }

    /// <summary>
    /// Batch check for existing GSTN notice IDs to avoid N+1 queries.
    /// </summary>
    private async Task<HashSet<string>> GetExistingGstnNoticeIdsAsync(
        Guid organizationId,
        List<string> gstnNoticeIds,
        CancellationToken cancellationToken)
    {
        if (gstnNoticeIds.Count == 0)
            return [];

        var existingIds = await _context.Notices
            .Where(n => n.OrganizationId == organizationId)
            .Where(n => n.GstnNoticeId != null && gstnNoticeIds.Contains(n.GstnNoticeId))
            .Where(n => n.DeletedAt == null)
            .Select(n => n.GstnNoticeId!)
            .ToListAsync(cancellationToken);

        return existingIds.ToHashSet();
    }

    private async Task<Notice> CreateNoticeFromGspAsync(
        GstnConnection connection,
        GspNotice gspNotice,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        // ConnectedById should always be set for a valid GSTN connection
        // as it's set during OTP verification. If it's null, something is wrong.
        if (!connection.ConnectedById.HasValue)
        {
            throw new InvalidOperationException(
                $"GSTN connection {connection.Id} has no ConnectedById. " +
                "This indicates a corrupted connection state.");
        }

        var notice = new Notice
        {
            OrganizationId = connection.OrganizationGstin.OrganizationId,
            UploadedById = connection.ConnectedById.Value,
            GstinId = connection.OrganizationGstinId,
            Gstin = connection.OrganizationGstin.Gstin,

            // GSTN portal fields
            Source = NoticeSource.GstnPortal,
            GstnNoticeId = gspNotice.NoticeId,
            GstnReferenceNumber = gspNotice.ReferenceNumber,
            FetchedFromGstnAt = DateTime.UtcNow,
            GspCorrelationId = correlationId,

            // Notice details
            NoticeType = gspNotice.NoticeType,
            NoticeCategory = gspNotice.NoticeCategory,
            Section = gspNotice.Section,
            Summary = gspNotice.Subject,
            IssueDate = gspNotice.IssueDate.HasValue
                ? DateOnly.FromDateTime(gspNotice.IssueDate.Value)
                : null,
            DueDate = gspNotice.DueDate,
            ResponseDeadline = gspNotice.DueDate.HasValue
                ? DateOnly.FromDateTime(gspNotice.DueDate.Value)
                : null,

            // Financial amounts
            TaxAmount = gspNotice.TaxAmount,
            PenaltyAmount = gspNotice.PenaltyAmount,
            InterestAmount = gspNotice.InterestAmount,

            // Period
            FinancialYear = gspNotice.FinancialYear,
            PeriodFrom = gspNotice.PeriodFrom.HasValue
                ? DateOnly.FromDateTime(gspNotice.PeriodFrom.Value)
                : null,
            PeriodTo = gspNotice.PeriodTo.HasValue
                ? DateOnly.FromDateTime(gspNotice.PeriodTo.Value)
                : null,

            // Authority
            IssuingAuthority = gspNotice.IssuingAuthority,
            IssuingOfficer = gspNotice.IssuingOfficer,
            Jurisdiction = gspNotice.Jurisdiction,

            // Status
            Status = NoticeStatus.Uploaded,
            ProcessingStatus = gspNotice.HasDocument
                ? NoticeProcessingStatus.Queued
                : NoticeProcessingStatus.Completed,
            Priority = DeterminePriority(gspNotice),

            // File info - use placeholder values that indicate pending download
            // These are [Required] fields, so we use identifiable placeholders
            // that will be replaced when the document is downloaded
            FileUrl = gspNotice.HasDocument
                ? $"pending://gstn/{gspNotice.NoticeId}"
                : "none://no-document",
            FileName = gspNotice.HasDocument
                ? $"pending_notice_{gspNotice.NoticeId}.{gspNotice.DocumentFormat ?? "pdf"}"
                : "no_document",
            IsDocumentArchived = false,

            // Metadata
            Metadata = gspNotice.AdditionalData
        };

        _context.Notices.Add(notice);
        await _context.SaveChangesAsync(cancellationToken);

        return notice;
    }

    private static string DeterminePriority(GspNotice notice)
    {
        // High priority if due date is within 7 days
        if (notice.DueDate.HasValue)
        {
            var daysUntilDue = (notice.DueDate.Value - DateTime.UtcNow).TotalDays;
            if (daysUntilDue <= 3) return NoticePriority.Critical;
            if (daysUntilDue <= 7) return NoticePriority.High;
            if (daysUntilDue <= 14) return NoticePriority.Medium;
        }

        // High priority for large amounts
        var totalAmount = (notice.TaxAmount ?? 0) + (notice.PenaltyAmount ?? 0) + (notice.InterestAmount ?? 0);
        if (totalAmount >= 1000000) return NoticePriority.High; // >= 10 lakhs
        if (totalAmount >= 100000) return NoticePriority.Medium; // >= 1 lakh

        return NoticePriority.Low;
    }

    private async Task<GstnSyncResult> CompleteSyncWithError(
        GstnSyncLog syncLog,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        syncLog.Status = GstnSyncStatus.Failed;
        syncLog.ErrorMessage = errorMessage;
        syncLog.CompletedAt = DateTime.UtcNow;
        syncLog.DurationMs = (long)(syncLog.CompletedAt.Value - syncLog.StartedAt).TotalMilliseconds;

        await _context.SaveChangesAsync(cancellationToken);

        return new GstnSyncResult(
            Success: false,
            SyncLogId: syncLog.Id,
            NoticesFound: 0,
            NoticesImported: 0,
            NoticesSkipped: 0,
            NoticesFailed: 0,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            ImportedNoticeIds: null
        );
    }

    /// <summary>
    /// Sends notifications to organization members about newly synced notices.
    /// </summary>
    private async Task NotifyNewNoticesAsync(
        GstnConnection connection,
        int noticeCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var organizationId = connection.OrganizationGstin.OrganizationId;
            var gstin = connection.OrganizationGstin.Gstin;
            var tradeName = connection.OrganizationGstin.TradeName ?? gstin;

            // Get organization admins and owners to notify
            var usersToNotify = await _context.OrganizationMembers
                .Where(m => m.OrganizationId == organizationId)
                .Where(m => m.Role == "owner" || m.Role == "admin")
                .Where(m => m.Status == "active")
                .Where(m => m.DeletedAt == null)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);

            foreach (var userId in usersToNotify)
            {
                try
                {
                    await _notificationService.SendAsync(
                        new SendNotificationRequest(
                            UserId: userId,
                            Type: "gstn_notices_synced",
                            Data: new Dictionary<string, object>
                            {
                                ["gstin"] = gstin,
                                ["tradeName"] = tradeName,
                                ["noticeCount"] = noticeCount,
                                ["organizationId"] = organizationId.ToString(),
                                ["message"] = noticeCount == 1
                                    ? $"1 new GST notice has been synced for {tradeName}"
                                    : $"{noticeCount} new GST notices have been synced for {tradeName}"
                            }
                        ),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log but don't fail the sync if notification fails
                    _logger.LogWarning(ex,
                        "Failed to send GSTN sync notification to user {UserId}",
                        userId);
                }
            }

            _logger.LogInformation(
                "Sent GSTN sync notifications to {UserCount} users for {NoticeCount} new notices",
                usersToNotify.Count,
                noticeCount);
        }
        catch (Exception ex)
        {
            // Log but don't fail the sync if notification fails
            _logger.LogError(ex, "Failed to send GSTN sync notifications");
        }
    }
}
