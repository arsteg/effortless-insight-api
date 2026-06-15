using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EffortlessInsight.Api.Services.Admin;

public class AdminJwtService : IAdminJwtService
{
    private readonly AdminAuthOptions _options;
    private readonly ILogger<AdminJwtService> _logger;
    private readonly SymmetricSecurityKey _signingKey;

    // Custom claim types for admin tokens
    public const string AdminRoleClaim = "admin_role";
    public const string PermissionsClaim = "permissions";
    public const string MfaVerifiedClaim = "mfa_verified";
    public const string SessionIdClaim = "session_id";
    public const string AdminIdClaim = "admin_id";

    public AdminJwtService(
        IOptions<AdminAuthOptions> options,
        ILogger<AdminJwtService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
    }

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(AdminUser admin, string? sessionId = null)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, admin.Email),
            new(JwtRegisteredClaimNames.Name, admin.Name),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(AdminIdClaim, admin.Id.ToString()),
            new(AdminRoleClaim, admin.Role),
            new(MfaVerifiedClaim, admin.MfaEnabled.ToString().ToLower(), ClaimValueTypes.Boolean)
        };

        // Add permissions as a single claim with comma-separated values
        if (admin.Permissions.Count > 0)
        {
            claims.Add(new Claim(PermissionsClaim, string.Join(",", admin.Permissions)));
        }

        // Add session ID if provided
        if (!string.IsNullOrEmpty(sessionId))
        {
            claims.Add(new Claim(SessionIdClaim, sessionId));
        }

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _options.JwtIssuer,
            Audience = _options.JwtAudience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return (tokenString, expiresAt);
    }

    public (string Token, DateTime ExpiresAt) GenerateRefreshToken()
    {
        var expiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpiryDays);
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        return (token, expiresAt);
    }

    public AdminTokenClaims? ValidateAccessToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _options.JwtIssuer,
                ValidAudience = _options.JwtAudience,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            var adminIdClaim = principal.FindFirst(AdminIdClaim)?.Value ??
                               principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!Guid.TryParse(adminIdClaim, out var adminId))
            {
                return null;
            }

            var permissionsClaim = principal.FindFirst(PermissionsClaim)?.Value;
            var permissions = string.IsNullOrEmpty(permissionsClaim)
                ? new List<string>()
                : permissionsClaim.Split(',').ToList();

            return new AdminTokenClaims
            {
                AdminId = adminId,
                Email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? string.Empty,
                Name = principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? string.Empty,
                Role = principal.FindFirst(AdminRoleClaim)?.Value ?? string.Empty,
                Permissions = permissions,
                MfaVerified = bool.TryParse(principal.FindFirst(MfaVerifiedClaim)?.Value, out var mfa) && mfa,
                SessionId = principal.FindFirst(SessionIdClaim)?.Value,
                IssuedAt = jwtToken.ValidFrom,
                ExpiresAt = jwtToken.ValidTo
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Admin token validation failed");
            return null;
        }
    }
}
