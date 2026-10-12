namespace EffortlessInsight.Api.Services.Notices;

public record EmailIngestionContext(Guid AccountId, string Provider, string ConnectedEmail,
    string MessageId, string AttachmentId, string Sender, string Subject, DateTime ReceivedAt,
    string AttachmentHash, string? ReferenceNumber = null);
