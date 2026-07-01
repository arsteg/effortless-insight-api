using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace EffortlessInsight.Api.Data.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(15)]
    public string? Mobile { get; set; }

    [MaxLength(15)]
    public string? MobileNormalized { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "member"; // owner, admin, manager, member, ca, viewer

    public bool IsActive { get; set; } = true;

    public bool IsMobileVerified { get; set; }

    public DateTime? MobileVerifiedAt { get; set; }

    public bool IsEmailVerified { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    // Security fields
    public bool Is2faEnabled { get; set; }

    public byte[]? TotpSecretEncrypted { get; set; }

    public string[]? BackupCodesHash { get; set; }

    public bool IsLocked { get; set; }

    public DateTime? LockedUntil { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTime? LastFailedLoginAt { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public bool MustChangePassword { get; set; }

    // OAuth
    [MaxLength(50)]
    public string? OAuthProvider { get; set; }

    [MaxLength(255)]
    public string? OAuthProviderId { get; set; }

    [MaxLength(255)]
    public string? GoogleId { get; set; }

    [MaxLength(255)]
    public string? MicrosoftId { get; set; }

    // WhatsApp Integration
    /// <summary>
    /// WhatsApp phone number in E.164 format (e.g., 919876543210).
    /// </summary>
    [MaxLength(20)]
    public string? WhatsAppPhoneNumber { get; set; }

    /// <summary>
    /// Whether WhatsApp number is verified.
    /// </summary>
    public bool WhatsAppVerified { get; set; }

    /// <summary>
    /// When WhatsApp was verified.
    /// </summary>
    public DateTime? WhatsAppVerifiedAt { get; set; }

    /// <summary>
    /// Whether user has opted in for WhatsApp notifications.
    /// </summary>
    public bool WhatsAppOptedIn { get; set; }

    /// <summary>
    /// When user opted in for WhatsApp notifications.
    /// </summary>
    public DateTime? WhatsAppOptedInAt { get; set; }

    /// <summary>
    /// Last WhatsApp message timestamp (for 24h window tracking).
    /// </summary>
    public DateTime? WhatsAppLastMessageAt { get; set; }

    // Activity tracking
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Last activity timestamp for the user.
    /// </summary>
    public DateTime? LastActivityAt { get; set; }

    [MaxLength(45)]
    public string? LastLoginIp { get; set; }

    public string? LastLoginUserAgent { get; set; }

    /// <summary>
    /// Full name of the user (computed or stored).
    /// </summary>
    [NotMapped]
    public string FullName => Name ?? Email ?? "Unknown";

    // Preferences as JSON
    public Dictionary<string, object>? Preferences { get; set; }

    // Terms acceptance
    public bool TermsAccepted { get; set; }

    public DateTime? TermsAcceptedAt { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    [InverseProperty(nameof(Notice.UploadedBy))]
    public ICollection<Notice> UploadedNotices { get; set; } = [];

    [InverseProperty(nameof(Notice.AssignedTo))]
    public ICollection<Notice> AssignedNotices { get; set; } = [];

    [InverseProperty(nameof(Comment.User))]
    public ICollection<Comment> Comments { get; set; } = [];

    [InverseProperty(nameof(NoticeTask.CreatedBy))]
    public ICollection<NoticeTask> CreatedTasks { get; set; } = [];

    [InverseProperty(nameof(NoticeTask.AssignedTo))]
    public ICollection<NoticeTask> AssignedTasks { get; set; } = [];
    public ICollection<UserSession> Sessions { get; set; } = [];

    /// <summary>
    /// OAuth providers linked to this user (supports multiple providers).
    /// </summary>
    public ICollection<UserOAuthProvider> OAuthProviders { get; set; } = [];

    /// <summary>
    /// WhatsApp verifications for this user.
    /// </summary>
    public ICollection<WhatsAppVerification> WhatsAppVerifications { get; set; } = [];

    /// <summary>
    /// WhatsApp sessions for this user.
    /// </summary>
    public ICollection<WhatsAppSession> WhatsAppSessions { get; set; } = [];
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() : base() { }
    public ApplicationRole(string roleName) : base(roleName) { }
}
