using EffortlessInsight.Api.Data.Entities.Admin;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for generating and validating admin JWT tokens.
/// </summary>
public interface IAdminJwtService
{
    /// <summary>
    /// Generate access token for admin user.
    /// </summary>
    (string Token, DateTime ExpiresAt) GenerateAccessToken(AdminUser admin, string? sessionId = null);

    /// <summary>
    /// Generate refresh token.
    /// </summary>
    (string Token, DateTime ExpiresAt) GenerateRefreshToken();

    /// <summary>
    /// Validate access token and extract claims.
    /// </summary>
    AdminTokenClaims? ValidateAccessToken(string token);
}

/// <summary>
/// Claims extracted from admin JWT token.
/// </summary>
public record AdminTokenClaims
{
    public Guid AdminId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public List<string> Permissions { get; init; } = [];
    public bool MfaVerified { get; init; }
    public string? SessionId { get; init; }
    public DateTime IssuedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}
