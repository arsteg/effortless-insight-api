using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Services;

/// <summary>
/// Unit tests for AuthService - tests business logic without database dependencies
/// </summary>
public class AuthServiceRegistrationTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly Mock<IDistributedCache> _mockCache;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<ITwoFactorService> _mockTwoFactorService;
    private readonly Mock<IOtpService> _mockOtpService;
    private readonly Mock<IGeoLocationService> _mockGeoLocationService;
    private readonly IConfiguration _configuration;

    public AuthServiceRegistrationTests()
    {
        _mockUserManager = MockHelpers.CreateMockUserManager();
        _mockJwtService = new Mock<IJwtService>();
        _mockCache = MockHelpers.CreateMockDistributedCache();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockTwoFactorService = new Mock<ITwoFactorService>();
        _mockOtpService = new Mock<IOtpService>();
        _mockGeoLocationService = new Mock<IGeoLocationService>();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "App:BaseUrl", "http://localhost:3000" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ShouldThrowException()
    {
        // Arrange
        var request = TestFixture.CreateRegisterRequest(email: "existing@example.com");
        var existingUser = TestFixture.CreateUser(email: "existing@example.com");

        _mockUserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(existingUser);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.RegisterAsync(request, "127.0.0.1", "TestAgent");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("EMAIL_EXISTS");
    }

    // Note: Registration tests that require full DbContext functionality are in integration tests
    // These tests validate business logic that can be tested with mocks

    private AuthService CreateAuthServiceWithMockedDbContext()
    {
        // Create a mock DbContext that doesn't throw errors
        var mockDbContext = Substitute.For<EffortlessInsight.Api.Data.ApplicationDbContext>(
            new Microsoft.EntityFrameworkCore.DbContextOptions<EffortlessInsight.Api.Data.ApplicationDbContext>());

        return new AuthService(
            _mockUserManager.Object,
            mockDbContext,
            _mockJwtService.Object,
            _mockCache.Object,
            _mockEmailService.Object,
            _mockLogger.Object,
            _configuration,
            _mockTwoFactorService.Object,
            _mockOtpService.Object,
            _mockGeoLocationService.Object
        );
    }
}

public class AuthServiceLoginTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly Mock<IDistributedCache> _mockCache;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<ITwoFactorService> _mockTwoFactorService;
    private readonly Mock<IOtpService> _mockOtpService;
    private readonly Mock<IGeoLocationService> _mockGeoLocationService;
    private readonly IConfiguration _configuration;

    public AuthServiceLoginTests()
    {
        _mockUserManager = MockHelpers.CreateMockUserManager();
        _mockJwtService = new Mock<IJwtService>();
        _mockCache = MockHelpers.CreateMockDistributedCache();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockTwoFactorService = new Mock<ITwoFactorService>();
        _mockOtpService = new Mock<IOtpService>();
        _mockGeoLocationService = new Mock<IGeoLocationService>();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "App:BaseUrl", "http://localhost:3000" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentEmail_ShouldThrowException()
    {
        // Arrange
        var request = TestFixture.CreateLoginRequest(email: "nonexistent@example.com");

        _mockUserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync((ApplicationUser?)null);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task LoginAsync_WithLockedAccount_ShouldThrowException()
    {
        // Arrange
        var user = TestFixture.CreateUser(email: "test@example.com");
        user.IsLocked = true;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(10);

        var request = TestFixture.CreateLoginRequest(email: "test@example.com");

        _mockUserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("ACCOUNT_LOCKED*");
    }

    [Fact]
    public async Task LoginAsync_WithDisabledAccount_ShouldThrowException()
    {
        // Arrange
        var user = TestFixture.CreateUser(email: "test@example.com", isActive: false);
        var request = TestFixture.CreateLoginRequest(email: "test@example.com");

        _mockUserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("ACCOUNT_DISABLED");
    }

    [Fact]
    public async Task LoginAsync_WithUnverifiedEmail_ShouldThrowException()
    {
        // Arrange
        var user = TestFixture.CreateUser(email: "test@example.com", emailConfirmed: false);
        var request = TestFixture.CreateLoginRequest(email: "test@example.com", password: "SecureP@ss123");

        _mockUserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _mockUserManager.Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(true);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ShouldThrowException()
    {
        // Arrange
        var user = TestFixture.CreateUser(email: "test@example.com");
        var request = TestFixture.CreateLoginRequest(email: "test@example.com", password: "WrongPassword");

        _mockUserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _mockUserManager.Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(false);

        _mockUserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task LoginAsync_With2FAEnabled_ShouldReturnPartialToken()
    {
        // Arrange
        var user = TestFixture.CreateUser(email: "test@example.com", emailConfirmed: true);
        user.Is2faEnabled = true;

        var request = TestFixture.CreateLoginRequest(email: "test@example.com", password: "SecureP@ss123");

        _mockUserManager.Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _mockUserManager.Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(true);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act
        var result = await authService.LoginAsync(request, "127.0.0.1", "TestAgent");

        // Assert
        result.Should().BeOfType<TwoFactorRequiredResponse>();
        var twoFactorResponse = (TwoFactorRequiredResponse)result;
        twoFactorResponse.Requires2fa.Should().BeTrue();
        twoFactorResponse.PartialToken.Should().NotBeNullOrEmpty();
        twoFactorResponse.Methods.Should().Contain("totp");
    }

    private AuthService CreateAuthServiceWithMockedDbContext()
    {
        var mockDbContext = Substitute.For<EffortlessInsight.Api.Data.ApplicationDbContext>(
            new Microsoft.EntityFrameworkCore.DbContextOptions<EffortlessInsight.Api.Data.ApplicationDbContext>());

        return new AuthService(
            _mockUserManager.Object,
            mockDbContext,
            _mockJwtService.Object,
            _mockCache.Object,
            _mockEmailService.Object,
            _mockLogger.Object,
            _configuration,
            _mockTwoFactorService.Object,
            _mockOtpService.Object,
            _mockGeoLocationService.Object
        );
    }
}

public class AuthServiceEmailVerificationTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly Mock<IDistributedCache> _mockCache;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<ITwoFactorService> _mockTwoFactorService;
    private readonly Mock<IOtpService> _mockOtpService;
    private readonly Mock<IGeoLocationService> _mockGeoLocationService;
    private readonly IConfiguration _configuration;

    public AuthServiceEmailVerificationTests()
    {
        _mockUserManager = MockHelpers.CreateMockUserManager();
        _mockJwtService = new Mock<IJwtService>();
        _mockCache = new Mock<IDistributedCache>();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockTwoFactorService = new Mock<ITwoFactorService>();
        _mockOtpService = new Mock<IOtpService>();
        _mockGeoLocationService = new Mock<IGeoLocationService>();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "App:BaseUrl", "http://localhost:3000" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task VerifyEmailAsync_WithInvalidToken_ShouldThrowException()
    {
        // Arrange
        _mockCache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.VerifyEmailAsync("invalid-token");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("INVALID_TOKEN");
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenAlreadyVerified_ShouldThrowException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestFixture.CreateUser(id: userId, emailConfirmed: true);
        var token = "valid-token";

        var verificationData = new
        {
            UserId = userId,
            Email = user.Email,
            CreatedAt = DateTime.UtcNow
        };

        var cacheData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(verificationData));

        _mockCache.Setup(x => x.GetAsync($"email_verify:{token}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cacheData);

        _mockUserManager.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.VerifyEmailAsync(token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ALREADY_VERIFIED");
    }

    [Fact]
    public async Task VerifyEmailAsync_WithValidToken_ShouldVerifyEmail()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestFixture.CreateUser(id: userId, email: "test@example.com", emailConfirmed: false);
        var token = "valid-verification-token";

        var verificationData = new
        {
            UserId = userId,
            Email = user.Email,
            CreatedAt = DateTime.UtcNow
        };

        var cacheData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(verificationData));

        _mockCache.Setup(x => x.GetAsync($"email_verify:{token}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cacheData);

        _mockUserManager.Setup(x => x.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);

        _mockUserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act
        await authService.VerifyEmailAsync(token);

        // Assert
        _mockUserManager.Verify(x => x.UpdateAsync(
            It.Is<ApplicationUser>(u => u.EmailConfirmed == true)), Times.Once);

        _mockCache.Verify(x => x.RemoveAsync($"email_verify:{token}", It.IsAny<CancellationToken>()), Times.Once);
    }

    private AuthService CreateAuthServiceWithMockedDbContext()
    {
        var mockDbContext = Substitute.For<EffortlessInsight.Api.Data.ApplicationDbContext>(
            new Microsoft.EntityFrameworkCore.DbContextOptions<EffortlessInsight.Api.Data.ApplicationDbContext>());

        return new AuthService(
            _mockUserManager.Object,
            mockDbContext,
            _mockJwtService.Object,
            _mockCache.Object,
            _mockEmailService.Object,
            _mockLogger.Object,
            _configuration,
            _mockTwoFactorService.Object,
            _mockOtpService.Object,
            _mockGeoLocationService.Object
        );
    }
}

public class AuthServicePasswordTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly Mock<IDistributedCache> _mockCache;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<ITwoFactorService> _mockTwoFactorService;
    private readonly Mock<IOtpService> _mockOtpService;
    private readonly Mock<IGeoLocationService> _mockGeoLocationService;
    private readonly IConfiguration _configuration;

    public AuthServicePasswordTests()
    {
        _mockUserManager = MockHelpers.CreateMockUserManager();
        _mockJwtService = new Mock<IJwtService>();
        _mockCache = new Mock<IDistributedCache>();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockTwoFactorService = new Mock<ITwoFactorService>();
        _mockOtpService = new Mock<IOtpService>();
        _mockGeoLocationService = new Mock<IGeoLocationService>();

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "App:BaseUrl", "http://localhost:3000" }
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithExistingEmail_ShouldSendResetEmail()
    {
        // Arrange
        var user = TestFixture.CreateUser(email: "test@example.com");

        _mockUserManager.Setup(x => x.FindByEmailAsync("test@example.com"))
            .ReturnsAsync(user);

        _mockEmailService.Setup(x => x.SendTemplateAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>()))
            .Returns(Task.CompletedTask);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act
        await authService.ForgotPasswordAsync("test@example.com", "127.0.0.1");

        // Assert
        _mockEmailService.Verify(x => x.SendTemplateAsync(
            "test@example.com",
            "auth_password_reset",
            It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("reset_link") &&
                d.ContainsKey("user_name"))),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_WithNonExistingEmail_ShouldNotThrow()
    {
        // Arrange
        _mockUserManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert (should complete without throwing)
        var act = () => authService.ForgotPasswordAsync("nonexistent@example.com", "127.0.0.1");

        await act.Should().NotThrowAsync();

        // Should not send email
        _mockEmailService.Verify(x => x.SendTemplateAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithMismatchedPasswords_ShouldThrowException()
    {
        // Arrange
        var request = new ResetPasswordRequest("token", "Password1@", "Password2@");

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.ResetPasswordAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("PASSWORD_MISMATCH");
    }

    [Fact]
    public async Task ResetPasswordAsync_WithInvalidToken_ShouldThrowException()
    {
        // Arrange
        var request = new ResetPasswordRequest("invalid-token", "NewP@ss123", "NewP@ss123");

        _mockCache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.ResetPasswordAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("INVALID_TOKEN");
    }

    [Fact]
    public async Task ChangePasswordAsync_WithMismatchedPasswords_ShouldThrowException()
    {
        // Arrange
        var request = new ChangePasswordRequest("OldP@ss123", "NewP@ss123", "DifferentP@ss123");

        var authService = CreateAuthServiceWithMockedDbContext();

        // Act & Assert
        var act = () => authService.ChangePasswordAsync(Guid.NewGuid(), request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("PASSWORD_MISMATCH");
    }

    private AuthService CreateAuthServiceWithMockedDbContext()
    {
        var mockDbContext = Substitute.For<EffortlessInsight.Api.Data.ApplicationDbContext>(
            new Microsoft.EntityFrameworkCore.DbContextOptions<EffortlessInsight.Api.Data.ApplicationDbContext>());

        return new AuthService(
            _mockUserManager.Object,
            mockDbContext,
            _mockJwtService.Object,
            _mockCache.Object,
            _mockEmailService.Object,
            _mockLogger.Object,
            _configuration,
            _mockTwoFactorService.Object,
            _mockOtpService.Object,
            _mockGeoLocationService.Object
        );
    }
}
