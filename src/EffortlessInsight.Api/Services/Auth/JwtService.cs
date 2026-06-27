using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace EffortlessInsight.Api.Services.Auth;

public class JwtSettings
{
    /// <summary>
    /// RSA private key in PEM format for RS256 signing.
    /// For development, can fall back to symmetric key if not provided.
    /// </summary>
    public string? RsaPrivateKey { get; set; }

    /// <summary>
    /// RSA public key in PEM format for RS256 verification.
    /// </summary>
    public string? RsaPublicKey { get; set; }

    /// <summary>
    /// Fallback symmetric secret (only used if RSA keys not configured).
    /// DEPRECATED: Use RSA keys in production.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Whether to use RS256 (asymmetric) or HS256 (symmetric) signing.
    /// Defaults to true if RSA keys are configured.
    /// </summary>
    public bool UseAsymmetricSigning { get; set; } = true;

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
    private readonly RsaSecurityKey? _rsaPrivateKey;
    private readonly RsaSecurityKey? _rsaPublicKey;
    private readonly bool _useAsymmetricSigning;

    public JwtService(IConfiguration configuration, ILogger<JwtService> logger)
    {
        _settings = new JwtSettings();
        configuration.GetSection("Jwt").Bind(_settings);
        _logger = logger;

        // Initialize RSA keys if configured
        if (!string.IsNullOrEmpty(_settings.RsaPrivateKey) && !string.IsNullOrEmpty(_settings.RsaPublicKey))
        {
            try
            {
                var rsaPrivate = RSA.Create();
                rsaPrivate.ImportFromPem(_settings.RsaPrivateKey.AsSpan());
                _rsaPrivateKey = new RsaSecurityKey(rsaPrivate);

                var rsaPublic = RSA.Create();
                rsaPublic.ImportFromPem(_settings.RsaPublicKey.AsSpan());
                _rsaPublicKey = new RsaSecurityKey(rsaPublic);

                _useAsymmetricSigning = true;
                _logger.LogInformation("JWT configured with RS256 asymmetric signing");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load RSA keys, falling back to symmetric signing");
                _useAsymmetricSigning = false;
            }
        }
        else
        {
            _useAsymmetricSigning = false;
            if (_settings.UseAsymmetricSigning)
            {
                _logger.LogWarning("RS256 signing requested but RSA keys not configured. Using HS256 symmetric signing. Configure RSA keys for production.");
            }
        }
    }

    public string GenerateAccessToken(ApplicationUser user, Organization? organization, string? roleOverride = null, bool isExternal = false)
    {
        // Use roleOverride if provided, otherwise user.Role, default to "member" if both null
        var role = roleOverride ?? user.Role ?? "member";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.Name),
            new("role", role),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (organization != null)
        {
            claims.Add(new Claim("org_id", organization.Id.ToString()));
            claims.Add(new Claim("org_name", organization.Name));
        }

        // Add external collaborator claim
        claims.Add(new Claim("is_external", isExternal.ToString().ToLower()));

        // Add email verified claim
        claims.Add(new Claim("email_verified", user.EmailConfirmed.ToString().ToLower()));

        SigningCredentials credentials;
        if (_useAsymmetricSigning && _rsaPrivateKey != null)
        {
            // Use RS256 asymmetric signing (recommended for production)
            credentials = new SigningCredentials(_rsaPrivateKey, SecurityAlgorithms.RsaSha256);
        }
        else
        {
            // Fallback to HS256 symmetric signing
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
            credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }

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

            SecurityKey signingKey;
            if (_useAsymmetricSigning && _rsaPublicKey != null)
            {
                // Use RSA public key for RS256 verification
                signingKey = _rsaPublicKey;
            }
            else
            {
                // Use symmetric key for HS256 verification
                signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
            }

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _settings.Issuer,
                ValidAudience = _settings.Audience,
                IssuerSigningKey = signingKey,
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
