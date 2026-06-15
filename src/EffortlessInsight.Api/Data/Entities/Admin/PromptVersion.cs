using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Version-controlled AI prompts for notice analysis.
/// Allows admins to manage and rollback prompt versions.
/// </summary>
public class PromptVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Prompt identifier (e.g., "notice_analysis", "risk_assessment")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string PromptKey { get; set; } = string.Empty;

    /// <summary>
    /// Version number
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Human-readable name
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of this prompt's purpose
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The actual prompt content
    /// </summary>
    [Required]
    public string PromptContent { get; set; } = string.Empty;

    /// <summary>
    /// System instructions (prepended to prompt)
    /// </summary>
    public string? SystemInstructions { get; set; }

    /// <summary>
    /// Prompt variables with descriptions
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>
    /// Model configuration (temperature, max_tokens, etc.)
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> ModelConfig { get; set; } = new();

    /// <summary>
    /// Target AI model (e.g., "gpt-4", "claude-3")
    /// </summary>
    [MaxLength(50)]
    public string? TargetModel { get; set; }

    /// <summary>
    /// Status: draft, active, deprecated, archived
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "draft";

    /// <summary>
    /// Whether this is the active version for this prompt key
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Expected output format (json_schema, text, etc.)
    /// </summary>
    [MaxLength(50)]
    public string? OutputFormat { get; set; }

    /// <summary>
    /// JSON schema for expected output (if applicable)
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? OutputSchema { get; set; }

    /// <summary>
    /// Admin who created this version
    /// </summary>
    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public AdminUser CreatedBy { get; set; } = null!;

    /// <summary>
    /// Admin who activated this version
    /// </summary>
    public Guid? ActivatedById { get; set; }

    [ForeignKey(nameof(ActivatedById))]
    public AdminUser? ActivatedBy { get; set; }

    /// <summary>
    /// When activated
    /// </summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// Change notes explaining what changed in this version
    /// </summary>
    public string? ChangeNotes { get; set; }

    /// <summary>
    /// Test results/metrics for this version
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? TestResults { get; set; }

    /// <summary>
    /// Accuracy metrics after deployment
    /// </summary>
    public double? AccuracyScore { get; set; }

    /// <summary>
    /// Number of times this prompt has been used
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// Average processing time in milliseconds
    /// </summary>
    public double? AvgProcessingTimeMs { get; set; }

    /// <summary>
    /// Timestamps
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Prompt status constants
/// </summary>
public static class PromptStatus
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Deprecated = "deprecated";
    public const string Archived = "archived";
}

/// <summary>
/// Common prompt keys
/// </summary>
public static class PromptKeys
{
    public const string NoticeAnalysis = "notice_analysis";
    public const string RiskAssessment = "risk_assessment";
    public const string ActionExtraction = "action_extraction";
    public const string DocumentClassification = "document_classification";
    public const string ResponseGeneration = "response_generation";
    public const string SummaryGeneration = "summary_generation";
}
