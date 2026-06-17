using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AdminAuditLogSummaryDto = EffortlessInsight.Api.DTOs.Admin.AdminAuditLogSummaryDto;
using AdminSessionSummaryDto = EffortlessInsight.Api.DTOs.Admin.AdminSessionSummaryDto;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing admin users.
/// Only accessible by super admins.
/// </summary>
[Route("api/v1/admin/admins")]
[Authorize(Policy = "AdminSuperAdmin")]
public class AdminAdminsController : AdminControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAdminAuditService _auditService;
    private readonly IAdminMfaService _mfaService;

    public AdminAdminsController(
        ApplicationDbContext dbContext,
        IAdminAuditService auditService,
        IAdminMfaService mfaService,
        ILogger<AdminAdminsController> logger)
        : base(logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _mfaService = mfaService;
    }

    /// <summary>
    /// List all admin users.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminUserListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdmins([FromQuery] AdminUserSearchRequest request)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        var query = _dbContext.AdminUsers
            .Where(a => a.DeletedAt == null)
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrEmpty(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(a =>
                a.Email.ToLower().Contains(searchLower) ||
                a.Name.ToLower().Contains(searchLower));
        }

        // Apply role filter
        if (!string.IsNullOrEmpty(request.Role))
        {
            query = query.Where(a => a.Role == request.Role);
        }

        // Apply status filter
        if (request.IsActive.HasValue)
        {
            query = query.Where(a => a.IsActive == request.IsActive.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var admins = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminUserSummary
            {
                Id = a.Id,
                Email = a.Email,
                Name = a.Name,
                Role = a.Role,
                IsActive = a.IsActive,
                MfaEnabled = a.MfaEnabled,
                LastLoginAt = a.LastLoginAt,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Success(new AdminUserListResponse
        {
            Admins = admins,
            Pagination = new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        });
    }

    /// <summary>
    /// Get admin user details.
    /// </summary>
    [HttpGet("{adminId:guid}")]
    [ProducesResponseType(typeof(AdminUserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAdmin(Guid adminId)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        if (admin == null)
        {
            return NotFoundResponse("Admin user not found");
        }

        // Get recent activity
        var recentActivityLogs = await _dbContext.AdminAuditLogs
            .Where(a => a.AdminUserId == adminId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .Select(a => new AdminAuditLogSummaryDto
            {
                Id = a.Id,
                Action = a.Action,
                TargetType = a.TargetType,
                TargetId = a.TargetId,
                Description = a.Description,
                Outcome = a.Outcome,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        // Get active sessions
        var activeSessions = await _dbContext.AdminSessions
            .Where(s => s.AdminUserId == adminId && s.IsActive && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => new AdminSessionSummaryDto
            {
                SessionId = s.Id,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                CreatedAt = s.CreatedAt,
                LastActivityAt = s.LastActivityAt
            })
            .ToListAsync();

        return Success(new AdminUserDetailDto
        {
            Id = admin.Id,
            Email = admin.Email,
            Name = admin.Name,
            AvatarUrl = admin.AvatarUrl,
            Role = admin.Role,
            Permissions = admin.Permissions,
            IsActive = admin.IsActive,
            MfaEnabled = admin.MfaEnabled,
            IsLocked = admin.IsLocked,
            LockedUntil = admin.LockedUntil,
            MustChangePassword = admin.MustChangePassword,
            PasswordChangedAt = admin.PasswordChangedAt,
            IpWhitelist = admin.IpWhitelist,
            LastLoginAt = admin.LastLoginAt,
            LastLoginIp = admin.LastLoginIp,
            CreatedAt = admin.CreatedAt,
            UpdatedAt = admin.UpdatedAt,
            RecentActivity = recentActivityLogs,
            ActiveSessions = activeSessions
        });
    }

    /// <summary>
    /// Create a new admin user.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAdmin([FromBody] AdminCreateRequest request)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        // Check if email already exists
        var existingAdmin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.EmailNormalized == request.Email.ToUpperInvariant() && a.DeletedAt == null);

        if (existingAdmin != null)
        {
            return Error("Email already in use", "EMAIL_EXISTS");
        }

        // Validate role
        if (!AdminRoles.IsValid(request.Role))
        {
            return Error("Invalid role", "INVALID_ROLE");
        }

        var admin = new AdminUser
        {
            Email = request.Email,
            EmailNormalized = request.Email.ToUpperInvariant(),
            Name = request.Name,
            Role = request.Role,
            Permissions = request.Permissions ?? AdminPermissions.GetDefaultPermissions(request.Role),
            PasswordHash = HashPassword(request.Password),
            MustChangePassword = true,
            IsActive = true
        };

        _dbContext.AdminUsers.Add(admin);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.AdminCreated,
            AuditTargetTypes.AdminUser,
            admin.Id.ToString(),
            $"Admin user created: {admin.Email}",
            new Dictionary<string, object>
            {
                ["email"] = admin.Email,
                ["role"] = admin.Role
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Created($"/api/v1/admin/admins/{admin.Id}", Success(new AdminUserDto
        {
            Id = admin.Id,
            Email = admin.Email,
            Name = admin.Name,
            Role = admin.Role,
            Permissions = admin.Permissions,
            MfaEnabled = admin.MfaEnabled,
            IsActive = admin.IsActive,
            LastLoginAt = admin.LastLoginAt,
            CreatedAt = admin.CreatedAt
        }));
    }

    /// <summary>
    /// Update admin user.
    /// </summary>
    [HttpPatch("{adminId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAdmin(Guid adminId, [FromBody] AdminUpdateRequest request)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        if (admin == null)
        {
            return NotFoundResponse("Admin user not found");
        }

        // Prevent self-demotion
        if (adminId == CurrentAdminId && request.Role != null && request.Role != admin.Role)
        {
            return Error("Cannot change your own role", "CANNOT_SELF_DEMOTE");
        }

        var changes = new Dictionary<string, object>();

        if (request.Name != null && request.Name != admin.Name)
        {
            changes["name"] = new { before = admin.Name, after = request.Name };
            admin.Name = request.Name;
        }

        if (request.Role != null && request.Role != admin.Role)
        {
            if (!AdminRoles.IsValid(request.Role))
            {
                return Error("Invalid role", "INVALID_ROLE");
            }
            changes["role"] = new { before = admin.Role, after = request.Role };
            admin.Role = request.Role;
            admin.Permissions = AdminPermissions.GetDefaultPermissions(request.Role);
        }

        if (request.Permissions != null)
        {
            changes["permissions"] = new { before = admin.Permissions, after = request.Permissions };
            admin.Permissions = request.Permissions;
        }

        if (request.IpWhitelist != null)
        {
            changes["ip_whitelist"] = new { before = admin.IpWhitelist, after = request.IpWhitelist };
            admin.IpWhitelist = request.IpWhitelist;
        }

        admin.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.AdminUpdated,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            "Admin user updated",
            changes,
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "Admin user updated successfully");
    }

    /// <summary>
    /// Suspend admin user.
    /// </summary>
    [HttpPost("{adminId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendAdmin(Guid adminId, [FromBody] SuspendAdminRequest request)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        if (adminId == CurrentAdminId)
        {
            return Error("Cannot suspend yourself", "CANNOT_SELF_SUSPEND");
        }

        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        if (admin == null)
        {
            return NotFoundResponse("Admin user not found");
        }

        admin.IsActive = false;
        admin.UpdatedAt = DateTime.UtcNow;

        // Invalidate all sessions
        var sessions = await _dbContext.AdminSessions
            .Where(s => s.AdminUserId == adminId && s.IsActive)
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.IsActive = false;
            session.InvalidatedAt = DateTime.UtcNow;
            session.InvalidationReason = SessionInvalidationReasons.AccountSuspended;
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.AdminSuspended,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            $"Admin user suspended: {request.Reason}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason,
                ["sessions_terminated"] = sessions.Count
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "Admin user suspended");
    }

    /// <summary>
    /// Reactivate admin user.
    /// </summary>
    [HttpPost("{adminId:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateAdmin(Guid adminId)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        if (admin == null)
        {
            return NotFoundResponse("Admin user not found");
        }

        admin.IsActive = true;
        admin.IsLocked = false;
        admin.LockedUntil = null;
        admin.FailedLoginAttempts = 0;
        admin.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.AdminReactivated,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            "Admin user reactivated",
            ipAddress: ClientIpAddress,
            userAgent: ClientUserAgent,
            sessionId: CurrentSessionId);

        return Success<object?>(null, "Admin user reactivated");
    }

    /// <summary>
    /// Reset admin password.
    /// </summary>
    [HttpPost("{adminId:guid}/reset-password")]
    [ProducesResponseType(typeof(ResetPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid adminId)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        if (admin == null)
        {
            return NotFoundResponse("Admin user not found");
        }

        // Generate temporary password
        var tempPassword = GenerateTemporaryPassword();
        admin.PasswordHash = HashPassword(tempPassword);
        admin.MustChangePassword = true;
        admin.PasswordChangedAt = DateTime.UtcNow;
        admin.UpdatedAt = DateTime.UtcNow;

        // Invalidate all sessions
        var sessions = await _dbContext.AdminSessions
            .Where(s => s.AdminUserId == adminId && s.IsActive)
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.IsActive = false;
            session.InvalidatedAt = DateTime.UtcNow;
            session.InvalidationReason = SessionInvalidationReasons.PasswordChange;
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.PasswordResetByAdmin,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            "Password reset by admin",
            new Dictionary<string, object>
            {
                ["sessions_terminated"] = sessions.Count
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        // In production, this would send an email
        return Success(new ResetPasswordResponse
        {
            TemporaryPassword = tempPassword,
            Message = "Temporary password generated. Admin must change on first login."
        });
    }

    /// <summary>
    /// Disable MFA for admin.
    /// </summary>
    [HttpPost("{adminId:guid}/disable-mfa")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableMfa(Guid adminId, [FromBody] DisableMfaRequest request)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        if (admin == null)
        {
            return NotFoundResponse("Admin user not found");
        }

        if (!admin.MfaEnabled)
        {
            return Error("MFA is not enabled", "MFA_NOT_ENABLED");
        }

        admin.MfaEnabled = false;
        admin.MfaSecretEncrypted = null;
        admin.BackupCodesHash = null;
        admin.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.MfaDisabledByAdmin,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            $"MFA disabled by admin: {request.Reason}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "MFA disabled");
    }

    /// <summary>
    /// Delete admin user.
    /// </summary>
    [HttpDelete("{adminId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAdmin(Guid adminId)
    {
        if (!HasPermission(AdminPermissions.AdminManage))
        {
            return Forbid();
        }

        if (adminId == CurrentAdminId)
        {
            return Error("Cannot delete yourself", "CANNOT_SELF_DELETE");
        }

        var admin = await _dbContext.AdminUsers
            .FirstOrDefaultAsync(a => a.Id == adminId && a.DeletedAt == null);

        if (admin == null)
        {
            return NotFoundResponse("Admin user not found");
        }

        // Soft delete
        admin.DeletedAt = DateTime.UtcNow;
        admin.IsActive = false;

        // Invalidate all sessions
        var sessions = await _dbContext.AdminSessions
            .Where(s => s.AdminUserId == adminId && s.IsActive)
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.IsActive = false;
            session.InvalidatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.AdminDeleted,
            AuditTargetTypes.AdminUser,
            adminId.ToString(),
            $"Admin user deleted: {admin.Email}",
            ipAddress: ClientIpAddress,
            userAgent: ClientUserAgent,
            sessionId: CurrentSessionId);

        return Success<object?>(null, "Admin user deleted");
    }

    /// <summary>
    /// Get available roles.
    /// </summary>
    [HttpGet("roles")]
    [ProducesResponseType(typeof(List<RoleInfo>), StatusCodes.Status200OK)]
    public IActionResult GetRoles()
    {
        var roles = AdminRoles.All.Select(role => new RoleInfo
        {
            Code = role,
            Name = role.Replace("_", " ").ToTitleCase(),
            DefaultPermissions = AdminPermissions.GetDefaultPermissions(role)
        }).ToList();

        return Success(roles);
    }

    // Private helpers

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    private static string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%^&*";
        var random = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[20];
        random.GetBytes(bytes);

        var password = new char[20];
        for (int i = 0; i < 20; i++)
        {
            password[i] = chars[bytes[i] % chars.Length];
        }
        return new string(password);
    }
}

// Extension method
public static class StringExtensions
{
    public static string ToTitleCase(this string str)
    {
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str.ToLower());
    }
}

// DTOs

public record AdminUserSearchRequest
{
    public string? Search { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public record AdminUserListResponse
{
    public List<AdminUserSummary> Admins { get; init; } = [];
    public PaginationInfo Pagination { get; init; } = new();
}

public record AdminUserSummary
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool MfaEnabled { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record AdminSessionSummary
{
    public string SessionId { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
}

public record SuspendAdminRequest
{
    public string Reason { get; init; } = string.Empty;
}

public record DisableMfaRequest
{
    public string Reason { get; init; } = string.Empty;
}

public record ResetPasswordResponse
{
    public string TemporaryPassword { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public record RoleInfo
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<string> DefaultPermissions { get; init; } = [];
}
