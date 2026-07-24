using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Service for managing user notification preferences
/// </summary>
public class NotificationPreferencesService : INotificationPreferencesService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IChannelUnsubscribeService _channelUnsubscribe;
    private readonly ILogger<NotificationPreferencesService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NotificationPreferencesService(
        ApplicationDbContext dbContext,
        IChannelUnsubscribeService channelUnsubscribe,
        ILogger<NotificationPreferencesService> logger)
    {
        _dbContext = dbContext;
        _channelUnsubscribe = channelUnsubscribe;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        var prefs = await _dbContext.UserNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var pushTokens = await _dbContext.PushTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.IsActive)
            .Select(t => new PushTokenInfoDto(t.Platform, t.CreatedAt))
            .ToListAsync(cancellationToken);

        if (prefs == null)
        {
            // Return default preferences
            return BuildDefaultPreferences(user, pushTokens);
        }

        return MapToDto(prefs, user, pushTokens);
    }

    /// <inheritdoc />
    public async Task<NotificationPreferencesDto> UpdatePreferencesAsync(
        Guid userId, UpdatePreferencesRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        var prefs = await _dbContext.UserNotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (prefs == null)
        {
            prefs = new UserNotificationPreference
            {
                UserId = userId,
                ChannelSettings = BuildDefaultChannelSettings(user),
                QuietHours = BuildDefaultQuietHours(),
                TypePreferences = BuildDefaultTypePreferences(),
                DigestSettings = BuildDefaultDigestSettings()
            };
            _dbContext.UserNotificationPreferences.Add(prefs);
        }

        // Update channel settings
        if (request.Channels != null)
        {
            var channels = ParseChannelSettings(prefs.ChannelSettings);
            channels = MergeChannelUpdates(channels, request.Channels);
            prefs.ChannelSettings = SerializeToDict(channels);
        }

        // Update quiet hours
        if (request.QuietHours != null)
        {
            var quietHours = ParseQuietHours(prefs.QuietHours);
            if (request.QuietHours.Enabled.HasValue)
                quietHours.Enabled = request.QuietHours.Enabled.Value;
            if (request.QuietHours.Start != null)
                quietHours.Start = request.QuietHours.Start;
            if (request.QuietHours.End != null)
                quietHours.End = request.QuietHours.End;
            if (request.QuietHours.Timezone != null)
                quietHours.Timezone = request.QuietHours.Timezone;
            prefs.QuietHours = SerializeToDict(quietHours);
        }

        // Update type preferences
        if (request.Preferences != null)
        {
            var typePrefs = ParseTypePreferences(prefs.TypePreferences);
            foreach (var kvp in request.Preferences)
            {
                typePrefs[kvp.Key] = kvp.Value;
            }
            prefs.TypePreferences = SerializeToDict(typePrefs);
        }

        // Update digest settings
        if (request.Digest != null)
        {
            var digest = ParseDigestSettings(prefs.DigestSettings);
            if (request.Digest.Daily != null)
            {
                if (request.Digest.Daily.Enabled.HasValue)
                    digest.Daily.Enabled = request.Digest.Daily.Enabled.Value;
                if (request.Digest.Daily.Time != null)
                    digest.Daily.Time = request.Digest.Daily.Time;
                if (request.Digest.Daily.Timezone != null)
                    digest.Daily.Timezone = request.Digest.Daily.Timezone;
            }
            if (request.Digest.Weekly != null)
            {
                if (request.Digest.Weekly.Enabled.HasValue)
                    digest.Weekly.Enabled = request.Digest.Weekly.Enabled.Value;
                if (request.Digest.Weekly.Day.HasValue)
                    digest.Weekly.Day = request.Digest.Weekly.Day.Value;
                if (request.Digest.Weekly.Time != null)
                    digest.Weekly.Time = request.Digest.Weekly.Time;
            }
            prefs.DigestSettings = SerializeToDict(digest);
        }

        prefs.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated notification preferences for user {UserId}", userId);

        var pushTokens = await _dbContext.PushTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.IsActive)
            .Select(t => new PushTokenInfoDto(t.Platform, t.CreatedAt))
            .ToListAsync(cancellationToken);

        return MapToDto(prefs, user, pushTokens);
    }

    /// <inheritdoc />
    public async Task InitializeDefaultPreferencesAsync(
        Guid userId, string email, string? phone = null, CancellationToken cancellationToken = default)
    {
        var exists = await _dbContext.UserNotificationPreferences
            .AnyAsync(p => p.UserId == userId, cancellationToken);

        if (exists)
            return;

        var user = await _dbContext.Users.FindAsync([userId], cancellationToken);
        if (user == null)
            return;

        var prefs = new UserNotificationPreference
        {
            UserId = userId,
            ChannelSettings = BuildDefaultChannelSettings(user),
            QuietHours = BuildDefaultQuietHours(),
            TypePreferences = BuildDefaultTypePreferences(),
            DigestSettings = BuildDefaultDigestSettings()
        };

        _dbContext.UserNotificationPreferences.Add(prefs);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Initialized default notification preferences for user {UserId}", userId);
    }

    /// <inheritdoc />
    public async Task<ChannelDecision> EvaluateChannelsAsync(
        Guid userId, string notificationType, string priority, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return new ChannelDecision(false, false, false, false, true, false, false, null);
        }

        var prefs = await _dbContext.UserNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        // Use defaults if no preferences saved
        var channels = prefs != null ? ParseChannelSettings(prefs.ChannelSettings) : BuildDefaultChannelSettingsDto();
        var quietHours = prefs != null ? ParseQuietHours(prefs.QuietHours) : new QuietHoursDto();
        var typePrefs = prefs != null ? ParseTypePreferences(prefs.TypePreferences) : BuildDefaultTypePreferencesDto();

        // Check quiet hours
        var (isQuietHours, deliveryTime) = EvaluateQuietHours(quietHours, priority);

        // Get type-specific preferences (or defaults for this notification type)
        var defaultChannels = NotificationType.GetDefaultChannels(notificationType);
        var typePref = typePrefs.GetValueOrDefault(notificationType) ?? new TypeChannelPreferencesDto
        {
            Email = defaultChannels.Contains("email"),
            Sms = defaultChannels.Contains("sms"),
            Push = defaultChannels.Contains("push"),
            WhatsApp = defaultChannels.Contains("whatsapp"),
            InApp = defaultChannels.Contains("inApp")
        };

        // Combine channel enabled status with type preferences
        var shouldEmail = channels.Email.Enabled && typePref.Email && !string.IsNullOrEmpty(user.Email);
        var shouldSms = channels.Sms.Enabled && typePref.Sms && !string.IsNullOrEmpty(user.Mobile);
        var shouldPush = channels.Push.Enabled && typePref.Push;
        var shouldWhatsApp = channels.WhatsApp.Enabled && typePref.WhatsApp && !string.IsNullOrEmpty(user.Mobile);
        var shouldInApp = typePref.InApp;  // Always check type pref for in-app

        // Check legacy email unsubscribe table (address-based)
        if (shouldEmail && !string.IsNullOrEmpty(user.Email))
        {
            var isUnsubscribed = await IsUnsubscribedAsync(user.Email, notificationType, cancellationToken);
            if (isUnsubscribed)
                shouldEmail = false;
        }

        // Consult per-channel / per-category / per-type opt-outs for every
        // channel (audit BE-24). This is what honours SMS STOP webhooks and
        // granular "mute this category on push" choices.
        var category = NotificationType.GetCategory(notificationType);
        if (shouldEmail && await _channelUnsubscribe.IsUnsubscribedAsync(userId, NotificationChannel.Email, category, notificationType, cancellationToken))
            shouldEmail = false;
        if (shouldSms && await _channelUnsubscribe.IsUnsubscribedAsync(userId, NotificationChannel.Sms, category, notificationType, cancellationToken))
            shouldSms = false;
        if (shouldPush && await _channelUnsubscribe.IsUnsubscribedAsync(userId, NotificationChannel.Push, category, notificationType, cancellationToken))
            shouldPush = false;
        if (shouldWhatsApp && await _channelUnsubscribe.IsUnsubscribedAsync(userId, NotificationChannel.WhatsApp, category, notificationType, cancellationToken))
            shouldWhatsApp = false;

        return new ChannelDecision(
            shouldEmail,
            shouldSms,
            shouldPush,
            shouldWhatsApp,
            shouldInApp,
            isQuietHours,
            isQuietHours && priority != NotificationPriority.Critical,
            deliveryTime);
    }

    /// <inheritdoc />
    public async Task<UnsubscribeResponse> UnsubscribeAsync(
        string email, string? notificationType, string? reason, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.ToLowerInvariant().Trim();

        // Check if already unsubscribed
        var existing = await _dbContext.EmailUnsubscribes
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.NotificationType == notificationType, cancellationToken);

        if (existing != null)
        {
            return new UnsubscribeResponse(true, "You are already unsubscribed from these notifications.");
        }

        // Find user by email
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail.ToUpperInvariant(), cancellationToken);

        var unsubscribe = new EmailUnsubscribe
        {
            Email = normalizedEmail,
            UserId = user?.Id,
            NotificationType = notificationType,
            Reason = reason,
            UnsubscribedAt = DateTime.UtcNow
        };

        _dbContext.EmailUnsubscribes.Add(unsubscribe);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User unsubscribed: {Email} from {Type}", normalizedEmail, notificationType ?? "all");

        var message = notificationType != null
            ? $"You have been unsubscribed from {FormatNotificationType(notificationType)} notifications."
            : "You have been unsubscribed from all email notifications.";

        return new UnsubscribeResponse(true, message);
    }

    /// <inheritdoc />
    public async Task<bool> IsUnsubscribedAsync(string email, string? notificationType = null, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.ToLowerInvariant().Trim();

        // Check for global unsubscribe or specific type unsubscribe
        return await _dbContext.EmailUnsubscribes
            .AnyAsync(u => u.Email == normalizedEmail &&
                          (u.NotificationType == null || u.NotificationType == notificationType),
                cancellationToken);
    }

    #region Helper Methods

    private static (bool IsQuietHours, DateTime? DeliveryTime) EvaluateQuietHours(QuietHoursDto quietHours, string priority)
    {
        if (!quietHours.Enabled)
            return (false, null);

        // Critical notifications always bypass quiet hours
        if (priority == NotificationPriority.Critical)
            return (false, null);

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(quietHours.Timezone);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var currentTime = now.TimeOfDay;

            var start = TimeSpan.Parse(quietHours.Start);
            var end = TimeSpan.Parse(quietHours.End);

            bool isQuiet;
            if (start < end)
            {
                // Same day quiet hours (e.g., 13:00 - 15:00)
                isQuiet = currentTime >= start && currentTime < end;
            }
            else
            {
                // Overnight quiet hours (e.g., 22:00 - 07:00)
                isQuiet = currentTime >= start || currentTime < end;
            }

            if (isQuiet)
            {
                // Calculate when to deliver
                var deliveryLocal = now.Date.Add(end);
                if (deliveryLocal <= now)
                    deliveryLocal = deliveryLocal.AddDays(1);

                var deliveryUtc = TimeZoneInfo.ConvertTimeToUtc(deliveryLocal, tz);
                return (true, deliveryUtc);
            }

            return (false, null);
        }
        catch (Exception)
        {
            // If timezone parsing fails, don't apply quiet hours
            return (false, null);
        }
    }

    private NotificationPreferencesDto BuildDefaultPreferences(ApplicationUser user, List<PushTokenInfoDto> pushTokens)
    {
        return new NotificationPreferencesDto
        {
            Channels = new ChannelPreferencesDto
            {
                Email = new EmailChannelDto
                {
                    Enabled = true,
                    Address = user.Email,
                    Verified = user.EmailConfirmed
                },
                Sms = new SmsChannelDto
                {
                    Enabled = !string.IsNullOrEmpty(user.Mobile),
                    Phone = user.Mobile,
                    Verified = user.PhoneNumberConfirmed
                },
                WhatsApp = new WhatsAppChannelDto
                {
                    Enabled = false,
                    Phone = user.Mobile,
                    Verified = false
                },
                Push = new PushChannelDto
                {
                    Enabled = true,
                    Tokens = pushTokens
                }
            },
            QuietHours = new QuietHoursDto(),
            Preferences = BuildDefaultTypePreferencesDto(),
            Digest = new DigestPreferencesDto()
        };
    }

    private NotificationPreferencesDto MapToDto(UserNotificationPreference prefs, ApplicationUser user, List<PushTokenInfoDto> pushTokens)
    {
        var channels = ParseChannelSettings(prefs.ChannelSettings);
        var quietHours = ParseQuietHours(prefs.QuietHours);
        var typePrefs = ParseTypePreferences(prefs.TypePreferences);
        var digest = ParseDigestSettings(prefs.DigestSettings);

        return new NotificationPreferencesDto
        {
            Channels = new ChannelPreferencesDto
            {
                Email = new EmailChannelDto
                {
                    Enabled = channels.Email.Enabled,
                    Address = user.Email,
                    Verified = user.EmailConfirmed
                },
                Sms = new SmsChannelDto
                {
                    Enabled = channels.Sms.Enabled,
                    Phone = user.Mobile,
                    Verified = user.PhoneNumberConfirmed
                },
                WhatsApp = new WhatsAppChannelDto
                {
                    Enabled = channels.WhatsApp.Enabled,
                    Phone = user.Mobile,
                    Verified = false  // TODO: Track WhatsApp verification separately
                },
                Push = new PushChannelDto
                {
                    Enabled = channels.Push.Enabled,
                    Tokens = pushTokens
                }
            },
            QuietHours = quietHours,
            Preferences = typePrefs,
            Digest = digest
        };
    }

    private static Dictionary<string, object> BuildDefaultChannelSettings(ApplicationUser user)
    {
        return new Dictionary<string, object>
        {
            ["email"] = new Dictionary<string, object> { ["enabled"] = true },
            ["sms"] = new Dictionary<string, object> { ["enabled"] = !string.IsNullOrEmpty(user.Mobile) },
            ["whatsapp"] = new Dictionary<string, object> { ["enabled"] = false },
            ["push"] = new Dictionary<string, object> { ["enabled"] = true }
        };
    }

    private static ChannelPreferencesDto BuildDefaultChannelSettingsDto()
    {
        return new ChannelPreferencesDto
        {
            Email = new EmailChannelDto { Enabled = true },
            Sms = new SmsChannelDto { Enabled = true },
            WhatsApp = new WhatsAppChannelDto { Enabled = false },
            Push = new PushChannelDto { Enabled = true }
        };
    }

    private static Dictionary<string, object> BuildDefaultQuietHours()
    {
        return new Dictionary<string, object>
        {
            ["enabled"] = false,
            ["start"] = "22:00",
            ["end"] = "07:00",
            ["timezone"] = "Asia/Kolkata"
        };
    }

    private static Dictionary<string, object> BuildDefaultTypePreferences()
    {
        return SerializeToDict(BuildDefaultTypePreferencesDto());
    }

    private static Dictionary<string, TypeChannelPreferencesDto> BuildDefaultTypePreferencesDto()
    {
        var defaults = new Dictionary<string, TypeChannelPreferencesDto>();

        // Set up defaults based on notification type
        var types = new[]
        {
            NotificationType.Deadline7Day, NotificationType.Deadline3Day,
            NotificationType.Deadline1Day, NotificationType.DeadlineToday,
            NotificationType.DeadlineMissed, NotificationType.SlaWarning,
            NotificationType.SlaCritical, NotificationType.SlaBreach,
            NotificationType.NoticeUploaded, NotificationType.NoticeAnalyzed,
            NotificationType.NoticeHighRisk, NotificationType.TaskAssigned,
            NotificationType.TaskDueSoon, NotificationType.TaskOverdue,
            NotificationType.TaskCompleted, NotificationType.UserMentioned,
            NotificationType.DocumentRequested, NotificationType.DocumentReceived,
            NotificationType.CommentAdded,
            // GST Sync notification types
            NotificationType.GstSyncNoticesSynced, NotificationType.GstSyncDailyDigest,
            NotificationType.GstSyncFailed, NotificationType.GstSyncDueDateReminder,
            NotificationType.GstSyncDueDateOverdue, NotificationType.GstSyncExtensionDisconnected,
            NotificationType.GstSyncPaused, NotificationType.GstSyncImportCompleted
        };

        foreach (var type in types)
        {
            var channels = NotificationType.GetDefaultChannels(type);
            defaults[type] = new TypeChannelPreferencesDto
            {
                Email = channels.Contains("email"),
                Sms = channels.Contains("sms"),
                Push = channels.Contains("push"),
                WhatsApp = channels.Contains("whatsapp"),
                InApp = channels.Contains("inApp")
            };
        }

        return defaults;
    }

    private static Dictionary<string, object> BuildDefaultDigestSettings()
    {
        return new Dictionary<string, object>
        {
            ["daily"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["time"] = "09:00",
                ["timezone"] = "Asia/Kolkata"
            },
            ["weekly"] = new Dictionary<string, object>
            {
                ["enabled"] = false,
                ["day"] = 1,
                ["time"] = "09:00"
            }
        };
    }

    private static ChannelPreferencesDto ParseChannelSettings(Dictionary<string, object> settings)
    {
        var json = JsonSerializer.Serialize(settings);
        return JsonSerializer.Deserialize<ChannelPreferencesDto>(json, JsonOptions) ?? new ChannelPreferencesDto();
    }

    private static QuietHoursDto ParseQuietHours(Dictionary<string, object> settings)
    {
        var json = JsonSerializer.Serialize(settings);
        return JsonSerializer.Deserialize<QuietHoursDto>(json, JsonOptions) ?? new QuietHoursDto();
    }

    private static Dictionary<string, TypeChannelPreferencesDto> ParseTypePreferences(Dictionary<string, object> settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings);
            return JsonSerializer.Deserialize<Dictionary<string, TypeChannelPreferencesDto>>(json, JsonOptions)
                   ?? new Dictionary<string, TypeChannelPreferencesDto>();
        }
        catch
        {
            return new Dictionary<string, TypeChannelPreferencesDto>();
        }
    }

    private static DigestPreferencesDto ParseDigestSettings(Dictionary<string, object> settings)
    {
        var json = JsonSerializer.Serialize(settings);
        return JsonSerializer.Deserialize<DigestPreferencesDto>(json, JsonOptions) ?? new DigestPreferencesDto();
    }

    private static ChannelPreferencesDto MergeChannelUpdates(
        ChannelPreferencesDto channels, UpdateChannelPreferencesDto updates)
    {
        // The channel DTOs are immutable records (init-only), so merge by building
        // new instances with `with`. Only fields present on the update DTO are
        // changed; everything else (verified flags, tokens) is preserved.
        var email = channels.Email;
        if (updates.Email != null)
        {
            email = email with
            {
                Enabled = updates.Email.Enabled ?? email.Enabled,
                Address = updates.Email.Address ?? email.Address,
            };
        }

        var sms = channels.Sms;
        if (updates.Sms != null)
        {
            sms = sms with
            {
                Enabled = updates.Sms.Enabled ?? sms.Enabled,
                Phone = updates.Sms.Phone ?? sms.Phone,
            };
        }

        var whatsApp = channels.WhatsApp;
        if (updates.WhatsApp != null)
        {
            whatsApp = whatsApp with
            {
                Enabled = updates.WhatsApp.Enabled ?? whatsApp.Enabled,
                Phone = updates.WhatsApp.Phone ?? whatsApp.Phone,
            };
        }

        var push = channels.Push;
        if (updates.Push != null)
        {
            push = push with { Enabled = updates.Push.Enabled ?? push.Enabled };
        }

        return channels with
        {
            Email = email,
            Sms = sms,
            WhatsApp = whatsApp,
            Push = push,
        };
    }

    private static Dictionary<string, object> SerializeToDict<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
    }

    private static string FormatNotificationType(string type)
    {
        return type.Replace("_", " ").Replace("-", " ");
    }

    #endregion
}
