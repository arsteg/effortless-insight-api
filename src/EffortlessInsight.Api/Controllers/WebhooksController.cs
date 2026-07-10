using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Webhook endpoints for payment providers.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRazorpayService _razorpayService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        ApplicationDbContext dbContext,
        IRazorpayService razorpayService,
        ISubscriptionService subscriptionService,
        IInvoiceService invoiceService,
        ILogger<WebhooksController> logger)
    {
        _dbContext = dbContext;
        _razorpayService = razorpayService;
        _subscriptionService = subscriptionService;
        _invoiceService = invoiceService;
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
                CurrentPeriodStart = subscriptionEntity.StartAt,
                CurrentPeriodEnd = subscriptionEntity.EndAt
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
