using System.Security.Cryptography;
using EffortlessInsight.Api.DTOs;
using Microsoft.Extensions.Caching.Distributed;

namespace EffortlessInsight.Api.Services.Auth;

public interface IOtpService
{
    Task<OtpResponse> RequestOtpAsync(string mobile, string purpose, string ipAddress);
    Task<bool> VerifyOtpAsync(string mobile, string otp, string purpose);
    Task InvalidateOtpAsync(string mobile, string purpose);
}

public interface ISmsService
{
    Task SendSmsAsync(string mobile, string message);
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
        // Console/log-only SMS provider for development
        _logger.LogInformation("=================================================");
        _logger.LogInformation("SMS to {Mobile}: {Message}", mobile, message);
        _logger.LogInformation("=================================================");
        return Task.CompletedTask;
    }
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

        // Send SMS
        var message = $"Your EffortlessInsight verification code is: {otp}. Valid for {_expiryMinutes} minutes.";
        await _smsService.SendSmsAsync(normalizedMobile, message);

        _logger.LogInformation("OTP sent to {Mobile} for {Purpose}", MaskMobile(normalizedMobile), purpose);

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
