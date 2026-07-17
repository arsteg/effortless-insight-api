using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for ensuring webhook idempotency.
/// Prevents duplicate processing of webhook payloads.
/// </summary>
public interface IWhatsAppWebhookIdempotencyService
{
    /// <summary>
    /// Check if a webhook payload has already been processed.
    /// </summary>
    Task<bool> IsProcessedAsync(string payload, CancellationToken ct = default);

    /// <summary>
    /// Mark a webhook payload as received (before processing).
    /// </summary>
    Task<string> MarkReceivedAsync(string payload, string? entryId, string eventType, CancellationToken ct = default);

    /// <summary>
    /// Mark a webhook as processed (after processing).
    /// </summary>
    Task MarkProcessedAsync(string payloadHash, string result, string? errorMessage = null, CancellationToken ct = default);

    /// <summary>
    /// Clean up old webhook events.
    /// </summary>
    Task CleanupOldEventsAsync(int retentionDays = 7, CancellationToken ct = default);
}

/// <summary>
/// Implementation of webhook idempotency service.
/// </summary>
public class WhatsAppWebhookIdempotencyService : IWhatsAppWebhookIdempotencyService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<WhatsAppWebhookIdempotencyService> _logger;

    public WhatsAppWebhookIdempotencyService(
        ApplicationDbContext db,
        ILogger<WhatsAppWebhookIdempotencyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> IsProcessedAsync(string payload, CancellationToken ct = default)
    {
        var hash = ComputeHash(payload);

        return await _db.WhatsAppWebhookEvents
            .AnyAsync(e => e.PayloadHash == hash, ct);
    }

    public async Task<string> MarkReceivedAsync(
        string payload,
        string? entryId,
        string eventType,
        CancellationToken ct = default)
    {
        var hash = ComputeHash(payload);

        var existing = await _db.WhatsAppWebhookEvents
            .FirstOrDefaultAsync(e => e.PayloadHash == hash, ct);

        if (existing != null)
        {
            _logger.LogDebug("Webhook already received: {Hash}", hash);
            return hash;
        }

        var webhookEvent = new WhatsAppWebhookEvent
        {
            PayloadHash = hash,
            EntryId = entryId,
            EventType = eventType,
            ReceivedAt = DateTime.UtcNow
        };

        _db.WhatsAppWebhookEvents.Add(webhookEvent);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent insert - already exists
            _logger.LogDebug("Concurrent webhook insert detected: {Hash}", hash);
        }

        return hash;
    }

    public async Task MarkProcessedAsync(
        string payloadHash,
        string result,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var webhookEvent = await _db.WhatsAppWebhookEvents
            .FirstOrDefaultAsync(e => e.PayloadHash == payloadHash, ct);

        if (webhookEvent != null)
        {
            webhookEvent.ProcessedAt = DateTime.UtcNow;
            webhookEvent.ProcessingResult = result;
            webhookEvent.ErrorMessage = errorMessage?.Length > 500
                ? errorMessage[..500]
                : errorMessage;

            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task CleanupOldEventsAsync(int retentionDays = 7, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var oldEvents = await _db.WhatsAppWebhookEvents
            .Where(e => e.ReceivedAt < cutoff)
            .ToListAsync(ct);

        if (oldEvents.Any())
        {
            _db.WhatsAppWebhookEvents.RemoveRange(oldEvents);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} old webhook events", oldEvents.Count);
        }
    }

    private static string ComputeHash(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
