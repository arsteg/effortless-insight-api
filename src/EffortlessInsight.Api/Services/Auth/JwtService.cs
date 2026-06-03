using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace EffortlessInsight.Api.Services.Auth;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
    public int RememberMeRefreshTokenExpiryDays { get; set; } = 30;
}

public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;
    private readonly ILogger<JwtService> _logger;

    public JwtService(IConfiguration configuration, ILogger<JwtService> logger)
    {
        _settings = new JwtSettings();
        configuration.GetSection("Jwt").Bind(_settings);
        _logger = logger;
    }

    public string GenerateAccessToken(ApplicationUser user, Organization? organization)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.Name),
            new("role", user.Role),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (organization != null)
        {
            claims.Add(new Claim("org_id", organization.Id.ToString()));
            claims.Add(new Claim("org_name", organization.Name));
        }

        // Add email verified claim
        claims.Add(new Claim("email_verified", user.EmailConfirmed.ToString().ToLower()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string Jti, DateTime ExpiresAt) GenerateRefreshToken(bool rememberMe = false)
    {
        var jti = Guid.NewGuid().ToString();
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        var token = $"{jti}:{Convert.ToBase64String(randomBytes)}";
        var expiryDays = rememberMe ? _settings.RememberMeRefreshTokenExpiryDays : _settings.RefreshTokenExpiryDays;
        var expiresAt = DateTime.UtcNow.AddDays(expiryDays);

        return (token, jti, expiresAt);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_settings.Secret);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _settings.Issuer,
                ValidAudience = _settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    public int GetAccessTokenExpiryMinutes() => _settings.AccessTokenExpiryMinutes;
}
