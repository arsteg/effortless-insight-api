using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Jobs;

public class BillingJobsTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IUsageService _usageService;
    private readonly IBillingNotificationService _billingNotificationService;
    private readonly IRazorpayService _razorpayService;
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<BillingJobs> _logger;
    private readonly BillingJobs _sut;

    public BillingJobsTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _subscriptionService = Substitute.For<ISubscriptionService>();
        _usageService = Substitute.For<IUsageService>();
        _billingNotificationService = Substitute.For<IBillingNotificationService>();
        _razorpayService = Substitute.For<IRazorpayService>();
        _invoiceService = Substitute.For<IInvoiceService>();
        _logger = Substitute.For<ILogger<BillingJobs>>();

        _sut = new BillingJobs(
            _dbContext,
            _subscriptionService,
            _usageService,
            _billingNotificationService,
            _razorpayService,
            _invoiceService,
            _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region ProcessTrialExpirationsAsync Tests

    [Fact]
    public async Task ProcessTrialExpirationsAsync_WithExpiredTrials_ShouldExpireThem()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateTrialSubscription(trialDaysRemaining: -1);
        subscription.TrialEnd = DateTime.UtcNow.AddDays(-1);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessTrialExpirationsAsync();

        // Assert
        await _subscriptionService.Received(1).ExpireTrialAsync(subscription.Id);
    }

    [Fact]
    public async Task ProcessTrialExpirationsAsync_WithActiveTrials_ShouldNotExpireThem()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateTrialSubscription(trialDaysRemaining: 7);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessTrialExpirationsAsync();

        // Assert
        await _subscriptionService.DidNotReceive().ExpireTrialAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ProcessTrialExpirationsAsync_WhenServiceThrows_ShouldContinueProcessing()
    {
        // Arrange
        var sub1 = BillingTestFixture.CreateTrialSubscription(trialDaysRemaining: -1);
        sub1.TrialEnd = DateTime.UtcNow.AddHours(-1);
        var sub2 = BillingTestFixture.CreateTrialSubscription(trialDaysRemaining: -1);
        sub2.TrialEnd = DateTime.UtcNow.AddHours(-2);

        _dbContext.BillingSubscriptions.AddRange(sub1, sub2);
        await _dbContext.SaveChangesAsync();

        _subscriptionService.ExpireTrialAsync(sub1.Id).Returns(Task.FromException(new Exception("Test error")));

        // Act
        await _sut.ProcessTrialExpirationsAsync();

        // Assert
        await _subscriptionService.Received(2).ExpireTrialAsync(Arg.Any<Guid>());
    }

    #endregion

    #region ProcessSubscriptionRenewalsAsync Tests

    [Fact]
    public async Task ProcessSubscriptionRenewalsAsync_WithDueRenewals_ShouldProcess()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateSubscription(status: SubscriptionStatus.Active);
        subscription.CurrentPeriodEnd = DateTime.UtcNow.AddHours(-1);
        subscription.CancelAtPeriodEnd = false;
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessSubscriptionRenewalsAsync();

        // Assert
        await _subscriptionService.Received(1).ProcessRenewalAsync(subscription.Id);
    }

    [Fact]
    public async Task ProcessSubscriptionRenewalsAsync_WithCancelAtPeriodEnd_ShouldCancelSubscription()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, status: SubscriptionStatus.Active);
        subscription.CurrentPeriodEnd = DateTime.UtcNow.AddHours(-1);
        subscription.CancelAtPeriodEnd = true;
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessSubscriptionRenewalsAsync();

        // Assert
        var updatedSubscription = await _dbContext.BillingSubscriptions.FindAsync(subscription.Id);
        updatedSubscription!.Status.Should().Be(SubscriptionStatus.Cancelled);
        updatedSubscription.EndedAt.Should().NotBeNull();

        var updatedOrg = await _dbContext.Organizations.FindAsync(orgId);
        updatedOrg!.SubscriptionStatus.Should().Be("cancelled");
    }

    [Fact]
    public async Task ProcessSubscriptionRenewalsAsync_WithFutureRenewal_ShouldNotProcess()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateSubscription(status: SubscriptionStatus.Active);
        subscription.CurrentPeriodEnd = DateTime.UtcNow.AddDays(15);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessSubscriptionRenewalsAsync();

        // Assert
        await _subscriptionService.DidNotReceive().ProcessRenewalAsync(Arg.Any<Guid>());
    }

    #endregion

    #region ApplyScheduledPlanChangesAsync Tests

    [Fact]
    public async Task ApplyScheduledPlanChangesAsync_WithScheduledChange_ShouldApply()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateSubscription();
        subscription.ScheduledPlanCode = "professional";
        subscription.ScheduledChangeDate = DateTime.UtcNow.AddHours(-1);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ApplyScheduledPlanChangesAsync();

        // Assert
        await _subscriptionService.Received(1).ApplyScheduledChangesAsync(subscription.Id);
    }

    [Fact]
    public async Task ApplyScheduledPlanChangesAsync_WithFutureChange_ShouldNotApply()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateSubscription();
        subscription.ScheduledPlanCode = "professional";
        subscription.ScheduledChangeDate = DateTime.UtcNow.AddDays(7);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ApplyScheduledPlanChangesAsync();

        // Assert
        await _subscriptionService.DidNotReceive().ApplyScheduledChangesAsync(Arg.Any<Guid>());
    }

    #endregion

    #region ProcessGracePeriodExpirationsAsync Tests

    [Fact]
    public async Task ProcessGracePeriodExpirationsAsync_WithExpiredGracePeriod_ShouldExpire()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, status: SubscriptionStatus.PastDue);
        subscription.GracePeriodEndAt = DateTime.UtcNow.AddHours(-1);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessGracePeriodExpirationsAsync();

        // Assert
        var updatedSubscription = await _dbContext.BillingSubscriptions.FindAsync(subscription.Id);
        updatedSubscription!.Status.Should().Be(SubscriptionStatus.Expired);
        updatedSubscription.EndedAt.Should().NotBeNull();

        var updatedOrg = await _dbContext.Organizations.FindAsync(orgId);
        updatedOrg!.SubscriptionStatus.Should().Be("expired");
    }

    [Fact]
    public async Task ProcessGracePeriodExpirationsAsync_WithActiveGracePeriod_ShouldNotExpire()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateSubscription(status: SubscriptionStatus.PastDue);
        subscription.GracePeriodEndAt = DateTime.UtcNow.AddDays(5);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessGracePeriodExpirationsAsync();

        // Assert
        var updatedSubscription = await _dbContext.BillingSubscriptions.FindAsync(subscription.Id);
        updatedSubscription!.Status.Should().Be(SubscriptionStatus.PastDue);
    }

    #endregion

    #region ResetMonthlyUsageAsync Tests

    [Fact]
    public async Task ResetMonthlyUsageAsync_ShouldResetUsageForActiveSubscriptions()
    {
        // Arrange
        var sub1 = BillingTestFixture.CreateSubscription(status: SubscriptionStatus.Active);
        var sub2 = BillingTestFixture.CreateTrialSubscription();
        var sub3 = BillingTestFixture.CreateSubscription(status: SubscriptionStatus.Cancelled);

        _dbContext.BillingSubscriptions.AddRange(sub1, sub2, sub3);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ResetMonthlyUsageAsync();

        // Assert
        await _usageService.Received(1).ResetUsageForPeriodAsync(
            sub1.OrganizationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
        await _usageService.Received(1).ResetUsageForPeriodAsync(
            sub2.OrganizationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
        await _usageService.DidNotReceive().ResetUsageForPeriodAsync(
            sub3.OrganizationId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    #endregion

    #region SendUsageWarningsAsync Tests

    [Fact]
    public async Task SendUsageWarningsAsync_At80Percent_ShouldSend80PercentWarning()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.NoticesPerMonth = 100;
        _dbContext.SubscriptionPlans.Add(plan);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.Active);
        subscription.Plan = plan;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var usageRecord = BillingTestFixture.CreateUsageRecord(organizationId: orgId, noticesCount: 85);
        _usageService.GetCurrentUsageAsync(orgId).Returns(usageRecord);

        // Act
        await _sut.SendUsageWarningsAsync();

        // Assert
        await _billingNotificationService.Received(1).SendUsageWarning80Async(
            userId, "notices", 85, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendUsageWarningsAsync_At90Percent_ShouldSend90PercentWarning()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.NoticesPerMonth = 100;
        _dbContext.SubscriptionPlans.Add(plan);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.Active);
        subscription.Plan = plan;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var usageRecord = BillingTestFixture.CreateUsageRecord(organizationId: orgId, noticesCount: 92);
        _usageService.GetCurrentUsageAsync(orgId).Returns(usageRecord);

        // Act
        await _sut.SendUsageWarningsAsync();

        // Assert
        await _billingNotificationService.Received(1).SendUsageWarning90Async(
            userId, "notices", 92, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendUsageWarningsAsync_At100Percent_ShouldSendLimitReached()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.NoticesPerMonth = 100;
        _dbContext.SubscriptionPlans.Add(plan);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.Active);
        subscription.Plan = plan;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var usageRecord = BillingTestFixture.CreateUsageRecord(organizationId: orgId, noticesCount: 100);
        _usageService.GetCurrentUsageAsync(orgId).Returns(usageRecord);

        // Act
        await _sut.SendUsageWarningsAsync();

        // Assert
        await _billingNotificationService.Received(1).SendUsageLimitReachedAsync(
            userId, "notices", 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendUsageWarningsAsync_UnlimitedPlan_ShouldNotSendWarnings()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var plan = BillingTestFixture.CreateEnterprisePlan();
        plan.Limits.NoticesPerMonth = -1; // Unlimited
        _dbContext.SubscriptionPlans.Add(plan);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.Active);
        subscription.Plan = plan;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var usageRecord = BillingTestFixture.CreateUsageRecord(organizationId: orgId, noticesCount: 10000);
        _usageService.GetCurrentUsageAsync(orgId).Returns(usageRecord);

        // Act
        await _sut.SendUsageWarningsAsync();

        // Assert
        await _billingNotificationService.DidNotReceive().SendUsageWarning80Async(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region CleanupWebhookEventsAsync Tests

    [Fact]
    public async Task CleanupWebhookEventsAsync_ShouldDeleteOldProcessedEvents()
    {
        // Arrange
        var oldEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            EventId = "evt_old",
            EventType = "payment.captured",
            Payload = "{}"
        };
        var recentEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            EventId = "evt_recent",
            EventType = "payment.captured",
            Payload = "{}"
        };
        _dbContext.WebhookEvents.AddRange(oldEvent, recentEvent);
        await _dbContext.SaveChangesAsync();

        // Update CreatedAt after save (since SaveChangesAsync overwrites it)
        // Mark as Modified to allow update
        oldEvent.CreatedAt = DateTime.UtcNow.AddDays(-100);
        oldEvent.Status = WebhookEventStatus.Processed;
        recentEvent.CreatedAt = DateTime.UtcNow.AddDays(-30);
        recentEvent.Status = WebhookEventStatus.Processed;
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.CleanupWebhookEventsAsync();

        // Assert
        var events = await _dbContext.WebhookEvents.ToListAsync();
        events.Should().HaveCount(1);
        events.First().EventId.Should().Be("evt_recent");
    }

    #endregion

    #region RetryFailedWebhookEventsAsync Tests

    [Fact]
    public async Task RetryFailedWebhookEventsAsync_ShouldRetryFailedEvents()
    {
        // Arrange
        var failedEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            EventId = "evt_failed",
            EventType = "payment.failed",
            Payload = "{}", // Invalid payload (no event type) - will fail processing
            Status = WebhookEventStatus.Failed,
            AttemptCount = 2,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        _dbContext.WebhookEvents.Add(failedEvent);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.RetryFailedWebhookEventsAsync();

        // Assert - Event should be retried (attempt count increased) and remain failed due to invalid payload
        var updatedEvent = await _dbContext.WebhookEvents.FindAsync(failedEvent.Id);
        updatedEvent!.Status.Should().Be(WebhookEventStatus.Failed);
        updatedEvent.AttemptCount.Should().Be(3);
        updatedEvent.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RetryFailedWebhookEventsAsync_MaxAttempts_ShouldNotRequeue()
    {
        // Arrange
        var failedEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            EventId = "evt_maxed",
            EventType = "payment.failed",
            Payload = "{}",
            Status = WebhookEventStatus.Failed,
            AttemptCount = 5,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        _dbContext.WebhookEvents.Add(failedEvent);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.RetryFailedWebhookEventsAsync();

        // Assert
        var updatedEvent = await _dbContext.WebhookEvents.FindAsync(failedEvent.Id);
        updatedEvent!.Status.Should().Be(WebhookEventStatus.Failed);
        updatedEvent.AttemptCount.Should().Be(5);
    }

    #endregion

    #region SendTrialEndingNotificationsAsync Tests

    [Fact]
    public async Task SendTrialEndingNotificationsAsync_ThreeDaysBefore_ShouldSendNotification()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trialEnd = DateTime.UtcNow.AddDays(3).Date;

        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);

        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateTrialSubscription(organizationId: orgId, planId: plan.Id);
        subscription.TrialEnd = trialEnd.AddHours(12);
        subscription.Plan = plan;
        subscription.Organization = org;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SendTrialEndingNotificationsAsync();

        // Assert
        await _billingNotificationService.Received(1).SendTrialEndingAsync(
            userId, Arg.Any<string>(), Arg.Any<DateTime>(), 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTrialEndingNotificationsAsync_OneDayBefore_ShouldSendNotification()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var trialEnd = DateTime.UtcNow.AddDays(1).Date;

        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);

        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateTrialSubscription(organizationId: orgId, planId: plan.Id);
        subscription.TrialEnd = trialEnd.AddHours(12);
        subscription.Plan = plan;
        subscription.Organization = org;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SendTrialEndingNotificationsAsync();

        // Assert
        await _billingNotificationService.Received(1).SendTrialEndingAsync(
            userId, Arg.Any<string>(), Arg.Any<DateTime>(), 1, Arg.Any<CancellationToken>());
    }

    #endregion

    #region SendRenewalRemindersAsync Tests

    [Fact]
    public async Task SendRenewalRemindersAsync_SevenDaysBefore_ShouldSendReminder()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var renewalDate = DateTime.UtcNow.AddDays(7).Date;

        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);

        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.Active);
        subscription.CurrentPeriodEnd = renewalDate;
        subscription.CancelAtPeriodEnd = false;
        subscription.Plan = plan;
        subscription.Organization = org;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SendRenewalRemindersAsync();

        // Assert
        await _billingNotificationService.Received(1).SendRenewalReminderAsync(
            userId, Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendRenewalRemindersAsync_CancelledSubscription_ShouldNotSendReminder()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var renewalDate = DateTime.UtcNow.AddDays(7).Date;

        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);

        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.Active);
        subscription.CurrentPeriodEnd = renewalDate;
        subscription.CancelAtPeriodEnd = true; // Will be cancelled
        subscription.Plan = plan;
        subscription.Organization = org;
        _dbContext.BillingSubscriptions.Add(subscription);

        var user = BillingTestFixture.CreateUser(organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SendRenewalRemindersAsync();

        // Assert
        await _billingNotificationService.DidNotReceive().SendRenewalReminderAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region ProcessPaymentRetriesAsync Tests

    [Fact]
    public async Task ProcessPaymentRetriesAsync_WithValidPaymentMethod_ShouldAttemptRetry()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);

        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.PastDue);
        subscription.PaymentRetryCount = 1;
        subscription.NextPaymentRetryAt = DateTime.UtcNow.AddHours(-1);
        subscription.Plan = plan;
        subscription.Organization = org;
        subscription.RazorpaySubscriptionId = "sub_123"; // Has razorpay sub
        _dbContext.BillingSubscriptions.Add(subscription);

        var paymentMethod = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: true);
        paymentMethod.RazorpayTokenId = "token_123";
        _dbContext.PaymentMethods.Add(paymentMethod);

        var user = BillingTestFixture.CreateUser(id: userId, organizationId: orgId, role: "owner");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _razorpayService.CreateOrderAsync(Arg.Any<CreateOrderRequest>())
            .Returns(new RazorpayOrderDto("order_123", 99900, "INR", "receipt_123", "rzp_key"));

        // Act
        await _sut.ProcessPaymentRetriesAsync();

        // Assert
        var updatedSubscription = await _dbContext.BillingSubscriptions.FindAsync(subscription.Id);
        updatedSubscription!.Status.Should().Be(SubscriptionStatus.Active);
        updatedSubscription.PaymentRetryCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessPaymentRetriesAsync_WithoutPaymentMethod_ShouldSkip()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);

        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId, planId: plan.Id, status: SubscriptionStatus.PastDue);
        subscription.PaymentRetryCount = 1;
        subscription.NextPaymentRetryAt = null;
        subscription.Plan = plan;
        subscription.Organization = org;
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessPaymentRetriesAsync();

        // Assert
        await _razorpayService.DidNotReceive().CreateOrderAsync(Arg.Any<CreateOrderRequest>());
    }

    [Fact]
    public async Task ProcessPaymentRetriesAsync_MaxRetriesReached_ShouldNotRetry()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateSubscription(status: SubscriptionStatus.PastDue);
        subscription.PaymentRetryCount = 3;
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ProcessPaymentRetriesAsync();

        // Assert
        await _razorpayService.DidNotReceive().CreateOrderAsync(Arg.Any<CreateOrderRequest>());
    }

    #endregion
}
