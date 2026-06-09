using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ISessionService _sessionService;
    private readonly IOrganizationManagementService _organizationService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ISessionService sessionService,
        IOrganizationManagementService organizationService,
        ApplicationDbContext dbContext,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _sessionService = sessionService;
        _organizationService = organizationService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user with email and password
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _authService.RegisterAsync(request, ipAddress, userAgent);

            return StatusCode(StatusCodes.Status201Created, new ApiResponse<RegisterResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_EXISTS")
        {
            return Conflict(new ApiErrorResponse(false, "EMAIL_EXISTS", "Email is already registered"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "MOBILE_EXISTS")
        {
            return Conflict(new ApiErrorResponse(false, "MOBILE_EXISTS", "Mobile number is already registered"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("REGISTRATION_FAILED"))
        {
            return BadRequest(new ApiErrorResponse(false, "REGISTRATION_FAILED", ex.Message.Replace("REGISTRATION_FAILED: ", "")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for email: {Email}", request.Email);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Verify email address using token from email link
    /// </summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        try
        {
            await _authService.VerifyEmailAsync(request.Token);

            return Ok(new ApiResponse<object>(true, new
            {
                Message = "Email verified successfully",
                RedirectUrl = "/onboarding"
            }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_TOKEN")
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_TOKEN", "Verification token is invalid"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "ALREADY_VERIFIED")
        {
            return BadRequest(new ApiErrorResponse(false, "ALREADY_VERIFIED", "Email is already verified"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email verification failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Login with email and password
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TwoFactorRequiredResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _authService.LoginAsync(request, ipAddress, userAgent);

            if (result is TwoFactorRequiredResponse twoFactorResponse)
            {
                return Ok(new ApiResponse<TwoFactorRequiredResponse>(true, twoFactorResponse));
            }

            return Ok(new ApiResponse<LoginResponse>(true, (LoginResponse)result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_CREDENTIALS")
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_CREDENTIALS", "Invalid email or password"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "EMAIL_NOT_VERIFIED")
        {
            return Unauthorized(new ApiErrorResponse(false, "EMAIL_NOT_VERIFIED", "Please verify your email before logging in"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message.StartsWith("ACCOUNT_LOCKED"))
        {
            var minutes = ex.Message.Contains(":") ? ex.Message.Split(':')[1] : "15";
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ACCOUNT_LOCKED", $"Account locked due to too many failed attempts. Try again in {minutes} minutes."));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ACCOUNT_DISABLED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ACCOUNT_DISABLED", "Account has been disabled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for email: {Email}", request.Email);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<TokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _authService.RefreshTokenAsync(request.RefreshToken, ipAddress, userAgent);

            return Ok(new ApiResponse<TokenResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_REFRESH_TOKEN")
        {
            return Unauthorized(new ApiErrorResponse(false, "INVALID_REFRESH_TOKEN", "Refresh token is invalid"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "REFRESH_TOKEN_EXPIRED")
        {
            return Unauthorized(new ApiErrorResponse(false, "REFRESH_TOKEN_EXPIRED", "Refresh token has expired"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ACCOUNT_DISABLED")
        {
            return Unauthorized(new ApiErrorResponse(false, "ACCOUNT_DISABLED", "Account has been disabled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Request password reset email
    /// </summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();

            await _authService.ForgotPasswordAsync(request.Email, ipAddress);

            // Always return success to prevent email enumeration
            return Ok(new ApiResponse<object>(true, new
            {
                Message = "If the email exists, a password reset link has been sent"
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forgot password failed for email: {Email}", request.Email);
            // Still return success to prevent email enumeration
            return Ok(new ApiResponse<object>(true, new
            {
                Message = "If the email exists, a password reset link has been sent"
            }));
        }
    }

    /// <summary>
    /// Reset password using token from email
    /// </summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            await _authService.ResetPasswordAsync(request);

            return Ok(new ApiResponse<object>(true, new
            {
                Message = "Password reset successful. Please log in with your new password."
            }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "PASSWORD_MISMATCH")
        {
            return BadRequest(new ApiErrorResponse(false, "PASSWORD_MISMATCH", "Passwords do not match"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVALID_TOKEN")
        {
            return BadRequest(new ApiErrorResponse(false, "INVALID_TOKEN", "Reset token is invalid or expired"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("PASSWORD_RESET_FAILED"))
        {
            return BadRequest(new ApiErrorResponse(false, "PASSWORD_TOO_WEAK", "Password does not meet requirements"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password reset failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Change password for authenticated user
    /// </summary>
    [Authorize]
    [HttpPut("change-password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _authService.ChangePasswordAsync(userId, request);

            return Ok(new ApiResponse<object>(true, new
            {
                Message = "Password changed successfully"
            }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "PASSWORD_MISMATCH")
        {
            return BadRequest(new ApiErrorResponse(false, "PASSWORD_MISMATCH", "Passwords do not match"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_CURRENT_PASSWORD")
        {
            return Unauthorized(new ApiErrorResponse(false, "INVALID_CURRENT_PASSWORD", "Current password is incorrect"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("PASSWORD_CHANGE_FAILED"))
        {
            return BadRequest(new ApiErrorResponse(false, "PASSWORD_TOO_WEAK", "Password does not meet requirements"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password change failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Logout current session
    /// </summary>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;

            await _authService.LogoutAsync(userId, jti, request?.AllDevices ?? false);

            return Ok(new ApiResponse<object>(true, new
            {
                Message = "Logged out successfully"
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get current user profile
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser()
    {
        try
        {
            var userId = GetCurrentUserId();

            var user = await _dbContext.Users
                .Include(u => u.Organization)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", "User not found"));
            }

            // Get all organization memberships for this user
            var memberships = await _dbContext.OrganizationMembers
                .Include(m => m.Organization)
                .Where(m => m.UserId == userId && m.Status == "active" && m.Organization.DeletedAt == null)
                .ToListAsync();

            // Get current organization from claims or user's default
            var currentOrgIdClaim = User.FindFirst("org_id")?.Value;
            Guid? currentOrgId = !string.IsNullOrEmpty(currentOrgIdClaim) && Guid.TryParse(currentOrgIdClaim, out var id) ? id : user.OrganizationId;

            // Determine current organization
            UserOrganizationDto? currentOrg = null;
            var currentMembership = memberships.FirstOrDefault(m => m.OrganizationId == currentOrgId);
            if (currentMembership != null)
            {
                currentOrg = new UserOrganizationDto(
                    currentMembership.OrganizationId,
                    currentMembership.Organization.Name,
                    currentMembership.Role
                );
            }
            else if (user.Organization != null)
            {
                // Fallback to legacy single-org relationship
                currentOrg = new UserOrganizationDto(
                    user.Organization.Id,
                    user.Organization.Name,
                    user.Role
                );
            }

            // Build list of all organizations
            var organizations = memberships.Select(m => new UserOrganizationDto(
                m.OrganizationId,
                m.Organization.Name,
                m.Role
            )).ToList();

            // If no memberships found but user has legacy org, include it
            if (!organizations.Any() && currentOrg != null)
            {
                organizations.Add(currentOrg);
            }

            var profile = new UserProfileDto(
                Id: user.Id,
                Email: user.Email!,
                Name: user.Name,
                Mobile: user.Mobile,
                AvatarUrl: user.AvatarUrl,
                EmailVerified: user.EmailConfirmed,
                MobileVerified: user.IsMobileVerified,
                Is2faEnabled: user.Is2faEnabled,
                Role: currentOrg?.Role ?? user.Role,
                Organization: currentOrg,
                Organizations: organizations,
                Preferences: user.Preferences,
                CreatedAt: user.CreatedAt,
                LastLogin: user.LastLoginAt
            );

            return Ok(new ApiResponse<UserProfileDto>(true, profile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get current user failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Switch to a different organization
    /// </summary>
    [Authorize]
    [HttpPost("switch-organization")]
    [ProducesResponseType(typeof(ApiResponse<SwitchOrganizationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SwitchOrganization([FromBody] SwitchOrganizationRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var ipAddress = GetClientIpAddress();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _organizationService.SwitchOrganizationAsync(request, userId, ipAddress, userAgent);

            return Ok(new ApiResponse<SwitchOrganizationResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "NOT_A_MEMBER")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "NOT_A_MEMBER", "You are not a member of this organization or your access has expired"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Switch organization failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Request OTP for login
    /// </summary>
    [HttpPost("otp/request")]
    [ProducesResponseType(typeof(ApiResponse<OtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestOtp([FromBody] OtpRequestRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var result = await _authService.RequestOtpLoginAsync(request.Mobile, ipAddress);

            return Ok(new ApiResponse<OtpResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "MOBILE_NOT_FOUND")
        {
            return NotFound(new ApiErrorResponse(false, "MOBILE_NOT_FOUND", "Mobile number not registered"));
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("RATE_LIMIT_EXCEEDED"))
        {
            var retryAfter = ex.Message.Split(':').Length > 1 ? ex.Message.Split(':')[1] : "3600";
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ApiErrorResponse(false, "RATE_LIMIT_EXCEEDED", $"Too many OTP requests. Try again in {retryAfter} seconds."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTP request failed for mobile: {Mobile}", request.Mobile);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Verify OTP and login
    /// </summary>
    [HttpPost("otp/verify")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TwoFactorRequiredResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _authService.VerifyOtpLoginAsync(request, ipAddress, userAgent);

            if (result is TwoFactorRequiredResponse twoFactorResponse)
            {
                return Ok(new ApiResponse<TwoFactorRequiredResponse>(true, twoFactorResponse));
            }

            return Ok(new ApiResponse<LoginResponse>(true, (LoginResponse)result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_OTP")
        {
            return Unauthorized(new ApiErrorResponse(false, "INVALID_OTP", "Invalid or expired OTP"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "USER_NOT_FOUND")
        {
            return Unauthorized(new ApiErrorResponse(false, "USER_NOT_FOUND", "No account found with this mobile number"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "ACCOUNT_DISABLED")
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(false, "ACCOUNT_DISABLED", "Account has been disabled"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "MAX_ATTEMPTS_EXCEEDED")
        {
            return BadRequest(new ApiErrorResponse(false, "MAX_ATTEMPTS_EXCEEDED", "Too many invalid OTP attempts. Please request a new OTP."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTP verification failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Setup two-factor authentication
    /// </summary>
    [Authorize]
    [HttpPost("2fa/setup")]
    [ProducesResponseType(typeof(ApiResponse<TwoFactorSetupResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Setup2fa()
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _authService.Setup2faAsync(userId);

            return Ok(new ApiResponse<TwoFactorSetupResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "2FA_ALREADY_ENABLED")
        {
            return BadRequest(new ApiErrorResponse(false, "2FA_ALREADY_ENABLED", "Two-factor authentication is already enabled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2FA setup failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Verify 2FA setup with TOTP code
    /// </summary>
    [Authorize]
    [HttpPost("2fa/verify-setup")]
    [ProducesResponseType(typeof(ApiResponse<TwoFactorVerifySetupResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifySetup2fa([FromBody] TwoFactorVerifySetupRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _authService.VerifySetup2faAsync(userId, request.Code);

            return Ok(new ApiResponse<TwoFactorVerifySetupResponse>(true, result));
        }
        catch (InvalidOperationException ex) when (ex.Message == "2FA_ALREADY_ENABLED")
        {
            return BadRequest(new ApiErrorResponse(false, "2FA_ALREADY_ENABLED", "Two-factor authentication is already enabled"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "2FA_SETUP_NOT_FOUND")
        {
            return BadRequest(new ApiErrorResponse(false, "2FA_SETUP_NOT_FOUND", "No pending 2FA setup found. Please start setup again."));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_2FA_CODE")
        {
            return Unauthorized(new ApiErrorResponse(false, "INVALID_2FA_CODE", "Invalid verification code"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2FA verify setup failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Complete login with 2FA code
    /// </summary>
    [HttpPost("2fa/login")]
    [ProducesResponseType(typeof(ApiResponse<TwoFactorLoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login2fa([FromBody] TwoFactorLoginRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _authService.Complete2faLoginAsync(request, ipAddress, userAgent);

            return Ok(new ApiResponse<TwoFactorLoginResponse>(true, result));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_PARTIAL_TOKEN")
        {
            return Unauthorized(new ApiErrorResponse(false, "INVALID_PARTIAL_TOKEN", "Session expired. Please login again."));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_2FA_CODE")
        {
            return Unauthorized(new ApiErrorResponse(false, "INVALID_2FA_CODE", "Invalid verification code"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "2FA_NOT_ENABLED")
        {
            return BadRequest(new ApiErrorResponse(false, "2FA_NOT_ENABLED", "Two-factor authentication is not enabled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2FA login failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Disable two-factor authentication
    /// </summary>
    [Authorize]
    [HttpDelete("2fa")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Disable2fa([FromBody] TwoFactorDisableRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _authService.Disable2faAsync(userId, request.Password);

            return Ok(new ApiResponse<object>(true, new
            {
                Message = "Two-factor authentication disabled successfully"
            }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "2FA_NOT_ENABLED")
        {
            return BadRequest(new ApiErrorResponse(false, "2FA_NOT_ENABLED", "Two-factor authentication is not enabled"));
        }
        catch (UnauthorizedAccessException ex) when (ex.Message == "INVALID_PASSWORD")
        {
            return Unauthorized(new ApiErrorResponse(false, "INVALID_PASSWORD", "Password is incorrect"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2FA disable failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get all active sessions for current user
    /// </summary>
    [Authorize]
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ApiResponse<SessionListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions()
    {
        try
        {
            var userId = GetCurrentUserId();
            var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value ?? "";

            var result = await _sessionService.GetUserSessionsAsync(userId, jti);

            return Ok(new ApiResponse<SessionListResponse>(true, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get sessions failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Revoke a specific session
    /// </summary>
    [Authorize]
    [HttpDelete("sessions/{sessionId}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value ?? "";

            await _sessionService.RevokeSessionAsync(userId, sessionId, jti);

            return Ok(new ApiResponse<object>(true, new
            {
                Message = "Session revoked successfully"
            }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "SESSION_NOT_FOUND")
        {
            return NotFound(new ApiErrorResponse(false, "SESSION_NOT_FOUND", "Session not found"));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_REVOKE_CURRENT_SESSION")
        {
            return BadRequest(new ApiErrorResponse(false, "CANNOT_REVOKE_CURRENT_SESSION", "Cannot revoke current session. Use logout instead."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Revoke session failed");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "INTERNAL_ERROR", "An unexpected error occurred"));
        }
    }

    #region Private Methods

    private string GetClientIpAddress()
    {
        // Check for forwarded headers first (for reverse proxy setups)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        return Guid.Parse(userIdClaim);
    }

    #endregion
}
