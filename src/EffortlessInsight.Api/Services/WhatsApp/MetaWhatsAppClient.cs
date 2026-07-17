using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Implementation of Meta WhatsApp Cloud API client.
/// </summary>
public class MetaWhatsAppClient : IMetaWhatsAppClient
{
    private readonly HttpClient _httpClient;
    private readonly MetaWhatsAppOptions _options;
    private readonly ILogger<MetaWhatsAppClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MetaWhatsAppClient(
        HttpClient httpClient,
        IOptions<MetaWhatsAppOptions> options,
        ILogger<MetaWhatsAppClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Configure HttpClient
        _httpClient.BaseAddress = new Uri(_options.GraphApiBaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.AccessToken);
    }

    #region Sending Messages

    public async Task<WhatsAppSendResult> SendTextMessageAsync(
        string to,
        string text,
        bool previewUrl = false,
        CancellationToken ct = default)
    {
        var request = new MetaTextMessageRequest
        {
            To = FormatPhoneNumber(to),
            Text = new MetaTextContent
            {
                Body = text,
                PreviewUrl = previewUrl
            }
        };

        return await SendMessageAsync(request, ct);
    }

    public async Task<WhatsAppSendResult> SendTemplateMessageAsync(
        string to,
        string templateName,
        string language,
        List<TemplateParameter>? bodyParameters = null,
        List<TemplateParameter>? headerParameters = null,
        CancellationToken ct = default)
    {
        var components = new List<MetaTemplateComponent>();

        if (headerParameters?.Any() == true)
        {
            components.Add(new MetaTemplateComponent
            {
                Type = "header",
                Parameters = headerParameters.Select(p => new MetaTemplateParameter
                {
                    Type = p.Type,
                    Text = p.Type == "text" ? p.Value?.ToString() : null,
                    Image = p.Type == "image" ? new MetaMediaObject { Id = p.Value?.ToString() } : null
                }).ToList()
            });
        }

        if (bodyParameters?.Any() == true)
        {
            components.Add(new MetaTemplateComponent
            {
                Type = "body",
                Parameters = bodyParameters.Select(p => new MetaTemplateParameter
                {
                    Type = p.Type,
                    Text = p.Type == "text" ? p.Value?.ToString() : null
                }).ToList()
            });
        }

        var request = new MetaTemplateMessageRequest
        {
            To = FormatPhoneNumber(to),
            Template = new MetaTemplateContent
            {
                Name = templateName,
                Language = new MetaLanguage { Code = language },
                Components = components.Count > 0 ? components : null
            }
        };

        return await SendMessageAsync(request, ct);
    }

    public async Task<WhatsAppSendResult> SendInteractiveButtonsAsync(
        string to,
        string bodyText,
        List<WhatsAppButton> buttons,
        string? headerText = null,
        string? footerText = null,
        CancellationToken ct = default)
    {
        if (buttons.Count > 3)
        {
            _logger.LogWarning("WhatsApp buttons limited to 3. Truncating from {Count}", buttons.Count);
            buttons = buttons.Take(3).ToList();
        }

        var request = new MetaInteractiveMessageRequest
        {
            To = FormatPhoneNumber(to),
            Interactive = new MetaInteractiveContent
            {
                Type = "button",
                Header = headerText != null ? new MetaInteractiveHeader { Text = headerText } : null,
                Body = new MetaInteractiveBody { Text = bodyText },
                Footer = footerText != null ? new MetaInteractiveFooter { Text = footerText } : null,
                Action = new MetaInteractiveAction
                {
                    Buttons = buttons.Select(b => new MetaInteractiveButton
                    {
                        Reply = new MetaButtonReply
                        {
                            Id = b.Id,
                            Title = b.Title.Length > 20 ? b.Title[..20] : b.Title
                        }
                    }).ToList()
                }
            }
        };

        return await SendMessageAsync(request, ct);
    }

    public async Task<WhatsAppSendResult> SendInteractiveListAsync(
        string to,
        string bodyText,
        string buttonText,
        List<WhatsAppListSection> sections,
        string? headerText = null,
        string? footerText = null,
        CancellationToken ct = default)
    {
        var request = new MetaInteractiveMessageRequest
        {
            To = FormatPhoneNumber(to),
            Interactive = new MetaInteractiveContent
            {
                Type = "list",
                Header = headerText != null ? new MetaInteractiveHeader { Text = headerText } : null,
                Body = new MetaInteractiveBody { Text = bodyText },
                Footer = footerText != null ? new MetaInteractiveFooter { Text = footerText } : null,
                Action = new MetaInteractiveAction
                {
                    Button = buttonText.Length > 20 ? buttonText[..20] : buttonText,
                    Sections = sections.Select(s => new MetaListSection
                    {
                        Title = s.Title,
                        Rows = s.Rows.Select(r => new MetaListRow
                        {
                            Id = r.Id,
                            Title = r.Title.Length > 24 ? r.Title[..24] : r.Title,
                            Description = r.Description?.Length > 72 ? r.Description[..72] : r.Description
                        }).ToList()
                    }).ToList()
                }
            }
        };

        return await SendMessageAsync(request, ct);
    }

    private async Task<WhatsAppSendResult> SendMessageAsync<T>(T request, CancellationToken ct)
    {
        try
        {
            var url = $"/{_options.GraphApiVersion}/{_options.PhoneNumberId}/messages";
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogDebug("Sending WhatsApp message to {Url}", url);

            var response = await _httpClient.PostAsync(url, content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<MetaMessageResponse>(responseBody, _jsonOptions);
                var messageId = result?.Messages?.FirstOrDefault()?.Id;

                _logger.LogInformation("WhatsApp message sent successfully. MessageId: {MessageId}", messageId);

                return new WhatsAppSendResult(true, messageId, null, null);
            }

            var error = JsonSerializer.Deserialize<MetaErrorResponse>(responseBody, _jsonOptions);
            _logger.LogError(
                "WhatsApp message failed. Code: {Code}, Message: {Message}",
                error?.Error?.Code,
                error?.Error?.Message);

            return new WhatsAppSendResult(
                false,
                null,
                error?.Error?.Code.ToString(),
                error?.Error?.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp message");
            return new WhatsAppSendResult(false, null, "EXCEPTION", ex.Message);
        }
    }

    #endregion

    #region Media

    public async Task<string?> UploadMediaAsync(
        Stream content,
        string mimeType,
        string fileName,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"/{_options.GraphApiVersion}/{_options.PhoneNumberId}/media";

            using var formData = new MultipartFormDataContent();
            using var streamContent = new StreamContent(content);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

            formData.Add(new StringContent("whatsapp"), "messaging_product");
            formData.Add(streamContent, "file", fileName);

            var response = await _httpClient.PostAsync(url, formData, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);
                return doc.RootElement.GetProperty("id").GetString();
            }

            _logger.LogError("Failed to upload media: {Response}", responseBody);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading media to WhatsApp");
            return null;
        }
    }

    public async Task<WhatsAppSendResult> SendImageAsync(
        string to,
        string mediaId,
        string? caption = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = FormatPhoneNumber(to),
            type = "image",
            image = new { id = mediaId, caption }
        };

        return await SendMessageAsync(request, ct);
    }

    public async Task<WhatsAppSendResult> SendDocumentAsync(
        string to,
        string mediaId,
        string? caption = null,
        string? fileName = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = FormatPhoneNumber(to),
            type = "document",
            document = new { id = mediaId, caption, filename = fileName }
        };

        return await SendMessageAsync(request, ct);
    }

    public async Task<WhatsAppMediaInfo?> GetMediaInfoAsync(string mediaId, CancellationToken ct = default)
    {
        try
        {
            var url = $"/{_options.GraphApiVersion}/{mediaId}";
            var response = await _httpClient.GetAsync(url, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<WhatsAppMediaInfo>(responseBody, _jsonOptions);
                _logger.LogDebug("Retrieved media info for {MediaId}: {MimeType}", mediaId, result?.MimeType);
                return result;
            }

            _logger.LogError("Failed to get media info: {Response}", responseBody);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting media info for {MediaId}", mediaId);
            return null;
        }
    }

    public async Task<Stream?> DownloadMediaAsync(string mediaUrl, CancellationToken ct = default)
    {
        try
        {
            // Create a new request with authorization header
            using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Downloaded media from {Url}", MaskUrl(mediaUrl));
                return await response.Content.ReadAsStreamAsync(ct);
            }

            _logger.LogError("Failed to download media: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading media from {Url}", MaskUrl(mediaUrl));
            return null;
        }
    }

    private static string MaskUrl(string url)
    {
        // Mask access tokens in URL for logging
        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}[masked]";
    }

    #endregion

    #region Templates

    public async Task<List<WhatsAppTemplateInfo>> GetTemplatesAsync(CancellationToken ct = default)
    {
        var templates = new List<WhatsAppTemplateInfo>();

        try
        {
            var url = $"/{_options.GraphApiVersion}/{_options.WabaId}/message_templates";

            while (!string.IsNullOrEmpty(url))
            {
                var response = await _httpClient.GetAsync(url, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to fetch templates: {Response}", responseBody);
                    break;
                }

                var result = JsonSerializer.Deserialize<MetaTemplatesResponse>(responseBody, _jsonOptions);
                if (result?.Data != null)
                {
                    templates.AddRange(result.Data);
                }

                url = result?.Paging?.Next;
            }

            _logger.LogInformation("Fetched {Count} WhatsApp templates", templates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching WhatsApp templates");
        }

        return templates;
    }

    #endregion

    #region Read Receipts

    public async Task MarkAsReadAsync(string messageId, CancellationToken ct = default)
    {
        try
        {
            var url = $"/{_options.GraphApiVersion}/{_options.PhoneNumberId}/messages";
            var request = new
            {
                messaging_product = "whatsapp",
                status = "read",
                message_id = messageId
            };

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await _httpClient.PostAsync(url, content, ct);
            _logger.LogDebug("Marked message {MessageId} as read", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to mark message {MessageId} as read", messageId);
        }
    }

    #endregion

    #region Phone Number Formatting

    public string FormatPhoneNumber(string phone)
    {
        if (string.IsNullOrEmpty(phone))
            return phone;

        // Remove all non-digit characters
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // If starts with 0, assume Indian number and add 91
        if (digits.StartsWith("0"))
        {
            digits = "91" + digits[1..];
        }
        // If doesn't start with country code, assume Indian
        else if (!digits.StartsWith("91") && digits.Length == 10)
        {
            digits = "91" + digits;
        }

        return digits;
    }

    public string MaskPhoneNumber(string phone)
    {
        var formatted = FormatPhoneNumber(phone);
        if (formatted.Length < 6)
            return formatted;

        // Show first 2 and last 4 digits: +91****3210
        return $"+{formatted[..2]}****{formatted[^4..]}";
    }

    #endregion
}
