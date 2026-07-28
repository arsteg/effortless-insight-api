using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Minimal team management for task assignment: list, create, update, delete
/// teams within the current organization.
/// </summary>
[ApiController]
[Route("api/v1/teams")]
[Authorize]
public class TeamsController : ControllerBase
{
    private static readonly string[] ManageRoles = ["owner", "admin"];

    private readonly ApplicationDbContext _context;
    private readonly ICurrentOrganizationService _orgService;
    private readonly ILogger<TeamsController> _logger;

    public TeamsController(
        ApplicationDbContext context,
        ICurrentOrganizationService orgService,
        ILogger<TeamsController> logger)
    {
        _context = context;
        _orgService = orgService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("Invalid or missing user identifier");
        return userId;
    }

    private bool CanManageTeams() =>
        ManageRoles.Contains(_orgService.Role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// List active teams in the organization with their members.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TeamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeams()
    {
        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");

        var teams = await _context.Teams
            .Where(t => t.OrganizationId == orgId && t.IsActive && t.DeletedAt == null)
            .Include(t => t.Members).ThenInclude(m => m.User)
            .OrderBy(t => t.Name)
            .ToListAsync();

        return Ok(teams.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Create a team with an optional initial member list.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTeam([FromBody] CreateTeamDto dto)
    {
        if (!CanManageTeams())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only owners and admins can manage teams" });

        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
        var normalized = dto.Name.Trim().ToLowerInvariant();

        var duplicate = await _context.Teams.AnyAsync(t =>
            t.OrganizationId == orgId && t.NameNormalized == normalized && t.DeletedAt == null);
        if (duplicate)
            return BadRequest(new { error = $"A team named \"{dto.Name.Trim()}\" already exists" });

        var memberIds = await ValidateMemberIdsAsync(orgId, dto.MemberIds);
        if (memberIds == null)
            return BadRequest(new { error = "One or more selected members do not belong to this organization" });

        var team = new Team
        {
            OrganizationId = orgId,
            Name = dto.Name.Trim(),
            NameNormalized = normalized,
            Description = dto.Description?.Trim(),
            Color = dto.Color,
            IsActive = true,
        };
        _context.Teams.Add(team);

        foreach (var userId in memberIds)
        {
            _context.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = userId });
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("User {UserId} created team {TeamId} ({Name})", GetUserId(), team.Id, team.Name);

        var created = await LoadTeamAsync(orgId, team.Id);
        return CreatedAtAction(nameof(GetTeams), MapToDto(created!));
    }

    /// <summary>
    /// Update a team's name, description, color, and/or full member list.
    /// </summary>
    [HttpPut("{teamId:guid}")]
    [ProducesResponseType(typeof(TeamDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTeam(Guid teamId, [FromBody] UpdateTeamDto dto)
    {
        if (!CanManageTeams())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only owners and admins can manage teams" });

        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
        var team = await LoadTeamAsync(orgId, teamId);
        if (team == null)
            return NotFound(new { error = "Team not found" });

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var normalized = dto.Name.Trim().ToLowerInvariant();
            var duplicate = await _context.Teams.AnyAsync(t =>
                t.OrganizationId == orgId && t.NameNormalized == normalized &&
                t.Id != teamId && t.DeletedAt == null);
            if (duplicate)
                return BadRequest(new { error = $"A team named \"{dto.Name.Trim()}\" already exists" });

            team.Name = dto.Name.Trim();
            team.NameNormalized = normalized;
        }

        if (dto.Description != null) team.Description = dto.Description.Trim();
        if (dto.Color != null) team.Color = dto.Color;

        if (dto.MemberIds != null)
        {
            var memberIds = await ValidateMemberIdsAsync(orgId, dto.MemberIds);
            if (memberIds == null)
                return BadRequest(new { error = "One or more selected members do not belong to this organization" });

            var toRemove = team.Members.Where(m => !memberIds.Contains(m.UserId)).ToList();
            _context.TeamMembers.RemoveRange(toRemove);

            var existing = team.Members.Select(m => m.UserId).ToHashSet();
            foreach (var userId in memberIds.Where(id => !existing.Contains(id)))
            {
                _context.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = userId });
            }
        }

        team.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updated = await LoadTeamAsync(orgId, teamId);
        return Ok(MapToDto(updated!));
    }

    /// <summary>
    /// Soft-delete a team. Tasks previously assigned to it keep their history;
    /// the team can no longer be assigned to new tasks.
    /// </summary>
    [HttpDelete("{teamId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTeam(Guid teamId)
    {
        if (!CanManageTeams())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only owners and admins can manage teams" });

        var orgId = _orgService.OrganizationId ?? throw new InvalidOperationException("No organization context");
        var team = await _context.Teams.FirstOrDefaultAsync(t =>
            t.Id == teamId && t.OrganizationId == orgId && t.DeletedAt == null);
        if (team == null)
            return NotFound(new { error = "Team not found" });

        team.IsActive = false;
        team.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} deleted team {TeamId} ({Name})", GetUserId(), team.Id, team.Name);
        return NoContent();
    }

    private async Task<Team?> LoadTeamAsync(Guid orgId, Guid teamId) =>
        await _context.Teams
            .Include(t => t.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(t => t.Id == teamId && t.OrganizationId == orgId && t.DeletedAt == null);

    /// <summary>
    /// Returns the distinct valid member ids, or null if any id is not an
    /// organization member.
    /// </summary>
    private async Task<List<Guid>?> ValidateMemberIdsAsync(Guid orgId, List<Guid>? memberIds)
    {
        var distinct = memberIds?.Distinct().ToList() ?? [];
        if (distinct.Count == 0) return distinct;

        var validCount = await _context.OrganizationMembers
            .CountAsync(m => m.OrganizationId == orgId && distinct.Contains(m.UserId));
        return validCount == distinct.Count ? distinct : null;
    }

    private static TeamDto MapToDto(Team team) => new(
        team.Id,
        team.Name,
        team.Description,
        team.Color,
        team.Icon,
        team.Members.Count,
        team.Members
            .Where(m => m.User != null)
            .Select(m => new TeamMemberInfoDto(m.UserId, m.User.Name, m.User.Email, m.User.AvatarUrl))
            .ToList()
    );
}

// =============================================================================
// TEAM DTOs
// =============================================================================

public record TeamDto(
    Guid Id,
    string Name,
    string? Description,
    string? Color,
    string? Icon,
    int MemberCount,
    List<TeamMemberInfoDto> Members
);

public record TeamMemberInfoDto(
    Guid Id,
    string Name,
    string? Email,
    string? AvatarUrl
);

public record CreateTeamDto(
    [Required][MaxLength(100)] string Name,
    [MaxLength(500)] string? Description,
    [MaxLength(7)] string? Color,
    List<Guid>? MemberIds
);

public record UpdateTeamDto(
    [MaxLength(100)] string? Name,
    [MaxLength(500)] string? Description,
    [MaxLength(7)] string? Color,
    List<Guid>? MemberIds
);
