using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Email;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EffortlessInsight.Api.Services.EmailSync;

public record EmailInspection(bool IsNotice, bool Uncertain, bool AiUsed, List<string> Gstins,
    byte[] Document, bool ConvertedToPdf, string? ReferenceNumber = null);

public interface IEmailNoticeInspector
{
    Task<EmailInspection> InspectAsync(byte[] data, string mimeType, EmailMessage email,
        Guid organizationId, bool allowAi, CancellationToken cancellationToken);
}

public sealed class EmailNoticeInspector(IHttpClientFactory factory, IOptions<AiServiceOptions> aiOptions,
    IOptions<EmailSyncOptions> options) : IEmailNoticeInspector
{
    private static readonly Regex Gstin = new(@"\b[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][A-Z0-9]Z[A-Z0-9]\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex NoticeType = new(@"\b(?:DRC[-\s]?(?:01A?|03|07)|ASMT[-\s]?(?:10|12|14|16)|REG[-\s]?(?:17|18|19|21))\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    public async Task<EmailInspection> InspectAsync(byte[] data, string mimeType, EmailMessage email,
        Guid organizationId, bool allowAi, CancellationToken cancellationToken)
    {
        if (!EmailSenderPolicy.IsAllowed(email.Sender, options.Value.AllowedSenders)) return new(false, false, false, [], [], false);
        var pdf = mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
        string text;
        if (pdf)
        {
            if (!data.AsSpan().StartsWith("%PDF-"u8)) throw new InvalidDataException("Unreadable PDF");
            var extraction = await PostAsync<ExtractedText>("extract", new { data = Convert.ToBase64String(data), organizationId }, cancellationToken);
            text = extraction.Text;
            if (string.IsNullOrWhiteSpace(text)) return new(false, true, false, [], data, false);
        }
        else
        {
            text = Encoding.UTF8.GetString(data);
            if (mimeType.Contains("html", StringComparison.OrdinalIgnoreCase) || text.TrimStart().StartsWith('<')) text = HtmlToText.Convert(text);
            text = text[..Math.Min(text.Length, 50000)];
        }
        // Attachment GSTIN takes precedence; an unrelated GSTIN in a mail signature cannot route it.
        var gstins = Gstin.Matches(text).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        if (gstins.Count == 0) gstins = Gstin.Matches(email.Subject + " " + (email.BodyText ?? HtmlToText.Convert(email.BodyHtml ?? ""))).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        var snippet = text[..Math.Min(text.Length, 6000)];
        var deterministic = NoticeType.IsMatch(text) && gstins.Count == 1;
        var plausible = deterministic || options.Value.NoticeKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
            (gstins.Count == 1 && text.Contains("GST", StringComparison.OrdinalIgnoreCase));
        var detected = deterministic;
        var used = false;
        var uncertain = plausible && !deterministic;
        if (uncertain && allowAi && gstins.Count == 1)
        {
            used = true;
            var classification = await PostAsync<Classification>("classify", new { snippet,
                subject = email.Subject[..Math.Min(email.Subject.Length, 300)], sender = email.Sender, organizationId }, cancellationToken);
            detected = classification.IsNotice && classification.Confidence >= options.Value.AiConfidenceThreshold;
            uncertain = classification.Confidence < options.Value.AiConfidenceThreshold;
        }
        if (!pdf && detected)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            data = Document.Create(c => c.Page(page => { page.Margin(30); page.DefaultTextStyle(x => x.FontSize(10)); page.Content().Text(text); })).GeneratePdf();
        }
        var reference = Regex.Match(text, @"(?:Reference|Notice|Order|ARN|DIN)\s*(?:Number|No\.?|#)?\s*[:=-]\s*([A-Z0-9][A-Z0-9/-]{7,49})", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        return new(detected, uncertain, used, gstins, data, !pdf, reference.Success ? reference.Groups[1].Value.ToUpperInvariant() : null);
    }

    private async Task<T> PostAsync<T>(string action, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, aiOptions.Value.BaseUrl.TrimEnd('/') + "/api/v1/email-notices/" + action);
        request.Headers.Add("X-API-Key", aiOptions.Value.ApiKey);
        request.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await factory.CreateClient("EmailInspection").SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(timeout.Token) ?? throw new InvalidDataException("Empty inspection response");
    }
    private record ExtractedText(string Text);
    private record Classification(bool IsNotice, double Confidence);
}
