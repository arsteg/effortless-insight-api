using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Represents an administrator user with elevated privileges.
/// Separate from ApplicationUser to maintain security isolation.
/// </summary>
public class AdminUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(255)]
    public string EmailNormalized { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Admin role: super_admin, operations_admin, finance_admin, support_admin, content_admin
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "support_admin";

    /// <summary>
    /// Granular permissions as JSON array
    /// </summary>
    public List<string> Permissions { get; set; } = [];

    public bool IsActive { get; set; } = true;

    // MFA/TOTP
    public bool MfaEnabled { get; set; } = false;

    /// <summary>
    /// Encrypted TOTP secret for MFA
    /// </summary>
    public byte[]? MfaSecretEncrypted { get; set; }

    /// <summary>
    /// Hashed backup codes for MFA recovery
    /// </summary>
    public string[]? BackupCodesHash { get; set; }

    // Security
    public bool IsLocked { get; set; }

    public DateTime? LockedUntil { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTime? LastFailedLoginAt { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public bool MustChangePassword { get; set; }

    // IP Whitelisting (optional, JSON array of CIDRs)
    public List<string>? IpWhitelist { get; set; }

    // Activity tracking
    public DateTime? LastLoginAt { get; set; }

    [MaxLength(45)]
    public string? LastLoginIp { get; set; }

    public string? LastLoginUserAgent { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    [InverseProperty(nameof(AdminAuditLog.AdminUser))]
    public ICollection<AdminAuditLog> AuditLogs { get; set; } = [];

    [InverseProperty(nameof(ImpersonationSession.AdminUser))]
    public ICollection<ImpersonationSession> ImpersonationSessions { get; set; } = [];

    [InverseProperty(nameof(OrganizationCredit.GrantedBy))]
    public ICollection<OrganizationCredit> GrantedCredits { get; set; } = [];

    [InverseProperty(nameof(AdminSession.AdminUser))]
    public ICollection<AdminSession> Sessions { get; set; } = [];
}

/// <summary>
/// Admin role constants
/// </summary>
public static class AdminRoles
{
    public const string SuperAdmin = "super_admin";
    public const string OperationsAdmin = "operations_admin";
    public const string FinanceAdmin = "finance_admin";
    public const string SupportAdmin = "support_admin";
    public const string ContentAdmin = "content_admin";

    public static readonly string[] All =
    [
        SuperAdmin,
        OperationsAdmin,
        FinanceAdmin,
        SupportAdmin,
        ContentAdmin
    ];

    public static bool IsValid(string role) => All.Contains(role);
}

/// <summary>
/// Admin permission constants
/// </summary>
public static class AdminPermissions
{
    // Dashboard
    public const string DashboardView = "dashboard:view";
    public const string DashboardExport = "dashboard:export";

    // Users
    public const string UsersView = "users:view";
    public const string UsersSuspend = "users:suspend";
    public const string UsersDelete = "users:delete";
    public const string UsersImpersonate = "users:impersonate";
    public const string UsersResetPassword = "users:reset_password";

    // Organizations
    public const string OrganizationsView = "organizations:view";
    public const string OrganizationsUpdate = "organizations:update";
    public const string OrganizationsDelete = "organizations:delete";
    public const string OrganizationsCredits = "organizations:credits";

    // Billing
    public const string BillingView = "billing:view";
    public const string BillingRefund = "billing:refund";
    public const string BillingOverride = "billing:override";
    public const string BillingCredits = "billing:credits";

    // AI Operations
    public const string AiOpsView = "ai_ops:view";
    public const string AiOpsRetry = "ai_ops:retry";
    public const string AiOpsPrompts = "ai_ops:prompts";

    // Audit
    public const string AuditView = "audit:view";
    public const string AuditExport = "audit:export";

    // Content
    public const string ContentView = "content:view";
    public const string ContentEdit = "content:edit";
    public const string ContentPublish = "content:publish";

    // Settings & Admin Management
    public const string SettingsView = "settings:view";
    public const string SettingsUpdate = "settings:update";
    public const string AdminManage = "admin:manage";

    // System
    public const string SystemHealth = "system:health";
    public const string SystemAlerts = "system:alerts";

    /// <summary>
    /// Get default permissions for a role
    /// </summary>
    public static List<string> GetDefaultPermissions(string role) => role switch
    {
        AdminRoles.SuperAdmin => [
            DashboardView, DashboardExport,
            UsersView, UsersSuspend, UsersDelete, UsersImpersonate, UsersResetPassword,
            OrganizationsView, OrganizationsUpdate, OrganizationsDelete, OrganizationsCredits,
            BillingView, BillingRefund, BillingOverride, BillingCredits,
            AiOpsView, AiOpsRetry, AiOpsPrompts,
            AuditView, AuditExport,
            ContentView, ContentEdit, ContentPublish,
            SettingsView, SettingsUpdate, AdminManage,
            SystemHealth, SystemAlerts
        ],
        AdminRoles.OperationsAdmin => [
            DashboardView,
            UsersView, UsersSuspend, UsersImpersonate,
            OrganizationsView, OrganizationsUpdate,
            AiOpsView, AiOpsRetry,
            AuditView,
            SystemHealth, SystemAlerts
        ],
        AdminRoles.FinanceAdmin => [
            DashboardView, DashboardExport,
            UsersView,
            OrganizationsView, OrganizationsCredits,
            BillingView, BillingRefund, BillingOverride, BillingCredits,
            AuditView
        ],
        AdminRoles.SupportAdmin => [
            DashboardView,
            UsersView, UsersImpersonate,
            OrganizationsView,
            AuditView
        ],
        AdminRoles.ContentAdmin => [
            DashboardView,
            ContentView, ContentEdit, ContentPublish,
            AuditView
        ],
        _ => [DashboardView, AuditView]
    };
}
