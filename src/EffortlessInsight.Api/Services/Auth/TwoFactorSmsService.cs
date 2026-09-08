using System.Text.Json;

namespace EffortlessInsight.Api.Services.Auth;

/// <summary>
/// <see cref="ISmsService"/> implementation backed by 2Factor.in.
///
/// Uses 2Factor's "custom OTP value" SMS route
/// (GET {BaseUrl}/{api_key}/SMS/{phone}/{otp}[/{template}]) so OTP
/// generation, storage, rate limiting and verification all stay in the
/// existing <see cref="OtpService"/> — 2Factor is delivery only, and can be
/// swapped back to console/Twilio via the Sms:Provider setting.
///
/// The API key comes from configuration (Sms:TwoFactor:ApiKey) and must be
/// supplied via environment variables / user-secrets — never committed.
/// Provider errors are logged server-side and surfaced to callers as a
/// generic SMS_SEND_FAILED so no provider details leak to users.
/// </summary>
public class TwoFactorSmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwoFactorSmsService> _logger;

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _otpTemplateName;
    private readonly string _countryCode;

    public TwoFactorSmsService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TwoFactorSmsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _apiKey = configuration["Sms:TwoFactor:ApiKey"] ?? string.Empty;
        _baseUrl = (configuration["Sms:TwoFactor:BaseUrl"] ?? "https://2factor.in/API/V1").TrimEnd('/');
        _otpTemplateName = configuration["Sms:TwoFactor:OtpTemplateName"] ?? string.Empty;
        _countryCode = configuration["Sms:TwoFactor:CountryCode"] ?? "91";
    }

    public Task SendSmsAsync(string mobile, string message)
    {
        // 2Factor's transactional (free-text) route needs the customer's own
        // DLT-registered templates; this integration is scoped to OTP
        // delivery. Fail loudly so a misconfigured caller is caught in dev.
        _logger.LogError("TwoFactorSmsService only supports OTP SMS; free-text send to {Mobile} rejected", Mask(mobile));
        throw new NotSupportedException("The 2Factor SMS provider only supports OTP messages.");
    }

    public async Task SendOtpAsync(string mobile, string otp, int expiryMinutes)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogError("Sms:TwoFactor:ApiKey is not configured — cannot send OTP SMS");
            throw new InvalidOperationException("SMS_SEND_FAILED");
        }

        // Normalized numbers are the last 10 digits; 2Factor expects a
        // country-code-qualified number.
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        var phone = digits.Length == 10 ? $"+{_countryCode}{digits}" : $"+{digits}";

        var url = $"{_baseUrl}/{_apiKey}/SMS/{Uri.EscapeDataString(phone)}/{Uri.EscapeDataString(otp)}";
        if (!string.IsNullOrWhiteSpace(_otpTemplateName))
        {
            url += $"/{Uri.EscapeDataString(_otpTemplateName)}";
        }

        try
        {
            using var response = await _httpClient.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("2Factor OTP send failed for {Mobile}: HTTP {Status}", Mask(phone), (int)response.StatusCode);
                throw new InvalidOperationException("SMS_SEND_FAILED");
            }

            using var json = JsonDocument.Parse(body);
            var status = json.RootElement.TryGetProperty("Status", out var s) ? s.GetString() : null;
            if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                var details = json.RootElement.TryGetProperty("Details", out var d) ? d.GetString() : "unknown";
                _logger.LogError("2Factor OTP send rejected for {Mobile}: {Details}", Mask(phone), details);
                throw new InvalidOperationException("SMS_SEND_FAILED");
            }

            _logger.LogInformation("2Factor OTP SMS sent to {Mobile}", Mask(phone));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Network/parse failures — log detail server-side, generic error out
            _logger.LogError(ex, "2Factor OTP send errored for {Mobile}", Mask(phone));
            throw new InvalidOperationException("SMS_SEND_FAILED");
        }
    }

    private static string Mask(string mobile) =>
        mobile.Length < 4 ? "****" : $"******{mobile[^4..]}";
}
