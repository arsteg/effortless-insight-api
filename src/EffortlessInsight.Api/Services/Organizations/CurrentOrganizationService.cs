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

    // NOTE ON KEEPING THIS IN SYNC WITH Permissions.GetDefaultPermissionsForRole:
    // There are two parallel, independently-maintained authorization systems in
    // this codebase - this hardcoded claims-driven switch (what NoticesController
    // and most other controllers actually call), and the DB-driven
    // Permissions.GetDefaultPermissionsForRole/CustomRole.Permissions system
    // (consulted by RoleService for custom-role assignment/checks). They use
    // different permission-name vocabularies (this one has gstins.*/settings.*/
    // organization.* entries that Permissions.All does not) and can silently
    // disagree - e.g. a "ca" member granted a broader CustomRole would still be
    // blocked here, since this method never loads CustomRole.Permissions. Full
    // unification (making this delegate to RoleService) is a larger refactor
    // than any single feature justifies; until then, any change to what the
    // "ca" role can do must be applied in BOTH places by hand.
    public bool HasPermission(string permission)
    {
        var role = Role?.ToLowerInvariant();

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
            // CA can view the team (needed to work alongside a BO's members) but
            // never invite/remove/change roles - the BO remains the owner of
            // their organization's membership and governance.
            "members.view" => true,
            "members.invite" => role is "owner" or "admin",
            "members.remove" => role is "owner" or "admin",
            "members.change_role" => role is "owner" or "admin",

            // GSTIN permissions
            // Deliberately owner/admin-only for add/remove even for CA: the BO
            // owns their GSTIN registrations, not the CA managing their notices.
            "gstins.view" => true,
            "gstins.add" => role is "owner" or "admin",
            "gstins.remove" => role is "owner" or "admin",

            // Notice permissions
            "notices.view" => true,
            "notices.view_all" => role is not "viewer" || !IsExternal,
            "notices.upload" => role is not "viewer",
            "notices.edit" => role is not "viewer",
            "notices.delete" => role is "owner" or "admin",
            // Deliberately left owner/admin/manager-only (not opened to "ca"):
            // assignment and approval are BO-governance concerns, not something
            // the CA-as-distributor requirements ask a CA to do.
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
            // CA can view organization settings (needed context to work the
            // client's notices) but never edit them - editing remains
            // owner/admin-only, consistent with "BO owns their organization."
            "settings.view" => true,
            "settings.edit" => role is "owner" or "admin",

            // Task permissions
            "tasks.view" => true,
            "tasks.create" => role is not "viewer",
            "tasks.edit" => role is not "viewer",
            "tasks.delete" => role is not "viewer",

            // Workflow permissions
            // Deliberately left owner/admin/manager-only: workflow administration
            // is a BO-governance concern, same rationale as notices.assign above.
            "workflow.view" => true,
            "workflow.transition" => role is not "viewer",
            "workflow.admin" => role is "owner" or "admin" or "manager",

            _ => false
        };
    }
}
