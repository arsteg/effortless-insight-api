using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Storage;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// Service for managing raw GST notices.
/// </summary>
public class GstNoticeRawService : IGstNoticeRawService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageServiceExtended _storageService;
    private readonly IGstSyncNotificationService _notificationService;
    private readonly IGstinLinkService _gstinLink;
    private readonly ICaBoGstinLinkService _caBoGstinLinkService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly Billing.IUsageService _usageService;
    private readonly ILogger<GstNoticeRawService> _logger;

    public GstNoticeRawService(
        ApplicationDbContext context,
        IFileStorageServiceExtended storageService,
        IGstSyncNotificationService notificationService,
        IGstinLinkService gstinLink,
        ICaBoGstinLinkService caBoGstinLinkService,
        UserManager<ApplicationUser> userManager,
        IBackgroundJobClient backgroundJobs,
        Billing.IUsageService usageService,
        ILogger<GstNoticeRawService> logger)
    {
        _context = context;
        _storageService = storageService;
        _notificationService = notificationService;
        _gstinLink = gstinLink;
        _caBoGstinLinkService = caBoGstinLinkService;
        _userManager = userManager;
        _backgroundJobs = backgroundJobs;
        _usageService = usageService;
        _logger = logger;
    }

    public async Task<List<GstNoticeRawDto>> GetNoticesAsync(Guid clientId, bool? imported = null, CancellationToken cancellationToken = default)
    {
        var query = _context.GstNoticesRaw
            .Where(n => n.GstClientId == clientId);

        if (imported.HasValue)
        {
            query = query.Where(n => n.ImportedToNotices == imported.Value);
        }

        var notices = await query
            .OrderByDescending(n => n.IssueDate)
            .ThenByDescending(n => n.FirstSyncedAt)
            .ToListAsync(cancellationToken);

        return notices.Select(MapToDto).ToList();
    }

    public async Task<List<GstNoticeRawDto>> GetNoticesByOrganizationAsync(Guid organizationId, bool? imported = null, CancellationToken cancellationToken = default)
    {
        var query = _context.GstNoticesRaw
            .Where(n => n.OrganizationId == organizationId);

        if (imported.HasValue)
        {
            query = query.Where(n => n.ImportedToNotices == imported.Value);
        }

        var notices = await query
            .OrderByDescending(n => n.IssueDate)
            .ThenByDescending(n => n.FirstSyncedAt)
            .ToListAsync(cancellationToken);

        return notices.Select(MapToDto).ToList();
    }

    public async Task<GstNoticeRawDto?> GetNoticeByIdAsync(Guid noticeId, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);

        return notice == null ? null : MapToDto(notice);
    }

    public async Task<ImportNoticesResult> ImportNoticesAsync(Guid organizationId, Guid userId, ImportNoticesRequest request, CancellationToken cancellationToken = default)
    {
        var result = new ImportNoticesResult();
        var imported = new List<ImportedNoticeInfo>();
        var errors = new List<string>();
        var noticesToProcess = new List<Guid>();
        var importedGstins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alreadyImportedCount = 0;
        var failedCount = 0;

        foreach (var noticeId in request.NoticeIds)
        {
            try
            {
                var rawNotice = await _context.GstNoticesRaw
                    .Include(n => n.GstClient)
                    .FirstOrDefaultAsync(n => n.Id == noticeId && n.OrganizationId == organizationId, cancellationToken);

                if (rawNotice == null)
                {
                    errors.Add($"Notice {noticeId} not found.");
                    failedCount++;
                    continue;
                }

                if (rawNotice.ImportedToNotices)
                {
                    alreadyImportedCount++;
                    continue;
                }

                // Authoritative per-plan notice quota — same gate as manual
                // upload (NoticeService.UploadAsync). Checked per notice so a
                // batch stops cleanly when the monthly allowance runs out.
                var (canCreate, quotaReason) = await _usageService.CanCreateNoticeAsync(organizationId);
                if (!canCreate)
                {
                    var remaining = request.NoticeIds.Count - imported.Count - alreadyImportedCount - failedCount;
                    errors.Add($"NOTICE_LIMIT_EXCEEDED: {quotaReason} {imported.Count} of {request.NoticeIds.Count} notices imported; the remaining {remaining} were skipped.");
                    failedCount += remaining;
                    break;
                }

                // Resolve the org GSTIN registry entry for this client so the
                // imported notice is linked (self-heals clients that predate
                // the OrganizationGstinId column).
                if (rawNotice.GstClient.OrganizationGstinId == null)
                {
                    var registryEntry = await _gstinLink.FindOrCreateAsync(
                        rawNotice.GstClient.OrganizationId,
                        rawNotice.GstClient.Gstin,
                        rawNotice.GstClient.TradeName,
                        rawNotice.GstClient.LegalName,
                        cancellationToken);
                    rawNotice.GstClient.OrganizationGstinId = registryEntry.Id;
                }

                // Create a new Notice in the main Notices table
                var notice = new Data.Entities.Notice
                {
                    OrganizationId = organizationId,
                    UploadedById = userId,
                    NoticeType = rawNotice.NoticeType,
                    NoticeCategory = rawNotice.NoticeCategory,
                    NoticeNumber = rawNotice.ReferenceNumber ?? rawNotice.PortalNoticeId,
                    Gstin = rawNotice.Gstin,
                    // GstinHash is essential for cross-org notice visibility (CA-BO linking)
                    GstinHash = !string.IsNullOrEmpty(rawNotice.Gstin)
                        ? ICrossOrgNoticeVisibilityService.ComputeGstinHash(rawNotice.Gstin)
                        : null,
                    GstinId = rawNotice.GstClient.OrganizationGstinId,
                    IssueDate = rawNotice.IssueDate,
                    ResponseDeadline = rawNotice.DueDate,
                    TaxAmount = rawNotice.TaxAmount,
                    InterestAmount = rawNotice.InterestAmount,
                    PenaltyAmount = rawNotice.PenaltyAmount,
                    FinancialYear = rawNotice.FinancialYear,
                    Section = rawNotice.SectionRule,
                    IssuingOfficer = rawNotice.OfficerName,
                    OfficerDesignation = rawNotice.OfficerDesignation,
                    Jurisdiction = rawNotice.Jurisdiction,
                    Status = Data.Entities.NoticeStatus.Uploaded,
                    // With a captured PDF the notice goes through the full AI
                    // pipeline (OCR → extraction → analysis); metadata-only
                    // notices have nothing to process.
                    ProcessingStatus = rawNotice.PdfS3Key != null
                        ? Data.Entities.NoticeProcessingStatus.Queued
                        : Data.Entities.NoticeProcessingStatus.Completed,
                    Priority = DeterminePriority(rawNotice),
                    Source = Data.Entities.NoticeSource.GstnPortal,
                    GstnNoticeId = rawNotice.PortalNoticeId,
                    // Assignment
                    AssignedToId = request.AssignToUserId,
                    AssignedById = request.AssignToUserId.HasValue ? userId : null,
                    AssignedAt = request.AssignToUserId.HasValue ? DateTime.UtcNow : null,
                    // Set required file fields with placeholders (no actual file from sync)
                    FileUrl = rawNotice.PdfS3Key ?? $"gst-sync-import/{rawNotice.Id}",
                    FileName = $"GST_Notice_{rawNotice.NoticeType}_{rawNotice.PortalNoticeId}.pdf",
                    FileSize = rawNotice.PdfSizeBytes ?? 0,
                    FileMimeType = "application/pdf",
                    Metadata = new Dictionary<string, object>
                    {
                        ["gst_sync_notice_id"] = rawNotice.Id.ToString(),
                        ["portal_status"] = rawNotice.StatusOnPortal ?? "",
                        ["tax_period"] = rawNotice.TaxPeriod ?? "",
                        ["demand_amount"] = rawNotice.DemandAmount?.ToString() ?? ""
                    }
                };

                _context.Notices.Add(notice);

                // Advance the per-plan usage counter (also flushes the pending
                // notice via its SaveChanges, keeping the quota check accurate
                // for the rest of the batch).
                await _usageService.IncrementNoticeCountAsync(organizationId);

                // Update raw notice with import info
                rawNotice.ImportedToNotices = true;
                rawNotice.ImportedNoticeId = notice.Id;
                rawNotice.ImportedAt = DateTime.UtcNow;

                if (rawNotice.PdfS3Key != null)
                {
                    noticesToProcess.Add(notice.Id);
                }

                imported.Add(new ImportedNoticeInfo(rawNotice.Id, notice.Id));

                // Track GSTIN for auto-linking
                if (!string.IsNullOrEmpty(rawNotice.Gstin))
                {
                    importedGstins.Add(rawNotice.Gstin);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to import notice {noticeId}: {ex.Message}");
                _logger.LogError(ex, "Failed to import notice {NoticeId}", noticeId);
                failedCount++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Queue AI analysis for notices that came with a captured PDF —
        // same pipeline as manual uploads (OCR → extraction → analysis).
        foreach (var noticeId in noticesToProcess)
        {
            _backgroundJobs.Enqueue<INoticeProcessingJob>(
                job => job.ProcessAsync(noticeId, CancellationToken.None));
        }

        _logger.LogInformation("Imported {Count} notices for organization {OrganizationId} ({AiQueued} queued for AI analysis)",
            imported.Count, organizationId, noticesToProcess.Count);

        // Send import completion notification
        if (imported.Count > 0 || failedCount > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _notificationService.NotifyImportCompletedAsync(userId, imported.Count, failedCount, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send import completion notification to user {UserId}", userId);
                }
            }, CancellationToken.None);
        }

        // Auto-create CaBoGstinLinks if a CA is importing notices for connected client GSTINs
        if (imported.Count > 0 && importedGstins.Count > 0)
        {
            await EnsureCaBoGstinLinksAsync(organizationId, userId, importedGstins, cancellationToken);
        }

        return new ImportNoticesResult
        {
            Imported = imported.Count,
            AlreadyImported = alreadyImportedCount,
            Failed = failedCount,
            ImportedNotices = imported,
            Errors = errors
        };
    }

    public async Task<PdfUploadUrlResponse> GetPdfUploadUrlAsync(Guid organizationId, GetPdfUploadUrlRequest request, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .FirstOrDefaultAsync(n => n.Id == request.NoticeId && n.OrganizationId == organizationId, cancellationToken);

        if (notice == null)
        {
            throw new InvalidOperationException("Notice not found.");
        }

        // Get presigned URL from storage service
        // Note: The storage service generates its own S3 key path internally
        var presignedUrl = await _storageService.GenerateUploadUrlAsync(
            organizationId,
            notice.Id,
            request.FileName,
            "application/pdf",
            request.FileSize,
            cancellationToken);

        return new PdfUploadUrlResponse
        {
            UploadUrl = presignedUrl.Url,
            S3Key = presignedUrl.Key,
            ExpiresAt = presignedUrl.ExpiresAt
        };
    }

    public async Task<bool> ConfirmPdfUploadAsync(Guid organizationId, ConfirmPdfUploadRequest request, CancellationToken cancellationToken = default)
    {
        var notice = await _context.GstNoticesRaw
            .FirstOrDefaultAsync(n => n.Id == request.NoticeId && n.OrganizationId == organizationId, cancellationToken);

        if (notice == null)
        {
            return false;
        }

        notice.PdfS3Key = request.S3Key;
        notice.PdfSizeBytes = request.FileSize;
        notice.PdfDownloadedAt = DateTime.UtcNow;
        notice.PdfAvailable = true;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Confirmed PDF upload for notice {NoticeId}", notice.Id);

        return true;
    }

    private static string DeterminePriority(GstNoticeRaw notice)
    {
        // Determine priority based on notice type and due date
        if (notice.DueDate.HasValue)
        {
            var daysUntilDue = (DateTime.SpecifyKind(notice.DueDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - DateTime.UtcNow).Days;
            if (daysUntilDue <= 3)
                return "critical";
            if (daysUntilDue <= 7)
                return "high";
            if (daysUntilDue <= 15)
                return "medium";
        }

        // Priority based on notice type
        if (notice.NoticeType.StartsWith("DRC", StringComparison.OrdinalIgnoreCase))
            return "high"; // Demand and Recovery

        if (notice.NoticeType.StartsWith("ASMT", StringComparison.OrdinalIgnoreCase))
            return "medium"; // Assessment

        return "normal";
    }

    public async Task<UpcomingDueDatesResponse> GetUpcomingDueDatesAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var futureLimit = today.AddDays(14); // 14 days in the future

        // Get notices with due dates that are overdue or coming up in the next 14 days
        var notices = await _context.GstNoticesRaw
            .Include(n => n.GstClient)
            .Where(n => n.OrganizationId == organizationId)
            .Where(n => n.DueDate.HasValue && n.DueDate.Value <= futureLimit)
            .Where(n => !n.ImportedToNotices) // Only show unimported notices
            .OrderBy(n => n.DueDate)
            .Take(50) // Limit to prevent overwhelming notifications
            .ToListAsync(cancellationToken);

        var result = notices.Select(n =>
        {
            var daysUntilDue = n.DueDate!.Value.DayNumber - today.DayNumber;
            return new UpcomingDueDateNotice
            {
                Id = n.Id,
                Gstin = n.Gstin,
                GstClientId = n.GstClientId,
                ClientName = n.GstClient?.TradeName ?? n.GstClient?.LegalName,
                NoticeType = n.NoticeType,
                DueDate = n.DueDate!.Value,
                DemandAmount = n.DemandAmount,
                DaysUntilDue = daysUntilDue,
                IsOverdue = daysUntilDue < 0
            };
        }).ToList();

        return new UpcomingDueDatesResponse
        {
            Notices = result,
            TotalCount = result.Count
        };
    }

    private static GstNoticeRawDto MapToDto(GstNoticeRaw notice)
    {
        return new GstNoticeRawDto
        {
            Id = notice.Id,
            GstClientId = notice.GstClientId,
            Gstin = notice.Gstin,
            PortalNoticeId = notice.PortalNoticeId,
            ReferenceNumber = notice.ReferenceNumber,
            NoticeType = notice.NoticeType,
            NoticeCategory = notice.NoticeCategory,
            IssueDate = notice.IssueDate,
            DueDate = notice.DueDate,
            StatusOnPortal = notice.StatusOnPortal,
            DemandAmount = notice.DemandAmount,
            TaxAmount = notice.TaxAmount,
            InterestAmount = notice.InterestAmount,
            PenaltyAmount = notice.PenaltyAmount,
            TaxPeriod = notice.TaxPeriod,
            FinancialYear = notice.FinancialYear,
            SectionRule = notice.SectionRule,
            OfficerName = notice.OfficerName,
            OfficerDesignation = notice.OfficerDesignation,
            Jurisdiction = notice.Jurisdiction,
            PdfAvailable = notice.PdfAvailable,
            PdfS3Key = notice.PdfS3Key,
            PdfSizeBytes = notice.PdfSizeBytes,
            ImportedToNotices = notice.ImportedToNotices,
            ImportedNoticeId = notice.ImportedNoticeId,
            FirstSyncedAt = notice.FirstSyncedAt,
            LastSyncedAt = notice.LastSyncedAt,
            SyncCount = notice.SyncCount
        };
    }

    public async Task<AutoImportResult> AutoImportForGstinAsync(
        Guid caOrganizationId,
        Guid boOrganizationId,
        string gstin,
        Guid boUserId,
        CancellationToken cancellationToken = default)
    {
        var result = new AutoImportResult();
        var normalizedGstin = gstin.Trim().ToUpperInvariant();
        var noticesToProcess = new List<Guid>();
        var imported = 0;
        var skippedAsDuplicate = 0;
        var failed = 0;

        // Find unimported GstNoticeRaw for this GSTIN in CA's org
        var rawNotices = await _context.GstNoticesRaw
            .Include(n => n.GstClient)
            .Where(n =>
                n.OrganizationId == caOrganizationId &&
                n.Gstin == normalizedGstin &&
                !n.ImportedToNotices &&
                n.DeletedAt == null)
            .ToListAsync(cancellationToken);

        if (rawNotices.Count == 0)
        {
            return result;
        }

        // Get or create the OrganizationGstin for the BO's organization
        var boGstinEntry = await _gstinLink.FindOrCreateAsync(
            boOrganizationId,
            normalizedGstin,
            tradeName: null,
            legalName: null,
            cancellationToken);

        foreach (var raw in rawNotices)
        {
            try
            {
                // Check quota
                var (canCreate, reason) = await _usageService.CanCreateNoticeAsync(boOrganizationId);
                if (!canCreate)
                {
                    return result with
                    {
                        Imported = imported,
                        SkippedAsDuplicate = skippedAsDuplicate,
                        Failed = failed,
                        QuotaExceeded = true,
                        QuotaMessage = reason
                    };
                }

                // Dedupe by PortalNoticeId or ReferenceNumber
                var existing = await FindMatchingNoticeAsync(boOrganizationId, raw, cancellationToken);
                if (existing != null)
                {
                    raw.ImportedToNotices = true;
                    raw.ImportedNoticeId = existing.Id;
                    raw.ImportedAt = DateTime.UtcNow;
                    skippedAsDuplicate++;
                    continue;
                }

                // Create Notice in BO's org
                var notice = new Data.Entities.Notice
                {
                    OrganizationId = boOrganizationId,
                    UploadedById = boUserId,
                    NoticeType = raw.NoticeType,
                    NoticeCategory = raw.NoticeCategory,
                    NoticeNumber = raw.ReferenceNumber ?? raw.PortalNoticeId,
                    Gstin = raw.Gstin,
                    GstinHash = !string.IsNullOrEmpty(raw.Gstin)
                        ? ICrossOrgNoticeVisibilityService.ComputeGstinHash(raw.Gstin)
                        : null,
                    GstinId = boGstinEntry.Id,
                    IssueDate = raw.IssueDate,
                    ResponseDeadline = raw.DueDate,
                    TaxAmount = raw.TaxAmount,
                    InterestAmount = raw.InterestAmount,
                    PenaltyAmount = raw.PenaltyAmount,
                    FinancialYear = raw.FinancialYear,
                    Section = raw.SectionRule,
                    IssuingOfficer = raw.OfficerName,
                    OfficerDesignation = raw.OfficerDesignation,
                    Jurisdiction = raw.Jurisdiction,
                    Status = Data.Entities.NoticeStatus.Uploaded,
                    ProcessingStatus = raw.PdfS3Key != null
                        ? Data.Entities.NoticeProcessingStatus.Queued
                        : Data.Entities.NoticeProcessingStatus.Completed,
                    Priority = DeterminePriority(raw),
                    Source = Data.Entities.NoticeSource.GstnPortal,
                    GstnNoticeId = raw.PortalNoticeId,
                    FileUrl = raw.PdfS3Key ?? $"gst-sync-auto-import/{raw.Id}",
                    FileName = $"GST_Notice_{raw.NoticeType}_{raw.PortalNoticeId}.pdf",
                    FileSize = raw.PdfSizeBytes ?? 0,
                    FileMimeType = "application/pdf",
                    Metadata = new Dictionary<string, object>
                    {
                        ["gst_sync_notice_id"] = raw.Id.ToString(),
                        ["portal_status"] = raw.StatusOnPortal ?? "",
                        ["tax_period"] = raw.TaxPeriod ?? "",
                        ["demand_amount"] = raw.DemandAmount?.ToString() ?? "",
                        ["auto_imported_from_ca_org"] = caOrganizationId.ToString()
                    }
                };

                _context.Notices.Add(notice);

                // Advance the per-plan usage counter
                await _usageService.IncrementNoticeCountAsync(boOrganizationId);

                raw.ImportedToNotices = true;
                raw.ImportedNoticeId = notice.Id;
                raw.ImportedAt = DateTime.UtcNow;

                if (raw.PdfS3Key != null)
                {
                    noticesToProcess.Add(notice.Id);
                }

                imported++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-import GstNoticeRaw {RawNoticeId} to org {BoOrgId}",
                    raw.Id, boOrganizationId);
                failed++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Queue AI analysis for notices with PDFs
        foreach (var noticeId in noticesToProcess)
        {
            _backgroundJobs.Enqueue<INoticeProcessingJob>(
                job => job.ProcessAsync(noticeId, CancellationToken.None));
        }

        _logger.LogInformation(
            "Auto-imported {Imported} GST notices from CA org {CaOrgId} to BO org {BoOrgId} for GSTIN {Gstin} ({AiQueued} queued for AI)",
            imported, caOrganizationId, boOrganizationId, normalizedGstin, noticesToProcess.Count);

        return new AutoImportResult
        {
            Imported = imported,
            SkippedAsDuplicate = skippedAsDuplicate,
            Failed = failed,
            QuotaExceeded = false,
            QuotaMessage = null
        };
    }

    private async Task<Data.Entities.Notice?> FindMatchingNoticeAsync(
        Guid organizationId,
        GstNoticeRaw raw,
        CancellationToken cancellationToken)
    {
        // Match by PortalNoticeId (GstnNoticeId in Notice)
        if (!string.IsNullOrWhiteSpace(raw.PortalNoticeId))
        {
            var byPortalId = await _context.Notices
                .FirstOrDefaultAsync(n =>
                    n.OrganizationId == organizationId &&
                    n.GstnNoticeId == raw.PortalNoticeId &&
                    n.DeletedAt == null,
                    cancellationToken);
            if (byPortalId != null)
                return byPortalId;
        }

        // Match by ReferenceNumber (NoticeNumber in Notice)
        if (!string.IsNullOrWhiteSpace(raw.ReferenceNumber))
        {
            var byReference = await _context.Notices
                .FirstOrDefaultAsync(n =>
                    n.OrganizationId == organizationId &&
                    n.NoticeNumber == raw.ReferenceNumber &&
                    n.DeletedAt == null,
                    cancellationToken);
            if (byReference != null)
                return byReference;
        }

        return null;
    }

    /// <summary>
    /// Ensures CaBoGstinLinks exist for cross-org notice visibility when a CA imports notices.
    /// </summary>
    private async Task EnsureCaBoGstinLinksAsync(
        Guid organizationId,
        Guid userId,
        IEnumerable<string> gstins,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if the user is a CA
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null || !user.IsCA)
            {
                return;
            }

            // Check if this organization is the CA's own org (where they are owner)
            var isOwnerOfOrg = await _context.OrganizationMembers
                .AnyAsync(m =>
                    m.UserId == userId &&
                    m.OrganizationId == organizationId &&
                    m.Role == "owner" &&
                    m.Status == "active" &&
                    m.DeletedAt == null,
                    cancellationToken);

            if (!isOwnerOfOrg)
            {
                // Importing to a client's org, not their own - no cross-org link needed
                return;
            }

            // CA is importing from their own organization - create links to connected BO orgs
            var totalLinksCreated = 0;
            foreach (var gstin in gstins)
            {
                var linksCreated = await _caBoGstinLinkService.EnsureLinksForGstinAsync(
                    organizationId,
                    userId,
                    gstin,
                    cancellationToken);
                totalLinksCreated += linksCreated;
            }

            if (totalLinksCreated > 0)
            {
                _logger.LogInformation(
                    "Created {LinkCount} CaBoGstinLinks for CA {UserId} importing notices with {GstinCount} GSTINs",
                    totalLinksCreated, userId, gstins.Count());
            }
        }
        catch (Exception ex)
        {
            // Don't fail the import if link creation fails
            _logger.LogWarning(ex,
                "Failed to create CaBoGstinLinks for user {UserId}, org {OrgId}",
                userId, organizationId);
        }
    }
}
