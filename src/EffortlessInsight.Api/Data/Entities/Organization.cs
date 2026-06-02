using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class Organization : BaseEntity
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public List<string> Gstins { get; set; } = [];

    [MaxLength(100)]
    public string? Industry { get; set; }

    [MaxLength(50)]
    public string? State { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    public string? Address { get; set; }

    [MaxLength(10)]
    public string? PinCode { get; set; }

    public decimal? AnnualTurnover { get; set; }

    public int? EmployeeCount { get; set; }

    [MaxLength(10)]
    public string? Pan { get; set; }

    public Guid? PlanId { get; set; }
    public Plan? Plan { get; set; }

    [MaxLength(20)]
    public string SubscriptionStatus { get; set; } = "trial";

    public DateTime? TrialEndsAt { get; set; }

    public Dictionary<string, object>? Settings { get; set; }

    // Navigation properties
    public ICollection<ApplicationUser> Users { get; set; } = [];
    public ICollection<Notice> Notices { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
