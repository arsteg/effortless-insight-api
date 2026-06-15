using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs.Admin;
using OneOf;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Result type for admin login operations.
/// Can be: successful login response, MFA required response, or error string.
/// </summary>
public class AdminLoginResult : OneOfBase<AdminLoginResponse, AdminMfaRequiredResponse, string>
{
    AdminLoginResult(OneOf<AdminLoginResponse, AdminMfaRequiredResponse, string> input) : base(input) { }
    public static implicit operator AdminLoginResult(AdminLoginResponse response) => new(response);
    public static implicit operator AdminLoginResult(AdminMfaRequiredResponse response) => new(response);
    public static implicit operator AdminLoginResult(string error) => new(error);
}

/// <summary>
/// Result type for MFA verification operations.
/// Can be: successful login response or error string.
/// </summary>
public class AdminMfaResult : OneOfBase<AdminLoginResponse, string>
{
    AdminMfaResult(OneOf<AdminLoginResponse, string> input) : base(input) { }
    public static implicit operator AdminMfaResult(AdminLoginResponse response) => new(response);
    public static implicit operator AdminMfaResult(string error) => new(error);
}

/// <summary>
/// Result type for MFA setup operations.
/// Can be: successful setup response or error string.
/// </summary>
public class AdminMfaSetupResult : OneOfBase<AdminMfaSetupResponse, string>
{
    AdminMfaSetupResult(OneOf<AdminMfaSetupResponse, string> input) : base(input) { }
    public static implicit operator AdminMfaSetupResult(AdminMfaSetupResponse response) => new(response);
    public static implicit operator AdminMfaSetupResult(string error) => new(error);
}

/// <summary>
/// Result type for password change operations.
/// Can be: success (unit) or error string.
/// </summary>
public class AdminPasswordResult : OneOfBase<bool, string>
{
    AdminPasswordResult(OneOf<bool, string> input) : base(input) { }
    public static implicit operator AdminPasswordResult(bool success) => new(success);
    public static implicit operator AdminPasswordResult(string error) => new(error);
}

/// <summary>
/// Service for admin authentication operations.
/// </summary>
public interface IAdminAuthService
{
    /// <summary>
    /// Authenticate admin with email and password.
    /// Returns MFA session token if MFA is enabled.
    /// </summary>
    Task<AdminLoginResult> LoginAsync(AdminLoginRequest request, string? ipAddress, string? userAgent);

    /// <summary>
    /// Verify MFA code to complete login.
    /// </summary>
    Task<AdminMfaResult> VerifyMfaAsync(AdminMfaVerifyRequest request, string? ipAddress, string? userAgent);

    /// <summary>
    /// Refresh access token using refresh token.
    /// </summary>
    Task<AdminMfaResult> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// Logout admin and invalidate session.
    /// </summary>
    Task LogoutAsync(string sessionId);

    /// <summary>
    /// Logout admin from all sessions.
    /// </summary>
    Task LogoutAllSessionsAsync(Guid adminId);

    /// <summary>
    /// Get current admin user by ID.
    /// </summary>
    Task<AdminUserDto?> GetAdminByIdAsync(Guid adminId);

    /// <summary>
    /// Get current admin user by email.
    /// </summary>
    Task<AdminUser?> GetAdminByEmailAsync(string email);

    /// <summary>
    /// Setup MFA for admin account.
    /// </summary>
    Task<AdminMfaSetupResult> SetupMfaAsync(Guid adminId);

    /// <summary>
    /// Enable MFA after verifying setup code.
    /// </summary>
    Task<bool> EnableMfaAsync(Guid adminId, string code);

    /// <summary>
    /// Disable MFA for admin account.
    /// </summary>
    Task<bool> DisableMfaAsync(Guid adminId, string password);

    /// <summary>
    /// Change admin password.
    /// </summary>
    Task<AdminPasswordResult> ChangePasswordAsync(Guid adminId, AdminChangePasswordRequest request);

    /// <summary>
    /// Validate admin session.
    /// </summary>
    Task<bool> ValidateSessionAsync(string sessionId, string? ipAddress = null);

    /// <summary>
    /// Check if IP is whitelisted for admin.
    /// </summary>
    bool IsIpWhitelisted(AdminUser admin, string? ipAddress);

    /// <summary>
    /// Get all active sessions for admin.
    /// </summary>
    Task<List<AdminSession>> GetActiveSessionsAsync(Guid adminId);

    /// <summary>
    /// Revoke a specific session.
    /// </summary>
    Task<bool> RevokeSessionAsync(Guid adminId, string sessionId);
}
