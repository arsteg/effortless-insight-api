using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// Auto-Draft DTOs
// ============================================================================

/// <summary>
/// Request for AI-powered auto-draft generation.
/// </summary>
public record AutoDraftRequest
{
    /// <summary>
    /// Tone of the response: formal, conciliatory, or defensive. Default: formal
    /// </summary>
    /// <example>formal</example>
    [RegularExpression("^(formal|conciliatory|defensive)$",
        ErrorMessage = "Tone must be 'formal', 'conciliatory', or 'defensive'")]
    public string? Tone { get; init; }

    /// <summary>
    /// Language for the response: en (English) or hi (Hindi). Default: en
    /// </summary>
    /// <example>en</example>
    [RegularExpression("^(en|hi)$",
        ErrorMessage = "Language must be 'en' or 'hi'")]
    public string? Language { get; init; }

    /// <summary>
    /// Specific points to address in the response. Max 10 items, 500 chars each.
    /// </summary>
    [MaxLength(10, ErrorMessage = "Maximum 10 points to address allowed")]
    public List<string>? PointsToAddress { get; init; }

    /// <summary>
    /// Additional context or instructions for the AI. Max 1000 characters.
    /// </summary>
    [MaxLength(1000, ErrorMessage = "Additional instructions cannot exceed 1000 characters")]
    public string? AdditionalInstructions { get; init; }
}

/// <summary>
/// Response containing the AI-generated draft and metadata.
/// </summary>
public record AutoDraftResponseDto(
    /// <summary>The generated draft response text.</summary>
    string DraftContent,
    /// <summary>Metadata about the generation process.</summary>
    AutoDraftMetadataDto Metadata);

/// <summary>
/// Metadata about the auto-draft generation process.
/// </summary>
public record AutoDraftMetadataDto(
    /// <summary>The AI model used for generation.</summary>
    string Model,
    /// <summary>Time taken to generate the response in milliseconds.</summary>
    int ProcessingTimeMs,
    /// <summary>Type of notice the response was generated for.</summary>
    string? NoticeType,
    /// <summary>Number of input tokens used.</summary>
    int InputTokens,
    /// <summary>Number of output tokens generated.</summary>
    int OutputTokens,
    /// <summary>Estimated cost in USD.</summary>
    decimal EstimatedCost);
