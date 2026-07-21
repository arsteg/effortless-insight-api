using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Notifications;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services.Notifications;

/// <summary>
/// Regression tests for notification channel preference persistence.
/// Guards audit finding BE-01: MergeChannelUpdates was an empty method, so
/// channel toggles (and email unsubscribe, which flows through the same path)
/// silently did not persist.
/// </summary>
public class NotificationPreferencesServiceTests
{
    private static NotificationPreferencesService CreateService(EffortlessInsight.Api.Data.ApplicationDbContext db)
        => new(db, Mock.Of<IChannelUnsubscribeService>(), Mock.Of<ILogger<NotificationPreferencesService>>());

    [Fact]
    public async Task UpdatePreferences_DisablingPushChannel_Persists()
    {
        using var db = BillingTestDbContextFactory.Create();
        var user = TestFixture.CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Push is enabled by default; turn it off.
        await service.UpdatePreferencesAsync(user.Id, new UpdatePreferencesRequest
        {
            Channels = new UpdateChannelPreferencesDto
            {
                Push = new UpdatePushChannelDto { Enabled = false }
            }
        });

        var prefs = await service.GetPreferencesAsync(user.Id);
        Assert.False(prefs.Channels.Push.Enabled);
        // Other channels must be untouched by a push-only update.
        Assert.True(prefs.Channels.Email.Enabled);
    }

    [Fact]
    public async Task UpdatePreferences_DisablingEmail_ReflectedInChannelEvaluation()
    {
        using var db = BillingTestDbContextFactory.Create();
        var user = TestFixture.CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.UpdatePreferencesAsync(user.Id, new UpdatePreferencesRequest
        {
            Channels = new UpdateChannelPreferencesDto
            {
                Email = new UpdateEmailChannelDto { Enabled = false }
            }
        });

        // The channel decision that actually drives sending must honour the opt-out.
        var decision = await service.EvaluateChannelsAsync(
            user.Id, NotificationType.DeadlineToday, NotificationPriority.High);

        Assert.False(decision.ShouldSendEmail);
    }

    [Fact]
    public async Task UpdatePreferences_SequentialChannelUpdates_Accumulate()
    {
        // The original empty MergeChannelUpdates would have lost the first update
        // when the second was applied; each partial update must stick and must
        // not reset the channels it didn't touch.
        using var db = BillingTestDbContextFactory.Create();
        var user = TestFixture.CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        await service.UpdatePreferencesAsync(user.Id, new UpdatePreferencesRequest
        {
            Channels = new UpdateChannelPreferencesDto { Push = new UpdatePushChannelDto { Enabled = false } }
        });
        await service.UpdatePreferencesAsync(user.Id, new UpdatePreferencesRequest
        {
            Channels = new UpdateChannelPreferencesDto { Email = new UpdateEmailChannelDto { Enabled = false } }
        });

        var prefs = await service.GetPreferencesAsync(user.Id);
        Assert.False(prefs.Channels.Push.Enabled);   // first update survived
        Assert.False(prefs.Channels.Email.Enabled);  // second update applied
        Assert.True(prefs.Channels.Sms.Enabled);     // untouched channel unchanged
    }
}
