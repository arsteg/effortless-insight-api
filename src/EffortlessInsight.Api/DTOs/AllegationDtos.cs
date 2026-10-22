using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// Allegation-wise Draft DTOs
// ============================================================================

/// <summary>
/// Request for generating allegation-wise (point-wise) draft responses.
/// </summary>
public record AllegationWiseDraftRequest
{
    /// <summary>
    /// Tone of the responses: formal, conciliatory, or defensive. Default: formal
    /// </summary>
    /// <example>formal</example>
    [RegularExpression("^(formal|conciliatory|defensive)$",
        ErrorMessage = "Tone must be 'formal', 'conciliatory', or 'defensive'")]
    public string? Tone { get; init; }

    /// <summary>
    /// Language for the responses: en (English) or hi (Hindi). Default: en
    /// </summary>
    /// <example>en</example>
    [RegularExpression("^(en|hi)$",
        ErrorMessage = "Language must be 'en' or 'hi'")]
    public string? Language { get; init; }

    /// <summary>
    /// Custom instructions per allegation ID. Key is allegation ID (e.g., "ALG-001").
    /// </summary>
    public Dictionary<string, string>? AllegationOverrides { get; init; }

    /// <summary>
    /// Whether to include case law references in responses. Default: true
    /// </summary>
    public bool IncludeCaseLaw { get; init; } = true;

    /// <summary>
    /// Additional context about the taxpayer's situation.
    /// </summary>
    public Dictionary<string, object>? TaxpayerContext { get; init; }
}

/// <summary>
/// Response containing allegation-wise draft responses.
/// </summary>
public record AllegationWiseDraftResponse(
    /// <summary>List of extracted allegations from the notice.</summary>
    List<AllegationDto> Allegations,
    /// <summary>AI-generated responses for each allegation.</summary>
    List<AllegationResponseDto> Responses,
    /// <summary>Combined document containing all responses.</summary>
    string? CombinedDraft,
    /// <summary>Aggregated list of missing information across all allegations.</summary>
    List<string> MissingInfoSummary,
    /// <summary>Metadata about the generation process.</summary>
    AutoDraftMetadataDto Metadata,
    AllegationWiseDraftRequest? GenerationOptions = null);

/// <summary>
/// Individual allegation extracted from a GST notice.
/// </summary>
public record AllegationDto(
    /// <summary>Unique identifier (e.g., "ALG-001").</summary>
    string Id,
    /// <summary>Sequential number of the allegation in the notice.</summary>
    int SerialNumber,
    /// <summary>Full text of the allegation.</summary>
    string Description,
    /// <summary>Category of allegation (e.g., "Tax Evasion", "ITC Mismatch").</summary>
    string Category,
    /// <summary>Section/Rule cited (e.g., "Section 74(1)").</summary>
    string? LegalProvision,
    /// <summary>Tax/penalty amount for this specific allegation.</summary>
    decimal? AmountInvolved,
    /// <summary>Tax period covered (e.g., "FY 2022-23").</summary>
    string? Period,
    /// <summary>Documents/evidence cited by the department.</summary>
    List<string> EvidenceCited,
    /// <summary>Page/paragraph reference in the notice.</summary>
    string? PageReference);

/// <summary>
/// AI-generated response for a single allegation.
/// </summary>
public record AllegationResponseDto(
    /// <summary>ID of the allegation this responds to.</summary>
    string AllegationId,
    /// <summary>Draft reply text for this allegation.</summary>
    string ResponseText,
    /// <summary>Legal provisions supporting the response.</summary>
    List<string> LegalBasis,
    /// <summary>Documents supporting the response.</summary>
    List<SupportingDocumentDto> SupportingEvidence,
    /// <summary>Information/documents still needed.</summary>
    List<string> MissingInformation,
    /// <summary>Relevant case law citations.</summary>
    List<CaseLawReferenceDto>? CaseLawReferences,
    /// <summary>Confidence score (0.0 to 1.0).</summary>
    double Confidence,
    /// <summary>Status: "complete", "needs_review", or "missing_info".</summary>
    string Status);

/// <summary>
/// Supporting document reference for an allegation response.
/// </summary>
public record SupportingDocumentDto(
    /// <summary>Name of the supporting document.</summary>
    string DocumentName,
    /// <summary>Type of document (invoice, return, bank statement, etc.).</summary>
    string DocumentType,
    /// <summary>How this document supports the response.</summary>
    string Relevance,
    /// <summary>Whether the document is already available.</summary>
    bool Available);

/// <summary>
/// Case law citation for legal support.
/// </summary>
public record CaseLawReferenceDto(
    /// <summary>Case citation (e.g., "2023 (1) TMI 123 - CESTAT").</summary>
    string Citation,
    /// <summary>Court name (Supreme Court, High Court, CESTAT, etc.).</summary>
    string Court,
    /// <summary>Legal principle or ruling that applies.</summary>
    string Principle,
    /// <summary>How this case law supports the response.</summary>
    string Relevance);

// ============================================================================
// Allegation Extraction DTOs
// ============================================================================

/// <summary>
/// Result of extracting allegations from a notice.
/// </summary>
public record AllegationExtractionResult(
    /// <summary>Whether extraction succeeded.</summary>
    bool Success,
    /// <summary>Error message if extraction failed.</summary>
    string? Error,
    /// <summary>List of extracted allegations.</summary>
    List<AllegationDto> Allegations,
    /// <summary>Sum of all allegation amounts.</summary>
    decimal? TotalAmount,
    /// <summary>Processing metadata.</summary>
    AutoDraftMetadataDto? Metadata);

// ============================================================================
// Regenerate Single Allegation Response DTOs
// ============================================================================

/// <summary>
/// Request for regenerating a single allegation response.
/// </summary>
public record RegenerateAllegationResponseRequest
{
    /// <summary>
    /// The allegation to respond to.
    /// </summary>
    [Required]
    public required AllegationDto Allegation { get; init; }

    /// <summary>
    /// Tone of the response: formal, conciliatory, or defensive. Default: formal
    /// </summary>
    [RegularExpression("^(formal|conciliatory|defensive)$",
        ErrorMessage = "Tone must be 'formal', 'conciliatory', or 'defensive'")]
    public string? Tone { get; init; }

    /// <summary>
    /// Language for the response: en (English) or hi (Hindi). Default: en
    /// </summary>
    [RegularExpression("^(en|hi)$",
        ErrorMessage = "Language must be 'en' or 'hi'")]
    public string? Language { get; init; }

    /// <summary>
    /// Specific instructions for this allegation response.
    /// </summary>
    [MaxLength(1000, ErrorMessage = "Custom instructions cannot exceed 1000 characters")]
    public string? CustomInstructions { get; init; }

    /// <summary>
    /// Additional context about the taxpayer's situation.
    /// </summary>
    public Dictionary<string, object>? TaxpayerContext { get; init; }
}
