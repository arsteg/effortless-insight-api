using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class LoginAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [MaxLength(255)]
    public string? EmailAttempted { get; set; }

    public bool Success { get; set; }

    [MaxLength(50)]
    public string? FailureReason { get; set; } // invalid_password, locked, disabled, 2fa_failed

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    public string? UserAgent { get; set; }

    [MaxLength(100)]
    public string? LocationCity { get; set; }

    [MaxLength(100)]
    public string? LocationCountry { get; set; }

    [Required]
    [MaxLength(20)]
    public string AuthMethod { get; set; } = "password"; // password, otp, google, 2fa

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
