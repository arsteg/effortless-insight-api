using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// A GST notice a CA has synced or uploaded for a CaProspectClient before that
/// client's Business Owner has accepted an invitation and claimed a real
/// Organization. Mirrors the subset of Notice's fields needed for full
/// functionality once merged (see Phase 3 handoff), plus a dedup identity
/// (SourceReferenceNumber + ContentHash) so the merge into a real Notice can
/// never create a duplicate.
///
/// Deliberately modeled after Notice, not GstNoticeRaw: GstNoticeRaw is
/// portal-scrape-specific (PortalNoticeId, RawData) and always belongs to an
/// existing Organization via GstClient. This table accepts both manual uploads
/// and synced notices for a client that has no Organization yet.
/// </summary>
public class CaStagedNotice : BaseEntity
{
    [Required]
    public Guid CaProspectClientId { get; set; }

    [ForeignKey(nameof(CaProspectClientId))]
    public CaProspectClient CaProspectClient { get; set; } = null!;

    [MaxLength(100)]
    public string? NoticeNumber { get; set; }

    [MaxLength(20)]
    public string? NoticeType { get; set; }

    [MaxLength(50)]
    public string? NoticeCategory { get; set; }

    [MaxLength(1000)]
    public string? Summary { get; set; }

    /// <summary>
    /// Denormalized plaintext GSTIN for display/filtering, matching the
    /// convention of Notice.Gstin (the authoritative encrypted record is
    /// CaProspectClient.Gstin, same as Notice's authoritative record is
    /// OrganizationGstin.Gstin).
    /// </summary>
    [MaxLength(15)]
    public string? Gstin { get; set; }

    public DateOnly? IssueDate { get; set; }

    public DateOnly? ResponseDeadline { get; set; }

    [Column(TypeName = "decimal(15,2)")]
    public decimal? TaxAmount { get; set; }

    [Column(TypeName = "decimal(15,2)")]
    public decimal? PenaltyAmount { get; set; }

    [Column(TypeName = "decimal(15,2)")]
    public decimal? InterestAmount { get; set; }

    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    [MaxLength(10)]
    public string? FinancialYear { get; set; }

    [MaxLength(500)]
    public string? FileUrl { get; set; }

    [MaxLength(255)]
    public string? FileName { get; set; }

    public int? FileSize { get; set; }

    [MaxLength(100)]
    public string? FileMimeType { get; set; }

    /// <summary>
    /// SHA-256 of the file content, when a file is attached (manual upload).
    /// </summary>
    [MaxLength(64)]
    public string? FileHash { get; set; }

    /// <summary>
    /// The notice's own reference/acknowledgement number when known (from a
    /// synced portal notice, or extracted from an uploaded PDF). Combined with
    /// FileHash, this is the dedup identity used by the Phase 3 merge.
    /// </summary>
    [MaxLength(100)]
    public string? SourceReferenceNumber { get; set; }

    [Required]
    public Guid UploadedByUserId { get; set; }

    [ForeignKey(nameof(UploadedByUserId))]
    public ApplicationUser UploadedByUser { get; set; } = null!;

    [Required]
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Source of this staged notice: manual_upload, gst_sync.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Source { get; set; } = "manual_upload";

    public bool MergedToNotices { get; set; }

    public Guid? MergedNoticeId { get; set; }

    public DateTime? MergedAt { get; set; }
}
