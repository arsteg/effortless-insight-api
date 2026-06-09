using System.Security.Claims;
using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.Auth;

public interface IJwtService
{
    string GenerateAccessToken(ApplicationUser user, Organization? organization, string? roleOverride = null, bool isExternal = false);
    (string Token, string Jti, DateTime ExpiresAt) GenerateRefreshToken(bool rememberMe = false);
    ClaimsPrincipal? ValidateAccessToken(string token);
    int GetAccessTokenExpiryMinutes();
}
