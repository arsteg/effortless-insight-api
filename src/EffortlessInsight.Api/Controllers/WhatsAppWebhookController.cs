using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Webhook controller for Meta WhatsApp Cloud API.
/// Handles incoming messages and delivery status updates.
/// </summary>
[ApiController]
[Route("api/webhooks/meta/whatsapp")]
[AllowAnonymous]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly MetaWhatsAppOptions _options;
    private readonly IWhatsAppBotService _botService;
    private readonly IWhatsAppMessageLogService _messageLogService;
    private readonly IWhatsAppWebhookIdempotencyService _idempotencyService;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IOptions<MetaWhatsAppOptions> options,
        IWhatsAppBotService botService,
        IWhatsAppMessageLogService messageLogService,
        IWhatsAppWebhookIdempotencyService idempotencyService,
        ILogger<WhatsAppWebhookController> logger)
    {
        _options = options.Value;
        _botService = botService;
        _messageLogService = messageLogService;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    /// <summary>
    /// Webhook verification endpoint.
    /// Meta sends a GET request to verify the webhook URL.
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        _logger.LogInformation(
            "WhatsApp webhook verification request. Mode: {Mode}, Token present: {TokenPresent}",
            mode,
            !string.IsNullOrEmpty(token));

        if (!_options.Enabled)
        {
            _logger.LogWarning("WhatsApp bot is disabled");
            return StatusCode(503, "WhatsApp bot is disabled");
        }

        if (mode == "subscribe" && token == _options.VerifyToken)
        {
            _logger.LogInformation("WhatsApp webhook verified successfully");
            return Ok(challenge);
        }

        _logger.LogWarning("WhatsApp webhook verification failed. Invalid token.");
        return Forbid();
    }

    /// <summary>
    /// Webhook endpoint for incoming messages and status updates.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleWebhook(CancellationToken ct)
    {
        // Generate correlation ID for request tracing
        var correlationId = HttpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..16];

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        if (!_options.Enabled)
        {
            return Ok(); // Acknowledge but don't process
        }

        // Read the raw body for signature verification
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        // Verify signature
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (!VerifySignature(rawBody, signature))
        {
            _logger.LogWarning("Invalid webhook signature. CorrelationId: {CorrelationId}", correlationId);
            return Unauthorized("Invalid signature");
        }

        try
        {
            // Entry-level idempotency check
            if (await _idempotencyService.IsProcessedAsync(rawBody, ct))
            {
                _logger.LogDebug("Skipping duplicate webhook payload. CorrelationId: {CorrelationId}", correlationId);
                return Ok();
            }

            var payload = JsonSerializer.Deserialize<MetaWebhookPayload>(rawBody);
            if (payload?.Object != "whatsapp_business_account")
            {
                _logger.LogDebug("Ignoring non-WhatsApp webhook payload");
                return Ok();
            }

            // Mark webhook as received for idempotency
            var entryId = payload.Entry?.FirstOrDefault()?.Id;
            var eventType = payload.Entry?.FirstOrDefault()?.Changes?.FirstOrDefault()?.Field ?? "unknown";
            var payloadHash = await _idempotencyService.MarkReceivedAsync(rawBody, entryId, eventType, ct);

            // Process each entry
            foreach (var entry in payload.Entry ?? [])
            {
                foreach (var change in entry.Changes ?? [])
                {
                    if (change.Field != "messages")
                        continue;

                    var value = change.Value;
                    if (value == null)
                        continue;

                    // Process incoming messages
                    if (value.Messages?.Any() == true)
                    {
                        foreach (var message in value.Messages)
                        {
                            await ProcessIncomingMessageAsync(message, value.Metadata, ct);
                        }
                    }

                    // Process status updates
                    if (value.Statuses?.Any() == true)
                    {
                        foreach (var status in value.Statuses)
                        {
                            await ProcessStatusUpdateAsync(status, ct);
                        }
                    }

                    // Process errors
                    if (value.Errors?.Any() == true)
                    {
                        foreach (var error in value.Errors)
                        {
                            _logger.LogError(
                                "WhatsApp webhook error. Code: {Code}, Title: {Title}, Message: {Message}",
                                error.Code,
                                error.Title,
                                error.Message);
                        }
                    }
                }
            }

            // Mark as processed successfully
            await _idempotencyService.MarkProcessedAsync(payloadHash, "success", null, ct);

            return Ok();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse webhook payload");
            return BadRequest("Invalid JSON payload");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            // Return OK to prevent Meta from retrying
            return Ok();
        }
    }

    private async Task ProcessIncomingMessageAsync(
        MetaWebhookMessage message,
        MetaWebhookMetadata? metadata,
        CancellationToken ct)
    {
        if (message.From == null || message.Id == null)
            return;

        // Idempotency check: Skip if message was already processed
        var existingMessage = await _messageLogService.GetByWamIdAsync(message.Id, ct);
        if (existingMessage != null)
        {
            _logger.LogDebug(
                "Skipping duplicate message. WamId: {WamId}, Original processing at: {ProcessedAt}",
                message.Id,
                existingMessage.CreatedAt);
            return;
        }

        var incomingMessage = new WhatsAppIncomingMessage(
            WamId: message.Id,
            From: message.From,
            PhoneNumberId: metadata?.PhoneNumberId ?? string.Empty,
            Timestamp: long.TryParse(message.Timestamp, out var ts) ? ts : DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Type: message.Type ?? "text",
            Text: message.Text?.Body,
            ButtonReplyId: message.Interactive?.ButtonReply?.Id ?? message.Button?.Payload,
            ListReplyId: message.Interactive?.ListReply?.Id,
            MediaId: message.Image?.Id ?? message.Document?.Id,
            Caption: message.Image?.Caption ?? message.Document?.Caption
        );

        _logger.LogInformation(
            "Received WhatsApp message. From: {From}, Type: {Type}, WamId: {WamId}",
            MaskPhoneNumber(message.From),
            message.Type,
            message.Id);

        // Process the message through the bot service
        await _botService.ProcessIncomingMessageAsync(incomingMessage, ct);
    }

    private async Task ProcessStatusUpdateAsync(MetaWebhookStatus status, CancellationToken ct)
    {
        if (status.Id == null)
            return;

        _logger.LogDebug(
            "WhatsApp status update. MessageId: {MessageId}, Status: {Status}",
            status.Id,
            status.Status);

        var timestamp = long.TryParse(status.Timestamp, out var ts)
            ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime
            : DateTime.UtcNow;

        await _messageLogService.UpdateMessageStatusAsync(
            status.Id,
            status.Status ?? "unknown",
            timestamp,
            status.Errors?.FirstOrDefault()?.Code.ToString(),
            status.Errors?.FirstOrDefault()?.Message,
            ct);
    }

    private bool VerifySignature(string payload, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || !signature.StartsWith("sha256="))
        {
            _logger.LogWarning("Missing or invalid signature format");
            return false;
        }

        var expectedHash = signature[7..]; // Remove "sha256=" prefix

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.AppSecret));
        var computedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHash.ToLower()),
            Encoding.UTF8.GetBytes(computedHash.ToLower()));

        if (!isValid)
        {
            _logger.LogWarning("Signature mismatch");
        }

        return isValid;
    }

    private static string MaskPhoneNumber(string phone)
    {
        if (phone.Length < 6)
            return phone;
        return $"+{phone[..2]}****{phone[^4..]}";
    }
}
