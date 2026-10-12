namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Service for detecting GST notices in emails with minimal AI usage.
/// </summary>
public interface INoticeDetectionService
{
    /// <summary>
    /// Analyzes an email to detect if it contains a GST notice.
    /// </summary>
    Task<NoticeDetectionResult> DetectNoticeAsync(
        EmailMessage email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts GSTIN from email content.
    /// </summary>
    string? ExtractGstin(string text);

    /// <summary>
    /// Checks if attachment is a potential notice document.
    /// </summary>
    bool IsPotentialNoticeAttachment(EmailAttachment attachment);
}

/// <summary>
/// Result of notice detection analysis.
/// </summary>
public record NoticeDetectionResult
{
    /// <summary>
    /// Whether a GST notice was detected.
    /// </summary>
    public bool IsNotice { get; init; }

    /// <summary>
    /// Confidence score (0.0 - 1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Detection method used: "sender", "keyword", "gstin", "ai".
    /// </summary>
    public string DetectionMethod { get; init; } = string.Empty;

    /// <summary>
    /// Extracted GSTIN if found.
    /// </summary>
    public string? ExtractedGstin { get; init; }

    /// <summary>
    /// Detected notice type (DRC-01, ASMT-10, etc.).
    /// </summary>
    public string? DetectedNoticeType { get; init; }

    /// <summary>
    /// Indices of attachments that appear to be notice documents.
    /// </summary>
    public List<int> NoticeAttachmentIndices { get; init; } = [];

    /// <summary>
    /// Whether AI fallback was used.
    /// </summary>
    public bool AiUsed { get; init; }

    /// <summary>
    /// Reason for the detection decision.
    /// </summary>
    public string? Reason { get; init; }
}
