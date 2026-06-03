using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class JwtServiceTests
{
    private readonly JwtService _jwtService;
    private readonly IConfiguration _configuration;

    public JwtServiceTests()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Jwt:Secret", "your-super-secret-jwt-key-change-in-production-min-32-chars" },
            { "Jwt:Issuer", "effortlessinsight-test" },
            { "Jwt:Audience", "effortlessinsight-api-test" },
            { "Jwt:AccessTokenExpiryMinutes", "15" },
            { "Jwt:RefreshTokenExpiryDays", "7" },
            { "Jwt:RememberMeRefreshTokenExpiryDays", "30" }
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var logger = new Mock<ILogger<JwtService>>();
        _jwtService = new JwtService(_configuration, logger.Object);
    }

    #region GenerateAccessToken Tests

    [Fact]
    public void GenerateAccessToken_ShouldReturnValidJwtToken()
    {
        // Arrange
        var user = TestFixture.CreateUser(email: "test@example.com", name: "Test User");
        var organization = TestFixture.CreateOrganization(name: "Test Org");

        // Act
        var token = _jwtService.GenerateAccessToken(user, organization);

        // Assert
        token.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Should().NotBeNull();
        jwtToken.Issuer.Should().Be("effortlessinsight-test");
        jwtToken.Audiences.Should().Contain("effortlessinsight-api-test");
    }

    [Fact]
    public void GenerateAccessToken_ShouldIncludeUserClaims()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestFixture.CreateUser(id: userId, email: "test@example.com", name: "Test User", role: "owner");
        var organization = TestFixture.CreateOrganization(name: "Test Org");

        // Act
        var token = _jwtService.GenerateAccessToken(user, organization);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Subject.Should().Be(userId.ToString());
        jwtToken.Claims.Should().Contain(c => c.Type == "email" && c.Value == "test@example.com");
        jwtToken.Claims.Should().Contain(c => c.Type == "name" && c.Value == "Test User");
        jwtToken.Claims.Should().Contain(c => c.Type == "role" && c.Value == "owner");
    }

    [Fact]
    public void GenerateAccessToken_ShouldIncludeOrganizationClaims()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var user = TestFixture.CreateUser();
        var organization = TestFixture.CreateOrganization(id: orgId, name: "Test Organization");

        // Act
        var token = _jwtService.GenerateAccessToken(user, organization);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Claims.Should().Contain(c => c.Type == "org_id" && c.Value == orgId.ToString());
        jwtToken.Claims.Should().Contain(c => c.Type == "org_name" && c.Value == "Test Organization");
    }

    [Fact]
    public void GenerateAccessToken_WithNullOrganization_ShouldNotIncludeOrgClaims()
    {
        // Arrange
        var user = TestFixture.CreateUser();

        // Act
        var token = _jwtService.GenerateAccessToken(user, null);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Claims.Should().NotContain(c => c.Type == "org_id");
        jwtToken.Claims.Should().NotContain(c => c.Type == "org_name");
    }

    [Fact]
    public void GenerateAccessToken_ShouldSetCorrectExpiry()
    {
        // Arrange
        var user = TestFixture.CreateUser();

        // Act
        var token = _jwtService.GenerateAccessToken(user, null);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        var expectedExpiry = DateTime.UtcNow.AddMinutes(15);
        jwtToken.ValidTo.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(1));
    }

    #endregion

    #region GenerateRefreshToken Tests

    [Fact]
    public void GenerateRefreshToken_ShouldReturnTokenWithJti()
    {
        // Act
        var (token, jti, expiresAt) = _jwtService.GenerateRefreshToken();

        // Assert
        token.Should().NotBeNullOrEmpty();
        jti.Should().NotBeNullOrEmpty();
        token.Should().StartWith(jti);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldSetCorrectExpiry_WhenNotRememberMe()
    {
        // Act
        var (_, _, expiresAt) = _jwtService.GenerateRefreshToken(rememberMe: false);

        // Assert
        var expectedExpiry = DateTime.UtcNow.AddDays(7);
        expiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateRefreshToken_ShouldSetExtendedExpiry_WhenRememberMe()
    {
        // Act
        var (_, _, expiresAt) = _jwtService.GenerateRefreshToken(rememberMe: true);

        // Assert
        var expectedExpiry = DateTime.UtcNow.AddDays(30);
        expiresAt.Should().BeCloseTo(expectedExpiry, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateRefreshToken_ShouldGenerateUniqueTokens()
    {
        // Act
        var (token1, jti1, _) = _jwtService.GenerateRefreshToken();
        var (token2, jti2, _) = _jwtService.GenerateRefreshToken();

        // Assert
        token1.Should().NotBe(token2);
        jti1.Should().NotBe(jti2);
    }

    #endregion

    #region ValidateAccessToken Tests

    [Fact]
    public void ValidateAccessToken_WithValidToken_ShouldReturnPrincipal()
    {
        // Arrange
        var user = TestFixture.CreateUser();
        var token = _jwtService.GenerateAccessToken(user, null);

        // Act
        var principal = _jwtService.ValidateAccessToken(token);

        // Assert
        principal.Should().NotBeNull();
        principal!.Identity.Should().NotBeNull();
        principal.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void ValidateAccessToken_WithInvalidToken_ShouldReturnNull()
    {
        // Act
        var principal = _jwtService.ValidateAccessToken("invalid-token");

        // Assert
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WithMalformedToken_ShouldReturnNull()
    {
        // Act
        var principal = _jwtService.ValidateAccessToken("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.invalid");

        // Assert
        principal.Should().BeNull();
    }

    #endregion

    #region GetAccessTokenExpiryMinutes Tests

    [Fact]
    public void GetAccessTokenExpiryMinutes_ShouldReturnConfiguredValue()
    {
        // Act
        var expiryMinutes = _jwtService.GetAccessTokenExpiryMinutes();

        // Assert
        expiryMinutes.Should().Be(15);
    }

    #endregion
}
