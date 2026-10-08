namespace EffortlessInsight.Api.Constants;

/// <summary>
/// Centralized error codes for consistent error handling across the API.
/// </summary>
public static class ErrorCodes
{
    // GSTIN Limit Errors
    public const string GstinLimitExceeded = "GSTIN_LIMIT_EXCEEDED";
    public const string GstinNotInOrganization = "GSTIN_NOT_IN_ORGANIZATION";

    // Subscription Errors
    public const string SubscriptionExpired = "SUBSCRIPTION_EXPIRED";
    public const string SubscriptionCancelled = "SUBSCRIPTION_CANCELLED";
    public const string SubscriptionRequired = "SUBSCRIPTION_REQUIRED";
    public const string TrialExpired = "TRIAL_EXPIRED";

    // Concurrency Errors
    public const string ConcurrentOperation = "CONCURRENT_OPERATION";

    // GSTIN Validation Errors
    public const string InvalidGstin = "INVALID_GSTIN";
    public const string GstinExists = "GSTIN_EXISTS";

    // General Errors
    public const string InternalError = "INTERNAL_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
}
