using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// Service for managing sync sessions and syncing notices.
/// </summary>
public class GstSyncService : IGstSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly IGstSyncNotificationService _notificationService;
    private readonly ILogger<GstSyncService> _logger;

    public GstSyncService(
        ApplicationDbContext context,
        IGstSyncNotificationService notificationService,
        ILogger<GstSyncService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<GstSyncSessionDto> StartSessionAsync(Guid organizationId, StartSyncSessionRequest request, CancellationToken cancellationToken = default)
    {
        // Validate the client belongs to the organization
        var client = await _context.GstClients
            .FirstOrDefaultAsync(c => c.Id == request.GstClientId && c.OrganizationId == organizationId, cancellationToken);

        if (client == null)
        {
            throw new InvalidOperationException("GST client not found or does not belong to this organization.");
        }

        if (!GstClientStatus.CanSync(client.Status))
        {
            throw new InvalidOperationException($"Cannot sync client in status '{client.Status}'.");
        }

        // Check if there's an existing in-progress session
        var existingSession = await _context.GstSyncSessions
            .FirstOrDefaultAsync(s => s.GstClientId == request.GstClientId && s.Status == GstSyncSessionStatus.InProgress, cancellationToken);

        if (existingSession != null)
        {
            // If session is older than 30 minutes, mark it as failed
            if (existingSession.StartedAt < DateTime.UtcNow.AddMinutes(-30))
            {
                existingSession.Status = GstSyncSessionStatus.Failed;
                existingSession.ErrorMessage = "Session timed out";
                existingSession.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                throw new InvalidOperationException("A sync session is already in progress.");
            }
        }

        var session = new GstSyncSession
        {
            GstClientId = request.GstClientId,
            OrganizationId = organizationId,
            Gstin = client.Gstin,
            SyncSource = request.SyncSource,
            SourceVersion = request.SourceVersion,
            SourceMetadata = request.SourceMetadata,
            StartedAt = DateTime.UtcNow,
            Status = GstSyncSessionStatus.InProgress
        };

        _context.GstSyncSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Started sync session {SessionId} for client {ClientId}", session.Id, client.Id);

        return MapSessionToDto(session);
    }

    public async Task<SyncNoticesResult> SyncNoticesAsync(Guid organizationId, SyncNoticesRequest request, CancellationToken cancellationToken = default)
    {
        var session = await _context.GstSyncSessions
            .Include(s => s.GstClient)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.OrganizationId == organizationId, cancellationToken);

        if (session == null)
        {
            throw new InvalidOperationException("Sync session not found.");
        }

        if (session.Status != GstSyncSessionStatus.InProgress)
        {
            throw new InvalidOperationException($"Cannot sync notices to session in status '{session.Status}'.");
        }

        var result = new SyncNoticesResult();
        var newCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        var errors = new List<string>();

        foreach (var noticeData in request.Notices)
        {
            try
            {
                var (status, _) = await ProcessNoticeAsync(session, noticeData, cancellationToken);
                switch (status)
                {
                    case "new":
                        newCount++;
                        break;
                    case "updated":
                        updatedCount++;
                        break;
                    case "unchanged":
                        unchangedCount++;
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to process notice {noticeData.PortalNoticeId}: {ex.Message}");
                _logger.LogError(ex, "Failed to process notice {PortalNoticeId}", noticeData.PortalNoticeId);
            }
        }

        // Update session counts
        session.NoticesFound += request.Notices.Count;
        session.NoticesNew += newCount;
        session.NoticesUpdated += updatedCount;
        session.NoticesUnchanged += unchangedCount;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Synced {Count} notices for session {SessionId}: {New} new, {Updated} updated, {Unchanged} unchanged",
            request.Notices.Count, session.Id, newCount, updatedCount, unchangedCount);

        return new SyncNoticesResult
        {
            NoticesProcessed = request.Notices.Count,
            NoticesNew = newCount,
            NoticesUpdated = updatedCount,
            NoticesUnchanged = unchangedCount,
            Errors = errors
        };
    }

    public async Task<GstSyncSessionDto?> CompleteSessionAsync(Guid organizationId, CompleteSyncSessionRequest request, CancellationToken cancellationToken = default)
    {
        var session = await _context.GstSyncSessions
            .Include(s => s.GstClient)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.OrganizationId == organizationId, cancellationToken);

        if (session == null)
        {
            return null;
        }

        session.Status = request.Status;
        session.ErrorMessage = request.ErrorMessage;
        session.ErrorCode = request.ErrorCode;
        session.CompletedAt = DateTime.UtcNow;
        session.DurationMs = (int)(session.CompletedAt.Value - session.StartedAt).TotalMilliseconds;

        // Update client status
        var client = session.GstClient;
        client.LastSyncAt = DateTime.UtcNow;
        client.LastSyncSource = session.SyncSource;
        client.TotalSyncsPerformed++;

        if (request.Status == GstSyncSessionStatus.Completed)
        {
            client.LastSuccessfulSyncAt = DateTime.UtcNow;
            client.ConsecutiveFailures = 0;
            client.Status = GstClientStatus.Active;
            client.StatusMessage = null;
            client.TotalNoticesSynced += session.NoticesNew;
        }
        else if (request.Status == GstSyncSessionStatus.Failed)
        {
            client.ConsecutiveFailures++;
            if (client.ConsecutiveFailures >= 5)
            {
                client.Status = GstClientStatus.Error;
                client.StatusMessage = $"Sync failed {client.ConsecutiveFailures} times: {request.ErrorMessage}";
            }
        }

        // Calculate next sync due time
        client.NextSyncDueAt = DateTime.UtcNow.AddHours(client.SyncFrequencyHours);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Completed sync session {SessionId} with status {Status}", session.Id, request.Status);

        // Send notifications asynchronously (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                if (request.Status == GstSyncSessionStatus.Completed && (session.NoticesNew > 0 || session.NoticesUpdated > 0))
                {
                    await _notificationService.NotifyNoticesSyncedAsync(
                        organizationId,
                        session.GstClientId,
                        session.NoticesNew,
                        session.NoticesUpdated,
                        CancellationToken.None);
                }
                else if (request.Status == GstSyncSessionStatus.Failed)
                {
                    await _notificationService.NotifySyncFailedAsync(
                        organizationId,
                        session.GstClientId,
                        request.ErrorMessage ?? "Unknown error",
                        client.ConsecutiveFailures,
                        CancellationToken.None);

                    // If sync was paused due to errors
                    if (client.ConsecutiveFailures >= 5)
                    {
                        await _notificationService.NotifySyncPausedAsync(
                            organizationId,
                            session.GstClientId,
                            $"Sync failed {client.ConsecutiveFailures} consecutive times",
                            CancellationToken.None);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send sync completion notification for session {SessionId}", session.Id);
            }
        }, CancellationToken.None);

        return MapSessionToDto(session);
    }

    public async Task<GstSyncSessionDto?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _context.GstSyncSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        return session == null ? null : MapSessionToDto(session);
    }

    public async Task<List<GstSyncSessionDto>> GetSyncHistoryAsync(Guid clientId, int limit = 20, CancellationToken cancellationToken = default)
    {
        var sessions = await _context.GstSyncSessions
            .Where(s => s.GstClientId == clientId)
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return sessions.Select(MapSessionToDto).ToList();
    }

    public async Task<GstSyncSessionListResponse> GetSessionsAsync(Guid organizationId, Guid? gstClientId, string? status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.GstSyncSessions
            .Where(s => s.OrganizationId == organizationId);

        if (gstClientId.HasValue)
        {
            query = query.Where(s => s.GstClientId == gstClientId.Value);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(s => s.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var sessions = await query
            .OrderByDescending(s => s.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new GstSyncSessionListResponse
        {
            Items = sessions.Select(MapSessionToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<GstSyncStatisticsDto> GetStatisticsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;

        // Client statistics
        var clientStats = await _context.GstClients
            .Where(c => c.OrganizationId == organizationId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalClients = g.Count(),
                ActiveClients = g.Count(c => c.Status == GstClientStatus.Active),
                PausedClients = g.Count(c => c.Status == GstClientStatus.Paused),
                ErrorClients = g.Count(c => c.Status == GstClientStatus.Error),
                TotalNoticesSynced = g.Sum(c => c.TotalNoticesSynced)
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Notice statistics
        var noticeStats = await _context.GstNoticesRaw
            .Where(n => n.OrganizationId == organizationId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                PendingImports = g.Count(n => !n.ImportedToNotices),
                SyncedToday = g.Count(n => n.FirstSyncedAt >= today)
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Session statistics
        var sessionStats = await _context.GstSyncSessions
            .Where(s => s.OrganizationId == organizationId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalSessions = g.Count(),
                SessionsToday = g.Count(s => s.StartedAt >= today)
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Last sync time
        var lastSync = await _context.GstSyncSessions
            .Where(s => s.OrganizationId == organizationId && s.Status == GstSyncSessionStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .Select(s => s.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new GstSyncStatisticsDto
        {
            TotalClients = clientStats?.TotalClients ?? 0,
            ActiveClients = clientStats?.ActiveClients ?? 0,
            PausedClients = clientStats?.PausedClients ?? 0,
            ErrorClients = clientStats?.ErrorClients ?? 0,
            TotalNoticesSynced = clientStats?.TotalNoticesSynced ?? 0,
            NoticesSyncedToday = noticeStats?.SyncedToday ?? 0,
            PendingImports = noticeStats?.PendingImports ?? 0,
            LastSyncTime = lastSync,
            TotalSyncSessions = sessionStats?.TotalSessions ?? 0,
            SyncSessionsToday = sessionStats?.SessionsToday ?? 0
        };
    }

    private async Task<(string status, GstNoticeRaw notice)> ProcessNoticeAsync(GstSyncSession session, SyncNoticeData data, CancellationToken cancellationToken)
    {
        // Calculate content hash for change detection
        var contentHash = ComputeContentHash(data);

        // Check if notice already exists
        var existingNotice = await _context.GstNoticesRaw
            .FirstOrDefaultAsync(n => n.GstClientId == session.GstClientId && n.PortalNoticeId == data.PortalNoticeId, cancellationToken);

        if (existingNotice != null)
        {
            // Check if content changed
            if (existingNotice.ContentHash == contentHash)
            {
                // Just update sync metadata
                existingNotice.LastSyncedAt = DateTime.UtcNow;
                existingNotice.LastSyncSessionId = session.Id;
                existingNotice.SyncCount++;
                return ("unchanged", existingNotice);
            }

            // Update notice
            UpdateNoticeFromData(existingNotice, data);
            existingNotice.ContentHash = contentHash;
            existingNotice.LastSyncedAt = DateTime.UtcNow;
            existingNotice.LastSyncSessionId = session.Id;
            existingNotice.SyncCount++;
            return ("updated", existingNotice);
        }

        // Create new notice
        var notice = new GstNoticeRaw
        {
            GstClientId = session.GstClientId,
            OrganizationId = session.OrganizationId,
            Gstin = session.Gstin,
            PortalNoticeId = data.PortalNoticeId,
            ReferenceNumber = data.ReferenceNumber,
            NoticeType = data.NoticeType,
            NoticeCategory = GstNoticeCategory.FromNoticeType(data.NoticeType),
            IssueDate = data.IssueDate,
            DueDate = data.DueDate,
            StatusOnPortal = data.StatusOnPortal,
            DemandAmount = data.DemandAmount,
            TaxAmount = data.TaxAmount,
            InterestAmount = data.InterestAmount,
            PenaltyAmount = data.PenaltyAmount,
            TaxPeriod = data.TaxPeriod,
            FinancialYear = data.FinancialYear,
            SectionRule = data.SectionRule,
            OfficerName = data.OfficerName,
            OfficerDesignation = data.OfficerDesignation,
            Jurisdiction = data.Jurisdiction,
            PdfAvailable = data.PdfAvailable,
            RawData = data.RawData,
            ContentHash = contentHash,
            FirstSyncedAt = DateTime.UtcNow,
            LastSyncedAt = DateTime.UtcNow,
            LastSyncSessionId = session.Id
        };

        _context.GstNoticesRaw.Add(notice);
        return ("new", notice);
    }

    private static void UpdateNoticeFromData(GstNoticeRaw notice, SyncNoticeData data)
    {
        notice.ReferenceNumber = data.ReferenceNumber;
        notice.NoticeType = data.NoticeType;
        notice.NoticeCategory = GstNoticeCategory.FromNoticeType(data.NoticeType);
        notice.IssueDate = data.IssueDate;
        notice.DueDate = data.DueDate;
        notice.StatusOnPortal = data.StatusOnPortal;
        notice.DemandAmount = data.DemandAmount;
        notice.TaxAmount = data.TaxAmount;
        notice.InterestAmount = data.InterestAmount;
        notice.PenaltyAmount = data.PenaltyAmount;
        notice.TaxPeriod = data.TaxPeriod;
        notice.FinancialYear = data.FinancialYear;
        notice.SectionRule = data.SectionRule;
        notice.OfficerName = data.OfficerName;
        notice.OfficerDesignation = data.OfficerDesignation;
        notice.Jurisdiction = data.Jurisdiction;
        notice.PdfAvailable = data.PdfAvailable;
        notice.RawData = data.RawData;
    }

    private static string ComputeContentHash(SyncNoticeData data)
    {
        // Create a deterministic JSON string of key fields for hashing
        var hashInput = JsonSerializer.Serialize(new
        {
            data.PortalNoticeId,
            data.ReferenceNumber,
            data.NoticeType,
            data.IssueDate,
            data.DueDate,
            data.StatusOnPortal,
            data.DemandAmount,
            data.TaxAmount,
            data.InterestAmount,
            data.PenaltyAmount
        }, new JsonSerializerOptions { WriteIndented = false });

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static GstSyncSessionDto MapSessionToDto(GstSyncSession session)
    {
        return new GstSyncSessionDto
        {
            Id = session.Id,
            GstClientId = session.GstClientId,
            Gstin = session.Gstin,
            SyncSource = session.SyncSource,
            SourceVersion = session.SourceVersion,
            StartedAt = session.StartedAt,
            CompletedAt = session.CompletedAt,
            DurationMs = session.DurationMs,
            Status = session.Status,
            ErrorMessage = session.ErrorMessage,
            ErrorCode = session.ErrorCode,
            NoticesFound = session.NoticesFound,
            NoticesNew = session.NoticesNew,
            NoticesUpdated = session.NoticesUpdated,
            NoticesUnchanged = session.NoticesUnchanged,
            PdfsDownloaded = session.PdfsDownloaded,
            PdfsFailed = session.PdfsFailed
        };
    }
}
