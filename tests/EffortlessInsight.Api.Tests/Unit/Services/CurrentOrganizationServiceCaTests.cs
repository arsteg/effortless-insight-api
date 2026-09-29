using System.Security.Claims;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Encryption;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EffortlessInsight.Api.Tests.Unit.Services;

/// <summary>
/// Phase 5 hardening: the CA authorization matrix. HasPermission is a pure
/// function of JWT claims (Role/IsExternal) - it never touches the database,
/// so an active-vs-suspended-vs-expired distinction can only be enforced by a
/// separate DB-backed check (ValidateMembershipAsync, used at org-switch time
/// and by OrganizationsController's route-scoped endpoints). These tests cover
/// both halves and make that boundary explicit rather than assumed.
/// </summary>
public class CurrentOrganizationServiceCaTests
{
    static CurrentOrganizationServiceCaTests()
    {
        if (!FieldEncryptionServiceAccessor.IsConfigured)
        {
            FieldEncryptionServiceAccessor.SetInstance(
                new FieldEncryptionService(new ConfigurationBuilder().Build(), NullLogger<FieldEncryptionService>.Instance));
        }
    }

    private static CurrentOrganizationService CreateService(
        Guid userId, Guid orgId, string role, bool isExternal,
        out EffortlessInsight.Api.Data.ApplicationDbContext dbContext)
    {
        dbContext = BillingTestDbContextFactory.Create();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("org_id", orgId.ToString()),
            new("role", role),
            new("is_external", isExternal ? "true" : "false"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        var accessorMock = new Moq.Mock<IHttpContextAccessor>();
        accessorMock.Setup(a => a.HttpContext).Returns(httpContext);

        return new CurrentOrganizationService(accessorMock.Object, dbContext);
    }

    // ------------------------------------------------------------------
    // HasPermission: claims-only, role="ca" - the broadened Phase 5 ceiling
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("notices.view", true)]
    [InlineData("notices.view_all", true)]
    [InlineData("notices.upload", true)]
    [InlineData("notices.edit", true)]
    [InlineData("notices.comment", true)]
    [InlineData("notices.draft_response", true)]
    [InlineData("tasks.view", true)]
    [InlineData("tasks.create", true)]
    [InlineData("workflow.view", true)]
    [InlineData("workflow.transition", true)]
    [InlineData("members.view", true)] // Phase 5 fix: was role-is-not-ca before
    [InlineData("settings.view", true)] // Phase 5 fix: was role-is-not-ca-or-external before
    [InlineData("gstins.view", true)]
    [InlineData("reports.view", true)]
    // Deliberately still denied - BO governance / owner-only actions:
    [InlineData("notices.delete", false)]
    [InlineData("notices.assign", false)]
    [InlineData("notices.approve", false)]
    [InlineData("notices.approve_response", false)]
    [InlineData("workflow.admin", false)]
    [InlineData("members.invite", false)]
    [InlineData("members.remove", false)]
    [InlineData("members.change_role", false)]
    [InlineData("gstins.add", false)]
    [InlineData("gstins.remove", false)]
    [InlineData("settings.edit", false)]
    [InlineData("organization.edit", false)]
    [InlineData("organization.delete", false)]
    [InlineData("organization.billing", false)]
    [InlineData("organization.transfer", false)]
    public void HasPermission_ActiveCaMembership_MatchesBroadenedCeiling(string permission, bool expected)
    {
        var service = CreateService(Guid.NewGuid(), Guid.NewGuid(), "ca", isExternal: true, out _);

        service.HasPermission(permission).Should().Be(expected);
    }

    [Fact]
    public void HasPermission_Owner_StillHasFullAccess_RegressionGuard()
    {
        var service = CreateService(Guid.NewGuid(), Guid.NewGuid(), "owner", isExternal: false, out _);

        service.HasPermission("organization.delete").Should().BeTrue();
        service.HasPermission("members.invite").Should().BeTrue();
        service.HasPermission("gstins.add").Should().BeTrue();
        service.HasPermission("settings.edit").Should().BeTrue();
    }

    [Fact]
    public void HasPermission_Viewer_StillRestricted_RegressionGuard()
    {
        var service = CreateService(Guid.NewGuid(), Guid.NewGuid(), "viewer", isExternal: false, out _);

        service.HasPermission("notices.upload").Should().BeFalse();
        service.HasPermission("notices.edit").Should().BeFalse();
        service.HasPermission("tasks.create").Should().BeFalse();
        // Read-only access remains
        service.HasPermission("notices.view").Should().BeTrue();
        service.HasPermission("members.view").Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // ValidateMembershipAsync: the actual DB-backed gate for active/
    // suspended/expired/no-membership-at-all scenarios.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ValidateMembershipAsync_ActiveCaMembership_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var service = CreateService(userId, orgId, "ca", isExternal: true, out var db);
        SeedMembership(db, userId, orgId, "ca", status: "active", accessExpiresAt: null);

        (await service.ValidateMembershipAsync(orgId)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateMembershipAsync_SuspendedCaMembership_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var service = CreateService(userId, orgId, "ca", isExternal: true, out var db);
        SeedMembership(db, userId, orgId, "ca", status: "suspended", accessExpiresAt: null);

        (await service.ValidateMembershipAsync(orgId)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateMembershipAsync_ExpiredCaAccess_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var service = CreateService(userId, orgId, "ca", isExternal: true, out var db);
        SeedMembership(db, userId, orgId, "ca", status: "active", accessExpiresAt: DateTime.UtcNow.AddDays(-1));

        (await service.ValidateMembershipAsync(orgId)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateMembershipAsync_NoMembershipAtAll_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var unrelatedOrgId = Guid.NewGuid();
        var service = CreateService(userId, orgId, "ca", isExternal: true, out var db);
        // CA has a membership on a *different* org, not this one.
        SeedMembership(db, userId, unrelatedOrgId, "ca", status: "active", accessExpiresAt: null);

        (await service.ValidateMembershipAsync(orgId)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateMembershipAsync_FutureAccessExpiry_ReturnsTrue()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var service = CreateService(userId, orgId, "ca", isExternal: true, out var db);
        SeedMembership(db, userId, orgId, "ca", status: "active", accessExpiresAt: DateTime.UtcNow.AddDays(30));

        (await service.ValidateMembershipAsync(orgId)).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Documents the real bound of "revoke access immediately": HasPermission
    // trusts JWT claims alone and does not re-check ValidateMembershipAsync,
    // so a CA whose membership is suspended/expired mid-session keeps whatever
    // access their already-issued access token's claims grant until that
    // token expires (or is refreshed) - revocation is enforced at the next
    // token-issuing checkpoint (login/switch-organization/refresh), not on
    // every single request. This is a conscious, tested limit, not a bug.
    // ------------------------------------------------------------------
    [Fact]
    public async Task HasPermission_DoesNotReflectMidSessionSuspension_DocumentedBound()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var service = CreateService(userId, orgId, "ca", isExternal: true, out var db);
        SeedMembership(db, userId, orgId, "ca", status: "suspended", accessExpiresAt: null);

        // The already-issued token's claims still say role=ca, so HasPermission
        // (claims-only) still grants notices.upload...
        service.HasPermission("notices.upload").Should().BeTrue();

        // ...even though the actual membership row backing that claim is no
        // longer valid, as the DB-backed check correctly reports.
        (await service.ValidateMembershipAsync(orgId)).Should().BeFalse();
    }

    private static void SeedMembership(
        EffortlessInsight.Api.Data.ApplicationDbContext db, Guid userId, Guid orgId, string role,
        string status, DateTime? accessExpiresAt)
    {
        db.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = "Test Org " + orgId,
            NameNormalized = "test org " + orgId,
            State = "Maharashtra",
            SubscriptionStatus = "none"
        });
        db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = orgId,
            UserId = userId,
            Role = role,
            IsExternal = role == "ca",
            Status = status,
            AccessExpiresAt = accessExpiresAt
        });
        db.SaveChanges();
    }
}
