using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class CouponServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPlanService _planService;
    private readonly ILogger<CouponService> _logger;
    private readonly CouponService _sut;

    public CouponServiceTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _planService = Substitute.For<IPlanService>();
        _logger = Substitute.For<ILogger<CouponService>>();

        _sut = new CouponService(_dbContext, _planService, _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region ValidateCouponAsync Tests

    [Fact]
    public async Task ValidateCouponAsync_WithInvalidCode_ShouldReturnInvalid()
    {
        // Act
        var result = await _sut.ValidateCouponAsync("INVALID", "starter", "monthly");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Invalid coupon code");
    }

    [Fact]
    public async Task ValidateCouponAsync_WithInactiveCoupon_ShouldReturnInvalid()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon(isActive: false);
        coupon.IsActive = true; // CreateCoupon respects isActive param
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Mark as inactive
        coupon.IsActive = false;
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly");

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateCouponAsync_WithNotYetValidCoupon_ShouldReturnInvalid()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon();
        coupon.ValidFrom = DateTime.UtcNow.AddDays(1);
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("This coupon is not yet valid");
    }

    [Fact]
    public async Task ValidateCouponAsync_WithExpiredCoupon_ShouldReturnInvalid()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon();
        coupon.ValidUntil = DateTime.UtcNow.AddDays(-1);
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("This coupon has expired");
    }

    [Fact]
    public async Task ValidateCouponAsync_WithMaxRedemptionsReached_ShouldReturnInvalid()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon();
        coupon.MaxRedemptions = 5;
        coupon.TimesRedeemed = 5;
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("This coupon has reached its maximum redemptions");
    }

    [Fact]
    public async Task ValidateCouponAsync_WithInvalidPlan_ShouldReturnInvalid()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon();
        coupon.ApplicablePlans = new List<string> { "professional" };
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("This coupon is not valid for the selected plan");
    }

    [Fact]
    public async Task ValidateCouponAsync_WithInvalidBillingCycle_ShouldReturnInvalid()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon();
        coupon.ApplicablePlans = new List<string> { "*" };
        coupon.ApplicableCycles = new List<string> { "annually" };
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("This coupon is not valid for the selected billing cycle");
    }

    [Fact]
    public async Task ValidateCouponAsync_WhenAlreadyRedeemed_ShouldReturnInvalid()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var coupon = BillingTestFixture.CreateCoupon();
        coupon.ApplicablePlans = new List<string> { "*" };
        coupon.ApplicableCycles = new List<string> { "*" };
        _dbContext.Coupons.Add(coupon);

        var redemption = new CouponRedemption
        {
            CouponId = coupon.Id,
            OrganizationId = orgId,
            DiscountApplied = 1000,
            OriginalAmount = 10000,
            FinalAmount = 9000,
            RedeemedAt = DateTime.UtcNow
        };
        _dbContext.CouponRedemptions.Add(redemption);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly", orgId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("You have already used this coupon");
    }

    [Fact]
    public async Task ValidateCouponAsync_FirstTimeOnlyWithExistingSubscription_ShouldReturnInvalid()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var coupon = BillingTestFixture.CreateCoupon();
        coupon.ApplicablePlans = new List<string> { "*" };
        coupon.ApplicableCycles = new List<string> { "*" };
        coupon.FirstTimeOnly = true;
        _dbContext.Coupons.Add(coupon);

        var subscription = BillingTestFixture.CreateSubscription(organizationId: orgId);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly", orgId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("This coupon is only valid for first-time subscribers");
    }

    [Fact]
    public async Task ValidateCouponAsync_WithValidCoupon_ShouldReturnValid()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon(discountValue: 20);
        coupon.ApplicablePlans = new List<string> { "*" };
        coupon.ApplicableCycles = new List<string> { "*" };
        _dbContext.Coupons.Add(coupon);

        var plan = BillingTestFixture.CreateStarterPlan();
        _dbContext.SubscriptionPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        _planService.GetPlanByCodeAsync("starter").Returns(plan);

        // Act
        var result = await _sut.ValidateCouponAsync(coupon.Code, "starter", "monthly");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Coupon.Should().NotBeNull();
        result.Coupon!.Code.Should().Be(coupon.Code);
    }

    #endregion

    #region CalculateDiscount Tests

    [Fact]
    public void CalculateDiscount_PercentDiscount_ShouldCalculateCorrectly()
    {
        // Arrange
        var coupon = new Coupon
        {
            DiscountType = CouponDiscountType.Percent,
            DiscountValue = 20
        };

        // Act
        var discount = _sut.CalculateDiscount(coupon, 10000);

        // Assert
        discount.Should().Be(2000); // 20% of 10000
    }

    [Fact]
    public void CalculateDiscount_FixedDiscount_ShouldReturnFixedAmount()
    {
        // Arrange
        var coupon = new Coupon
        {
            DiscountType = CouponDiscountType.Fixed,
            DiscountValue = 5000
        };

        // Act
        var discount = _sut.CalculateDiscount(coupon, 10000);

        // Assert
        discount.Should().Be(5000);
    }

    [Fact]
    public void CalculateDiscount_WithMaxDiscountCap_ShouldRespectCap()
    {
        // Arrange
        var coupon = new Coupon
        {
            DiscountType = CouponDiscountType.Percent,
            DiscountValue = 50,
            MaxDiscountAmount = 2000
        };

        // Act
        var discount = _sut.CalculateDiscount(coupon, 10000);

        // Assert
        discount.Should().Be(2000); // 50% would be 5000, but capped at 2000
    }

    [Fact]
    public void CalculateDiscount_BelowMinPurchase_ShouldReturnZero()
    {
        // Arrange
        var coupon = new Coupon
        {
            DiscountType = CouponDiscountType.Percent,
            DiscountValue = 20,
            MinPurchaseAmount = 15000
        };

        // Act
        var discount = _sut.CalculateDiscount(coupon, 10000);

        // Assert
        discount.Should().Be(0);
    }

    [Fact]
    public void CalculateDiscount_DiscountExceedsPurchase_ShouldReturnPurchaseAmount()
    {
        // Arrange
        var coupon = new Coupon
        {
            DiscountType = CouponDiscountType.Fixed,
            DiscountValue = 15000
        };

        // Act
        var discount = _sut.CalculateDiscount(coupon, 10000);

        // Assert
        discount.Should().Be(10000); // Cannot exceed purchase amount
    }

    #endregion

    #region RedeemCouponAsync Tests

    [Fact]
    public async Task RedeemCouponAsync_WithValidCoupon_ShouldCreateRedemption()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var coupon = BillingTestFixture.CreateCoupon();
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var redemption = await _sut.RedeemCouponAsync(
            coupon.Id, orgId, null, null, 1000, 10000, 9000);

        // Assert
        redemption.Should().NotBeNull();
        redemption.CouponId.Should().Be(coupon.Id);
        redemption.OrganizationId.Should().Be(orgId);
        redemption.DiscountApplied.Should().Be(1000);

        var updatedCoupon = await _dbContext.Coupons.FindAsync(coupon.Id);
        updatedCoupon!.TimesRedeemed.Should().Be(1);
    }

    [Fact]
    public async Task RedeemCouponAsync_WhenAlreadyRedeemed_ShouldThrow()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var coupon = BillingTestFixture.CreateCoupon();
        _dbContext.Coupons.Add(coupon);

        var existingRedemption = new CouponRedemption
        {
            CouponId = coupon.Id,
            OrganizationId = orgId,
            DiscountApplied = 1000,
            OriginalAmount = 10000,
            FinalAmount = 9000,
            RedeemedAt = DateTime.UtcNow
        };
        _dbContext.CouponRedemptions.Add(existingRedemption);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = () => _sut.RedeemCouponAsync(coupon.Id, orgId, null, null, 1000, 10000, 9000);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Coupon already redeemed by this organization");
    }

    [Fact]
    public async Task RedeemCouponAsync_WithInvalidCouponId_ShouldThrow()
    {
        // Act
        var act = () => _sut.RedeemCouponAsync(
            Guid.NewGuid(), Guid.NewGuid(), null, null, 1000, 10000, 9000);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Coupon not found");
    }

    #endregion

    #region CreateCouponAsync Tests

    [Fact]
    public async Task CreateCouponAsync_WithValidData_ShouldCreateCoupon()
    {
        // Act
        var coupon = await _sut.CreateCouponAsync(
            code: "NEWCODE20",
            discountType: CouponDiscountType.Percent,
            discountValue: 20,
            maxDiscountAmount: 5000,
            description: "Test coupon");

        // Assert
        coupon.Should().NotBeNull();
        coupon.Code.Should().Be("NEWCODE20");
        coupon.DiscountType.Should().Be(CouponDiscountType.Percent);
        coupon.DiscountValue.Should().Be(20);
        coupon.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCouponAsync_WithDuplicateCode_ShouldThrow()
    {
        // Arrange
        var existingCoupon = BillingTestFixture.CreateCoupon(code: "DUPLICATE");
        _dbContext.Coupons.Add(existingCoupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = () => _sut.CreateCouponAsync(
            code: "duplicate",
            discountType: CouponDiscountType.Percent,
            discountValue: 10);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Coupon code 'duplicate' already exists");
    }

    #endregion

    #region DeactivateCouponAsync Tests

    [Fact]
    public async Task DeactivateCouponAsync_WithValidId_ShouldDeactivate()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon();
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeactivateCouponAsync(coupon.Id);

        // Assert
        var deactivatedCoupon = await _dbContext.Coupons.FindAsync(coupon.Id);
        deactivatedCoupon!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateCouponAsync_WithInvalidId_ShouldNotThrow()
    {
        // Act
        var act = () => _sut.DeactivateCouponAsync(Guid.NewGuid());

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetCouponByCodeAsync Tests

    [Fact]
    public async Task GetCouponByCodeAsync_WithValidCode_ShouldReturnCoupon()
    {
        // Arrange
        var coupon = BillingTestFixture.CreateCoupon(code: "TESTCODE");
        _dbContext.Coupons.Add(coupon);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetCouponByCodeAsync("testcode"); // Case insensitive

        // Assert
        result.Should().NotBeNull();
        result!.Code.Should().Be("TESTCODE");
    }

    [Fact]
    public async Task GetCouponByCodeAsync_WithInvalidCode_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetCouponByCodeAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
