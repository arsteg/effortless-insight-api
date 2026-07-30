using System.Net.Mail;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.Email;

/// <summary>
/// Amazon SES v2 implementation of <see cref="IEmailService"/>.
/// Uses the SES SendEmail API (not SMTP). Transient throttling errors are
/// retried by the AWS SDK's standard retry mode; permanent rejections are
/// surfaced as <see cref="EmailDeliveryException"/>.
/// </summary>
public sealed class AmazonSesEmailService : IEmailService
{
    private const string Utf8 = "UTF-8";

    private readonly IAmazonSimpleEmailServiceV2 _ses;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly EmailOptions _options;
    private readonly ILogger<AmazonSesEmailService> _logger;

    public AmazonSesEmailService(
        IAmazonSimpleEmailServiceV2 ses,
        IEmailTemplateRenderer templateRenderer,
        IOptions<EmailOptions> options,
        ILogger<AmazonSesEmailService> logger)
    {
        _ses = ses;
        _templateRenderer = templateRenderer;
        _options = options.Value;
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default) =>
        SendAsync(EmailMessage.Single(to, subject, htmlBody), cancellationToken);

    public Task<EmailSendResult> SendTemplateAsync(string to, string templateId, IReadOnlyDictionary<string, object> data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var content = _templateRenderer.Render(templateId, data);
        return SendAsync(EmailMessage.Single(to, content.Subject, content.HtmlBody), cancellationToken);
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message);

        var request = BuildRequest(message);
        var recipient = message.To[0];

        _logger.LogInformation(
            "Sending email to {Recipient} ({RecipientCount} recipients) with subject {Subject}",
            recipient, message.To.Count + message.Cc.Count + message.Bcc.Count, message.Subject);

        try
        {
            var response = await _ses.SendEmailAsync(request, cancellationToken);

            _logger.LogInformation(
                "Email sent to {Recipient} with subject {Subject}, SES MessageId {MessageId}",
                recipient, message.Subject, response.MessageId);

            return new EmailSendResult(response.MessageId);
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            _logger.LogError(ex,
                "SES rejected email to {Recipient} with subject {Subject}: {ErrorCode} {ErrorMessage}",
                recipient, message.Subject, ex.ErrorCode, ex.Message);

            throw new EmailDeliveryException(
                $"Amazon SES rejected the email to '{recipient}': {ex.Message}",
                ex.ErrorCode, ex);
        }
        catch (AmazonServiceException ex)
        {
            _logger.LogError(ex,
                "Failed to reach Amazon SES while sending email to {Recipient} with subject {Subject}",
                recipient, message.Subject);

            throw new EmailDeliveryException(
                $"Failed to reach Amazon SES while sending email to '{recipient}': {ex.Message}",
                ex.ErrorCode, ex);
        }
    }

    private void Validate(EmailMessage message)
    {
        if (message.To.Count == 0)
        {
            throw new EmailDeliveryException("Email message has no recipients.");
        }

        foreach (var address in message.To.Concat(message.Cc).Concat(message.Bcc))
        {
            if (!MailAddress.TryCreate(address, out _))
            {
                throw new EmailDeliveryException($"Invalid recipient email address '{address}'.");
            }
        }

        var replyTo = message.ReplyTo ?? _options.ReplyToAddress;
        if (!string.IsNullOrEmpty(replyTo) && !MailAddress.TryCreate(replyTo, out _))
        {
            throw new EmailDeliveryException($"Invalid reply-to email address '{replyTo}'.");
        }

        if (string.IsNullOrWhiteSpace(message.Subject))
        {
            throw new EmailDeliveryException("Email message has no subject.");
        }

        if (string.IsNullOrWhiteSpace(message.HtmlBody) && string.IsNullOrWhiteSpace(message.TextBody))
        {
            throw new EmailDeliveryException("Email message has neither an HTML nor a text body.");
        }
    }

    private SendEmailRequest BuildRequest(EmailMessage message)
    {
        var textBody = message.TextBody;
        if (string.IsNullOrWhiteSpace(textBody) && !string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            textBody = HtmlToText.Convert(message.HtmlBody);
        }

        var body = new Body();
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            body.Html = new Content { Data = message.HtmlBody, Charset = Utf8 };
        }
        if (!string.IsNullOrWhiteSpace(textBody))
        {
            body.Text = new Content { Data = textBody, Charset = Utf8 };
        }

        var simple = new Message
        {
            Subject = new Content { Data = message.Subject, Charset = Utf8 },
            Body = body
        };

        if (message.Headers.Count > 0)
        {
            simple.Headers = message.Headers
                .Select(static h => new MessageHeader { Name = h.Key, Value = h.Value })
                .ToList();
        }

        if (message.Attachments.Count > 0)
        {
            simple.Attachments = message.Attachments
                .Select(static a => new Amazon.SimpleEmailV2.Model.Attachment
                {
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    RawContent = new MemoryStream(a.Content.ToArray())
                })
                .ToList();
        }

        var request = new SendEmailRequest
        {
            FromEmailAddress = string.IsNullOrWhiteSpace(_options.FromName)
                ? _options.FromAddress
                : $"{_options.FromName} <{_options.FromAddress}>",
            Destination = new Destination
            {
                ToAddresses = message.To.ToList(),
                CcAddresses = message.Cc.Count > 0 ? message.Cc.ToList() : null,
                BccAddresses = message.Bcc.Count > 0 ? message.Bcc.ToList() : null
            },
            Content = new EmailContent { Simple = simple }
        };

        var replyTo = message.ReplyTo ?? _options.ReplyToAddress;
        if (!string.IsNullOrEmpty(replyTo))
        {
            request.ReplyToAddresses = [replyTo];
        }

        if (!string.IsNullOrEmpty(_options.ConfigurationSetName))
        {
            request.ConfigurationSetName = _options.ConfigurationSetName;
        }

        if (message.Tags.Count > 0)
        {
            request.EmailTags = message.Tags
                .Select(static t => new MessageTag { Name = SanitizeTag(t.Key), Value = SanitizeTag(t.Value) })
                .ToList();
        }

        return request;
    }

    /// <summary>SES message tags allow only alphanumerics, '_' and '-', max 256 chars.</summary>
    private static string SanitizeTag(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 256)];
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = value[i];
            buffer[i] = char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_';
        }
        return new string(buffer);
    }
}
