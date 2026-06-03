using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Auth;

public interface ISessionService
{
    Task<SessionListResponse> GetUserSessionsAsync(Guid userId, string currentJti);
    Task RevokeSessionAsync(Guid userId, Guid sessionId, string currentJti);
    Task RevokeAllSessionsExceptCurrentAsync(Guid userId, string currentJti);
}

public class SessionService : ISessionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<SessionService> _logger;

    public SessionService(ApplicationDbContext dbContext, ILogger<SessionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<SessionListResponse> GetUserSessionsAsync(Guid userId, string currentJti)
    {
        var now = DateTime.UtcNow;

        var sessions = await _dbContext.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastActiveAt)
            .Select(s => new SessionDto(
                s.Id,
                s.DeviceName,
                s.Platform,
                s.IpAddress,
                s.LocationCity != null && s.LocationCountry != null
                    ? $"{s.LocationCity}, {s.LocationCountry}"
                    : s.LocationCountry,
                s.LastActiveAt,
                s.CreatedAt,
                s.RefreshTokenJti == currentJti
            ))
            .ToListAsync();

        var currentSession = sessions.FirstOrDefault(s => s.IsCurrent);
        var currentSessionId = currentSession?.Id ?? Guid.Empty;

        return new SessionListResponse(currentSessionId, sessions);
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, string currentJti)
    {
        var session = await _dbContext.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null);

        if (session == null)
        {
            throw new InvalidOperationException("SESSION_NOT_FOUND");
        }

        // Prevent revoking current session through this endpoint
        if (session.RefreshTokenJti == currentJti)
        {
            throw new InvalidOperationException("CANNOT_REVOKE_CURRENT_SESSION");
        }

        session.RevokedAt = DateTime.UtcNow;
        session.RevokedReason = "user_revoked";

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Session {SessionId} revoked for user {UserId}", sessionId, userId);
    }

    public async Task RevokeAllSessionsExceptCurrentAsync(Guid userId, string currentJti)
    {
        var sessions = await _dbContext.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.RefreshTokenJti != currentJti)
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.RevokedAt = DateTime.UtcNow;
            session.RevokedReason = "user_revoked_all";
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Revoked {Count} sessions for user {UserId} (except current)", sessions.Count, userId);
    }
}
