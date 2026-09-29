using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Encryption;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EffortlessInsight.Api.Tests.Unit.Services;

/// <summary>
/// Covers the "one active CA per GSTIN at a time" business rule that backs the
/// CA-as-distributor invitation and staging flows.
/// </summary>
public class CaGstinAuthorizationServiceTests
{
    private const string TestGstin = "27AABCU9603R1ZN";

    static CaGstinAuthorizationServiceTests()
    {
        // EncryptedStringConverter (used on OrganizationGstin.Gstin/CaProspectClient.Gstin)
        // reads a static accessor that only application startup normally configures.
        // Configure it once here with the same safe development fallback the real
        // service uses when no key is set, so these tests can run standalone.
        if (!FieldEncryptionServiceAccessor.IsConfigured)
        {
            var configuration = new ConfigurationBuilder().Build();
            FieldEncryptionServiceAccessor.SetInstance(
                new FieldEncryptionService(configuration, NullLogger<FieldEncryptionService>.Instance));
        }
    }

    private static ApplicationDbContext CreateInMemoryDbContext() => BillingTestDbContextFactory.Create();

    [Fact]
    public async Task CheckAsync_NoExistingClaim_ReturnsAllowed()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);

        var result = await service.CheckAsync(TestGstin, Guid.NewGuid());

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_GstinStagedBySameCa_ReturnsAllowed()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);
        var caUserId = Guid.NewGuid();

        dbContext.CaProspectClients.Add(new CaProspectClient
        {
            CaUserId = caUserId,
            Gstin = TestGstin,
            GstinHash = service.ComputeGstinHash(TestGstin),
            Status = "staging"
        });
        await dbContext.SaveChangesAsync();

        var result = await service.CheckAsync(TestGstin, caUserId);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_GstinStagedByDifferentCa_ReturnsBlocked()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);
        var existingCaUserId = Guid.NewGuid();
        var requestingCaUserId = Guid.NewGuid();

        dbContext.CaProspectClients.Add(new CaProspectClient
        {
            CaUserId = existingCaUserId,
            Gstin = TestGstin,
            GstinHash = service.ComputeGstinHash(TestGstin),
            Status = "staging"
        });
        await dbContext.SaveChangesAsync();

        var result = await service.CheckAsync(TestGstin, requestingCaUserId);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("GSTIN_HAS_ACTIVE_CA");
    }

    [Fact]
    public async Task CheckAsync_ProspectClientAlreadyMerged_DoesNotBlockAnotherCa()
    {
        // A merged prospect client means the handoff to a real organization is
        // done - it must not be treated as an ongoing "staging" claim.
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);
        var existingCaUserId = Guid.NewGuid();
        var requestingCaUserId = Guid.NewGuid();

        dbContext.CaProspectClients.Add(new CaProspectClient
        {
            CaUserId = existingCaUserId,
            Gstin = TestGstin,
            GstinHash = service.ComputeGstinHash(TestGstin),
            Status = "merged",
            MergedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var result = await service.CheckAsync(TestGstin, requestingCaUserId);

        result.IsAllowed.Should().BeTrue();
    }

    private async Task<Guid> SeedOrganizationWithPrimaryGstinAsync(ApplicationDbContext dbContext, string gstin)
    {
        var organization = new Organization
        {
            Name = "Test Org " + Guid.NewGuid(),
            NameNormalized = "test org " + Guid.NewGuid(),
            State = "Maharashtra",
            SubscriptionStatus = "none"
        };
        dbContext.Organizations.Add(organization);
        await dbContext.SaveChangesAsync();

        dbContext.OrganizationGstins.Add(new OrganizationGstin
        {
            OrganizationId = organization.Id,
            Gstin = gstin,
            StateCode = "27",
            StateName = "Maharashtra",
            IsPrimary = true,
            Status = "active"
        });
        await dbContext.SaveChangesAsync();

        return organization.Id;
    }

    [Fact]
    public async Task CheckAsync_ActiveCaMembershipOnOrgForSameCa_ReturnsAllowed()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);
        var caUserId = Guid.NewGuid();

        var orgId = await SeedOrganizationWithPrimaryGstinAsync(dbContext, TestGstin);
        dbContext.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = orgId,
            UserId = caUserId,
            Role = "ca",
            IsExternal = true,
            Status = "active"
        });
        await dbContext.SaveChangesAsync();

        var result = await service.CheckAsync(TestGstin, caUserId);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_ActiveCaMembershipOnOrgForDifferentCa_ReturnsBlocked()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);
        var existingCaUserId = Guid.NewGuid();
        var requestingCaUserId = Guid.NewGuid();

        var orgId = await SeedOrganizationWithPrimaryGstinAsync(dbContext, TestGstin);
        dbContext.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = orgId,
            UserId = existingCaUserId,
            Role = "ca",
            IsExternal = true,
            Status = "active"
        });
        await dbContext.SaveChangesAsync();

        var result = await service.CheckAsync(TestGstin, requestingCaUserId);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("GSTIN_HAS_ACTIVE_CA");
    }

    [Fact]
    public async Task CheckAsync_SuspendedCaMembership_DoesNotBlockAnotherCa()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);
        var existingCaUserId = Guid.NewGuid();
        var requestingCaUserId = Guid.NewGuid();

        var orgId = await SeedOrganizationWithPrimaryGstinAsync(dbContext, TestGstin);
        dbContext.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = orgId,
            UserId = existingCaUserId,
            Role = "ca",
            IsExternal = true,
            Status = "suspended"
        });
        await dbContext.SaveChangesAsync();

        var result = await service.CheckAsync(TestGstin, requestingCaUserId);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_ExpiredCaAccess_DoesNotBlockAnotherCa()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);
        var existingCaUserId = Guid.NewGuid();
        var requestingCaUserId = Guid.NewGuid();

        var orgId = await SeedOrganizationWithPrimaryGstinAsync(dbContext, TestGstin);
        dbContext.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = orgId,
            UserId = existingCaUserId,
            Role = "ca",
            IsExternal = true,
            Status = "active",
            AccessExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await dbContext.SaveChangesAsync();

        var result = await service.CheckAsync(TestGstin, requestingCaUserId);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ComputeGstinHash_IsDeterministic_AndCaseInsensitive()
    {
        var dbContext = CreateInMemoryDbContext();
        var service = new CaGstinAuthorizationService(dbContext);

        var hash1 = service.ComputeGstinHash(TestGstin);
        var hash2 = service.ComputeGstinHash(TestGstin.ToLowerInvariant());
        var hash3 = service.ComputeGstinHash("  " + TestGstin + "  ");

        hash1.Should().Be(hash2);
        hash1.Should().Be(hash3);
    }
}
