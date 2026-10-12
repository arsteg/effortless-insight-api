using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Outlook/Microsoft Graph provider client.
/// </summary>
public class OutlookProviderClient : IEmailProviderClient
{
    private readonly EmailSyncOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OutlookProviderClient> _logger;

    private const string GraphApiBase = "https://graph.microsoft.com/v1.0";
    private const string GraphBetaBase = "https://graph.microsoft.com/beta";

    public OutlookProviderClient(
        IOptions<EmailSyncOptions> options,
        HttpClient httpClient,
        ILogger<OutlookProviderClient> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Provider => EmailProvider.Outlook;

    private string AuthEndpoint => $"https://login.microsoftonline.com/{_options.Outlook.TenantId}/oauth2/v2.0/authorize";
    private string TokenEndpoint => $"https://login.microsoftonline.com/{_options.Outlook.TenantId}/oauth2/v2.0/token";

    public string GetAuthorizationUrl(string state, string redirectUri, string codeChallenge)
    {
        var scopes = string.Join(" ", _options.Outlook.Scopes);
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _options.Outlook.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = scopes,
            ["state"] = state,
            ["response_mode"] = "query",
            ["prompt"] = "select_account"
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
            var scopes = string.Join(" ", _options.Outlook.Scopes);
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _options.Outlook.ClientId,
                ["client_secret"] = _options.Outlook.ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codeVerifier,
                ["scope"] = scopes
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return TokenFailure(content, response.StatusCode, "exchange");
            }

            var tokenResponse = JsonSerializer.Deserialize<MicrosoftTokenResponse>(content);
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
            _logger.LogError("Error exchanging Outlook authorization code: {Type}", ex.GetType().Name);
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
            var scopes = string.Join(" ", _options.Outlook.Scopes);
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.Outlook.ClientId,
                ["client_secret"] = _options.Outlook.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["scope"] = scopes
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return TokenFailure(content, response.StatusCode, "refresh");
            }

            var tokenResponse = JsonSerializer.Deserialize<MicrosoftTokenResponse>(content);
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
                RefreshToken = tokenResponse.RefreshToken ?? refreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                Scopes = tokenResponse.Scope
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error refreshing Outlook token: {Type}", ex.GetType().Name);
            return new OAuthTokenResult
            {
                Success = false,
                ErrorCode = "exception",
                ErrorMessage = "The email provider request failed. Please try again."
            };
        }
    }

    private OAuthTokenResult TokenFailure(string content, System.Net.HttpStatusCode status, string operation)
    {
        MicrosoftErrorResponse? error = null;
        try { error = JsonSerializer.Deserialize<MicrosoftErrorResponse>(content); }
        catch (JsonException) { }
        var aadCode = error?.ErrorCodes?.FirstOrDefault();
        var code = aadCode switch
        {
            7000215 => "client_secret_invalid",
            7000222 => "client_secret_expired",
            9002331 => "provider_audience_mismatch",
            _ => error?.Error switch
            {
                "invalid_client" => "invalid_client",
                "invalid_grant" => "invalid_grant",
                "invalid_scope" => "invalid_scope",
                "unauthorized_client" => "unauthorized_client",
                "access_denied" => "access_denied",
                _ => "token_exchange_failed"
            }
        };
        // Never log provider descriptions/bodies: they can contain credentials or user data.
        _logger.LogWarning("Outlook token {Operation} failed (HTTP {Status}, code {Code}, AADSTS {AadCode})",
            operation, (int)status, code, aadCode);
        return new() { Success = false, ErrorCode = code, ErrorMessage = code switch
        {
            "client_secret_invalid" or "invalid_client" => "The provider rejected the application credentials. Ask your administrator to check the client ID and client secret Value, not the Secret ID.",
            "client_secret_expired" => "The Microsoft client secret has expired. Ask your administrator to update it.",
            "provider_audience_mismatch" => "The Microsoft account types and application TenantId do not match. Contact your administrator.",
            "invalid_grant" => "This authorization expired or was already used. Please reconnect.",
            _ => "Email authorization failed. Please reconnect or contact your administrator."
        } };
    }

    public async Task<EmailUserProfile> GetUserProfileAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{GraphApiBase}/me?$select=id,mail,userPrincipalName,displayName";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get Outlook user profile (HTTP {Status})", (int)response.StatusCode);
                return new EmailUserProfile();
            }

            var userInfo = JsonSerializer.Deserialize<GraphUser>(content);
            return new EmailUserProfile
            {
                Email = userInfo?.Mail ?? userInfo?.UserPrincipalName ?? string.Empty,
                DisplayName = userInfo?.DisplayName,
                ProviderId = userInfo?.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting Outlook user profile: {Type}", ex.GetType().Name);
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
            var pending = cursor.PendingIds;
            if (pending == null)
            {
                var senders = string.Join(" OR ", options.SenderFilters.Select(s => "from:" + s.TrimStart('@')));
                var query = $"({senders}) AND received>={cursor.Day:yyyy-MM-ddTHH:mm:ssZ} AND received<{windowEnd:yyyy-MM-ddTHH:mm:ssZ}";
                // Graph search has a 1000-hit ceiling, not reliable arbitrary $skip paging.
                // Fetch headers only, split saturated windows, and checkpoint eligible IDs.
                var url = $"{GraphApiBase}/me/messages?$search={Uri.EscapeDataString("\"" + query + "\"")}&$top=1000&$select=id,from,receivedDateTime";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) return new() { ErrorCode = "provider_failed", ErrorMessage = "Microsoft could not read the pending email page. Please retry or reconnect." };
                var page = await response.Content.ReadFromJsonAsync<GraphMessageListResponse>(cancellationToken);
                if ((page?.Value?.Count ?? 0) >= 1000 || page?.NextLink != null)
                {
                    if ((windowEnd - cursor.Day).TotalSeconds <= 1)
                        return new() { ErrorCode = "provider_search_limit", ErrorMessage = "Too many matching messages share a timestamp. Narrow the sender list; pending work is retained." };
                    var midpoint = cursor.Day.AddSeconds(Math.Max(1, Math.Floor((windowEnd - cursor.Day).TotalSeconds / 2)));
                    cursor = new(cursor.Day, Until: midpoint);
                    continue;
                }
                pending = (page?.Value ?? []).Where(m => EmailSenderPolicy.IsAllowed(m.From?.EmailAddress?.Address ?? "", options.SenderFilters))
                    .Select(m => m.Id).Distinct().ToList();
            }
            var batch = pending.Take(options.MaxResults).ToList();
            foreach (var id in batch)
            {
                using var detail = new HttpRequestMessage(HttpMethod.Get, $"{GraphApiBase}/me/messages/{Uri.EscapeDataString(id)}?$select=id,subject,from,receivedDateTime,body,hasAttachments");
                detail.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                detail.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                using var detailResponse = await _httpClient.SendAsync(detail, cancellationToken);
                if (detailResponse.StatusCode == System.Net.HttpStatusCode.NotFound) continue;
                detailResponse.EnsureSuccessStatusCode();
                var full = await detailResponse.Content.ReadFromJsonAsync<GraphMessage>(cancellationToken) ?? throw new InvalidOperationException("Empty message");
                if (!EmailSenderPolicy.IsAllowed(full.From?.EmailAddress?.Address ?? "", options.SenderFilters)) continue;
                if (full.HasAttachments)
                {
                    // Separate collection request: no inline content bytes and no truncated expansion.
                    var attachmentUrl = $"{GraphApiBase}/me/messages/{Uri.EscapeDataString(id)}/attachments?$select=id,name,contentType,size";
                    full.Attachments = [];
                    while (attachmentUrl != null)
                    {
                        if (!Uri.TryCreate(attachmentUrl, UriKind.Absolute, out var attachmentUri) || attachmentUri.Scheme != "https" || attachmentUri.Host != "graph.microsoft.com") throw new InvalidOperationException("Invalid attachment page");
                        using var ar = new HttpRequestMessage(HttpMethod.Get, attachmentUrl);
                        ar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                        using var ap = await _httpClient.SendAsync(ar, cancellationToken);
                        ap.EnsureSuccessStatusCode();
                        var entries = await ap.Content.ReadFromJsonAsync<GraphAttachmentPage>(cancellationToken);
                        full.Attachments.AddRange(entries?.Value ?? []);
                        if (full.Attachments.Count > 200) throw new InvalidOperationException("Too many attachments");
                        attachmentUrl = entries?.NextLink;
                    }
                }
                messages.Add(ParseGraphMessage(full));
            }
            var remaining = pending.Skip(batch.Count).ToList();
            cursor = remaining.Count > 0 ? cursor with { PendingIds = remaining, Page = null, Seen = 0 } : new(windowEnd);
            if (batch.Count > 0) break;
        }
        return new() { Success = true, Messages = messages, NextSyncCursor = cursor.Encode(), HasMore = cursor.Day < cutoff };
    }

    private class GraphAttachmentPage
    {
        [JsonPropertyName("value")] public List<GraphAttachmentRef>? Value { get; set; }
        [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; set; }
    }

    private EmailMessage ParseGraphMessage(GraphMessage graphMessage)
    {
        var attachments = graphMessage.Attachments?
            .Where(a => a.OdataType != "#microsoft.graph.referenceAttachment") // Skip reference attachments
            .Select(a => new EmailAttachment
            {
                AttachmentId = a.Id,
                FileName = a.Name ?? "attachment",
                MimeType = a.ContentType ?? "application/octet-stream",
                Size = a.Size
            })
            .ToList() ?? [];

        return new EmailMessage
        {
            MessageId = graphMessage.Id,
            ProviderMessageId = graphMessage.Id,
            Subject = graphMessage.Subject ?? "",
            Sender = graphMessage.From?.EmailAddress?.Address ?? "",
            SenderName = graphMessage.From?.EmailAddress?.Name,
            ReceivedAt = graphMessage.ReceivedDateTime ?? DateTime.UtcNow,
            BodyText = graphMessage.Body?.ContentType == "text" ? graphMessage.Body.Content : null,
            BodyHtml = graphMessage.Body?.ContentType == "html" ? graphMessage.Body.Content : null,
            Attachments = attachments
        };
    }

    public async Task<AttachmentDownloadResult> DownloadAttachmentAsync(
        string accessToken,
        string messageId,
        string attachmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"{GraphApiBase}/me/messages/{messageId}/attachments/{attachmentId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download Outlook attachment (HTTP {Status})", (int)response.StatusCode);
                return new AttachmentDownloadResult
                {
                    Success = false,
                    ErrorCode = "download_failed",
                    ErrorMessage = "Failed to download attachment"
                };
            }

            var attachment = JsonSerializer.Deserialize<GraphAttachment>(content);
            if (attachment?.ContentBytes == null)
            {
                return new AttachmentDownloadResult
                {
                    Success = false,
                    ErrorCode = "no_data",
                    ErrorMessage = "Attachment data is empty"
                };
            }

            var data = Convert.FromBase64String(attachment.ContentBytes);
            return new AttachmentDownloadResult
            {
                Success = true,
                Data = data,
                FileName = attachment.Name,
                MimeType = attachment.ContentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error downloading Outlook attachment: {Type}", ex.GetType().Name);
            return new AttachmentDownloadResult
            {
                Success = false,
                ErrorCode = "exception",
                ErrorMessage = "The email provider request failed. Please try again."
            };
        }
    }

    #region Response DTOs

    private class MicrosoftTokenResponse
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

    private class MicrosoftErrorResponse
    {
        [JsonPropertyName("error_codes")]
        public int[]? ErrorCodes { get; set; }
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private class GraphUser
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("mail")]
        public string? Mail { get; set; }

        [JsonPropertyName("userPrincipalName")]
        public string? UserPrincipalName { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    private class GraphMessageListResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMessage>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private class GraphDeltaResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMessage>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }

        [JsonPropertyName("@odata.deltaLink")]
        public string? DeltaLink { get; set; }
    }

    private class GraphMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("from")]
        public GraphRecipient? From { get; set; }

        [JsonPropertyName("receivedDateTime")]
        public DateTime? ReceivedDateTime { get; set; }

        [JsonPropertyName("bodyPreview")]
        public string? BodyPreview { get; set; }

        [JsonPropertyName("body")]
        public GraphBody? Body { get; set; }

        [JsonPropertyName("hasAttachments")]
        public bool HasAttachments { get; set; }

        [JsonPropertyName("attachments")]
        public List<GraphAttachmentRef>? Attachments { get; set; }

        [JsonPropertyName("@removed")]
        public object? Removed { get; set; }
    }

    private class GraphRecipient
    {
        [JsonPropertyName("emailAddress")]
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    private class GraphEmailAddress
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }
    }

    private class GraphBody
    {
        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private class GraphAttachmentRef
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("@odata.type")]
        public string? OdataType { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private class GraphAttachment
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; }

        [JsonPropertyName("contentBytes")]
        public string? ContentBytes { get; set; }
    }

    #endregion
}
