using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class NoticeAiReport : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    public int ReportVersion { get; set; } = 1;

    public int? RiskScore { get; set; } // 0-100

    [MaxLength(20)]
    public string? RiskLevel { get; set; } // low, medium, high, critical

    public string? SummaryEn { get; set; }

    public string? SummaryHi { get; set; }

    public string? PlainEnglish { get; set; }

    public Dictionary<string, object>? ActionItems { get; set; }

    public Dictionary<string, object>? RequiredDocuments { get; set; }

    public Dictionary<string, object>? LegalReferences { get; set; }

    public Dictionary<string, object>? ConfidenceScores { get; set; }

    /// <summary>
    /// List of reasons explaining why this risk level was assigned.
    /// </summary>
    public List<string>? RiskReasons { get; set; }

    /// <summary>
    /// Detailed risk assessment breakdown with component scores.
    /// </summary>
    public Dictionary<string, object>? RiskAssessment { get; set; }

    /// <summary>
    /// List of calculation discrepancies found in the notice.
    /// </summary>
    public List<Dictionary<string, object>>? CalculationDiscrepancies { get; set; }

    /// <summary>
    /// Pre-filing review checklist items.
    /// </summary>
    public List<Dictionary<string, object>>? PrefilingChecklist { get; set; }

    [MaxLength(50)]
    public string? ModelUsed { get; set; }

    public int? ProcessingTimeMs { get; set; }

    public Dictionary<string, object>? FullReportJson { get; set; }
}
