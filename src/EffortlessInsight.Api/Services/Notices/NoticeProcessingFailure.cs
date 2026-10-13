namespace EffortlessInsight.Api.Services.Notices;

public static class NoticeProcessingFailure
{
    public static bool RequiresCorrection(string? error) => error != null &&
        (error.StartsWith("INVALID_GSTIN:", StringComparison.Ordinal)
         || error.StartsWith("GSTIN_REVIEW_REQUIRED:", StringComparison.Ordinal)
         || error.StartsWith("GSTIN_MISMATCH:", StringComparison.Ordinal)
         || error.StartsWith("The document GSTIN does not match", StringComparison.Ordinal));

    // Do not expose database/transport exception details through the public API.
    public static string? UserMessage(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        if (error.StartsWith("INVALID_GSTIN:", StringComparison.Ordinal))
            return "The document GSTIN is not registered in this organization. Check the selected organization and its registered GSTINs, then retry analysis.";
        if (error.StartsWith("GSTIN_REVIEW_REQUIRED:", StringComparison.Ordinal))
            return "A valid GSTIN could not be identified in the document. Check the document and select the correct registered GSTIN before retrying.";
        if (error.StartsWith("GSTIN_MISMATCH:", StringComparison.Ordinal)
            || error.StartsWith("The document GSTIN does not match", StringComparison.Ordinal))
            return "The document GSTIN does not match the selected GSTIN. Correct the notice's GSTIN or upload the matching document before retrying.";
        if (error.StartsWith("DUPLICATE_NOTICE:", StringComparison.Ordinal))
            return "This notice already exists. Open the existing notice from the notices list.";
        return "Analysis could not finish. Retry analysis; if it fails again, contact support with the notice ID.";
    }
}
