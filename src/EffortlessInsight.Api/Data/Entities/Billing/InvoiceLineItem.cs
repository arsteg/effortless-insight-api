using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities.Billing;

/// <summary>
/// Individual line items on an invoice.
/// </summary>
public class InvoiceLineItem : BaseEntity
{
    [Required]
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    /// <summary>
    /// Line item type: subscription, addon, proration, etc.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = LineItemType.Subscription;

    /// <summary>
    /// Description of the line item.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Quantity (e.g., number of seats).
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Unit price in paise.
    /// </summary>
    public int UnitPrice { get; set; }

    /// <summary>
    /// Total amount before tax in paise (quantity * unit price).
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// HSN/SAC code for this item.
    /// </summary>
    [MaxLength(10)]
    public string? HsnCode { get; set; }

    /// <summary>
    /// Period start date for subscription items.
    /// </summary>
    public DateOnly? PeriodStart { get; set; }

    /// <summary>
    /// Period end date for subscription items.
    /// </summary>
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>
    /// Plan code if this is a subscription item.
    /// </summary>
    [MaxLength(50)]
    public string? PlanCode { get; set; }

    /// <summary>
    /// Billing cycle if applicable.
    /// </summary>
    [MaxLength(10)]
    public string? BillingCycle { get; set; }

    /// <summary>
    /// Whether this is a proration adjustment.
    /// </summary>
    public bool IsProration { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Line item type constants.
/// </summary>
public static class LineItemType
{
    public const string Subscription = "subscription";
    public const string AdditionalSeats = "additional_seats";
    public const string Addon = "addon";
    public const string Proration = "proration";
    public const string Upgrade = "upgrade";
    public const string Downgrade = "downgrade";
}
