using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs.Admin;
using EffortlessInsight.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using OtpNet;

namespace EffortlessInsight.Api.Services.Admin;

public class AdminAuthService : IAdminAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAdminJwtService _jwtService;
    private readonly IAdminAuditService _auditService;
    private readonly IAdminSessionService _sessionService;
    private readonly IAdminMfaService _mfaService;
    private readonly IDistributedCache _cache;
    private readonly AdminAuthOptions _options;
    private readonly ILogger<AdminAuthService> _logger;

    private const string MFA_SESSION_PREFIX = "admin:mfa:";
    private const string MFA_SETUP_PREFIX = "admin:mfa_setup:";

    public AdminAuthService(
        ApplicationDbContext dbContext,
        IAdminJwtService jwtService,
        IAdminAuditService auditService,
        IAdminSessionService sessionService,
        IAdminMfaService mfaService,
        IDistributedCache cache,
        IOptions<AdminAuthOptions> options,
        ILogger<AdminAuthService> logger)
    {
        _dbContext = dbContext;
        _jwtService = jwtService;
        _auditService = auditService;
        _sessionService = sessionService;
        _mfaService = mfaService;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdminLoginResult> LoginAsync(AdminLoginRequest request, string? ipAddress, string? userAgent)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();
        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.EmailNormalized == normalizedEmail && a.DeletedAt == null);

        if (admin == null)
        {
            _logger.LogWarning("Admin login failed: email not found - {Email}", request.Email);
            return AdminAuthErrors.InvalidCredentials;
        }

        // Check if account is active
        if (!admin.IsActive)
        {
            await LogFailedLoginAsync(admin, ipAddress, userAgent, "Account disabled");
            return AdminAuthErrors.AccountDisabled;
        }

        // Check if account is locked
        if (admin.IsLocked && admin.LockedUntil > DateTime.UtcNow)
        {
            await LogFailedLoginAsync(admin, ipAddress, userAgent, "Account locked");
            return "ACCOUNT_LOCKED";
        }

        // Check IP whitelist
        if (!IsIpWhitelisted(admin, ipAddress))
        {
            await LogFailedLoginAsync(admin, ipAddress, userAgent, "IP not whitelisted");
            return "IP_NOT_WHITELISTED";
        }

        // Verify password
        if (!VerifyPassword(request.Password, admin.PasswordHash))
        {
            await HandleFailedLoginAsync(admin, ipAddress, userAgent);
            return AdminAuthErrors.InvalidCredentials;
        }

        // Clear failed attempts on successful password verification
        if (admin.FailedLoginAttempts > 0 || admin.IsLocked)
        {
            admin.FailedLoginAttempts = 0;
            admin.IsLocked = false;
            admin.LockedUntil = null;
        }

        // Check if MFA is required
        if (admin.MfaEnabled)
        {
            var mfaSessionToken = GenerateSecureToken();
            var mfaExpiresAt = DateTime.UtcNow.AddMinutes(5);

            // Store MFA session in cache
            await _cache.SetStringAsync(
                $"{MFA_SESSION_PREFIX}{mfaSessionToken}",
                admin.Id.ToString(),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = mfaExpiresAt
                });

            await _dbContext.SaveChangesAsync();

            return new AdminMfaRequiredResponse
            {
                MfaRequired = true,
                SessionToken = mfaSessionToken,
                ExpiresAt = mfaExpiresAt
            };
        }

        // Complete login without MFA
        return await CompleteLoginAsync(admin, ipAddress, userAgent);
    }

    public async Task<AdminMfaResult> VerifyMfaAsync(AdminMfaVerifyRequest request, string? ipAddress, string? userAgent)
    {
        // Get admin ID from MFA session
        var adminIdStr = await _cache.GetStringAsync($"{MFA_SESSION_PREFIX}{request.SessionToken}");
        if (string.IsNullOrEmpty(adminIdStr) || !Guid.TryParse(adminIdStr, out var adminId))
        {
            return "INVALID_MFA_SESSION";
        }

        var admin = await _dbContext.AdminUsers.FindAsync(adminId);
        if (admin == null || !admin.IsActive)
        {
            return AdminAuthErrors.InvalidCredentials;
        }

        // Verify TOTP code
        if (admin.MfaSecretEncrypted != null && _mfaService.VerifyCodeWithEncryptedSecret(admin.MfaSecretEncrypted, request.Code))
        {
            // Remove MFA session
            await _cache.RemoveAsync($"{MFA_SESSION_PREFIX}{request.SessionToken}");
            return await CompleteLoginAsync(admin, ipAddress, userAgent);
        }

        // Check backup codes
        if (admin.BackupCodesHash != null)
        {
            var codeIndex = _mfaService.VerifyBackupCode(request.Code, admin.BackupCodesHash.ToList());
            if (codeIndex >= 0)
            {
                // Remove used backup code
                var codesList = admin.BackupCodesHash.ToList();
                codesList.RemoveAt(codeIndex);
                admin.BackupCodesHash = codesList.ToArray();
                await _dbContext.SaveChangesAsync();

                // Remove MFA session
                await _cache.RemoveAsync($"{MFA_SESSION_PREFIX}{request.SessionToken}");
                return await CompleteLoginAsync(admin, ipAddress, userAgent);
            }
        }

        await LogFailedLoginAsync(admin, ipAddress, userAgent, "Invalid MFA code");
        return "INVALID_MFA_CODE";
    }

    public async Task<AdminMfaResult> RefreshTokenAsync(string refreshToken)
    {
        // Find session with this refresh token
        var session = await _dbContext.AdminSessions
            .Include(s => s.AdminUser)
            .FirstOrDefaultAsync(s =>
                s.RefreshToken == refreshToken &&
                s.IsActive &&
                s.RefreshTokenExpiresAt > DateTime.UtcNow);

        if (session == null || session.AdminUser == null)
        {
            return AdminAuthErrors.InvalidRefreshToken;
        }

        var admin = session.AdminUser;
        if (!admin.IsActive)
        {
            return AdminAuthErrors.AccountDisabled;
        }

        // Generate new tokens
        var (accessToken, accessExpiry) = _jwtService.GenerateAccessToken(admin, session.Id);
        var (newRefreshToken, refreshExpiry) = _jwtService.GenerateRefreshToken();

        // Update session with new refresh token
        session.RefreshToken = newRefreshToken;
        session.RefreshTokenExpiresAt = refreshExpiry;
        session.LastActivityAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new AdminLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            AccessTokenExpiresAt = accessExpiry,
            RefreshTokenExpiresAt = refreshExpiry,
            User = MapToDto(admin)
        };
    }

    public async Task LogoutAsync(string sessionId)
    {
        await _sessionService.InvalidateSessionAsync(sessionId);
    }

    public async Task LogoutAllSessionsAsync(Guid adminId)
    {
        await _sessionService.InvalidateAllSessionsAsync(adminId);

        await _auditService.LogAsync(
            adminId,
            AdminAuditActions.Logout,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            "Admin logged out from all sessions");
    }

    public async Task<AdminUserDto?> GetAdminByIdAsync(Guid adminId)
    {
        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        return admin == null ? null : MapToDto(admin);
    }

    public async Task<AdminUser?> GetAdminByEmailAsync(string email)
    {
        var normalizedEmail = email.ToUpperInvariant();
        return await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.EmailNormalized == normalizedEmail && a.DeletedAt == null);
    }

    public async Task<AdminMfaSetupResult> SetupMfaAsync(Guid adminId)
    {
        var admin = await _dbContext.AdminUsers.FindAsync(adminId);
        if (admin == null)
        {
            return "ADMIN_NOT_FOUND";
        }

        if (admin.MfaEnabled)
        {
            return "MFA_ALREADY_ENABLED";
        }

        // Generate setup data
        var (secret, qrCodeUri, backupCodes) = _mfaService.GenerateSetupData(admin.Email);

        // Store temporarily until verified
        await _cache.SetStringAsync(
            $"{MFA_SETUP_PREFIX}{adminId}",
            secret,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = DateTime.UtcNow.AddMinutes(15)
            });

        // Hash backup codes
        var hashedCodes = _mfaService.HashBackupCodes(backupCodes);
        admin.BackupCodesHash = hashedCodes.ToArray();
        await _dbContext.SaveChangesAsync();

        return new AdminMfaSetupResponse
        {
            Secret = secret,
            QrCodeUri = qrCodeUri,
            BackupCodes = backupCodes
        };
    }

    public async Task<bool> EnableMfaAsync(Guid adminId, string code)
    {
        var admin = await _dbContext.AdminUsers.FindAsync(adminId);
        if (admin == null)
        {
            return false;
        }

        // Get pending secret from cache
        var secret = await _cache.GetStringAsync($"{MFA_SETUP_PREFIX}{adminId}");
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        // Verify the code
        if (!_mfaService.VerifyCode(secret, code))
        {
            return false;
        }

        // Enable MFA
        admin.MfaSecretEncrypted = _mfaService.EncryptSecret(secret);
        admin.MfaEnabled = true;
        admin.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Remove setup cache
        await _cache.RemoveAsync($"{MFA_SETUP_PREFIX}{adminId}");

        await _auditService.LogAsync(
            adminId,
            AdminAuditActions.MfaEnabled,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            "MFA enabled for admin account");

        return true;
    }

    public async Task<bool> DisableMfaAsync(Guid adminId, string password)
    {
        var admin = await _dbContext.AdminUsers.FindAsync(adminId);
        if (admin == null || !admin.MfaEnabled)
        {
            return false;
        }

        // Verify password
        if (!VerifyPassword(password, admin.PasswordHash))
        {
            return false;
        }

        admin.MfaEnabled = false;
        admin.MfaSecretEncrypted = null;
        admin.BackupCodesHash = null;
        admin.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Invalidate all sessions for security
        await _sessionService.InvalidateAllSessionsAsync(adminId);

        await _auditService.LogAsync(
            adminId,
            AdminAuditActions.MfaDisabled,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            "MFA disabled for admin account");

        return true;
    }

    public async Task<AdminPasswordResult> ChangePasswordAsync(Guid adminId, AdminChangePasswordRequest request)
    {
        var admin = await _dbContext.AdminUsers.FindAsync(adminId);
        if (admin == null)
        {
            return "ADMIN_NOT_FOUND";
        }

        // Verify current password
        if (!VerifyPassword(request.CurrentPassword, admin.PasswordHash))
        {
            return "INVALID_CURRENT_PASSWORD";
        }

        // Validate new password
        if (request.NewPassword.Length < _options.PasswordMinLength)
        {
            return "PASSWORD_TOO_SHORT";
        }

        // TODO: Add password history check
        // TODO: Add password complexity check

        // Hash new password
        admin.PasswordHash = HashPassword(request.NewPassword);
        admin.PasswordChangedAt = DateTime.UtcNow;
        admin.MustChangePassword = false;
        admin.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Invalidate all other sessions
        var currentSessionId = ""; // Would come from current context
        await _sessionService.InvalidateAllSessionsAsync(adminId, currentSessionId);

        await _auditService.LogAsync(
            adminId,
            AdminAuditActions.PasswordChanged,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            "Admin password changed");

        return true;
    }

    public async Task<bool> ValidateSessionAsync(string sessionId, string? ipAddress = null)
    {
        return await _sessionService.ValidateAndRefreshAsync(sessionId, ipAddress);
    }

    public bool IsIpWhitelisted(AdminUser admin, string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
        {
            return false;
        }

        // Check global whitelist first if enabled
        if (_options.EnableIpWhitelisting && _options.GlobalIpWhitelist.Count > 0)
        {
            if (!_options.GlobalIpWhitelist.Contains(ipAddress) &&
                !_options.GlobalIpWhitelist.Any(cidr => IsIpInCidr(ipAddress, cidr)))
            {
                return false;
            }
        }

        // If no per-admin whitelist configured, allow all (or use global only)
        if (admin.IpWhitelist == null || admin.IpWhitelist.Count == 0)
        {
            return true;
        }

        // Check per-admin whitelist
        return admin.IpWhitelist.Contains(ipAddress) ||
               admin.IpWhitelist.Any(cidr => IsIpInCidr(ipAddress, cidr));
    }

    public async Task<List<AdminSession>> GetActiveSessionsAsync(Guid adminId)
    {
        return await _sessionService.GetActiveSessionsAsync(adminId);
    }

    public async Task<bool> RevokeSessionAsync(Guid adminId, string sessionId)
    {
        var sessions = await GetActiveSessionsAsync(adminId);
        if (!sessions.Any(s => s.Id == sessionId))
        {
            return false;
        }

        await _sessionService.InvalidateSessionAsync(sessionId);
        return true;
    }

    // ============================================================================
    // Private Helper Methods
    // ============================================================================

    private async Task<AdminLoginResponse> CompleteLoginAsync(AdminUser admin, string? ipAddress, string? userAgent)
    {
        // Create session
        var session = await _sessionService.CreateSessionAsync(admin.Id, ipAddress ?? "", userAgent ?? "");

        // Generate tokens
        var (accessToken, accessExpiry) = _jwtService.GenerateAccessToken(admin, session.Id);
        var (refreshToken, refreshExpiry) = _jwtService.GenerateRefreshToken();

        // Update session with refresh token
        session.RefreshToken = refreshToken;
        session.RefreshTokenExpiresAt = refreshExpiry;
        await _dbContext.SaveChangesAsync();

        // Update last login
        admin.LastLoginAt = DateTime.UtcNow;
        admin.LastLoginIp = ipAddress;
        admin.LastLoginUserAgent = userAgent;
        await _dbContext.SaveChangesAsync();

        // Audit log
        await _auditService.LogAsync(
            admin.Id,
            AdminAuditActions.Login,
            AuditTargetTypes.AdminUser,
            admin.Id.ToString(),
            "Admin logged in successfully",
            new Dictionary<string, object>
            {
                ["ip_address"] = ipAddress ?? "",
                ["user_agent"] = userAgent ?? "",
                ["session_id"] = session.Id
            },
            ipAddress,
            userAgent,
            session.Id);

        return new AdminLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessExpiry,
            RefreshTokenExpiresAt = refreshExpiry,
            User = MapToDto(admin)
        };
    }

    private async Task HandleFailedLoginAsync(AdminUser admin, string? ipAddress, string? userAgent)
    {
        admin.FailedLoginAttempts++;
        admin.LastFailedLoginAt = DateTime.UtcNow;

        if (admin.FailedLoginAttempts >= _options.MaxFailedAttempts)
        {
            admin.IsLocked = true;
            admin.LockedUntil = DateTime.UtcNow.AddMinutes(_options.LockoutDurationMinutes);
            _logger.LogWarning("Admin account locked due to failed attempts: {Email}", admin.Email);
        }

        await _dbContext.SaveChangesAsync();
        await LogFailedLoginAsync(admin, ipAddress, userAgent, "Invalid password");
    }

    private async Task LogFailedLoginAsync(AdminUser admin, string? ipAddress, string? userAgent, string reason)
    {
        await _auditService.LogAsync(
            admin.Id,
            AdminAuditActions.LoginFailed,
            AuditTargetTypes.AdminUser,
            admin.Id.ToString(),
            $"Login failed: {reason}",
            new Dictionary<string, object>
            {
                ["ip_address"] = ipAddress ?? "",
                ["user_agent"] = userAgent ?? "",
                ["reason"] = reason
            },
            ipAddress,
            userAgent,
            outcome: AuditOutcomes.Failure);
    }

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static bool IsIpInCidr(string ipAddress, string cidr)
    {
        // Simple CIDR check - in production use a proper library
        if (!cidr.Contains('/'))
        {
            return ipAddress == cidr;
        }

        // For complex CIDR matching, use System.Net.IPNetwork or similar
        return false;
    }

    private static AdminUserDto MapToDto(AdminUser admin) => new()
    {
        Id = admin.Id,
        Email = admin.Email,
        Name = admin.Name,
        AvatarUrl = admin.AvatarUrl,
        Role = admin.Role,
        Permissions = admin.Permissions,
        MfaEnabled = admin.MfaEnabled,
        IsActive = admin.IsActive,
        LastLoginAt = admin.LastLoginAt,
        CreatedAt = admin.CreatedAt
    };
}
