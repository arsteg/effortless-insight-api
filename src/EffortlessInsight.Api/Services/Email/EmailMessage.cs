using System.Collections.ObjectModel;

namespace EffortlessInsight.Api.Services.Email;

/// <summary>
/// Provider-agnostic outbound email. Immutable; construct with an object
/// initializer or <see cref="Single"/>.
/// </summary>
public sealed record EmailMessage
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Primary recipients. At least one address is required.</summary>
    public required IReadOnlyList<string> To { get; init; }

    public IReadOnlyList<string> Cc { get; init; } = [];

    public IReadOnlyList<string> Bcc { get; init; } = [];

    public required string Subject { get; init; }

    public string? HtmlBody { get; init; }

    /// <summary>
    /// Plain-text body. When null and <see cref="HtmlBody"/> is set, a
    /// text fallback is generated automatically at send time.
    /// </summary>
    public string? TextBody { get; init; }

    /// <summary>Overrides the configured default reply-to address.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>Custom SMTP headers (e.g. correlation ids).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = Empty;

    /// <summary>
    /// Provider message tags for analytics/event filtering. Names and values
    /// are sanitized to the provider's allowed character set at send time.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = Empty;

    public IReadOnlyList<EmailMessageAttachment> Attachments { get; init; } = [];

    /// <summary>Creates a message for a single recipient.</summary>
    public static EmailMessage Single(string to, string subject, string? htmlBody, string? textBody = null) =>
        new()
        {
            To = [to],
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody
        };
}

/// <summary>An attachment on an outbound email.</summary>
public sealed record EmailMessageAttachment(
    string FileName,
    string ContentType,
    ReadOnlyMemory<byte> Content
);

/// <summary>Result of a successful send.</summary>
/// <param name="MessageId">The provider-assigned message id (SES MessageId).</param>
public sealed record EmailSendResult(string MessageId);
