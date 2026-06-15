using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class SubscriptionServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPlanService _planService;
    private readonly IUsageService _usageService;
    private readonly IRazorpayService _razorpayService;
    private readonly ICouponService _couponService;
    private readonly IInvoiceService _invoiceService;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly SubscriptionService _sut;

    public SubscriptionServiceTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _planService = Substitute.For<IPlanService>();
        _usageService = Substitute.For<IUsageService>();
        _razorpayService = Substitute.For<IRazorpayService>();
        _couponService = Substitute.For<ICouponService>();
        _invoiceService = Substitute.For<IInvoiceService>();
        _logger = Substitute.For<ILogger<SubscriptionService>>();

        _sut = new SubscriptionService(
            _dbContext,
            _planService,
            _usageService,
            _razorpayService,
            _couponService,
            _invoiceService,
            _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetCurrentSubscriptionAsync Tests

    [Fact]
    public async Task GetCurrentSubscriptionAsync_WithExistingSubscription_ShouldReturnSubscription()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        _planService.GetPlanByIdAsync(plan.Id).Returns(plan);
        _usageService.GetCurrentUsageAsync(org.Id).Returns(BillingTestFixture.CreateUsageRecord(org.Id));

        // Act
        var result = await _sut.GetCurrentSubscriptionAsync(org.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Subscription.PlanCode.Should().Be("starter");
        result.Subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task GetCurrentSubscriptionAsync_WithNoSubscription_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetCurrentSubscriptionAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetSubscriptionEntityAsync Tests

    [Fact]
    public async Task GetSubscriptionEntityAsync_WithExistingSubscription_ShouldReturnEntity()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetSubscriptionEntityAsync(org.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(subscription.Id);
    }

    #endregion

    #region StartTrialAsync Tests

    [Fact]
    public async Task StartTrialAsync_ShouldCreateTrialSubscription()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        _planService.GetPlanByCodeAsync("starter").Returns(plan);

        // Act
        var result = await _sut.StartTrialAsync(org.Id, "starter", 14);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(SubscriptionStatus.Trialing);
        result.TrialEnd.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task StartTrialAsync_WithInvalidPlan_ShouldThrow()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        _dbContext.Organizations.Add(org);
        await _dbContext.SaveChangesAsync();

        _planService.GetPlanByCodeAsync("nonexistent").Returns((SubscriptionPlan?)null);

        // Act
        var act = () => _sut.StartTrialAsync(org.Id, "nonexistent", 14);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region ExpireTrialAsync Tests

    [Fact]
    public async Task ExpireTrialAsync_ShouldSetStatusToExpired()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateTrialSubscription(trialDaysRemaining: -1);
        var org = BillingTestFixture.CreateOrganization(subscription.OrganizationId);

        _dbContext.Organizations.Add(org);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.ExpireTrialAsync(subscription.Id);

        // Assert
        var updatedSubscription = await _dbContext.BillingSubscriptions.FindAsync(subscription.Id);
        updatedSubscription!.Status.Should().Be(SubscriptionStatus.Expired);
        updatedSubscription.EndedAt.Should().NotBeNull();
    }

    #endregion

    #region HandlePaymentFailureAsync Tests

    [Fact]
    public async Task HandlePaymentFailureAsync_ShouldSetStatusToPastDue()
    {
        // Arrange - Service only sets PastDue after 3 failed attempts
        var subscription = BillingTestFixture.CreateSubscription();
        subscription.FailedPaymentAttempts = 2; // Start with 2 failures, next one will trigger PastDue
        var org = BillingTestFixture.CreateOrganization(subscription.OrganizationId);

        _dbContext.Organizations.Add(org);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act - Third failure should trigger PastDue
        await _sut.HandlePaymentFailureAsync(subscription.Id, "Card declined");

        // Assert
        var updatedSubscription = await _dbContext.BillingSubscriptions.FindAsync(subscription.Id);
        updatedSubscription!.Status.Should().Be(SubscriptionStatus.PastDue);
        updatedSubscription.FailedPaymentAttempts.Should().Be(3);
        updatedSubscription.LastPaymentFailedAt.Should().NotBeNull();
    }

    #endregion

    #region GetByRazorpayIdAsync Tests

    [Fact]
    public async Task GetByRazorpayIdAsync_WithValidId_ShouldReturnSubscription()
    {
        // Arrange
        var subscription = BillingTestFixture.CreateSubscription();
        subscription.RazorpaySubscriptionId = "sub_test123";
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByRazorpayIdAsync("sub_test123");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(subscription.Id);
    }

    [Fact]
    public async Task GetByRazorpayIdAsync_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetByRazorpayIdAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
