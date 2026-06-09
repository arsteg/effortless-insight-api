using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Reference table for Indian state codes used in GSTIN validation.
/// First 2 digits of GSTIN represent the state code.
/// </summary>
public class GstinStateCode
{
    /// <summary>
    /// 2-digit state code (e.g., "27" for Maharashtra)
    /// </summary>
    [Key]
    [MaxLength(2)]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// State or Union Territory name
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a Union Territory rather than a State
    /// </summary>
    public bool IsUnionTerritory { get; set; }
}
