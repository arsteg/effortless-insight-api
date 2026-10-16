using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    // Organization selected when these tokens were issued; retained across refreshes.
    public Guid? OrganizationId { get; set; }

    [Required]
    [MaxLength(64)]
    public string RefreshTokenHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(36)]
    public string RefreshTokenJti { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? DeviceId { get; set; }

    [MaxLength(255)]
    public string? DeviceName { get; set; }

    [Required]
    [MaxLength(20)]
    public string Platform { get; set; } = "web"; // web, ios, android

    public string? UserAgent { get; set; }

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LocationCity { get; set; }

    [MaxLength(100)]
    public string? LocationCountry { get; set; }

    // Device/browser metadata
    /// <summary>
    /// IANA timezone identifier for this session (e.g., "Asia/Kolkata").
    /// </summary>
    [MaxLength(50)]
    public string? Timezone { get; set; }

    /// <summary>
    /// Preferred language from Accept-Language header (e.g., "en-IN").
    /// </summary>
    [MaxLength(10)]
    public string? Language { get; set; }

    /// <summary>
    /// Operating system name parsed from User-Agent (e.g., "Windows 11", "macOS").
    /// </summary>
    [MaxLength(50)]
    public string? OsName { get; set; }

    /// <summary>
    /// Operating system version parsed from User-Agent.
    /// </summary>
    [MaxLength(20)]
    public string? OsVersion { get; set; }

    /// <summary>
    /// Browser name parsed from User-Agent (e.g., "Chrome", "Safari").
    /// </summary>
    [MaxLength(50)]
    public string? BrowserName { get; set; }

    /// <summary>
    /// Browser version parsed from User-Agent.
    /// </summary>
    [MaxLength(20)]
    public string? BrowserVersion { get; set; }

    /// <summary>
    /// Screen width in pixels.
    /// </summary>
    public int? ScreenWidth { get; set; }

    /// <summary>
    /// Screen height in pixels.
    /// </summary>
    public int? ScreenHeight { get; set; }

    /// <summary>
    /// Screen color depth in bits.
    /// </summary>
    public int? ColorDepth { get; set; }

    /// <summary>
    /// Whether the device supports touch input.
    /// </summary>
    public bool IsTouchDevice { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    [MaxLength(50)]
    public string? RevokedReason { get; set; } // logout, password_change, admin, security
}
