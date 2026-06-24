using System.Text.Json;
using System.Text.Json.Serialization;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Redis-backed admin session management service.
/// </summary>
public class AdminSessionService : IAdminSessionService
{
    private readonly IDistributedCache _cache;
    private readonly ApplicationDbContext _dbContext;
    private readonly AdminAuthOptions _options;
    private readonly ILogger<AdminSessionService> _logger;

    private const string SessionKeyPrefix = "admin:session:";
    private const string AdminSessionsKeyPrefix = "admin:sessions:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public AdminSessionService(
        IDistributedCache cache,
        ApplicationDbContext dbContext,
        IOptions<AdminAuthOptions> options,
        ILogger<AdminSessionService> logger)
    {
        _cache = cache;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdminSession> CreateSessionAsync(
        Guid adminId,
        string ipAddress,
        string userAgent,
        string? deviceFingerprint = null)
    {
        var session = new AdminSession
        {
            AdminUserId = adminId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceFingerprint = deviceFingerprint,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_options.SessionTimeoutMinutes),
            LastActivityAt = DateTime.UtcNow
        };

        // Store in database for persistence
        _dbContext.AdminSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Cache session data for fast lookups
        await CacheSessionAsync(session);

        // Track session in admin's session set
        await AddToAdminSessionSetAsync(adminId, session.Id);

        _logger.LogInformation(
            "Created admin session {SessionId} for admin {AdminId} from {IpAddress}",
            session.Id, adminId, ipAddress);

        return session;
    }

    public async Task<AdminSession?> GetSessionAsync(string sessionId)
    {
        // Try cache first
        var cached = await _cache.GetStringAsync(SessionKeyPrefix + sessionId);
        if (!string.IsNullOrEmpty(cached))
        {
            var session = JsonSerializer.Deserialize<AdminSession>(cached, JsonOptions);
            if (session != null && session.ExpiresAt > DateTime.UtcNow && session.IsActive)
            {
                return session;
            }
        }

        // Fall back to database
        var dbSession = await _dbContext.AdminSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.IsActive && s.ExpiresAt > DateTime.UtcNow);

        if (dbSession != null)
        {
            await CacheSessionAsync(dbSession);
        }

        return dbSession;
    }

    public async Task<bool> ValidateAndRefreshAsync(string sessionId, string? ipAddress = null)
    {
        var session = await GetSessionAsync(sessionId);
        if (session == null)
        {
            return false;
        }

        // Check if session has timed out due to inactivity
        var idleTimeout = DateTime.UtcNow.AddMinutes(-_options.SessionTimeoutMinutes);
        if (session.LastActivityAt < idleTimeout)
        {
            await InvalidateSessionAsync(sessionId);
            _logger.LogInformation("Admin session {SessionId} timed out due to inactivity", sessionId);
            return false;
        }

        // Update activity timestamp
        await UpdateActivityAsync(sessionId);

        return true;
    }

    public async Task UpdateActivityAsync(string sessionId)
    {
        var session = await _dbContext.AdminSessions.FindAsync(sessionId);
        if (session != null && session.IsActive)
        {
            session.LastActivityAt = DateTime.UtcNow;
            session.ExpiresAt = DateTime.UtcNow.AddMinutes(_options.SessionTimeoutMinutes);
            await _dbContext.SaveChangesAsync();

            // Update cache
            await CacheSessionAsync(session);
        }
    }

    public async Task InvalidateSessionAsync(string sessionId)
    {
        var session = await _dbContext.AdminSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.IsActive = false;
            session.InvalidatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Remove from cache
            await _cache.RemoveAsync(SessionKeyPrefix + sessionId);

            // Remove from admin's session set
            await RemoveFromAdminSessionSetAsync(session.AdminUserId, sessionId);

            _logger.LogInformation("Invalidated admin session {SessionId}", sessionId);
        }
    }

    public async Task InvalidateAllSessionsAsync(Guid adminId, string? exceptSessionId = null)
    {
        var sessions = await _dbContext.AdminSessions
            .Where(s => s.AdminUserId == adminId && s.IsActive)
            .ToListAsync();

        foreach (var session in sessions)
        {
            if (exceptSessionId != null && session.Id == exceptSessionId)
            {
                continue;
            }

            session.IsActive = false;
            session.InvalidatedAt = DateTime.UtcNow;
            await _cache.RemoveAsync(SessionKeyPrefix + session.Id);
        }

        await _dbContext.SaveChangesAsync();

        // Clear admin's session set
        await _cache.RemoveAsync(AdminSessionsKeyPrefix + adminId);

        // Re-add the preserved session if any
        if (exceptSessionId != null)
        {
            await AddToAdminSessionSetAsync(adminId, exceptSessionId);
        }

        _logger.LogInformation(
            "Invalidated all sessions for admin {AdminId} except {ExceptSessionId}",
            adminId, exceptSessionId ?? "none");
    }

    public async Task<List<AdminSession>> GetActiveSessionsAsync(Guid adminId)
    {
        return await _dbContext.AdminSessions
            .Where(s => s.AdminUserId == adminId && s.IsActive && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync();
    }

    public async Task<int> GetActiveSessionCountAsync(Guid adminId)
    {
        return await _dbContext.AdminSessions
            .CountAsync(s => s.AdminUserId == adminId && s.IsActive && s.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<bool> IsSessionLimitExceededAsync(Guid adminId)
    {
        if (_options.MaxConcurrentSessions == 0)
        {
            return false; // Unlimited
        }

        var count = await GetActiveSessionCountAsync(adminId);
        return count >= _options.MaxConcurrentSessions;
    }

    public async Task CleanupExpiredSessionsAsync()
    {
        var expiredSessions = await _dbContext.AdminSessions
            .Where(s => s.IsActive && s.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        foreach (var session in expiredSessions)
        {
            session.IsActive = false;
            session.InvalidatedAt = DateTime.UtcNow;
            await _cache.RemoveAsync(SessionKeyPrefix + session.Id);
        }

        await _dbContext.SaveChangesAsync();

        if (expiredSessions.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired admin sessions", expiredSessions.Count);
        }
    }

    private async Task CacheSessionAsync(AdminSession session)
    {
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = session.ExpiresAt
        };

        var json = JsonSerializer.Serialize(session, JsonOptions);
        await _cache.SetStringAsync(SessionKeyPrefix + session.Id, json, cacheOptions);
    }

    private async Task AddToAdminSessionSetAsync(Guid adminId, string sessionId)
    {
        var key = AdminSessionsKeyPrefix + adminId;
        var existing = await _cache.GetStringAsync(key);
        var sessions = string.IsNullOrEmpty(existing)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(existing) ?? [];

        if (!sessions.Contains(sessionId))
        {
            sessions.Add(sessionId);
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(sessions));
        }
    }

    private async Task RemoveFromAdminSessionSetAsync(Guid adminId, string sessionId)
    {
        var key = AdminSessionsKeyPrefix + adminId;
        var existing = await _cache.GetStringAsync(key);
        if (!string.IsNullOrEmpty(existing))
        {
            var sessions = JsonSerializer.Deserialize<List<string>>(existing) ?? [];
            sessions.Remove(sessionId);
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(sessions));
        }
    }
}
