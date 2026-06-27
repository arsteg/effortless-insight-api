using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Tracks password history for admin users to prevent password reuse.
/// Stores the last N password hashes to enforce password history policy.
/// </summary>
public class AdminPasswordHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AdminUserId { get; set; }

    [ForeignKey(nameof(AdminUserId))]
    public AdminUser AdminUser { get; set; } = null!;

    /// <summary>
    /// BCrypt hash of the password
    /// </summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// When this password was set
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
