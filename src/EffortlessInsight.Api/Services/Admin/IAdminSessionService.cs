using EffortlessInsight.Api.Data.Entities.Admin;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for managing admin sessions with Redis backing.
/// </summary>
public interface IAdminSessionService
{
    /// <summary>
    /// Create a new admin session.
    /// </summary>
    /// <param name="adminId">Admin user ID.</param>
    /// <param name="ipAddress">Client IP address.</param>
    /// <param name="userAgent">Client user agent.</param>
    /// <param name="deviceFingerprint">Optional device fingerprint.</param>
    /// <returns>Created session with ID.</returns>
    Task<AdminSession> CreateSessionAsync(
        Guid adminId,
        string ipAddress,
        string userAgent,
        string? deviceFingerprint = null);

    /// <summary>
    /// Get session by ID.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <returns>Session if found and valid.</returns>
    Task<AdminSession?> GetSessionAsync(string sessionId);

    /// <summary>
    /// Validate a session and refresh its activity.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="ipAddress">Current client IP.</param>
    /// <returns>True if session is valid.</returns>
    Task<bool> ValidateAndRefreshAsync(string sessionId, string? ipAddress = null);

    /// <summary>
    /// Update session's last activity timestamp.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    Task UpdateActivityAsync(string sessionId);

    /// <summary>
    /// Invalidate (terminate) a session.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    Task InvalidateSessionAsync(string sessionId);

    /// <summary>
    /// Invalidate all sessions for an admin user.
    /// </summary>
    /// <param name="adminId">Admin user ID.</param>
    /// <param name="exceptSessionId">Optional session ID to preserve.</param>
    Task InvalidateAllSessionsAsync(Guid adminId, string? exceptSessionId = null);

    /// <summary>
    /// Get all active sessions for an admin user.
    /// </summary>
    /// <param name="adminId">Admin user ID.</param>
    /// <returns>List of active sessions.</returns>
    Task<List<AdminSession>> GetActiveSessionsAsync(Guid adminId);

    /// <summary>
    /// Get count of active sessions for an admin user.
    /// </summary>
    /// <param name="adminId">Admin user ID.</param>
    /// <returns>Number of active sessions.</returns>
    Task<int> GetActiveSessionCountAsync(Guid adminId);

    /// <summary>
    /// Check if admin has exceeded maximum concurrent sessions.
    /// </summary>
    /// <param name="adminId">Admin user ID.</param>
    /// <returns>True if limit exceeded.</returns>
    Task<bool> IsSessionLimitExceededAsync(Guid adminId);

    /// <summary>
    /// Clean up expired sessions.
    /// </summary>
    Task CleanupExpiredSessionsAsync();
}
