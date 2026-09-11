using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Email;
using EffortlessInsight.Api.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SnsMessage = Amazon.SimpleNotificationService.Util.Message;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Webhook endpoints for payment providers and email delivery events.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRazorpayService _razorpayService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IInvoiceService _invoiceService;
    private readonly IPaymentMethodService _paymentMethodService;
    private readonly IDeliveryTrackingService _deliveryTracking;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        ApplicationDbContext dbContext,
        IRazorpayService razorpayService,
        ISubscriptionService subscriptionService,
        IInvoiceService invoiceService,
        IPaymentMethodService paymentMethodService,
        IDeliveryTrackingService deliveryTracking,
        IHttpClientFactory httpClientFactory,
        IOptions<EmailOptions> emailOptions,
        ILogger<WebhooksController> logger)
    {
        _dbContext = dbContext;
        _razorpayService = razorpayService;
        _subscriptionService = subscriptionService;
        _invoiceService = invoiceService;
        _paymentMethodService = paymentMethodService;
        _deliveryTracking = deliveryTracking;
        _httpClientFactory = httpClientFactory;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Handle Razorpay webhook events.
    /// </summary>
    [HttpPost("razorpay")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleRazorpayWebhook()
    {
        // Read raw body
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        // Verify signature
        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Razorpay webhook received without signature");
            return BadRequest("Missing signature");
        }

        if (!_razorpayService.VerifyWebhookSignature(payload, signature))
        {
            _logger.LogWarning("Razorpay webhook signature verification failed");
            return BadRequest("Invalid signature");
        }

        // Parse payload
        RazorpayWebhookPayload? webhookPayload;
        try
        {
            webhookPayload = JsonSerializer.Deserialize<RazorpayWebhookPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Razorpay webhook payload");
            return BadRequest("Invalid payload");
        }

        if (webhookPayload?.Event == null)
        {
            return BadRequest("Missing event type");
        }

        // Check for duplicate (idempotency)
        // Fixes Issue #3: Webhook Replay Attack Vulnerability
        // Use Razorpay's event ID from header (unique per event) instead of payload timestamp
        var eventId = Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(eventId))
        {
            // Fallback to generated ID if header not present (shouldn't happen with Razorpay)
            eventId = $"{webhookPayload.Event}_{Guid.NewGuid()}_{webhookPayload.CreatedAt}";
            _logger.LogWarning("Razorpay webhook received without X-Razorpay-Event-Id header, using generated ID");
        }

        var existingEvent = await _dbContext.WebhookEvents
            .FirstOrDefaultAsync(e => e.Provider == "razorpay" && e.EventId == eventId);

        if (existingEvent != null)
        {
            _logger.LogInformation("Duplicate webhook event ignored: {EventId}", eventId);
            return Ok(new { status = "duplicate" });
        }

        // Store event
        var webhookEvent = new WebhookEvent
        {
            Provider = "razorpay",
            EventId = eventId,
            EventType = webhookPayload.Event,
            Status = WebhookEventStatus.Processing,
            Payload = payload,
            Signature = signature,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ProcessingStartedAt = DateTime.UtcNow
        };
        _dbContext.WebhookEvents.Add(webhookEvent);
        await _dbContext.SaveChangesAsync();

        try
        {
            // Process event
            await ProcessRazorpayEventAsync(webhookPayload, webhookEvent);

            webhookEvent.Status = WebhookEventStatus.Processed;
            webhookEvent.ProcessedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return Ok(new { status = "processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Razorpay webhook: {Event}", webhookPayload.Event);

            webhookEvent.Status = WebhookEventStatus.Failed;
            webhookEvent.ErrorMessage = ex.Message;
            webhookEvent.AttemptCount++;
            webhookEvent.LastAttemptAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Fixes Issue #8: Return HTTP 500 for transient errors so Razorpay retries
            // Return HTTP 200 for permanent errors (business logic, invalid data)
            if (IsTransientError(ex))
            {
                _logger.LogWarning(
                    "Transient error processing webhook {EventId}, Razorpay will retry. Error: {Error}",
                    webhookEvent.EventId, ex.Message);
                return StatusCode(500, new { status = "failed", error = "Transient error, will retry", retryable = true });
            }

            _logger.LogWarning(
                "Permanent error processing webhook {EventId}, will not retry. Error: {Error}",
                webhookEvent.EventId, ex.Message);
            return Ok(new { status = "failed", error = ex.Message, retryable = false });
        }
    }

    private async Task ProcessRazorpayEventAsync(RazorpayWebhookPayload payload, WebhookEvent webhookEvent)
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

            case "token.cancelled":
                await HandleTokenCancelledAsync(payload);
                break;

            case "subscription.pending":
                await HandleSubscriptionPendingAsync(payload);
                break;

            case "subscription.paused":
                await HandleSubscriptionPausedAsync(payload);
                break;

            case "subscription.authenticated":
                await HandleSubscriptionAuthenticatedAsync(payload);
                break;

            case "subscription.completed":
                await HandleSubscriptionCompletedAsync(payload);
                break;

            case "subscription.updated":
                await HandleSubscriptionUpdatedAsync(payload);
                break;

            case "invoice.paid":
                await HandleInvoicePaidAsync(payload);
                break;

            case "invoice.expired":
                await HandleInvoiceExpiredAsync(payload);
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
        var amount = paymentEntity.Amount ?? 0;

        _logger.LogInformation(
            "Payment captured: {PaymentId}, Order: {OrderId}, Amount: {Amount}",
            paymentId, orderId, amount);

        // Find the payment record
        var payment = await _dbContext.Payments
            .FirstOrDefaultAsync(p => p.RazorpayOrderId == orderId);

        if (payment != null)
        {
            payment.Status = PaymentStatus.Captured;
            payment.RazorpayPaymentId = paymentId;
            payment.CapturedAt = DateTime.UtcNow;
            payment.PaymentMethod = paymentEntity.Method ?? "unknown";

            // Mark invoice as paid
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
        var errorCode = paymentEntity.ErrorCode;
        var errorDescription = paymentEntity.ErrorDescription;

        _logger.LogWarning(
            "Payment failed for order {OrderId}: {ErrorCode} - {ErrorDescription}",
            orderId, errorCode, errorDescription);

        // Find the payment and subscription
        var payment = await _dbContext.Payments
            .Include(p => p.Subscription)
            .FirstOrDefaultAsync(p => p.RazorpayOrderId == orderId);

        if (payment != null)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = errorCode;
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

        _logger.LogInformation("Subscription activated: {SubscriptionId}", razorpaySubId);

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
            "Subscription charged: {SubscriptionId}, Amount: {Amount}",
            razorpaySubId, subscriptionEntity.Amount);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            // Build renewal webhook data from the payload
            var webhookData = new RenewalWebhookData
            {
                RazorpayPaymentId = paymentEntity?.Id,
                AmountInPaise = subscriptionEntity.Amount ?? paymentEntity?.Amount,
                Currency = subscriptionEntity.Currency ?? paymentEntity?.Currency ?? "INR",
                ChargeAt = subscriptionEntity.ChargeAt ?? paymentEntity?.CreatedAt,
                CurrentPeriodStart = subscriptionEntity.CurrentStart ?? subscriptionEntity.StartAt,
                CurrentPeriodEnd = subscriptionEntity.CurrentEnd ?? subscriptionEntity.EndAt
            };

            await _subscriptionService.ProcessRenewalAsync(subscription.Id, webhookData);
        }
        else
        {
            _logger.LogWarning(
                "No local subscription found for Razorpay subscription {RazorpaySubscriptionId}",
                razorpaySubId);
        }
    }

    private async Task HandleSubscriptionCancelledAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation("Subscription cancelled: {SubscriptionId}", razorpaySubId);

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

        _logger.LogWarning("Subscription halted: {SubscriptionId}", razorpaySubId);

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
        var paymentId = refundEntity.OrderId; // Refund contains payment_id in this field

        _logger.LogInformation("Refund created: {RefundId}", refundId);

        // Update payment record
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

    private async Task HandleTokenCancelledAsync(RazorpayWebhookPayload payload)
    {
        var tokenEntity = payload.Payload?.Token?.Entity;
        if (tokenEntity == null) return;

        var tokenId = tokenEntity.Id;
        var reason = tokenEntity.Reason ?? "Mandate cancelled by customer or bank";

        _logger.LogWarning(
            "Token/mandate cancelled: {TokenId}, Reason: {Reason}",
            tokenId, reason);

        // Deactivate the associated payment method
        var deactivated = await _paymentMethodService.DeactivateByMandateCancelledAsync(
            tokenId!,
            reason);

        if (deactivated)
        {
            _logger.LogInformation(
                "Payment method deactivated due to token cancellation: {TokenId}",
                tokenId);
        }
        else
        {
            _logger.LogWarning(
                "No payment method found to deactivate for cancelled token: {TokenId}",
                tokenId);
        }
    }

    private async Task HandleSubscriptionPendingAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation(
            "Subscription pending authentication: {SubscriptionId}",
            razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            // Subscription is waiting for customer to complete mandate authentication
            // This typically happens for UPI AutoPay when user needs to approve from their app
            subscription.Metadata ??= new Dictionary<string, object>();
            subscription.Metadata["pendingSince"] = DateTime.UtcNow.ToString("O");
            subscription.Metadata["awaitingMandateAuth"] = true;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Updated subscription {SubscriptionId} to pending state - awaiting mandate authentication",
                subscription.Id);
        }
    }

    private async Task HandleSubscriptionPausedAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation("Subscription paused: {SubscriptionId}", razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            subscription.Status = SubscriptionStatus.Paused;
            subscription.Metadata ??= new Dictionary<string, object>();
            subscription.Metadata["pausedAt"] = DateTime.UtcNow.ToString("O");
            subscription.Metadata["pausedByRazorpay"] = true;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Updated subscription {SubscriptionId} to paused state",
                subscription.Id);
        }
    }

    private async Task HandleSubscriptionAuthenticatedAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation(
            "Subscription mandate authenticated: {RazorpaySubscriptionId}",
            razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            // Update metadata to indicate mandate is authenticated
            // But DO NOT change status from trialing to active
            // The subscription remains in trial until the first charge happens
            subscription.Metadata ??= new Dictionary<string, object>();
            subscription.Metadata["mandateAuthenticated"] = true;
            subscription.Metadata["authenticatedAt"] = DateTime.UtcNow.ToString("O");
            subscription.Metadata["razorpayStatus"] = "authenticated";

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Mandate authenticated for subscription {SubscriptionId}, status remains: {Status}",
                subscription.Id, subscription.Status);
        }
        else
        {
            _logger.LogWarning(
                "No local subscription found for Razorpay subscription {RazorpaySubscriptionId}",
                razorpaySubId);
        }
    }

    private async Task HandleSubscriptionCompletedAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation(
            "Subscription completed all billing cycles: {RazorpaySubscriptionId}",
            razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            subscription.Status = SubscriptionStatus.Completed;
            subscription.EndedAt = DateTime.UtcNow;
            subscription.Metadata ??= new Dictionary<string, object>();
            subscription.Metadata["completedAt"] = DateTime.UtcNow.ToString("O");
            subscription.Metadata["totalCyclesCompleted"] = subscriptionEntity.PaidCount;

            // Update organization subscription status
            var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
            if (org != null)
            {
                org.SubscriptionStatus = "completed";
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Subscription {SubscriptionId} marked as completed after {PaidCount} billing cycles",
                subscription.Id, subscriptionEntity.PaidCount);
        }
        else
        {
            _logger.LogWarning(
                "No local subscription found for completed Razorpay subscription {RazorpaySubscriptionId}",
                razorpaySubId);
        }
    }

    private async Task HandleSubscriptionUpdatedAsync(RazorpayWebhookPayload payload)
    {
        var subscriptionEntity = payload.Payload?.Subscription?.Entity;
        if (subscriptionEntity == null) return;

        var razorpaySubId = subscriptionEntity.Id;

        _logger.LogInformation(
            "Subscription updated: {RazorpaySubscriptionId}",
            razorpaySubId);

        var subscription = await _subscriptionService.GetByRazorpayIdAsync(razorpaySubId!);
        if (subscription != null)
        {
            var changes = new List<string>();

            // Sync plan if changed - find local plan by Razorpay plan ID
            if (!string.IsNullOrEmpty(subscriptionEntity.PlanId))
            {
                var newPlan = await _dbContext.SubscriptionPlans
                    .FirstOrDefaultAsync(p =>
                        p.RazorpayPlanIdMonthly == subscriptionEntity.PlanId ||
                        p.RazorpayPlanIdAnnually == subscriptionEntity.PlanId);

                if (newPlan != null && subscription.PlanId != newPlan.Id)
                {
                    changes.Add($"plan: {subscription.PlanCode} -> {newPlan.Code}");
                    subscription.PlanId = newPlan.Id;
                    subscription.PlanCode = newPlan.Code;

                    // Update billing cycle based on which Razorpay plan ID matched
                    if (newPlan.RazorpayPlanIdAnnually == subscriptionEntity.PlanId)
                    {
                        subscription.BillingCycle = BillingCycle.Annually;
                    }
                    else
                    {
                        subscription.BillingCycle = BillingCycle.Monthly;
                    }
                }
            }

            // Sync status if changed
            var mappedStatus = MapRazorpayStatus(subscriptionEntity.Status);
            if (!string.IsNullOrEmpty(mappedStatus) && subscription.Status != mappedStatus)
            {
                changes.Add($"status: {subscription.Status} -> {mappedStatus}");
                subscription.Status = mappedStatus;
            }

            subscription.Metadata ??= new Dictionary<string, object>();
            subscription.Metadata["lastSyncedAt"] = DateTime.UtcNow.ToString("O");

            if (changes.Count > 0)
            {
                subscription.Metadata["lastChanges"] = string.Join(", ", changes);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Subscription {SubscriptionId} synced with Razorpay. Changes: {Changes}",
                    subscription.Id, string.Join(", ", changes));
            }
            else
            {
                _logger.LogInformation(
                    "Subscription {SubscriptionId} already in sync with Razorpay",
                    subscription.Id);
            }
        }
        else
        {
            _logger.LogWarning(
                "No local subscription found for updated Razorpay subscription {RazorpaySubscriptionId}",
                razorpaySubId);
        }
    }

    private async Task HandleInvoicePaidAsync(RazorpayWebhookPayload payload)
    {
        var invoiceEntity = payload.Payload?.Invoice?.Entity;
        if (invoiceEntity == null) return;

        var invoiceId = invoiceEntity.Id;
        var subscriptionId = invoiceEntity.Notes?.GetValueOrDefault("subscription_id")?.ToString();

        _logger.LogInformation(
            "Invoice paid: {InvoiceId}, Amount: {Amount} paise",
            invoiceId, invoiceEntity.Amount);

        // This event can serve as a backup confirmation for subscription.charged
        // Check if we have a subscription linked to this invoice
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            var subscription = await _subscriptionService.GetByRazorpayIdAsync(subscriptionId);
            if (subscription != null)
            {
                // Ensure subscription is active (in case subscription.charged was missed)
                if (subscription.Status == SubscriptionStatus.PastDue ||
                    subscription.Status == SubscriptionStatus.Paused)
                {
                    _logger.LogInformation(
                        "Reactivating subscription {SubscriptionId} based on invoice.paid event",
                        subscription.Id);

                    subscription.Status = SubscriptionStatus.Active;
                    subscription.Metadata ??= new Dictionary<string, object>();
                    subscription.Metadata["reactivatedViaInvoice"] = invoiceId;
                    subscription.Metadata["reactivatedAt"] = DateTime.UtcNow.ToString("O");

                    await _dbContext.SaveChangesAsync();
                }
            }
        }
    }

    private async Task HandleInvoiceExpiredAsync(RazorpayWebhookPayload payload)
    {
        var invoiceEntity = payload.Payload?.Invoice?.Entity;
        if (invoiceEntity == null) return;

        var invoiceId = invoiceEntity.Id;
        var subscriptionId = invoiceEntity.Notes?.GetValueOrDefault("subscription_id")?.ToString();

        _logger.LogWarning(
            "Invoice expired without payment: {InvoiceId}, Amount: {Amount} paise",
            invoiceId, invoiceEntity.Amount);

        // Find associated subscription and update status
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            var subscription = await _subscriptionService.GetByRazorpayIdAsync(subscriptionId);
            if (subscription != null)
            {
                // Mark as past due if not already cancelled
                if (subscription.Status != SubscriptionStatus.Cancelled &&
                    subscription.Status != SubscriptionStatus.Expired)
                {
                    subscription.Status = SubscriptionStatus.PastDue;
                    subscription.Metadata ??= new Dictionary<string, object>();
                    subscription.Metadata["expiredInvoiceId"] = invoiceId;
                    subscription.Metadata["invoiceExpiredAt"] = DateTime.UtcNow.ToString("O");

                    var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
                    if (org != null)
                    {
                        org.SubscriptionStatus = "past_due";
                    }

                    await _dbContext.SaveChangesAsync();

                    _logger.LogWarning(
                        "Subscription {SubscriptionId} marked as past_due due to expired invoice {InvoiceId}",
                        subscription.Id, invoiceId);
                }
            }
        }
    }

    /// <summary>
    /// Maps Razorpay subscription status to local status.
    /// </summary>
    private static string? MapRazorpayStatus(string? razorpayStatus)
    {
        return razorpayStatus?.ToLowerInvariant() switch
        {
            "created" => SubscriptionStatus.Trialing,
            "authenticated" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "paused" => SubscriptionStatus.Paused,
            "halted" => SubscriptionStatus.PastDue,
            "cancelled" => SubscriptionStatus.Cancelled,
            "completed" => SubscriptionStatus.Completed,
            "expired" => SubscriptionStatus.Expired,
            _ => null
        };
    }

    /// <summary>
    /// Handle Amazon SES email events delivered via an SNS HTTPS subscription:
    /// subscription confirmation plus Send/Delivery/Open/Click/Bounce/Complaint/
    /// Reject events, which are mapped onto notification delivery tracking.
    /// </summary>
    [HttpPost("ses")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleSesWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        SnsMessage snsMessage;
        try
        {
            snsMessage = SnsMessage.ParseMessage(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SES webhook received unparseable SNS payload");
            return BadRequest("Invalid SNS message");
        }

        if (!snsMessage.IsMessageSignatureValid())
        {
            _logger.LogWarning("SES webhook SNS signature verification failed for message {MessageId}", snsMessage.MessageId);
            return BadRequest("Invalid signature");
        }

        // When EventTopicArn is configured, reject events from any other topic.
        if (!string.IsNullOrEmpty(_emailOptions.EventTopicArn) &&
            !string.Equals(snsMessage.TopicArn, _emailOptions.EventTopicArn, StringComparison.Ordinal))
        {
            _logger.LogWarning("SES webhook received event from unexpected topic {TopicArn}", snsMessage.TopicArn);
            return BadRequest("Unexpected topic");
        }

        if (snsMessage.IsSubscriptionType)
        {
            return await ConfirmSnsSubscriptionAsync(snsMessage, cancellationToken);
        }

        if (!snsMessage.IsNotificationType)
        {
            _logger.LogInformation("Ignoring SNS message of type {Type}", snsMessage.Type);
            return Ok(new { status = "ignored" });
        }

        // Idempotency: SNS retries deliveries, so dedupe on the SNS message id.
        var existingEvent = await _dbContext.WebhookEvents
            .FirstOrDefaultAsync(e => e.Provider == "ses" && e.EventId == snsMessage.MessageId, cancellationToken);

        if (existingEvent != null)
        {
            return Ok(new { status = "duplicate" });
        }

        var webhookEvent = new WebhookEvent
        {
            Provider = "ses",
            EventId = snsMessage.MessageId,
            EventType = "unknown",
            Status = WebhookEventStatus.Processing,
            Payload = snsMessage.MessageText,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            ProcessingStartedAt = DateTime.UtcNow
        };
        _dbContext.WebhookEvents.Add(webhookEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await ProcessSesEventAsync(snsMessage.MessageText, webhookEvent, cancellationToken);

            webhookEvent.Status = webhookEvent.Status == WebhookEventStatus.Skipped
                ? WebhookEventStatus.Skipped
                : WebhookEventStatus.Processed;
            webhookEvent.ProcessedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { status = "processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process SES event {EventId}", webhookEvent.EventId);

            webhookEvent.Status = WebhookEventStatus.Failed;
            webhookEvent.ErrorMessage = ex.Message;
            webhookEvent.AttemptCount++;
            webhookEvent.LastAttemptAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 500 makes SNS redeliver (it retries with backoff); the dedupe row
            // above is keyed on the SNS message id, so remove it to allow the
            // retry to be processed.
            if (IsTransientError(ex))
            {
                _dbContext.WebhookEvents.Remove(webhookEvent);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return StatusCode(500, new { status = "failed", retryable = true });
            }

            return Ok(new { status = "failed", retryable = false });
        }
    }

    private async Task<IActionResult> ConfirmSnsSubscriptionAsync(SnsMessage snsMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(snsMessage.SubscribeURL) ||
            !Uri.TryCreate(snsMessage.SubscribeURL, UriKind.Absolute, out var subscribeUri) ||
            subscribeUri.Scheme != Uri.UriSchemeHttps ||
            !subscribeUri.Host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("SES webhook subscription confirmation had invalid SubscribeURL");
            return BadRequest("Invalid SubscribeURL");
        }

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(subscribeUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation("Confirmed SNS subscription for topic {TopicArn}", snsMessage.TopicArn);
        return Ok(new { status = "subscription_confirmed" });
    }

    private async Task ProcessSesEventAsync(string messageJson, WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;

        // Configuration-set event publishing uses "eventType"; identity-level
        // notifications use "notificationType". Support both.
        var eventType =
            root.TryGetProperty("eventType", out var et) ? et.GetString() :
            root.TryGetProperty("notificationType", out var nt) ? nt.GetString() :
            null;

        if (eventType == null || !root.TryGetProperty("mail", out var mail) ||
            !mail.TryGetProperty("messageId", out var messageIdProp))
        {
            _logger.LogWarning("SES event missing eventType or mail.messageId");
            webhookEvent.Status = WebhookEventStatus.Skipped;
            return;
        }

        var messageId = messageIdProp.GetString()!;
        webhookEvent.EventType = eventType;
        webhookEvent.RelatedEntityId = messageId;

        switch (eventType.ToLowerInvariant())
        {
            case "send":
                await _deliveryTracking.UpdateStatusAsync(
                    NotificationChannel.Email, messageId, DeliveryStatus.Sent,
                    GetEventTimestamp(root, "send", mail), cancellationToken: cancellationToken);
                break;

            case "delivery":
                await _deliveryTracking.UpdateStatusAsync(
                    NotificationChannel.Email, messageId, DeliveryStatus.Delivered,
                    GetEventTimestamp(root, "delivery", mail), cancellationToken: cancellationToken);
                break;

            case "open":
                await _deliveryTracking.UpdateStatusAsync(
                    NotificationChannel.Email, messageId, DeliveryStatus.Opened,
                    GetEventTimestamp(root, "open", mail), cancellationToken: cancellationToken);
                break;

            case "click":
                var url = root.TryGetProperty("click", out var click) &&
                          click.TryGetProperty("link", out var link)
                    ? link.GetString()
                    : null;
                await _deliveryTracking.RecordClickAsync(NotificationChannel.Email, messageId, url, cancellationToken);
                break;

            case "bounce":
                var bounceReason = root.TryGetProperty("bounce", out var bounce)
                    ? $"{bounce.GetPropertyOrDefault("bounceType")}/{bounce.GetPropertyOrDefault("bounceSubType")}"
                    : "unknown";
                _logger.LogWarning("SES bounce for message {MessageId}: {Reason}", messageId, bounceReason);
                await _deliveryTracking.UpdateStatusAsync(
                    NotificationChannel.Email, messageId, DeliveryStatus.Bounced,
                    GetEventTimestamp(root, "bounce", mail), $"Bounce: {bounceReason}", cancellationToken);
                break;

            case "complaint":
                var complaintType = root.TryGetProperty("complaint", out var complaint)
                    ? complaint.GetPropertyOrDefault("complaintFeedbackType") ?? "unknown"
                    : "unknown";
                _logger.LogWarning("SES complaint for message {MessageId}: {Type}", messageId, complaintType);
                await _deliveryTracking.UpdateStatusAsync(
                    NotificationChannel.Email, messageId, DeliveryStatus.Bounced,
                    GetEventTimestamp(root, "complaint", mail), $"Complaint: {complaintType}", cancellationToken);
                break;

            case "reject":
                var rejectReason = root.TryGetProperty("reject", out var reject)
                    ? reject.GetPropertyOrDefault("reason") ?? "unknown"
                    : "unknown";
                await _deliveryTracking.UpdateStatusAsync(
                    NotificationChannel.Email, messageId, DeliveryStatus.Failed,
                    GetEventTimestamp(root, "reject", mail), $"Reject: {rejectReason}", cancellationToken);
                break;

            default:
                _logger.LogInformation("Unhandled SES event type: {EventType}", eventType);
                webhookEvent.Status = WebhookEventStatus.Skipped;
                break;
        }
    }

    private static DateTime GetEventTimestamp(JsonElement root, string eventProperty, JsonElement mail)
    {
        // Prefer the event-specific timestamp, fall back to the mail timestamp,
        // then to now.
        if (root.TryGetProperty(eventProperty, out var evt) &&
            evt.TryGetProperty("timestamp", out var ts) &&
            ts.TryGetDateTime(out var eventTime))
        {
            return eventTime.ToUniversalTime();
        }

        if (mail.TryGetProperty("timestamp", out var mailTs) &&
            mailTs.TryGetDateTime(out var mailTime))
        {
            return mailTime.ToUniversalTime();
        }

        return DateTime.UtcNow;
    }

    /// <summary>
    /// Determines if an exception is a transient error that should trigger a retry.
    /// Fixes Issue #8: Webhook Retry Mechanism
    /// </summary>
    private static bool IsTransientError(Exception ex)
    {
        // Transient errors: Database, network, timeout issues
        // These should return HTTP 500 so Razorpay retries
        return ex is DbUpdateException
            || ex is TimeoutException
            || ex is TaskCanceledException
            || ex is OperationCanceledException
            || ex is HttpRequestException
            || (ex.InnerException != null && IsTransientError(ex.InnerException))
            || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase);

        // Permanent errors (ArgumentException, InvalidOperationException, etc.)
        // return HTTP 200 to prevent infinite retries
    }
}

/// <summary>
/// JSON helpers for webhook payload parsing.
/// </summary>
internal static class WebhookJsonExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
