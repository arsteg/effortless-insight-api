using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin authentication controller.
/// Handles login, MFA, session management, and password operations.
/// </summary>
[ApiController]
[Route("api/v1/admin/auth")]
[Produces("application/json")]
public class AdminAuthController : ControllerBase
{
    private readonly IAdminAuthService _authService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AdminAuthController> _logger;

    public AdminAuthController(
        IAdminAuthService authService,
        ApplicationDbContext dbContext,
        ILogger<AdminAuthController> logger)
    {
        _authService = authService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Admin login with email and password.
    /// Returns tokens on success or MFA challenge if MFA is enabled.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AdminMfaRequiredResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        var result = await _authService.LoginAsync(request, ipAddress, userAgent);

        return result.Match<IActionResult>(
            response => Ok(new ApiResponse<AdminLoginResponse>
            {
                Success = true,
                Data = response
            }),
            mfaRequired => Ok(new ApiResponse<AdminMfaRequiredResponse>
            {
                Success = true,
                Data = mfaRequired
            }),
            error => error switch
            {
                "ACCOUNT_LOCKED" => StatusCode(StatusCodes.Status423Locked, new ApiResponse<object>
                {
                    Success = false,
                    Error = new ApiError { Code = error, Message = "Account is locked due to too many failed attempts" }
                }),
                "ACCOUNT_SUSPENDED" => StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<object>
                {
                    Success = false,
                    Error = new ApiError { Code = error, Message = "Account has been suspended" }
                }),
                "IP_NOT_WHITELISTED" => StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<object>
                {
                    Success = false,
                    Error = new ApiError { Code = error, Message = "Access denied from this IP address" }
                }),
                _ => Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Error = new ApiError { Code = "INVALID_CREDENTIALS", Message = "Invalid email or password" }
                })
            });
    }

    /// <summary>
    /// Verify MFA code after initial login.
    /// </summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyMfa([FromBody] AdminMfaVerifyRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        var result = await _authService.VerifyMfaAsync(request, ipAddress, userAgent);

        return result.Match<IActionResult>(
            response => Ok(new ApiResponse<AdminLoginResponse>
            {
                Success = true,
                Data = response
            }),
            error => Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = error, Message = GetMfaErrorMessage(error) }
            }));
    }

    /// <summary>
    /// Request password reset for admin account.
    /// Always returns success to prevent email enumeration.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] AdminForgotPasswordRequest request)
    {
        try
        {
            await _authService.RequestPasswordResetAsync(request.Email);
        }
        catch (Exception ex)
        {
            // Log the error but always return success to prevent email enumeration
            _logger.LogWarning(ex, "Admin password reset requested for {Email}", request.Email);
        }

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "If an admin account exists for that email, a password reset link has been sent."
        });
    }

    /// <summary>
    /// Reset password using token from email.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] AdminResetPasswordRequest request)
    {
        var result = await _authService.ResetPasswordAsync(request.Token, request.NewPassword);

        return result.Match<IActionResult>(
            _ => Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Password has been reset successfully."
            }),
            error => BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = error, Message = GetResetPasswordErrorMessage(error) }
            }));
    }

    /// <summary>
    /// Refresh access token using refresh token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AdminLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] AdminRefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken);

        return result.Match<IActionResult>(
            response => Ok(new ApiResponse<AdminLoginResponse>
            {
                Success = true,
                Data = response
            }),
            error => Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = error, Message = "Invalid or expired refresh token" }
            }));
    }

    /// <summary>
    /// Logout and invalidate current session.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        var sessionId = User.FindFirst(AdminJwtService.SessionIdClaim)?.Value;
        if (!string.IsNullOrEmpty(sessionId))
        {
            await _authService.LogoutAsync(sessionId);
        }

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Logged out successfully"
        });
    }

    /// <summary>
    /// Logout from all sessions.
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoutAll()
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (Guid.TryParse(adminIdClaim, out var adminId))
        {
            await _authService.LogoutAllSessionsAsync(adminId);
        }

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Logged out from all sessions"
        });
    }

    /// <summary>
    /// Get MFA setup data (QR code URI and backup codes).
    /// Only available when MFA is not yet enabled.
    /// </summary>
    [HttpPost("mfa/setup")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(typeof(AdminMfaSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetupMfa()
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var result = await _authService.SetupMfaAsync(adminId);

        return result.Match<IActionResult>(
            response => Ok(new ApiResponse<AdminMfaSetupResponse>
            {
                Success = true,
                Data = response
            }),
            error => BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = error, Message = GetSetupMfaErrorMessage(error) }
            }));
    }

    /// <summary>
    /// Enable MFA by verifying a TOTP code.
    /// </summary>
    [HttpPost("mfa/enable")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnableMfa([FromBody] AdminMfaEnableRequest request)
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var success = await _authService.EnableMfaAsync(adminId, request.Code);

        if (success)
        {
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "MFA enabled successfully"
            });
        }

        return BadRequest(new ApiResponse<object>
        {
            Success = false,
            Error = new ApiError { Code = "INVALID_CODE", Message = "Invalid verification code" }
        });
    }

    /// <summary>
    /// Disable MFA (requires super admin or current password).
    /// </summary>
    [HttpPost("mfa/disable")]
    [Authorize(AuthenticationSchemes = "AdminBearer", Policy = "AdminMfaVerified")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DisableMfa([FromBody] AdminMfaDisableRequest request)
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var success = await _authService.DisableMfaAsync(adminId, request.Password);

        if (success)
        {
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "MFA disabled successfully"
            });
        }

        return BadRequest(new ApiResponse<object>
        {
            Success = false,
            Error = new ApiError { Code = "INVALID_PASSWORD", Message = "Invalid password" }
        });
    }

    /// <summary>
    /// Change password.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] AdminChangePasswordRequest request)
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var result = await _authService.ChangePasswordAsync(adminId, request);

        return result.Match<IActionResult>(
            _ => Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Password changed successfully"
            }),
            error => BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = error, Message = GetPasswordErrorMessage(error) }
            }));
    }

    /// <summary>
    /// Get current admin profile.
    /// </summary>
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentAdmin()
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var admin = await _authService.GetAdminByIdAsync(adminId);
        if (admin == null)
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = "NOT_FOUND", Message = "Admin not found" }
            });
        }

        return Ok(new ApiResponse<AdminUserDto>
        {
            Success = true,
            Data = admin
        });
    }

    /// <summary>
    /// Get active sessions for current admin.
    /// </summary>
    [HttpGet("sessions")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(typeof(List<AdminSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSessions()
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var currentSessionId = User.FindFirst(AdminJwtService.SessionIdClaim)?.Value;
        var sessions = await _authService.GetActiveSessionsAsync(adminId);

        // Mark current session
        var sessionDtos = sessions.Select(s => new AdminSessionDto
        {
            SessionId = s.Id,
            IpAddress = s.IpAddress,
            UserAgent = s.UserAgent,
            Location = s.Location,
            CreatedAt = s.CreatedAt,
            LastActivityAt = s.LastActivityAt,
            IsCurrent = s.Id == currentSessionId
        }).ToList();

        return Ok(new ApiResponse<List<AdminSessionDto>>
        {
            Success = true,
            Data = sessionDtos
        });
    }

    /// <summary>
    /// Terminate a specific session.
    /// </summary>
    [HttpDelete("sessions/{sessionId}")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TerminateSession(string sessionId)
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        // Verify the session belongs to this admin
        var sessions = await _authService.GetActiveSessionsAsync(adminId);
        if (!sessions.Any(s => s.Id == sessionId))
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = "NOT_FOUND", Message = "Session not found" }
            });
        }

        await _authService.LogoutAsync(sessionId);

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Session terminated"
        });
    }

    /// <summary>
    /// Get notification preferences for current admin.
    /// </summary>
    [HttpGet("notification-preferences")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(typeof(AdminNotificationPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotificationPreferences()
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var admin = await _dbContext.AdminUsers.FindAsync(adminId);
        if (admin == null)
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = "NOT_FOUND", Message = "Admin not found" }
            });
        }

        return Ok(new ApiResponse<AdminNotificationPreferencesDto>
        {
            Success = true,
            Data = new AdminNotificationPreferencesDto
            {
                CriticalAlerts = admin.NotifyCriticalAlerts,
                SecurityAlerts = admin.NotifySecurityAlerts,
                DailySummary = admin.NotifyDailySummary,
                EmailNotifications = admin.NotifyEmailEnabled
            }
        });
    }

    /// <summary>
    /// Update notification preferences for current admin.
    /// </summary>
    [HttpPut("notification-preferences")]
    [Authorize(AuthenticationSchemes = "AdminBearer")]
    [ProducesResponseType(typeof(AdminNotificationPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateNotificationPreferences([FromBody] AdminNotificationPreferencesDto request)
    {
        var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
        {
            return Unauthorized();
        }

        var admin = await _dbContext.AdminUsers.FindAsync(adminId);
        if (admin == null)
        {
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Error = new ApiError { Code = "NOT_FOUND", Message = "Admin not found" }
            });
        }

        admin.NotifyCriticalAlerts = request.CriticalAlerts;
        admin.NotifySecurityAlerts = request.SecurityAlerts;
        admin.NotifyDailySummary = request.DailySummary;
        admin.NotifyEmailEnabled = request.EmailNotifications;
        admin.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} updated notification preferences", adminId);

        return Ok(new ApiResponse<AdminNotificationPreferencesDto>
        {
            Success = true,
            Data = new AdminNotificationPreferencesDto
            {
                CriticalAlerts = admin.NotifyCriticalAlerts,
                SecurityAlerts = admin.NotifySecurityAlerts,
                DailySummary = admin.NotifyDailySummary,
                EmailNotifications = admin.NotifyEmailEnabled
            }
        });
    }

    private static string GetMfaErrorMessage(string error) => error switch
    {
        "INVALID_MFA_SESSION" => "Invalid or expired MFA session",
        "INVALID_MFA_CODE" => "Invalid verification code",
        "MFA_NOT_ENABLED" => "MFA is not enabled for this account",
        "TOO_MANY_ATTEMPTS" => "Too many failed attempts",
        _ => "MFA verification failed"
    };

    private static string GetSetupMfaErrorMessage(string error) => error switch
    {
        "MFA_ALREADY_ENABLED" => "MFA is already enabled for this account",
        "ADMIN_NOT_FOUND" => "Admin not found",
        _ => "Failed to setup MFA"
    };

    private static string GetPasswordErrorMessage(string error) => error switch
    {
        "INVALID_CURRENT_PASSWORD" => "Current password is incorrect",
        "PASSWORD_TOO_SHORT" => "Password is too short",
        "PASSWORD_TOO_WEAK" => "Password does not meet complexity requirements",
        "PASSWORD_RECENTLY_USED" => "This password was recently used",
        _ => "Failed to change password"
    };

    private static string GetResetPasswordErrorMessage(string error) => error switch
    {
        "INVALID_TOKEN" => "Invalid or expired reset token",
        "TOKEN_EXPIRED" => "Reset token has expired",
        "PASSWORD_TOO_SHORT" => "Password is too short",
        "PASSWORD_TOO_WEAK" => "Password does not meet complexity requirements",
        _ => "Failed to reset password"
    };
}

/// <summary>
/// Request to refresh access token.
/// </summary>
public record AdminRefreshTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>
/// Request to enable MFA.
/// </summary>
public record AdminMfaEnableRequest
{
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// Request to disable MFA.
/// </summary>
public record AdminMfaDisableRequest
{
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Admin session DTO.
/// </summary>
public record AdminSessionDto
{
    public string SessionId { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? Location { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
    public bool IsCurrent { get; init; }
}

/// <summary>
/// Request to reset admin password.
/// </summary>
public record AdminForgotPasswordRequest
{
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// Request to complete password reset.
/// </summary>
public record AdminResetPasswordRequest
{
    public string Token { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>
/// Admin notification preferences DTO.
/// </summary>
public record AdminNotificationPreferencesDto
{
    public bool CriticalAlerts { get; init; } = true;
    public bool SecurityAlerts { get; init; } = true;
    public bool DailySummary { get; init; } = false;
    public bool EmailNotifications { get; init; } = true;
}
