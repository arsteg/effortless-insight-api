using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

// Only durable decision metadata; no mailbox body or token is retained.
public class EmailImportReceipt : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Guid ConnectedEmailAccountId { get; set; }
    [MaxLength(64)] public string ItemKey { get; set; } = "";
    [MaxLength(64)] public string? AttachmentHash { get; set; }
    [MaxLength(30)] public string Outcome { get; set; } = "pending";
    [MaxLength(500)] public string? Message { get; set; }
    public Guid? NoticeId { get; set; }
    public bool AiUsed { get; set; }
}
