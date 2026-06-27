using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background jobs for billing operations.
/// </summary>
public class BillingJobs
{
    private const int MaxWebhookRetries = 5;

    private readonly ApplicationDbContext _dbContext;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IUsageService _usageService;
    private readonly IBillingNotificationService _billingNotificationService;
    private readonly IRazorpayService _razorpayService;
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<BillingJobs> _logger;

    public BillingJobs(
        ApplicationDbContext dbContext,
        ISubscriptionService subscriptionService,
        IUsageService usageService,
        IBillingNotificationService billingNotificationService,
        IRazorpayService razorpayService,
        IInvoiceService invoiceService,
        ILogger<BillingJobs> logger)
    {
        _dbContext = dbContext;
        _subscriptionService = subscriptionService;
        _usageService = usageService;
        _billingNotificationService = billingNotificationService;
        _razorpayService = razorpayService;
        _invoiceService = invoiceService;
        _logger = logger;
    }

    /// <summary>
    /// Process expired trials - runs daily at midnight.
    /// </summary>
    public async Task ProcessTrialExpirationsAsync()
    {
        _logger.LogInformation("Processing trial expirations...");

        var now = DateTime.UtcNow;
        var expiredTrials = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Trialing &&
                       s.TrialEnd.HasValue &&
                       s.TrialEnd.Value <= now)
            .ToListAsync();

        foreach (var subscription in expiredTrials)
        {
            try
            {
                await _subscriptionService.ExpireTrialAsync(subscription.Id);
                _logger.LogInformation("Expired trial for subscription {SubscriptionId}", subscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire trial for subscription {SubscriptionId}", subscription.Id);
            }
        }

        _logger.LogInformation("Processed {Count} trial expirations", expiredTrials.Count);
    }

    /// <summary>
    /// Process subscription renewals - runs daily at 6 AM.
    /// Skips paused subscriptions - they will be handled when resumed.
    /// </summary>
    public async Task ProcessSubscriptionRenewalsAsync()
    {
        _logger.LogInformation("Processing subscription renewals...");

        var now = DateTime.UtcNow;

        // Skip paused subscriptions - they should not be renewed while paused
        var subscriptionsToRenew = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Active &&
                       s.CurrentPeriodEnd <= now &&
                       !s.CancelAtPeriodEnd)
            .ToListAsync();

        foreach (var subscription in subscriptionsToRenew)
        {
            try
            {
                await _subscriptionService.ProcessRenewalAsync(subscription.Id);
                _logger.LogInformation("Processed renewal for subscription {SubscriptionId}", subscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process renewal for subscription {SubscriptionId}", subscription.Id);
            }
        }

        // Process scheduled cancellations
        var cancellations = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Active &&
                       s.CancelAtPeriodEnd &&
                       s.CurrentPeriodEnd <= now)
            .ToListAsync();

        foreach (var subscription in cancellations)
        {
            try
            {
                subscription.Status = SubscriptionStatus.Cancelled;
                subscription.EndedAt = now;

                var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
                if (org != null)
                {
                    org.SubscriptionStatus = "cancelled";
                }

                _logger.LogInformation("Cancelled subscription {SubscriptionId} at period end", subscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel subscription {SubscriptionId}", subscription.Id);
            }
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Processed {Renewals} renewals and {Cancellations} cancellations",
            subscriptionsToRenew.Count, cancellations.Count);
    }

    /// <summary>
    /// Apply scheduled plan changes - runs daily.
    /// </summary>
    public async Task ApplyScheduledPlanChangesAsync()
    {
        _logger.LogInformation("Applying scheduled plan changes...");

        var now = DateTime.UtcNow;
        var subscriptionsWithChanges = await _dbContext.BillingSubscriptions
            .Where(s => s.ScheduledPlanCode != null &&
                       s.ScheduledChangeDate.HasValue &&
                       s.ScheduledChangeDate.Value <= now)
            .ToListAsync();

        foreach (var subscription in subscriptionsWithChanges)
        {
            try
            {
                await _subscriptionService.ApplyScheduledChangesAsync(subscription.Id);
                _logger.LogInformation(
                    "Applied scheduled plan change for subscription {SubscriptionId}",
                    subscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply scheduled plan change for subscription {SubscriptionId}",
                    subscription.Id);
            }
        }

        _logger.LogInformation("Applied {Count} scheduled plan changes", subscriptionsWithChanges.Count);
    }

    /// <summary>
    /// Process grace period expirations - runs daily.
    /// </summary>
    public async Task ProcessGracePeriodExpirationsAsync()
    {
        _logger.LogInformation("Processing grace period expirations...");

        var now = DateTime.UtcNow;
        var expiredGracePeriods = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == SubscriptionStatus.PastDue &&
                       s.GracePeriodEndAt.HasValue &&
                       s.GracePeriodEndAt.Value <= now)
            .ToListAsync();

        foreach (var subscription in expiredGracePeriods)
        {
            subscription.Status = SubscriptionStatus.Expired;
            subscription.EndedAt = now;

            var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
            if (org != null)
            {
                org.SubscriptionStatus = "expired";
            }

            _logger.LogInformation(
                "Expired subscription {SubscriptionId} after grace period",
                subscription.Id);
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Processed {Count} grace period expirations", expiredGracePeriods.Count);
    }

    /// <summary>
    /// Reset monthly usage - runs on the 1st of each month at midnight.
    /// </summary>
    public async Task ResetMonthlyUsageAsync()
    {
        _logger.LogInformation("Resetting monthly usage...");

        var now = DateTime.UtcNow;
        var periodStart = new DateOnly(now.Year, now.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var activeSubscriptions = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Active ||
                       s.Status == SubscriptionStatus.Trialing)
            .ToListAsync();

        foreach (var subscription in activeSubscriptions)
        {
            try
            {
                await _usageService.ResetUsageForPeriodAsync(
                    subscription.OrganizationId, periodStart, periodEnd);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to reset usage for organization {OrganizationId}",
                    subscription.OrganizationId);
            }
        }

        _logger.LogInformation("Reset usage for {Count} subscriptions", activeSubscriptions.Count);
    }

    /// <summary>
    /// Send usage warnings - runs every 6 hours.
    /// </summary>
    public async Task SendUsageWarningsAsync()
    {
        _logger.LogInformation("Checking usage warnings...");

        var activeSubscriptions = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active)
            .ToListAsync();

        var warningsSent = 0;

        foreach (var subscription in activeSubscriptions)
        {
            try
            {
                // Get organization owner for notifications
                var owner = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId &&
                                            u.Role == "owner");

                if (owner == null) continue;

                // Get current usage record
                var usageRecord = await _usageService.GetCurrentUsageAsync(subscription.OrganizationId);
                if (usageRecord == null) continue;

                // Get plan limits
                var noticeLimit = subscription.Plan?.Limits?.NoticesPerMonth ?? 0;
                var storageLimit = subscription.Plan?.Limits?.StorageGb ?? 0;

                // Check notice usage (skip unlimited plans where limit is -1)
                if (noticeLimit > 0)
                {
                    var noticePercentage = (int)((usageRecord.NoticesCount * 100) / noticeLimit);

                    if (noticePercentage >= 100)
                    {
                        await _billingNotificationService.SendUsageLimitReachedAsync(
                            owner.Id, "notices", noticeLimit);
                        warningsSent++;
                    }
                    else if (noticePercentage >= 90)
                    {
                        await _billingNotificationService.SendUsageWarning90Async(
                            owner.Id, "notices", usageRecord.NoticesCount, noticeLimit);
                        warningsSent++;
                    }
                    else if (noticePercentage >= 80)
                    {
                        await _billingNotificationService.SendUsageWarning80Async(
                            owner.Id, "notices", usageRecord.NoticesCount, noticeLimit);
                        warningsSent++;
                    }
                }

                // Check storage usage (skip unlimited plans where limit is -1)
                if (storageLimit > 0)
                {
                    var storageLimitBytes = storageLimit * 1024L * 1024L * 1024L;
                    var storagePercentage = (int)((usageRecord.StorageBytes * 100) / storageLimitBytes);
                    var storageUsedGb = (int)(usageRecord.StorageBytes / (1024 * 1024 * 1024));

                    if (storagePercentage >= 100)
                    {
                        await _billingNotificationService.SendUsageLimitReachedAsync(
                            owner.Id, "storage", storageLimit);
                        warningsSent++;
                    }
                    else if (storagePercentage >= 90)
                    {
                        await _billingNotificationService.SendUsageWarning90Async(
                            owner.Id, "storage", storageUsedGb, storageLimit);
                        warningsSent++;
                    }
                    else if (storagePercentage >= 80)
                    {
                        await _billingNotificationService.SendUsageWarning80Async(
                            owner.Id, "storage", storageUsedGb, storageLimit);
                        warningsSent++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to check usage for organization {OrganizationId}",
                    subscription.OrganizationId);
            }
        }

        _logger.LogInformation("Sent {Count} usage warnings", warningsSent);
    }

    /// <summary>
    /// Clean up old webhook events - runs weekly.
    /// </summary>
    public async Task CleanupWebhookEventsAsync()
    {
        _logger.LogInformation("Cleaning up old webhook events...");

        var cutoffDate = DateTime.UtcNow.AddDays(-90);

        // Use standard removal approach that works with all providers including InMemory
        var eventsToDelete = await _dbContext.WebhookEvents
            .Where(e => e.CreatedAt < cutoffDate &&
                       (e.Status == WebhookEventStatus.Processed ||
                        e.Status == WebhookEventStatus.Skipped))
            .ToListAsync();

        _dbContext.WebhookEvents.RemoveRange(eventsToDelete);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted {Count} old webhook events", eventsToDelete.Count);
    }

    /// <summary>
    /// Retry failed webhook events - runs every hour.
    /// </summary>
    public async Task RetryFailedWebhookEventsAsync()
    {
        _logger.LogInformation("Retrying failed webhook events...");

        var failedEvents = await _dbContext.WebhookEvents
            .Where(e => e.Status == WebhookEventStatus.Failed &&
                       e.AttemptCount < MaxWebhookRetries)
            .OrderBy(e => e.CreatedAt)
            .Take(10)
            .ToListAsync();

        var processedCount = 0;
        var failedCount = 0;
        var deadLetterCount = 0;

        foreach (var webhookEvent in failedEvents)
        {
            webhookEvent.AttemptCount++;
            webhookEvent.LastAttemptAt = DateTime.UtcNow;
            webhookEvent.Status = WebhookEventStatus.Processing;
            webhookEvent.ProcessingStartedAt = DateTime.UtcNow;

            try
            {
                // Deserialize the payload
                var payload = JsonSerializer.Deserialize<RazorpayWebhookPayload>(
                    webhookEvent.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload?.Event == null)
                {
                    webhookEvent.Status = WebhookEventStatus.Failed;
                    webhookEvent.ErrorMessage = "Invalid payload: missing event type";
                    failedCount++;
                    continue;
                }

                // Process based on event type
                await ProcessWebhookEventAsync(payload, webhookEvent);

                webhookEvent.Status = WebhookEventStatus.Processed;
                webhookEvent.ProcessedAt = DateTime.UtcNow;
                webhookEvent.ErrorMessage = null;
                processedCount++;

                _logger.LogInformation(
                    "Webhook event {EventId} processed successfully on retry attempt {Attempt}",
                    webhookEvent.EventId, webhookEvent.AttemptCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process webhook event {EventId} on attempt {Attempt}",
                    webhookEvent.EventId, webhookEvent.AttemptCount);

                webhookEvent.ErrorMessage = ex.Message;

                // Move to dead letter after max retries
                if (webhookEvent.AttemptCount >= MaxWebhookRetries)
                {
                    webhookEvent.Status = WebhookEventStatus.DeadLetter;
                    deadLetterCount++;

                    _logger.LogWarning(
                        "Webhook event {EventId} moved to dead letter after {MaxRetries} attempts",
                        webhookEvent.EventId, MaxWebhookRetries);
                }
                else
                {
                    webhookEvent.Status = WebhookEventStatus.Failed;
                    failedCount++;
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Processed {Total} webhook retries: {Processed} succeeded, {Failed} failed, {DeadLetter} moved to dead letter",
            failedEvents.Count, processedCount, failedCount, deadLetterCount);
    }

    /// <summary>
    /// Process a webhook event based on its type.
    /// </summary>
    private async Task ProcessWebhookEventAsync(RazorpayWebhookPayload payload, WebhookEvent webhookEvent)
    {
        switch (payload.Event)
        {
            case "payment.captured":
                await HandlePaymentCapturedAsync(payload);
                break;

            case "payment.failed":
                await HandlePaymentFailedAsync(payload);
                break;

            case "subscription.activated":
                await HandleSubscriptionActivatedAsync(payload);
                break;

            case "subscription.charged":
                await HandleSubscriptionChargedAsync(payload);
                break;

            case "subscription.cancelled":
                await HandleSubscriptionCancelledAsync(payload);
                break;

            case "subscription.halted":
                await HandleSubscriptionHaltedAsync(payload);
                break;

            case "refund.created":
                await HandleRefundCreatedAsync(payload);
                break;

            default:
                _logger.LogInformation("Unhandled Razorpay event: {Event}", payload.Event);
                webhookEvent.Status = WebhookEventStatus.Skipped;
                break;
        }
    }

    private async Task HandlePaymentCapturedAsync(RazorpayWebhookPayload payload)
    {
        var paymentEntity = payload.Payload?.Payment?.Entity;
        if (paymentEntity == null) return;

        var paymentId = paymentEntity.Id;
        var orderId = paymentEntity.OrderId;

        _logger.LogInformation(
            "Processing payment.captured: {PaymentId}, Order: {OrderId}",
            paymentId, orderId);

        var payment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.RazorpayOrderId == orderId);

        if (payment != null)
        {
            payment.Status = PaymentStatus.Captured;
            payment.RazorpayPaymentId = paymentId;
            payment.CapturedAt = DateTime.UtcNow;
            payment.PaymentMethod = paymentEntity.Method ?? "unknown";

            if (payment.InvoiceId.HasValue)
            {
                await _invoiceService.MarkAsPaidAsync(payment.InvoiceId.Value, paymentId!);
            }

            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task HandlePaymentFailedAsync(RazorpayWebhookPayload payload)
    {
        var paymentEntity = payload.Payload?.Payment?.Entity;
        if (paymentEntity == null) return;

        var orderId = paymentEntity.OrderId;
        var errorDescription = paymentEntity.ErrorDescription;

        _logger.LogWarning(
            "Processing payment.failed for order {OrderId}: {Error}",
            orderId, errorDescription);

        var payment = await _dbContext.Payments
            .Include(p => p.Subscription)
            .FirstOrDefaultAsync(p => p.RazorpayOrderId == orderId);

        if (payment != null)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = paymentEntity.ErrorCode;
            payment.FailureReason = errorDescription;

            if (payment.SubscriptionId.HasValue)
            {
                await _subscriptionService.HandlePaymentFailureAsync(
                    payment.SubscriptionId.Value,
                    errorDescription ?? "Payment failed");
            }

            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task HandleSubscriptionActivatedAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation("Processing subscription.activated: {SubscriptionId}", razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            subscription.Status = SubscriptionStatus.Active;
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task HandleSubscriptionChargedAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;
        var paymentEntity = payload.Payload?.Payment?.Entity;

        _logger.LogInformation(
            "Processing subscription.charged: {SubscriptionId}",
            razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            var webhookData = new RenewalWebhookData
            {
                RazorpayPaymentId = paymentEntity?.Id,
                AmountInPaise = subscriptionEntity.Amount ?? paymentEntity?.Amount,
                Currency = subscriptionEntity.Currency ?? paymentEntity?.Currency ?? "INR",
                ChargeAt = subscriptionEntity.ChargeAt ?? paymentEntity?.CreatedAt,
                CurrentPeriodStart = subscriptionEntity.StartAt,
                CurrentPeriodEnd = subscriptionEntity.EndAt
            };

            await _subscriptionService.ProcessRenewalAsync(subscription.Id, webhookData);
        }
    }

    private async Task HandleSubscriptionCancelledAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation("Processing subscription.cancelled: {SubscriptionId}", razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndedAt = DateTime.UtcNow;

            var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
            if (org != null)
            {
                org.SubscriptionStatus = "cancelled";
            }

            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task HandleSubscriptionHaltedAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogWarning("Processing subscription.halted: {SubscriptionId}", razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            subscription.Status = SubscriptionStatus.PastDue;
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task HandleRefundCreatedAsync(RazorpayWebhookPayload payload)
    {
        var refundEntity = payload.Payload?.Refund?.Entity;
        if (refundEntity == null) return;

        var refundId = refundEntity.Id;
        var paymentId = refundEntity.OrderId;

        _logger.LogInformation("Processing refund.created: {RefundId}", refundId);

        var payment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.RazorpayPaymentId == paymentId);

        if (payment != null)
        {
            payment.RefundId = refundId;
            payment.RefundAmount = refundEntity.Amount;
            payment.RefundedAt = DateTime.UtcNow;
            payment.Status = payment.RefundAmount >= payment.Amount
                ? PaymentStatus.Refunded
                : PaymentStatus.PartialRefund;

            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Send trial ending notifications - runs daily at 9 AM.
    /// Sends notifications 3 days and 1 day before trial ends.
    /// </summary>
    public async Task SendTrialEndingNotificationsAsync()
    {
        _logger.LogInformation("Sending trial ending notifications...");

        var now = DateTime.UtcNow;
        var threeDaysFromNow = now.AddDays(3);
        var oneDayFromNow = now.AddDays(1);

        // Find subscriptions with trials ending in 3 days
        var trialsEndingInThreeDays = await _dbContext.BillingSubscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Trialing &&
                       s.TrialEnd.HasValue &&
                       s.TrialEnd.Value.Date == threeDaysFromNow.Date)
            .ToListAsync();

        // Find subscriptions with trials ending in 1 day
        var trialsEndingInOneDay = await _dbContext.BillingSubscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Trialing &&
                       s.TrialEnd.HasValue &&
                       s.TrialEnd.Value.Date == oneDayFromNow.Date)
            .ToListAsync();

        var notificationsSent = 0;

        foreach (var subscription in trialsEndingInThreeDays)
        {
            try
            {
                var owner = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId &&
                                            u.Role == "owner");

                if (owner != null && subscription.TrialEnd.HasValue)
                {
                    await _billingNotificationService.SendTrialEndingAsync(
                        owner.Id,
                        subscription.Plan?.DisplayName ?? "your plan",
                        subscription.TrialEnd.Value,
                        daysRemaining: 3);
                    notificationsSent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send trial ending notification for subscription {SubscriptionId}",
                    subscription.Id);
            }
        }

        foreach (var subscription in trialsEndingInOneDay)
        {
            try
            {
                var owner = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId &&
                                            u.Role == "owner");

                if (owner != null && subscription.TrialEnd.HasValue)
                {
                    await _billingNotificationService.SendTrialEndingAsync(
                        owner.Id,
                        subscription.Plan?.DisplayName ?? "your plan",
                        subscription.TrialEnd.Value,
                        daysRemaining: 1);
                    notificationsSent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send trial ending notification for subscription {SubscriptionId}",
                    subscription.Id);
            }
        }

        _logger.LogInformation(
            "Sent {Count} trial ending notifications (3 days: {ThreeDays}, 1 day: {OneDay})",
            notificationsSent, trialsEndingInThreeDays.Count, trialsEndingInOneDay.Count);
    }

    /// <summary>
    /// Send renewal reminder notifications - runs daily at 9 AM.
    /// Sends reminders 7 days and 3 days before renewal.
    /// </summary>
    public async Task SendRenewalRemindersAsync()
    {
        _logger.LogInformation("Sending renewal reminders...");

        var now = DateTime.UtcNow;
        var sevenDaysFromNow = now.AddDays(7);
        var threeDaysFromNow = now.AddDays(3);

        // Find subscriptions renewing in 7 days
        var renewingInSevenDays = await _dbContext.BillingSubscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active &&
                       !s.CancelAtPeriodEnd &&
                       s.CurrentPeriodEnd.Date == sevenDaysFromNow.Date)
            .ToListAsync();

        // Find subscriptions renewing in 3 days
        var renewingInThreeDays = await _dbContext.BillingSubscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active &&
                       !s.CancelAtPeriodEnd &&
                       s.CurrentPeriodEnd.Date == threeDaysFromNow.Date)
            .ToListAsync();

        var notificationsSent = 0;

        foreach (var subscription in renewingInSevenDays.Concat(renewingInThreeDays))
        {
            try
            {
                var owner = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId &&
                                            u.Role == "owner");

                if (owner != null)
                {
                    var amount = subscription.TotalAmount;
                    await _billingNotificationService.SendRenewalReminderAsync(
                        owner.Id,
                        subscription.Plan?.DisplayName ?? "your plan",
                        amount,
                        subscription.CurrentPeriodEnd);
                    notificationsSent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send renewal reminder for subscription {SubscriptionId}",
                    subscription.Id);
            }
        }

        _logger.LogInformation("Sent {Count} renewal reminders", notificationsSent);
    }

    /// <summary>
    /// Process scheduled subscription resumes - runs daily.
    /// Automatically resumes paused subscriptions when their scheduled resume date arrives.
    /// </summary>
    public async Task ProcessScheduledResumesAsync()
    {
        _logger.LogInformation("Processing scheduled subscription resumes...");

        var now = DateTime.UtcNow;
        var subscriptionsToResume = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Paused &&
                       s.ScheduledResumeAt.HasValue &&
                       s.ScheduledResumeAt.Value <= now)
            .ToListAsync();

        var resumedCount = 0;

        foreach (var subscription in subscriptionsToResume)
        {
            try
            {
                await _subscriptionService.ResumeSubscriptionAsync(subscription.Id);
                resumedCount++;
                _logger.LogInformation(
                    "Automatically resumed subscription {SubscriptionId} at scheduled time",
                    subscription.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to resume subscription {SubscriptionId}",
                    subscription.Id);
            }
        }

        _logger.LogInformation("Processed {Count} scheduled subscription resumes", resumedCount);
    }

    /// <summary>
    /// Process payment retries - runs every 4 hours.
    /// Retries failed payments with exponential backoff.
    /// </summary>
    public async Task ProcessPaymentRetriesAsync()
    {
        _logger.LogInformation("Processing payment retries...");

        var now = DateTime.UtcNow;

        // Find past-due subscriptions that need payment retry
        var subscriptionsNeedingRetry = await _dbContext.BillingSubscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.PastDue &&
                       s.PaymentRetryCount < 3 &&
                       (s.NextPaymentRetryAt == null || s.NextPaymentRetryAt <= now))
            .ToListAsync();

        var successCount = 0;
        var failureCount = 0;

        foreach (var subscription in subscriptionsNeedingRetry)
        {
            try
            {
                // Get the default payment method
                var paymentMethod = await _dbContext.PaymentMethods
                    .FirstOrDefaultAsync(pm => pm.OrganizationId == subscription.OrganizationId &&
                                              pm.IsDefault &&
                                              pm.IsActive);

                if (paymentMethod == null || string.IsNullOrEmpty(paymentMethod.RazorpayTokenId))
                {
                    _logger.LogWarning(
                        "No valid payment method for subscription {SubscriptionId}",
                        subscription.Id);
                    continue;
                }

                // Attempt to charge the payment method
                var paymentResult = await AttemptPaymentAsync(subscription, paymentMethod);

                if (paymentResult.Success)
                {
                    // Payment succeeded
                    subscription.Status = SubscriptionStatus.Active;
                    subscription.PaymentRetryCount = 0;
                    subscription.NextPaymentRetryAt = null;

                    var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
                    if (org != null)
                    {
                        org.SubscriptionStatus = "active";
                    }

                    // Send success notification
                    var owner = await _dbContext.Users
                        .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId &&
                                                u.Role == "owner");

                    if (owner != null)
                    {
                        await _billingNotificationService.SendPaymentSuccessAsync(
                            owner.Id,
                            subscription.TotalAmount,
                            paymentResult.InvoiceNumber ?? "N/A",
                            subscription.Plan?.DisplayName ?? "your plan");
                    }

                    successCount++;
                    _logger.LogInformation(
                        "Payment retry succeeded for subscription {SubscriptionId}",
                        subscription.Id);
                }
                else
                {
                    // Payment failed
                    subscription.PaymentRetryCount++;
                    subscription.LastPaymentFailedAt = now;

                    // Calculate next retry with exponential backoff
                    var hoursUntilNextRetry = subscription.PaymentRetryCount switch
                    {
                        1 => 24,   // 1 day
                        2 => 48,   // 2 days
                        _ => 72    // 3 days
                    };
                    subscription.NextPaymentRetryAt = now.AddHours(hoursUntilNextRetry);

                    // Send failure notification
                    var owner = await _dbContext.Users
                        .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId &&
                                                u.Role == "owner");

                    if (owner != null)
                    {
                        if (subscription.PaymentRetryCount < 3)
                        {
                            await _billingNotificationService.SendPaymentRetryAsync(
                                owner.Id,
                                subscription.TotalAmount,
                                subscription.Plan?.DisplayName ?? "your plan",
                                subscription.NextPaymentRetryAt.Value,
                                subscription.PaymentRetryCount);
                        }
                        else
                        {
                            await _billingNotificationService.SendPaymentFailedAsync(
                                owner.Id,
                                subscription.TotalAmount,
                                subscription.Plan?.DisplayName ?? "your plan",
                                paymentResult.ErrorMessage ?? "Payment declined",
                                subscription.PaymentRetryCount);
                        }
                    }

                    failureCount++;
                    _logger.LogWarning(
                        "Payment retry {Attempt}/3 failed for subscription {SubscriptionId}: {Error}",
                        subscription.PaymentRetryCount, subscription.Id, paymentResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing payment retry for subscription {SubscriptionId}",
                    subscription.Id);
                failureCount++;
            }
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation(
            "Processed {Total} payment retries: {Success} succeeded, {Failed} failed",
            subscriptionsNeedingRetry.Count, successCount, failureCount);
    }

    private async Task<PaymentRetryResult> AttemptPaymentAsync(
        BillingSubscription subscription,
        PaymentMethod paymentMethod)
    {
        try
        {
            // For Razorpay recurring payments, we would typically use the token
            // This is a simplified implementation - actual implementation would
            // call Razorpay's recurring payment API

            // Create a new order for the recurring payment
            var order = await _razorpayService.CreateOrderAsync(new CreateOrderRequest
            {
                AmountInPaise = (int)(subscription.TotalAmount * 100),
                Currency = subscription.Currency,
                Receipt = $"retry-{subscription.Id}-{subscription.PaymentRetryCount}",
                OrganizationId = subscription.OrganizationId,
                PlanCode = subscription.Plan?.Code ?? "",
                SubscriptionId = subscription.Id
            });

            // In a real implementation, we would:
            // 1. Use the Razorpay recurring payments API with the saved token
            // 2. Automatically charge the customer
            // For now, we'll create the order and assume manual payment is needed

            _logger.LogInformation(
                "Created order {OrderId} for payment retry on subscription {SubscriptionId}",
                order.Id, subscription.Id);

            // For subscriptions with a Razorpay subscription ID, the payment
            // is handled automatically by Razorpay
            if (!string.IsNullOrEmpty(subscription.RazorpaySubscriptionId))
            {
                return new PaymentRetryResult
                {
                    Success = true,
                    InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}"
                };
            }

            return new PaymentRetryResult
            {
                Success = false,
                ErrorMessage = "Manual payment required"
            };
        }
        catch (Exception ex)
        {
            return new PaymentRetryResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private record PaymentRetryResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? InvoiceNumber { get; init; }
    }
}
