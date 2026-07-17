using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for managing WhatsApp conversation sessions and state machine.
/// </summary>
public interface IWhatsAppSessionService
{
    /// <summary>
    /// Get or create a session for a phone number.
    /// </summary>
    Task<WhatsAppSession> GetOrCreateSessionAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>
    /// Get session by phone number.
    /// </summary>
    Task<WhatsAppSession?> GetSessionByPhoneAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>
    /// Get session by user ID.
    /// </summary>
    Task<WhatsAppSession?> GetSessionByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Update session state.
    /// </summary>
    Task UpdateStateAsync(
        Guid sessionId,
        string newState,
        string? pendingEmail = null,
        Guid? pendingVerificationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Link session to user after successful verification.
    /// </summary>
    Task LinkSessionToUserAsync(Guid sessionId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Update last interaction timestamp.
    /// </summary>
    Task UpdateLastInteractionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Update session context data.
    /// </summary>
    Task UpdateContextAsync(
        Guid sessionId,
        Dictionary<string, object> context,
        CancellationToken ct = default);

    /// <summary>
    /// Update session state and context atomically.
    /// </summary>
    Task UpdateStateAndContextAsync(
        Guid sessionId,
        string? newState,
        string? pendingEmail,
        Guid? pendingVerificationId,
        Dictionary<string, object>? contextUpdate,
        CancellationToken ct = default);

    /// <summary>
    /// Update current page for pagination.
    /// </summary>
    Task UpdateCurrentPageAsync(Guid sessionId, int page, CancellationToken ct = default);

    /// <summary>
    /// Check if user has an active 24-hour conversation window.
    /// </summary>
    Task<bool> IsConversationActiveAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>
    /// Check if user is linked and verified.
    /// </summary>
    Task<bool> IsUserLinkedAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>
    /// Unlink session from user (disconnect WhatsApp).
    /// </summary>
    Task UnlinkSessionAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Clean up expired sessions.
    /// </summary>
    Task CleanupExpiredSessionsAsync(CancellationToken ct = default);
}
