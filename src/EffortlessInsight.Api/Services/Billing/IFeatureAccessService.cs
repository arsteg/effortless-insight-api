namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for checking feature access based on subscription plan.
/// </summary>
public interface IFeatureAccessService
{
    /// <summary>
    /// Checks if the organization has access to a specific feature.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="featureCode">The feature code (e.g., "whatsapp_integration", "workflows")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the feature is available, false otherwise</returns>
    Task<bool> HasFeatureAccessAsync(Guid organizationId, string featureCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all features available to the organization.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of feature codes available to the organization</returns>
    Task<List<string>> GetAvailableFeaturesAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates feature access and throws if not available.
    /// </summary>
    /// <param name="organizationId">The organization ID</param>
    /// <param name="featureCode">The feature code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="FeatureNotAvailableException">Thrown when feature is not available</exception>
    Task RequireFeatureAccessAsync(Guid organizationId, string featureCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached feature list for an organization (call after a plan
    /// change or a Free CA Access grant/revoke, so the change takes effect immediately
    /// rather than waiting out the cache TTL).
    /// </summary>
    Task InvalidateCacheAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Exception thrown when a feature is not available for the organization's plan.
/// </summary>
public class FeatureNotAvailableException : Exception
{
    public string FeatureCode { get; }
    public string? RequiredPlan { get; }

    public FeatureNotAvailableException(string featureCode, string? requiredPlan = null)
        : base($"Feature '{featureCode}' is not available in your current plan.{(requiredPlan != null ? $" Upgrade to {requiredPlan} to access this feature." : " Please upgrade your plan to access this feature.")}")
    {
        FeatureCode = featureCode;
        RequiredPlan = requiredPlan;
    }
}

/// <summary>
/// Known feature codes used in the system.
/// Clean set of 15 technical features (no duplicates, no marketing features).
/// </summary>
public static class FeatureCodes
{
    // ============================================================================
    // Core Features (Available on Free plan)
    // ============================================================================

    /// <summary>
    /// Notice detection and tracking. Available on all plans including free.
    /// </summary>
    public const string NoticeDetection = "notice_detection";

    /// <summary>
    /// Email notifications for deadline reminders. Available on all plans.
    /// </summary>
    public const string EmailNotifications = "email_notifications";

    /// <summary>
    /// Push notifications for real-time alerts. Available on all plans.
    /// </summary>
    public const string PushNotifications = "push_notifications";

    // ============================================================================
    // AI Features (Paid plans only)
    // ============================================================================

    /// <summary>
    /// AI-powered explanation of notices in plain language.
    /// </summary>
    public const string AiExplanation = "ai_explanation";

    /// <summary>
    /// AI-generated draft reply for notices.
    /// </summary>
    public const string DraftReply = "draft_reply";

    /// <summary>
    /// WhatsApp assistant for interacting with notices.
    /// </summary>
    public const string WhatsAppAssistant = "whatsapp_assistant";

    /// <summary>
    /// Multilingual support for notice translation.
    /// </summary>
    public const string MultilingualSupport = "multilingual_support";

    // ============================================================================
    // Team Features
    // ============================================================================

    /// <summary>
    /// Team collaboration features.
    /// </summary>
    public const string Collaboration = "collaboration";

    /// <summary>
    /// Advanced analytics and reporting.
    /// </summary>
    public const string AdvancedAnalytics = "advanced_analytics";

    // ============================================================================
    // Premium Features
    // ============================================================================

    /// <summary>
    /// Custom workflows for notice processing.
    /// </summary>
    public const string Workflows = "workflows";

    /// <summary>
    /// Bulk operations on notices.
    /// </summary>
    public const string BulkOperations = "bulk_operations";

    /// <summary>
    /// Data export functionality.
    /// </summary>
    public const string DataExport = "data_export";

    // ============================================================================
    // CA-specific Features
    // ============================================================================

    /// <summary>
    /// CA client management features.
    /// </summary>
    public const string CaClientManagement = "ca_client_management";

    // ============================================================================
    // Enterprise Features
    // ============================================================================

    /// <summary>
    /// Single sign-on integration.
    /// </summary>
    public const string Sso = "sso";

    /// <summary>
    /// API access for integrations.
    /// </summary>
    public const string ApiAccess = "api_access";
}
