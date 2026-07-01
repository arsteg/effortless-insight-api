using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace EffortlessInsight.Api.Services.Auth;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IJwtService _jwtService;
    private readonly IDistributedCache _cache;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ITwoFactorService _twoFactorService;
    private readonly IOtpService _otpService;
    private readonly IGeoLocationService _geoLocationService;

    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 30;
    private const int VerificationTokenExpiryHours = 24;
    private const int PasswordResetTokenExpiryHours = 1;
    private const int PasswordHistoryCount = 5;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IJwtService jwtService,
        IDistributedCache cache,
        IEmailService emailService,
        ILogger<AuthService> logger,
        IConfiguration configuration,
        ITwoFactorService twoFactorService,
        IOtpService otpService,
        IGeoLocationService geoLocationService)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _jwtService = jwtService;
        _cache = cache;
        _emailService = emailService;
        _logger = logger;
        _configuration = configuration;
        _twoFactorService = twoFactorService;
        _otpService = otpService;
        _geoLocationService = geoLocationService;
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, string ipAddress, string? userAgent)
    {
        // Check if email already exists
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            throw new InvalidOperationException("EMAIL_EXISTS");
        }

        // Check if mobile already exists (if provided)
        if (!string.IsNullOrEmpty(request.Mobile))
        {
            var mobileExists = await _dbContext.Users
                .AnyAsync(u => u.MobileNormalized == NormalizeMobile(request.Mobile) && u.DeletedAt == null);
            if (mobileExists)
            {
                throw new InvalidOperationException("MOBILE_EXISTS");
            }
        }

        // Create user
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            Name = request.Name,
            Mobile = request.Mobile,
            MobileNormalized = NormalizeMobile(request.Mobile),
            Role = "owner", // First user becomes owner
            TermsAccepted = request.AcceptTerms,
            TermsAcceptedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("User registration failed: {Errors}", errors);
            throw new InvalidOperationException($"REGISTRATION_FAILED: {errors}");
        }

        // Generate email verification token and store in cache
        var verificationToken = GenerateSecureToken();
        var cacheKey = $"email_verify:{verificationToken}";
        var verificationData = new EmailVerificationData
        {
            UserId = user.Id,
            Email = user.Email!,
            CreatedAt = DateTime.UtcNow
        };

        await _cache.SetStringAsync(
            cacheKey,
            System.Text.Json.JsonSerializer.Serialize(verificationData),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(VerificationTokenExpiryHours)
            });

        // Send verification email
        try
        {
            var verificationLink = $"{_configuration["App:BaseUrl"]}/verify-email?token={verificationToken}";
            await _emailService.SendTemplateAsync(
                user.Email!,
                "auth_verify_email",
                new Dictionary<string, object>
                {
                    { "user_name", user.Name },
                    { "verification_link", verificationLink },
                    { "expiry_hours", VerificationTokenExpiryHours }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to {Email}", user.Email);
            // Don't fail registration if email sending fails
        }

        // Log audit
        await LogLoginAuditAsync(user.Id, user.Email, true, null, ipAddress, userAgent, "register");

        _logger.LogInformation("User registered successfully: {Email}", user.Email);

        return new RegisterResponse(
            UserId: user.Id,
            Email: user.Email!,
            Name: user.Name,
            EmailVerified: false,
            Message: "Registration successful. Please verify your email."
        );
    }

    public async Task<object> LoginAsync(LoginRequest request, string ipAddress, string? userAgent)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user == null)
        {
            await LogLoginAuditAsync(null, request.Email, false, "invalid_credentials", ipAddress, userAgent, "password");
            throw new UnauthorizedAccessException("INVALID_CREDENTIALS");
        }

        // Check if account is locked
        if (user.IsLocked && user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            await LogLoginAuditAsync(user.Id, request.Email, false, "account_locked", ipAddress, userAgent, "password");
            var remainingMinutes = (int)(user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes;
            throw new UnauthorizedAccessException($"ACCOUNT_LOCKED:{remainingMinutes}");
        }

        // Check if account is active
        if (!user.IsActive)
        {
            await LogLoginAuditAsync(user.Id, request.Email, false, "account_disabled", ipAddress, userAgent, "password");
            throw new UnauthorizedAccessException("ACCOUNT_DISABLED");
        }

        // Verify password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            await HandleFailedLoginAsync(user, ipAddress, userAgent);
            throw new UnauthorizedAccessException("INVALID_CREDENTIALS");
        }

        // Check if email is verified
        if (!user.EmailConfirmed)
        {
            await LogLoginAuditAsync(user.Id, request.Email, false, "email_not_verified", ipAddress, userAgent, "password");
            throw new UnauthorizedAccessException("EMAIL_NOT_VERIFIED");
        }

        // Check if 2FA is enabled
        if (user.Is2faEnabled)
        {
            // Generate partial token for 2FA flow
            var partialToken = GenerateSecureToken();
            var partialTokenData = new PartialTokenData
            {
                UserId = user.Id,
                Email = user.Email!,
                RememberMe = request.RememberMe,
                DeviceInfo = request.DeviceInfo,
                CreatedAt = DateTime.UtcNow
            };

            await _cache.SetStringAsync(
                $"2fa_partial:{partialToken}",
                System.Text.Json.JsonSerializer.Serialize(partialTokenData),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

            return new TwoFactorRequiredResponse(
                Requires2fa: true,
                PartialToken: partialToken,
                ExpiresIn: 300,
                Methods: new List<string> { "totp", "backup_code" }
            );
        }

        // Reset failed login attempts on successful login
        await ResetFailedLoginAttemptsAsync(user, ipAddress, userAgent);

        // Generate tokens and create session
        var loginResponse = await CreateLoginSessionAsync(user, request.RememberMe, request.DeviceInfo, ipAddress, userAgent);

        await LogLoginAuditAsync(user.Id, request.Email, true, null, ipAddress, userAgent, "password");

        return loginResponse;
    }

    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken, string ipAddress, string? userAgent)
    {
        // Parse the refresh token to get JTI
        var tokenParts = refreshToken.Split(':');
        if (tokenParts.Length != 2)
        {
            throw new UnauthorizedAccessException("INVALID_REFRESH_TOKEN");
        }

        var jti = tokenParts[0];

        // Find the session
        var session = await _dbContext.UserSessions
            .Include(s => s.User)
            .ThenInclude(u => u.Organization)
            .FirstOrDefaultAsync(s => s.RefreshTokenJti == jti && s.RevokedAt == null);

        if (session == null)
        {
            throw new UnauthorizedAccessException("INVALID_REFRESH_TOKEN");
        }

        // Check if token is expired
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("REFRESH_TOKEN_EXPIRED");
        }

        // Verify the token hash
        var expectedHash = ComputeSha256Hash(refreshToken);
        if (session.RefreshTokenHash != expectedHash)
        {
            throw new UnauthorizedAccessException("INVALID_REFRESH_TOKEN");
        }

        var user = session.User;

        // Check if user is still active
        if (!user.IsActive || user.DeletedAt != null)
        {
            throw new UnauthorizedAccessException("ACCOUNT_DISABLED");
        }

        // Revoke old session
        session.RevokedAt = DateTime.UtcNow;
        session.RevokedReason = "token_refresh";

        // Get user's organization from memberships first (multi-org support), then fallback to legacy field
        var membership = await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .Where(m => m.UserId == user.Id && m.Status == "active" && m.Organization.DeletedAt == null)
            .OrderByDescending(m => m.JoinedAt)
            .FirstOrDefaultAsync();

        var organization = membership?.Organization ?? user.Organization;
        var roleOverride = membership?.Role;

        // Generate new tokens
        var (newRefreshToken, newJti, expiresAt) = _jwtService.GenerateRefreshToken(session.ExpiresAt > DateTime.UtcNow.AddDays(7));
        var accessToken = _jwtService.GenerateAccessToken(user, organization, roleOverride);

        // Create new session
        var newSession = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = ComputeSha256Hash(newRefreshToken),
            RefreshTokenJti = newJti,
            DeviceId = session.DeviceId,
            DeviceName = session.DeviceName,
            Platform = session.Platform,
            UserAgent = userAgent ?? session.UserAgent,
            IpAddress = ipAddress,
            ExpiresAt = expiresAt,
            LastActiveAt = DateTime.UtcNow
        };

        _dbContext.UserSessions.Add(newSession);
        await _dbContext.SaveChangesAsync();

        return new TokenResponse(
            AccessToken: accessToken,
            RefreshToken: newRefreshToken,
            TokenType: "Bearer",
            ExpiresIn: _jwtService.GetAccessTokenExpiryMinutes() * 60
        );
    }

    public async Task VerifyEmailAsync(string token)
    {
        var cacheKey = $"email_verify:{token}";
        var cachedData = await _cache.GetStringAsync(cacheKey);

        if (string.IsNullOrEmpty(cachedData))
        {
            throw new InvalidOperationException("INVALID_TOKEN");
        }

        var verificationData = System.Text.Json.JsonSerializer.Deserialize<EmailVerificationData>(cachedData);
        if (verificationData == null)
        {
            throw new InvalidOperationException("INVALID_TOKEN");
        }

        var user = await _userManager.FindByIdAsync(verificationData.UserId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        if (user.EmailConfirmed)
        {
            throw new InvalidOperationException("ALREADY_VERIFIED");
        }

        // Mark email as verified
        user.EmailConfirmed = true;
        user.IsEmailVerified = true;
        user.EmailVerifiedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Remove the token from cache
        await _cache.RemoveAsync(cacheKey);

        // Audit log
        await LogAuthEventAsync(user.Id, user.Email, AuthEventTypes.EmailVerified, true, null, null, null);

        _logger.LogInformation("Email verified for user: {Email}", user.Email);
    }

    public async Task ForgotPasswordAsync(string email, string ipAddress)
    {
        var user = await _userManager.FindByEmailAsync(email);

        // Always return success to prevent email enumeration
        if (user == null)
        {
            _logger.LogInformation("Password reset requested for non-existent email: {Email}", email);
            return;
        }

        // Generate password reset token
        var resetToken = GenerateSecureToken();
        var cacheKey = $"password_reset:{resetToken}";
        var resetData = new PasswordResetData
        {
            UserId = user.Id,
            Email = user.Email!,
            CreatedAt = DateTime.UtcNow
        };

        await _cache.SetStringAsync(
            cacheKey,
            System.Text.Json.JsonSerializer.Serialize(resetData),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(PasswordResetTokenExpiryHours)
            });

        // Send reset email
        try
        {
            var resetLink = $"{_configuration["App:BaseUrl"]}/reset-password?token={resetToken}";
            await _emailService.SendTemplateAsync(
                user.Email!,
                "auth_password_reset",
                new Dictionary<string, object>
                {
                    { "user_name", user.Name },
                    { "reset_link", resetLink },
                    { "expiry_hours", PasswordResetTokenExpiryHours },
                    { "ip_address", ipAddress }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
        }

        // Audit log
        await LogAuthEventAsync(user.Id, user.Email, AuthEventTypes.PasswordResetRequest, true, null, ipAddress, null);

        _logger.LogInformation("Password reset requested for: {Email}", user.Email);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (request.Password != request.ConfirmPassword)
        {
            throw new InvalidOperationException("PASSWORD_MISMATCH");
        }

        var cacheKey = $"password_reset:{request.Token}";
        var cachedData = await _cache.GetStringAsync(cacheKey);

        if (string.IsNullOrEmpty(cachedData))
        {
            throw new InvalidOperationException("INVALID_TOKEN");
        }

        var resetData = JsonSerializer.Deserialize<PasswordResetData>(cachedData);
        if (resetData == null)
        {
            throw new InvalidOperationException("INVALID_TOKEN");
        }

        var user = await _userManager.FindByIdAsync(resetData.UserId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        // Check password history
        if (await IsPasswordRecentlyUsedAsync(user.Id, request.Password))
        {
            throw new InvalidOperationException("PASSWORD_RECENTLY_USED");
        }

        // Reset password
        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, resetToken, request.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"PASSWORD_RESET_FAILED: {errors}");
        }

        // Add to password history
        await AddPasswordHistoryAsync(user.Id, user.PasswordHash!);

        // Update password changed timestamp
        user.PasswordChangedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Remove the token from cache
        await _cache.RemoveAsync(cacheKey);

        // Revoke all sessions
        await RevokeAllUserSessionsAsync(user.Id, "password_change");

        // Audit log
        await LogAuthEventAsync(user.Id, user.Email, AuthEventTypes.PasswordResetComplete, true, null, null, null);

        _logger.LogInformation("Password reset successful for: {Email}", user.Email);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            throw new InvalidOperationException("PASSWORD_MISMATCH");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        // Check password history
        if (await IsPasswordRecentlyUsedAsync(userId, request.NewPassword))
        {
            throw new InvalidOperationException("PASSWORD_RECENTLY_USED");
        }

        // Store old password hash before changing
        var oldPasswordHash = user.PasswordHash!;

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            if (errors.Contains("Incorrect password"))
            {
                throw new UnauthorizedAccessException("INVALID_CURRENT_PASSWORD");
            }
            throw new InvalidOperationException($"PASSWORD_CHANGE_FAILED: {errors}");
        }

        // Add old password to history
        await AddPasswordHistoryAsync(userId, oldPasswordHash);

        // Update password changed timestamp
        user.PasswordChangedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Audit log
        await LogAuthEventAsync(user.Id, user.Email, AuthEventTypes.PasswordChange, true, null, null, null);

        _logger.LogInformation("Password changed for user: {UserId}", userId);
    }

    public async Task LogoutAsync(Guid userId, string? refreshTokenJti, bool allDevices)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (allDevices)
        {
            await RevokeAllUserSessionsAsync(userId, "logout");

            // Audit log
            await LogAuthEventAsync(userId, user?.Email, AuthEventTypes.AllSessionsRevoked, true, null, null, null);
        }
        else if (!string.IsNullOrEmpty(refreshTokenJti))
        {
            var session = await _dbContext.UserSessions
                .FirstOrDefaultAsync(s => s.RefreshTokenJti == refreshTokenJti && s.UserId == userId);

            if (session != null)
            {
                session.RevokedAt = DateTime.UtcNow;
                session.RevokedReason = "logout";
                await _dbContext.SaveChangesAsync();
            }

            // Audit log
            await LogAuthEventAsync(userId, user?.Email, AuthEventTypes.Logout, true, null, null, null);
        }

        _logger.LogInformation("User logged out: {UserId}, AllDevices: {AllDevices}", userId, allDevices);
    }

    #region OTP Login

    public async Task<OtpResponse> RequestOtpLoginAsync(string mobile, string ipAddress)
    {
        // For login purpose, verify mobile exists in database first
        var normalizedMobile = NormalizeMobile(mobile);
        var userExists = await _dbContext.Users
            .AnyAsync(u => u.MobileNormalized == normalizedMobile && u.DeletedAt == null);

        if (!userExists)
        {
            throw new InvalidOperationException("MOBILE_NOT_FOUND");
        }

        return await _otpService.RequestOtpAsync(mobile, "login", ipAddress);
    }

    public async Task<object> VerifyOtpLoginAsync(OtpVerifyRequest request, string ipAddress, string? userAgent)
    {
        var isValid = await _otpService.VerifyOtpAsync(request.Mobile, request.Otp, "login");
        if (!isValid)
        {
            throw new UnauthorizedAccessException("INVALID_OTP");
        }

        // Find user by mobile
        var normalizedMobile = NormalizeMobile(request.Mobile);
        var user = await _dbContext.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.MobileNormalized == normalizedMobile && u.DeletedAt == null);

        if (user == null)
        {
            throw new UnauthorizedAccessException("USER_NOT_FOUND");
        }

        // Check if account is active
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("ACCOUNT_DISABLED");
        }

        // Check if mobile is verified, if not verify it now
        if (!user.IsMobileVerified)
        {
            user.IsMobileVerified = true;
            user.MobileVerifiedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }

        // Check if 2FA is enabled
        if (user.Is2faEnabled)
        {
            var partialToken = GenerateSecureToken();
            var partialTokenData = new PartialTokenData
            {
                UserId = user.Id,
                Email = user.Email!,
                RememberMe = false,
                DeviceInfo = null,
                CreatedAt = DateTime.UtcNow
            };

            await _cache.SetStringAsync(
                $"2fa_partial:{partialToken}",
                JsonSerializer.Serialize(partialTokenData),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

            return new TwoFactorRequiredResponse(
                Requires2fa: true,
                PartialToken: partialToken,
                ExpiresIn: 300,
                Methods: new List<string> { "totp", "backup_code" }
            );
        }

        // Generate tokens and create session
        var loginResponse = await CreateLoginSessionAsync(user, false, null, ipAddress, userAgent);

        await LogLoginAuditAsync(user.Id, user.Email, true, null, ipAddress, userAgent, "otp");

        return loginResponse;
    }

    #endregion

    #region 2FA Setup

    public async Task<TwoFactorSetupResponse> Setup2faAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        if (user.Is2faEnabled)
        {
            throw new InvalidOperationException("2FA_ALREADY_ENABLED");
        }

        // Generate 2FA setup
        var (secret, qrCodeDataUrl, otpauthUrl, backupCodes) = _twoFactorService.GenerateSetup(user.Email!);

        // Store pending setup in cache (not in DB until verified)
        var setupData = new TwoFactorSetupData
        {
            UserId = userId,
            Secret = secret,
            BackupCodes = backupCodes,
            CreatedAt = DateTime.UtcNow
        };

        await _cache.SetStringAsync(
            $"2fa_setup:{userId}",
            JsonSerializer.Serialize(setupData),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            });

        _logger.LogInformation("2FA setup initiated for user: {UserId}", userId);

        return new TwoFactorSetupResponse(
            Secret: secret,
            QrCodeDataUrl: qrCodeDataUrl,
            OtpauthUrl: otpauthUrl,
            BackupCodes: backupCodes
        );
    }

    public async Task<TwoFactorVerifySetupResponse> VerifySetup2faAsync(Guid userId, string code)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        if (user.Is2faEnabled)
        {
            throw new InvalidOperationException("2FA_ALREADY_ENABLED");
        }

        // Get pending setup from cache
        var setupDataJson = await _cache.GetStringAsync($"2fa_setup:{userId}");
        if (string.IsNullOrEmpty(setupDataJson))
        {
            throw new InvalidOperationException("2FA_SETUP_NOT_FOUND");
        }

        var setupData = JsonSerializer.Deserialize<TwoFactorSetupData>(setupDataJson);
        if (setupData == null)
        {
            throw new InvalidOperationException("2FA_SETUP_NOT_FOUND");
        }

        // Verify the code
        if (!_twoFactorService.VerifyCode(setupData.Secret, code))
        {
            throw new UnauthorizedAccessException("INVALID_2FA_CODE");
        }

        // Enable 2FA for user
        user.Is2faEnabled = true;
        user.TotpSecretEncrypted = _twoFactorService.EncryptSecret(setupData.Secret);
        user.BackupCodesHash = _twoFactorService.HashBackupCodes(setupData.BackupCodes);
        user.UpdatedAt = DateTime.UtcNow;

        await _userManager.UpdateAsync(user);

        // Remove setup from cache
        await _cache.RemoveAsync($"2fa_setup:{userId}");

        // Audit log
        await LogAuthEventAsync(user.Id, user.Email, AuthEventTypes.TwoFactorEnabled, true, null, null, null);

        _logger.LogInformation("2FA enabled for user: {UserId}", userId);

        return new TwoFactorVerifySetupResponse(
            Message: "Two-factor authentication enabled successfully",
            BackupCodesRemaining: setupData.BackupCodes.Count
        );
    }

    public async Task Disable2faAsync(Guid userId, string password)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        if (!user.Is2faEnabled)
        {
            throw new InvalidOperationException("2FA_NOT_ENABLED");
        }

        // Verify password
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, password);
        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("INVALID_PASSWORD");
        }

        // Disable 2FA
        user.Is2faEnabled = false;
        user.TotpSecretEncrypted = null;
        user.BackupCodesHash = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userManager.UpdateAsync(user);

        // Audit log
        await LogAuthEventAsync(user.Id, user.Email, AuthEventTypes.TwoFactorDisabled, true, null, null, null);

        _logger.LogInformation("2FA disabled for user: {UserId}", userId);
    }

    #endregion

    #region 2FA Login

    public async Task<TwoFactorLoginResponse> Complete2faLoginAsync(TwoFactorLoginRequest request, string ipAddress, string? userAgent)
    {
        // Get partial token data
        var partialTokenDataJson = await _cache.GetStringAsync($"2fa_partial:{request.PartialToken}");
        if (string.IsNullOrEmpty(partialTokenDataJson))
        {
            throw new UnauthorizedAccessException("INVALID_PARTIAL_TOKEN");
        }

        var partialTokenData = JsonSerializer.Deserialize<PartialTokenData>(partialTokenDataJson);
        if (partialTokenData == null)
        {
            throw new UnauthorizedAccessException("INVALID_PARTIAL_TOKEN");
        }

        var user = await _userManager.FindByIdAsync(partialTokenData.UserId.ToString());
        if (user == null)
        {
            throw new UnauthorizedAccessException("USER_NOT_FOUND");
        }

        if (!user.Is2faEnabled || user.TotpSecretEncrypted == null)
        {
            throw new UnauthorizedAccessException("2FA_NOT_ENABLED");
        }

        var backupCodeUsed = false;
        var isValid = false;

        // Check if it's a backup code (8 alphanumeric characters)
        if (request.Code.Length == 8 && request.Code.All(c => char.IsLetterOrDigit(c)))
        {
            // Try to verify as backup code
            if (user.BackupCodesHash != null && _twoFactorService.VerifyBackupCode(user.BackupCodesHash, request.Code, out var usedIndex))
            {
                // Mark backup code as used
                user.BackupCodesHash[usedIndex] = string.Empty;
                await _userManager.UpdateAsync(user);
                isValid = true;
                backupCodeUsed = true;
            }
        }

        // If not a backup code or backup verification failed, try TOTP
        if (!isValid)
        {
            var secret = _twoFactorService.DecryptSecret(user.TotpSecretEncrypted);
            isValid = _twoFactorService.VerifyCode(secret, request.Code);
        }

        if (!isValid)
        {
            await LogLoginAuditAsync(user.Id, user.Email, false, "invalid_2fa_code", ipAddress, userAgent, "2fa");
            throw new UnauthorizedAccessException("INVALID_2FA_CODE");
        }

        // Remove partial token
        await _cache.RemoveAsync($"2fa_partial:{request.PartialToken}");

        // Reset failed login attempts
        await ResetFailedLoginAttemptsAsync(user, ipAddress, userAgent);

        // Get user's organization from memberships first (multi-org support), then fallback to legacy field
        var membership = await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .Where(m => m.UserId == user.Id && m.Status == "active" && m.Organization.DeletedAt == null)
            .OrderByDescending(m => m.JoinedAt)
            .FirstOrDefaultAsync();

        var organization = membership?.Organization
            ?? (user.OrganizationId.HasValue
                ? await _dbContext.Organizations.FindAsync(user.OrganizationId.Value)
                : null);

        var roleOverride = membership?.Role;

        var accessToken = _jwtService.GenerateAccessToken(user, organization, roleOverride);
        var (refreshToken, _, expiresAt) = _jwtService.GenerateRefreshToken(partialTokenData.RememberMe);

        var session = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = ComputeSha256Hash(refreshToken),
            RefreshTokenJti = refreshToken.Split(':')[0],
            DeviceId = partialTokenData.DeviceInfo?.DeviceId,
            DeviceName = partialTokenData.DeviceInfo?.DeviceName ?? ExtractDeviceName(userAgent),
            Platform = partialTokenData.DeviceInfo?.Platform ?? "web",
            UserAgent = userAgent,
            IpAddress = ipAddress,
            ExpiresAt = expiresAt,
            LastActiveAt = DateTime.UtcNow
        };

        _dbContext.UserSessions.Add(session);

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ipAddress;
        user.LastLoginUserAgent = userAgent;

        await _dbContext.SaveChangesAsync();

        await LogLoginAuditAsync(user.Id, user.Email, true, null, ipAddress, userAgent, backupCodeUsed ? "2fa_backup" : "2fa");

        _logger.LogInformation("2FA login successful for user: {UserId} (backup code: {BackupCodeUsed})", user.Id, backupCodeUsed);

        return new TwoFactorLoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            TokenType: "Bearer",
            ExpiresIn: _jwtService.GetAccessTokenExpiryMinutes() * 60,
            BackupCodeUsed: backupCodeUsed
        );
    }

    #endregion

    #region Password History

    public async Task<bool> IsPasswordRecentlyUsedAsync(Guid userId, string password)
    {
        var recentPasswords = await _dbContext.PasswordHistory
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(PasswordHistoryCount)
            .Select(p => p.PasswordHash)
            .ToListAsync();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return false;
        }

        // Check current password
        var currentPasswordResult = _userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash!, password);
        if (currentPasswordResult == PasswordVerificationResult.Success || currentPasswordResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            return true;
        }

        // Check historical passwords
        foreach (var hash in recentPasswords)
        {
            var result = _userManager.PasswordHasher.VerifyHashedPassword(user, hash, password);
            if (result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                return true;
            }
        }

        return false;
    }

    public async Task AddPasswordHistoryAsync(Guid userId, string passwordHash)
    {
        var passwordHistory = new PasswordHistory
        {
            UserId = userId,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PasswordHistory.Add(passwordHistory);

        // Remove old entries beyond the limit
        var oldEntries = await _dbContext.PasswordHistory
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(PasswordHistoryCount)
            .ToListAsync();

        if (oldEntries.Any())
        {
            _dbContext.PasswordHistory.RemoveRange(oldEntries);
        }

        await _dbContext.SaveChangesAsync();
    }

    #endregion

    #region OAuth

    public async Task<OAuthProvidersResponse> GetEnabledOAuthProvidersAsync()
    {
        var oauthSection = _configuration.GetSection("OAuth");
        var providers = new List<OAuthProviderInfo>();

        var googleEnabled = oauthSection.GetSection("Google").GetValue<bool>("Enabled");
        var googleClientId = oauthSection.GetSection("Google").GetValue<string>("ClientId");
        if (googleEnabled && !string.IsNullOrEmpty(googleClientId))
        {
            providers.Add(new OAuthProviderInfo("google", "Google", true));
        }

        var microsoftEnabled = oauthSection.GetSection("Microsoft").GetValue<bool>("Enabled");
        var microsoftClientId = oauthSection.GetSection("Microsoft").GetValue<string>("ClientId");
        if (microsoftEnabled && !string.IsNullOrEmpty(microsoftClientId))
        {
            providers.Add(new OAuthProviderInfo("microsoft", "Microsoft", true));
        }

        return await Task.FromResult(new OAuthProvidersResponse(providers));
    }

    public async Task<OAuthLoginUrlResponse> GetOAuthLoginUrlAsync(string provider, string? state, bool forceReauth = false, string? redirectUri = null, string? platform = null)
    {
        var oauthSection = _configuration.GetSection("OAuth");
        var callbackBaseUrl = oauthSection.GetValue<string>("CallbackBaseUrl") ?? "http://localhost:3000";

        // For mobile apps, use a special API callback endpoint that returns JSON
        // The mobile app will handle the callback and exchange the code for tokens
        var isMobile = platform == "ios" || platform == "android" || !string.IsNullOrEmpty(redirectUri);
        var effectiveCallbackBase = isMobile
            ? oauthSection.GetValue<string>("MobileCallbackBaseUrl") ?? callbackBaseUrl
            : callbackBaseUrl;

        // Generate state token for CSRF protection if not provided
        var stateToken = state ?? GenerateSecureToken();

        // Store state in cache for verification on callback, including mobile redirect info
        var stateData = new OAuthStateData
        {
            CreatedAt = DateTime.UtcNow,
            Provider = provider,
            RedirectUri = redirectUri,
            Platform = platform
        };

        await _cache.SetStringAsync(
            $"oauth_state:{stateToken}",
            JsonSerializer.Serialize(stateData),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

        string loginUrl;
        switch (provider.ToLowerInvariant())
        {
            case "google":
                var googleClientId = oauthSection.GetSection("Google").GetValue<string>("ClientId");
                if (string.IsNullOrEmpty(googleClientId))
                {
                    throw new InvalidOperationException("GOOGLE_OAUTH_NOT_CONFIGURED");
                }
                var googleRedirectUri = Uri.EscapeDataString($"{effectiveCallbackBase}/auth/callback/google");
                // Use prompt=login for forced re-authentication, otherwise prompt=consent for refresh token
                var googlePrompt = forceReauth ? "login" : "consent";
                loginUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={googleClientId}&redirect_uri={googleRedirectUri}&response_type=code&scope=email%20profile&state={stateToken}&access_type=offline&prompt={googlePrompt}";
                break;

            case "microsoft":
                var microsoftClientId = oauthSection.GetSection("Microsoft").GetValue<string>("ClientId");
                if (string.IsNullOrEmpty(microsoftClientId))
                {
                    throw new InvalidOperationException("MICROSOFT_OAUTH_NOT_CONFIGURED");
                }
                var microsoftRedirectUri = Uri.EscapeDataString($"{effectiveCallbackBase}/auth/callback/microsoft");
                // Include User.Read scope for profile photo access
                // Use prompt=login for forced re-authentication
                var microsoftPrompt = forceReauth ? "&prompt=login" : "";
                loginUrl = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={microsoftClientId}&redirect_uri={microsoftRedirectUri}&response_type=code&scope=openid%20email%20profile%20User.Read&state={stateToken}&response_mode=query{microsoftPrompt}";
                break;

            default:
                throw new ArgumentException($"Unsupported OAuth provider: {provider}");
        }

        return new OAuthLoginUrlResponse(loginUrl, stateToken);
    }

    public async Task<object> HandleOAuthCallbackAsync(string provider, string code, string state, string ipAddress, string? userAgent)
    {
        // State is required for CSRF protection
        if (string.IsNullOrEmpty(state))
        {
            throw new InvalidOperationException("MISSING_OAUTH_STATE");
        }

        // Verify state token exists and hasn't expired
        var stateDataJson = await _cache.GetStringAsync($"oauth_state:{state}");
        if (string.IsNullOrEmpty(stateDataJson))
        {
            throw new InvalidOperationException("INVALID_OAUTH_STATE");
        }

        // Remove state token immediately to prevent replay attacks
        await _cache.RemoveAsync($"oauth_state:{state}");

        // Verify the provider matches what was requested
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var stateData = JsonSerializer.Deserialize<OAuthStateData>(stateDataJson, jsonOptions);

        _logger.LogDebug("OAuth state data: {StateJson}, Parsed provider: {ParsedProvider}, Expected provider: {ExpectedProvider}",
            stateDataJson, stateData?.Provider, provider);

        if (stateData == null || string.IsNullOrEmpty(stateData.Provider))
        {
            _logger.LogWarning("OAuth state deserialization failed. Raw JSON: {StateJson}", stateDataJson);
            throw new InvalidOperationException("OAUTH_PROVIDER_MISMATCH");
        }

        if (!string.Equals(stateData.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("OAuth provider mismatch. State provider: {StateProvider}, Callback provider: {CallbackProvider}",
                stateData.Provider, provider);
            throw new InvalidOperationException("OAUTH_PROVIDER_MISMATCH");
        }

        var oauthSection = _configuration.GetSection("OAuth");
        var callbackBaseUrl = oauthSection.GetValue<string>("CallbackBaseUrl") ?? "http://localhost:3000";

        OAuthUserInfo? userInfo;
        switch (provider.ToLowerInvariant())
        {
            case "google":
                userInfo = await ExchangeGoogleCodeAsync(code, callbackBaseUrl);
                break;
            case "microsoft":
                userInfo = await ExchangeMicrosoftCodeAsync(code, callbackBaseUrl);
                break;
            default:
                throw new ArgumentException($"Unsupported OAuth provider: {provider}");
        }

        if (userInfo == null || string.IsNullOrEmpty(userInfo.Email))
        {
            throw new InvalidOperationException("FAILED_TO_GET_USER_INFO");
        }

        var providerLower = provider.ToLowerInvariant();
        ApplicationUser? user = null;
        UserOAuthProvider? oauthLink = null;

        // First, check if this OAuth provider ID is already linked in the new table
        oauthLink = await _dbContext.UserOAuthProviders
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Provider == providerLower && p.ProviderId == userInfo.Id);

        if (oauthLink != null)
        {
            user = oauthLink.User;
            // Update last used timestamp
            oauthLink.LastUsedAt = DateTime.UtcNow;
            // Update avatar if changed
            if (!string.IsNullOrEmpty(userInfo.Picture) && oauthLink.AvatarUrl != userInfo.Picture)
            {
                oauthLink.AvatarUrl = userInfo.Picture;
            }
        }
        else
        {
            // Check legacy fields for existing users
            user = await _dbContext.Users
                .FirstOrDefaultAsync(u =>
                    u.OAuthProvider == providerLower && u.OAuthProviderId == userInfo.Id && u.DeletedAt == null);

            // If not found by OAuth ID, try by email
            if (user == null)
            {
                user = await _userManager.FindByEmailAsync(userInfo.Email);
            }
        }

        if (user == null)
        {
            // Create new user for OAuth login
            user = new ApplicationUser
            {
                UserName = userInfo.Email,
                Email = userInfo.Email,
                Name = userInfo.Name ?? userInfo.Email.Split('@')[0],
                EmailConfirmed = true, // OAuth emails are verified
                IsEmailVerified = true,
                EmailVerifiedAt = DateTime.UtcNow,
                Role = "owner",
                AvatarUrl = userInfo.Picture,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("OAuth user registration failed: {Errors}", errors);
                throw new InvalidOperationException($"REGISTRATION_FAILED: {errors}");
            }

            // Create OAuth link in new table
            oauthLink = new UserOAuthProvider
            {
                UserId = user.Id,
                Provider = providerLower,
                ProviderId = userInfo.Id!,
                Email = userInfo.Email,
                DisplayName = userInfo.Name,
                AvatarUrl = userInfo.Picture,
                LinkedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };
            _dbContext.UserOAuthProviders.Add(oauthLink);

            _logger.LogInformation("Created new user via OAuth: {Email} ({Provider})", userInfo.Email, provider);
        }
        else if (oauthLink == null)
        {
            // User exists but OAuth provider not linked - check if we should link it
            var existingLink = await _dbContext.UserOAuthProviders
                .AnyAsync(p => p.UserId == user.Id && p.Provider == providerLower);

            if (!existingLink)
            {
                // Auto-link this OAuth provider to the existing user
                oauthLink = new UserOAuthProvider
                {
                    UserId = user.Id,
                    Provider = providerLower,
                    ProviderId = userInfo.Id!,
                    Email = userInfo.Email,
                    DisplayName = userInfo.Name,
                    AvatarUrl = userInfo.Picture,
                    LinkedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow
                };
                _dbContext.UserOAuthProviders.Add(oauthLink);

                _logger.LogInformation("Auto-linked OAuth provider to existing user: {Email} ({Provider})", userInfo.Email, provider);
            }

            // Update avatar if not set
            if (string.IsNullOrEmpty(user.AvatarUrl) && !string.IsNullOrEmpty(userInfo.Picture))
            {
                user.AvatarUrl = userInfo.Picture;
                await _userManager.UpdateAsync(user);
            }
        }

        await _dbContext.SaveChangesAsync();

        // Check if account is active
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("ACCOUNT_DISABLED");
        }

        // Check for 2FA
        if (user.Is2faEnabled)
        {
            var partialToken = GenerateSecureToken();
            var partialTokenData = new PartialTokenData
            {
                UserId = user.Id,
                Email = user.Email!,
                RememberMe = false,
                DeviceInfo = null,
                CreatedAt = DateTime.UtcNow
            };

            await _cache.SetStringAsync(
                $"2fa_partial:{partialToken}",
                JsonSerializer.Serialize(partialTokenData),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });

            return new TwoFactorRequiredResponse(
                Requires2fa: true,
                PartialToken: partialToken,
                ExpiresIn: 300,
                Methods: new List<string> { "totp", "backup_code" }
            );
        }

        // Create login session
        var loginResponse = await CreateLoginSessionAsync(user, false, null, ipAddress, userAgent);

        await LogLoginAuditAsync(user.Id, user.Email, true, null, ipAddress, userAgent, $"oauth_{provider}");

        // If this is a mobile OAuth request, include the redirect URI in the response
        if (!string.IsNullOrEmpty(stateData.RedirectUri))
        {
            return new LoginResponse(
                loginResponse.AccessToken,
                loginResponse.RefreshToken,
                loginResponse.TokenType,
                loginResponse.ExpiresIn,
                loginResponse.User,
                stateData.RedirectUri
            );
        }

        return loginResponse;
    }

    private async Task<OAuthUserInfo?> ExchangeGoogleCodeAsync(string code, string callbackBaseUrl)
    {
        var oauthSection = _configuration.GetSection("OAuth:Google");
        var clientId = oauthSection.GetValue<string>("ClientId");
        var clientSecret = oauthSection.GetValue<string>("ClientSecret");
        var redirectUri = $"{callbackBaseUrl}/auth/callback/google";

        using var httpClient = new HttpClient();

        // Exchange code for tokens
        var tokenRequest = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId!,
            ["client_secret"] = clientSecret!,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        var tokenResponse = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(tokenRequest));

        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to exchange Google code: {Status}", tokenResponse.StatusCode);
            return null;
        }

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);
        var accessToken = tokenData.GetProperty("access_token").GetString();

        // Get user info
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var userInfoResponse = await httpClient.GetAsync("https://www.googleapis.com/oauth2/v2/userinfo");
        if (!userInfoResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get Google user info: {Status}", userInfoResponse.StatusCode);
            return null;
        }

        var userInfoJson = await userInfoResponse.Content.ReadAsStringAsync();
        var userInfo = JsonSerializer.Deserialize<JsonElement>(userInfoJson);

        return new OAuthUserInfo
        {
            Id = userInfo.TryGetProperty("id", out var id) ? id.GetString() : null,
            Email = userInfo.TryGetProperty("email", out var email) ? email.GetString() : null,
            Name = userInfo.TryGetProperty("name", out var name) ? name.GetString() : null,
            Picture = userInfo.TryGetProperty("picture", out var picture) ? picture.GetString() : null
        };
    }

    private async Task<OAuthUserInfo?> ExchangeMicrosoftCodeAsync(string code, string callbackBaseUrl)
    {
        var oauthSection = _configuration.GetSection("OAuth:Microsoft");
        var clientId = oauthSection.GetValue<string>("ClientId");
        var clientSecret = oauthSection.GetValue<string>("ClientSecret");
        var redirectUri = $"{callbackBaseUrl}/auth/callback/microsoft";

        using var httpClient = new HttpClient();

        // Exchange code for tokens
        var tokenRequest = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId!,
            ["client_secret"] = clientSecret!,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = "openid email profile User.Read"
        };

        var tokenResponse = await httpClient.PostAsync(
            "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            new FormUrlEncodedContent(tokenRequest));

        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to exchange Microsoft code: {Status}", tokenResponse.StatusCode);
            return null;
        }

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);
        var accessToken = tokenData.GetProperty("access_token").GetString();

        // Get user info from Microsoft Graph
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var userInfoResponse = await httpClient.GetAsync("https://graph.microsoft.com/v1.0/me");
        if (!userInfoResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get Microsoft user info: {Status}", userInfoResponse.StatusCode);
            return null;
        }

        var userInfoJson = await userInfoResponse.Content.ReadAsStringAsync();
        var userInfo = JsonSerializer.Deserialize<JsonElement>(userInfoJson);

        // Get email - Microsoft may return it in different fields
        string? email = null;
        if (userInfo.TryGetProperty("mail", out var mailProp))
        {
            email = mailProp.GetString();
        }
        if (string.IsNullOrEmpty(email) && userInfo.TryGetProperty("userPrincipalName", out var upnProp))
        {
            email = upnProp.GetString();
        }

        // Try to fetch profile photo from Microsoft Graph
        string? pictureUrl = null;
        try
        {
            var photoResponse = await httpClient.GetAsync("https://graph.microsoft.com/v1.0/me/photo/$value");
            if (photoResponse.IsSuccessStatusCode)
            {
                var photoBytes = await photoResponse.Content.ReadAsByteArrayAsync();
                var contentType = photoResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                pictureUrl = $"data:{contentType};base64,{Convert.ToBase64String(photoBytes)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch Microsoft profile photo (non-critical)");
        }

        return new OAuthUserInfo
        {
            Id = userInfo.TryGetProperty("id", out var id) ? id.GetString() : null,
            Email = email,
            Name = userInfo.TryGetProperty("displayName", out var name) ? name.GetString() : null,
            Picture = pictureUrl
        };
    }

    public async Task DisconnectOAuthAsync(Guid userId, string provider, string password)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        // Verify password before allowing disconnect
        var isPasswordValid = await _userManager.CheckPasswordAsync(user, password);
        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("INVALID_PASSWORD");
        }

        // Check if the provider is linked in the new table
        var oauthProvider = await _dbContext.UserOAuthProviders
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Provider == provider.ToLowerInvariant());

        // Also check legacy fields
        var isLegacyProvider = string.Equals(user.OAuthProvider, provider, StringComparison.OrdinalIgnoreCase);

        if (oauthProvider == null && !isLegacyProvider)
        {
            throw new InvalidOperationException("PROVIDER_NOT_LINKED");
        }

        // Count remaining auth methods after disconnect
        var hasPassword = await _userManager.HasPasswordAsync(user);
        var otherProvidersCount = await _dbContext.UserOAuthProviders
            .CountAsync(p => p.UserId == userId && p.Provider != provider.ToLowerInvariant());

        // Check legacy provider isn't the only other method
        var hasOtherLegacyProvider = !string.IsNullOrEmpty(user.OAuthProvider) &&
            !string.Equals(user.OAuthProvider, provider, StringComparison.OrdinalIgnoreCase);

        var willHaveAuthMethod = hasPassword || otherProvidersCount > 0 || hasOtherLegacyProvider;

        if (!willHaveAuthMethod)
        {
            throw new InvalidOperationException("LAST_AUTH_METHOD");
        }

        // Remove from new table
        if (oauthProvider != null)
        {
            _dbContext.UserOAuthProviders.Remove(oauthProvider);
        }

        // Remove legacy fields if they match
        if (isLegacyProvider)
        {
            user.OAuthProvider = null;
            user.OAuthProviderId = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
        }

        await _dbContext.SaveChangesAsync();

        // Audit log
        await LogAuthEventAsync(
            user.Id,
            user.Email,
            AuthEventTypes.OAuthDisconnected,
            true,
            $"Disconnected OAuth provider: {provider}",
            null,
            null);

        _logger.LogInformation("OAuth disconnected for user {UserId}: {Provider}", userId, provider);
    }

    public async Task<UserOAuthInfoResponse?> GetUserOAuthInfoAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return null;
        }

        var hasPassword = await _userManager.HasPasswordAsync(user);

        // Check new table first, then legacy fields
        var primaryProvider = await _dbContext.UserOAuthProviders
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.LastUsedAt ?? p.LinkedAt)
            .FirstOrDefaultAsync();

        if (primaryProvider != null)
        {
            return new UserOAuthInfoResponse(
                Provider: primaryProvider.Provider,
                ProviderId: primaryProvider.ProviderId,
                HasPassword: hasPassword
            );
        }

        // Fall back to legacy fields
        return new UserOAuthInfoResponse(
            Provider: user.OAuthProvider,
            ProviderId: user.OAuthProviderId,
            HasPassword: hasPassword
        );
    }

    public async Task<UserOAuthProvidersResponse> GetUserOAuthProvidersAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        var hasPassword = await _userManager.HasPasswordAsync(user);

        // Get all linked providers from new table
        var linkedProviders = await _dbContext.UserOAuthProviders
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.LastUsedAt ?? p.LinkedAt)
            .Select(p => new LinkedOAuthProviderDto(
                p.Id,
                p.Provider,
                p.Email,
                p.DisplayName,
                p.AvatarUrl,
                p.LinkedAt,
                p.LastUsedAt
            ))
            .ToListAsync();

        // Also include legacy provider if not already in the list
        if (!string.IsNullOrEmpty(user.OAuthProvider) &&
            !linkedProviders.Any(p => string.Equals(p.Provider, user.OAuthProvider, StringComparison.OrdinalIgnoreCase)))
        {
            linkedProviders.Add(new LinkedOAuthProviderDto(
                Id: Guid.Empty, // Legacy provider has no ID
                Provider: user.OAuthProvider,
                Email: user.Email,
                DisplayName: user.Name,
                AvatarUrl: user.AvatarUrl,
                LinkedAt: user.CreatedAt,
                LastUsedAt: user.LastLoginAt
            ));
        }

        // Determine available providers (ones not yet linked)
        var linkedProviderNames = linkedProviders.Select(p => p.Provider.ToLowerInvariant()).ToHashSet();
        var allProviders = new[] { "google", "microsoft" };
        var availableProviders = allProviders.Where(p => !linkedProviderNames.Contains(p)).ToList();

        return new UserOAuthProvidersResponse(
            LinkedProviders: linkedProviders,
            AvailableProviders: availableProviders,
            HasPassword: hasPassword
        );
    }

    public async Task<LinkedOAuthProviderDto> LinkOAuthProviderAsync(Guid userId, string provider, string code, string state)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new InvalidOperationException("USER_NOT_FOUND");
        }

        // Verify state
        if (string.IsNullOrEmpty(state))
        {
            throw new InvalidOperationException("MISSING_OAUTH_STATE");
        }

        var stateDataJson = await _cache.GetStringAsync($"oauth_state:{state}");
        if (string.IsNullOrEmpty(stateDataJson))
        {
            throw new InvalidOperationException("INVALID_OAUTH_STATE");
        }

        await _cache.RemoveAsync($"oauth_state:{state}");

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var stateData = JsonSerializer.Deserialize<OAuthStateData>(stateDataJson, jsonOptions);
        if (stateData == null || string.IsNullOrEmpty(stateData.Provider) ||
            !string.Equals(stateData.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OAUTH_PROVIDER_MISMATCH");
        }

        // Check if provider is already linked
        var existingLink = await _dbContext.UserOAuthProviders
            .AnyAsync(p => p.UserId == userId && p.Provider == provider.ToLowerInvariant());

        if (existingLink || string.Equals(user.OAuthProvider, provider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PROVIDER_ALREADY_LINKED");
        }

        // Exchange code for user info
        var oauthSection = _configuration.GetSection("OAuth");
        var callbackBaseUrl = oauthSection.GetValue<string>("CallbackBaseUrl") ?? "http://localhost:3000";

        OAuthUserInfo? userInfo;
        switch (provider.ToLowerInvariant())
        {
            case "google":
                userInfo = await ExchangeGoogleCodeAsync(code, callbackBaseUrl);
                break;
            case "microsoft":
                userInfo = await ExchangeMicrosoftCodeAsync(code, callbackBaseUrl);
                break;
            default:
                throw new ArgumentException($"Unsupported OAuth provider: {provider}");
        }

        if (userInfo == null || string.IsNullOrEmpty(userInfo.Id))
        {
            throw new InvalidOperationException("FAILED_TO_GET_USER_INFO");
        }

        // Check if this provider ID is already linked to another user
        var existingUserWithProvider = await _dbContext.UserOAuthProviders
            .AnyAsync(p => p.Provider == provider.ToLowerInvariant() && p.ProviderId == userInfo.Id && p.UserId != userId);

        if (existingUserWithProvider)
        {
            throw new InvalidOperationException("PROVIDER_LINKED_TO_ANOTHER_USER");
        }

        // Create the link
        var oauthLink = new UserOAuthProvider
        {
            UserId = userId,
            Provider = provider.ToLowerInvariant(),
            ProviderId = userInfo.Id,
            Email = userInfo.Email,
            DisplayName = userInfo.Name,
            AvatarUrl = userInfo.Picture,
            LinkedAt = DateTime.UtcNow
        };

        _dbContext.UserOAuthProviders.Add(oauthLink);
        await _dbContext.SaveChangesAsync();

        // Audit log
        await LogAuthEventAsync(
            user.Id,
            user.Email,
            AuthEventTypes.OAuthLinked,
            true,
            $"Linked OAuth provider: {provider}",
            null,
            null);

        _logger.LogInformation("OAuth provider linked for user {UserId}: {Provider}", userId, provider);

        return new LinkedOAuthProviderDto(
            Id: oauthLink.Id,
            Provider: oauthLink.Provider,
            Email: oauthLink.Email,
            DisplayName: oauthLink.DisplayName,
            AvatarUrl: oauthLink.AvatarUrl,
            LinkedAt: oauthLink.LinkedAt,
            LastUsedAt: null
        );
    }

    #endregion

    #region Private Methods

    private async Task<LoginResponse> CreateLoginSessionAsync(
        ApplicationUser user,
        bool rememberMe,
        DeviceInfo? deviceInfo,
        string ipAddress,
        string? userAgent)
    {
        // Get user's organization from memberships first (multi-org support), then fallback to legacy field
        var membership = await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .Where(m => m.UserId == user.Id && m.Status == "active" && m.Organization.DeletedAt == null)
            .OrderByDescending(m => m.JoinedAt)
            .FirstOrDefaultAsync();

        var organization = membership?.Organization
            ?? (user.OrganizationId.HasValue
                ? await _dbContext.Organizations.FindAsync(user.OrganizationId.Value)
                : null);

        // Use role from membership if available
        var roleOverride = membership?.Role;

        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user, organization, roleOverride);
        var (refreshToken, jti, expiresAt) = _jwtService.GenerateRefreshToken(rememberMe);

        // Get geolocation for IP address (fire-and-forget friendly, won't block if it fails)
        var geoLocation = await _geoLocationService.GetLocationAsync(ipAddress);

        // Create session
        var session = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = ComputeSha256Hash(refreshToken),
            RefreshTokenJti = jti,
            DeviceId = deviceInfo?.DeviceId,
            DeviceName = deviceInfo?.DeviceName ?? ExtractDeviceName(userAgent),
            Platform = deviceInfo?.Platform ?? "web",
            UserAgent = userAgent,
            IpAddress = ipAddress,
            LocationCity = geoLocation?.City,
            LocationCountry = geoLocation?.Country,
            ExpiresAt = expiresAt,
            LastActiveAt = DateTime.UtcNow
        };

        _dbContext.UserSessions.Add(session);

        // Update last login info
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ipAddress;
        user.LastLoginUserAgent = userAgent;

        await _dbContext.SaveChangesAsync();

        // Build organization info for response
        UserOrganizationDto? currentOrg = null;
        var orgs = new List<UserOrganizationDto>();

        if (membership != null)
        {
            currentOrg = new UserOrganizationDto(membership.OrganizationId, membership.Organization.Name, membership.Role);
            orgs.Add(currentOrg);
        }
        else if (organization != null)
        {
            currentOrg = new UserOrganizationDto(organization.Id, organization.Name, user.Role ?? "member");
            orgs.Add(currentOrg);
        }

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            TokenType: "Bearer",
            ExpiresIn: _jwtService.GetAccessTokenExpiryMinutes() * 60,
            User: new UserDto(
                Id: user.Id,
                Email: user.Email!,
                Name: user.Name,
                Mobile: user.Mobile,
                AvatarUrl: user.AvatarUrl,
                Role: roleOverride ?? user.Role ?? "member",
                Organization: currentOrg,
                Organizations: orgs
            )
        );
    }

    private async Task HandleFailedLoginAsync(ApplicationUser user, string ipAddress, string? userAgent)
    {
        user.FailedLoginAttempts++;
        user.LastFailedLoginAt = DateTime.UtcNow;

        var wasLocked = false;
        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            user.IsLocked = true;
            user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            wasLocked = true;
            _logger.LogWarning("Account locked due to too many failed attempts: {Email}", user.Email);
        }

        await _userManager.UpdateAsync(user);
        await LogLoginAuditAsync(user.Id, user.Email, false, "invalid_password", ipAddress, userAgent, "password");

        // Log account locked event
        if (wasLocked)
        {
            await LogAuthEventAsync(
                user.Id,
                user.Email,
                AuthEventTypes.AccountLocked,
                true,
                $"Locked after {MaxFailedAttempts} failed attempts",
                ipAddress,
                userAgent);
        }
    }

    private async Task ResetFailedLoginAttemptsAsync(ApplicationUser user, string? ipAddress = null, string? userAgent = null)
    {
        var wasLocked = user.IsLocked;
        if (user.FailedLoginAttempts > 0 || user.IsLocked)
        {
            user.FailedLoginAttempts = 0;
            user.IsLocked = false;
            user.LockedUntil = null;
            await _userManager.UpdateAsync(user);

            // Log account unlocked event if it was locked
            if (wasLocked)
            {
                await LogAuthEventAsync(
                    user.Id,
                    user.Email,
                    AuthEventTypes.AccountUnlocked,
                    true,
                    "Unlocked after successful login",
                    ipAddress,
                    userAgent);
            }
        }
    }

    private async Task RevokeAllUserSessionsAsync(Guid userId, string reason)
    {
        var activeSessions = await _dbContext.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync();

        foreach (var session in activeSessions)
        {
            session.RevokedAt = DateTime.UtcNow;
            session.RevokedReason = reason;
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task LogLoginAuditAsync(
        Guid? userId,
        string? email,
        bool success,
        string? failureReason,
        string ipAddress,
        string? userAgent,
        string authMethod)
    {
        await LogAuthEventAsync(
            userId,
            email,
            success ? AuthEventTypes.Login : AuthEventTypes.LoginFailed,
            success,
            failureReason,
            ipAddress,
            userAgent,
            authMethod);
    }

    private async Task LogAuthEventAsync(
        Guid? userId,
        string? email,
        string eventType,
        bool success,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        string? authMethod = null,
        string? metadata = null)
    {
        var audit = new LoginAudit
        {
            UserId = userId,
            EmailAttempted = email,
            EventType = eventType,
            Success = success,
            FailureReason = failureReason,
            IpAddress = ipAddress ?? "unknown",
            UserAgent = userAgent,
            AuthMethod = authMethod ?? "system",
            Metadata = metadata,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.LoginAudits.Add(audit);
        await _dbContext.SaveChangesAsync();
    }

    private static string GenerateSecureToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLower();
    }

    private static string? NormalizeMobile(string? mobile)
    {
        if (string.IsNullOrEmpty(mobile))
            return null;

        // Remove any non-digit characters and keep last 10 digits
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        return digits.Length >= 10 ? digits[^10..] : digits;
    }

    private static string ExtractDeviceName(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "Unknown Device";

        if (userAgent.Contains("Chrome"))
            return "Chrome Browser";
        if (userAgent.Contains("Firefox"))
            return "Firefox Browser";
        if (userAgent.Contains("Safari"))
            return "Safari Browser";
        if (userAgent.Contains("Edge"))
            return "Edge Browser";

        return "Web Browser";
    }

    #endregion
}

#region Helper Classes

internal class EmailVerificationData
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

internal class PasswordResetData
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

internal class PartialTokenData
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public DeviceInfo? DeviceInfo { get; set; }
    public DateTime CreatedAt { get; set; }
}

internal class TwoFactorSetupData
{
    public Guid UserId { get; set; }
    public string Secret { get; set; } = string.Empty;
    public List<string> BackupCodes { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

internal class OAuthStateData
{
    [System.Text.Json.Serialization.JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("redirectUri")]
    public string? RedirectUri { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("platform")]
    public string? Platform { get; set; }
}

internal class OAuthUserInfo
{
    public string? Id { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Picture { get; set; }
}

#endregion
