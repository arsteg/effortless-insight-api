using System.Security.Cryptography;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for managing WhatsApp phone verification.
/// </summary>
public class WhatsAppVerificationService : IWhatsAppVerificationService
{
    private readonly ApplicationDbContext _db;
    private readonly MetaWhatsAppOptions _options;
    private readonly IMetaWhatsAppClient _client;
    private readonly INotificationEngineService _notificationService;
    private readonly ILogger<WhatsAppVerificationService> _logger;

    public WhatsAppVerificationService(
        ApplicationDbContext db,
        IOptions<MetaWhatsAppOptions> options,
        IMetaWhatsAppClient client,
        INotificationEngineService notificationService,
        ILogger<WhatsAppVerificationService> logger)
    {
        _db = db;
        _options = options.Value;
        _client = client;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<(bool Success, DateTime? ExpiresAt, string? Message)> InitiateVerificationAsync(
        Guid userId,
        string phoneNumber,
        CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        // Check if user exists
        var user = await _db.Users.FindAsync([userId], ct);
        if (user == null)
        {
            return (false, null, "User not found");
        }

        // Check for existing pending verification
        var existing = await _db.WhatsAppVerifications
            .Where(v => v.UserId == userId && !v.IsVerified && v.ExpiresAt > DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            // Rate limit: don't allow new verification if recent one exists
            var timeSinceCreated = DateTime.UtcNow - existing.CreatedAt;
            if (timeSinceCreated.TotalMinutes < 1)
            {
                return (false, existing.ExpiresAt, "Please wait before requesting a new code");
            }

            // Invalidate the existing verification
            existing.DeletedAt = DateTime.UtcNow;
        }

        // Generate verification code
        var code = GenerateVerificationCode();
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.VerificationCodeExpiryMinutes);

        var verification = new WhatsAppVerification
        {
            UserId = userId,
            PhoneNumber = formattedPhone,
            VerificationCode = code,
            ExpiresAt = expiresAt,
            MaxAttempts = _options.MaxVerificationAttempts,
            InitiatedFrom = "app"
        };

        _db.WhatsAppVerifications.Add(verification);
        await _db.SaveChangesAsync(ct);

        // Send in-app notification with the code
        await SendVerificationNotificationAsync(userId, code, ct);

        _logger.LogInformation(
            "WhatsApp verification initiated for user {UserId}, phone {Phone}",
            userId,
            _client.MaskPhoneNumber(phoneNumber));

        return (true, expiresAt, null);
    }

    public async Task<(bool Success, string? Message, Guid? VerificationId)> InitiateVerificationFromBotAsync(
        string email,
        string phoneNumber,
        CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        // Find user by email
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant() && u.DeletedAt == null, ct);

        if (user == null)
        {
            return (false, "No account found with this email. Please register at effortlessinsight.com", null);
        }

        // Check if phone is already linked to another user
        var existingLink = await _db.Users
            .AnyAsync(u => u.WhatsAppPhoneNumber == formattedPhone && u.Id != user.Id && u.DeletedAt == null, ct);

        if (existingLink)
        {
            return (false, "This phone number is already linked to another account", null);
        }

        // Clean up existing pending verifications for this phone
        var existingVerifications = await _db.WhatsAppVerifications
            .Where(v => v.PhoneNumber == formattedPhone && !v.IsVerified && v.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var ev in existingVerifications)
        {
            ev.DeletedAt = DateTime.UtcNow;
        }

        // Generate verification code
        var code = GenerateVerificationCode();
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.VerificationCodeExpiryMinutes);

        var verification = new WhatsAppVerification
        {
            UserId = user.Id,
            PhoneNumber = formattedPhone,
            VerificationCode = code,
            ExpiresAt = expiresAt,
            MaxAttempts = _options.MaxVerificationAttempts,
            InitiatedFrom = "bot"
        };

        _db.WhatsAppVerifications.Add(verification);
        await _db.SaveChangesAsync(ct);

        // Send in-app notification with the code
        await SendVerificationNotificationAsync(user.Id, code, ct);

        _logger.LogInformation(
            "WhatsApp verification initiated from bot for user {UserId}, phone {Phone}",
            user.Id,
            _client.MaskPhoneNumber(phoneNumber));

        return (true, null, verification.Id);
    }

    public async Task<(bool Success, Guid? UserId, string? Message)> VerifyCodeAsync(
        Guid verificationId,
        string code,
        CancellationToken ct = default)
    {
        var verification = await _db.WhatsAppVerifications
            .Include(v => v.User)
            .FirstOrDefaultAsync(v => v.Id == verificationId && v.DeletedAt == null, ct);

        if (verification == null)
        {
            return (false, null, "Verification not found");
        }

        return await VerifyCodeInternalAsync(verification, code, ct);
    }

    public async Task<(bool Success, Guid? UserId, string? Message)> VerifyCodeByPhoneAsync(
        string phoneNumber,
        string code,
        CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        var verification = await _db.WhatsAppVerifications
            .Include(v => v.User)
            .Where(v =>
                v.PhoneNumber == formattedPhone &&
                !v.IsVerified &&
                v.DeletedAt == null)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (verification == null)
        {
            return (false, null, "No pending verification found. Please start again by providing your email.");
        }

        return await VerifyCodeInternalAsync(verification, code, ct);
    }

    private async Task<(bool Success, Guid? UserId, string? Message)> VerifyCodeInternalAsync(
        WhatsAppVerification verification,
        string code,
        CancellationToken ct)
    {
        // Check expiry
        if (verification.ExpiresAt < DateTime.UtcNow)
        {
            return (false, null, "Verification code has expired. Please request a new code.");
        }

        // Check attempts
        if (verification.AttemptCount >= verification.MaxAttempts)
        {
            return (false, null, "Maximum attempts exceeded. Please request a new code.");
        }

        // Increment attempt
        verification.AttemptCount++;
        await _db.SaveChangesAsync(ct);

        // Check code
        if (!string.Equals(verification.VerificationCode, code.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var remaining = verification.MaxAttempts - verification.AttemptCount;
            return (false, null, $"Invalid code. {remaining} attempts remaining.");
        }

        // Mark as verified
        verification.IsVerified = true;
        verification.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "WhatsApp verification successful for user {UserId}",
            verification.UserId);

        return (true, verification.UserId, null);
    }

    public async Task<WhatsAppVerification?> GetPendingVerificationAsync(
        string phoneNumber,
        CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        return await _db.WhatsAppVerifications
            .Where(v =>
                v.PhoneNumber == formattedPhone &&
                !v.IsVerified &&
                v.ExpiresAt > DateTime.UtcNow &&
                v.AttemptCount < v.MaxAttempts &&
                v.DeletedAt == null)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task CleanupExpiredVerificationsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1); // Keep for 1 hour after expiry

        var expired = await _db.WhatsAppVerifications
            .Where(v => v.ExpiresAt < cutoff && v.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var verification in expired)
        {
            verification.DeletedAt = DateTime.UtcNow;
        }

        if (expired.Any())
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} expired WhatsApp verifications", expired.Count);
        }
    }

    private static string GenerateVerificationCode()
    {
        // Generate 6-digit code
        var bytes = new byte[4];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var number = Math.Abs(BitConverter.ToInt32(bytes, 0)) % 1000000;
        return number.ToString("D6");
    }

    private async Task SendVerificationNotificationAsync(Guid userId, string code, CancellationToken ct)
    {
        try
        {
            // Send in-app notification using the existing notification engine
            await _notificationService.SendAsync(new DTOs.SendNotificationRequest(
                UserId: userId,
                Type: "whatsapp_verification",
                Data: new Dictionary<string, object>
                {
                    ["title"] = "WhatsApp Verification Code",
                    ["body"] = $"Your verification code is: {code}. Enter this code in WhatsApp to link your account.",
                    ["code"] = code,
                    ["expiresInMinutes"] = _options.VerificationCodeExpiryMinutes
                }
            ), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp verification notification to user {UserId}", userId);
        }
    }
}
