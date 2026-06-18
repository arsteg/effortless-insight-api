using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Provides organization context for the current request.
/// Used for multi-tenancy to determine which organization the user is operating in.
/// </summary>
public interface ICurrentOrganizationService
{
    /// <summary>
    /// Current organization ID from JWT claims
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// Current user ID from JWT claims
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// User's role in the current organization
    /// </summary>
    string? Role { get; }

    /// <summary>
    /// Whether the user is an external collaborator (e.g., CA)
    /// </summary>
    bool IsExternal { get; }

    /// <summary>
    /// Whether the user is the organization owner
    /// </summary>
    bool IsOwner { get; }

    /// <summary>
    /// Whether the user is an admin or owner
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>
    /// Whether the user can manage members (owner or admin)
    /// </summary>
    bool CanManageMembers { get; }

    /// <summary>
    /// Whether the user can manage organization settings (owner or admin)
    /// </summary>
    bool CanManageSettings { get; }

    /// <summary>
    /// Whether the user can manage billing (owner only)
    /// </summary>
    bool CanManageBilling { get; }

    /// <summary>
    /// Whether the user can view audit logs (owner or admin)
    /// </summary>
    bool CanViewAuditLogs { get; }

    /// <summary>
    /// Gets the current membership record from database
    /// </summary>
    Task<OrganizationMember?> GetCurrentMembershipAsync();

    /// <summary>
    /// Validates that the user is a member of the specified organization
    /// </summary>
    Task<bool> ValidateMembershipAsync(Guid organizationId);

    /// <summary>
    /// Gets all organizations the current user belongs to
    /// </summary>
    Task<List<OrganizationMember>> GetUserMembershipsAsync();

    /// <summary>
    /// Checks if the current user has a specific permission
    /// </summary>
    bool HasPermission(string permission);
}

public class CurrentOrganizationService : ICurrentOrganizationService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _dbContext;

    public CurrentOrganizationService(
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext dbContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
    }

    public Guid? OrganizationId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("org_id");
            return claim != null && Guid.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    public Guid? UserId
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)
                ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub");
            return claim != null && Guid.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    public string? Role
    {
        get
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirst("role")?.Value;
        }
    }

    public bool IsExternal
    {
        get
        {
            var claim = _httpContextAccessor.HttpContext?.User?.FindFirst("is_external");
            return claim?.Value?.ToLowerInvariant() == "true";
        }
    }

    public bool IsOwner => Role?.ToLowerInvariant() == "owner";

    public bool IsAdmin => Role?.ToLowerInvariant() is "owner" or "admin";

    public bool CanManageMembers => IsAdmin;

    public bool CanManageSettings => IsAdmin;

    public bool CanManageBilling => IsOwner;

    public bool CanViewAuditLogs => IsAdmin;

    public async Task<OrganizationMember?> GetCurrentMembershipAsync()
    {
        if (!UserId.HasValue || !OrganizationId.HasValue)
            return null;

        return await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m =>
                m.UserId == UserId.Value &&
                m.OrganizationId == OrganizationId.Value &&
                m.Status == "active");
    }

    public async Task<bool> ValidateMembershipAsync(Guid organizationId)
    {
        if (!UserId.HasValue)
            return false;

        return await _dbContext.OrganizationMembers
            .AnyAsync(m =>
                m.UserId == UserId.Value &&
                m.OrganizationId == organizationId &&
                m.Status == "active" &&
                (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow));
    }

    public async Task<List<OrganizationMember>> GetUserMembershipsAsync()
    {
        if (!UserId.HasValue)
            return [];

        return await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .Where(m =>
                m.UserId == UserId.Value &&
                m.Status == "active" &&
                m.Organization.DeletedAt == null &&
                (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow))
            .ToListAsync();
    }

    public bool HasPermission(string permission)
    {
        var role = Role?.ToLowerInvariant();

        // Debug logging - remove after fixing
        System.Diagnostics.Debug.WriteLine($"HasPermission check: permission={permission}, role={role ?? "NULL"}, orgId={OrganizationId}");
        Console.WriteLine($"HasPermission check: permission={permission}, role={role ?? "NULL"}, orgId={OrganizationId}");

        if (role == null) return false;

        // Define permission mappings based on role
        // Note: Use dots (.) as separator to match controller usage
        return permission.ToLowerInvariant() switch
        {
            // Organization permissions
            "organization.view" => true, // All members can view
            "organization.edit" => role is "owner" or "admin",
            "organization.delete" => role == "owner",
            "organization.billing" => role == "owner",
            "organization.transfer" => role == "owner",

            // Member permissions
            "members.view" => role is not "ca", // CA cannot see members
            "members.invite" => role is "owner" or "admin",
            "members.remove" => role is "owner" or "admin",
            "members.change_role" => role is "owner" or "admin",

            // GSTIN permissions
            "gstins.view" => true,
            "gstins.add" => role is "owner" or "admin",
            "gstins.remove" => role is "owner" or "admin",

            // Notice permissions
            "notices.view" => true,
            "notices.view_all" => role is not "viewer" || !IsExternal,
            "notices.upload" => role is not "viewer",
            "notices.edit" => role is not "viewer",
            "notices.delete" => role is "owner" or "admin",
            "notices.assign" => role is "owner" or "admin" or "manager",
            "notices.comment" => role is not "viewer",
            "notices.draft_response" => role is not "viewer",
            "notices.approve" => role is "owner" or "admin" or "manager",
            "notices.approve_response" => role is "owner" or "admin" or "manager",

            // Reports permissions
            "reports.view" => true,
            "reports.export" => role is "owner" or "admin" or "manager",
            "audit.view" => role is "owner" or "admin",

            // Settings permissions
            "settings.view" => role is not "ca" || !IsExternal,
            "settings.edit" => role is "owner" or "admin",

            _ => false
        };
    }
}
