using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Gmail provider client using Google APIs.
/// </summary>
public class GmailProviderClient : IEmailProviderClient
{
    private readonly EmailSyncOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GmailProviderClient> _logger;

    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
    private const string GmailApiBase = "https://gmail.googleapis.com/gmail/v1/users/me";

    public GmailProviderClient(
        IOptions<EmailSyncOptions> options,
        HttpClient httpClient,
        ILogger<GmailProviderClient> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Provider => EmailProvider.Gmail;

    public string GetAuthorizationUrl(string state, string redirectUri, string codeChallenge)
    {
        var scopes = string.Join(" ", _options.Gmail.Scopes);
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _options.Gmail.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = scopes,
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent" // Always prompt to get refresh token
        };

        var query = string.Join("&", queryParams.Select(kvp =>
            $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

        return $"{AuthEndpoint}?{query}";
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _options.Gmail.ClientId,
                ["client_secret"] = _options.Gmail.ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codeVerifier
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gmail token exchange failed (HTTP {Status})", (int)response.StatusCode);
                var error = JsonSerializer.Deserialize<GoogleErrorResponse>(content);
                return new OAuthTokenResult
                {
                    Success = false,
                    ErrorCode = error?.Error ?? "token_exchange_failed",
                    ErrorMessage = "Email authorization failed. Please reconnect or contact your administrator."
                };
            }

            var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(content);
            if (tokenResponse == null)
            {
                return new OAuthTokenResult
                {
                    Success = false,
                    ErrorCode = "invalid_response",
                    ErrorMessage = "Failed to parse token response"
                };
            }

            return new OAuthTokenResult
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                Scopes = tokenResponse.Scope
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error exchanging Gmail authorization code: {Type}", ex.GetType().Name);
            return new OAuthTokenResult
            {
                Success = false,
                ErrorCode = "exception",
                ErrorMessage = "The email provider request failed. Please try again."
            };
        }
    }

    public async Task<OAuthTokenResult> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.Gmail.ClientId,
                ["client_secret"] = _options.Gmail.ClientSecret,
                ["grant_type"] = "refresh_token"
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gmail token refresh failed (HTTP {Status})", (int)response.StatusCode);
                var error = JsonSerializer.Deserialize<GoogleErrorResponse>(content);
                return new OAuthTokenResult
                {
                    Success = false,
                    ErrorCode = error?.Error ?? "refresh_failed",
                    ErrorMessage = "Email authorization failed. Please reconnect or contact your administrator."
                };
            }

            var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(content);
            if (tokenResponse == null)
            {
                return new OAuthTokenResult
                {
                    Success = false,
                    ErrorCode = "invalid_response",
                    ErrorMessage = "Failed to parse token response"
                };
            }

            return new OAuthTokenResult
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? refreshToken, // Keep old if not returned
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                Scopes = tokenResponse.Scope
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error refreshing Gmail token: {Type}", ex.GetType().Name);
            return new OAuthTokenResult
            {
                Success = false,
                ErrorCode = "exception",
                ErrorMessage = "The email provider request failed. Please try again."
            };
        }
    }

    public async Task<EmailUserProfile> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get Gmail user profile (HTTP {Status})", (int)response.StatusCode);
                return new EmailUserProfile();
            }

            var userInfo = JsonSerializer.Deserialize<GoogleUserInfo>(content);
            return new EmailUserProfile
            {
                Email = userInfo?.Email ?? string.Empty,
                DisplayName = userInfo?.Name,
                ProviderId = userInfo?.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting Gmail user profile: {Type}", ex.GetType().Name);
            return new EmailUserProfile();
        }
    }

    public async Task<EmailFetchResult> FetchEmailsAsync(string accessToken, EmailFetchOptions options, CancellationToken cancellationToken = default)
    {
        if (options.SenderFilters.Count == 0 || options.SenderFilters.Any(s => !EmailSenderPolicy.IsValidRule(s)))
            return new() { ErrorCode = "sender_configuration", ErrorMessage = "Configure valid GST sender addresses or domains before syncing." };
        var cursor = EmailScanCursor.Read(options.SyncCursor, options.AfterDate ?? DateTime.UtcNow.AddDays(-90));
        var messages = new List<EmailMessage>();
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        for (var calls = 0; calls < 100 && messages.Count < options.MaxResults && cursor.Day < cutoff; calls++)
        {
            cursor = cursor with { Until = cursor.Until ?? (cursor.Day.AddDays(1) < cutoff ? cursor.Day.AddDays(1) : cutoff) };
            var windowEnd = cursor.Until.Value;
            var senders = string.Join(" OR ", options.SenderFilters.Select(s => "from:" + s.TrimStart('@')));
            var after = new DateTimeOffset(cursor.Day).ToUnixTimeSeconds();
            var before = new DateTimeOffset(windowEnd).ToUnixTimeSeconds();
            var query = $"({senders}) after:{after - 1} before:{before}";
            var url = $"{GmailApiBase}/messages?maxResults={options.MaxResults - messages.Count}&q={HttpUtility.UrlEncode(query)}";
            if (cursor.Page != null) url += "&pageToken=" + HttpUtility.UrlEncode(cursor.Page);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return await ReadFetchErrorAsync(response, cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<GmailMessageListResponse>(cancellationToken);
            foreach (var item in page?.Messages ?? [])
            {
                // Exact sender validation from headers before fetching a body or attachment.
                using var headers = new HttpRequestMessage(HttpMethod.Get, $"{GmailApiBase}/messages/{Uri.EscapeDataString(item.Id)}?format=metadata&metadataHeaders=From");
                headers.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                using var headerResponse = await _httpClient.SendAsync(headers, cancellationToken);
                if (!headerResponse.IsSuccessStatusCode) return await ReadFetchErrorAsync(headerResponse, cancellationToken);
                var metadata = await headerResponse.Content.ReadFromJsonAsync<GmailMessage>(cancellationToken);
                var sender = metadata?.Payload?.Headers?.FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                if (!EmailSenderPolicy.IsAllowed(sender, options.SenderFilters)) continue;
                var message = await GetMessageDetailsAsync(accessToken, item.Id, cancellationToken);
                if (message == null) return new() { ErrorCode = "message_unavailable", ErrorMessage = "A pending GST email could not be read. Please retry." };
                messages.Add(message);
            }
            cursor = page?.NextPageToken != null ? cursor with { Page = page.NextPageToken } : new(windowEnd);
            if (page?.Messages?.Count > 0) break;
        }
        return new() { Success = true, Messages = messages, NextSyncCursor = cursor.Encode(), HasMore = cursor.Day < cutoff };
    }

    private async Task<EmailFetchResult> ReadFetchErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Inspect only machine-readable reasons; never expose or log Google's raw
        // response, which may contain account details or request parameters.
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (body.RootElement.ValueKind == JsonValueKind.Object &&
                body.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in new[] { "errors", "details" })
                    if (error.TryGetProperty(field, out var entries) && entries.ValueKind == JsonValueKind.Array)
                        foreach (var entry in entries.EnumerateArray())
                            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("reason", out var reason) &&
                                reason.ValueKind == JsonValueKind.String)
                                reasons.Add(reason.GetString()!);
            }
        }
        catch (JsonException) { /* Proxies may return HTML or an empty body. */ }

        var status = (int)response.StatusCode;
        var (code, message) = status switch
        {
            401 => ("reauthentication_required", "Gmail authorization is no longer valid. Reconnect this email account to resume syncing."),
            403 when reasons.Overlaps(["accessNotConfigured", "SERVICE_DISABLED"]) =>
                ("gmail_api_disabled", "The Gmail API is disabled for this application's Google Cloud project. Ask your administrator to enable the Gmail API in the project that owns the OAuth client, then retry sync."),
            403 when reasons.Overlaps(["rateLimitExceeded", "userRateLimitExceeded", "dailyLimitExceeded", "quotaExceeded", "RATE_LIMIT_EXCEEDED", "QUOTA_EXCEEDED"]) =>
                ("provider_rate_limited", "Gmail's request quota has been reached. Retry sync later."),
            429 => ("provider_rate_limited", "Gmail is temporarily limiting requests. Retry sync later."),
            403 when reasons.Contains("domainPolicy") =>
                ("gmail_domain_policy", "Your Google Workspace administrator has blocked Gmail API access. Ask them to allow this application, then retry sync."),
            403 when reasons.Overlaps(["insufficientPermissions", "ACCESS_TOKEN_SCOPE_INSUFFICIENT"]) =>
                ("gmail_permission_required", "Gmail read permission was not granted. Reconnect this account and allow the requested read-only Gmail access."),
            403 => ("gmail_access_denied", "Google denied Gmail access. Ask your administrator to check Gmail API enablement, read-only permissions and Workspace access policies."),
            >= 500 => ("provider_unavailable", "Gmail is temporarily unavailable. Retry sync later."),
            _ => ("provider_failed", $"Gmail could not read emails (HTTP {status}). Ask your administrator to check the API logs before reconnecting.")
        };
        _logger.LogWarning("Gmail email fetch failed (HTTP {Status}, classification {ErrorCode})", status, code);
        return new() { ErrorCode = code, ErrorMessage = message };
    }

    private async Task<EmailMessage?> GetMessageDetailsAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{GmailApiBase}/messages/{messageId}?format=full";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var gmailMessage = JsonSerializer.Deserialize<GmailMessage>(content);
            if (gmailMessage == null) return null;

            return ParseGmailMessage(gmailMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Gmail message details for {MessageId}", messageId);
            return null;
        }
    }

    private EmailMessage ParseGmailMessage(GmailMessage gmailMessage)
    {
        var headers = gmailMessage.Payload?.Headers ?? [];
        var subject = headers.FirstOrDefault(h => h.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        var from = headers.FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        var dateStr = headers.FirstOrDefault(h => h.Name.Equals("Date", StringComparison.OrdinalIgnoreCase))?.Value;

        // Parse sender
        var senderEmail = from;
        var senderName = (string?)null;
        if (from.Contains('<') && from.Contains('>'))
        {
            var match = System.Text.RegularExpressions.Regex.Match(from, @"^(.+?)\s*<(.+?)>$");
            if (match.Success)
            {
                senderName = match.Groups[1].Value.Trim().Trim('"');
                senderEmail = match.Groups[2].Value.Trim();
            }
        }

        // Parse date
        var receivedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(dateStr))
        {
            if (DateTimeOffset.TryParse(dateStr, out var dto))
            {
                receivedAt = dto.UtcDateTime;
            }
        }
        else if (long.TryParse(gmailMessage.InternalDate, out var internalDate))
        {
            receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(internalDate).UtcDateTime;
        }

        // Extract body and attachments
        var (bodyText, bodyHtml, attachments) = ExtractBodyAndAttachments(gmailMessage.Payload);

        return new EmailMessage
        {
            MessageId = gmailMessage.Id,
            ProviderMessageId = gmailMessage.Id,
            Subject = subject,
            Sender = senderEmail,
            SenderName = senderName,
            ReceivedAt = receivedAt,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            Attachments = attachments
        };
    }

    private (string? bodyText, string? bodyHtml, List<EmailAttachment> attachments) ExtractBodyAndAttachments(
        GmailMessagePart? part)
    {
        string? bodyText = null;
        string? bodyHtml = null;
        var attachments = new List<EmailAttachment>();

        if (part == null) return (bodyText, bodyHtml, attachments);

        // Direct body
        if (part.Body?.Data != null)
        {
            var decoded = DecodeBase64Url(part.Body.Data);
            if (part.MimeType == "text/plain")
                bodyText = decoded;
            else if (part.MimeType == "text/html")
                bodyHtml = decoded;
        }

        // Attachment
        if (!string.IsNullOrEmpty(part.Body?.AttachmentId))
        {
            attachments.Add(new EmailAttachment
            {
                AttachmentId = part.Body.AttachmentId,
                FileName = part.Filename ?? "attachment",
                MimeType = part.MimeType ?? "application/octet-stream",
                Size = part.Body.Size
            });
        }

        // Process parts recursively
        if (part.Parts != null)
        {
            foreach (var subPart in part.Parts)
            {
                var (subText, subHtml, subAttachments) = ExtractBodyAndAttachments(subPart);
                bodyText ??= subText;
                bodyHtml ??= subHtml;
                attachments.AddRange(subAttachments);
            }
        }

        return (bodyText, bodyHtml, attachments);
    }

    private static string DecodeBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var bytes = Convert.FromBase64String(base64);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task<string?> GetCurrentHistoryIdAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{GmailApiBase}/profile";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var profile = JsonSerializer.Deserialize<GmailProfile>(content);
            return profile?.HistoryId;
        }
        catch
        {
            return null;
        }
    }

    public async Task<AttachmentDownloadResult> DownloadAttachmentAsync(
        string accessToken,
        string messageId,
        string attachmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{GmailApiBase}/messages/{messageId}/attachments/{attachmentId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download Gmail attachment (HTTP {Status})", (int)response.StatusCode);
                return new AttachmentDownloadResult
                {
                    Success = false,
                    ErrorCode = "download_failed",
                    ErrorMessage = "Failed to download attachment"
                };
            }

            var attachment = JsonSerializer.Deserialize<GmailAttachment>(content);
            if (attachment?.Data == null)
            {
                return new AttachmentDownloadResult
                {
                    Success = false,
                    ErrorCode = "no_data",
                    ErrorMessage = "Attachment data is empty"
                };
            }

            var data = DecodeBase64UrlToBytes(attachment.Data);
            return new AttachmentDownloadResult
            {
                Success = true,
                Data = data
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error downloading Gmail attachment: {Type}", ex.GetType().Name);
            return new AttachmentDownloadResult
            {
                Success = false,
                ErrorCode = "exception",
                ErrorMessage = "The email provider request failed. Please try again."
            };
        }
    }

    private static byte[] DecodeBase64UrlToBytes(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    #region Response DTOs

    private class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    private class GoogleErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private class GoogleUserInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }

    private class GmailMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<GmailMessageRef>? Messages { get; set; }

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }
    }

    private class GmailMessageRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("threadId")]
        public string? ThreadId { get; set; }
    }

    private class GmailHistoryResponse
    {
        [JsonPropertyName("history")]
        public List<GmailHistory>? History { get; set; }

        [JsonPropertyName("historyId")]
        public string? HistoryId { get; set; }

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }
    }

    private class GmailHistory
    {
        [JsonPropertyName("messagesAdded")]
        public List<GmailMessageAdded>? MessagesAdded { get; set; }
    }

    private class GmailMessageAdded
    {
        [JsonPropertyName("message")]
        public GmailMessageRef Message { get; set; } = new();
    }

    private class GmailMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("threadId")]
        public string? ThreadId { get; set; }

        [JsonPropertyName("internalDate")]
        public string? InternalDate { get; set; }

        [JsonPropertyName("payload")]
        public GmailMessagePart? Payload { get; set; }
    }

    private class GmailMessagePart
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("headers")]
        public List<GmailHeader> Headers { get; set; } = [];

        [JsonPropertyName("body")]
        public GmailBody? Body { get; set; }

        [JsonPropertyName("parts")]
        public List<GmailMessagePart>? Parts { get; set; }
    }

    private class GmailHeader
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    private class GmailBody
    {
        [JsonPropertyName("attachmentId")]
        public string? AttachmentId { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }

    private class GmailAttachment
    {
        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }

    private class GmailProfile
    {
        [JsonPropertyName("emailAddress")]
        public string? EmailAddress { get; set; }

        [JsonPropertyName("historyId")]
        public string? HistoryId { get; set; }
    }

    #endregion
}
