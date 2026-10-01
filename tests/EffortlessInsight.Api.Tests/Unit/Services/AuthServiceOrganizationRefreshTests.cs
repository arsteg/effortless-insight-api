using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Services.Email;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class AuthServiceOrganizationRefreshTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Refresh_PreservesSelectedOrganizationAndRoleAcrossRotation(bool legacy, bool clientSelected)
    {
        using var db = BillingTestDbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true, IsCA = true };
        var orgs = Enumerable.Range(0, 3).Select(i => new Organization { Id = Guid.NewGuid(), Name = $"Org {i}", NameNormalized = $"org {i}" }).ToArray();
        var selected = orgs[clientSelected ? 1 : 0];
        // A different device may change the user's global selection.
        user.OrganizationId = legacy ? selected.Id : orgs[2].Id;
        db.Users.Add(user);
        db.Organizations.AddRange(orgs);
        for (var i = 0; i < orgs.Length; i++)
            db.OrganizationMembers.Add(new OrganizationMember { UserId = user.Id, OrganizationId = orgs[i].Id,
                Role = i == 0 ? "owner" : "ca", IsExternal = i != 0, JoinedAt = DateTime.UtcNow.AddDays(i - 3) });
        const string token = "original:secret";
        db.UserSessions.Add(new UserSession { UserId = user.Id, OrganizationId = legacy ? null : selected.Id,
            RefreshTokenJti = "original", RefreshTokenHash = Hash(token), ExpiresAt = DateTime.UtcNow.AddDays(2) });
        await db.SaveChangesAsync();
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.GenerateRefreshToken(It.IsAny<bool>())).Returns(() => { var jti = Guid.NewGuid().ToString(); return ($"{jti}:secret", jti, DateTime.UtcNow.AddDays(7)); });
        jwt.Setup(j => j.GenerateAccessToken(user, selected, clientSelected ? "ca" : "owner", clientSelected)).Returns("correct-org-token");
        var service = Create(db, jwt);
        var result = await service.RefreshTokenAsync(token, "127.0.0.1", "test");
        result.AccessToken.Should().Be("correct-org-token");
        var rotated = await db.UserSessions.SingleAsync(s => s.RevokedAt == null);
        rotated.OrganizationId.Should().Be(selected.Id);
        user.OrganizationId = orgs[2].Id;
        await db.SaveChangesAsync();
        (await service.RefreshTokenAsync(result.RefreshToken, "127.0.0.1", "test")).AccessToken.Should().Be("correct-org-token");
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("expired")]
    [InlineData("deleted")]
    [InlineData("removed")]
    public async Task Refresh_RejectsInvalidSelectedMembership(string reason)
    {
        using var db = BillingTestDbContextFactory.Create();
        var user = new ApplicationUser { Id = Guid.NewGuid(), IsActive = true };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Firm", NameNormalized = "firm", DeletedAt = reason == "deleted" ? DateTime.UtcNow : null };
        db.Users.Add(user);
        db.Organizations.Add(org);
        if (reason != "removed")
            db.OrganizationMembers.Add(new OrganizationMember { UserId = user.Id, OrganizationId = org.Id,
                Status = reason == "suspended" ? "suspended" : "active", AccessExpiresAt = reason == "expired" ? DateTime.UtcNow.AddDays(-1) : null });
        db.UserSessions.Add(new UserSession { UserId = user.Id, OrganizationId = org.Id, RefreshTokenJti = "original",
            RefreshTokenHash = Hash("original:secret"), ExpiresAt = DateTime.UtcNow.AddDays(2) });
        await db.SaveChangesAsync();
        var service = Create(db, new Mock<IJwtService>());
        var act = () => service.RefreshTokenAsync("original:secret", "127.0.0.1", "test");
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("NOT_A_MEMBER");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static AuthService Create(EffortlessInsight.Api.Data.ApplicationDbContext db, Mock<IJwtService> jwt) => new(
        MockHelpers.CreateMockUserManager().Object, db, jwt.Object, MockHelpers.CreateMockDistributedCache().Object,
        new Mock<IEmailService>().Object, new Mock<ILogger<AuthService>>().Object, new ConfigurationBuilder().Build(),
        new Mock<ITwoFactorService>().Object, new Mock<IOtpService>().Object, new Mock<IGeoLocationService>().Object);
}
