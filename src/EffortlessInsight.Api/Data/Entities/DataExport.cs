using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks organization data export requests and their status.
/// Exports are generated asynchronously and stored in S3 for download.
/// </summary>
public class DataExport : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid RequestedById { get; set; }

    [ForeignKey(nameof(RequestedById))]
    public ApplicationUser RequestedBy { get; set; } = null!;

    /// <summary>
    /// Status: pending, processing, completed, failed, expired
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// S3 key for the generated export file
    /// </summary>
    [MaxLength(500)]
    public string? FileKey { get; set; }

    /// <summary>
    /// Pre-signed download URL (regenerated on demand)
    /// </summary>
    public string? FileUrl { get; set; }

    /// <summary>
    /// Export format: json, csv, zip
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string Format { get; set; } = "json";

    /// <summary>
    /// File size in bytes after generation
    /// </summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>
    /// When the export file expires (typically 7 days after completion)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// When processing started
    /// </summary>
    public DateTime? ProcessingStartedAt { get; set; }

    /// <summary>
    /// When processing completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message if export failed
    /// </summary>
    [MaxLength(1000)]
    public string? Error { get; set; }

    /// <summary>
    /// Export options stored as JSONB
    /// </summary>
    public Dictionary<string, object>? Options { get; set; }

    /// <summary>
    /// Summary of exported data counts
    /// </summary>
    public Dictionary<string, object>? Summary { get; set; }
}
