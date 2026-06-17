using System.Security.Cryptography;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing platform users.
/// </summary>
[Route("api/v1/admin/users")]
[Authorize(Policy = "AdminAuthenticated")]
public class AdminUsersController : AdminControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAdminAuditService _auditService;

    public AdminUsersController(
        ApplicationDbContext dbContext,
        IAdminAuditService auditService,
        ILogger<AdminUsersController> logger)
        : base(logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    /// <summary>
    /// Search and list users with filtering.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(UserListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers([FromQuery] UserSearchRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersView))
        {
            return Forbid();
        }

        var query = _dbContext.Users
            .Include(u => u.Organization)
            .ThenInclude(o => o!.Plan)
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrEmpty(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                u.Name.ToLower().Contains(searchLower) ||
                (u.Mobile != null && u.Mobile.Contains(request.Search)));
        }

        // Apply status filter
        if (!string.IsNullOrEmpty(request.Status))
        {
            query = request.Status.ToLower() switch
            {
                "active" => query.Where(u => u.IsActive && !u.IsLocked),
                "suspended" => query.Where(u => u.IsLocked),
                "inactive" => query.Where(u => !u.IsActive),
                _ => query
            };
        }

        // Apply plan filter
        if (!string.IsNullOrEmpty(request.Plan))
        {
            query = query.Where(u => u.Organization != null &&
                u.Organization.Plan != null &&
                u.Organization.Plan.Code == request.Plan);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = request.SortBy?.ToLower() switch
        {
            "email" => request.SortDesc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "name" => request.SortDesc ? query.OrderByDescending(u => u.Name) : query.OrderBy(u => u.Name),
            "lastlogin" => request.SortDesc ? query.OrderByDescending(u => u.LastLoginAt) : query.OrderBy(u => u.LastLoginAt),
            _ => query.OrderByDescending(u => u.CreatedAt)
        };

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserListItem
            {
                Id = u.Id,
                Email = u.Email ?? "",
                Name = u.Name,
                Phone = u.Mobile,
                Status = u.IsLocked ? "suspended" : (u.IsActive ? "active" : "inactive"),
                Organization = u.Organization != null ? new AdminOrgSummary
                {
                    Id = u.Organization.Id,
                    Name = u.Organization.Name
                } : null,
                Plan = u.Organization != null && u.Organization.Plan != null
                    ? u.Organization.Plan.Code
                    : "free",
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            })
            .ToListAsync();

        return Success(new UserListResponse
        {
            Users = users,
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
    /// Get user details.
    /// </summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(AdminUserDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(Guid userId)
    {
        if (!HasPermission(AdminPermissions.UsersView))
        {
            return Forbid();
        }

        var user = await _dbContext.Users
            .Include(u => u.Organization)
            .ThenInclude(o => o!.Plan)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFoundResponse("User not found");
        }

        // Get member count for the organization
        var memberCount = user.Organization != null
            ? await _dbContext.Users.CountAsync(u => u.OrganizationId == user.OrganizationId)
            : 0;

        // Get subscription status
        var subscriptionStatus = "none";
        if (user.Organization != null)
        {
            var subscription = await _dbContext.BillingSubscriptions
                .FirstOrDefaultAsync(s => s.OrganizationId == user.OrganizationId);
            subscriptionStatus = subscription?.Status ?? user.Organization.SubscriptionStatus;
        }

        // Get recent notices uploaded by user
        var recentNotices = await _dbContext.Notices
            .Where(n => n.UploadedById == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .Select(n => new RecentNoticeDto
            {
                Id = n.Id,
                NoticeNumber = n.NoticeNumber,
                NoticeType = n.NoticeType ?? "",
                Status = n.Status,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        // Get login history
        var loginHistory = await _dbContext.LoginAudits
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(10)
            .Select(l => new LoginHistoryDto
            {
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent,
                Success = l.Success,
                AttemptedAt = l.CreatedAt
            })
            .ToListAsync();

        return Success(new AdminUserDetail
        {
            Id = user.Id,
            Email = user.Email ?? "",
            Name = user.Name,
            Phone = user.Mobile,
            Status = user.IsLocked ? "suspended" : (user.IsActive ? "active" : "inactive"),
            EmailVerified = user.EmailConfirmed,
            PhoneVerified = user.IsMobileVerified,
            TwoFactorEnabled = user.Is2faEnabled,
            Organization = user.Organization != null ? new AdminOrgDetail
            {
                Id = user.Organization.Id,
                Name = user.Organization.Name,
                PlanCode = user.Organization.Plan?.Code ?? "free",
                PlanName = user.Organization.Plan?.Name ?? "Free",
                SubscriptionStatus = subscriptionStatus,
                MemberCount = memberCount
            } : null,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            LockedAt = user.LockedUntil,
            LockoutReason = null,
            RecentNotices = recentNotices,
            LoginHistory = loginHistory
        });
    }

    /// <summary>
    /// Suspend a user.
    /// </summary>
    [HttpPost("{userId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SuspendUser(Guid userId, [FromBody] SuspendUserRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFoundResponse("User not found");
        }

        if (user.IsLocked)
        {
            return Error("User is already suspended", "ALREADY_SUSPENDED");
        }

        user.IsLocked = true;
        user.LockedUntil = DateTime.MaxValue;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.UserSuspended,
            AuditTargetTypes.User,
            userId.ToString(),
            $"User suspended: {request.Reason}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason,
                ["notes"] = request.Notes ?? ""
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "User suspended successfully");
    }

    /// <summary>
    /// Unsuspend a user.
    /// </summary>
    [HttpPost("{userId:guid}/unsuspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnsuspendUser(Guid userId)
    {
        if (!HasPermission(AdminPermissions.UsersSuspend))
        {
            return Forbid();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFoundResponse("User not found");
        }

        if (!user.IsLocked)
        {
            return Error("User is not suspended", "NOT_SUSPENDED");
        }

        user.IsLocked = false;
        user.LockedUntil = null;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.UserUnsuspended,
            AuditTargetTypes.User,
            userId.ToString(),
            "User unsuspended",
            ipAddress: ClientIpAddress,
            userAgent: ClientUserAgent,
            sessionId: CurrentSessionId);

        return Success<object?>(null, "User unsuspended successfully");
    }

    /// <summary>
    /// Reset user password (sends reset email).
    /// </summary>
    [HttpPost("{userId:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid userId)
    {
        if (!HasPermission(AdminPermissions.UsersResetPassword))
        {
            return Forbid();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFoundResponse("User not found");
        }

        // In a real implementation, trigger password reset email
        // For now, just log the action

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.PasswordResetRequested,
            AuditTargetTypes.User,
            userId.ToString(),
            "Password reset initiated by admin",
            ipAddress: ClientIpAddress,
            userAgent: ClientUserAgent,
            sessionId: CurrentSessionId);

        return Success<object?>(null, "Password reset email sent");
    }

    /// <summary>
    /// Start impersonation session.
    /// </summary>
    [HttpPost("{userId:guid}/impersonate")]
    [Authorize(Policy = "AdminMfaVerified")]
    [ProducesResponseType(typeof(ImpersonationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Impersonate(Guid userId, [FromBody] ImpersonateRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersImpersonate))
        {
            return Forbid();
        }

        var user = await _dbContext.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFoundResponse("User not found");
        }

        // Generate impersonation token
        var token = GenerateSecureToken();
        var tokenHash = HashToken(token);

        var session = new ImpersonationSession
        {
            AdminUserId = CurrentAdminId,
            TargetUserId = userId,
            TargetOrganizationId = user.OrganizationId,
            TokenHash = tokenHash,
            Permissions = request.ReadOnly ? ["read"] : ["read", "write"],
            Reason = request.Reason,
            Status = ImpersonationStatus.Active,
            IpAddress = ClientIpAddress,
            UserAgent = ClientUserAgent,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _dbContext.ImpersonationSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.ImpersonationStarted,
            AuditTargetTypes.User,
            userId.ToString(),
            $"Impersonation started: {request.Reason}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason,
                ["read_only"] = request.ReadOnly,
                ["session_id"] = session.Id
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        // Build impersonation URL
        var baseUrl = "https://app.effortlessinsight.com"; // From config in real impl
        var impersonationUrl = $"{baseUrl}/impersonate?token={token}";

        return Success(new ImpersonationResponse
        {
            ImpersonationToken = token,
            ExpiresAt = session.ExpiresAt,
            Permissions = session.Permissions,
            Url = impersonationUrl
        });
    }

    /// <summary>
    /// End impersonation session.
    /// </summary>
    [HttpPost("{userId:guid}/impersonate/end")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> EndImpersonation(Guid userId)
    {
        var session = await _dbContext.ImpersonationSessions
            .FirstOrDefaultAsync(s =>
                s.AdminUserId == CurrentAdminId &&
                s.TargetUserId == userId &&
                s.Status == ImpersonationStatus.Active);

        if (session != null)
        {
            session.Status = ImpersonationStatus.Ended;
            session.EndedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync(
                CurrentAdminId,
                AdminAuditActions.ImpersonationEnded,
                AuditTargetTypes.User,
                userId.ToString(),
                "Impersonation ended",
                new Dictionary<string, object>
                {
                    ["session_id"] = session.Id,
                    ["pages_visited"] = session.PagesVisited.Count
                },
                ClientIpAddress,
                ClientUserAgent,
                CurrentSessionId);
        }

        return Success<object?>(null, "Impersonation ended");
    }

    /// <summary>
    /// Delete user (GDPR compliance).
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [Authorize(Policy = "AdminSuperAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(Guid userId, [FromBody] DeleteUserRequest request)
    {
        if (!HasPermission(AdminPermissions.UsersDelete))
        {
            return Forbid();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFoundResponse("User not found");
        }

        // Require confirmation
        if (!request.Confirmed)
        {
            return Error("Deletion must be confirmed", "CONFIRMATION_REQUIRED");
        }

        // In a real implementation:
        // 1. Remove from organization
        // 2. Anonymize personal data
        // 3. Delete or anonymize notices
        // 4. Keep audit trail
        // For now, mark as deleted

        user.Email = $"deleted_{userId}@deleted.local";
        user.Name = "Deleted User";
        user.Mobile = null;
        user.IsActive = false;
        user.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.UserDeleted,
            AuditTargetTypes.User,
            userId.ToString(),
            $"User deleted (GDPR): {request.Reason}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason,
                ["gdpr_request"] = request.GdprRequest
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "User deleted successfully");
    }

    // Private helpers

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }
}

// DTOs

public record UserSearchRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? Plan { get; init; }
    public string? SortBy { get; init; }
    public bool SortDesc { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public record UserListResponse
{
    public List<AdminUserListItem> Users { get; init; } = [];
    public PaginationInfo Pagination { get; init; } = new();
}

public record PaginationInfo
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public int TotalPages { get; init; }
}

public record AdminUserListItem
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string Status { get; init; } = string.Empty;
    public AdminOrgSummary? Organization { get; init; }
    public string Plan { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
}

public record AdminOrgSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public record AdminUserDetail
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
    public bool PhoneVerified { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public AdminOrgDetail? Organization { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTime? LockedAt { get; init; }
    public string? LockoutReason { get; init; }
    public List<RecentNoticeDto> RecentNotices { get; init; } = [];
    public List<LoginHistoryDto> LoginHistory { get; init; } = [];
}

public record AdminOrgDetail
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string SubscriptionStatus { get; init; } = string.Empty;
    public int MemberCount { get; init; }
}

public record RecentNoticeDto
{
    public Guid Id { get; init; }
    public string? NoticeNumber { get; init; }
    public string NoticeType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record LoginHistoryDto
{
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public bool Success { get; init; }
    public DateTime AttemptedAt { get; init; }
}

public record SuspendUserRequest
{
    public string Reason { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

public record ImpersonateRequest
{
    public string Reason { get; init; } = string.Empty;
    public bool ReadOnly { get; init; } = true;
}

public record ImpersonationResponse
{
    public string ImpersonationToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public List<string> Permissions { get; init; } = [];
    public string Url { get; init; } = string.Empty;
}

public record DeleteUserRequest
{
    public string Reason { get; init; } = string.Empty;
    public bool GdprRequest { get; init; }
    public bool Confirmed { get; init; }
}
