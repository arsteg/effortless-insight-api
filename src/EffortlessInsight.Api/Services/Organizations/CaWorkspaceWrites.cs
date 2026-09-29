using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.GstSync;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Persistence invariant shared by uploads, manual entry, imports and sync jobs.
/// The lock is held until SaveChanges (or the enclosing handover) commits.
/// Identified notices may route to an accepted BO tenant only after validating
/// the destination relationship, current write membership and subscription.
/// In-flight sync retries resolve their tenant at the extension API boundary.
/// </summary>
public static class CaWorkspaceWrites
{
    public static string Hash(string gstin) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(gstin.Trim().ToUpperInvariant())));

    public static async Task LockAsync(ApplicationDbContext db, Guid organizationId, string gstin,
        CancellationToken ct = default)
    {
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL") return;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"ca-workspace:{organizationId}:{Hash(gstin)}"));
        var key = BinaryPrimitives.ReadInt64BigEndian(bytes);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    public static bool HasWrites(ApplicationDbContext db) => db.ChangeTracker.Entries().Any(e =>
        (e.State == EntityState.Added && e.Entity is Notice or GstClient or GstNoticeRaw or GstSyncSession)
        || (e.Entity is Notice && e.State == EntityState.Modified && e.Property(nameof(Notice.Gstin)).IsModified));

    public static async Task PrepareAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var notices = db.ChangeTracker.Entries<Notice>().Where(e => e.State == EntityState.Added
                || (e.State == EntityState.Modified && e.Property(n => n.Gstin).IsModified))
            .Select(e => e.Entity).Where(n => !string.IsNullOrWhiteSpace(n.Gstin)).ToList();
        foreach (var notice in notices)
        {
            notice.Gstin = notice.Gstin!.Trim().ToUpperInvariant();
            notice.GstinHash = Hash(notice.Gstin);
            if (notice.CaProspectClientId is Guid prospectId)
            {
                var linked = db.CaProspectClients.Local.FirstOrDefault(p => p.Id == prospectId)
                    ?? await db.CaProspectClients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == prospectId, ct);
                if (linked != null && linked.GstinHash != notice.GstinHash)
                    throw new InvalidOperationException("CLIENT_GSTIN_MISMATCH: The notice belongs to a different client GSTIN.");
            }
        }
        var clients = db.ChangeTracker.Entries<GstClient>().Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity).Where(e => !string.IsNullOrWhiteSpace(e.Gstin)).ToList();
        var raw = db.ChangeTracker.Entries<GstNoticeRaw>().Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity).Where(e => !string.IsNullOrWhiteSpace(e.Gstin)).ToList();
        var sessions = db.ChangeTracker.Entries<GstSyncSession>().Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity).Where(e => !string.IsNullOrWhiteSpace(e.Gstin)).ToList();
        var scopes = notices.Select(n => (n.OrganizationId, Gstin: n.Gstin!.Trim().ToUpperInvariant()))
            .Concat(clients.Select(c => (c.OrganizationId, Gstin: c.Gstin.Trim().ToUpperInvariant())))
            .Concat(raw.Select(r => (r.OrganizationId, Gstin: r.Gstin.Trim().ToUpperInvariant())))
            .Concat(sessions.Select(r => (r.OrganizationId, Gstin: r.Gstin.Trim().ToUpperInvariant())))
            .Distinct().OrderBy(s => s.OrganizationId).ThenBy(s => s.Gstin).ToList();

        var routes = new Dictionary<Guid, OrganizationGstin>();
        foreach (var notice in notices)
        {
            var destination = await CaNoticeRouting.ResolveAsync(db, notice.OrganizationId, notice.UploadedById, notice.Gstin!, ct);
            if (destination != null) routes[notice.Id] = destination;
        }
        foreach (var scope in scopes.Concat(routes.Values.Select(g => (g.OrganizationId, Gstin: g.Gstin)))
                     .Distinct().OrderBy(s => s.OrganizationId).ThenBy(s => s.Gstin))
            await LockAsync(db, scope.OrganizationId, scope.Gstin, ct);

        foreach (var scope in scopes)
        {
            await LockAsync(db, scope.OrganizationId, scope.Gstin, ct);
            if (notices.Where(n => n.OrganizationId == scope.OrganizationId && Hash(n.Gstin!) == Hash(scope.Gstin)
                    && n.DeletedAt == null && n.GstnNoticeId != null).GroupBy(n => n.GstnNoticeId).Any(g => g.Count() > 1))
                throw new InvalidOperationException("NOTICE_ALREADY_EXISTS: This batch contains a duplicate GST portal notice.");
            foreach (var notice in notices.Where(n => n.OrganizationId == scope.OrganizationId
                         && Hash(n.Gstin!) == Hash(scope.Gstin) && n.GstnNoticeId != null))
            {
                var hashForNotice = Hash(scope.Gstin);
                if (await db.Notices.IgnoreQueryFilters().AnyAsync(n => n.OrganizationId == scope.OrganizationId
                        && n.Id != notice.Id && n.DeletedAt == null && n.GstnNoticeId == notice.GstnNoticeId
                        && (n.GstinHash == hashForNotice || n.Gstin == scope.Gstin), ct))
                    throw new InvalidOperationException("NOTICE_ALREADY_EXISTS: This GST portal notice is already imported.");
            }
            var scopeHash = Hash(scope.Gstin);
            // An organization can be owned by someone who also has a CA account.
            // Explicit BO ownership takes precedence over that person's account type.
            if (await db.CaProspectClients.AnyAsync(p => p.MergedIntoOrganizationId == scope.OrganizationId
                    && p.GstinHash == scopeHash && p.Status == "merged" && p.DeletedAt == null, ct)
                || db.CaProspectClients.Local.Any(p => p.MergedIntoOrganizationId == scope.OrganizationId
                    && p.GstinHash == scopeHash && p.Status == "merged" && p.DeletedAt == null)) continue;
            var ownerId = await db.OrganizationMembers.Where(m => m.OrganizationId == scope.OrganizationId
                    && m.Role == "owner" && m.Status == "active" && m.DeletedAt == null && m.User.IsCA)
                .Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct);
            if (ownerId == null) continue; // BO -> CA membership does not create a distributor workspace.

            var hash = Hash(scope.Gstin);
            var prospect = await db.CaProspectClients.AsNoTracking().FirstOrDefaultAsync(p =>
                p.CaUserId == ownerId && p.GstinHash == hash && p.DeletedAt == null, ct);
            prospect ??= db.CaProspectClients.Local.FirstOrDefault(p => p.CaUserId == ownerId
                && p.GstinHash == hash && p.DeletedAt == null);
            if (prospect?.Status == "merged")
            {
                var matching = notices.Where(n => n.OrganizationId == scope.OrganizationId && Hash(n.Gstin!) == hash).ToList();
                if (matching.Count == 0 || clients.Any(c => c.OrganizationId == scope.OrganizationId && Hash(c.Gstin) == hash)
                    || raw.Any(r => r.OrganizationId == scope.OrganizationId && Hash(r.Gstin) == hash)
                    || sessions.Any(s => s.OrganizationId == scope.OrganizationId && Hash(s.Gstin) == hash))
                    throw new InvalidOperationException("CLIENT_ORGANIZATION_CHANGED: Retry sync to resolve the accepted client's organization.");
                foreach (var notice in matching)
                {
                    if (!routes.TryGetValue(notice.Id, out var destination))
                        throw new InvalidOperationException("CLIENT_ROUTING_RETRY: The client was accepted during processing. Retry to resolve its organization.");
                    await CaNoticeRouting.ResolveAsync(db, scope.OrganizationId, notice.UploadedById, scope.Gstin, ct);
                    if (await db.Notices.IgnoreQueryFilters().AnyAsync(n => n.OrganizationId == destination.OrganizationId
                            && n.Id != notice.Id && n.DeletedAt == null && n.GstinHash == hash
                            && ((notice.GstnNoticeId != null && n.GstnNoticeId == notice.GstnNoticeId)
                                || (notice.FileHash != null && n.FileHash == notice.FileHash)), ct))
                        throw new InvalidOperationException("NOTICE_ALREADY_EXISTS: A matching notice already exists in the BO organization.");
                    await new CaDistributorHandover(db).RouteIdentifiedNoticeAsync(notice, destination, prospect.Id, ct);
                }
                continue;
            }
            if (prospect != null && prospect.Status != "staging")
                throw new InvalidOperationException("PROSPECT_CLIENT_NOT_STAGING");
            if (prospect == null)
            {
                prospect = new CaProspectClient { CaUserId = ownerId.Value, Gstin = scope.Gstin, GstinHash = hash };
                db.CaProspectClients.Add(prospect);
            }
            foreach (var notice in notices.Where(n => n.OrganizationId == scope.OrganizationId
                         && Hash(n.Gstin!) == hash))
            {
                notice.Gstin = scope.Gstin;
                notice.GstinHash = hash;
                notice.CaProspectClientId = prospect.Id;
                var registry = (await db.OrganizationGstins.Where(g => g.OrganizationId == scope.OrganizationId && g.DeletedAt == null).ToListAsync(ct))
                    .Concat(db.OrganizationGstins.Local.Where(g => g.OrganizationId == scope.OrganizationId && g.DeletedAt == null))
                    .FirstOrDefault(g => Hash(g.Gstin) == hash);
                if (registry == null)
                {
                    registry = new OrganizationGstin { OrganizationId = scope.OrganizationId, Gstin = scope.Gstin,
                        StateCode = scope.Gstin[..2], StateName = "Unknown", Source = OrganizationGstinSource.Manual,
                        IsPrimary = !await db.OrganizationGstins.AnyAsync(g => g.OrganizationId == scope.OrganizationId && g.DeletedAt == null, ct)
                            && !db.OrganizationGstins.Local.Any(g => g.OrganizationId == scope.OrganizationId && g.DeletedAt == null) };
                    db.OrganizationGstins.Add(registry);
                }
                notice.GstinId = registry.Id;
            }
        }
    }
}
