using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for managing subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Gets the current subscription for an organization.
    /// </summary>
    Task<CurrentSubscriptionResponse?> GetCurrentSubscriptionAsync(Guid organizationId);

    /// <summary>
    /// Gets the billing subscription entity for an organization.
    /// </summary>
    Task<BillingSubscription?> GetSubscriptionEntityAsync(Guid organizationId);

    /// <summary>
    /// Creates a new subscription (initiates checkout).
    /// </summary>
    Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
        Guid organizationId,
        Guid userId,
        CreateSubscriptionRequest request);

    /// <summary>
    /// Verifies a payment and activates the subscription.
    /// </summary>
    Task<VerifyPaymentResponse> VerifyPaymentAsync(
        Guid organizationId,
        Guid userId,
        VerifyPaymentRequest request);

    /// <summary>
    /// Changes the subscription plan (upgrade/downgrade).
    /// </summary>
    Task<ChangePlanResponse> ChangePlanAsync(
        Guid organizationId,
        Guid userId,
        ChangePlanRequest request);

    /// <summary>
    /// Cancels the subscription.
    /// </summary>
    Task<CancelSubscriptionResponse> CancelSubscriptionAsync(
        Guid organizationId,
        Guid userId,
        CancelSubscriptionRequest request);

    /// <summary>
    /// Adds additional seats to the subscription.
    /// </summary>
    Task<AddSeatsResponse> AddSeatsAsync(
        Guid organizationId,
        Guid userId,
        AddSeatsRequest request);

    /// <summary>
    /// Reactivates a cancelled subscription (within grace period).
    /// </summary>
    Task<SubscriptionDto> ReactivateSubscriptionAsync(Guid organizationId, Guid userId);

    /// <summary>
    /// Starts a trial for an organization.
    /// </summary>
    Task<SubscriptionDto> StartTrialAsync(
        Guid organizationId,
        string planCode,
        string billingCycle);

    /// <summary>
    /// Processes subscription renewal.
    /// </summary>
    Task ProcessRenewalAsync(Guid subscriptionId);

    /// <summary>
    /// Processes subscription renewal with webhook data from Razorpay.
    /// </summary>
    Task ProcessRenewalAsync(Guid subscriptionId, RenewalWebhookData webhookData);

    /// <summary>
    /// Handles payment failure for a subscription.
    /// </summary>
    Task HandlePaymentFailureAsync(Guid subscriptionId, string failureReason);

    /// <summary>
    /// Expires a trial subscription.
    /// </summary>
    Task ExpireTrialAsync(Guid subscriptionId);

    /// <summary>
    /// Applies scheduled plan changes.
    /// </summary>
    Task ApplyScheduledChangesAsync(Guid subscriptionId);

    /// <summary>
    /// Gets subscription by Razorpay subscription ID.
    /// </summary>
    Task<BillingSubscription?> GetByRazorpayIdAsync(string razorpaySubscriptionId);

    /// <summary>
    /// Pauses a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="reason">The reason for pausing.</param>
    /// <param name="resumeAt">Optional scheduled resume date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The paused subscription.</returns>
    Task<BillingSubscription> PauseSubscriptionAsync(
        Guid subscriptionId,
        string reason,
        DateTime? resumeAt,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resumed subscription.</returns>
    Task<BillingSubscription> ResumeSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken ct = default);

    /// <summary>
    /// Manually retries the last failed payment for a subscription.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <returns>Payment retry result.</returns>
    Task<PaymentRetryResponse> RetryPaymentAsync(Guid organizationId);

    /// <summary>
    /// Creates a refund for a subscription payment.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="amount">Optional partial refund amount. If null, refunds the full payment.</param>
    /// <param name="reason">The reason for the refund.</param>
    /// <returns>Refund details.</returns>
    Task<RefundResponse> CreateRefundAsync(Guid subscriptionId, decimal? amount, string reason);

    /// <summary>
    /// Expires subscriptions that are past their grace period.
    /// Called by Hangfire recurring job to clean up past_due subscriptions.
    /// Fixes Issue #10: Grace Period Expiration Not Automatically Handled
    /// </summary>
    Task ExpireGracePeriodSubscriptionsAsync();
}

/// <summary>
/// Response for payment retry operation.
/// </summary>
/// <param name="Success">Whether the payment retry was successful.</param>
/// <param name="Message">Human-readable message about the result.</param>
/// <param name="NewStatus">The new subscription status.</param>
/// <param name="NextBillingDate">The next billing date if payment succeeded.</param>
public record PaymentRetryResponse(
    bool Success,
    string Message,
    string NewStatus,
    DateTime? NextBillingDate
);

/// <summary>
/// Response for refund operation.
/// </summary>
/// <param name="RefundId">The Razorpay refund ID.</param>
/// <param name="PaymentId">The original payment ID.</param>
/// <param name="Amount">The refund amount in the currency's standard unit.</param>
/// <param name="Currency">The currency code.</param>
/// <param name="Status">The refund status.</param>
/// <param name="Reason">The reason for the refund.</param>
/// <param name="CreatedAt">When the refund was created.</param>
public record RefundResponse(
    string RefundId,
    string PaymentId,
    decimal Amount,
    string Currency,
    string Status,
    string Reason,
    DateTime CreatedAt
);

/// <summary>
/// Data from Razorpay webhook for subscription renewal.
/// </summary>
public record RenewalWebhookData
{
    /// <summary>
    /// The Razorpay payment ID for this charge.
    /// </summary>
    public string? RazorpayPaymentId { get; init; }

    /// <summary>
    /// Amount charged in paise.
    /// </summary>
    public int? AmountInPaise { get; init; }

    /// <summary>
    /// Currency code (e.g., INR).
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Unix timestamp of when the charge occurred.
    /// </summary>
    public long? ChargeAt { get; init; }

    /// <summary>
    /// Unix timestamp for current period start.
    /// </summary>
    public long? CurrentPeriodStart { get; init; }

    /// <summary>
    /// Unix timestamp for current period end.
    /// </summary>
    public long? CurrentPeriodEnd { get; init; }
}
