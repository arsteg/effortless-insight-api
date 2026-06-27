using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a dependency relationship between tasks.
/// When DependencyType is "blocks", DependsOnTask must be completed before Task can start.
/// </summary>
public class TaskDependency : BaseEntity
{
    /// <summary>
    /// The task that depends on another task.
    /// </summary>
    [Required]
    public Guid TaskId { get; set; }
    public NoticeTask Task { get; set; } = null!;

    /// <summary>
    /// The task that this task depends on (must be completed first).
    /// </summary>
    [Required]
    public Guid DependsOnTaskId { get; set; }
    public NoticeTask DependsOnTask { get; set; } = null!;

    /// <summary>
    /// Type of dependency: "blocks" (must complete first) or "related" (informational only).
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string DependencyType { get; set; } = TaskDependencyType.Blocks;
}

/// <summary>
/// Task dependency type constants.
/// </summary>
public static class TaskDependencyType
{
    /// <summary>
    /// The DependsOnTask must be completed before the Task can start.
    /// </summary>
    public const string Blocks = "blocks";

    /// <summary>
    /// Informational relationship only, no blocking behavior.
    /// </summary>
    public const string Related = "related";

    public static readonly string[] All = [Blocks, Related];

    public static bool IsValid(string type) => All.Contains(type, StringComparer.OrdinalIgnoreCase);
}
