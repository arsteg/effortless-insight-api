using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace EffortlessInsight.Api.Features.Workflows.Services;

/// <summary>
/// Service for determining automatic workflow assignment based on rules.
/// </summary>
public interface IAssignmentEngineService
{
    /// <summary>
    /// Determines the assignee for a workflow instance based on assignment rules.
    /// </summary>
    /// <param name="instance">The workflow instance.</param>
    /// <param name="stage">The workflow stage to assign for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>User ID of the selected assignee, or null if no assignment could be made.</returns>
    Task<Guid?> DetermineAssigneeAsync(
        NoticeWorkflowInstance instance,
        WorkflowStage stage,
        CancellationToken ct);
}

/// <summary>
/// Implementation of the assignment engine that evaluates rules and applies strategies.
/// </summary>
public class AssignmentEngineService : IAssignmentEngineService
{
    private readonly ApplicationDbContext _context;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AssignmentEngineService> _logger;

    // Redis key prefix for round-robin tracking
    private const string RoundRobinKeyPrefix = "workflow:roundrobin:";

    public AssignmentEngineService(
        ApplicationDbContext context,
        IConditionEvaluator conditionEvaluator,
        IConnectionMultiplexer redis,
        ILogger<AssignmentEngineService> logger)
    {
        _context = context;
        _conditionEvaluator = conditionEvaluator;
        _redis = redis;
        _logger = logger;
    }

    public async Task<Guid?> DetermineAssigneeAsync(
        NoticeWorkflowInstance instance,
        WorkflowStage stage,
        CancellationToken ct)
    {
        // Load the notice for condition evaluation
        var notice = await _context.Notices
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == instance.NoticeId, ct);

        if (notice == null)
        {
            _logger.LogWarning(
                "Cannot determine assignee: Notice {NoticeId} not found for workflow instance {InstanceId}",
                instance.NoticeId, instance.Id);
            return null;
        }

        // Load assignment rules for the workflow template, ordered by priority
        var rules = await _context.WorkflowAssignmentRules
            .Where(r => r.WorkflowTemplateId == instance.WorkflowTemplateId && r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            _logger.LogDebug(
                "No assignment rules found for workflow template {TemplateId}",
                instance.WorkflowTemplateId);
            return null;
        }

        // Evaluate each rule in priority order
        foreach (var rule in rules)
        {
            // Check if all conditions match
            if (!EvaluateConditions(rule.Conditions, notice))
            {
                continue;
            }

            // Find the assign action
            var assignAction = rule.Actions.FirstOrDefault(a =>
                a.Type.Equals("assign", StringComparison.OrdinalIgnoreCase));

            if (assignAction?.Target == null)
            {
                continue;
            }

            // Execute the assignment strategy
            var assigneeId = await ExecuteAssignmentStrategyAsync(
                assignAction.Target,
                notice.OrganizationId,
                stage,
                ct);

            if (assigneeId.HasValue)
            {
                _logger.LogInformation(
                    "Auto-assignment rule '{RuleName}' matched for notice {NoticeId}, assigned to user {UserId}",
                    rule.Name, instance.NoticeId, assigneeId.Value);
                return assigneeId;
            }
        }

        _logger.LogDebug(
            "No assignment rules matched for notice {NoticeId} in stage {StageKey}",
            instance.NoticeId, stage.StageKey);
        return null;
    }

    /// <summary>
    /// Evaluates all conditions in a rule against the notice.
    /// </summary>
    private bool EvaluateConditions(List<RuleCondition> conditions, Notice notice)
    {
        if (conditions.Count == 0)
        {
            // No conditions means the rule always matches
            return true;
        }

        // All conditions must match (AND logic)
        foreach (var condition in conditions)
        {
            var workflowCondition = new WorkflowCondition
            {
                Field = condition.Field,
                Operator = condition.Operator,
                Value = condition.Value
            };

            if (!_conditionEvaluator.Evaluate(workflowCondition, notice))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Executes the assignment strategy based on target type.
    /// </summary>
    private async Task<Guid?> ExecuteAssignmentStrategyAsync(
        AssignmentTarget target,
        Guid organizationId,
        WorkflowStage stage,
        CancellationToken ct)
    {
        var strategyType = target.Type.ToLowerInvariant();

        return strategyType switch
        {
            "specific_user" => await AssignSpecificUserAsync(target, organizationId, ct),
            "by_role" => await AssignByRoleAsync(target, organizationId, ct),
            "round_robin" or "roundrobin" => await AssignRoundRobinAsync(target, organizationId, stage, ct),
            "least_workload" or "leastworkload" => await AssignLeastWorkloadAsync(target, organizationId, ct),
            "by_skill" or "byskill" => await AssignBySkillAsync(target, organizationId, stage, ct),
            _ => HandleUnknownStrategy(strategyType)
        };
    }

    /// <summary>
    /// Assigns to a specific user by ID.
    /// </summary>
    private async Task<Guid?> AssignSpecificUserAsync(
        AssignmentTarget target,
        Guid organizationId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(target.Value, out var userId))
        {
            _logger.LogWarning("Invalid user ID in specific_user assignment: {Value}", target.Value);
            return null;
        }

        // Verify the user is an active member of the organization
        var isMember = await _context.OrganizationMembers
            .AnyAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.Status == "active",
                ct);

        if (!isMember)
        {
            _logger.LogWarning(
                "User {UserId} is not an active member of organization {OrganizationId}",
                userId, organizationId);
            return null;
        }

        return userId;
    }

    /// <summary>
    /// Assigns to the first available user with a specific role.
    /// </summary>
    private async Task<Guid?> AssignByRoleAsync(
        AssignmentTarget target,
        Guid organizationId,
        CancellationToken ct)
    {
        var role = target.Value;

        if (string.IsNullOrWhiteSpace(role))
        {
            _logger.LogWarning("Role not specified in by_role assignment");
            return null;
        }

        // Find users with the specified role in the organization
        var eligibleUsers = await _context.OrganizationMembers
            .Where(m =>
                m.OrganizationId == organizationId &&
                m.Role == role &&
                m.Status == "active")
            .Select(m => m.UserId)
            .ToListAsync(ct);

        if (eligibleUsers.Count == 0)
        {
            _logger.LogDebug(
                "No users with role '{Role}' found in organization {OrganizationId}",
                role, organizationId);
            return null;
        }

        // Return the first eligible user (could be enhanced with additional logic)
        return eligibleUsers.First();
    }

    /// <summary>
    /// Assigns using round-robin rotation among eligible users.
    /// </summary>
    private async Task<Guid?> AssignRoundRobinAsync(
        AssignmentTarget target,
        Guid organizationId,
        WorkflowStage stage,
        CancellationToken ct)
    {
        // Determine the pool of users
        var pool = target.Pool ?? "all_members";
        var eligibleUsers = await GetEligibleUsersForPoolAsync(target, pool, organizationId, ct);

        if (eligibleUsers.Count == 0)
        {
            _logger.LogDebug("No eligible users in pool '{Pool}' for round-robin assignment", pool);
            return null;
        }

        // Use Redis to track last assigned index
        var redisKey = $"{RoundRobinKeyPrefix}{organizationId}:{stage.WorkflowTemplateId}:{stage.StageKey}";
        var db = _redis.GetDatabase();

        // Get the current index and increment atomically
        var currentIndex = (int)await db.StringIncrementAsync(redisKey);

        // Calculate the next user index (wrap around)
        var userIndex = (currentIndex - 1) % eligibleUsers.Count;
        var assignedUserId = eligibleUsers[userIndex];

        _logger.LogDebug(
            "Round-robin assignment: index {Index} of {Total} users, assigned {UserId}",
            userIndex, eligibleUsers.Count, assignedUserId);

        return assignedUserId;
    }

    /// <summary>
    /// Assigns to the user with the least active workload.
    /// </summary>
    private async Task<Guid?> AssignLeastWorkloadAsync(
        AssignmentTarget target,
        Guid organizationId,
        CancellationToken ct)
    {
        // Determine the pool of users
        var pool = target.Pool ?? "all_members";
        var eligibleUsers = await GetEligibleUsersForPoolAsync(target, pool, organizationId, ct);

        if (eligibleUsers.Count == 0)
        {
            _logger.LogDebug("No eligible users in pool '{Pool}' for least_workload assignment", pool);
            return null;
        }

        // Count active tasks per user
        var taskCounts = await _context.Tasks
            .Where(t =>
                eligibleUsers.Contains(t.AssignedToId!.Value) &&
                t.Status != TaskStatusValues.Done &&
                t.Status != TaskStatusValues.Cancelled)
            .GroupBy(t => t.AssignedToId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Count active workflow instances (notices) assigned per user
        var workflowCounts = await _context.NoticeWorkflowInstances
            .Where(i =>
                eligibleUsers.Contains(i.AssignedToId!.Value) &&
                i.Status == WorkflowInstanceStatuses.Active)
            .GroupBy(i => i.AssignedToId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Calculate total workload per user
        var workloads = eligibleUsers.Select(userId =>
        {
            var tasks = taskCounts.GetValueOrDefault(userId, 0);
            var workflows = workflowCounts.GetValueOrDefault(userId, 0);
            return new { UserId = userId, Workload = tasks + workflows };
        })
        .OrderBy(x => x.Workload)
        .ToList();

        var selectedUser = workloads.First();

        _logger.LogDebug(
            "Least workload assignment: selected user {UserId} with workload {Workload}",
            selectedUser.UserId, selectedUser.Workload);

        return selectedUser.UserId;
    }

    /// <summary>
    /// Assigns based on skill matching from user metadata.
    /// </summary>
    private async Task<Guid?> AssignBySkillAsync(
        AssignmentTarget target,
        Guid organizationId,
        WorkflowStage stage,
        CancellationToken ct)
    {
        // Get required skills from stage metadata or target config
        var requiredSkills = GetRequiredSkills(target, stage);

        if (requiredSkills.Count == 0)
        {
            _logger.LogWarning(
                "No required skills specified for by_skill assignment in stage {StageKey}",
                stage.StageKey);
            return null;
        }

        // Get eligible users with their metadata
        var pool = target.Pool ?? "all_members";
        var eligibleUserIds = await GetEligibleUsersForPoolAsync(target, pool, organizationId, ct);

        if (eligibleUserIds.Count == 0)
        {
            _logger.LogDebug("No eligible users in pool '{Pool}' for by_skill assignment", pool);
            return null;
        }

        // Load user metadata to check skills
        var users = await _context.Users
            .Where(u => eligibleUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Preferences })
            .ToListAsync(ct);

        // Find users with matching skills
        var matchingUsers = new List<(Guid UserId, int MatchCount)>();

        foreach (var user in users)
        {
            var userSkills = GetUserSkills(user.Preferences);
            var matchCount = requiredSkills.Count(skill =>
                userSkills.Contains(skill, StringComparer.OrdinalIgnoreCase));

            if (matchCount > 0)
            {
                matchingUsers.Add((user.Id, matchCount));
            }
        }

        if (matchingUsers.Count == 0)
        {
            _logger.LogDebug(
                "No users with matching skills {Skills} found",
                string.Join(", ", requiredSkills));
            return null;
        }

        // Select the user with the most matching skills
        var selectedUser = matchingUsers
            .OrderByDescending(x => x.MatchCount)
            .First();

        _logger.LogDebug(
            "Skill-based assignment: selected user {UserId} with {MatchCount} matching skills",
            selectedUser.UserId, selectedUser.MatchCount);

        return selectedUser.UserId;
    }

    /// <summary>
    /// Gets eligible users based on pool configuration.
    /// </summary>
    private async Task<List<Guid>> GetEligibleUsersForPoolAsync(
        AssignmentTarget target,
        string pool,
        Guid organizationId,
        CancellationToken ct)
    {
        IQueryable<OrganizationMember> query = _context.OrganizationMembers
            .Where(m => m.OrganizationId == organizationId && m.Status == "active");

        // Apply pool filter
        switch (pool.ToLowerInvariant())
        {
            case "all_members":
                // All active members
                break;

            case "managers":
                query = query.Where(m => m.Role == "manager" || m.Role == "admin" || m.Role == "owner");
                break;

            case "admins":
                query = query.Where(m => m.Role == "admin" || m.Role == "owner");
                break;

            default:
                // Treat pool as a role name
                if (!string.IsNullOrWhiteSpace(target.Value))
                {
                    query = query.Where(m => m.Role == target.Value);
                }
                break;
        }

        return await query
            .Select(m => m.UserId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets required skills from target config or stage metadata.
    /// </summary>
    private static List<string> GetRequiredSkills(AssignmentTarget target, WorkflowStage stage)
    {
        var skills = new List<string>();

        // Check target config for skills
        if (!string.IsNullOrWhiteSpace(target.Value))
        {
            // Value can be comma-separated list of skills
            skills.AddRange(target.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()));
        }

        // Check stage metadata for required skills
        if (stage.Metadata?.TryGetValue("requiredSkills", out var metaSkills) == true)
        {
            if (metaSkills is IEnumerable<object> skillList)
            {
                skills.AddRange(skillList.Select(s => s.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)));
            }
            else if (metaSkills is string skillString)
            {
                skills.AddRange(skillString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()));
            }
        }

        return skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Gets user skills from preferences metadata.
    /// </summary>
    private static List<string> GetUserSkills(Dictionary<string, object>? preferences)
    {
        if (preferences == null)
        {
            return [];
        }

        if (!preferences.TryGetValue("skills", out var skillsValue))
        {
            return [];
        }

        if (skillsValue is IEnumerable<object> skillList)
        {
            return skillList.Select(s => s.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        if (skillsValue is string skillString)
        {
            return skillString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();
        }

        return [];
    }

    /// <summary>
    /// Handles unknown strategy types.
    /// </summary>
    private Guid? HandleUnknownStrategy(string strategyType)
    {
        _logger.LogWarning("Unknown assignment strategy type: {StrategyType}", strategyType);
        return null;
    }
}

/// <summary>
/// Assignment strategy type constants.
/// </summary>
public static class AssignmentStrategyTypes
{
    public const string SpecificUser = "specific_user";
    public const string ByRole = "by_role";
    public const string RoundRobin = "round_robin";
    public const string LeastWorkload = "least_workload";
    public const string BySkill = "by_skill";

    public static readonly string[] All =
    [
        SpecificUser, ByRole, RoundRobin, LeastWorkload, BySkill
    ];

    public static bool IsValid(string type) =>
        All.Contains(type, StringComparer.OrdinalIgnoreCase);
}
