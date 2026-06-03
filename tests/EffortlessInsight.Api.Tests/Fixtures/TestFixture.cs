using Bogus;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Tests.Fixtures;

public static class TestFixture
{
    private static readonly Faker Faker = new("en");

    public static ApplicationUser CreateUser(
        Guid? id = null,
        string? email = null,
        string? name = null,
        string? role = "member",
        bool emailConfirmed = true,
        bool isActive = true,
        Guid? organizationId = null)
    {
        var userEmail = email ?? Faker.Internet.Email();
        return new ApplicationUser
        {
            Id = id ?? Guid.NewGuid(),
            UserName = userEmail,
            Email = userEmail,
            NormalizedEmail = userEmail.ToUpperInvariant(),
            NormalizedUserName = userEmail.ToUpperInvariant(),
            Name = name ?? Faker.Name.FullName(),
            Role = role ?? "member",
            EmailConfirmed = emailConfirmed,
            IsActive = isActive,
            OrganizationId = organizationId,
            Mobile = Faker.Phone.PhoneNumber("##########"),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Organization CreateOrganization(
        Guid? id = null,
        string? name = null)
    {
        return new Organization
        {
            Id = id ?? Guid.NewGuid(),
            Name = name ?? Faker.Company.CompanyName(),
            Gstins = new List<string> { $"29{Faker.Random.AlphaNumeric(13).ToUpper()}" },
            Industry = Faker.Commerce.Categories(1)[0],
            State = "Karnataka",
            City = "Bangalore",
            SubscriptionStatus = "active",
            CreatedAt = DateTime.UtcNow
        };
    }

    public static RegisterRequest CreateRegisterRequest(
        string? email = null,
        string? password = null,
        string? name = null,
        string? mobile = null,
        bool acceptTerms = true)
    {
        return new RegisterRequest(
            Email: email ?? Faker.Internet.Email(),
            Password: password ?? "SecureP@ss123",
            Name: name ?? Faker.Name.FullName(),
            Mobile: mobile ?? $"{Faker.Random.Int(6, 9)}{Faker.Random.String2(9, "0123456789")}",
            AcceptTerms: acceptTerms
        );
    }

    public static LoginRequest CreateLoginRequest(
        string? email = null,
        string? password = null,
        bool rememberMe = false)
    {
        return new LoginRequest(
            Email: email ?? Faker.Internet.Email(),
            Password: password ?? "SecureP@ss123",
            RememberMe: rememberMe,
            DeviceInfo: new DeviceInfo("test-device-id", "Test Device", "web")
        );
    }

    public static UserSession CreateUserSession(
        Guid? id = null,
        Guid? userId = null,
        string? refreshTokenJti = null,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null)
    {
        return new UserSession
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            RefreshTokenHash = Faker.Random.Hash(),
            RefreshTokenJti = refreshTokenJti ?? Guid.NewGuid().ToString(),
            DeviceId = "test-device",
            DeviceName = "Test Device",
            Platform = "web",
            IpAddress = Faker.Internet.IpAddress().ToString(),
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            RevokedAt = revokedAt,
            CreatedAt = DateTime.UtcNow
        };
    }
}
