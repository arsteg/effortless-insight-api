using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EffortlessInsight.Api.Services.Notices;

public sealed class DuplicateNoticeException(Guid existingNoticeId)
    : InvalidOperationException("NOTICE_ALREADY_EXISTS: This notice already exists.")
{
    public Guid ExistingNoticeId { get; } = existingNoticeId;
}

/// <summary>Shared identity for every notice ingestion path. Filenames are never identifiers.</summary>
public static class NoticeIdentity
{
    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    public static bool IsDuplicate(Exception error) => error is DuplicateNoticeException
        || error is DbUpdateException { InnerException: PostgresException { SqlState: "23505", ConstraintName: var name } }
            && name is "UX_Notices_Identity" or "UX_Notices_Content" or "UX_Notices_Portal"
                or "IX_Notices_OrganizationId_EmailImportKey" or "PK_Notices";

    public static Task<Notice?> FindAsync(ApplicationDbContext db, Guid organizationId, string? gstin,
        string? reference, string? portalId = null, string? fileHash = null, Guid? excludeId = null,
        CancellationToken ct = default)
    {
        var gst = Normalize(gstin);
        var number = Normalize(reference);
        var portal = Normalize(portalId);
        return db.Notices.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(n =>
            n.OrganizationId == organizationId && n.DeletedAt == null && n.Id != excludeId &&
            ((!string.IsNullOrEmpty(fileHash) && n.FileHash == fileHash) ||
             (gst != null && n.Gstin != null && n.Gstin.Trim().ToUpper() == gst &&
              ((number != null && n.NoticeNumber != null && n.NoticeNumber.Trim().ToUpper() == number) ||
               (portal != null && n.GstnNoticeId != null && n.GstnNoticeId.Trim().ToUpper() == portal)))), ct);
    }

    public static async Task ValidateGstinAsync(ApplicationDbContext db, Guid organizationId, string? gstin, CancellationToken ct)
    {
        // Registry GSTINs are encrypted: scope the query, then compare decrypted values.
        if (Normalize(gstin) == null) return;
        var isBusinessOwner = await db.OrganizationMembers.AnyAsync(m =>
            m.OrganizationId == organizationId && m.Role == "owner" && m.Status == "active"
            && m.DeletedAt == null && !m.User.IsCA, ct)
            || await db.CaProspectClients.AnyAsync(p => p.MergedIntoOrganizationId == organizationId
                && p.Status == "merged" && p.DeletedAt == null, ct);
        if (!isBusinessOwner) return;
        var registry = await db.OrganizationGstins.AsNoTracking().Where(g =>
            g.OrganizationId == organizationId && g.DeletedAt == null).ToListAsync(ct);
        if (!registry.Any(g => Normalize(g.Gstin) == Normalize(gstin)))
            throw new InvalidOperationException("INVALID_GSTIN: The document GSTIN is not registered in this organization.");
    }

    public static async Task ValidateAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var entries = db.ChangeTracker.Entries<Notice>().Where(e => e.State == EntityState.Added ||
            e.State == EntityState.Modified && new[] { nameof(Notice.OrganizationId), nameof(Notice.Gstin),
                nameof(Notice.NoticeNumber), nameof(Notice.GstnNoticeId), nameof(Notice.FileHash), nameof(Notice.DeletedAt) }
                .Any(p => e.Property(p).IsModified)).ToList();
        var pending = new List<Notice>();
        foreach (var entry in entries.Where(e => e.Entity.DeletedAt == null))
        {
            var notice = entry.Entity;
            notice.NoticeNumber = string.IsNullOrWhiteSpace(notice.NoticeNumber) ? null : notice.NoticeNumber.Trim();
            notice.GstnNoticeId = string.IsNullOrWhiteSpace(notice.GstnNoticeId) ? null : notice.GstnNoticeId.Trim();
            await ValidateGstinAsync(db, notice.OrganizationId, notice.Gstin, ct);
            var existing = pending.FirstOrDefault(n => n.OrganizationId == notice.OrganizationId &&
                ((!string.IsNullOrEmpty(notice.FileHash) && n.FileHash == notice.FileHash) ||
                 (Normalize(notice.Gstin) != null && Normalize(n.Gstin) == Normalize(notice.Gstin) &&
                  ((Normalize(notice.NoticeNumber) != null && Normalize(n.NoticeNumber) == Normalize(notice.NoticeNumber)) ||
                   (Normalize(notice.GstnNoticeId) != null && Normalize(n.GstnNoticeId) == Normalize(notice.GstnNoticeId))))));
            existing ??= await FindAsync(db, notice.OrganizationId, notice.Gstin, notice.NoticeNumber,
                notice.GstnNoticeId, notice.FileHash, notice.Id, ct);
            if (existing != null) throw new DuplicateNoticeException(existing.Id);
            pending.Add(notice);
        }
    }
}
