using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Service interface for managing custom roles within organizations.
/// </summary>
public interface IRoleService
{
    /// <summary>
    /// Creates a new custom role in an organization.
    /// </summary>
    Task<CustomRole> CreateRoleAsync(Guid organizationId, CreateRoleRequest request, Guid userId);

    /// <summary>
    /// Gets all roles for an organization (including system roles).
    /// </summary>
    Task<List<CustomRole>> GetRolesAsync(Guid organizationId);

    /// <summary>
    /// Gets a specific role by ID.
    /// </summary>
    Task<CustomRole?> GetRoleByIdAsync(Guid organizationId, Guid roleId);

    /// <summary>
    /// Updates a custom role.
    /// </summary>
    Task<CustomRole> UpdateRoleAsync(Guid organizationId, Guid roleId, UpdateRoleRequest request, Guid userId);

    /// <summary>
    /// Deletes a custom role.
    /// </summary>
    Task DeleteRoleAsync(Guid organizationId, Guid roleId, Guid userId);

    /// <summary>
    /// Assigns a custom role to an organization member.
    /// </summary>
    Task AssignRoleToMemberAsync(Guid organizationId, Guid memberId, Guid roleId, Guid userId);

    /// <summary>
    /// Removes custom role from an organization member (reverts to base role).
    /// </summary>
    Task RemoveRoleFromMemberAsync(Guid organizationId, Guid memberId, Guid userId);

    /// <summary>
    /// Gets all permissions for a user in an organization.
    /// Considers both base role and custom role permissions.
    /// </summary>
    Task<List<string>> GetPermissionsForUserAsync(Guid organizationId, Guid userId);

    /// <summary>
    /// Checks if a user has a specific permission.
    /// </summary>
    Task<bool> HasPermissionAsync(Guid organizationId, Guid userId, string permission);

    /// <summary>
    /// Gets all available permissions in the system.
    /// </summary>
    List<PermissionCategory> GetAvailablePermissions();

    /// <summary>
    /// Creates default system roles for a new organization.
    /// </summary>
    Task CreateDefaultRolesAsync(Guid organizationId);
}

/// <summary>
/// Request model for creating a custom role.
/// </summary>
public record CreateRoleRequest(
    string Name,
    string? Description,
    string? BaseRole,
    List<string> Permissions,
    string? Color
);

/// <summary>
/// Request model for updating a custom role.
/// </summary>
public record UpdateRoleRequest(
    string? Name,
    string? Description,
    List<string>? Permissions,
    string? Color,
    bool? IsActive
);

/// <summary>
/// Permission category for UI grouping.
/// </summary>
public record PermissionCategory(
    string Name,
    List<PermissionInfo> Permissions
);

/// <summary>
/// Individual permission info.
/// </summary>
public record PermissionInfo(
    string Key,
    string Name,
    string Description
);

/// <summary>
/// Implementation of the role management service.
/// </summary>
public class RoleService : IRoleService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly IAuditService _auditService;
    private readonly ILogger<RoleService> _logger;

    public RoleService(
        ApplicationDbContext dbContext,
        ICurrentOrganizationService currentOrg,
        IAuditService auditService,
        ILogger<RoleService> logger)
    {
        _dbContext = dbContext;
        _currentOrg = currentOrg;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<CustomRole> CreateRoleAsync(Guid organizationId, CreateRoleRequest request, Guid userId)
    {
        // Validate organization access
        await ValidateAdminAccessAsync(organizationId, userId);

        var normalizedName = request.Name.Trim().ToLowerInvariant();

        // Check for duplicate name
        if (await _dbContext.CustomRoles.AnyAsync(r =>
            r.OrganizationId == organizationId &&
            r.NameNormalized == normalizedName &&
            r.DeletedAt == null))
        {
            throw new InvalidOperationException("ROLE_NAME_EXISTS");
        }

        // Validate base role if provided
        if (!string.IsNullOrEmpty(request.BaseRole))
        {
            var validBaseRoles = new[] { "owner", "admin", "manager", "member", "ca", "viewer" };
            if (!validBaseRoles.Contains(request.BaseRole.ToLowerInvariant()))
            {
                throw new InvalidOperationException("INVALID_BASE_ROLE");
            }
        }

        // Validate permissions
        var invalidPermissions = request.Permissions.Except(Permissions.All).ToList();
        if (invalidPermissions.Count > 0)
        {
            throw new InvalidOperationException($"INVALID_PERMISSIONS: {string.Join(", ", invalidPermissions)}");
        }

        // Get max display order
        var maxOrder = await _dbContext.CustomRoles
            .Where(r => r.OrganizationId == organizationId && r.DeletedAt == null)
            .MaxAsync(r => (int?)r.DisplayOrder) ?? 0;

        var role = new CustomRole
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            NameNormalized = normalizedName,
            Description = request.Description?.Trim(),
            BaseRole = request.BaseRole?.ToLowerInvariant(),
            Permissions = request.Permissions,
            Color = request.Color,
            DisplayOrder = maxOrder + 1,
            IsSystem = false,
            IsActive = true
        };

        _dbContext.CustomRoles.Add(role);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Custom role {RoleName} created in organization {OrganizationId} by user {UserId}",
            role.Name, organizationId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "role.created",
            EntityType = "CustomRole",
            EntityId = role.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new { role.Name, role.BaseRole, role.Permissions }
        });

        return role;
    }

    public async Task<List<CustomRole>> GetRolesAsync(Guid organizationId)
    {
        return await _dbContext.CustomRoles
            .Where(r => r.OrganizationId == organizationId && r.DeletedAt == null)
            .OrderBy(r => r.IsSystem ? 0 : 1)
            .ThenBy(r => r.DisplayOrder)
            .ToListAsync();
    }

    public async Task<CustomRole?> GetRoleByIdAsync(Guid organizationId, Guid roleId)
    {
        return await _dbContext.CustomRoles
            .FirstOrDefaultAsync(r =>
                r.Id == roleId &&
                r.OrganizationId == organizationId &&
                r.DeletedAt == null);
    }

    public async Task<CustomRole> UpdateRoleAsync(Guid organizationId, Guid roleId, UpdateRoleRequest request, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var role = await _dbContext.CustomRoles
            .FirstOrDefaultAsync(r =>
                r.Id == roleId &&
                r.OrganizationId == organizationId &&
                r.DeletedAt == null)
            ?? throw new KeyNotFoundException("ROLE_NOT_FOUND");

        if (role.IsSystem)
        {
            throw new InvalidOperationException("CANNOT_MODIFY_SYSTEM_ROLE");
        }

        // Check for duplicate name if changing
        if (!string.IsNullOrEmpty(request.Name))
        {
            var normalizedName = request.Name.Trim().ToLowerInvariant();
            if (normalizedName != role.NameNormalized &&
                await _dbContext.CustomRoles.AnyAsync(r =>
                    r.OrganizationId == organizationId &&
                    r.NameNormalized == normalizedName &&
                    r.Id != roleId &&
                    r.DeletedAt == null))
            {
                throw new InvalidOperationException("ROLE_NAME_EXISTS");
            }
            role.Name = request.Name.Trim();
            role.NameNormalized = normalizedName;
        }

        if (request.Description != null)
            role.Description = request.Description.Trim();

        if (request.Permissions != null)
        {
            var invalidPermissions = request.Permissions.Except(Permissions.All).ToList();
            if (invalidPermissions.Count > 0)
            {
                throw new InvalidOperationException($"INVALID_PERMISSIONS: {string.Join(", ", invalidPermissions)}");
            }
            role.Permissions = request.Permissions;
        }

        if (request.Color != null)
            role.Color = request.Color;

        if (request.IsActive.HasValue)
            role.IsActive = request.IsActive.Value;

        role.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Custom role {RoleId} updated in organization {OrganizationId} by user {UserId}",
            roleId, organizationId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "role.updated",
            EntityType = "CustomRole",
            EntityId = roleId,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new { role.Name, role.Permissions, role.IsActive }
        });

        return role;
    }

    public async Task DeleteRoleAsync(Guid organizationId, Guid roleId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var role = await _dbContext.CustomRoles
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r =>
                r.Id == roleId &&
                r.OrganizationId == organizationId &&
                r.DeletedAt == null)
            ?? throw new KeyNotFoundException("ROLE_NOT_FOUND");

        if (role.IsSystem)
        {
            throw new InvalidOperationException("CANNOT_DELETE_SYSTEM_ROLE");
        }

        if (role.Members.Any())
        {
            throw new InvalidOperationException("ROLE_HAS_MEMBERS");
        }

        role.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Custom role {RoleId} deleted in organization {OrganizationId} by user {UserId}",
            roleId, organizationId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "role.deleted",
            EntityType = "CustomRole",
            EntityId = roleId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { role.Name }
        });
    }

    public async Task AssignRoleToMemberAsync(Guid organizationId, Guid memberId, Guid roleId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var member = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m =>
                m.Id == memberId &&
                m.OrganizationId == organizationId &&
                m.Status == "active")
            ?? throw new KeyNotFoundException("MEMBER_NOT_FOUND");

        var role = await _dbContext.CustomRoles
            .FirstOrDefaultAsync(r =>
                r.Id == roleId &&
                r.OrganizationId == organizationId &&
                r.IsActive &&
                r.DeletedAt == null)
            ?? throw new KeyNotFoundException("ROLE_NOT_FOUND");

        var previousRoleId = member.CustomRoleId;
        member.CustomRoleId = roleId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Custom role {RoleId} assigned to member {MemberId} in organization {OrganizationId}",
            roleId, memberId, organizationId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "role.assigned",
            EntityType = "OrganizationMember",
            EntityId = memberId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { CustomRoleId = previousRoleId },
            NewValues = new { CustomRoleId = roleId, RoleName = role.Name }
        });
    }

    public async Task RemoveRoleFromMemberAsync(Guid organizationId, Guid memberId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var member = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m =>
                m.Id == memberId &&
                m.OrganizationId == organizationId &&
                m.Status == "active")
            ?? throw new KeyNotFoundException("MEMBER_NOT_FOUND");

        var previousRoleId = member.CustomRoleId;
        member.CustomRoleId = null;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Custom role removed from member {MemberId} in organization {OrganizationId}",
            memberId, organizationId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "role.removed",
            EntityType = "OrganizationMember",
            EntityId = memberId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { CustomRoleId = previousRoleId }
        });
    }

    public async Task<List<string>> GetPermissionsForUserAsync(Guid organizationId, Guid userId)
    {
        var member = await _dbContext.OrganizationMembers
            .Include(m => m.CustomRole)
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.Status == "active");

        if (member == null)
            return [];

        // If member has a custom role, use its permissions
        if (member.CustomRole != null && member.CustomRole.IsActive)
        {
            var permissions = new HashSet<string>(member.CustomRole.Permissions);

            // If custom role has a base role, merge base role permissions
            if (!string.IsNullOrEmpty(member.CustomRole.BaseRole))
            {
                var basePermissions = Permissions.GetDefaultPermissionsForRole(member.CustomRole.BaseRole);
                foreach (var perm in basePermissions)
                {
                    permissions.Add(perm);
                }
            }

            return [.. permissions];
        }

        // Otherwise, use default permissions for base role
        return Permissions.GetDefaultPermissionsForRole(member.Role);
    }

    public async Task<bool> HasPermissionAsync(Guid organizationId, Guid userId, string permission)
    {
        var permissions = await GetPermissionsForUserAsync(organizationId, userId);
        return permissions.Contains(permission);
    }

    public List<PermissionCategory> GetAvailablePermissions()
    {
        return Permissions.Categories.Select(kv => new PermissionCategory(
            kv.Key,
            kv.Value.Select(p => new PermissionInfo(
                p,
                FormatPermissionName(p),
                GetPermissionDescription(p)
            )).ToList()
        )).ToList();
    }

    public async Task CreateDefaultRolesAsync(Guid organizationId)
    {
        var systemRoles = new[]
        {
            ("Owner", "owner", "Full access to all organization features", "#dc2626", 1),
            ("Admin", "admin", "Administrative access except billing and ownership", "#ea580c", 2),
            ("Manager", "manager", "Manage notices, tasks, and team workflow", "#ca8a04", 3),
            ("Member", "member", "Standard team member access", "#16a34a", 4),
            ("CA", "ca", "External chartered accountant access", "#2563eb", 5),
            ("Viewer", "viewer", "Read-only access", "#6b7280", 6)
        };

        foreach (var (name, baseRole, description, color, order) in systemRoles)
        {
            var role = new CustomRole
            {
                OrganizationId = organizationId,
                Name = name,
                NameNormalized = name.ToLowerInvariant(),
                Description = description,
                BaseRole = baseRole,
                Permissions = Permissions.GetDefaultPermissionsForRole(baseRole),
                Color = color,
                DisplayOrder = order,
                IsSystem = true,
                IsActive = true
            };

            _dbContext.CustomRoles.Add(role);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Default system roles created for organization {OrganizationId}", organizationId);
    }

    private async Task ValidateAdminAccessAsync(Guid organizationId, Guid userId)
    {
        var member = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.Status == "active" &&
                (m.Role == "owner" || m.Role == "admin"));

        if (member == null)
            throw new UnauthorizedAccessException("ADMIN_REQUIRED");
    }

    private static string FormatPermissionName(string permission)
    {
        // Convert "notices.view_all" to "View All"
        var parts = permission.Split('.');
        var action = parts.Length > 1 ? parts[1] : parts[0];
        return string.Join(" ", action.Split('_').Select(s =>
            char.ToUpper(s[0]) + s[1..]));
    }

    private static string GetPermissionDescription(string permission)
    {
        return permission switch
        {
            Permissions.NoticesView => "View notices in the organization",
            Permissions.NoticesViewAll => "View all notices regardless of assignment",
            Permissions.NoticesCreate => "Upload and create new notices",
            Permissions.NoticesEdit => "Edit existing notices",
            Permissions.NoticesDelete => "Delete notices",
            Permissions.NoticesAssign => "Assign notices to team members",
            Permissions.NoticesComment => "Add comments to notices",
            Permissions.NoticesDraftResponse => "Draft responses to notices",
            Permissions.NoticesApproveResponse => "Approve notice responses",
            Permissions.NoticesExport => "Export notices and reports",

            Permissions.TasksView => "View tasks",
            Permissions.TasksCreate => "Create new tasks",
            Permissions.TasksEdit => "Edit existing tasks",
            Permissions.TasksDelete => "Delete tasks",
            Permissions.TasksAssign => "Assign tasks to team members",
            Permissions.TasksComplete => "Mark tasks as complete",

            Permissions.WorkflowView => "View workflow status",
            Permissions.WorkflowTransition => "Move notices through workflow stages",
            Permissions.WorkflowAdmin => "Configure workflow templates",
            Permissions.WorkflowApprove => "Approve workflow transitions",

            Permissions.OrgMembersView => "View organization members",
            Permissions.OrgMembersInvite => "Invite new members",
            Permissions.OrgMembersManage => "Manage member settings",
            Permissions.OrgMembersRemove => "Remove members from organization",
            Permissions.OrgMembersChangeRole => "Change member roles",

            Permissions.OrgSettingsView => "View organization settings",
            Permissions.OrgSettingsEdit => "Edit organization settings",
            Permissions.OrgGstinsManage => "Manage organization GSTINs",
            Permissions.OrgDelete => "Delete the organization",
            Permissions.OrgTransferOwnership => "Transfer organization ownership",

            Permissions.BillingView => "View billing information",
            Permissions.BillingManage => "Manage billing and subscriptions",
            Permissions.BillingInvoices => "View and download invoices",

            Permissions.ReportsView => "View reports and analytics",
            Permissions.ReportsExport => "Export reports",
            Permissions.AuditView => "View audit logs",
            Permissions.AnalyticsView => "View analytics dashboards",

            Permissions.TeamsView => "View teams and departments",
            Permissions.TeamsCreate => "Create new teams",
            Permissions.TeamsEdit => "Edit team settings",
            Permissions.TeamsDelete => "Delete teams",
            Permissions.TeamsManageMembers => "Manage team members",

            Permissions.RolesView => "View custom roles",
            Permissions.RolesCreate => "Create custom roles",
            Permissions.RolesEdit => "Edit custom roles",
            Permissions.RolesDelete => "Delete custom roles",

            Permissions.DocumentRequestsView => "View document requests",
            Permissions.DocumentRequestsCreate => "Create document requests",
            Permissions.DocumentRequestsManage => "Manage document requests",

            _ => "Access to this feature"
        };
    }
}
