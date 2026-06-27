using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Interface for managing notification dead letter queue
/// </summary>
public interface IDeadLetterService
{
    /// <summary>
    /// Move a failed delivery to the dead letter queue
    /// </summary>
    Task<NotificationDeadLetter> MoveToDeadLetterAsync(
        NotificationDelivery delivery,
        string recipient,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get dead letters with optional filtering
    /// </summary>
    Task<DeadLetterListResponse> GetDeadLettersAsync(
        DeadLetterQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific dead letter by ID
    /// </summary>
    Task<NotificationDeadLetter?> GetDeadLetterAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempt to reprocess a dead letter
    /// </summary>
    Task<ReprocessResult> ReprocessAsync(
        Guid deadLetterId,
        Guid resolvedById,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discard a dead letter (mark as resolved without reprocessing)
    /// </summary>
    Task<bool> DiscardAsync(
        Guid deadLetterId,
        Guid resolvedById,
        string? notes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get dead letter statistics
    /// </summary>
    Task<DeadLetterStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing notification dead letter queue
/// </summary>
public class DeadLetterService : IDeadLetterService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly INotificationEngineService _notificationEngine;
    private readonly ILogger<DeadLetterService> _logger;

    // Maximum retry counts per channel before moving to dead letter
    private static readonly Dictionary<string, int> MaxRetries = new()
    {
        [NotificationChannel.Email] = 5,
        [NotificationChannel.Sms] = 5,
        [NotificationChannel.Push] = 3,
        [NotificationChannel.WhatsApp] = 3,
        [NotificationChannel.InApp] = 1
    };

    public DeadLetterService(
        ApplicationDbContext dbContext,
        INotificationEngineService notificationEngine,
        ILogger<DeadLetterService> logger)
    {
        _dbContext = dbContext;
        _notificationEngine = notificationEngine;
        _logger = logger;
    }

    /// <summary>
    /// Get the maximum retry count for a channel
    /// </summary>
    public static int GetMaxRetries(string channel) =>
        MaxRetries.TryGetValue(channel, out var max) ? max : 3;

    /// <inheritdoc />
    public async Task<NotificationDeadLetter> MoveToDeadLetterAsync(
        NotificationDelivery delivery,
        string recipient,
        CancellationToken cancellationToken = default)
    {
        // Load the notification if not already loaded
        var notification = delivery.Notification ?? await _dbContext.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == delivery.NotificationId, cancellationToken);

        if (notification == null)
        {
            throw new InvalidOperationException($"Notification {delivery.NotificationId} not found");
        }

        // Create dead letter entry
        var deadLetter = new NotificationDeadLetter
        {
            OriginalDeliveryId = delivery.Id,
            NotificationId = delivery.NotificationId,
            Channel = delivery.Channel,
            Recipient = recipient,
            LastError = delivery.FailureReason,
            AttemptCount = delivery.RetryCount + 1, // Include initial attempt
            FirstAttemptAt = delivery.CreatedAt,
            LastAttemptAt = delivery.FailedAt ?? DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(new DeadLetterPayload
            {
                NotificationId = notification.Id,
                UserId = notification.UserId,
                Type = notification.Type,
                Category = notification.Category,
                Priority = notification.Priority,
                Title = notification.Title,
                Body = notification.Body,
                Data = notification.Data,
                ActionUrl = notification.ActionUrl
            }),
            IsResolved = false
        };

        _dbContext.NotificationDeadLetters.Add(deadLetter);

        // Mark the delivery as permanently failed
        delivery.Status = DeliveryStatus.Failed;
        delivery.NextRetryAt = null;
        delivery.Metadata["movedToDeadLetter"] = true;
        delivery.Metadata["deadLetterId"] = deadLetter.Id.ToString();

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Moved delivery {DeliveryId} to dead letter queue after {AttemptCount} attempts. " +
            "Channel: {Channel}, Recipient: {Recipient}, Error: {Error}",
            delivery.Id, deadLetter.AttemptCount, delivery.Channel, recipient, delivery.FailureReason);

        return deadLetter;
    }

    /// <inheritdoc />
    public async Task<DeadLetterListResponse> GetDeadLettersAsync(
        DeadLetterQuery query,
        CancellationToken cancellationToken = default)
    {
        var queryable = _dbContext.NotificationDeadLetters
            .AsNoTracking()
            .Include(d => d.Notification)
            .Where(d => d.DeletedAt == null);

        // Apply filters
        if (!string.IsNullOrEmpty(query.Channel))
            queryable = queryable.Where(d => d.Channel == query.Channel);

        if (query.IsResolved.HasValue)
            queryable = queryable.Where(d => d.IsResolved == query.IsResolved.Value);

        if (query.Since.HasValue)
            queryable = queryable.Where(d => d.CreatedAt >= query.Since.Value);

        if (query.Until.HasValue)
            queryable = queryable.Where(d => d.CreatedAt <= query.Until.Value);

        // Get total count
        var totalCount = await queryable.CountAsync(cancellationToken);

        // Get paginated results
        var items = await queryable
            .OrderByDescending(d => d.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(d => new DeadLetterDto(
                d.Id,
                d.NotificationId,
                d.Channel,
                d.Recipient,
                d.LastError,
                d.AttemptCount,
                d.FirstAttemptAt,
                d.LastAttemptAt,
                d.IsResolved,
                d.ResolvedAt,
                d.Resolution,
                d.Notification.Type,
                d.Notification.Title
            ))
            .ToListAsync(cancellationToken);

        return new DeadLetterListResponse(
            items,
            totalCount,
            query.Page,
            query.PageSize,
            (int)Math.Ceiling(totalCount / (double)query.PageSize)
        );
    }

    /// <inheritdoc />
    public async Task<NotificationDeadLetter?> GetDeadLetterAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationDeadLetters
            .Include(d => d.Notification)
            .Include(d => d.OriginalDelivery)
            .FirstOrDefaultAsync(d => d.Id == id && d.DeletedAt == null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReprocessResult> ReprocessAsync(
        Guid deadLetterId,
        Guid resolvedById,
        CancellationToken cancellationToken = default)
    {
        var deadLetter = await _dbContext.NotificationDeadLetters
            .Include(d => d.Notification)
            .FirstOrDefaultAsync(d => d.Id == deadLetterId && d.DeletedAt == null, cancellationToken);

        if (deadLetter == null)
        {
            return new ReprocessResult(false, "Dead letter not found");
        }

        if (deadLetter.IsResolved)
        {
            return new ReprocessResult(false, "Dead letter is already resolved");
        }

        try
        {
            // Deserialize the payload
            if (string.IsNullOrEmpty(deadLetter.Payload))
            {
                return new ReprocessResult(false, "Dead letter payload is empty");
            }

            var payload = JsonSerializer.Deserialize<DeadLetterPayload>(deadLetter.Payload);
            if (payload == null)
            {
                return new ReprocessResult(false, "Failed to deserialize dead letter payload");
            }

            // Re-send the notification
            var request = new DTOs.SendNotificationRequest(
                payload.UserId,
                payload.Type,
                payload.Data,
                OverridePreferences: true
            );

            var response = await _notificationEngine.SendAsync(request, cancellationToken);

            // Mark as resolved
            deadLetter.IsResolved = true;
            deadLetter.ResolvedAt = DateTime.UtcNow;
            deadLetter.Resolution = DeadLetterResolution.Reprocessed;
            deadLetter.ResolvedById = resolvedById;
            deadLetter.ResolutionNotes = $"Reprocessed successfully. New notification ID: {response.NotificationId}";

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Successfully reprocessed dead letter {DeadLetterId}. New notification: {NotificationId}",
                deadLetterId, response.NotificationId);

            return new ReprocessResult(true, "Successfully reprocessed", response.NotificationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reprocess dead letter {DeadLetterId}", deadLetterId);
            return new ReprocessResult(false, $"Reprocessing failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> DiscardAsync(
        Guid deadLetterId,
        Guid resolvedById,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var deadLetter = await _dbContext.NotificationDeadLetters
            .FirstOrDefaultAsync(d => d.Id == deadLetterId && d.DeletedAt == null, cancellationToken);

        if (deadLetter == null || deadLetter.IsResolved)
        {
            return false;
        }

        deadLetter.IsResolved = true;
        deadLetter.ResolvedAt = DateTime.UtcNow;
        deadLetter.Resolution = DeadLetterResolution.Discarded;
        deadLetter.ResolvedById = resolvedById;
        deadLetter.ResolutionNotes = notes ?? "Discarded by administrator";

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Discarded dead letter {DeadLetterId} by user {UserId}",
            deadLetterId, resolvedById);

        return true;
    }

    /// <inheritdoc />
    public async Task<DeadLetterStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = await _dbContext.NotificationDeadLetters
            .Where(d => d.DeletedAt == null)
            .GroupBy(d => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Unresolved = g.Count(d => !d.IsResolved),
                Resolved = g.Count(d => d.IsResolved),
                Reprocessed = g.Count(d => d.Resolution == DeadLetterResolution.Reprocessed),
                Discarded = g.Count(d => d.Resolution == DeadLetterResolution.Discarded)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var byChannel = await _dbContext.NotificationDeadLetters
            .Where(d => d.DeletedAt == null && !d.IsResolved)
            .GroupBy(d => d.Channel)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Channel, x => x.Count, cancellationToken);

        var oldest = await _dbContext.NotificationDeadLetters
            .Where(d => d.DeletedAt == null && !d.IsResolved)
            .OrderBy(d => d.CreatedAt)
            .Select(d => d.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new DeadLetterStats(
            Total: stats?.Total ?? 0,
            Unresolved: stats?.Unresolved ?? 0,
            Resolved: stats?.Resolved ?? 0,
            Reprocessed: stats?.Reprocessed ?? 0,
            Discarded: stats?.Discarded ?? 0,
            ByChannel: byChannel,
            OldestUnresolved: oldest == default ? null : oldest
        );
    }
}

#region DTOs

/// <summary>
/// Serialized notification content stored in dead letter
/// </summary>
public record DeadLetterPayload
{
    public Guid NotificationId { get; init; }
    public Guid UserId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public Dictionary<string, object> Data { get; init; } = new();
    public string? ActionUrl { get; init; }
}

/// <summary>
/// Query parameters for dead letter listing
/// </summary>
public record DeadLetterQuery(
    string? Channel = null,
    bool? IsResolved = null,
    DateTime? Since = null,
    DateTime? Until = null,
    int Page = 1,
    int PageSize = 50
);

/// <summary>
/// Dead letter list response
/// </summary>
public record DeadLetterListResponse(
    List<DeadLetterDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

/// <summary>
/// Dead letter DTO for API responses
/// </summary>
public record DeadLetterDto(
    Guid Id,
    Guid NotificationId,
    string Channel,
    string Recipient,
    string? LastError,
    int AttemptCount,
    DateTime FirstAttemptAt,
    DateTime LastAttemptAt,
    bool IsResolved,
    DateTime? ResolvedAt,
    string? Resolution,
    string NotificationType,
    string NotificationTitle
);

/// <summary>
/// Result of reprocessing a dead letter
/// </summary>
public record ReprocessResult(
    bool Success,
    string? Message,
    Guid? NewNotificationId = null
);

/// <summary>
/// Dead letter queue statistics
/// </summary>
public record DeadLetterStats(
    int Total,
    int Unresolved,
    int Resolved,
    int Reprocessed,
    int Discarded,
    Dictionary<string, int> ByChannel,
    DateTime? OldestUnresolved
);

#endregion
