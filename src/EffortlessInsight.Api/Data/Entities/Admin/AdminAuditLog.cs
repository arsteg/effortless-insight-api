using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Comprehensive audit log for all admin actions.
/// Immutable - entries cannot be modified or deleted.
/// </summary>
public class AdminAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Admin who performed the action
    /// </summary>
    public Guid AdminUserId { get; set; }

    [ForeignKey(nameof(AdminUserId))]
    public AdminUser AdminUser { get; set; } = null!;

    /// <summary>
    /// Action performed: user.suspend, billing.refund, impersonation.start, etc.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity affected: user, organization, subscription, etc.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TargetType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the affected entity
    /// </summary>
    [MaxLength(100)]
    public string? TargetId { get; set; }

    /// <summary>
    /// Human-readable description of the action
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Additional details as JSON (old values, new values, etc.)
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Details { get; set; } = new();

    /// <summary>
    /// Outcome of the action: success, failure, pending
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Outcome { get; set; } = "success";

    /// <summary>
    /// Error message if action failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// IP address of the admin
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Session ID for correlation
    /// </summary>
    [MaxLength(100)]
    public string? SessionId { get; set; }

    /// <summary>
    /// Request ID for tracing
    /// </summary>
    [MaxLength(100)]
    public string? RequestId { get; set; }

    /// <summary>
    /// Duration of the action in milliseconds
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Timestamp of the action
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Common audit action constants
/// </summary>
public static class AdminAuditActions
{
    // Authentication
    public const string Login = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string Logout = "auth.logout";
    public const string MfaEnabled = "auth.mfa_enabled";
    public const string MfaDisabled = "auth.mfa_disabled";
    public const string PasswordChanged = "auth.password_changed";

    // Users
    public const string UserViewed = "user.viewed";
    public const string UserSuspended = "user.suspended";
    public const string UserUnsuspended = "user.unsuspended";
    public const string UserDeleted = "user.deleted";
    public const string UserPasswordReset = "user.password_reset";

    // Impersonation
    public const string ImpersonationStarted = "impersonation.started";
    public const string ImpersonationEnded = "impersonation.ended";

    // Organizations
    public const string OrgViewed = "organization.viewed";
    public const string OrgUpdated = "organization.updated";
    public const string OrgDeleted = "organization.deleted";
    public const string OrgCreditApplied = "organization.credit_applied";

    // Billing
    public const string SubscriptionViewed = "subscription.viewed";
    public const string RefundProcessed = "billing.refund_processed";
    public const string PlanOverridden = "billing.plan_overridden";
    public const string InvoiceViewed = "invoice.viewed";

    // AI Operations
    public const string AiJobRetried = "ai.job_retried";
    public const string PromptUpdated = "ai.prompt_updated";

    // Content
    public const string ContentCreated = "content.created";
    public const string ContentUpdated = "content.updated";
    public const string ContentPublished = "content.published";
    public const string ContentDeleted = "content.deleted";

    // System
    public const string AlertAcknowledged = "system.alert_acknowledged";
    public const string AlertResolved = "system.alert_resolved";
    public const string SettingChanged = "system.setting_changed";

    // Admin Management
    public const string AdminCreated = "admin.created";
    public const string AdminUpdated = "admin.updated";
    public const string AdminDeleted = "admin.deleted";
    public const string AdminRoleChanged = "admin.role_changed";
    public const string AdminSuspended = "admin.suspended";
    public const string AdminReactivated = "admin.reactivated";
    public const string PasswordResetByAdmin = "admin.password_reset";
    public const string MfaDisabledByAdmin = "admin.mfa_disabled";

    // Organizations (full names)
    public const string OrganizationUpdated = "organization.updated";
    public const string OrganizationSuspended = "organization.suspended";
    public const string OrganizationUnsuspended = "organization.unsuspended";
    public const string OrganizationDeleted = "organization.deleted";
    public const string CreditApplied = "organization.credit_applied";

    // Users
    public const string PasswordResetRequested = "user.password_reset_requested";

    // Billing
    public const string SubscriptionPlanOverridden = "billing.plan_overridden";

    // Audit
    public const string AuditExported = "audit.exported";
}

/// <summary>
/// Audit target type constants
/// </summary>
public static class AuditTargetTypes
{
    public const string User = "user";
    public const string Organization = "organization";
    public const string Subscription = "subscription";
    public const string Invoice = "invoice";
    public const string Payment = "payment";
    public const string AiJob = "ai_job";
    public const string Prompt = "prompt";
    public const string Content = "content";
    public const string Alert = "alert";
    public const string Setting = "setting";
    public const string AdminUser = "admin_user";
    public const string ImpersonationSession = "impersonation_session";
    public const string AuditLog = "audit_log";
}

/// <summary>
/// Audit outcome constants
/// </summary>
public static class AuditOutcomes
{
    public const string Success = "success";
    public const string Failure = "failure";
    public const string Pending = "pending";
}
