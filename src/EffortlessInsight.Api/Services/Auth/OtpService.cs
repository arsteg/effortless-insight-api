using System.Security.Cryptography;
using EffortlessInsight.Api.DTOs;
using Microsoft.Extensions.Caching.Distributed;

namespace EffortlessInsight.Api.Services.Auth;

public interface IOtpService
{
    Task<OtpResponse> RequestOtpAsync(string mobile, string purpose, string ipAddress);
    Task<bool> VerifyOtpAsync(string mobile, string otp, string purpose);
    Task InvalidateOtpAsync(string mobile, string purpose);

    /// <summary>
    /// Issues a short-lived, single-use verification token proving that
    /// <paramref name="mobile"/> passed OTP verification for <paramref name="purpose"/>.
    /// The caller (e.g. registration) presents it back via
    /// <see cref="ValidateVerificationTokenAsync"/> / <see cref="ConsumeVerificationTokenAsync"/>.
    /// </summary>
    Task<string> IssueVerificationTokenAsync(string mobile, string purpose);

    /// <summary>Checks a verification token without consuming it.</summary>
    Task<bool> ValidateVerificationTokenAsync(string mobile, string purpose, string token);

    /// <summary>Checks and invalidates a verification token (single use).</summary>
    Task<bool> ConsumeVerificationTokenAsync(string mobile, string purpose, string token);
}

public interface ISmsService
{
    Task SendSmsAsync(string mobile, string message);

    /// <summary>
    /// Sends an OTP. Providers with a dedicated OTP route (e.g. 2Factor.in,
    /// which requires the OTP value rather than free text) override this;
    /// generic providers fall back to a formatted SMS.
    /// </summary>
    Task SendOtpAsync(string mobile, string otp, int expiryMinutes)
        => SendSmsAsync(mobile, $"Your EffortlessInsight verification code is: {otp}. Valid for {expiryMinutes} minutes.");
}

public class ConsoleSmsSer­vice : ISmsService
{
    private readonly ILogger<ConsoleSmsSer­vice> _logger;

    public ConsoleSmsSer­vice(ILogger<ConsoleSmsSer­vice> logger)
    {
        _logger = logger;
    }

    public Task SendSmsAsync(string mobile, string message)
    {
        // Log-only provider for local development. Message bodies can contain
        // OTPs, so they are only ever emitted in Development. If this provider
        // is somehow active in any other environment (e.g. a misconfigured
        // Production that fell back to console), the contents are NOT logged.
        if (IsDevelopment)
        {
            _logger.LogInformation("[DEV-SMS] to {Mobile}: {Message}", MaskMobile(mobile), message);
        }
        else
        {
            _logger.LogWarning(
                "Console SMS provider is active outside Development: message to {Mobile} was NOT delivered and its contents were NOT logged. Set Sms:Provider=2factor.",
                MaskMobile(mobile));
        }
        return Task.CompletedTask;
    }

    // OTP-aware path (the one OtpService actually calls). Overriding it means
    // the OTP value never flows through SendSmsAsync's message string. The
    // code is shown ONLY in Development so devs can test without a gateway;
    // in every other environment it is never written to the logs.
    public Task SendOtpAsync(string mobile, string otp, int expiryMinutes)
    {
        if (IsDevelopment)
        {
            _logger.LogInformation("[DEV-OTP] code {Otp} for {Mobile} (valid {Expiry} min)",
                otp, MaskMobile(mobile), expiryMinutes);
        }
        else
        {
            _logger.LogWarning(
                "Console SMS provider is active outside Development: OTP for {Mobile} was NOT delivered (and not logged). Set Sms:Provider=2factor.",
                MaskMobile(mobile));
        }
        return Task.CompletedTask;
    }

    private static bool IsDevelopment =>
        string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            "Development", StringComparison.OrdinalIgnoreCase);

    private static string MaskMobile(string mobile) =>
        mobile.Length < 4 ? "****" : $"******{mobile[^4..]}";
}

public class OtpService : IOtpService
{
    private readonly IDistributedCache _cache;
    private readonly ISmsService _smsService;
    private readonly ILogger<OtpService> _logger;
    private readonly IConfiguration _configuration;

    private readonly int _otpLength;
    private readonly int _expiryMinutes;
    private readonly int _maxAttempts;
    private readonly int _maxRequestsPerHour;

    public OtpService(
        IDistributedCache cache,
        ISmsService smsService,
        ILogger<OtpService> logger,
        IConfiguration configuration)
    {
        _cache = cache;
        _smsService = smsService;
        _logger = logger;
        _configuration = configuration;

        _otpLength = _configuration.GetValue("Otp:Length", 6);
        _expiryMinutes = _configuration.GetValue("Otp:ExpiryMinutes", 5);
        _maxAttempts = _configuration.GetValue("Otp:MaxAttempts", 3);
        _maxRequestsPerHour = _configuration.GetValue("Otp:MaxRequestsPerHour", 5);
    }

    public async Task<OtpResponse> RequestOtpAsync(string mobile, string purpose, string ipAddress)
    {
        var normalizedMobile = NormalizeMobile(mobile);
        var rateLimitKey = $"otp_rate:{normalizedMobile}";
        var otpKey = $"otp:{purpose}:{normalizedMobile}";

        // Check rate limit
        var rateLimitData = await GetRateLimitDataAsync(rateLimitKey);
        if (rateLimitData.RequestCount >= _maxRequestsPerHour)
        {
            var retryAfter = (int)(rateLimitData.WindowEnd - DateTime.UtcNow).TotalSeconds;
            throw new InvalidOperationException($"RATE_LIMIT_EXCEEDED:{retryAfter}");
        }

        // Check if there's an existing valid OTP (to prevent spamming)
        var existingOtpJson = await _cache.GetStringAsync(otpKey);
        if (!string.IsNullOrEmpty(existingOtpJson))
        {
            var existingOtp = System.Text.Json.JsonSerializer.Deserialize<OtpData>(existingOtpJson);
            if (existingOtp != null && existingOtp.CreatedAt.AddSeconds(60) > DateTime.UtcNow)
            {
                // Resend cooldown: wait at least 60 seconds between OTP requests
                var retryAfter = (int)(existingOtp.CreatedAt.AddSeconds(60) - DateTime.UtcNow).TotalSeconds;
                return new OtpResponse(
                    Message: "OTP already sent. Please wait before requesting again.",
                    MaskedMobile: MaskMobile(normalizedMobile),
                    ExpiresIn: (int)(existingOtp.ExpiresAt - DateTime.UtcNow).TotalSeconds,
                    RetryAfter: retryAfter
                );
            }
        }

        // Generate new OTP
        var otp = GenerateOtp(_otpLength);
        var otpData = new OtpData
        {
            Otp = otp,
            Mobile = normalizedMobile,
            Purpose = purpose,
            Attempts = 0,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_expiryMinutes),
            IpAddress = ipAddress
        };

        // Store OTP
        await _cache.SetStringAsync(
            otpKey,
            System.Text.Json.JsonSerializer.Serialize(otpData),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_expiryMinutes)
            });

        // Update rate limit
        await IncrementRateLimitAsync(rateLimitKey);

        // Send via the configured SMS provider (OTP-aware providers like
        // 2Factor.in take the OTP value; others get a formatted message).
        // The concrete provider type is logged so a wrong provider for the
        // environment (e.g. ConsoleSmsService in Production) is obvious from
        // the signup logs instead of failing silently.
        var smsProvider = _smsService.GetType().Name;
        _logger.LogInformation("Dispatching {Purpose} OTP to {Mobile} via {SmsProvider}",
            purpose, MaskMobile(normalizedMobile), smsProvider);
        try
        {
            await _smsService.SendOtpAsync(normalizedMobile, otp, _expiryMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch {Purpose} OTP to {Mobile} via {SmsProvider}",
                purpose, MaskMobile(normalizedMobile), smsProvider);
            throw;
        }

        _logger.LogInformation("{Purpose} OTP dispatched to {Mobile} via {SmsProvider}",
            purpose, MaskMobile(normalizedMobile), smsProvider);

        return new OtpResponse(
            Message: "OTP sent successfully",
            MaskedMobile: MaskMobile(normalizedMobile),
            ExpiresIn: _expiryMinutes * 60,
            RetryAfter: 60
        );
    }

    public async Task<bool> VerifyOtpAsync(string mobile, string otp, string purpose)
    {
        var normalizedMobile = NormalizeMobile(mobile);
        var otpKey = $"otp:{purpose}:{normalizedMobile}";

        var otpDataJson = await _cache.GetStringAsync(otpKey);
        if (string.IsNullOrEmpty(otpDataJson))
        {
            _logger.LogWarning("OTP verification failed: No OTP found for {Mobile}", MaskMobile(normalizedMobile));
            return false;
        }

        var otpData = System.Text.Json.JsonSerializer.Deserialize<OtpData>(otpDataJson);
        if (otpData == null)
        {
            return false;
        }

        // Check if OTP is expired
        if (otpData.ExpiresAt < DateTime.UtcNow)
        {
            await _cache.RemoveAsync(otpKey);
            _logger.LogWarning("OTP verification failed: OTP expired for {Mobile}", MaskMobile(normalizedMobile));
            return false;
        }

        // Check max attempts
        if (otpData.Attempts >= _maxAttempts)
        {
            await _cache.RemoveAsync(otpKey);
            _logger.LogWarning("OTP verification failed: Max attempts exceeded for {Mobile}", MaskMobile(normalizedMobile));
            throw new InvalidOperationException("MAX_ATTEMPTS_EXCEEDED");
        }

        // Verify OTP
        if (otpData.Otp != otp)
        {
            // Increment attempts
            otpData.Attempts++;
            await _cache.SetStringAsync(
                otpKey,
                System.Text.Json.JsonSerializer.Serialize(otpData),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = otpData.ExpiresAt
                });

            _logger.LogWarning("OTP verification failed: Invalid OTP for {Mobile} (attempt {Attempt}/{Max})",
                MaskMobile(normalizedMobile), otpData.Attempts, _maxAttempts);
            return false;
        }

        // OTP is valid - remove it
        await _cache.RemoveAsync(otpKey);
        _logger.LogInformation("OTP verified successfully for {Mobile}", MaskMobile(normalizedMobile));

        return true;
    }

    public async Task InvalidateOtpAsync(string mobile, string purpose)
    {
        var normalizedMobile = NormalizeMobile(mobile);
        var otpKey = $"otp:{purpose}:{normalizedMobile}";
        await _cache.RemoveAsync(otpKey);
    }

    // Verification tokens: 30-minute proof that a mobile passed OTP
    // verification, bound to the normalized number so changing the number
    // invalidates the proof. Single-use via ConsumeVerificationTokenAsync.
    private const int VerificationTokenExpiryMinutes = 30;

    public async Task<string> IssueVerificationTokenAsync(string mobile, string purpose)
    {
        var normalizedMobile = NormalizeMobile(mobile);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await _cache.SetStringAsync(
            VerificationTokenKey(normalizedMobile, purpose),
            token,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(VerificationTokenExpiryMinutes)
            });

        _logger.LogInformation("Issued mobile verification token for {Mobile} ({Purpose})",
            MaskMobile(normalizedMobile), purpose);
        return token;
    }

    public async Task<bool> ValidateVerificationTokenAsync(string mobile, string purpose, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalizedMobile = NormalizeMobile(mobile);
        var stored = await _cache.GetStringAsync(VerificationTokenKey(normalizedMobile, purpose));
        return !string.IsNullOrEmpty(stored)
               && CryptographicOperations.FixedTimeEquals(
                   System.Text.Encoding.UTF8.GetBytes(stored),
                   System.Text.Encoding.UTF8.GetBytes(token));
    }

    public async Task<bool> ConsumeVerificationTokenAsync(string mobile, string purpose, string token)
    {
        if (!await ValidateVerificationTokenAsync(mobile, purpose, token))
        {
            return false;
        }

        await _cache.RemoveAsync(VerificationTokenKey(NormalizeMobile(mobile), purpose));
        return true;
    }

    private static string VerificationTokenKey(string normalizedMobile, string purpose) =>
        $"otp_verified:{purpose}:{normalizedMobile}";

    private static string GenerateOtp(int length)
    {
        var max = (int)Math.Pow(10, length);
        var otp = RandomNumberGenerator.GetInt32(0, max);
        return otp.ToString().PadLeft(length, '0');
    }

    private static string NormalizeMobile(string mobile)
    {
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        return digits.Length >= 10 ? digits[^10..] : digits;
    }

    private static string MaskMobile(string mobile)
    {
        if (mobile.Length < 4)
            return "****";
        return $"******{mobile[^4..]}";
    }

    private async Task<RateLimitData> GetRateLimitDataAsync(string key)
    {
        var dataJson = await _cache.GetStringAsync(key);
        if (string.IsNullOrEmpty(dataJson))
        {
            return new RateLimitData
            {
                RequestCount = 0,
                WindowStart = DateTime.UtcNow,
                WindowEnd = DateTime.UtcNow.AddHours(1)
            };
        }

        return System.Text.Json.JsonSerializer.Deserialize<RateLimitData>(dataJson) ?? new RateLimitData
        {
            RequestCount = 0,
            WindowStart = DateTime.UtcNow,
            WindowEnd = DateTime.UtcNow.AddHours(1)
        };
    }

    private async Task IncrementRateLimitAsync(string key)
    {
        var data = await GetRateLimitDataAsync(key);

        if (data.WindowEnd < DateTime.UtcNow)
        {
            // Window expired, start new window
            data = new RateLimitData
            {
                RequestCount = 1,
                WindowStart = DateTime.UtcNow,
                WindowEnd = DateTime.UtcNow.AddHours(1)
            };
        }
        else
        {
            data.RequestCount++;
        }

        await _cache.SetStringAsync(
            key,
            System.Text.Json.JsonSerializer.Serialize(data),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = data.WindowEnd
            });
    }
}

internal class OtpData
{
    public string Otp { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}

internal class RateLimitData
{
    public int RequestCount { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
}
