using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>Fresh subscription checks shared by every proactive WhatsApp send, including retries.</summary>
public static class WhatsAppNotificationEligibility
{
    public static async Task<bool> HasPlanFeatureAsync(
        ApplicationDbContext db, Guid organizationId, CancellationToken ct = default)
    {
        // Do not use the feature cache or CA access overrides: this feature must be
        // explicitly included in the current subscription's configured plan.
        var subscription = await db.BillingSubscriptions.AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.OrganizationId == organizationId && s.DeletedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        if (subscription?.Plan == null || subscription.Plan.DeletedAt != null ||
            subscription.EndedAt <= now ||
            subscription.Plan.Features?.Contains(FeatureCodes.WhatsAppAssistant, StringComparer.OrdinalIgnoreCase) != true)
            return false;

        return subscription.Status switch
        {
            SubscriptionStatus.Active => subscription.CurrentPeriodStart <= now && subscription.CurrentPeriodEnd > now,
            SubscriptionStatus.Trialing => subscription.Plan.TrialDays > 0 && subscription.TrialEnd > now,
            _ => false
        };
    }

    public static async Task<ApplicationUser?> GetRecipientAsync(
        ApplicationDbContext db, Guid userId, CancellationToken ct = default)
    {
        // Re-read the account so queued messages use the current registered mobile.
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null && u.IsActive, ct);
        if (user == null || string.IsNullOrWhiteSpace(user.Mobile)) return null;

        // Keep notification opt-outs independent of the retired integration setup.
        if (await db.ChannelUnsubscribes.AsNoTracking().AnyAsync(u =>
                u.UserId == userId && u.Channel == NotificationChannel.WhatsApp &&
                u.Category == null && u.NotificationType == null && u.DeletedAt == null, ct))
            return null;
        var preferences = await db.UserNotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (preferences != null)
        {
            var channels = JsonSerializer.Deserialize<DTOs.ChannelPreferencesDto>(
                JsonSerializer.Serialize(preferences.ChannelSettings),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (channels?.WhatsApp.Enabled == false) return null;
        }

        var organizationId = user.OrganizationId;
        if (!organizationId.HasValue)
        {
            var memberships = await db.OrganizationMembers.AsNoTracking()
                .Where(m => m.UserId == userId && m.Status == "active" && m.DeletedAt == null)
                .Select(m => m.OrganizationId).Distinct().Take(2).ToListAsync(ct);
            // Never pick an arbitrary client's paid plan for a multi-organization user.
            if (memberships.Count != 1) return null;
            organizationId = memberships[0];
        }

        return await HasPlanFeatureAsync(db, organizationId.Value, ct) ? user : null;
    }
}
