using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for managing WhatsApp phone verification.
/// </summary>
public interface IWhatsAppVerificationService
{
    /// <summary>
    /// Initiate verification for a user from the app.
    /// Creates verification record and sends OTP via in-app notification.
    /// </summary>
    Task<(bool Success, DateTime? ExpiresAt, string? Message)> InitiateVerificationAsync(
        Guid userId,
        string phoneNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Initiate verification from the WhatsApp bot.
    /// Called when user provides their email in the bot conversation.
    /// </summary>
    Task<(bool Success, string? Message, Guid? VerificationId)> InitiateVerificationFromBotAsync(
        string email,
        string phoneNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Verify the OTP code.
    /// </summary>
    Task<(bool Success, Guid? UserId, string? Message)> VerifyCodeAsync(
        Guid verificationId,
        string code,
        CancellationToken ct = default);

    /// <summary>
    /// Verify code by phone number (for bot flow).
    /// </summary>
    Task<(bool Success, Guid? UserId, string? Message)> VerifyCodeByPhoneAsync(
        string phoneNumber,
        string code,
        CancellationToken ct = default);

    /// <summary>
    /// Get pending verification for a phone number.
    /// </summary>
    Task<WhatsAppVerification?> GetPendingVerificationAsync(
        string phoneNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Clean up expired verifications.
    /// </summary>
    Task CleanupExpiredVerificationsAsync(CancellationToken ct = default);
}
