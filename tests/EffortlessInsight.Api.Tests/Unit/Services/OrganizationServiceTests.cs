using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Validators;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace EffortlessInsight.Api.Tests.Unit.Services;

// Note: OrganizationManagementServiceTests require database access with ApplicationDbContext.
// Due to the EF Core InMemory provider not supporting Dictionary<string, object> types
// (used in ApplicationUser.Preferences and Organization.Settings), these tests are
// implemented as integration tests that use a real PostgreSQL database.
//
// The OrganizationManagementService covers:
// - CreateAsync, GetByIdAsync, GetUserOrganizationsAsync, UpdateAsync, DeleteAsync
// - GSTIN management: AddGstin, RemoveGstin, SetPrimaryGstin
// - Member management: GetMembers, InviteMember, AcceptInvitation, DeclineInvitation,
//   ChangeMemberRole, RemoveMember, LeaveOrganization, TransferOwnership
// - Settings: UpdateSettings
// - SwitchOrganization
//
// Run integration tests with: dotnet test --filter "Category=Integration"

#region GstinValidator Tests

public class GstinValidatorTests
{
    [Theory]
    [InlineData("27AABCU9603R1ZN", true, "27", "Maharashtra")] // Valid checksum
    [InlineData("07AABCU9603R1ZP", true, "07", "Delhi")]       // Valid checksum
    [InlineData("29AABCU9603R1ZJ", true, "29", "Karnataka")]   // Valid checksum
    public void Validate_ValidGstin_ReturnsSuccess(string gstin, bool expectedValid, string expectedStateCode, string expectedStateName)
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var validator = new GstinValidatorService(dbContext);

        // Act
        var result = validator.Validate(gstin);

        // Assert
        result.IsValid.Should().Be(expectedValid);
        if (expectedValid)
        {
            result.StateCode.Should().Be(expectedStateCode);
            result.StateName.Should().Contain(expectedStateName.Split(' ')[0]);
        }
    }

    [Theory]
    [InlineData("", "GSTIN is required")]
    [InlineData("27AABCU9603R1Z", "GSTIN must be exactly 15 characters")]
    [InlineData("27AABCU9603R1ZMX", "GSTIN must be exactly 15 characters")]
    [InlineData("27AABCU9603R1ZM", "Invalid GSTIN checksum")] // Wrong check digit
    // Note: lowercase is automatically converted to uppercase by the validator, so it passes validation
    public void Validate_InvalidGstin_ReturnsError(string gstin, string expectedErrorContains)
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var validator = new GstinValidatorService(dbContext);

        // Act
        var result = validator.Validate(gstin);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expectedErrorContains);
    }

    [Fact]
    public void Validate_ExtractsCorrectPan()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var validator = new GstinValidatorService(dbContext);
        var gstin = "27AABCU9603R1ZN";

        // Act
        var result = validator.Validate(gstin);

        // Assert
        result.Pan.Should().Be("AABCU9603R");
    }

    [Fact]
    public void Validate_ExtractsEntityCode()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var validator = new GstinValidatorService(dbContext);
        var gstin = "27AABCU9603R1ZN";

        // Act
        var result = validator.Validate(gstin);

        // Assert
        result.EntityCode.Should().Be("1");
    }

    [Fact]
    public void Validate_LowercaseGstin_ConvertsToUppercaseAndValidates()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var validator = new GstinValidatorService(dbContext);
        var gstin = "27aabcu9603r1zn"; // lowercase version of valid GSTIN

        // Act
        var result = validator.Validate(gstin);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Gstin.Should().Be("27AABCU9603R1ZN"); // Converted to uppercase
    }

    [Fact]
    public void Validate_InvalidStateCode00_ReturnsError()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var validator = new GstinValidatorService(dbContext);
        // State code 00 is invalid
        var gstin = "00AABCU9603R1ZR"; // Valid checksum for this pattern

        // Act
        var result = validator.Validate(gstin);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid state code");
    }

    [Fact]
    public void Validate_InvalidStateCode50_ReturnsError()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var validator = new GstinValidatorService(dbContext);
        // State code 50 is invalid (valid range is 01-38, 97, 99)
        var gstin = "50AABCU9603R1ZD"; // Valid checksum for this pattern

        // Act
        var result = validator.Validate(gstin);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid state code");
    }

    // Note: Async database tests (GetStateNameAsync, ExistsAsync) are tested through
    // integration tests because InMemory provider doesn't support Dictionary<string, object>
    // types used in ApplicationUser.Preferences

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}

#endregion

#region OrganizationValidator Tests

public class OrganizationValidatorTests
{
    [Fact]
    public void CreateOrganizationValidator_ValidRequest_PassesValidation()
    {
        // Arrange
        var validator = new CreateOrganizationRequestValidator();
        var request = new CreateOrganizationRequest(
            Name: "Test Organization",
            LegalName: "Test Org Pvt Ltd",
            Gstin: "27AABCU9603R1ZN",
            Industry: "Technology",
            State: "Maharashtra",
            City: "Mumbai",
            AnnualTurnoverRange: "1.5Cr-5Cr"
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateOrganizationValidator_MissingName_FailsValidation()
    {
        // Arrange
        var validator = new CreateOrganizationRequestValidator();
        var request = new CreateOrganizationRequest(
            Name: "",
            LegalName: null,
            Gstin: "27AABCU9603R1ZN",
            Industry: null,
            State: "Maharashtra",
            City: null,
            AnnualTurnoverRange: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void CreateOrganizationValidator_InvalidGstinFormat_FailsValidation()
    {
        // Arrange
        var validator = new CreateOrganizationRequestValidator();
        var request = new CreateOrganizationRequest(
            Name: "Test Organization",
            LegalName: null,
            Gstin: "INVALID-GSTIN",
            Industry: null,
            State: "Maharashtra",
            City: null,
            AnnualTurnoverRange: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Gstin");
    }

    [Fact]
    public void CreateOrganizationValidator_TooLongName_FailsValidation()
    {
        // Arrange
        var validator = new CreateOrganizationRequestValidator();
        var request = new CreateOrganizationRequest(
            Name: new string('A', 256), // Max is 255
            LegalName: null,
            Gstin: "27AABCU9603R1ZN",
            Industry: null,
            State: "Maharashtra",
            City: null,
            AnnualTurnoverRange: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void InviteMemberValidator_ValidRequest_PassesValidation()
    {
        // Arrange
        var validator = new InviteMemberRequestValidator();
        var request = new InviteMemberRequest(
            Email: "test@example.com",
            Role: "member",
            IsExternal: false,
            AccessDurationDays: null,
            ClientReference: null,
            Message: "Welcome to our team!"
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("manager")]
    [InlineData("member")]
    [InlineData("ca")]
    [InlineData("viewer")]
    public void InviteMemberValidator_ValidRoles_PassValidation(string role)
    {
        // Arrange
        var validator = new InviteMemberRequestValidator();
        var request = new InviteMemberRequest(
            Email: "test@example.com",
            Role: role,
            IsExternal: false,
            AccessDurationDays: null,
            ClientReference: null,
            Message: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void InviteMemberValidator_InvalidRole_FailsValidation()
    {
        // Arrange
        var validator = new InviteMemberRequestValidator();
        var request = new InviteMemberRequest(
            Email: "test@example.com",
            Role: "owner", // owner cannot be invited
            IsExternal: false,
            AccessDurationDays: null,
            ClientReference: null,
            Message: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void InviteMemberValidator_InvalidEmail_FailsValidation()
    {
        // Arrange
        var validator = new InviteMemberRequestValidator();
        var request = new InviteMemberRequest(
            Email: "not-an-email",
            Role: "member",
            IsExternal: false,
            AccessDurationDays: null,
            ClientReference: null,
            Message: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void InviteMemberValidator_ExternalWithAccessDuration_PassesValidation()
    {
        // Arrange
        var validator = new InviteMemberRequestValidator();
        var request = new InviteMemberRequest(
            Email: "ca@example.com",
            Role: "ca",
            IsExternal: true,
            AccessDurationDays: 90,
            ClientReference: "CLIENT-001",
            Message: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TransferOwnershipValidator_MissingPassword_FailsValidation()
    {
        // Arrange
        var validator = new TransferOwnershipRequestValidator();
        var request = new TransferOwnershipRequest(
            NewOwnerId: Guid.NewGuid(),
            Password: ""
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void TransferOwnershipValidator_EmptyNewOwnerId_FailsValidation()
    {
        // Arrange
        var validator = new TransferOwnershipRequestValidator();
        var request = new TransferOwnershipRequest(
            NewOwnerId: Guid.Empty,
            Password: "password123"
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewOwnerId");
    }

    [Fact]
    public void UpdateOrganizationSettingsValidator_ValidTimezone_PassesValidation()
    {
        // Arrange
        var validator = new UpdateOrganizationSettingsRequestValidator();
        var request = new UpdateOrganizationSettingsRequest(
            DefaultReminderDays: [7, 3, 1],
            NotificationEmail: true,
            NotificationSms: false,
            AllowCaAccess: true,
            RequireResponseApproval: false,
            Timezone: "Asia/Kolkata",
            Language: "en",
            DateFormat: "DD/MM/YYYY"
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateOrganizationSettingsValidator_InvalidLanguage_FailsValidation()
    {
        // Arrange
        var validator = new UpdateOrganizationSettingsRequestValidator();
        var request = new UpdateOrganizationSettingsRequest(
            DefaultReminderDays: null,
            NotificationEmail: null,
            NotificationSms: null,
            AllowCaAccess: null,
            RequireResponseApproval: null,
            Timezone: null,
            Language: "fr", // Not supported
            DateFormat: null
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Language");
    }

    [Fact]
    public void ChangeMemberRoleValidator_ValidRole_PassesValidation()
    {
        // Arrange
        var validator = new ChangeMemberRoleRequestValidator();
        var request = new ChangeMemberRoleRequest(Role: "admin");

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ChangeMemberRoleValidator_InvalidRole_FailsValidation()
    {
        // Arrange
        var validator = new ChangeMemberRoleRequestValidator();
        var request = new ChangeMemberRoleRequest(Role: "superuser");

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void DeleteOrganizationValidator_MissingConfirmation_FailsValidation()
    {
        // Arrange
        var validator = new DeleteOrganizationRequestValidator();
        var request = new DeleteOrganizationRequest(
            Confirmation: "",
            Password: "password123"
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Confirmation");
    }

    [Fact]
    public void SwitchOrganizationValidator_EmptyOrgId_FailsValidation()
    {
        // Arrange
        var validator = new SwitchOrganizationRequestValidator();
        var request = new SwitchOrganizationRequest(OrganizationId: Guid.Empty);

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AddGstinValidator_ValidRequest_PassesValidation()
    {
        // Arrange
        var validator = new AddGstinRequestValidator();
        var request = new AddGstinRequest(
            Gstin: "27AABCU9603R1ZN",
            TradeName: "Trade Name",
            IsPrimary: false
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AddGstinValidator_InvalidGstinFormat_FailsValidation()
    {
        // Arrange
        var validator = new AddGstinRequestValidator();
        var request = new AddGstinRequest(
            Gstin: "invalid",
            TradeName: null,
            IsPrimary: false
        );

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }
}

#endregion

#region Role Hierarchy Tests

public class RoleHierarchyTests
{
    [Theory]
    [InlineData("owner", "organization:delete", true)]
    [InlineData("admin", "organization:delete", false)]
    [InlineData("owner", "organization:edit", true)]
    [InlineData("admin", "organization:edit", true)]
    [InlineData("member", "organization:edit", false)]
    [InlineData("owner", "members:invite", true)]
    [InlineData("admin", "members:invite", true)]
    [InlineData("manager", "members:invite", false)]
    [InlineData("ca", "members:view", false)]
    [InlineData("member", "members:view", true)]
    [InlineData("viewer", "notices:upload", false)]
    [InlineData("member", "notices:upload", true)]
    [InlineData("owner", "organization:billing", true)]
    [InlineData("admin", "organization:billing", false)]
    [InlineData("owner", "organization:transfer", true)]
    [InlineData("admin", "organization:transfer", false)]
    [InlineData("manager", "notices:assign", true)]
    [InlineData("member", "notices:assign", false)]
    [InlineData("owner", "audit:view", true)]
    [InlineData("admin", "audit:view", true)]
    [InlineData("member", "audit:view", false)]
    public void HasPermission_ReturnsCorrectResult(string role, string permission, bool expected)
    {
        // Arrange
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var dbContext = CreateInMemoryDbContext();

        var claims = new[]
        {
            new Claim("role", role),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("org_id", Guid.NewGuid().ToString())
        };

        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new Mock<HttpContext>();
        httpContext.Setup(x => x.User).Returns(principal);
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext.Object);

        var service = new CurrentOrganizationService(httpContextAccessor.Object, dbContext);

        // Act
        var result = service.HasPermission(permission);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void HasPermission_NoRole_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var dbContext = CreateInMemoryDbContext();

        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("org_id", Guid.NewGuid().ToString())
        };

        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new Mock<HttpContext>();
        httpContext.Setup(x => x.User).Returns(principal);
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext.Object);

        var service = new CurrentOrganizationService(httpContextAccessor.Object, dbContext);

        // Act
        var result = service.HasPermission("organization:view");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasPermission_UnknownPermission_ReturnsFalse()
    {
        // Arrange
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        var dbContext = CreateInMemoryDbContext();

        var claims = new[]
        {
            new Claim("role", "owner"),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("org_id", Guid.NewGuid().ToString())
        };

        var identity = new ClaimsIdentity(claims, "test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new Mock<HttpContext>();
        httpContext.Setup(x => x.User).Returns(principal);
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext.Object);

        var service = new CurrentOrganizationService(httpContextAccessor.Object, dbContext);

        // Act
        var result = service.HasPermission("unknown:permission");

        // Assert
        result.Should().BeFalse();
    }

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}

#endregion

#region CurrentOrganizationService Tests

public class CurrentOrganizationServiceTests
{
    [Fact]
    public void OrganizationId_WithValidClaim_ReturnsId()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var service = CreateServiceWithClaims(new Claim("org_id", orgId.ToString()));

        // Act
        var result = service.OrganizationId;

        // Assert
        result.Should().Be(orgId);
    }

    [Fact]
    public void OrganizationId_WithNoClaim_ReturnsNull()
    {
        // Arrange
        var service = CreateServiceWithClaims();

        // Act
        var result = service.OrganizationId;

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void UserId_WithSubClaim_ReturnsId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var service = CreateServiceWithClaims(new Claim("sub", userId.ToString()));

        // Act
        var result = service.UserId;

        // Assert
        result.Should().Be(userId);
    }

    [Fact]
    public void UserId_WithNameIdentifierClaim_ReturnsId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var service = CreateServiceWithClaims(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        // Act
        var result = service.UserId;

        // Assert
        result.Should().Be(userId);
    }

    [Fact]
    public void Role_WithRoleClaim_ReturnsRole()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "admin"));

        // Act
        var result = service.Role;

        // Assert
        result.Should().Be("admin");
    }

    [Fact]
    public void IsExternal_WhenTrue_ReturnsTrue()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("is_external", "true"));

        // Act
        var result = service.IsExternal;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsExternal_WhenFalse_ReturnsFalse()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("is_external", "false"));

        // Act
        var result = service.IsExternal;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsOwner_WhenOwner_ReturnsTrue()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "owner"));

        // Act
        var result = service.IsOwner;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsOwner_WhenAdmin_ReturnsFalse()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "admin"));

        // Act
        var result = service.IsOwner;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsAdmin_WhenOwner_ReturnsTrue()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "owner"));

        // Act
        var result = service.IsAdmin;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_WhenAdmin_ReturnsTrue()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "admin"));

        // Act
        var result = service.IsAdmin;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_WhenMember_ReturnsFalse()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "member"));

        // Act
        var result = service.IsAdmin;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanManageBilling_WhenOwner_ReturnsTrue()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "owner"));

        // Act
        var result = service.CanManageBilling;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanManageBilling_WhenAdmin_ReturnsFalse()
    {
        // Arrange
        var service = CreateServiceWithClaims(new Claim("role", "admin"));

        // Act
        var result = service.CanManageBilling;

        // Assert
        result.Should().BeFalse();
    }

    // Note: Async database tests (GetCurrentMembershipAsync, ValidateMembershipAsync, GetUserMembershipsAsync)
    // are tested through integration tests because InMemory provider doesn't support Dictionary<string, object>
    // types used in ApplicationUser.Preferences

    private static CurrentOrganizationService CreateServiceWithClaims(params Claim[] claims)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext = new ApplicationDbContext(options);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();

        if (claims.Length > 0)
        {
            var identity = new ClaimsIdentity(claims, "test");
            var principal = new ClaimsPrincipal(identity);

            var httpContext = new Mock<HttpContext>();
            httpContext.Setup(x => x.User).Returns(principal);
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext.Object);
        }
        else
        {
            httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        }

        return new CurrentOrganizationService(httpContextAccessor.Object, dbContext);
    }
}

#endregion
