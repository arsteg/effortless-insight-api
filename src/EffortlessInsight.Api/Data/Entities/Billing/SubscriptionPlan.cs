using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Defines available subscription plans with pricing, limits, and features.
/// This replaces the basic Plan entity with a more comprehensive billing-aware model.
/// </summary>
public class SubscriptionPlan : BaseEntity
{
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Monthly price in paise (INR smallest unit). Null for enterprise/custom pricing.
    /// </summary>
    public int? PricingMonthly { get; set; }

    /// <summary>
    /// Annual price in paise (INR smallest unit). Null for enterprise/custom pricing.
    /// </summary>
    public int? PricingAnnually { get; set; }

    /// <summary>
    /// Per-seat monthly price in paise for additional users.
    /// </summary>
    public int? PerSeatMonthly { get; set; }

    /// <summary>
    /// Per-seat annual price in paise for additional users.
    /// </summary>
    public int? PerSeatAnnually { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "INR";

    /// <summary>
    /// Plan limits stored as JSONB. Contains: noticesPerMonth, users, storageGb, organizationsCount, apiCalls
    /// </summary>
    public PlanLimits Limits { get; set; } = new();

    /// <summary>
    /// Feature flags stored as JSONB. Array of feature codes like: full_ai_analysis, priority_processing, etc.
    /// </summary>
    public List<string> Features { get; set; } = [];

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this plan should be highlighted as popular/recommended.
    /// </summary>
    public bool IsPopular { get; set; }

    /// <summary>
    /// Number of trial days for this plan. 0 means no trial (e.g., free plan).
    /// </summary>
    public int TrialDays { get; set; }

    /// <summary>
    /// Display order on pricing page.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether this plan requires contacting sales (enterprise plans).
    /// </summary>
    public bool ContactSales { get; set; }

    /// <summary>
    /// Starting price for enterprise plans (display only).
    /// </summary>
    public int? StartingAt { get; set; }

    /// <summary>
    /// Razorpay plan ID for monthly billing.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayPlanIdMonthly { get; set; }

    /// <summary>
    /// Razorpay plan ID for annual billing.
    /// </summary>
    [MaxLength(50)]
    public string? RazorpayPlanIdAnnually { get; set; }

    /// <summary>
    /// Additional metadata for the plan.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Plan limits configuration.
/// </summary>
public class PlanLimits
{
    /// <summary>
    /// Maximum notices per month. -1 for unlimited.
    /// </summary>
    public int NoticesPerMonth { get; set; }

    /// <summary>
    /// Maximum users. -1 for unlimited.
    /// </summary>
    public int Users { get; set; }

    /// <summary>
    /// Storage limit in GB. -1 for unlimited.
    /// </summary>
    public int StorageGb { get; set; }

    /// <summary>
    /// Maximum organizations. -1 for unlimited.
    /// </summary>
    public int OrganizationsCount { get; set; } = 1;

    /// <summary>
    /// Whether additional users beyond the base limit can be purchased.
    /// </summary>
    public bool AdditionalUsersAllowed { get; set; }

    /// <summary>
    /// Maximum API calls per month. -1 for unlimited.
    /// </summary>
    public int ApiCalls { get; set; } = 10000;
}
