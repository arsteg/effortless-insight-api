using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for Razorpay payment integration.
/// </summary>
public interface IRazorpayService
{
    /// <summary>
    /// Creates a Razorpay order for payment.
    /// </summary>
    Task<RazorpayOrderDto> CreateOrderAsync(CreateOrderRequest request);

    /// <summary>
    /// Verifies the payment signature from Razorpay checkout.
    /// </summary>
    bool VerifyPaymentSignature(string orderId, string paymentId, string signature);

    /// <summary>
    /// Captures an authorized payment.
    /// </summary>
    Task<PaymentResult> CapturePaymentAsync(string paymentId, int? amount = null);

    /// <summary>
    /// Gets payment details by ID.
    /// </summary>
    Task<PaymentResult> GetPaymentAsync(string paymentId);

    /// <summary>
    /// Creates a Razorpay subscription for recurring billing.
    /// </summary>
    Task<RazorpaySubscriptionResult> CreateSubscriptionAsync(CreateRazorpaySubscriptionRequest request);

    /// <summary>
    /// Cancels a Razorpay subscription.
    /// </summary>
    Task CancelSubscriptionAsync(string subscriptionId, bool cancelAtCycleEnd = true);

    /// <summary>
    /// Creates or gets a Razorpay customer.
    /// </summary>
    Task<RazorpayCustomerResult> CreateOrGetCustomerAsync(string name, string email, string? phone);

    /// <summary>
    /// Initiates a refund for a payment.
    /// </summary>
    Task<RefundResult> CreateRefundAsync(string paymentId, int? amount = null, string? reason = null);

    /// <summary>
    /// Verifies webhook signature.
    /// </summary>
    bool VerifyWebhookSignature(string payload, string signature);

    /// <summary>
    /// Gets the public key for client-side checkout.
    /// </summary>
    string GetPublicKey();
}

/// <summary>
/// Request for creating a Razorpay order.
/// </summary>
public record CreateOrderRequest
{
    public int AmountInPaise { get; init; }
    public string Currency { get; init; } = "INR";
    public string Receipt { get; init; } = string.Empty;
    public Guid OrganizationId { get; init; }
    public string PlanCode { get; init; } = string.Empty;
    public Guid? SubscriptionId { get; init; }
}

/// <summary>
/// Result of a payment operation.
/// </summary>
public record PaymentResult
{
    public string PaymentId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Amount { get; init; }
    public string Currency { get; init; } = "INR";
    public string Method { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Contact { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorDescription { get; init; }
    public string? TokenId { get; init; }
    public string? Vpa { get; init; }
    public CardDetails? Card { get; init; }
}

/// <summary>
/// Card details from a payment.
/// </summary>
public record CardDetails
{
    public string? Last4 { get; init; }
    public string? Network { get; init; }
    public int? ExpiryMonth { get; init; }
    public int? ExpiryYear { get; init; }
    public string? Name { get; init; }
    public string? Type { get; init; }
    public string? Issuer { get; init; }
}

/// <summary>
/// Request for creating a Razorpay subscription.
/// </summary>
public record CreateRazorpaySubscriptionRequest
{
    public string RazorpayPlanId { get; init; } = string.Empty;
    public string RazorpayCustomerId { get; init; } = string.Empty;
    public Guid OrganizationId { get; init; }
    public string BillingCycle { get; init; } = "monthly";
    public int Quantity { get; init; } = 1;
}

/// <summary>
/// Result of a Razorpay subscription creation.
/// </summary>
public record RazorpaySubscriptionResult
{
    public string SubscriptionId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public string ShortUrl { get; init; } = string.Empty;
}

/// <summary>
/// Result of a Razorpay customer operation.
/// </summary>
public record RazorpayCustomerResult
{
    public string CustomerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Contact { get; init; }
}

/// <summary>
/// Result of a refund operation.
/// </summary>
public record RefundResult
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Amount { get; init; }
    public string PaymentId { get; init; } = string.Empty;
}
