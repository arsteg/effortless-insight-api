namespace EffortlessInsight.Api.Services.Email;

/// <summary>Rendered subject and HTML body for a named email template.</summary>
public sealed record EmailTemplateContent(string Subject, string HtmlBody);

/// <summary>
/// Renders named email templates (e.g. "auth_verify_email") into a subject
/// and HTML body. Unknown template ids render a generic notification shell.
/// </summary>
public interface IEmailTemplateRenderer
{
    EmailTemplateContent Render(string templateId, IReadOnlyDictionary<string, object> data);
}
