using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for managing WhatsApp conversation sessions and state machine.
/// </summary>
public class WhatsAppSessionService : IWhatsAppSessionService
{
    private readonly ApplicationDbContext _db;
    private readonly MetaWhatsAppOptions _options;
    private readonly IMetaWhatsAppClient _client;
    private readonly ILogger<WhatsAppSessionService> _logger;

    public WhatsAppSessionService(
        ApplicationDbContext db,
        IOptions<MetaWhatsAppOptions> options,
        IMetaWhatsAppClient client,
        ILogger<WhatsAppSessionService> logger)
    {
        _db = db;
        _options = options.Value;
        _client = client;
        _logger = logger;
    }

    public async Task<WhatsAppSession> GetOrCreateSessionAsync(string phoneNumber, CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        var session = await _db.WhatsAppSessions
            .FirstOrDefaultAsync(s => s.PhoneNumber == formattedPhone && s.DeletedAt == null, ct);

        if (session != null)
        {
            // Update last interaction
            session.LastInteractionAt = DateTime.UtcNow;
            session.SessionExpiresAt = DateTime.UtcNow.AddHours(_options.ConversationWindowHours);
            session.MessageCount++;
            await _db.SaveChangesAsync(ct);
            return session;
        }

        session = new WhatsAppSession
        {
            PhoneNumber = formattedPhone,
            CurrentState = WhatsAppSessionState.Start,
            LastInteractionAt = DateTime.UtcNow,
            SessionExpiresAt = DateTime.UtcNow.AddHours(_options.ConversationWindowHours),
            MessageCount = 1
        };

        _db.WhatsAppSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created new WhatsApp session for phone: {Phone}", _client.MaskPhoneNumber(phoneNumber));

        return session;
    }

    public async Task<WhatsAppSession?> GetSessionByPhoneAsync(string phoneNumber, CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        return await _db.WhatsAppSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.PhoneNumber == formattedPhone && s.DeletedAt == null, ct);
    }

    public async Task<WhatsAppSession?> GetSessionByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.WhatsAppSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.DeletedAt == null, ct);
    }

    public async Task UpdateStateAsync(
        Guid sessionId,
        string newState,
        string? pendingEmail = null,
        Guid? pendingVerificationId = null,
        CancellationToken ct = default)
    {
        var session = await _db.WhatsAppSessions.FindAsync([sessionId], ct);
        if (session == null)
            return;

        session.CurrentState = newState;
        session.PendingEmail = pendingEmail;
        session.PendingVerificationId = pendingVerificationId;
        session.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Updated session {SessionId} state to {State}", sessionId, newState);
    }

    public async Task LinkSessionToUserAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var session = await _db.WhatsAppSessions.FindAsync([sessionId], ct);
        if (session == null)
            return;

        // Unlink any existing session for this user
        var existingSessions = await _db.WhatsAppSessions
            .Where(s => s.UserId == userId && s.Id != sessionId && s.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var existing in existingSessions)
        {
            existing.UserId = null;
            existing.CurrentState = WhatsAppSessionState.Start;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        // Link new session
        session.UserId = userId;
        session.CurrentState = WhatsAppSessionState.Linked;
        session.PendingEmail = null;
        session.PendingVerificationId = null;
        session.UpdatedAt = DateTime.UtcNow;

        // Update user's WhatsApp fields
        var user = await _db.Users.FindAsync([userId], ct);
        if (user != null)
        {
            user.WhatsAppPhoneNumber = session.PhoneNumber;
            user.WhatsAppVerified = true;
            user.WhatsAppVerifiedAt = DateTime.UtcNow;
            user.WhatsAppOptedIn = true;
            user.WhatsAppOptedInAt = DateTime.UtcNow;
            user.WhatsAppLastMessageAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Linked WhatsApp session {SessionId} to user {UserId}", sessionId, userId);
    }

    public async Task UpdateLastInteractionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.WhatsAppSessions.FindAsync([sessionId], ct);
        if (session == null)
            return;

        session.LastInteractionAt = DateTime.UtcNow;
        session.SessionExpiresAt = DateTime.UtcNow.AddHours(_options.ConversationWindowHours);
        session.MessageCount++;

        // Update user's last message time if linked
        if (session.UserId.HasValue)
        {
            var user = await _db.Users.FindAsync([session.UserId.Value], ct);
            if (user != null)
            {
                user.WhatsAppLastMessageAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateContextAsync(
        Guid sessionId,
        Dictionary<string, object> context,
        CancellationToken ct = default)
    {
        var session = await _db.WhatsAppSessions.FindAsync([sessionId], ct);
        if (session == null)
            return;

        session.Context = context;
        session.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateCurrentPageAsync(Guid sessionId, int page, CancellationToken ct = default)
    {
        var session = await _db.WhatsAppSessions.FindAsync([sessionId], ct);
        if (session == null)
            return;

        session.CurrentPage = page;
        session.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> IsConversationActiveAsync(string phoneNumber, CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        var session = await _db.WhatsAppSessions
            .FirstOrDefaultAsync(s => s.PhoneNumber == formattedPhone && s.DeletedAt == null, ct);

        if (session == null)
            return false;

        return session.SessionExpiresAt > DateTime.UtcNow;
    }

    public async Task<bool> IsUserLinkedAsync(string phoneNumber, CancellationToken ct = default)
    {
        var formattedPhone = _client.FormatPhoneNumber(phoneNumber);

        return await _db.WhatsAppSessions
            .AnyAsync(s =>
                s.PhoneNumber == formattedPhone &&
                s.UserId != null &&
                s.CurrentState == WhatsAppSessionState.Linked &&
                s.DeletedAt == null,
                ct);
    }

    public async Task UnlinkSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await _db.WhatsAppSessions
            .Where(s => s.UserId == userId && s.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            session.UserId = null;
            session.CurrentState = WhatsAppSessionState.Start;
            session.PendingEmail = null;
            session.PendingVerificationId = null;
            session.Context = new Dictionary<string, object>();
            session.UpdatedAt = DateTime.UtcNow;
        }

        // Clear user's WhatsApp fields
        var user = await _db.Users.FindAsync([userId], ct);
        if (user != null)
        {
            user.WhatsAppPhoneNumber = null;
            user.WhatsAppVerified = false;
            user.WhatsAppVerifiedAt = null;
            user.WhatsAppOptedIn = false;
            user.WhatsAppOptedInAt = null;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Unlinked WhatsApp for user {UserId}", userId);
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7); // Keep sessions for 7 days

        var expiredSessions = await _db.WhatsAppSessions
            .Where(s => s.LastInteractionAt < cutoff && s.UserId == null && s.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var session in expiredSessions)
        {
            session.DeletedAt = DateTime.UtcNow;
        }

        if (expiredSessions.Any())
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} expired WhatsApp sessions", expiredSessions.Count);
        }
    }
}
