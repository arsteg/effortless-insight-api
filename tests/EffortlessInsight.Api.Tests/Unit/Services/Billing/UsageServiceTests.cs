using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class UsageServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<UsageService> _logger;
    private readonly UsageService _sut;

    public UsageServiceTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _cache = Substitute.For<IDistributedCache>();
        _logger = Substitute.For<ILogger<UsageService>>();

        _sut = new UsageService(_dbContext, _cache, _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetCurrentUsageAsync_WithExistingRecord_ShouldReturnUsage()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 50);

        _dbContext.Organizations.Add(org);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetCurrentUsageAsync(org.Id);

        // Assert
        result.Should().NotBeNull();
        result!.NoticesCount.Should().Be(50);
    }

    [Fact]
    public async Task GetCurrentUsageAsync_WithNoRecord_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetCurrentUsageAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateCurrentUsageAsync_WithNoRecord_ShouldCreateNew()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetOrCreateCurrentUsageAsync(org.Id);

        // Assert
        result.Should().NotBeNull();
        result.OrganizationId.Should().Be(org.Id);
        result.NoticesCount.Should().Be(0);

        var savedUsage = await _dbContext.UsageRecords.FirstOrDefaultAsync(u => u.OrganizationId == org.Id);
        savedUsage.Should().NotBeNull();
    }

    [Fact]
    public async Task IncrementNoticeCountAsync_ShouldIncreaseCount()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 10);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.IncrementNoticeCountAsync(org.Id);

        // Assert
        var updatedUsage = await _dbContext.UsageRecords.FirstAsync(u => u.OrganizationId == org.Id);
        updatedUsage.NoticesCount.Should().Be(11);
    }

    [Fact]
    public async Task DecrementNoticeCountAsync_ShouldDecreaseCount()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 10);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DecrementNoticeCountAsync(org.Id);

        // Assert
        var updatedUsage = await _dbContext.UsageRecords.FirstAsync(u => u.OrganizationId == org.Id);
        updatedUsage.NoticesCount.Should().Be(9);
    }

    [Fact]
    public async Task DecrementNoticeCountAsync_AtZero_ShouldNotGoBelowZero()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 0);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DecrementNoticeCountAsync(org.Id);

        // Assert
        var updatedUsage = await _dbContext.UsageRecords.FirstAsync(u => u.OrganizationId == org.Id);
        updatedUsage.NoticesCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateUserCountAsync_ShouldSetCount()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, usersCount: 5);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.UpdateUserCountAsync(org.Id, 10);

        // Assert
        var updatedUsage = await _dbContext.UsageRecords.FirstAsync(u => u.OrganizationId == org.Id);
        updatedUsage.UsersCount.Should().Be(10);
    }

    [Fact]
    public async Task UpdateStorageUsageAsync_ShouldAddBytes()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, storageBytes: 1000);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.UpdateStorageUsageAsync(org.Id, 500);

        // Assert
        var updatedUsage = await _dbContext.UsageRecords.FirstAsync(u => u.OrganizationId == org.Id);
        updatedUsage.StorageBytes.Should().Be(1500);
    }

    [Fact]
    public async Task UpdateStorageUsageAsync_WithNegativeChange_ShouldSubtractBytes()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, storageBytes: 1000);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.UpdateStorageUsageAsync(org.Id, -300);

        // Assert
        var updatedUsage = await _dbContext.UsageRecords.FirstAsync(u => u.OrganizationId == org.Id);
        updatedUsage.StorageBytes.Should().Be(700);
    }

    [Fact]
    public async Task CanCreateNoticeAsync_WithinLimit_ShouldReturnTrue()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.NoticesPerMonth = 100;
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 50);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var (canCreate, reason) = await _sut.CanCreateNoticeAsync(org.Id);

        // Assert
        canCreate.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public async Task CanCreateNoticeAsync_AtLimit_ShouldReturnFalse()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.NoticesPerMonth = 100;
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 100);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var (canCreate, reason) = await _sut.CanCreateNoticeAsync(org.Id);

        // Assert
        canCreate.Should().BeFalse();
        reason.Should().Contain("limit");
    }

    [Fact]
    public async Task CanCreateNoticeAsync_WithUnlimitedPlan_ShouldReturnTrue()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateEnterprisePlan();
        plan.Limits.NoticesPerMonth = -1; // Unlimited
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 10000);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var (canCreate, reason) = await _sut.CanCreateNoticeAsync(org.Id);

        // Assert
        canCreate.Should().BeTrue();
    }

    [Fact]
    public async Task CanAddUserAsync_WithinLimit_ShouldReturnTrue()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.Users = 10;
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        subscription.Plan = plan; // Set navigation property for Include query
        subscription.SeatsIncluded = 10; // Match the plan limits
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, usersCount: 5);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var (canAdd, reason) = await _sut.CanAddUserAsync(org.Id);

        // Assert
        canAdd.Should().BeTrue();
    }

    [Fact]
    public async Task CanUploadFileAsync_WithinLimit_ShouldReturnTrue()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.StorageGb = 10; // 10 GB
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, storageBytes: 5L * 1024 * 1024 * 1024); // 5 GB

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var (canUpload, reason) = await _sut.CanUploadFileAsync(org.Id, 1L * 1024 * 1024 * 1024); // 1 GB file

        // Assert
        canUpload.Should().BeTrue();
    }

    [Fact]
    public async Task CanUploadFileAsync_ExceedsLimit_ShouldReturnFalse()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.StorageGb = 10; // 10 GB
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        subscription.Plan = plan; // Set navigation property
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, storageBytes: 9L * 1024 * 1024 * 1024); // 9 GB

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var (canUpload, reason) = await _sut.CanUploadFileAsync(org.Id, 2L * 1024 * 1024 * 1024); // 2 GB file

        // Assert
        canUpload.Should().BeFalse();
        reason.Should().NotBeNull();
        reason!.ToLowerInvariant().Should().Contain("storage");
    }

    [Fact]
    public async Task GetUsagePercentageAsync_ShouldReturnCorrectPercentage()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        plan.Limits.NoticesPerMonth = 100;
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 75);

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        var percentage = await _sut.GetUsagePercentageAsync(org.Id, "notices");

        // Assert
        percentage.Should().Be(75);
    }

    [Fact]
    public async Task ResetUsageForPeriodAsync_ShouldCreateNewRecord()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var usage = BillingTestFixture.CreateUsageRecord(org.Id, noticesCount: 50);

        _dbContext.Organizations.Add(org);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        var newStart = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));
        var newEnd = newStart.AddMonths(1).AddDays(-1);

        // Act
        await _sut.ResetUsageForPeriodAsync(org.Id, newStart, newEnd);

        // Assert
        var newUsage = await _dbContext.UsageRecords
            .FirstOrDefaultAsync(u => u.OrganizationId == org.Id && u.PeriodStart == newStart);

        newUsage.Should().NotBeNull();
        newUsage!.NoticesCount.Should().Be(0);
    }

    [Fact]
    public async Task IncrementApiCallsAsync_ShouldIncreaseCount()
    {
        // Arrange
        var org = BillingTestFixture.CreateOrganization();
        var plan = BillingTestFixture.CreateStarterPlan();
        var subscription = BillingTestFixture.CreateSubscription(org.Id, plan.Id, plan.Code);
        var usage = BillingTestFixture.CreateUsageRecord(org.Id);
        usage.ApiCalls = 100;

        _dbContext.Organizations.Add(org);
        _dbContext.SubscriptionPlans.Add(plan);
        _dbContext.BillingSubscriptions.Add(subscription);
        _dbContext.UsageRecords.Add(usage);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.IncrementApiCallsAsync(org.Id);

        // Assert
        var updatedUsage = await _dbContext.UsageRecords.FirstAsync(u => u.OrganizationId == org.Id);
        updatedUsage.ApiCalls.Should().Be(101);
    }
}
