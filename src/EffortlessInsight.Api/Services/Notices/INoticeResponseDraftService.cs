using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Service for AI-powered auto-drafting of notice responses.
/// </summary>
public interface INoticeResponseDraftService
{
    /// <summary>
    /// Generate an AI-drafted response for a notice.
    /// </summary>
    /// <param name="noticeId">The notice ID to generate a response for.</param>
    /// <param name="organizationId">The organization ID for access control.</param>
    /// <param name="userId">The user ID requesting the draft (for audit).</param>
    /// <param name="options">Optional parameters for draft generation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The auto-generated draft response.</returns>
    Task<AutoDraftResult> GenerateAutoDraftAsync(
        Guid noticeId,
        Guid organizationId,
        Guid userId,
        AutoDraftOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Allowed tone values for auto-draft generation.
/// </summary>
public static class AutoDraftTone
{
    public const string Formal = "formal";
    public const string Conciliatory = "conciliatory";
    public const string Defensive = "defensive";

    public static readonly HashSet<string> AllowedValues = new(StringComparer.OrdinalIgnoreCase)
    {
        Formal, Conciliatory, Defensive
    };

    public static bool IsValid(string? tone) =>
        string.IsNullOrEmpty(tone) || AllowedValues.Contains(tone);
}

/// <summary>
/// Allowed language values for auto-draft generation.
/// </summary>
public static class AutoDraftLanguage
{
    public const string English = "en";
    public const string Hindi = "hi";

    public static readonly HashSet<string> AllowedValues = new(StringComparer.OrdinalIgnoreCase)
    {
        English, Hindi
    };

    public static bool IsValid(string? language) =>
        string.IsNullOrEmpty(language) || AllowedValues.Contains(language);
}

/// <summary>
/// Options for auto-draft generation.
/// </summary>
public record AutoDraftOptions
{
    /// <summary>
    /// Tone of the response: formal, conciliatory, or defensive.
    /// </summary>
    public string Tone { get; init; } = AutoDraftTone.Formal;

    /// <summary>
    /// Language for the response: en or hi.
    /// </summary>
    public string Language { get; init; } = AutoDraftLanguage.English;

    /// <summary>
    /// Include specific points to address (optional). Max 10 items, 500 chars each.
    /// </summary>
    [MaxLength(10)]
    public List<string>? PointsToAddress { get; init; }

    /// <summary>
    /// Additional context or instructions for the AI. Max 1000 characters.
    /// </summary>
    [MaxLength(1000)]
    public string? AdditionalInstructions { get; init; }

    /// <summary>
    /// Validates the options and returns any validation errors.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (!AutoDraftTone.IsValid(Tone))
        {
            errors.Add($"Invalid tone '{Tone}'. Allowed values: {string.Join(", ", AutoDraftTone.AllowedValues)}");
        }

        if (!AutoDraftLanguage.IsValid(Language))
        {
            errors.Add($"Invalid language '{Language}'. Allowed values: {string.Join(", ", AutoDraftLanguage.AllowedValues)}");
        }

        if (PointsToAddress != null)
        {
            if (PointsToAddress.Count > 10)
            {
                errors.Add("Maximum 10 points to address allowed");
            }

            for (int i = 0; i < PointsToAddress.Count; i++)
            {
                if (PointsToAddress[i]?.Length > 500)
                {
                    errors.Add($"Point {i + 1} exceeds maximum length of 500 characters");
                }
            }
        }

        if (AdditionalInstructions?.Length > 1000)
        {
            errors.Add("Additional instructions exceed maximum length of 1000 characters");
        }

        return errors;
    }

    /// <summary>
    /// Sanitizes the options to remove potentially harmful content.
    /// </summary>
    public AutoDraftOptions Sanitize()
    {
        return this with
        {
            Tone = AutoDraftTone.IsValid(Tone) ? Tone : AutoDraftTone.Formal,
            Language = AutoDraftLanguage.IsValid(Language) ? Language : AutoDraftLanguage.English,
            PointsToAddress = PointsToAddress?
                .Take(10)
                .Select(p => SanitizeText(p, 500))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)  // Non-null after Where filter
                .ToList(),
            AdditionalInstructions = SanitizeText(AdditionalInstructions, 1000)
        };
    }

    private static string? SanitizeText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Truncate to max length
        var sanitized = text.Length > maxLength ? text[..maxLength] : text;

        // Remove any potential prompt injection markers
        sanitized = sanitized
            .Replace("```", "")
            .Replace("##", "")
            .Replace("**", "")
            .Replace("{{", "{")
            .Replace("}}", "}")
            .Trim();

        return sanitized;
    }
}

/// <summary>
/// Result of auto-draft generation.
/// </summary>
public record AutoDraftResult
{
    public bool Success { get; init; }
    public string? DraftContent { get; init; }
    public AutoDraftMetadata? Metadata { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static AutoDraftResult Failure(string errorCode, string errorMessage)
        => new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };

    public static AutoDraftResult Ok(string draftContent, AutoDraftMetadata metadata)
        => new() { Success = true, DraftContent = draftContent, Metadata = metadata };
}

/// <summary>
/// Metadata about the auto-draft generation.
/// </summary>
public record AutoDraftMetadata
{
    public string Model { get; init; } = string.Empty;
    public int ProcessingTimeMs { get; init; }
    public string? NoticeType { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal EstimatedCost { get; init; }
}
