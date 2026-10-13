using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Notifications;
using EffortlessInsight.Api.Services.WhatsApp;
using EffortlessInsight.Api.Services.WhatsApp.Commands;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Hangfire;
using NSubstitute;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Text.Json;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class WhatsAppNotificationEligibilityTests
{
    [Fact]
    public async Task Admin_disabling_feature_stops_existing_worker_with_previously_loaded_plan()
    {
        using var workerDb = BillingTestDbContextFactory.Create();
        var (user, plan, _) = await SeedAsync(workerDb);
        plan.Code = "understand_respond";
        await workerDb.SaveChangesAsync();
        Assert.NotNull(await WhatsAppNotificationEligibility.GetRecipientAsync(workerDb, user.Id));

        // Simulate the admin request using its own scope while a worker still
        // tracks the old subscription and plan in its DbContext.
        using var adminDb = new TestableApplicationDbContext(
            (DbContextOptions<ApplicationDbContext>)workerDb.GetService<IDbContextOptions>());
        var cache = Substitute.For<IDistributedCache>();
        var plans = new PlanService(adminDb, cache, NullLogger<PlanService>.Instance);
        var request = JsonSerializer.Deserialize<UpdatePlanRequest>("{\"Features\":[\"ai_explanation\"]}")!;
        await plans.UpdatePlanAsync(plan.Id, request, Guid.NewGuid());

        Assert.Contains(FeatureCodes.WhatsAppAssistant, plan.Features);
        var client = Substitute.For<IMetaWhatsAppClient>();
        var result = await CreateBot(workerDb, client).SendTemplateToUserAsync(user.Id, "daily_digest", new());
        Assert.False(result.Success);
        Assert.Empty(client.ReceivedCalls());
        await cache.Received().RemoveAsync($"org_features:{user.OrganizationId}", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Notification_engine_enforces_plan_even_when_preferences_allow_or_are_overridden(bool allowed, bool critical)
    {
        using var db = BillingTestDbContextFactory.Create();
        var (user, plan, _) = await SeedAsync(db);
        if (!allowed) plan.Features = [];
        await db.SaveChangesAsync();
        var preferences = Substitute.For<INotificationPreferencesService>();
        preferences.EvaluateChannelsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChannelDecision(false, false, false, !critical, false, false, false, null));
        var whatsapp = Substitute.For<IWhatsAppChannelService>();
        whatsapp.SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ChannelSendResult(true, "test-id", null, null));
        var sms = Substitute.For<ISmsChannelService>();
        sms.SendAsync(Arg.Any<SmsNotificationMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ChannelSendResult(true, "sms-test", null, null));
        var engine = new NotificationEngineService(db, preferences, Substitute.For<INotificationTemplateService>(),
            Substitute.For<IEmailChannelService>(), sms, Substitute.For<IPushChannelService>(), whatsapp,
            Substitute.For<IInAppChannelService>(), Substitute.For<IPushTokenService>(),
            NullLogger<NotificationEngineService>.Instance, new ConfigurationBuilder().Build(),
            Substitute.For<IServiceProvider>(), Substitute.For<IBackgroundJobClient>());

        var response = await engine.SendAsync(new SendNotificationRequest(user.Id,
            critical ? NotificationType.SlaBreach : NotificationType.NoticeHighRisk, new(), OverridePreferences: critical));
        var delivery = Assert.Single(response.Deliveries, d => d.Channel == NotificationChannel.WhatsApp);
        Assert.Equal(allowed ? "sent" : "skipped", delivery.Status);
        Assert.Equal(allowed ? 1 : 0, whatsapp.ReceivedCalls().Count());
        Assert.Equal(allowed ? 1 : 0, db.NotificationDeliveries.Count(d => d.Channel == NotificationChannel.WhatsApp));
    }

    [Theory]
    [InlineData(false, false, SubscriptionStatus.Active, false)]
    [InlineData(true, false, SubscriptionStatus.Active, false)]
    [InlineData(false, true, SubscriptionStatus.Active, true)]
    [InlineData(true, true, SubscriptionStatus.Active, true)]
    [InlineData(true, true, SubscriptionStatus.Cancelled, false)]
    [InlineData(false, true, SubscriptionStatus.Expired, false)]
    [InlineData(false, true, SubscriptionStatus.PastDue, false)]
    public async Task Only_current_plan_feature_grants_access(bool caPlan, bool feature, string status, bool expected)
    {
        using var db = BillingTestDbContextFactory.Create();
        var (_, plan, subscription) = await SeedAsync(db);
        plan.IsCaOperatorPlan = caPlan;
        plan.Features = feature ? [FeatureCodes.WhatsAppAssistant] : [];
        subscription.Status = status;
        await db.SaveChangesAsync();
        var caAccess = Substitute.For<ICaAccessService>();
        caAccess.HasActiveFreeAccessAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var cache = Substitute.For<IDistributedCache>();
        var service = new FeatureAccessService(db, cache, caAccess, NullLogger<FeatureAccessService>.Instance);

        Assert.Equal(expected, await service.HasFeatureAccessAsync(subscription.OrganizationId, FeatureCodes.WhatsAppAssistant));
        Assert.Empty(cache.ReceivedCalls());
        Assert.Empty(caAccess.ReceivedCalls());
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active, -1, 14, false)]
    [InlineData(SubscriptionStatus.Trialing, 1, 14, true)]
    [InlineData(SubscriptionStatus.Trialing, -1, 14, false)]
    [InlineData(SubscriptionStatus.Trialing, 1, 0, false)]
    public async Task Expired_billing_periods_and_invalid_trials_are_denied(string status, int days, int trialDays, bool expected)
    {
        using var db = BillingTestDbContextFactory.Create();
        var (user, plan, subscription) = await SeedAsync(db);
        subscription.Status = status;
        subscription.CurrentPeriodEnd = DateTime.UtcNow.AddDays(days);
        subscription.TrialEnd = DateTime.UtcNow.AddDays(days);
        plan.TrialDays = trialDays;
        await db.SaveChangesAsync();
        Assert.Equal(expected, await WhatsAppNotificationEligibility.GetRecipientAsync(db, user.Id) != null);
    }

    [Fact]
    public async Task Every_send_rechecks_plan_and_uses_registered_mobile_without_linking()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (user, plan, _) = await SeedAsync(db);
        var client = Substitute.For<IMetaWhatsAppClient>();
        client.SendTemplateMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<List<TemplateParameter>>(), Arg.Any<List<TemplateParameter>?>(), Arg.Any<CancellationToken>())
            .Returns(new WhatsAppSendResult(true, "test-id", null, null));
        var service = CreateBot(db, client);

        Assert.True((await service.SendTemplateToUserAsync(user.Id, "notice", new() { ["name"] = "Test" })).Success);
        await client.Received(1).SendTemplateMessageAsync(user.Mobile!, "notice", "en",
            Arg.Any<List<TemplateParameter>>(), null, Arg.Any<CancellationToken>());
        plan.Features = [];
        await db.SaveChangesAsync();
        Assert.False((await service.SendTemplateToUserAsync(user.Id, "notice", new())).Success);
        Assert.Single(client.ReceivedCalls());

        plan.Features = [FeatureCodes.WhatsAppAssistant];
        user.Mobile = "919876543211";
        await db.SaveChangesAsync();
        Assert.True((await service.SendTemplateToUserAsync(user.Id, "notice", new())).Success);
        await client.Received(1).SendTemplateMessageAsync(user.Mobile, "notice", "en",
            Arg.Any<List<TemplateParameter>>(), null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("missing_subscription")]
    [InlineData("missing_mobile")]
    [InlineData("deleted_user")]
    [InlineData("disabled_user")]
    [InlineData("channel_disabled")]
    [InlineData("unsubscribed")]
    public async Task Ineligible_recipients_never_reach_provider(string reason)
    {
        using var db = BillingTestDbContextFactory.Create();
        var (user, _, subscription) = await SeedAsync(db);
        switch (reason)
        {
            case "missing_subscription": db.BillingSubscriptions.Remove(subscription); break;
            case "missing_mobile": user.Mobile = null; break;
            case "deleted_user": user.DeletedAt = DateTime.UtcNow; break;
            case "disabled_user": user.IsActive = false; break;
            case "channel_disabled":
                db.UserNotificationPreferences.Add(new UserNotificationPreference
                {
                    UserId = user.Id,
                    ChannelSettings = new() { ["whatsapp"] = new { enabled = false } }
                });
                break;
            case "unsubscribed": db.ChannelUnsubscribes.Add(new ChannelUnsubscribe
                { UserId = user.Id, Channel = NotificationChannel.WhatsApp }); break;
        }
        await db.SaveChangesAsync();
        var client = Substitute.For<IMetaWhatsAppClient>();
        var result = await CreateBot(db, client).SendTemplateToUserAsync(user.Id, "notice", new());
        Assert.False(result.Success);
        Assert.Empty(client.ReceivedCalls());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retry_checks_current_plan_and_registered_mobile(bool allowed)
    {
        using var db = BillingTestDbContextFactory.Create();
        var (user, plan, _) = await SeedAsync(db);
        if (!allowed) plan.Features = [];
        var message = new WhatsAppMessageLog
        {
            UserId = user.Id, FullPhoneNumber = "919999999999", TemplateName = "notice",
            TemplateParameters = ["Test"], IsRetryable = true
        };
        db.WhatsAppMessageLogs.Add(message);
        await db.SaveChangesAsync();
        var client = Substitute.For<IMetaWhatsAppClient>();
        client.SendTemplateMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<List<TemplateParameter>>(), Arg.Any<List<TemplateParameter>?>(), Arg.Any<CancellationToken>())
            .Returns(new WhatsAppSendResult(true, "retry-id", null, null));
        var logs = Substitute.For<IWhatsAppMessageLogService>();
        logs.GetFailedMessagesForRetryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([message]);
        using var provider = new ServiceCollection().AddSingleton<ApplicationDbContext>(db)
            .AddSingleton(client).AddSingleton(logs)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new MetaWhatsAppOptions())).BuildServiceProvider();
        await new WhatsAppJobs(provider, NullLogger<WhatsAppJobs>.Instance).RetryFailedMessagesAsync(default);

        if (allowed)
            await client.Received(1).SendTemplateMessageAsync(user.Mobile!, "notice", "en",
                Arg.Any<List<TemplateParameter>>(), null, Arg.Any<CancellationToken>());
        else
        {
            Assert.Empty(client.ReceivedCalls());
            Assert.False(message.IsRetryable);
        }
    }

    [Fact]
    public async Task Unlink_or_stop_remains_a_notification_opt_out()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (user, _, _) = await SeedAsync(db);
        var service = new WhatsAppSessionService(db,
            Microsoft.Extensions.Options.Options.Create(new MetaWhatsAppOptions()),
            Substitute.For<IMetaWhatsAppClient>(), NullLogger<WhatsAppSessionService>.Instance);
        Assert.NotNull(await WhatsAppNotificationEligibility.GetRecipientAsync(db, user.Id));
        await service.UnlinkSessionAsync(user.Id);
        Assert.Null(await WhatsAppNotificationEligibility.GetRecipientAsync(db, user.Id));
    }

    [Fact]
    public async Task Another_organizations_plan_cannot_enable_the_users_notifications()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (user, _, _) = await SeedAsync(db);
        var paidOrganization = user.OrganizationId!.Value;
        user.OrganizationId = Guid.NewGuid();
        db.OrganizationMembers.Add(new OrganizationMember
            { UserId = user.Id, OrganizationId = paidOrganization, Status = "active" });
        await db.SaveChangesAsync();
        Assert.Null(await WhatsAppNotificationEligibility.GetRecipientAsync(db, user.Id));
    }

    private static WhatsAppBotService CreateBot(ApplicationDbContext db, IMetaWhatsAppClient client) => new(
        client, Substitute.For<IWhatsAppSessionService>(), Substitute.For<IWhatsAppMessageLogService>(),
        new CommandRouter([], NullLogger<CommandRouter>.Instance), [], db,
        Microsoft.Extensions.Options.Options.Create(new MetaWhatsAppOptions()), NullLogger<WhatsAppBotService>.Instance);

    private static async Task<(ApplicationUser, SubscriptionPlan, BillingSubscription)> SeedAsync(ApplicationDbContext db)
    {
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Features = [FeatureCodes.WhatsAppAssistant];
        var subscription = BillingTestFixture.CreateSubscription(planId: plan.Id);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), Name = "Test", OrganizationId = subscription.OrganizationId,
            Mobile = "919876543210", WhatsAppPhoneNumber = "919999999999",
            WhatsAppVerified = false, WhatsAppOptedIn = false
        };
        db.SubscriptionPlans.Add(plan);
        db.BillingSubscriptions.Add(subscription);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, plan, subscription);
    }
}
