using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, string ipAddress, string? userAgent);
    Task<object> LoginAsync(LoginRequest request, string ipAddress, string? userAgent);
    Task<TokenResponse> RefreshTokenAsync(string refreshToken, string ipAddress, string? userAgent);
    Task VerifyEmailAsync(string token);
    Task ForgotPasswordAsync(string email, string ipAddress);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task LogoutAsync(Guid userId, string? refreshTokenJti, bool allDevices);
}
