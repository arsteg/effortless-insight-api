using System.Security.Claims;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
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
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    public IActionResult GetCurrentUser()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? "");
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value ?? "";
        var name = User.FindFirst("name")?.Value ?? "";
        var role = User.FindFirst("role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var orgId = User.FindFirst("org_id")?.Value;
        var orgName = User.FindFirst("org_name")?.Value;

        var user = new UserDto(
            Id: userId,
            Email: email,
            Name: name,
            Mobile: null, // Would need to fetch from DB for full profile
            AvatarUrl: null,
            Role: role,
            OrganizationId: orgId != null ? Guid.Parse(orgId) : null,
            OrganizationName: orgName
        );

        return Ok(new ApiResponse<UserDto>(true, user));
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
