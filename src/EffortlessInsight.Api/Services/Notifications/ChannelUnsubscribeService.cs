using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Interface for managing channel-specific unsubscribes (SMS, WhatsApp, Email, Push)
/// </summary>
public interface IChannelUnsubscribeService
{
    /// <summary>
    /// Check if a user has unsubscribed from a specific channel/category/type combination
    /// </summary>
    Task<bool> IsUnsubscribedAsync(Guid userId, string channel, string? category = null, string? notificationType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribe a user from a channel (optionally for specific category/type)
    /// </summary>
    Task<ChannelUnsubscribe> UnsubscribeAsync(UnsubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resubscribe a user to a channel (optionally for specific category/type)
    /// </summary>
    Task<bool> ResubscribeAsync(ResubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all unsubscribes for a user
    /// </summary>
    Task<List<ChannelUnsubscribeDto>> GetUserUnsubscribesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process webhook unsubscribe events (e.g., Twilio STOP messages)
    /// </summary>
    Task ProcessWebhookUnsubscribeAsync(string channel, string recipient, string? externalReference = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing channel-specific unsubscribes
/// </summary>
public class ChannelUnsubscribeService : IChannelUnsubscribeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ChannelUnsubscribeService> _logger;

    public ChannelUnsubscribeService(
        ApplicationDbContext dbContext,
        ILogger<ChannelUnsubscribeService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsUnsubscribedAsync(
        Guid userId,
        string channel,
        string? category = null,
        string? notificationType = null,
        CancellationToken cancellationToken = default)
    {
        // Check for global channel unsubscribe first
        var hasGlobalUnsubscribe = await _dbContext.ChannelUnsubscribes
            .AnyAsync(u => u.UserId == userId &&
                          u.Channel == channel &&
                          u.Category == null &&
                          u.NotificationType == null &&
                          u.DeletedAt == null,
                cancellationToken);

        if (hasGlobalUnsubscribe)
            return true;

        // Check for category-level unsubscribe
        if (!string.IsNullOrEmpty(category))
        {
            var hasCategoryUnsubscribe = await _dbContext.ChannelUnsubscribes
                .AnyAsync(u => u.UserId == userId &&
                              u.Channel == channel &&
                              u.Category == category &&
                              u.NotificationType == null &&
                              u.DeletedAt == null,
                    cancellationToken);

            if (hasCategoryUnsubscribe)
                return true;
        }

        // Check for type-level unsubscribe
        if (!string.IsNullOrEmpty(notificationType))
        {
            var hasTypeUnsubscribe = await _dbContext.ChannelUnsubscribes
                .AnyAsync(u => u.UserId == userId &&
                              u.Channel == channel &&
                              u.NotificationType == notificationType &&
                              u.DeletedAt == null,
                    cancellationToken);

            if (hasTypeUnsubscribe)
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<ChannelUnsubscribe> UnsubscribeAsync(
        UnsubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        // Check if already unsubscribed
        var existing = await _dbContext.ChannelUnsubscribes
            .FirstOrDefaultAsync(u => u.UserId == request.UserId &&
                                     u.Channel == request.Channel &&
                                     u.Category == request.Category &&
                                     u.NotificationType == request.NotificationType &&
                                     u.DeletedAt == null,
                cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation(
                "User {UserId} already unsubscribed from {Channel} (category: {Category}, type: {Type})",
                request.UserId, request.Channel, request.Category, request.NotificationType);
            return existing;
        }

        var unsubscribe = new ChannelUnsubscribe
        {
            UserId = request.UserId,
            Channel = request.Channel,
            Category = request.Category,
            NotificationType = request.NotificationType,
            UnsubscribedAt = DateTime.UtcNow,
            Reason = request.Reason,
            Source = request.Source ?? UnsubscribeSource.UserRequest
        };

        _dbContext.ChannelUnsubscribes.Add(unsubscribe);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} unsubscribed from {Channel} (category: {Category}, type: {Type}, reason: {Reason})",
            request.UserId, request.Channel, request.Category, request.NotificationType, request.Reason);

        return unsubscribe;
    }

    /// <inheritdoc />
    public async Task<bool> ResubscribeAsync(
        ResubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var unsubscribe = await _dbContext.ChannelUnsubscribes
            .FirstOrDefaultAsync(u => u.UserId == request.UserId &&
                                     u.Channel == request.Channel &&
                                     u.Category == request.Category &&
                                     u.NotificationType == request.NotificationType &&
                                     u.DeletedAt == null,
                cancellationToken);

        if (unsubscribe == null)
        {
            _logger.LogInformation(
                "User {UserId} was not unsubscribed from {Channel} (category: {Category}, type: {Type})",
                request.UserId, request.Channel, request.Category, request.NotificationType);
            return false;
        }

        // Soft delete the unsubscribe record
        unsubscribe.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} resubscribed to {Channel} (category: {Category}, type: {Type})",
            request.UserId, request.Channel, request.Category, request.NotificationType);

        return true;
    }

    /// <inheritdoc />
    public async Task<List<ChannelUnsubscribeDto>> GetUserUnsubscribesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var unsubscribes = await _dbContext.ChannelUnsubscribes
            .AsNoTracking()
            .Where(u => u.UserId == userId && u.DeletedAt == null)
            .OrderByDescending(u => u.UnsubscribedAt)
            .ToListAsync(cancellationToken);

        return unsubscribes.Select(u => new ChannelUnsubscribeDto(
            u.Id,
            u.Channel,
            u.Category,
            u.NotificationType,
            u.UnsubscribedAt,
            u.Reason
        )).ToList();
    }

    /// <inheritdoc />
    public async Task ProcessWebhookUnsubscribeAsync(
        string channel,
        string recipient,
        string? externalReference = null,
        CancellationToken cancellationToken = default)
    {
        // Find user by recipient (phone number for SMS/WhatsApp)
        ApplicationUser? user = null;

        if (channel == NotificationChannel.Sms || channel == NotificationChannel.WhatsApp)
        {
            // Normalize phone number for lookup
            var normalizedPhone = NormalizePhoneNumber(recipient);
            user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Mobile != null &&
                                         u.Mobile.Contains(normalizedPhone) &&
                                         u.DeletedAt == null,
                    cancellationToken);
        }
        else if (channel == NotificationChannel.Email)
        {
            user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == recipient && u.DeletedAt == null,
                    cancellationToken);
        }

        if (user == null)
        {
            _logger.LogWarning(
                "Could not find user for webhook unsubscribe: channel={Channel}, recipient={Recipient}",
                channel, recipient);
            return;
        }

        // Check if already unsubscribed
        var existing = await _dbContext.ChannelUnsubscribes
            .AnyAsync(u => u.UserId == user.Id &&
                          u.Channel == channel &&
                          u.Category == null &&
                          u.DeletedAt == null,
                cancellationToken);

        if (existing)
        {
            _logger.LogInformation(
                "User {UserId} already unsubscribed from {Channel} via webhook",
                user.Id, channel);
            return;
        }

        var unsubscribe = new ChannelUnsubscribe
        {
            UserId = user.Id,
            Channel = channel,
            Category = null,
            NotificationType = null,
            UnsubscribedAt = DateTime.UtcNow,
            Reason = "Unsubscribed via webhook (STOP message)",
            Source = UnsubscribeSource.Webhook,
            ExternalReference = externalReference
        };

        _dbContext.ChannelUnsubscribes.Add(unsubscribe);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} unsubscribed from {Channel} via webhook",
            user.Id, channel);
    }

    private static string NormalizePhoneNumber(string phone)
    {
        // Remove non-digit characters and country code prefix
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // Remove +91 prefix for Indian numbers
        if (digits.StartsWith("91") && digits.Length == 12)
            return digits[2..];

        return digits;
    }
}

#region DTOs

/// <summary>
/// Request to unsubscribe from a channel
/// </summary>
public record UnsubscribeRequest(
    Guid UserId,
    string Channel,
    string? Category = null,
    string? NotificationType = null,
    string? Reason = null,
    string? Source = null
);

/// <summary>
/// Request to resubscribe to a channel
/// </summary>
public record ResubscribeRequest(
    Guid UserId,
    string Channel,
    string? Category = null,
    string? NotificationType = null
);

/// <summary>
/// DTO for channel unsubscribe record
/// </summary>
public record ChannelUnsubscribeDto(
    Guid Id,
    string Channel,
    string? Category,
    string? NotificationType,
    DateTime UnsubscribedAt,
    string? Reason
);

#endregion
