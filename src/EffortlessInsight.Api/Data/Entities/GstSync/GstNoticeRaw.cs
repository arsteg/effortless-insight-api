using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.GstSync;

/// <summary>
/// Stores raw GST notice data as captured from the GST portal.
/// This is a staging table before notices are imported to the main Notices module.
/// </summary>
public class GstNoticeRaw : BaseEntity
{
    /// <summary>
    /// The GST client connection this notice belongs to.
    /// </summary>
    [Required]
    public Guid GstClientId { get; set; }

    [ForeignKey(nameof(GstClientId))]
    public GstClient GstClient { get; set; } = null!;

    /// <summary>
    /// Organization ID for efficient querying (denormalized).
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// GSTIN this notice belongs to (denormalized).
    /// </summary>
    [Required]
    [MaxLength(15)]
    public string Gstin { get; set; } = null!;

    /// <summary>
    /// Unique identifier from the GST portal for this notice.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string PortalNoticeId { get; set; } = null!;

    /// <summary>
    /// Reference number of the notice (e.g., DRC-01 reference).
    /// </summary>
    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    /// <summary>
    /// Type of notice (DRC-01, ASMT-10, etc.).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string NoticeType { get; set; } = null!;

    /// <summary>
    /// Category of notice (demand_recovery, assessment, general, etc.).
    /// </summary>
    [MaxLength(50)]
    public string? NoticeCategory { get; set; }

    /// <summary>
    /// Date when the notice was issued.
    /// </summary>
    [Required]
    public DateOnly IssueDate { get; set; }

    /// <summary>
    /// Due date for response/compliance.
    /// </summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>
    /// Status as shown on the GST portal (PENDING, REPLIED, CLOSED, etc.).
    /// </summary>
    [MaxLength(50)]
    public string? StatusOnPortal { get; set; }

    /// <summary>
    /// Total demand amount if applicable.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? DemandAmount { get; set; }

    /// <summary>
    /// Tax component of the demand.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? TaxAmount { get; set; }

    /// <summary>
    /// Interest component of the demand.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? InterestAmount { get; set; }

    /// <summary>
    /// Penalty component of the demand.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? PenaltyAmount { get; set; }

    /// <summary>
    /// Tax period covered by the notice (e.g., "Apr-2026" or "FY 2025-26").
    /// </summary>
    [MaxLength(50)]
    public string? TaxPeriod { get; set; }

    /// <summary>
    /// Financial year covered (e.g., "2025-26").
    /// </summary>
    [MaxLength(10)]
    public string? FinancialYear { get; set; }

    /// <summary>
    /// Legal section/rule under which notice was issued (e.g., "Section 73(1)").
    /// </summary>
    [MaxLength(100)]
    public string? SectionRule { get; set; }

    /// <summary>
    /// Name of the issuing officer.
    /// </summary>
    [MaxLength(255)]
    public string? OfficerName { get; set; }

    /// <summary>
    /// Designation of the issuing officer.
    /// </summary>
    [MaxLength(255)]
    public string? OfficerDesignation { get; set; }

    /// <summary>
    /// Jurisdiction (ward/division/zone).
    /// </summary>
    [MaxLength(255)]
    public string? Jurisdiction { get; set; }

    /// <summary>
    /// Whether PDF is available for this notice.
    /// </summary>
    public bool PdfAvailable { get; set; }

    /// <summary>
    /// S3 key where the PDF is stored.
    /// </summary>
    [MaxLength(500)]
    public string? PdfS3Key { get; set; }

    /// <summary>
    /// Size of the PDF in bytes.
    /// </summary>
    public int? PdfSizeBytes { get; set; }

    /// <summary>
    /// When the PDF was downloaded.
    /// </summary>
    public DateTime? PdfDownloadedAt { get; set; }

    /// <summary>
    /// Raw data from the GST portal for debugging/reprocessing.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? RawData { get; set; }

    /// <summary>
    /// Whether this notice has been imported to the main Notices module.
    /// </summary>
    public bool ImportedToNotices { get; set; }

    /// <summary>
    /// ID of the imported notice in the main Notices table.
    /// </summary>
    public Guid? ImportedNoticeId { get; set; }

    /// <summary>
    /// When the notice was imported to the main module.
    /// </summary>
    public DateTime? ImportedAt { get; set; }

    /// <summary>
    /// When this notice was first synced.
    /// </summary>
    [Required]
    public DateTime FirstSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this notice was last synced.
    /// </summary>
    [Required]
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The sync session that last synced this notice.
    /// </summary>
    public Guid? LastSyncSessionId { get; set; }

    [ForeignKey(nameof(LastSyncSessionId))]
    public GstSyncSession? LastSyncSession { get; set; }

    /// <summary>
    /// Number of times this notice has been synced.
    /// </summary>
    public int SyncCount { get; set; } = 1;

    /// <summary>
    /// SHA-256 hash of key fields for change detection.
    /// </summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }
}

/// <summary>
/// GST notice type constants.
/// </summary>
public static class GstNoticeType
{
    // Demand and Recovery
    public const string DRC01 = "DRC-01";
    public const string DRC01A = "DRC-01A";
    public const string DRC01B = "DRC-01B";
    public const string DRC02 = "DRC-02";
    public const string DRC03 = "DRC-03";
    public const string DRC07 = "DRC-07";

    // Assessment
    public const string ASMT10 = "ASMT-10";
    public const string ASMT11 = "ASMT-11";
    public const string ASMT12 = "ASMT-12";
    public const string ASMT14 = "ASMT-14";

    // Adjudication
    public const string ADJ01 = "ADJ-01";
    public const string ADJ02 = "ADJ-02";

    // Registration
    public const string REG17 = "REG-17";
    public const string REG18 = "REG-18";
    public const string REG31 = "REG-31";

    // Refund
    public const string RFD08 = "RFD-08";
    public const string RFD09 = "RFD-09";

    // General
    public const string General = "GENERAL";
    public const string Other = "OTHER";
}

/// <summary>
/// GST notice category constants.
/// </summary>
public static class GstNoticeCategory
{
    public const string DemandRecovery = "demand_recovery";
    public const string Assessment = "assessment";
    public const string Adjudication = "adjudication";
    public const string Registration = "registration";
    public const string Refund = "refund";
    public const string General = "general";
    public const string Other = "other";

    public static readonly string[] All =
    [
        DemandRecovery, Assessment, Adjudication, Registration, Refund, General, Other
    ];

    public static bool IsValid(string category) => All.Contains(category);

    /// <summary>
    /// Infer category from notice type.
    /// </summary>
    public static string FromNoticeType(string noticeType)
    {
        return noticeType.ToUpperInvariant() switch
        {
            var t when t.StartsWith("DRC") => DemandRecovery,
            var t when t.StartsWith("ASMT") => Assessment,
            var t when t.StartsWith("ADJ") => Adjudication,
            var t when t.StartsWith("REG") => Registration,
            var t when t.StartsWith("RFD") => Refund,
            "GENERAL" => General,
            _ => Other
        };
    }
}
