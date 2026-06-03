using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

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

    public DateTime ExpiresAt { get; set; }

    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    [MaxLength(50)]
    public string? RevokedReason { get; set; } // logout, password_change, admin, security
}
