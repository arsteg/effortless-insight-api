using System.Security.Claims;
using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.Auth;

public interface IJwtService
{
    string GenerateAccessToken(ApplicationUser user, Organization? organization);
    (string Token, string Jti, DateTime ExpiresAt) GenerateRefreshToken(bool rememberMe = false);
    ClaimsPrincipal? ValidateAccessToken(string token);
    int GetAccessTokenExpiryMinutes();
}
