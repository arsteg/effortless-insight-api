using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class AskAiFeatureAccessTests
{
    [Theory]
    [InlineData(false, false, SubscriptionStatus.Active, false)]
    [InlineData(true, false, SubscriptionStatus.Active, false)]
    [InlineData(false, true, SubscriptionStatus.Active, true)]
    [InlineData(true, true, SubscriptionStatus.Active, true)]
    [InlineData(true, true, SubscriptionStatus.Expired, false)]
    [InlineData(false, true, SubscriptionStatus.Cancelled, false)]
    public async Task AskAi_requires_explicit_plan_feature_even_with_CA_access(
        bool caPlan, bool enabled, string status, bool expected)
    {
        using var db = BillingTestDbContextFactory.Create();
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.IsCaOperatorPlan = caPlan;
        plan.Features = enabled ? [FeatureCodes.AskAi] : [FeatureCodes.AiExplanation];
        var subscription = BillingTestFixture.CreateSubscription(planId: plan.Id, status: status);
        db.SubscriptionPlans.Add(plan);
        db.BillingSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        var caAccess = Substitute.For<ICaAccessService>();
        caAccess.HasActiveFreeAccessAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var service = new FeatureAccessService(db, Substitute.For<IDistributedCache>(),
            caAccess, NullLogger<FeatureAccessService>.Instance);

        var result = await service.HasFeatureAccessAsync(subscription.OrganizationId, FeatureCodes.AskAi);

        result.Should().Be(expected);
        await caAccess.DidNotReceive().HasActiveFreeAccessAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}