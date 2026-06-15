namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for sending billing-related notifications
/// </summary>
public interface IBillingNotificationService
{
    // Trial notifications
    Task SendTrialStartedAsync(Guid userId, string planName, DateTime trialEndDate, CancellationToken cancellationToken = default);
    Task SendTrialEndingAsync(Guid userId, string planName, DateTime trialEndDate, int daysRemaining, CancellationToken cancellationToken = default);
    Task SendTrialEndedAsync(Guid userId, string planName, CancellationToken cancellationToken = default);

    // Subscription notifications
    Task SendSubscriptionActivatedAsync(Guid userId, string planName, decimal amount, string billingCycle, CancellationToken cancellationToken = default);
    Task SendSubscriptionCancelledAsync(Guid userId, string planName, DateTime endDate, CancellationToken cancellationToken = default);
    Task SendSubscriptionReactivatedAsync(Guid userId, string planName, CancellationToken cancellationToken = default);

    // Plan change notifications
    Task SendPlanUpgradedAsync(Guid userId, string oldPlanName, string newPlanName, decimal proratedAmount, CancellationToken cancellationToken = default);
    Task SendPlanDowngradedAsync(Guid userId, string oldPlanName, string newPlanName, DateTime effectiveDate, CancellationToken cancellationToken = default);

    // Payment notifications
    Task SendPaymentSuccessAsync(Guid userId, decimal amount, string invoiceNumber, string planName, CancellationToken cancellationToken = default);
    Task SendPaymentFailedAsync(Guid userId, decimal amount, string planName, string reason, int retryCount, CancellationToken cancellationToken = default);
    Task SendPaymentRetryAsync(Guid userId, decimal amount, string planName, DateTime nextRetryDate, int attemptNumber, CancellationToken cancellationToken = default);

    // Invoice notifications
    Task SendInvoiceReadyAsync(Guid userId, string invoiceNumber, decimal amount, string downloadUrl, CancellationToken cancellationToken = default);

    // Usage notifications
    Task SendUsageWarning80Async(Guid userId, string resourceType, int currentUsage, int limit, CancellationToken cancellationToken = default);
    Task SendUsageWarning90Async(Guid userId, string resourceType, int currentUsage, int limit, CancellationToken cancellationToken = default);
    Task SendUsageLimitReachedAsync(Guid userId, string resourceType, int limit, CancellationToken cancellationToken = default);

    // Renewal notifications
    Task SendRenewalReminderAsync(Guid userId, string planName, decimal amount, DateTime renewalDate, CancellationToken cancellationToken = default);

    // Seats notifications
    Task SendSeatsAddedAsync(Guid userId, int seatsAdded, int totalSeats, decimal additionalCost, CancellationToken cancellationToken = default);
}
