namespace EffortlessInsight.Api.Services.Email;

/// <summary>
/// Sends transactional email through the configured provider (Amazon SES).
/// All methods throw <see cref="EmailDeliveryException"/> when the provider
/// rejects the message or cannot be reached; callers for whom email is
/// best-effort should catch that exception and continue.
/// </summary>
public interface IEmailService
{
    /// <summary>Sends a fully-specified message.</summary>
    /// <exception cref="EmailDeliveryException">The message was not accepted by the provider.</exception>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Sends a simple HTML email to a single recipient.</summary>
    /// <exception cref="EmailDeliveryException">The message was not accepted by the provider.</exception>
    Task<EmailSendResult> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>Renders a named template and sends it to a single recipient.</summary>
    /// <exception cref="EmailDeliveryException">The message was not accepted by the provider.</exception>
    Task<EmailSendResult> SendTemplateAsync(string to, string templateId, IReadOnlyDictionary<string, object> data, CancellationToken cancellationToken = default);
}
