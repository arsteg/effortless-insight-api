namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for Razorpay integration.
/// </summary>
public class RazorpayOptions
{
    public const string SectionName = "Razorpay";

    /// <summary>
    /// Razorpay API Key ID.
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Razorpay API Key Secret.
    /// </summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>
    /// Webhook secret for signature verification.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Whether to use test mode.
    /// </summary>
    public bool TestMode { get; set; } = true;
}

/// <summary>
/// Configuration options for billing.
/// </summary>
public class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>
    /// Company name for invoices.
    /// </summary>
    public string CompanyName { get; set; } = "EffortlessInsight Technologies Pvt Ltd";

    /// <summary>
    /// Company GSTIN for invoices.
    /// </summary>
    public string CompanyGstin { get; set; } = string.Empty;

    /// <summary>
    /// Company address for invoices.
    /// </summary>
    public string CompanyAddress { get; set; } = string.Empty;

    /// <summary>
    /// Company state for GST determination.
    /// </summary>
    public string CompanyState { get; set; } = string.Empty;

    /// <summary>
    /// Company state code for GST determination.
    /// </summary>
    public string CompanyStateCode { get; set; } = string.Empty;

    /// <summary>
    /// HSN/SAC code for software services.
    /// </summary>
    public string HsnCode { get; set; } = "998314";

    /// <summary>
    /// Invoice number prefix.
    /// </summary>
    public string InvoicePrefix { get; set; } = "INV";

    /// <summary>
    /// GST rate percentage.
    /// </summary>
    public decimal GstRate { get; set; } = 18.00m;

    /// <summary>
    /// Default trial days for new subscriptions.
    /// </summary>
    public int DefaultTrialDays { get; set; } = 14;

    /// <summary>
    /// Grace period days after payment failure.
    /// </summary>
    public int GracePeriodDays { get; set; } = 7;

    /// <summary>
    /// Maximum payment retry attempts.
    /// </summary>
    public int MaxPaymentRetries { get; set; } = 3;

    /// <summary>
    /// Days until subscription can be reactivated after cancellation.
    /// </summary>
    public int ReactivationWindowDays { get; set; } = 30;
}
