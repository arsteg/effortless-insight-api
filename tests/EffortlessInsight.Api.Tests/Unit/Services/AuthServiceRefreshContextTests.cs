using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Services.Email;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Tests.Unit.Services;

/// <summary>
/// Refreshing a token must reproduce the context the session was opened under.
///
/// It used to guess, taking the user's most recently joined membership, which silently
/// moved anyone with more than one organization - and any CA working on a client - into a
/// different tenant every time the 15-minute access token rolled over.
/// </summary>
public class AuthServiceRefreshContextTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManager = MockHelpers.CreateMockUserManager();
    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<IDistributedCache> _cache = MockHelpers.CreateMockDistributedCache();
    private readonly Mock<IEmailService> _emailService = new();
    private readonly Mock<ILogger<AuthService>> _logger = new();
    private readonly Mock<ITwoFactorService> _twoFactor = new();
    private readonly Mock<IOtpService> _otp = new();
    private readonly Mock<IGeoLocationService> _geo = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "http://localhost:3000" })
        .Build();

    /// <summary>Arguments the service passed to GenerateAccessToken.</summary>
    private Organization? _tokenOrganization;
    private string? _tokenRole;
    private bool _tokenIsExternal;
    private List<Claim> _tokenAdditionalClaims = [];

    public AuthServiceRefreshContextTests()
    {
        _jwtService
            .Setup(x => x.GenerateAccessToken(
                It.IsAny<ApplicationUser>(),
                It.IsAny<Organization?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<IEnumerable<Claim>?>()))
            .Callback<ApplicationUser, Organization?, string?, bool, IEnumerable<Claim>?>(
                (_, org, role, isExternal, extra) =>
                {
                    _tokenOrganization = org;
                    _tokenRole = role;
                    _tokenIsExternal = isExternal;
                    _tokenAdditionalClaims = extra?.ToList() ?? [];
                })
            .Returns("access-token");

        _jwtService
            .Setup(x => x.GenerateRefreshToken(It.IsAny<bool>()))
            .Returns(("new-jti:new-secret", "new-jti", DateTime.UtcNow.AddDays(7)));

        _jwtService.Setup(x => x.GetAccessTokenExpiryMinutes()).Returns(15);
    }

    [Fact]
    public async Task RefreshToken_UsesTheOrganizationTheSessionWasOpenedFor_NotTheNewestMembership()
    {
        // Arrange: the user belongs to two organizations, and joined "Newer" most
        // recently - which is exactly what the old implementation would have picked.
        using var db = BillingTestDbContextFactory.Create();

        var user = TestFixture.CreateUser();
        var working = TestFixture.CreateOrganization(name: "Working Org");
        var newer = TestFixture.CreateOrganization(name: "Newer Org");

        db.Users.Add(user);
        db.Organizations.AddRange(working, newer);
        db.OrganizationMembers.AddRange(
            Membership(user.Id, working.Id, "manager", joinedAt: DateTime.UtcNow.AddDays(-30)),
            Membership(user.Id, newer.Id, "owner", joinedAt: DateTime.UtcNow.AddDays(-1)));

        var refreshToken = AddSession(db, user.Id, organizationId: working.Id, role: "manager");
        await db.SaveChangesAsync();

        // Act
        await CreateService(db).RefreshTokenAsync(refreshToken, "127.0.0.1", "TestAgent");

        // Assert
        _tokenOrganization!.Id.Should().Be(working.Id);
        _tokenRole.Should().Be("manager");
    }

    [Fact]
    public async Task RefreshToken_WithNoStampedOrganization_FallsBackToLegacyBehaviour()
    {
        // Arrange: a session created before the context columns existed.
        using var db = BillingTestDbContextFactory.Create();

        var user = TestFixture.CreateUser();
        var organization = TestFixture.CreateOrganization(name: "Only Org");

        db.Users.Add(user);
        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(Membership(user.Id, organization.Id, "owner", DateTime.UtcNow.AddDays(-5)));

        var refreshToken = AddSession(db, user.Id, organizationId: null, role: null);
        await db.SaveChangesAsync();

        // Act
        await CreateService(db).RefreshTokenAsync(refreshToken, "127.0.0.1", "TestAgent");

        // Assert
        _tokenOrganization!.Id.Should().Be(organization.Id);
        _tokenRole.Should().Be("owner");
    }

    [Fact]
    public async Task RefreshToken_ForCaWithLiveRelationship_KeepsClientContext()
    {
        // Arrange
        using var db = BillingTestDbContextFactory.Create();
        var (ca, clientOrg, relationship) = SeedCaEngagement(db, CaClientRelationshipStatus.Active);

        var refreshToken = AddSession(
            db, ca.Id, clientOrg.Id, role: "ca", isExternal: true, relationshipId: relationship.Id);
        await db.SaveChangesAsync();

        // Act
        await CreateService(db).RefreshTokenAsync(refreshToken, "127.0.0.1", "TestAgent");

        // Assert
        _tokenOrganization!.Id.Should().Be(clientOrg.Id);
        _tokenRole.Should().Be("ca");
        _tokenIsExternal.Should().BeTrue();
        _tokenAdditionalClaims.Should().ContainSingle(c =>
            c.Type == "ca_client_rel_id" && c.Value == relationship.Id.ToString());
    }

    [Fact]
    public async Task RefreshToken_WhenRelationshipRevoked_DropsCaBackToTheirOwnFirm()
    {
        // Arrange: the Business Owner revoked the engagement while the CA held a session.
        using var db = BillingTestDbContextFactory.Create();
        var (ca, clientOrg, relationship) = SeedCaEngagement(db, CaClientRelationshipStatus.Revoked);

        var refreshToken = AddSession(
            db, ca.Id, clientOrg.Id, role: "ca", isExternal: true, relationshipId: relationship.Id);
        await db.SaveChangesAsync();

        // Act
        var result = await CreateService(db).RefreshTokenAsync(refreshToken, "127.0.0.1", "TestAgent");

        // Assert: refreshing still succeeds - losing one client must not log the CA out -
        // but the client context is gone.
        result.AccessToken.Should().NotBeNullOrEmpty();
        _tokenOrganization!.Id.Should().NotBe(clientOrg.Id);
        _tokenAdditionalClaims.Should().NotContain(c => c.Type == "ca_client_rel_id");
    }

    [Fact]
    public async Task RefreshToken_ForCaWithLiveRelationship_StampsTheNewSession()
    {
        // Arrange
        using var db = BillingTestDbContextFactory.Create();
        var (ca, clientOrg, relationship) = SeedCaEngagement(db, CaClientRelationshipStatus.Active);

        var refreshToken = AddSession(
            db, ca.Id, clientOrg.Id, role: "ca", isExternal: true, relationshipId: relationship.Id);
        await db.SaveChangesAsync();

        // Act
        await CreateService(db).RefreshTokenAsync(refreshToken, "127.0.0.1", "TestAgent");

        // Assert: the context must carry onto the replacement session, or it would survive
        // exactly one refresh and then be lost.
        var newSession = db.UserSessions.Single(s => s.RefreshTokenJti == "new-jti");
        newSession.OrganizationId.Should().Be(clientOrg.Id);
        newSession.Role.Should().Be("ca");
        newSession.IsExternal.Should().BeTrue();
        newSession.CaClientRelationshipId.Should().Be(relationship.Id);
    }

    // ------------------------------------------------------------------ helpers

    private (ApplicationUser Ca, Organization ClientOrg, CaClientRelationship Relationship)
        SeedCaEngagement(ApplicationDbContext db, string relationshipStatus)
    {
        var ca = TestFixture.CreateUser(email: "ca@example.com", name: "Test CA");
        ca.IsCa = true;

        var caFirm = TestFixture.CreateOrganization(name: "CA Firm");
        ca.OrganizationId = caFirm.Id;

        var client = TestFixture.CreateUser(email: "bo@example.com", name: "Business Owner");
        var clientOrg = TestFixture.CreateOrganization(name: "Client Org");

        var relationship = new CaClientRelationship
        {
            Id = Guid.NewGuid(),
            CaUserId = ca.Id,
            ClientUserId = client.Id,
            OrganizationId = clientOrg.Id,
            Status = relationshipStatus
        };

        db.Users.AddRange(ca, client);
        db.Organizations.AddRange(caFirm, clientOrg);
        db.OrganizationMembers.Add(Membership(ca.Id, caFirm.Id, "owner", DateTime.UtcNow.AddDays(-10)));
        db.CaProfiles.Add(new CaProfile
        {
            Id = Guid.NewGuid(),
            UserId = ca.Id,
            Status = CaProfileStatus.Active
        });
        db.CaClientRelationships.Add(relationship);

        return (ca, clientOrg, relationship);
    }

    private static OrganizationMember Membership(
        Guid userId, Guid organizationId, string role, DateTime joinedAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        OrganizationId = organizationId,
        Role = role,
        Status = "active",
        JoinedAt = joinedAt
    };

    /// <summary>Adds a session and returns the refresh token that opens it.</summary>
    private static string AddSession(
        ApplicationDbContext db,
        Guid userId,
        Guid? organizationId,
        string? role,
        bool isExternal = false,
        Guid? relationshipId = null)
    {
        var jti = Guid.NewGuid().ToString();
        var refreshToken = $"{jti}:secret";

        db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            RefreshTokenJti = jti,
            RefreshTokenHash = Sha256Hex(refreshToken),
            IpAddress = "127.0.0.1",
            Platform = "web",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            LastActiveAt = DateTime.UtcNow,
            OrganizationId = organizationId,
            Role = role,
            IsExternal = isExternal,
            CaClientRelationshipId = relationshipId
        });

        return refreshToken;
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private AuthService CreateService(ApplicationDbContext db) => new(
        _userManager.Object,
        db,
        _jwtService.Object,
        _cache.Object,
        _emailService.Object,
        _logger.Object,
        _configuration,
        _twoFactor.Object,
        _otp.Object,
        _geo.Object);
}
