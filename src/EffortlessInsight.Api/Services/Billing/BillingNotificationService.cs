using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Notifications;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of billing notification service using the notification engine
/// </summary>
public class BillingNotificationService : IBillingNotificationService
{
    private readonly INotificationEngineService _notificationEngine;
    private readonly ILogger<BillingNotificationService> _logger;

    public BillingNotificationService(
        INotificationEngineService notificationEngine,
        ILogger<BillingNotificationService> logger)
    {
        _notificationEngine = notificationEngine;
        _logger = logger;
    }

    public async Task SendTrialStartedAsync(Guid userId, string planName, DateTime trialEndDate, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "trial_started", new Dictionary<string, object>
        {
            ["planName"] = planName,
            ["trialEndDate"] = trialEndDate.ToString("MMMM d, yyyy"),
            ["trialDays"] = (trialEndDate - DateTime.UtcNow).Days
        }, cancellationToken);
    }

    public async Task SendTrialEndingAsync(Guid userId, string planName, DateTime trialEndDate, int daysRemaining, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "trial_ending", new Dictionary<string, object>
        {
            ["planName"] = planName,
            ["trialEndDate"] = trialEndDate.ToString("MMMM d, yyyy"),
            ["daysRemaining"] = daysRemaining
        }, cancellationToken);
    }

    public async Task SendTrialEndedAsync(Guid userId, string planName, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "trial_ended", new Dictionary<string, object>
        {
            ["planName"] = planName
        }, cancellationToken);
    }

    public async Task SendSubscriptionActivatedAsync(Guid userId, string planName, decimal amount, string billingCycle, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "subscription_activated", new Dictionary<string, object>
        {
            ["planName"] = planName,
            ["amount"] = FormatAmount(amount),
            ["billingCycle"] = billingCycle
        }, cancellationToken);
    }

    public async Task SendSubscriptionCancelledAsync(Guid userId, string planName, DateTime endDate, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "subscription_cancelled", new Dictionary<string, object>
        {
            ["planName"] = planName,
            ["endDate"] = endDate.ToString("MMMM d, yyyy"),
            ["daysRemaining"] = Math.Max(0, (endDate - DateTime.UtcNow).Days)
        }, cancellationToken);
    }

    public async Task SendSubscriptionReactivatedAsync(Guid userId, string planName, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "subscription_reactivated", new Dictionary<string, object>
        {
            ["planName"] = planName
        }, cancellationToken);
    }

    public async Task SendPlanUpgradedAsync(Guid userId, string oldPlanName, string newPlanName, decimal proratedAmount, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "plan_upgraded", new Dictionary<string, object>
        {
            ["oldPlanName"] = oldPlanName,
            ["newPlanName"] = newPlanName,
            ["proratedAmount"] = FormatAmount(proratedAmount)
        }, cancellationToken);
    }

    public async Task SendPlanDowngradedAsync(Guid userId, string oldPlanName, string newPlanName, DateTime effectiveDate, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "plan_downgraded", new Dictionary<string, object>
        {
            ["oldPlanName"] = oldPlanName,
            ["newPlanName"] = newPlanName,
            ["effectiveDate"] = effectiveDate.ToString("MMMM d, yyyy")
        }, cancellationToken);
    }

    public async Task SendPaymentSuccessAsync(Guid userId, decimal amount, string invoiceNumber, string planName, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "payment_success", new Dictionary<string, object>
        {
            ["amount"] = FormatAmount(amount),
            ["invoiceNumber"] = invoiceNumber,
            ["planName"] = planName
        }, cancellationToken);
    }

    public async Task SendPaymentFailedAsync(Guid userId, decimal amount, string planName, string reason, int retryCount, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "payment_failed", new Dictionary<string, object>
        {
            ["amount"] = FormatAmount(amount),
            ["planName"] = planName,
            ["reason"] = reason,
            ["retryCount"] = retryCount,
            ["maxRetries"] = 3
        }, cancellationToken);
    }

    public async Task SendPaymentRetryAsync(Guid userId, decimal amount, string planName, DateTime nextRetryDate, int attemptNumber, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "payment_retry", new Dictionary<string, object>
        {
            ["amount"] = FormatAmount(amount),
            ["planName"] = planName,
            ["nextRetryDate"] = nextRetryDate.ToString("MMMM d, yyyy"),
            ["attemptNumber"] = attemptNumber,
            ["maxAttempts"] = 3
        }, cancellationToken);
    }

    public async Task SendInvoiceReadyAsync(Guid userId, string invoiceNumber, decimal amount, string downloadUrl, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "invoice_ready", new Dictionary<string, object>
        {
            ["invoiceNumber"] = invoiceNumber,
            ["amount"] = FormatAmount(amount),
            ["downloadUrl"] = downloadUrl
        }, cancellationToken);
    }

    public async Task SendUsageWarning80Async(Guid userId, string resourceType, int currentUsage, int limit, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "usage_warning_80", new Dictionary<string, object>
        {
            ["resourceType"] = resourceType,
            ["currentUsage"] = currentUsage,
            ["limit"] = limit,
            ["percentage"] = 80,
            ["remaining"] = limit - currentUsage
        }, cancellationToken);
    }

    public async Task SendUsageWarning90Async(Guid userId, string resourceType, int currentUsage, int limit, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "usage_warning_90", new Dictionary<string, object>
        {
            ["resourceType"] = resourceType,
            ["currentUsage"] = currentUsage,
            ["limit"] = limit,
            ["percentage"] = 90,
            ["remaining"] = limit - currentUsage
        }, cancellationToken);
    }

    public async Task SendUsageLimitReachedAsync(Guid userId, string resourceType, int limit, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "usage_limit_reached", new Dictionary<string, object>
        {
            ["resourceType"] = resourceType,
            ["limit"] = limit
        }, cancellationToken);
    }

    public async Task SendRenewalReminderAsync(Guid userId, string planName, decimal amount, DateTime renewalDate, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "renewal_reminder", new Dictionary<string, object>
        {
            ["planName"] = planName,
            ["amount"] = FormatAmount(amount),
            ["renewalDate"] = renewalDate.ToString("MMMM d, yyyy"),
            ["daysUntilRenewal"] = Math.Max(0, (renewalDate - DateTime.UtcNow).Days)
        }, cancellationToken);
    }

    public async Task SendSeatsAddedAsync(Guid userId, int seatsAdded, int totalSeats, decimal additionalCost, CancellationToken cancellationToken = default)
    {
        await SendNotificationAsync(userId, "seats_added", new Dictionary<string, object>
        {
            ["seatsAdded"] = seatsAdded,
            ["totalSeats"] = totalSeats,
            ["additionalCost"] = FormatAmount(additionalCost)
        }, cancellationToken);
    }

    private async Task SendNotificationAsync(Guid userId, string type, Dictionary<string, object> data, CancellationToken cancellationToken)
    {
        try
        {
            var request = new SendNotificationRequest(
                UserId: userId,
                Type: type,
                Data: data,
                ScheduledFor: null,
                OverridePreferences: false
            );

            var response = await _notificationEngine.SendAsync(request, cancellationToken);

            _logger.LogInformation(
                "Sent billing notification {Type} to user {UserId}. NotificationId: {NotificationId}",
                type, userId, response.NotificationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send billing notification {Type} to user {UserId}",
                type, userId);
        }
    }

    private static string FormatAmount(decimal amount)
    {
        return $"₹{amount:N2}";
    }
}
