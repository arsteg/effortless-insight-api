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
    Task<BillingSubscription> StartTrialAsync(
        Guid organizationId,
        string planCode,
        int trialDays);

    /// <summary>
    /// Processes subscription renewal.
    /// </summary>
    Task ProcessRenewalAsync(Guid subscriptionId);

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
}
