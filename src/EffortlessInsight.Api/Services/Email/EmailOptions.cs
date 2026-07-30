using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Services.Email;

/// <summary>
/// Outbound email configuration, bound from the "Email" section.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Sender address. Must belong to an SES verified identity (domain or
    /// address) in the configured region.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Display name used in the From header.</summary>
    public string FromName { get; set; } = "EffortlessInsight";

    /// <summary>Default reply-to address; individual messages can override it.</summary>
    [EmailAddress]
    public string? ReplyToAddress
    {
        get => _replyToAddress;
        // Config files use "" for "not set"; [EmailAddress] only accepts null.
        set => _replyToAddress = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string? _replyToAddress;

    /// <summary>
    /// AWS region for the SES client. Falls back to AWS:Region when empty.
    /// </summary>
    public string? AwsRegion { get; set; }

    /// <summary>
    /// Dedicated access key for the SES client (e.g. an IAM user limited to
    /// ses:SendEmail). Falls back to AWS:AccessKeyId when empty, so S3 and
    /// email can use separate credentials.
    /// </summary>
    public string? AwsAccessKeyId { get; set; }

    /// <summary>Secret for <see cref="AwsAccessKeyId"/>. Falls back to AWS:SecretAccessKey.</summary>
    public string? AwsSecretAccessKey { get; set; }

    /// <summary>
    /// SES configuration set applied to every send. Required for delivery,
    /// open, click, bounce and complaint event tracking.
    /// </summary>
    public string? ConfigurationSetName { get; set; }

    /// <summary>
    /// SNS topic ARN that delivers SES events to the /webhooks/ses endpoint.
    /// When set, inbound webhook calls from any other topic are rejected.
    /// </summary>
    public string? EventTopicArn { get; set; }
}
