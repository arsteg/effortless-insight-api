using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class PaymentMethodServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IRazorpayService _razorpayService;
    private readonly ILogger<PaymentMethodService> _logger;
    private readonly PaymentMethodService _sut;

    public PaymentMethodServiceTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _razorpayService = Substitute.For<IRazorpayService>();
        _logger = Substitute.For<ILogger<PaymentMethodService>>();

        _sut = new PaymentMethodService(_dbContext, _razorpayService, _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetPaymentMethodsAsync Tests

    [Fact]
    public async Task GetPaymentMethodsAsync_WithNoMethods_ShouldReturnEmptyList()
    {
        // Act
        var result = await _sut.GetPaymentMethodsAsync(Guid.NewGuid());

        // Assert
        result.PaymentMethods.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ShouldReturnOnlyActiveAndOrgMethods()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();

        var pm1 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId);
        var pm2 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId);
        pm2.IsActive = false;
        var pm3 = BillingTestFixture.CreatePaymentMethod(organizationId: otherOrgId);

        _dbContext.PaymentMethods.AddRange(pm1, pm2, pm3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPaymentMethodsAsync(orgId);

        // Assert
        result.PaymentMethods.Should().HaveCount(1);
        result.PaymentMethods.First().Id.Should().Be(pm1.Id);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ShouldOrderByDefaultThenLastUsed()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        var pm1 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: false);
        pm1.LastUsedAt = DateTime.UtcNow.AddDays(-1);

        var pm2 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: true);
        pm2.LastUsedAt = DateTime.UtcNow.AddDays(-3);

        var pm3 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: false);
        pm3.LastUsedAt = DateTime.UtcNow;

        _dbContext.PaymentMethods.AddRange(pm1, pm2, pm3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPaymentMethodsAsync(orgId);

        // Assert
        result.PaymentMethods.Should().HaveCount(3);
        result.PaymentMethods[0].Id.Should().Be(pm2.Id); // Default first
        result.PaymentMethods[1].Id.Should().Be(pm3.Id); // Then by last used
        result.PaymentMethods[2].Id.Should().Be(pm1.Id);
    }

    #endregion

    #region GetPaymentMethodAsync Tests

    [Fact]
    public async Task GetPaymentMethodAsync_WithValidId_ShouldReturnMethod()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var pm = BillingTestFixture.CreatePaymentMethod(organizationId: orgId);
        _dbContext.PaymentMethods.Add(pm);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPaymentMethodAsync(orgId, pm.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(pm.Id);
    }

    [Fact]
    public async Task GetPaymentMethodAsync_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetPaymentMethodAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPaymentMethodAsync_WithInactiveMethod_ShouldReturnNull()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var pm = BillingTestFixture.CreatePaymentMethod(organizationId: orgId);
        pm.IsActive = false;
        _dbContext.PaymentMethods.Add(pm);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPaymentMethodAsync(orgId, pm.Id);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPaymentMethodAsync_WithWrongOrg_ShouldReturnNull()
    {
        // Arrange
        var pm = BillingTestFixture.CreatePaymentMethod(organizationId: Guid.NewGuid());
        _dbContext.PaymentMethods.Add(pm);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPaymentMethodAsync(Guid.NewGuid(), pm.Id);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetDefaultPaymentMethodAsync Tests

    [Fact]
    public async Task GetDefaultPaymentMethodAsync_WithDefault_ShouldReturnDefault()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var pm1 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: false);
        var pm2 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: true);
        _dbContext.PaymentMethods.AddRange(pm1, pm2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetDefaultPaymentMethodAsync(orgId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(pm2.Id);
        result.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task GetDefaultPaymentMethodAsync_WithNoDefault_ShouldReturnNull()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var pm = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: false);
        _dbContext.PaymentMethods.Add(pm);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetDefaultPaymentMethodAsync(orgId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region SetDefaultPaymentMethodAsync Tests

    [Fact]
    public async Task SetDefaultPaymentMethodAsync_ShouldSetNewDefault()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var pm1 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: true);
        var pm2 = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: false);
        _dbContext.PaymentMethods.AddRange(pm1, pm2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.SetDefaultPaymentMethodAsync(orgId, pm2.Id);

        // Assert
        result.IsDefault.Should().BeTrue();

        var updatedPm1 = await _dbContext.PaymentMethods.FindAsync(pm1.Id);
        updatedPm1!.IsDefault.Should().BeFalse();

        var updatedPm2 = await _dbContext.PaymentMethods.FindAsync(pm2.Id);
        updatedPm2!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SetDefaultPaymentMethodAsync_WithInvalidId_ShouldThrow()
    {
        // Act
        var act = () => _sut.SetDefaultPaymentMethodAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Payment method not found");
    }

    #endregion

    #region DeletePaymentMethodAsync Tests

    [Fact]
    public async Task DeletePaymentMethodAsync_WithValidId_ShouldSoftDelete()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var pm = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: false);
        _dbContext.PaymentMethods.Add(pm);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeletePaymentMethodAsync(orgId, pm.Id);

        // Assert
        result.Should().BeTrue();

        var deletedPm = await _dbContext.PaymentMethods.FindAsync(pm.Id);
        deletedPm!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePaymentMethodAsync_DefaultMethod_ShouldThrow()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var pm = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: true);
        _dbContext.PaymentMethods.Add(pm);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = () => _sut.DeletePaymentMethodAsync(orgId, pm.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot delete default payment method*");
    }

    [Fact]
    public async Task DeletePaymentMethodAsync_WithInvalidId_ShouldReturnFalse()
    {
        // Act
        var result = await _sut.DeletePaymentMethodAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CreateFromRazorpayAsync Tests

    [Fact]
    public async Task CreateFromRazorpayAsync_Card_ShouldCreatePaymentMethod()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var paymentId = "pay_123456";
        var customerId = "cust_123456";

        _razorpayService.GetPaymentAsync(paymentId).Returns(new PaymentResult
        {
            Status = "captured",
            PaymentId = paymentId,
            Method = "card",
            TokenId = "token_123",
            Card = new CardDetails
            {
                Last4 = "4242",
                Network = "visa",
                ExpiryMonth = 12,
                ExpiryYear = 2028,
                Name = "Test User",
                Type = "credit"
            }
        });

        // Act
        var result = await _sut.CreateFromRazorpayAsync(orgId, paymentId, customerId);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(PaymentMethodType.Card);
        result.CardLast4.Should().Be("4242");
        result.CardBrand.Should().Be("visa");
        result.IsDefault.Should().BeTrue(); // First method is default
    }

    [Fact]
    public async Task CreateFromRazorpayAsync_Upi_ShouldCreatePaymentMethod()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var paymentId = "pay_123456";
        var customerId = "cust_123456";

        _razorpayService.GetPaymentAsync(paymentId).Returns(new PaymentResult
        {
            Status = "captured",
            PaymentId = paymentId,
            Method = "upi",
            Vpa = "test@upi"
        });

        // Act
        var result = await _sut.CreateFromRazorpayAsync(orgId, paymentId, customerId);

        // Assert
        result.Type.Should().Be(PaymentMethodType.Upi);
        result.UpiId.Should().Be("test@upi");
    }

    [Fact]
    public async Task CreateFromRazorpayAsync_SetAsDefault_ShouldUnsetOthers()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var existingPm = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: true);
        _dbContext.PaymentMethods.Add(existingPm);
        await _dbContext.SaveChangesAsync();

        _razorpayService.GetPaymentAsync(Arg.Any<string>()).Returns(new PaymentResult
        {
            Status = "captured",
            PaymentId = "pay_123456",
            Method = "card",
            Card = new CardDetails { Last4 = "1234", Network = "mastercard" }
        });

        // Act
        var result = await _sut.CreateFromRazorpayAsync(orgId, "pay_123456", "cust_123456", setAsDefault: true);

        // Assert
        result.IsDefault.Should().BeTrue();

        var oldDefault = await _dbContext.PaymentMethods.FindAsync(existingPm.Id);
        oldDefault!.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task CreateFromRazorpayAsync_NotFirstMethod_ShouldNotBeDefault()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var existingPm = BillingTestFixture.CreatePaymentMethod(organizationId: orgId, isDefault: true);
        _dbContext.PaymentMethods.Add(existingPm);
        await _dbContext.SaveChangesAsync();

        _razorpayService.GetPaymentAsync(Arg.Any<string>()).Returns(new PaymentResult
        {
            Status = "captured",
            PaymentId = "pay_123456",
            Method = "card",
            Card = new CardDetails { Last4 = "1234" }
        });

        // Act
        var result = await _sut.CreateFromRazorpayAsync(orgId, "pay_123456", "cust_123456", setAsDefault: false);

        // Assert
        result.IsDefault.Should().BeFalse();
    }

    #endregion

    #region UpdateLastUsedAsync Tests

    [Fact]
    public async Task UpdateLastUsedAsync_ShouldUpdateTimestamp()
    {
        // Arrange
        var pm = BillingTestFixture.CreatePaymentMethod();
        pm.LastUsedAt = DateTime.UtcNow.AddDays(-1);
        _dbContext.PaymentMethods.Add(pm);
        await _dbContext.SaveChangesAsync();

        var beforeUpdate = pm.LastUsedAt;

        // Act
        await _sut.UpdateLastUsedAsync(pm.Id);

        // Assert
        var updatedPm = await _dbContext.PaymentMethods.FindAsync(pm.Id);
        updatedPm!.LastUsedAt.Should().BeAfter(beforeUpdate!.Value);
    }

    [Fact]
    public async Task UpdateLastUsedAsync_WithInvalidId_ShouldNotThrow()
    {
        // Act
        var act = () => _sut.UpdateLastUsedAsync(Guid.NewGuid());

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion
}
