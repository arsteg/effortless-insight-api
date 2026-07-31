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
/// </summary>
public static class FeatureCodes
{
    public const string WhatsAppIntegration = "whatsapp_integration";
    public const string Workflows = "workflows";
    public const string AdvancedWorkflows = "advanced_workflows";
    public const string ApiAccess = "api_access";
    public const string AdvancedAnalytics = "advanced_analytics";
    public const string PrioritySupport = "priority_support";
    public const string CustomBranding = "custom_branding";
    public const string SsoIntegration = "sso_integration";
    public const string AuditLogs = "audit_logs";
    public const string BulkOperations = "bulk_operations";
    public const string AdvancedReporting = "advanced_reporting";
    public const string DataExport = "data_export";
    public const string FullAiAnalysis = "full_ai_analysis";
    public const string PriorityProcessing = "priority_processing";
}
