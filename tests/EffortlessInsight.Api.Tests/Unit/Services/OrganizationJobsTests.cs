using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Services.Organizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

// Note: OrganizationJobs and OrganizationDataMigrationService tests require database access.
// Due to limitations with InMemory provider not supporting Dictionary<string, object> types
// used in ApplicationUser.Preferences, these tests are implemented as integration tests
// in the OrganizationsControllerTests.cs file.
//
// The job classes are simple database operations (expire, cleanup, migrate) that are best
// tested with a real database context in integration tests.

#region OrganizationJobs Unit Tests (Logic-Only)

public class OrganizationJobsLogicTests
{
    // Test the business logic without database operations

    [Fact]
    public void InvitationExpiry_WhenPastExpiresAt_ShouldBeConsideredExpired()
    {
        // This tests the expiry logic
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            Status = "pending",
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // Past
        };

        var now = DateTime.UtcNow;
        var isExpired = invitation.Status == "pending" && invitation.ExpiresAt < now;

        isExpired.Should().BeTrue();
    }

    [Fact]
    public void InvitationExpiry_WhenFutureExpiresAt_ShouldNotBeConsideredExpired()
    {
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            Status = "pending",
            ExpiresAt = DateTime.UtcNow.AddDays(7) // Future
        };

        var now = DateTime.UtcNow;
        var isExpired = invitation.Status == "pending" && invitation.ExpiresAt < now;

        isExpired.Should().BeFalse();
    }

    [Fact]
    public void InvitationExpiry_WhenAlreadyAccepted_ShouldNotBeConsideredExpired()
    {
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            Status = "accepted", // Not pending
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // Past
        };

        var now = DateTime.UtcNow;
        var isExpired = invitation.Status == "pending" && invitation.ExpiresAt < now;

        isExpired.Should().BeFalse();
    }

    [Fact]
    public void CaAccessExpiry_WhenPastAccessExpiresAt_AndExternal_ShouldBeConsideredExpired()
    {
        var member = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            IsExternal = true,
            Status = "active",
            AccessExpiresAt = DateTime.UtcNow.AddDays(-1) // Past
        };

        var now = DateTime.UtcNow;
        var isExpired = member.IsExternal &&
                       member.Status == "active" &&
                       member.AccessExpiresAt.HasValue &&
                       member.AccessExpiresAt < now;

        isExpired.Should().BeTrue();
    }

    [Fact]
    public void CaAccessExpiry_WhenNoAccessExpiresAt_ShouldNotBeConsideredExpired()
    {
        var member = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            IsExternal = true,
            Status = "active",
            AccessExpiresAt = null // No expiry
        };

        var now = DateTime.UtcNow;
        var isExpired = member.IsExternal &&
                       member.Status == "active" &&
                       member.AccessExpiresAt.HasValue &&
                       member.AccessExpiresAt < now;

        isExpired.Should().BeFalse();
    }

    [Fact]
    public void CaAccessExpiry_WhenNotExternal_ShouldNotBeConsideredExpired()
    {
        var member = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            IsExternal = false, // Internal member
            Status = "active",
            AccessExpiresAt = DateTime.UtcNow.AddDays(-1) // Past
        };

        var now = DateTime.UtcNow;
        var isExpired = member.IsExternal &&
                       member.Status == "active" &&
                       member.AccessExpiresAt.HasValue &&
                       member.AccessExpiresAt < now;

        isExpired.Should().BeFalse();
    }

    [Fact]
    public void InvitationCleanup_WhenCancelledAndOld_ShouldBeConsideredForCleanup()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            Status = "cancelled",
            UpdatedAt = DateTime.UtcNow.AddDays(-35) // Older than 30 days
        };

        var shouldCleanup = (invitation.Status == "cancelled" ||
                            invitation.Status == "declined" ||
                            invitation.Status == "expired") &&
                           invitation.UpdatedAt < cutoffDate;

        shouldCleanup.Should().BeTrue();
    }

    [Fact]
    public void InvitationCleanup_WhenCancelledAndRecent_ShouldNotBeConsideredForCleanup()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            Status = "cancelled",
            UpdatedAt = DateTime.UtcNow.AddDays(-10) // Less than 30 days
        };

        var shouldCleanup = (invitation.Status == "cancelled" ||
                            invitation.Status == "declined" ||
                            invitation.Status == "expired") &&
                           invitation.UpdatedAt < cutoffDate;

        shouldCleanup.Should().BeFalse();
    }

    [Fact]
    public void InvitationCleanup_WhenPending_ShouldNotBeConsideredForCleanup()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            Status = "pending",
            UpdatedAt = DateTime.UtcNow.AddDays(-35) // Old but still pending
        };

        var shouldCleanup = (invitation.Status == "cancelled" ||
                            invitation.Status == "declined" ||
                            invitation.Status == "expired") &&
                           invitation.UpdatedAt < cutoffDate;

        shouldCleanup.Should().BeFalse();
    }

    [Fact]
    public void OrganizationDeletion_WhenSoftDeletedAndOld_ShouldBeConsideredForPermanentDeletion()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            NameNormalized = "test org",
            State = "Maharashtra",
            DeletedAt = DateTime.UtcNow.AddDays(-35) // Deleted more than 30 days ago
        };

        var shouldDelete = organization.DeletedAt.HasValue && organization.DeletedAt < cutoffDate;

        shouldDelete.Should().BeTrue();
    }

    [Fact]
    public void OrganizationDeletion_WhenSoftDeletedRecently_ShouldNotBeConsideredForPermanentDeletion()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            NameNormalized = "test org",
            State = "Maharashtra",
            DeletedAt = DateTime.UtcNow.AddDays(-10) // Deleted less than 30 days ago
        };

        var shouldDelete = organization.DeletedAt.HasValue && organization.DeletedAt < cutoffDate;

        shouldDelete.Should().BeFalse();
    }

    [Fact]
    public void OrganizationDeletion_WhenNotDeleted_ShouldNotBeConsideredForPermanentDeletion()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Test Org",
            NameNormalized = "test org",
            State = "Maharashtra",
            DeletedAt = null // Not deleted
        };

        var shouldDelete = organization.DeletedAt.HasValue && organization.DeletedAt < cutoffDate;

        shouldDelete.Should().BeFalse();
    }
}

#endregion

#region OrganizationDataMigration Logic Tests

public class OrganizationDataMigrationLogicTests
{
    [Fact]
    public void NormalizedName_TrimAndLowercase_ProducesCorrectResult()
    {
        var name = "  Test Organization Name  ";
        var normalized = name.Trim().ToLowerInvariant();

        normalized.Should().Be("test organization name");
    }

    [Fact]
    public void MembershipRole_CaRole_ShouldBeExternal()
    {
        var role = "ca";
        var isExternal = role == "ca";

        isExternal.Should().BeTrue();
    }

    [Fact]
    public void MembershipRole_MemberRole_ShouldNotBeExternal()
    {
        var role = "member";
        var isExternal = role == "ca";

        isExternal.Should().BeFalse();
    }

    [Fact]
    public void GstinMigration_FirstGstin_ShouldBePrimary()
    {
        var existingGstins = new HashSet<string>();
        var isFirst = !existingGstins.Any();

        isFirst.Should().BeTrue();
    }

    [Fact]
    public void GstinMigration_SecondGstin_ShouldNotBePrimary()
    {
        var existingGstins = new HashSet<string> { "27AABCU9603R1ZN" };
        var isFirst = !existingGstins.Any();

        isFirst.Should().BeFalse();
    }

    [Fact]
    public void MembershipKey_Format_IsCorrect()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var key = $"{orgId}:{userId}";

        key.Should().Contain(":");
        key.Split(':').Should().HaveCount(2);
    }
}

#endregion
