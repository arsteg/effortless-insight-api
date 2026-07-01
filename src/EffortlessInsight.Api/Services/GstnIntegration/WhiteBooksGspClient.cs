using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// WhiteBooks GSP client implementation for GSTN portal integration.
/// </summary>
public class WhiteBooksGspClient : IGspClient
{
    private readonly HttpClient _httpClient;
    private readonly GspProviderOptions _options;
    private readonly GstnOptions _gstnOptions;
    private readonly ILogger<WhiteBooksGspClient> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public WhiteBooksGspClient(
        HttpClient httpClient,
        IOptions<GspOptions> gspOptions,
        IOptions<GstnOptions> gstnOptions,
        ILogger<WhiteBooksGspClient> logger)
    {
        _httpClient = httpClient;
        _options = gspOptions.Value.WhiteBooks;
        _gstnOptions = gstnOptions.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.EffectiveBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                          r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            .WaitAndRetryAsync(
                _options.MaxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(_options.RetryDelaySeconds, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    _logger.LogWarning(
                        "WhiteBooks API retry {RetryAttempt} after {Delay}s due to {Reason}",
                        retryAttempt,
                        timespan.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });
    }

    public string ProviderName => "whitebooks";

    public async Task<GspOtpInitiationResult> InitiateOtpAsync(string gstin, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "Initiating OTP for GSTIN {Gstin} via WhiteBooks, CorrelationId: {CorrelationId}",
                MaskGstin(gstin),
                correlationId);

            var request = new
            {
                gstin,
                action = "OTPREQUEST",
                clientId = _options.ClientId
            };

            var response = await SendRequestAsync<WhiteBooksOtpResponse>(
                HttpMethod.Post,
                "/gsp/v1/otp/request",
                request,
                correlationId,
                cancellationToken);

            if (response.Status == "SUCCESS")
            {
                return new GspOtpInitiationResult
                {
                    Success = true,
                    SessionId = response.SessionId,
                    OtpDestination = response.MaskedDestination,
                    OtpDestinationType = response.DestinationType,
                    ExpirySeconds = response.ExpirySeconds ?? 300,
                    CorrelationId = correlationId
                };
            }

            return new GspOtpInitiationResult
            {
                Success = false,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                CorrelationId = correlationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate OTP for GSTIN {Gstin}", MaskGstin(gstin));
            return new GspOtpInitiationResult
            {
                Success = false,
                ErrorCode = "GSP_ERROR",
                ErrorMessage = ex.Message,
                CorrelationId = correlationId
            };
        }
    }

    public async Task<GspTokenResult> VerifyOtpAsync(string sessionId, string otp, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "Verifying OTP for session {SessionId}, CorrelationId: {CorrelationId}",
                sessionId[..8] + "...",
                correlationId);

            var request = new
            {
                sessionId,
                otp,
                action = "OTPVERIFY",
                clientId = _options.ClientId
            };

            var response = await SendRequestAsync<WhiteBooksTokenResponse>(
                HttpMethod.Post,
                "/gsp/v1/otp/verify",
                request,
                correlationId,
                cancellationToken);

            if (response.Status == "SUCCESS")
            {
                // Use GSP response expiry if provided, otherwise use configured defaults
                var defaultAccessExpiry = _gstnOptions.DefaultAccessTokenExpiryHours * 3600;
                var accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn ?? defaultAccessExpiry);
                var refreshTokenExpiresAt = response.RefreshTokenExpiresIn.HasValue
                    ? DateTime.UtcNow.AddSeconds(response.RefreshTokenExpiresIn.Value)
                    : DateTime.UtcNow.AddDays(_gstnOptions.DefaultRefreshTokenExpiryDays);

                return new GspTokenResult
                {
                    Success = true,
                    AccessToken = response.AccessToken,
                    RefreshToken = response.RefreshToken,
                    AccessTokenExpiresAt = accessTokenExpiresAt,
                    RefreshTokenExpiresAt = refreshTokenExpiresAt,
                    SessionId = response.SessionId,
                    CorrelationId = correlationId
                };
            }

            return new GspTokenResult
            {
                Success = false,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                CorrelationId = correlationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify OTP for session {SessionId}", sessionId[..8] + "...");
            return new GspTokenResult
            {
                Success = false,
                ErrorCode = "GSP_ERROR",
                ErrorMessage = ex.Message,
                CorrelationId = correlationId
            };
        }
    }

    public async Task<GspTokenResult> RefreshTokenAsync(string refreshToken, string gstin, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "Refreshing token for GSTIN {Gstin}, CorrelationId: {CorrelationId}",
                MaskGstin(gstin),
                correlationId);

            var request = new
            {
                refreshToken,
                gstin,
                action = "REFRESH",
                clientId = _options.ClientId
            };

            var response = await SendRequestAsync<WhiteBooksTokenResponse>(
                HttpMethod.Post,
                "/gsp/v1/token/refresh",
                request,
                correlationId,
                cancellationToken);

            if (response.Status == "SUCCESS")
            {
                // Use GSP response expiry if provided, otherwise use configured defaults
                var defaultAccessExpiry = _gstnOptions.DefaultAccessTokenExpiryHours * 3600;
                var accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn ?? defaultAccessExpiry);
                var refreshTokenExpiresAt = response.RefreshTokenExpiresIn.HasValue
                    ? DateTime.UtcNow.AddSeconds(response.RefreshTokenExpiresIn.Value)
                    : DateTime.UtcNow.AddDays(_gstnOptions.DefaultRefreshTokenExpiryDays);

                return new GspTokenResult
                {
                    Success = true,
                    AccessToken = response.AccessToken,
                    RefreshToken = response.RefreshToken ?? refreshToken,
                    AccessTokenExpiresAt = accessTokenExpiresAt,
                    RefreshTokenExpiresAt = refreshTokenExpiresAt,
                    SessionId = response.SessionId,
                    CorrelationId = correlationId
                };
            }

            return new GspTokenResult
            {
                Success = false,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                CorrelationId = correlationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for GSTIN {Gstin}", MaskGstin(gstin));
            return new GspTokenResult
            {
                Success = false,
                ErrorCode = "GSP_ERROR",
                ErrorMessage = ex.Message,
                CorrelationId = correlationId
            };
        }
    }

    public async Task RevokeTokenAsync(string accessToken, string gstin, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "Revoking token for GSTIN {Gstin}, CorrelationId: {CorrelationId}",
                MaskGstin(gstin),
                correlationId);

            var request = new
            {
                gstin,
                action = "REVOKE",
                clientId = _options.ClientId
            };

            await SendAuthenticatedRequestAsync<WhiteBooksBaseResponse>(
                HttpMethod.Post,
                "/gsp/v1/token/revoke",
                accessToken,
                request,
                correlationId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke token for GSTIN {Gstin}", MaskGstin(gstin));
        }
    }

    public async Task<GspNoticesResult> FetchNoticesAsync(
        string accessToken,
        string gstin,
        DateTime fromDate,
        DateTime toDate,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "Fetching notices for GSTIN {Gstin} from {From} to {To}, PageToken: {PageToken}, CorrelationId: {CorrelationId}",
                MaskGstin(gstin),
                fromDate.ToString("yyyy-MM-dd"),
                toDate.ToString("yyyy-MM-dd"),
                pageToken ?? "none",
                correlationId);

            object request = pageToken != null
                ? new
                {
                    gstin,
                    fromDate = fromDate.ToString("dd-MM-yyyy"),
                    toDate = toDate.ToString("dd-MM-yyyy"),
                    action = "GETNOTICES",
                    pageToken
                }
                : new
                {
                    gstin,
                    fromDate = fromDate.ToString("dd-MM-yyyy"),
                    toDate = toDate.ToString("dd-MM-yyyy"),
                    action = "GETNOTICES"
                };

            var response = await SendAuthenticatedRequestAsync<WhiteBooksNoticesResponse>(
                HttpMethod.Post,
                "/gsp/v1/notices/list",
                accessToken,
                request,
                correlationId,
                cancellationToken);

            if (response.Status == "SUCCESS")
            {
                return new GspNoticesResult
                {
                    Success = true,
                    Notices = MapNotices(response.Notices ?? []),
                    TotalCount = response.TotalCount ?? response.Notices?.Count ?? 0,
                    HasMore = response.HasMore ?? false,
                    NextPageToken = response.NextPageToken,
                    CorrelationId = correlationId
                };
            }

            return new GspNoticesResult
            {
                Success = false,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                CorrelationId = correlationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch notices for GSTIN {Gstin}", MaskGstin(gstin));
            return new GspNoticesResult
            {
                Success = false,
                ErrorCode = "GSP_ERROR",
                ErrorMessage = ex.Message,
                CorrelationId = correlationId
            };
        }
    }

    public async Task<GspDocumentResult> DownloadNoticeDocumentAsync(
        string accessToken,
        string gstin,
        string noticeId,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "Downloading document for notice {NoticeId}, CorrelationId: {CorrelationId}",
                noticeId,
                correlationId);

            // Create request inside retry lambda - HttpRequestMessage cannot be reused after sending
            // URL-encode parameters to prevent injection attacks
            var encodedNoticeId = Uri.EscapeDataString(noticeId);
            var encodedGstin = Uri.EscapeDataString(gstin);
            var response = await _retryPolicy.ExecuteAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"/gsp/v1/notices/{encodedNoticeId}/document?gstin={encodedGstin}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Add("X-Correlation-Id", correlationId);
                request.Headers.Add("X-Client-Id", _options.ClientId);
                return _httpClient.SendAsync(request, cancellationToken);
            });

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var contentDisposition = response.Content.Headers.ContentDisposition;

                return new GspDocumentResult
                {
                    Success = true,
                    Content = content,
                    FileName = contentDisposition?.FileName?.Trim('"') ?? $"notice_{noticeId}.pdf",
                    ContentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf",
                    ContentLength = content.Length,
                    CorrelationId = correlationId
                };
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return new GspDocumentResult
            {
                Success = false,
                ErrorCode = response.StatusCode.ToString(),
                ErrorMessage = errorBody,
                CorrelationId = correlationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download document for notice {NoticeId}", noticeId);
            return new GspDocumentResult
            {
                Success = false,
                ErrorCode = "GSP_ERROR",
                ErrorMessage = ex.Message,
                CorrelationId = correlationId
            };
        }
    }

    public async Task<bool> ValidateTokenAsync(string accessToken, string gstin, CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            var request = new { gstin, action = "VALIDATE" };

            var response = await SendAuthenticatedRequestAsync<WhiteBooksBaseResponse>(
                HttpMethod.Post,
                "/gsp/v1/token/validate",
                accessToken,
                request,
                correlationId,
                cancellationToken);

            return response.Status == "SUCCESS";
        }
        catch
        {
            return false;
        }
    }

    private async Task<T> SendRequestAsync<T>(
        HttpMethod method,
        string endpoint,
        object? body,
        string correlationId,
        CancellationToken cancellationToken) where T : new()
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Headers.Add("X-Client-Id", _options.ClientId);
        request.Headers.Add("X-Client-Secret", _options.ClientSecret);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _retryPolicy.ExecuteAsync(
            () => _httpClient.SendAsync(request, cancellationToken));

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "WhiteBooks API error: {StatusCode} - {Body}",
                response.StatusCode,
                responseBody);
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions) ?? new T();
    }

    private async Task<T> SendAuthenticatedRequestAsync<T>(
        HttpMethod method,
        string endpoint,
        string accessToken,
        object? body,
        string correlationId,
        CancellationToken cancellationToken) where T : new()
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Correlation-Id", correlationId);
        request.Headers.Add("X-Client-Id", _options.ClientId);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _retryPolicy.ExecuteAsync(
            () => _httpClient.SendAsync(request, cancellationToken));

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "WhiteBooks API error: {StatusCode} - {Body}",
                response.StatusCode,
                responseBody);
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions) ?? new T();
    }

    private static List<GspNotice> MapNotices(List<WhiteBooksNotice> notices)
    {
        return notices.Select(n => new GspNotice
        {
            NoticeId = n.NoticeId ?? string.Empty,
            ReferenceNumber = n.ReferenceNumber,
            NoticeType = n.NoticeType,
            NoticeCategory = n.Category,
            Section = n.Section,
            Subject = n.Subject,
            IssueDate = ParseDate(n.IssueDate),
            DueDate = ParseDate(n.DueDate),
            TaxAmount = n.TaxAmount,
            PenaltyAmount = n.PenaltyAmount,
            InterestAmount = n.InterestAmount,
            IssuingAuthority = n.IssuingAuthority,
            IssuingOfficer = n.IssuingOfficer,
            Jurisdiction = n.Jurisdiction,
            Status = n.Status,
            FinancialYear = n.FinancialYear,
            PeriodFrom = ParseDate(n.PeriodFrom),
            PeriodTo = ParseDate(n.PeriodTo),
            HasDocument = n.HasDocument ?? false,
            DocumentFormat = n.DocumentFormat
        }).ToList();
    }

    private static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;

        // Try common formats
        string[] formats = ["dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy"];
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format, null, System.Globalization.DateTimeStyles.None, out var date))
                return date;
        }

        return DateTime.TryParse(dateStr, out var parsed) ? parsed : null;
    }

    private static string MaskGstin(string gstin)
    {
        if (string.IsNullOrEmpty(gstin) || gstin.Length < 6)
            return "***";
        return gstin[..2] + "***" + gstin[^4..];
    }

    // WhiteBooks API response classes
    private class WhiteBooksBaseResponse
    {
        public string? Status { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private class WhiteBooksOtpResponse : WhiteBooksBaseResponse
    {
        public string? SessionId { get; set; }
        public string? MaskedDestination { get; set; }
        public string? DestinationType { get; set; }
        public int? ExpirySeconds { get; set; }
    }

    private class WhiteBooksTokenResponse : WhiteBooksBaseResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int? ExpiresIn { get; set; }
        public int? RefreshTokenExpiresIn { get; set; }
        public string? SessionId { get; set; }
    }

    private class WhiteBooksNoticesResponse : WhiteBooksBaseResponse
    {
        public List<WhiteBooksNotice>? Notices { get; set; }
        public int? TotalCount { get; set; }
        public bool? HasMore { get; set; }
        public string? NextPageToken { get; set; }
    }

    private class WhiteBooksNotice
    {
        public string? NoticeId { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? NoticeType { get; set; }
        public string? Category { get; set; }
        public string? Section { get; set; }
        public string? Subject { get; set; }
        public string? IssueDate { get; set; }
        public string? DueDate { get; set; }
        public decimal? TaxAmount { get; set; }
        public decimal? PenaltyAmount { get; set; }
        public decimal? InterestAmount { get; set; }
        public string? IssuingAuthority { get; set; }
        public string? IssuingOfficer { get; set; }
        public string? Jurisdiction { get; set; }
        public string? Status { get; set; }
        public string? FinancialYear { get; set; }
        public string? PeriodFrom { get; set; }
        public string? PeriodTo { get; set; }
        public bool? HasDocument { get; set; }
        public string? DocumentFormat { get; set; }
    }
}
