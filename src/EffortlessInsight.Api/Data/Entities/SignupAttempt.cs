using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// A lead captured when someone verifies their mobile via signup OTP but has
/// not (yet) completed registration. The row is created/refreshed on OTP
/// verification and hard-deleted the moment registration succeeds, so
/// whatever remains here is the "unsuccessful signups" call list shown in the
/// admin portal. Pre-signup data — deliberately has no organization/tenant.
/// </summary>
public class SignupAttempt : BaseEntity
{
    [Required]
    [MaxLength(20)]
    public string Mobile { get; set; } = null!;

    /// <summary>Last 10 digits; unique — one open attempt per number.</summary>
    [Required]
    [MaxLength(10)]
    public string MobileNormalized { get; set; } = null!;

    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    /// <summary>Where the attempt came from: web | mobile.</summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = "web";

    public DateTime MobileVerifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when an admin marks the lead as called.</summary>
    public DateTime? ContactedAt { get; set; }

    [MaxLength(1000)]
    public string? ContactNotes { get; set; }

    // UTM tracking
    /// <summary>
    /// UTM source parameter identifying traffic source.
    /// </summary>
    [MaxLength(50)]
    public string? UtmSource { get; set; }

    /// <summary>
    /// UTM medium parameter identifying marketing medium.
    /// </summary>
    [MaxLength(50)]
    public string? UtmMedium { get; set; }

    /// <summary>
    /// UTM campaign parameter identifying campaign name.
    /// </summary>
    [MaxLength(100)]
    public string? UtmCampaign { get; set; }

    /// <summary>
    /// UTM term parameter for paid search keywords.
    /// </summary>
    [MaxLength(100)]
    public string? UtmTerm { get; set; }

    /// <summary>
    /// UTM content parameter for A/B testing and content-targeted ads.
    /// </summary>
    [MaxLength(100)]
    public string? UtmContent { get; set; }
}
