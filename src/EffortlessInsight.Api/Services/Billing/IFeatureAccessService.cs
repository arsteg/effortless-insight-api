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
    /// Requires: understand_respond, team, enterprise, or ca_operator plan.
    /// </summary>
    public const string AiExplanation = "ai_explanation";

    /// <summary>
    /// AI-generated draft reply for notices.
    /// Requires: understand_respond, team, enterprise, or ca_operator plan.
    /// </summary>
    public const string DraftReply = "draft_reply";

    /// <summary>
    /// WhatsApp assistant for interacting with notices.
    /// Requires: understand_respond, team, enterprise, or ca_operator plan.
    /// </summary>
    public const string WhatsAppAssistant = "whatsapp_assistant";

    /// <summary>
    /// Legacy feature code for full AI analysis. Maps to ai_explanation + draft_reply.
    /// </summary>
    public const string FullAiAnalysis = "full_ai_analysis";

    // ============================================================================
    // Team & Collaboration Features (Team plan and above)
    // ============================================================================

    /// <summary>
    /// Team collaboration features.
    /// Requires: team, enterprise, or ca_operator plan.
    /// </summary>
    public const string Collaboration = "collaboration";

    /// <summary>
    /// Custom user roles and permissions.
    /// Requires: team, enterprise, or ca_operator plan.
    /// </summary>
    public const string CustomRoles = "custom_roles";

    /// <summary>
    /// Audit trail for compliance tracking.
    /// Requires: team, enterprise, or ca_operator plan.
    /// </summary>
    public const string AuditTrail = "audit_trail";

    /// <summary>
    /// Advanced analytics and reporting.
    /// Requires: team, enterprise, or ca_operator plan.
    /// </summary>
    public const string AdvancedAnalytics = "advanced_analytics";

    // ============================================================================
    // Enterprise Features
    // ============================================================================

    /// <summary>
    /// Single sign-on integration.
    /// Requires: enterprise plan.
    /// </summary>
    public const string Sso = "sso";

    /// <summary>
    /// API access for integrations.
    /// Requires: enterprise or ca_operator plan.
    /// </summary>
    public const string ApiAccess = "api_access";

    /// <summary>
    /// Custom workflows.
    /// Requires: enterprise or ca_operator plan.
    /// </summary>
    public const string Workflows = "workflows";

    /// <summary>
    /// SLA guarantee.
    /// Requires: enterprise plan.
    /// </summary>
    public const string SlaGuarantee = "sla_guarantee";

    /// <summary>
    /// Priority support.
    /// Requires: enterprise or ca_operator plan.
    /// </summary>
    public const string PrioritySupport = "priority_support";

    /// <summary>
    /// Dedicated account manager.
    /// Requires: enterprise plan.
    /// </summary>
    public const string DedicatedAccountManager = "dedicated_account_manager";

    /// <summary>
    /// Custom integrations.
    /// Requires: enterprise plan.
    /// </summary>
    public const string CustomIntegrations = "custom_integrations";

    // ============================================================================
    // CA-specific Features
    // ============================================================================

    /// <summary>
    /// CA client management features.
    /// Requires: ca_operator plan.
    /// </summary>
    public const string CaClientManagement = "ca_client_management";

    // ============================================================================
    // Legacy Feature Codes (for backwards compatibility)
    // ============================================================================

    public const string WhatsAppIntegration = "whatsapp_integration";
    public const string AdvancedWorkflows = "advanced_workflows";
    public const string CustomBranding = "custom_branding";
    public const string SsoIntegration = "sso_integration";
    public const string AuditLogs = "audit_logs";
    public const string BulkOperations = "bulk_operations";
    public const string AdvancedReporting = "advanced_reporting";
    public const string DataExport = "data_export";
    public const string PriorityProcessing = "priority_processing";

    // ============================================================================
    // Additional/Miscellaneous Features
    // ============================================================================

    /// <summary>
    /// Multilingual support for notice translation.
    /// Requires: understand_respond plan and above.
    /// </summary>
    public const string MultilingualSupport = "multilingual_support";
}
