using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Service interface for managing teams/departments within organizations.
/// </summary>
public interface ITeamService
{
    /// <summary>
    /// Creates a new team in an organization.
    /// </summary>
    Task<Team> CreateTeamAsync(Guid organizationId, CreateTeamRequest request, Guid userId);

    /// <summary>
    /// Gets all teams for an organization.
    /// </summary>
    Task<List<TeamDto>> GetTeamsAsync(Guid organizationId, bool includeMembers = false);

    /// <summary>
    /// Gets a specific team by ID with full details.
    /// </summary>
    Task<TeamDetailDto?> GetTeamByIdAsync(Guid organizationId, Guid teamId);

    /// <summary>
    /// Updates a team.
    /// </summary>
    Task<Team> UpdateTeamAsync(Guid organizationId, Guid teamId, UpdateTeamRequest request, Guid userId);

    /// <summary>
    /// Deletes a team.
    /// </summary>
    Task DeleteTeamAsync(Guid organizationId, Guid teamId, Guid userId);

    /// <summary>
    /// Gets the team hierarchy as a tree structure.
    /// </summary>
    Task<List<TeamTreeNode>> GetTeamHierarchyAsync(Guid organizationId);

    /// <summary>
    /// Adds a member to a team.
    /// </summary>
    Task<TeamMember> AddTeamMemberAsync(Guid organizationId, Guid teamId, AddTeamMemberRequest request, Guid userId);

    /// <summary>
    /// Updates a team member's role or settings.
    /// </summary>
    Task<TeamMember> UpdateTeamMemberAsync(Guid organizationId, Guid teamId, Guid memberId, UpdateTeamMemberRequest request, Guid userId);

    /// <summary>
    /// Removes a member from a team.
    /// </summary>
    Task RemoveTeamMemberAsync(Guid organizationId, Guid teamId, Guid memberId, Guid userId);

    /// <summary>
    /// Gets all teams a user belongs to.
    /// </summary>
    Task<List<TeamMembershipDto>> GetUserTeamsAsync(Guid organizationId, Guid userId);

    /// <summary>
    /// Sets a team as the primary team for a user.
    /// </summary>
    Task SetPrimaryTeamAsync(Guid organizationId, Guid teamId, Guid userId);
}

// Request/Response DTOs

public record CreateTeamRequest(
    string Name,
    string? Description,
    Guid? ParentTeamId,
    Guid? LeaderId,
    string? Color,
    string? Icon
);

public record UpdateTeamRequest(
    string? Name,
    string? Description,
    Guid? ParentTeamId,
    Guid? LeaderId,
    string? Color,
    string? Icon,
    bool? IsActive
);

public record AddTeamMemberRequest(
    Guid UserId,
    string Role = "member",
    string? Title = null,
    bool IsPrimary = false
);

public record UpdateTeamMemberRequest(
    string? Role,
    string? Title,
    bool? IsPrimary
);

public record TeamDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentTeamId,
    string? ParentTeamName,
    Guid? LeaderId,
    string? LeaderName,
    string? Color,
    string? Icon,
    int HierarchyLevel,
    int MemberCount,
    int SubTeamCount,
    bool IsActive,
    DateTime CreatedAt
);

public record TeamDetailDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentTeamId,
    string? ParentTeamName,
    Guid? LeaderId,
    string? LeaderName,
    string? LeaderEmail,
    string? Color,
    string? Icon,
    int HierarchyLevel,
    string? HierarchyPath,
    bool IsActive,
    DateTime CreatedAt,
    List<TeamMemberDto> Members,
    List<TeamDto> SubTeams
);

public record TeamMemberDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string? UserEmail,
    string? AvatarUrl,
    string Role,
    string? Title,
    bool IsPrimary,
    DateTime JoinedAt
);

public record TeamMembershipDto(
    Guid TeamId,
    string TeamName,
    string? TeamColor,
    string Role,
    string? Title,
    bool IsPrimary,
    Guid? LeaderId,
    int MemberCount
);

public record TeamTreeNode(
    Guid Id,
    string Name,
    string? Color,
    string? Icon,
    Guid? LeaderId,
    string? LeaderName,
    int MemberCount,
    List<TeamTreeNode> Children
);

/// <summary>
/// Implementation of the team management service.
/// </summary>
public class TeamService : ITeamService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly IAuditService _auditService;
    private readonly ILogger<TeamService> _logger;

    private const int MaxHierarchyDepth = 10;

    public TeamService(
        ApplicationDbContext dbContext,
        ICurrentOrganizationService currentOrg,
        IAuditService auditService,
        ILogger<TeamService> logger)
    {
        _dbContext = dbContext;
        _currentOrg = currentOrg;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<Team> CreateTeamAsync(Guid organizationId, CreateTeamRequest request, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var normalizedName = request.Name.Trim().ToLowerInvariant();

        // Check for duplicate name within same parent
        if (await _dbContext.Teams.AnyAsync(t =>
            t.OrganizationId == organizationId &&
            t.NameNormalized == normalizedName &&
            t.ParentTeamId == request.ParentTeamId &&
            t.DeletedAt == null))
        {
            throw new InvalidOperationException("TEAM_NAME_EXISTS");
        }

        // Validate parent team if provided
        int hierarchyLevel = 0;
        string? hierarchyPath = "/";
        if (request.ParentTeamId.HasValue)
        {
            var parentTeam = await _dbContext.Teams
                .FirstOrDefaultAsync(t =>
                    t.Id == request.ParentTeamId &&
                    t.OrganizationId == organizationId &&
                    t.DeletedAt == null)
                ?? throw new KeyNotFoundException("PARENT_TEAM_NOT_FOUND");

            if (parentTeam.HierarchyLevel >= MaxHierarchyDepth - 1)
            {
                throw new InvalidOperationException("MAX_HIERARCHY_DEPTH_EXCEEDED");
            }

            hierarchyLevel = parentTeam.HierarchyLevel + 1;
            hierarchyPath = $"{parentTeam.HierarchyPath}{parentTeam.Id}/";
        }

        // Validate leader if provided
        if (request.LeaderId.HasValue)
        {
            var leaderExists = await _dbContext.OrganizationMembers
                .AnyAsync(m =>
                    m.OrganizationId == organizationId &&
                    m.UserId == request.LeaderId &&
                    m.Status == "active");

            if (!leaderExists)
            {
                throw new KeyNotFoundException("LEADER_NOT_FOUND");
            }
        }

        var team = new Team
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            NameNormalized = normalizedName,
            Description = request.Description?.Trim(),
            ParentTeamId = request.ParentTeamId,
            LeaderId = request.LeaderId,
            Color = request.Color,
            Icon = request.Icon,
            HierarchyLevel = hierarchyLevel,
            HierarchyPath = hierarchyPath,
            IsActive = true
        };

        _dbContext.Teams.Add(team);
        await _dbContext.SaveChangesAsync();

        // Update hierarchy path to include new team ID
        team.HierarchyPath = $"{hierarchyPath}{team.Id}/";
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Team {TeamName} created in organization {OrganizationId} by user {UserId}",
            team.Name, organizationId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "team.created",
            EntityType = "Team",
            EntityId = team.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new { team.Name, team.ParentTeamId, team.LeaderId }
        });

        return team;
    }

    public async Task<List<TeamDto>> GetTeamsAsync(Guid organizationId, bool includeMembers = false)
    {
        var query = _dbContext.Teams
            .Include(t => t.Leader)
            .Include(t => t.ParentTeam)
            .Include(t => t.Members.Where(m => m.DeletedAt == null))
            .Include(t => t.SubTeams.Where(st => st.DeletedAt == null))
            .Where(t => t.OrganizationId == organizationId && t.DeletedAt == null);

        var teams = await query.OrderBy(t => t.HierarchyPath).ToListAsync();

        return teams.Select(t => new TeamDto(
            t.Id,
            t.Name,
            t.Description,
            t.ParentTeamId,
            t.ParentTeam?.Name,
            t.LeaderId,
            t.Leader?.Name,
            t.Color,
            t.Icon,
            t.HierarchyLevel,
            t.Members.Count,
            t.SubTeams.Count,
            t.IsActive,
            t.CreatedAt
        )).ToList();
    }

    public async Task<TeamDetailDto?> GetTeamByIdAsync(Guid organizationId, Guid teamId)
    {
        var team = await _dbContext.Teams
            .Include(t => t.Leader)
            .Include(t => t.ParentTeam)
            .Include(t => t.Members.Where(m => m.DeletedAt == null))
                .ThenInclude(m => m.User)
            .Include(t => t.SubTeams.Where(st => st.DeletedAt == null))
            .FirstOrDefaultAsync(t =>
                t.Id == teamId &&
                t.OrganizationId == organizationId &&
                t.DeletedAt == null);

        if (team == null)
            return null;

        return new TeamDetailDto(
            team.Id,
            team.Name,
            team.Description,
            team.ParentTeamId,
            team.ParentTeam?.Name,
            team.LeaderId,
            team.Leader?.Name,
            team.Leader?.Email,
            team.Color,
            team.Icon,
            team.HierarchyLevel,
            team.HierarchyPath,
            team.IsActive,
            team.CreatedAt,
            team.Members.Select(m => new TeamMemberDto(
                m.Id,
                m.UserId,
                m.User.Name,
                m.User.Email,
                m.User.AvatarUrl,
                m.Role,
                m.Title,
                m.IsPrimary,
                m.JoinedAt
            )).ToList(),
            team.SubTeams.Select(st => new TeamDto(
                st.Id,
                st.Name,
                st.Description,
                st.ParentTeamId,
                team.Name,
                st.LeaderId,
                null,
                st.Color,
                st.Icon,
                st.HierarchyLevel,
                0, 0, // Will need separate query for counts
                st.IsActive,
                st.CreatedAt
            )).ToList()
        );
    }

    public async Task<Team> UpdateTeamAsync(Guid organizationId, Guid teamId, UpdateTeamRequest request, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var team = await _dbContext.Teams
            .Include(t => t.SubTeams)
            .FirstOrDefaultAsync(t =>
                t.Id == teamId &&
                t.OrganizationId == organizationId &&
                t.DeletedAt == null)
            ?? throw new KeyNotFoundException("TEAM_NOT_FOUND");

        // Check for duplicate name if changing
        if (!string.IsNullOrEmpty(request.Name))
        {
            var normalizedName = request.Name.Trim().ToLowerInvariant();
            var checkParentId = request.ParentTeamId ?? team.ParentTeamId;

            if (normalizedName != team.NameNormalized &&
                await _dbContext.Teams.AnyAsync(t =>
                    t.OrganizationId == organizationId &&
                    t.NameNormalized == normalizedName &&
                    t.ParentTeamId == checkParentId &&
                    t.Id != teamId &&
                    t.DeletedAt == null))
            {
                throw new InvalidOperationException("TEAM_NAME_EXISTS");
            }

            team.Name = request.Name.Trim();
            team.NameNormalized = normalizedName;
        }

        // Handle parent change
        if (request.ParentTeamId.HasValue && request.ParentTeamId != team.ParentTeamId)
        {
            // Cannot set self as parent
            if (request.ParentTeamId == teamId)
            {
                throw new InvalidOperationException("CANNOT_SET_SELF_AS_PARENT");
            }

            // Cannot set a descendant as parent
            var descendants = await GetDescendantIdsAsync(teamId);
            if (descendants.Contains(request.ParentTeamId.Value))
            {
                throw new InvalidOperationException("CANNOT_SET_DESCENDANT_AS_PARENT");
            }

            var newParent = await _dbContext.Teams
                .FirstOrDefaultAsync(t =>
                    t.Id == request.ParentTeamId &&
                    t.OrganizationId == organizationId &&
                    t.DeletedAt == null)
                ?? throw new KeyNotFoundException("PARENT_TEAM_NOT_FOUND");

            if (newParent.HierarchyLevel + 1 + GetMaxChildDepth(team) > MaxHierarchyDepth)
            {
                throw new InvalidOperationException("MAX_HIERARCHY_DEPTH_EXCEEDED");
            }

            team.ParentTeamId = request.ParentTeamId;
            await UpdateHierarchyPathsAsync(team, newParent);
        }
        else if (request.ParentTeamId == null && team.ParentTeamId != null)
        {
            // Moving to top level
            team.ParentTeamId = null;
            team.HierarchyLevel = 0;
            team.HierarchyPath = $"/{team.Id}/";
            await UpdateChildHierarchyPathsAsync(team);
        }

        if (request.Description != null)
            team.Description = request.Description.Trim();

        if (request.LeaderId.HasValue)
        {
            var leaderExists = await _dbContext.OrganizationMembers
                .AnyAsync(m =>
                    m.OrganizationId == organizationId &&
                    m.UserId == request.LeaderId &&
                    m.Status == "active");

            if (!leaderExists)
            {
                throw new KeyNotFoundException("LEADER_NOT_FOUND");
            }
            team.LeaderId = request.LeaderId;
        }

        if (request.Color != null)
            team.Color = request.Color;

        if (request.Icon != null)
            team.Icon = request.Icon;

        if (request.IsActive.HasValue)
            team.IsActive = request.IsActive.Value;

        team.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Team {TeamId} updated in organization {OrganizationId} by user {UserId}",
            teamId, organizationId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "team.updated",
            EntityType = "Team",
            EntityId = teamId,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new { team.Name, team.ParentTeamId, team.LeaderId, team.IsActive }
        });

        return team;
    }

    public async Task DeleteTeamAsync(Guid organizationId, Guid teamId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var team = await _dbContext.Teams
            .Include(t => t.SubTeams.Where(st => st.DeletedAt == null))
            .Include(t => t.Members.Where(m => m.DeletedAt == null))
            .FirstOrDefaultAsync(t =>
                t.Id == teamId &&
                t.OrganizationId == organizationId &&
                t.DeletedAt == null)
            ?? throw new KeyNotFoundException("TEAM_NOT_FOUND");

        if (team.SubTeams.Any())
        {
            throw new InvalidOperationException("TEAM_HAS_SUBTEAMS");
        }

        // Soft delete team and its members
        team.DeletedAt = DateTime.UtcNow;
        foreach (var member in team.Members)
        {
            member.DeletedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Team {TeamId} deleted in organization {OrganizationId} by user {UserId}",
            teamId, organizationId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "team.deleted",
            EntityType = "Team",
            EntityId = teamId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { team.Name }
        });
    }

    public async Task<List<TeamTreeNode>> GetTeamHierarchyAsync(Guid organizationId)
    {
        var teams = await _dbContext.Teams
            .Include(t => t.Leader)
            .Include(t => t.Members.Where(m => m.DeletedAt == null))
            .Where(t => t.OrganizationId == organizationId && t.DeletedAt == null && t.IsActive)
            .OrderBy(t => t.HierarchyPath)
            .ToListAsync();

        var teamDict = teams.ToDictionary(t => t.Id);
        var rootNodes = new List<TeamTreeNode>();

        foreach (var team in teams.Where(t => t.ParentTeamId == null))
        {
            rootNodes.Add(BuildTreeNode(team, teamDict));
        }

        return rootNodes;
    }

    private static TeamTreeNode BuildTreeNode(Team team, Dictionary<Guid, Team> teamDict)
    {
        var children = teamDict.Values
            .Where(t => t.ParentTeamId == team.Id)
            .Select(t => BuildTreeNode(t, teamDict))
            .ToList();

        return new TeamTreeNode(
            team.Id,
            team.Name,
            team.Color,
            team.Icon,
            team.LeaderId,
            team.Leader?.Name,
            team.Members.Count,
            children
        );
    }

    public async Task<TeamMember> AddTeamMemberAsync(Guid organizationId, Guid teamId, AddTeamMemberRequest request, Guid userId)
    {
        await ValidateTeamManagementAccessAsync(organizationId, teamId, userId);

        var team = await _dbContext.Teams
            .FirstOrDefaultAsync(t =>
                t.Id == teamId &&
                t.OrganizationId == organizationId &&
                t.DeletedAt == null)
            ?? throw new KeyNotFoundException("TEAM_NOT_FOUND");

        // Validate user is org member
        var orgMember = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == request.UserId &&
                m.Status == "active")
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        // Check if already a member
        if (await _dbContext.TeamMembers.AnyAsync(m =>
            m.TeamId == teamId &&
            m.UserId == request.UserId &&
            m.DeletedAt == null))
        {
            throw new InvalidOperationException("ALREADY_TEAM_MEMBER");
        }

        // Handle primary team
        if (request.IsPrimary)
        {
            // Clear existing primary
            await _dbContext.TeamMembers
                .Where(m => m.UserId == request.UserId && m.IsPrimary && m.DeletedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsPrimary, false));
        }

        var teamMember = new TeamMember
        {
            TeamId = teamId,
            UserId = request.UserId,
            Role = request.Role.ToLowerInvariant(),
            Title = request.Title?.Trim(),
            IsPrimary = request.IsPrimary,
            JoinedAt = DateTime.UtcNow
        };

        _dbContext.TeamMembers.Add(teamMember);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {AddedUserId} added to team {TeamId} by user {UserId}",
            request.UserId, teamId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "team.member_added",
            EntityType = "TeamMember",
            EntityId = teamMember.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new { TeamId = teamId, request.UserId, request.Role }
        });

        return teamMember;
    }

    public async Task<TeamMember> UpdateTeamMemberAsync(Guid organizationId, Guid teamId, Guid memberId, UpdateTeamMemberRequest request, Guid userId)
    {
        await ValidateTeamManagementAccessAsync(organizationId, teamId, userId);

        var teamMember = await _dbContext.TeamMembers
            .FirstOrDefaultAsync(m =>
                m.Id == memberId &&
                m.TeamId == teamId &&
                m.DeletedAt == null)
            ?? throw new KeyNotFoundException("TEAM_MEMBER_NOT_FOUND");

        if (request.Role != null)
            teamMember.Role = request.Role.ToLowerInvariant();

        if (request.Title != null)
            teamMember.Title = request.Title.Trim();

        if (request.IsPrimary.HasValue && request.IsPrimary.Value)
        {
            // Clear existing primary for this user
            await _dbContext.TeamMembers
                .Where(m => m.UserId == teamMember.UserId && m.IsPrimary && m.Id != memberId && m.DeletedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsPrimary, false));

            teamMember.IsPrimary = true;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Team member {MemberId} updated in team {TeamId} by user {UserId}",
            memberId, teamId, userId);

        return teamMember;
    }

    public async Task RemoveTeamMemberAsync(Guid organizationId, Guid teamId, Guid memberId, Guid userId)
    {
        await ValidateTeamManagementAccessAsync(organizationId, teamId, userId);

        var teamMember = await _dbContext.TeamMembers
            .FirstOrDefaultAsync(m =>
                m.Id == memberId &&
                m.TeamId == teamId &&
                m.DeletedAt == null)
            ?? throw new KeyNotFoundException("TEAM_MEMBER_NOT_FOUND");

        teamMember.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Team member {MemberId} removed from team {TeamId} by user {UserId}",
            memberId, teamId, userId);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "team.member_removed",
            EntityType = "TeamMember",
            EntityId = memberId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { TeamId = teamId, teamMember.UserId }
        });
    }

    public async Task<List<TeamMembershipDto>> GetUserTeamsAsync(Guid organizationId, Guid userId)
    {
        return await _dbContext.TeamMembers
            .Include(m => m.Team)
                .ThenInclude(t => t.Members.Where(m => m.DeletedAt == null))
            .Where(m =>
                m.UserId == userId &&
                m.Team.OrganizationId == organizationId &&
                m.Team.DeletedAt == null &&
                m.DeletedAt == null)
            .Select(m => new TeamMembershipDto(
                m.TeamId,
                m.Team.Name,
                m.Team.Color,
                m.Role,
                m.Title,
                m.IsPrimary,
                m.Team.LeaderId,
                m.Team.Members.Count
            ))
            .ToListAsync();
    }

    public async Task SetPrimaryTeamAsync(Guid organizationId, Guid teamId, Guid userId)
    {
        var teamMember = await _dbContext.TeamMembers
            .Include(m => m.Team)
            .FirstOrDefaultAsync(m =>
                m.TeamId == teamId &&
                m.UserId == userId &&
                m.Team.OrganizationId == organizationId &&
                m.DeletedAt == null)
            ?? throw new KeyNotFoundException("TEAM_MEMBER_NOT_FOUND");

        // Clear existing primary
        await _dbContext.TeamMembers
            .Where(m => m.UserId == userId && m.IsPrimary && m.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsPrimary, false));

        teamMember.IsPrimary = true;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {UserId} set primary team to {TeamId}", userId, teamId);
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

    private async Task ValidateTeamManagementAccessAsync(Guid organizationId, Guid teamId, Guid userId)
    {
        // Check if user is org admin
        var orgMember = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.Status == "active");

        if (orgMember == null)
            throw new UnauthorizedAccessException("NOT_A_MEMBER");

        if (orgMember.Role is "owner" or "admin")
            return;

        // Check if user is team lead
        var team = await _dbContext.Teams
            .FirstOrDefaultAsync(t =>
                t.Id == teamId &&
                t.OrganizationId == organizationId &&
                t.DeletedAt == null);

        if (team?.LeaderId == userId)
            return;

        // Check if user has team lead role in this team
        var teamMember = await _dbContext.TeamMembers
            .FirstOrDefaultAsync(m =>
                m.TeamId == teamId &&
                m.UserId == userId &&
                m.Role == "lead" &&
                m.DeletedAt == null);

        if (teamMember != null)
            return;

        throw new UnauthorizedAccessException("TEAM_MANAGEMENT_ACCESS_REQUIRED");
    }

    private async Task<HashSet<Guid>> GetDescendantIdsAsync(Guid teamId)
    {
        var team = await _dbContext.Teams.FindAsync(teamId);
        if (team == null || string.IsNullOrEmpty(team.HierarchyPath))
            return [];

        var descendants = await _dbContext.Teams
            .Where(t => t.HierarchyPath!.StartsWith(team.HierarchyPath) && t.Id != teamId && t.DeletedAt == null)
            .Select(t => t.Id)
            .ToListAsync();

        return [.. descendants];
    }

    private static int GetMaxChildDepth(Team team)
    {
        if (!team.SubTeams.Any())
            return 0;

        return 1 + team.SubTeams.Max(GetMaxChildDepth);
    }

    private async Task UpdateHierarchyPathsAsync(Team team, Team newParent)
    {
        team.HierarchyLevel = newParent.HierarchyLevel + 1;
        team.HierarchyPath = $"{newParent.HierarchyPath}{team.Id}/";
        await UpdateChildHierarchyPathsAsync(team);
    }

    private async Task UpdateChildHierarchyPathsAsync(Team team)
    {
        var children = await _dbContext.Teams
            .Where(t => t.ParentTeamId == team.Id && t.DeletedAt == null)
            .ToListAsync();

        foreach (var child in children)
        {
            child.HierarchyLevel = team.HierarchyLevel + 1;
            child.HierarchyPath = $"{team.HierarchyPath}{child.Id}/";
            await UpdateChildHierarchyPathsAsync(child);
        }
    }
}
