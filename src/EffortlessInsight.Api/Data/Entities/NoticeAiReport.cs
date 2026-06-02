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

    [MaxLength(50)]
    public string? ModelUsed { get; set; }

    public int? ProcessingTimeMs { get; set; }

    public Dictionary<string, object>? FullReportJson { get; set; }
}
