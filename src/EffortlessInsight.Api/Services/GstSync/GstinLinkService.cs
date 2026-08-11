using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// Links GstSync clients to the organization's GSTIN registry (OrganizationGstins).
///
/// IMPORTANT: OrganizationGstins.Gstin is stored AES-GCM encrypted with a random
/// nonce, so it can never be matched in a SQL WHERE clause — every lookup here
/// materializes the organization's rows (EF decrypts via the value converter)
/// and compares plaintext in memory. Organizations hold at most a handful of
/// GSTINs, so this is cheap.
/// </summary>
public interface IGstinLinkService
{
    /// <summary>
    /// Finds the organization's registry entry for a GSTIN, creating an
    /// unverified entry if none exists. Never touches other organizations'
    /// entries (per-org GSTIN model).
    /// </summary>
    Task<OrganizationGstin> FindOrCreateAsync(
        Guid organizationId,
        string gstin,
        string? tradeName,
        string? legalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent startup backfill: links legacy gst_clients rows that predate
    /// the OrganizationGstinId column, and stamps GstinId on notices that were
    /// imported from sync before the linkage existed.
    /// </summary>
    Task BackfillAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrade the client's registry entry to session-verified: a completed
    /// extension sync proves the user has live access to this GSTIN's portal
    /// account. Never downgrades an already-verified entry.
    /// </summary>
    Task MarkSessionVerifiedAsync(Guid gstClientId, CancellationToken cancellationToken = default);
}

public class GstinLinkService : IGstinLinkService
{
    private readonly ApplicationDbContext _context;
    private readonly IGstinValidatorService _gstinValidator;
    private readonly ILogger<GstinLinkService> _logger;

    public GstinLinkService(
        ApplicationDbContext context,
        IGstinValidatorService gstinValidator,
        ILogger<GstinLinkService> logger)
    {
        _context = context;
        _gstinValidator = gstinValidator;
        _logger = logger;
    }

    public async Task<OrganizationGstin> FindOrCreateAsync(
        Guid organizationId,
        string gstin,
        string? tradeName,
        string? legalName,
        CancellationToken cancellationToken = default)
    {
        gstin = gstin.Trim().ToUpperInvariant();

        // Materialize the org's entries; Gstin is decrypted during materialization.
        var orgGstins = await _context.OrganizationGstins
            .Where(g => g.OrganizationId == organizationId && g.DeletedAt == null)
            .ToListAsync(cancellationToken);

        var existing = orgGstins.FirstOrDefault(g =>
            string.Equals(g.Gstin, gstin, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            return existing;
        }

        var stateCode = gstin[..2];
        var stateName = await _gstinValidator.GetStateNameAsync(stateCode) ?? "Unknown";

        var entry = new OrganizationGstin
        {
            OrganizationId = organizationId,
            Gstin = gstin,
            TradeName = tradeName?.Trim(),
            LegalName = legalName?.Trim(),
            StateCode = stateCode,
            StateName = stateName,
            Status = "active",
            Source = OrganizationGstinSource.GstSync,
            // Registered via sync, not yet verified against the portal (that
            // upgrade happens when a live portal session captures for it, or
            // via the OTP flow).
            IsVerified = false,
            // Same convention as OrganizationService.AddGstinAsync: the org's
            // first GSTIN becomes primary.
            IsPrimary = orgGstins.Count == 0
        };

        _context.OrganizationGstins.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created unverified OrganizationGstin {GstinId} for organization {OrganizationId} (state {StateCode}) via GST sync",
            entry.Id, organizationId, stateCode);

        return entry;
    }

    public async Task MarkSessionVerifiedAsync(Guid gstClientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients
            .FirstOrDefaultAsync(c => c.Id == gstClientId, cancellationToken);

        if (client == null)
        {
            return;
        }

        // Self-heal clients that predate the registry linkage
        if (client.OrganizationGstinId == null)
        {
            var entry = await FindOrCreateAsync(
                client.OrganizationId, client.Gstin, client.TradeName, client.LegalName, cancellationToken);
            client.OrganizationGstinId = entry.Id;
        }

        var registryEntry = await _context.OrganizationGstins
            .FirstOrDefaultAsync(g => g.Id == client.OrganizationGstinId, cancellationToken);

        if (registryEntry == null || registryEntry.IsVerified)
        {
            // Missing (shouldn't happen) or already verified (possibly via a
            // stronger method like OTP) — never downgrade.
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        registryEntry.IsVerified = true;
        registryEntry.VerifiedAt = DateTime.UtcNow;
        registryEntry.VerificationSource = "gst_portal_session";

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "OrganizationGstin {GstinId} marked session-verified via completed extension sync (client {ClientId})",
            registryEntry.Id, gstClientId);
    }

    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        // 1) Link legacy gst_clients that predate the OrganizationGstinId column.
        var unlinkedClients = await _context.GstClients
            .IgnoreQueryFilters()
            .Where(c => c.OrganizationGstinId == null && c.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var client in unlinkedClients)
        {
            var entry = await FindOrCreateAsync(
                client.OrganizationId, client.Gstin, client.TradeName, client.LegalName, cancellationToken);
            client.OrganizationGstinId = entry.Id;
        }

        if (unlinkedClients.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Backfilled OrganizationGstin link for {Count} gst_clients row(s)", unlinkedClients.Count);
        }

        // 2) Stamp GstinId on notices imported from sync before the linkage existed.
        //    Resolved via gst_notices_raw.ImportedNoticeId -> gst_clients.OrganizationGstinId.
        var orphanedPairs = await _context.GstNoticesRaw
            .IgnoreQueryFilters()
            .Where(r => r.ImportedToNotices
                && r.ImportedNoticeId != null
                && r.DeletedAt == null
                && r.GstClient.OrganizationGstinId != null)
            .Select(r => new { NoticeId = r.ImportedNoticeId!.Value, GstinId = r.GstClient.OrganizationGstinId!.Value })
            .ToListAsync(cancellationToken);

        if (orphanedPairs.Count == 0)
        {
            return;
        }

        var noticeIds = orphanedPairs.Select(p => p.NoticeId).ToList();
        var notices = await _context.Notices
            .IgnoreQueryFilters()
            .Where(n => noticeIds.Contains(n.Id) && n.GstinId == null && n.DeletedAt == null)
            .ToListAsync(cancellationToken);

        if (notices.Count == 0)
        {
            return;
        }

        var gstinIdByNoticeId = orphanedPairs
            .GroupBy(p => p.NoticeId)
            .ToDictionary(g => g.Key, g => g.First().GstinId);

        foreach (var notice in notices)
        {
            if (gstinIdByNoticeId.TryGetValue(notice.Id, out var gstinId))
            {
                notice.GstinId = gstinId;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Backfilled GstinId on {Count} imported notice(s)", notices.Count);
    }
}
