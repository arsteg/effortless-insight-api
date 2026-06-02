using System.ComponentModel.DataAnnotations;
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

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "member"; // owner, admin, manager, member, ca, viewer

    public bool IsActive { get; set; } = true;

    public bool IsMobileVerified { get; set; }

    public DateTime? LastLogin { get; set; }

    public Dictionary<string, object>? Preferences { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public ICollection<Notice> UploadedNotices { get; set; } = [];
    public ICollection<Notice> AssignedNotices { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
    public ICollection<NoticeTask> CreatedTasks { get; set; } = [];
    public ICollection<NoticeTask> AssignedTasks { get; set; } = [];
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() : base() { }
    public ApplicationRole(string roleName) : base(roleName) { }
}
