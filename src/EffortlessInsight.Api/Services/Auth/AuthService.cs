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

    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 15;
    private const int VerificationTokenExpiryHours = 24;
    private const int PasswordResetTokenExpiryHours = 24;
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
        IOtpService otpService)
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
        await ResetFailedLoginAttemptsAsync(user);

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
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Remove the token from cache
        await _cache.RemoveAsync(cacheKey);

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
            var resetLink = $"{_configuration["App:BaseUrl"]}/auth/reset-password?token={resetToken}";
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

        _logger.LogInformation("Password changed for user: {UserId}", userId);
    }

    public async Task LogoutAsync(Guid userId, string? refreshTokenJti, bool allDevices)
    {
        if (allDevices)
        {
            await RevokeAllUserSessionsAsync(userId, "logout");
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
        await ResetFailedLoginAttemptsAsync(user);

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

        // Debug logging
        Console.WriteLine($"CreateLoginSessionAsync: userId={user.Id}, membershipExists={membership != null}, membershipRole={membership?.Role ?? "NULL"}, orgId={organization?.Id}, roleOverride={roleOverride ?? "NULL"}");

        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user, organization, roleOverride);
        var (refreshToken, jti, expiresAt) = _jwtService.GenerateRefreshToken(rememberMe);

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

        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            user.IsLocked = true;
            user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
            _logger.LogWarning("Account locked due to too many failed attempts: {Email}", user.Email);
        }

        await _userManager.UpdateAsync(user);
        await LogLoginAuditAsync(user.Id, user.Email, false, "invalid_password", ipAddress, userAgent, "password");
    }

    private async Task ResetFailedLoginAttemptsAsync(ApplicationUser user)
    {
        if (user.FailedLoginAttempts > 0 || user.IsLocked)
        {
            user.FailedLoginAttempts = 0;
            user.IsLocked = false;
            user.LockedUntil = null;
            await _userManager.UpdateAsync(user);
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
        var audit = new LoginAudit
        {
            UserId = userId,
            EmailAttempted = email,
            Success = success,
            FailureReason = failureReason,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AuthMethod = authMethod,
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

#endregion
