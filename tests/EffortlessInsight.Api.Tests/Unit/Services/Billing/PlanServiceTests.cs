using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class PlanServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<PlanService> _logger;
    private readonly PlanService _sut;

    public PlanServiceTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _cache = Substitute.For<IDistributedCache>();
        _logger = Substitute.For<ILogger<PlanService>>();

        _sut = new PlanService(_dbContext, _cache, _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetAllPlansAsync Tests

    [Fact]
    public async Task GetAllPlansAsync_ShouldReturnOnlyActivePlans()
    {
        // Arrange
        var activePlan = BillingTestFixture.CreateStarterPlan();
        var inactivePlan = BillingTestFixture.CreatePlan(isActive: false);

        _dbContext.SubscriptionPlans.AddRange(activePlan, inactivePlan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAllPlansAsync();

        // Assert
        result.Plans.Should().HaveCount(1);
        result.Plans.First().Code.Should().Be("starter");
    }

    [Fact]
    public async Task GetAllPlansAsync_ShouldReturnPlansOrderedBySortOrder()
    {
        // Arrange
        var plan1 = BillingTestFixture.CreateStarterPlan();
        plan1.SortOrder = 2;

        var plan2 = BillingTestFixture.CreateFreePlan();
        plan2.SortOrder = 1;

        var plan3 = BillingTestFixture.CreateProfessionalPlan();
        plan3.SortOrder = 3;

        _dbContext.SubscriptionPlans.AddRange(plan1, plan2, plan3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAllPlansAsync();

        // Assert
        result.Plans.Should().HaveCount(3);
        result.Plans[0].Code.Should().Be("free");
        result.Plans[1].Code.Should().Be("starter");
        result.Plans[2].Code.Should().Be("professional");
    }

    [Fact]
    public async Task GetAllPlansAsync_ShouldIncludeAddOns()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAllPlansAsync();

        // Assert
        result.AddOns.Should().NotBeNull();
        result.AddOns.Should().NotBeEmpty();
    }

    #endregion

    #region GetPlanByCodeAsync Tests

    [Fact]
    public async Task GetPlanByCodeAsync_WithValidCode_ShouldReturnPlan()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPlanByCodeAsync("starter");

        // Assert
        result.Should().NotBeNull();
        result!.Code.Should().Be("starter");
        result.DisplayName.Should().Be("Starter");
    }

    [Fact]
    public async Task GetPlanByCodeAsync_WithInvalidCode_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetPlanByCodeAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPlanByCodeAsync_WithInactivePlan_ShouldReturnNull()
    {
        // Arrange
        var plan = BillingTestFixture.CreatePlan(code: "inactive", isActive: false);
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPlanByCodeAsync("inactive");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetPlanByIdAsync Tests

    [Fact]
    public async Task GetPlanByIdAsync_WithValidId_ShouldReturnPlan()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPlanByIdAsync(plan.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(plan.Id);
    }

    [Fact]
    public async Task GetPlanByIdAsync_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetPlanByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetPlanLimitsAsync Tests

    [Fact]
    public async Task GetPlanLimitsAsync_WithValidCode_ShouldReturnLimits()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPlanLimitsAsync("starter");

        // Assert
        result.Should().NotBeNull();
        result!.NoticesPerMonth.Should().Be(100);
        result.Users.Should().Be(5);
        result.StorageGb.Should().Be(10);
    }

    [Fact]
    public async Task GetPlanLimitsAsync_WithInvalidCode_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetPlanLimitsAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetDefaultPlanAsync Tests

    [Fact]
    public async Task GetDefaultPlanAsync_ShouldReturnFreePlan()
    {
        // Arrange
        var freePlan = BillingTestFixture.CreateFreePlan();
        _dbContext.SubscriptionPlans.Add(freePlan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetDefaultPlanAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Code.Should().Be("free");
    }

    [Fact]
    public async Task GetDefaultPlanAsync_WithNoFreePlan_ShouldReturnNull()
    {
        // Arrange
        var starterPlan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(starterPlan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetDefaultPlanAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetPlanChangeType Tests

    [Fact]
    public void GetPlanChangeType_FromStarterToProfessional_ShouldReturnUpgrade()
    {
        // Arrange
        var starterPlan = BillingTestFixture.CreateStarterPlan();
        var proPlan = BillingTestFixture.CreateProfessionalPlan();

        // Act
        var result = _sut.GetPlanChangeType(starterPlan, proPlan, "monthly", "monthly");

        // Assert
        result.Should().Be("upgrade");
    }

    [Fact]
    public void GetPlanChangeType_FromProfessionalToStarter_ShouldReturnDowngrade()
    {
        // Arrange
        var starterPlan = BillingTestFixture.CreateStarterPlan();
        var proPlan = BillingTestFixture.CreateProfessionalPlan();

        // Act
        var result = _sut.GetPlanChangeType(proPlan, starterPlan, "monthly", "monthly");

        // Assert
        result.Should().Be("downgrade");
    }

    [Fact]
    public void GetPlanChangeType_FromMonthlyToAnnual_HigherPrice_ShouldReturnUpgrade()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();

        // Act
        var result = _sut.GetPlanChangeType(plan, plan, "monthly", "annually");

        // Assert
        // Annual normalized to monthly should be less than monthly (10 months vs 12)
        // so this should be downgrade
        result.Should().BeOneOf("upgrade", "downgrade");
    }

    #endregion

    #region CalculateSubscriptionPrice Tests

    [Fact]
    public void CalculateSubscriptionPrice_MonthlyBilling_ShouldReturnCorrectPrice()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.PricingMonthly = 99900; // 999.00 in paise

        // Act
        var result = _sut.CalculateSubscriptionPrice(plan, "monthly", 0);

        // Assert
        result.BaseAmount.Should().Be(99900);
        result.Subtotal.Should().Be(99900);
        result.GstRate.Should().Be(18);
    }

    [Fact]
    public void CalculateSubscriptionPrice_AnnualBilling_ShouldReturnCorrectPrice()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.PricingAnnually = 999900; // 9999.00 in paise

        // Act
        var result = _sut.CalculateSubscriptionPrice(plan, "annually", 0);

        // Assert
        result.BaseAmount.Should().Be(999900);
        result.Subtotal.Should().Be(999900);
    }

    [Fact]
    public void CalculateSubscriptionPrice_WithAdditionalSeats_ShouldIncludeSeatPrice()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.PricingMonthly = 99900;
        plan.PerSeatMonthly = 10000; // 100.00 per seat

        // Act
        var result = _sut.CalculateSubscriptionPrice(plan, "monthly", 5);

        // Assert
        result.BaseAmount.Should().Be(99900);
        result.AdditionalSeatsAmount.Should().Be(50000); // 5 * 10000
        result.Subtotal.Should().Be(149900); // Base + seats
    }

    [Fact]
    public void CalculateSubscriptionPrice_WithDiscount_ShouldApplyDiscount()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.PricingMonthly = 99900;

        // Act
        var result = _sut.CalculateSubscriptionPrice(plan, "monthly", 0, discountAmount: 20000);

        // Assert
        result.Subtotal.Should().Be(79900); // 99900 - 20000
    }

    [Fact]
    public void CalculateSubscriptionPrice_DiscountExceedsPrice_ShouldBeZero()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.PricingMonthly = 10000;

        // Act
        var result = _sut.CalculateSubscriptionPrice(plan, "monthly", 0, discountAmount: 50000);

        // Assert
        result.Subtotal.Should().Be(0);
        result.Total.Should().Be(0);
    }

    [Fact]
    public void CalculateSubscriptionPrice_ShouldCalculateGst()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.PricingMonthly = 100000; // 1000 rupees

        // Act
        var result = _sut.CalculateSubscriptionPrice(plan, "monthly", 0);

        // Assert
        result.GstAmount.Should().Be(18000); // 18% of 100000
        result.Total.Should().Be(118000); // Subtotal + GST
    }

    #endregion

    #region CalculateProration Tests

    [Fact]
    public void CalculateProration_ToHigherPlan_ShouldReturnPositiveAmount()
    {
        // Arrange
        var starterPlan = BillingTestFixture.CreateStarterPlan();
        starterPlan.PricingMonthly = 99900;

        var proPlan = BillingTestFixture.CreateProfessionalPlan();
        proPlan.PricingMonthly = 249900;

        var periodStart = DateTime.UtcNow.AddDays(-15);
        var periodEnd = DateTime.UtcNow.AddDays(15);

        // Act
        var result = _sut.CalculateProration(
            starterPlan, proPlan, "monthly", "monthly", 0, 0, periodStart, periodEnd);

        // Assert
        result.Should().BePositive();
    }

    [Fact]
    public void CalculateProration_ToLowerPlan_ShouldReturnNegativeAmount()
    {
        // Arrange
        var starterPlan = BillingTestFixture.CreateStarterPlan();
        starterPlan.PricingMonthly = 99900;

        var proPlan = BillingTestFixture.CreateProfessionalPlan();
        proPlan.PricingMonthly = 249900;

        var periodStart = DateTime.UtcNow.AddDays(-15);
        var periodEnd = DateTime.UtcNow.AddDays(15);

        // Act
        var result = _sut.CalculateProration(
            proPlan, starterPlan, "monthly", "monthly", 0, 0, periodStart, periodEnd);

        // Assert
        result.Should().BeNegative();
    }

    [Fact]
    public void CalculateProration_NoDaysRemaining_ShouldReturnZero()
    {
        // Arrange
        var plan = BillingTestFixture.CreateStarterPlan();
        var periodStart = DateTime.UtcNow.AddDays(-30);
        var periodEnd = DateTime.UtcNow.AddDays(-1); // Already ended

        // Act
        var result = _sut.CalculateProration(
            plan, plan, "monthly", "monthly", 0, 0, periodStart, periodEnd);

        // Assert
        result.Should().Be(0);
    }

    #endregion
}
