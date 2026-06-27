using System.Security.Claims;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Base controller for all admin API endpoints.
/// Provides common functionality for admin authentication and auditing.
/// </summary>
[ApiController]
[Route("api/v1/admin/[controller]")]
[Authorize(AuthenticationSchemes = "AdminBearer")]
[Produces("application/json")]
public abstract class AdminControllerBase : ControllerBase
{
    protected readonly ILogger _logger;

    protected AdminControllerBase(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the current admin user ID from claims.
    /// </summary>
    protected Guid CurrentAdminId
    {
        get
        {
            var adminIdClaim = User.FindFirst(AdminJwtService.AdminIdClaim)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(adminIdClaim) || !Guid.TryParse(adminIdClaim, out var adminId))
            {
                throw new UnauthorizedAccessException("Invalid admin token");
            }

            return adminId;
        }
    }

    /// <summary>
    /// Gets the current admin's role from claims.
    /// </summary>
    protected string CurrentAdminRole =>
        User.FindFirst(AdminJwtService.AdminRoleClaim)?.Value ?? string.Empty;

    /// <summary>
    /// Gets the current admin's name from claims.
    /// </summary>
    protected string CurrentAdminName =>
        User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    /// <summary>
    /// Gets the current admin's email from claims.
    /// </summary>
    protected string CurrentAdminEmail =>
        User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

    /// <summary>
    /// Gets the current session ID from claims.
    /// </summary>
    protected string? CurrentSessionId =>
        User.FindFirst(AdminJwtService.SessionIdClaim)?.Value;

    /// <summary>
    /// Gets whether MFA has been verified for this session.
    /// </summary>
    protected bool IsMfaVerified =>
        bool.TryParse(User.FindFirst(AdminJwtService.MfaVerifiedClaim)?.Value, out var mfa) && mfa;

    /// <summary>
    /// Gets the current admin's permissions from claims.
    /// </summary>
    protected List<string> CurrentPermissions
    {
        get
        {
            var permissionsClaim = User.FindFirst(AdminJwtService.PermissionsClaim)?.Value;
            return string.IsNullOrEmpty(permissionsClaim)
                ? []
                : permissionsClaim.Split(',').ToList();
        }
    }

    /// <summary>
    /// Checks if the current admin has a specific permission.
    /// </summary>
    protected bool HasPermission(string permission)
    {
        // Super admins have all permissions
        if (CurrentAdminRole == AdminRoles.SuperAdmin)
        {
            return true;
        }

        return CurrentPermissions.Contains(permission);
    }

    /// <summary>
    /// Checks if the current admin has any of the specified permissions.
    /// </summary>
    protected bool HasAnyPermission(params string[] permissions)
    {
        if (CurrentAdminRole == AdminRoles.SuperAdmin)
        {
            return true;
        }

        return permissions.Any(p => CurrentPermissions.Contains(p));
    }

    /// <summary>
    /// Gets the client IP address.
    /// </summary>
    protected string? ClientIpAddress =>
        HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Gets the client user agent.
    /// </summary>
    protected string? ClientUserAgent =>
        HttpContext.Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Returns a forbidden response if the admin lacks permission.
    /// </summary>
    protected IActionResult ForbiddenIfNoPermission(string permission)
    {
        if (!HasPermission(permission))
        {
            _logger.LogWarning(
                "Admin {AdminId} denied access due to missing permission: {Permission}",
                CurrentAdminId, permission);

            return Forbid();
        }

        return null!;
    }

    /// <summary>
    /// Returns a standard success response.
    /// </summary>
    protected IActionResult Success<T>(T data, string? message = null)
    {
        return Ok(new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        });
    }

    /// <summary>
    /// Returns a standard error response.
    /// </summary>
    protected IActionResult Error(string message, string? code = null, int statusCode = 400)
    {
        return StatusCode(statusCode, new ApiResponse<object>
        {
            Success = false,
            Error = new ApiError
            {
                Code = code ?? "ERROR",
                Message = message
            }
        });
    }

    /// <summary>
    /// Returns a not found response.
    /// </summary>
    protected IActionResult NotFoundResponse(string message = "Resource not found")
    {
        return NotFound(new ApiResponse<object>
        {
            Success = false,
            Error = new ApiError
            {
                Code = "NOT_FOUND",
                Message = message
            }
        });
    }
}

/// <summary>
/// Standard API response wrapper.
/// </summary>
public class ApiResponse<T>
{
    public ApiResponse() { }

    public ApiResponse(bool success, T data)
    {
        Success = success;
        Data = data;
    }

    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public ApiError? Error { get; set; }
}

/// <summary>
/// API error details.
/// </summary>
public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string[]>? ValidationErrors { get; set; }
}
