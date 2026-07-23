using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.AIChat;

/// <summary>
/// Thrown when a user exceeds their AI chat message quota.
/// </summary>
public class ChatRateLimitExceededException : Exception
{
    public int RetryAfterSeconds { get; }

    public ChatRateLimitExceededException(string message, int retryAfterSeconds)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}

/// <summary>
/// Enforces per-user rate limits for AI chat messages.
/// </summary>
public interface IChatRateLimiter
{
    /// <summary>
    /// Throws <see cref="ChatRateLimitExceededException"/> when the user is over quota.
    /// </summary>
    Task EnforceAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Database-backed rate limiter using sliding windows over recent assistant messages.
/// Assistant messages are 1:1 with AI provider calls (sends and regenerations), so
/// counting them limits actual AI usage. Works across instances and restarts.
/// </summary>
public class ChatRateLimiter : IChatRateLimiter
{
    private readonly ApplicationDbContext _db;
    private readonly RateLimitingOptions _options;
    private readonly ILogger<ChatRateLimiter> _logger;

    public ChatRateLimiter(
        ApplicationDbContext db,
        IOptions<AIChatOptions> options,
        ILogger<ChatRateLimiter> logger)
    {
        _db = db;
        _options = options.Value.RateLimiting;
        _logger = logger;
    }

    public async Task EnforceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);

        var timestamps = await _db.NoticeMessages
            .Where(m => m.Conversation.UserId == userId)
            .Where(m => m.Role == MessageRole.Assistant)
            .Where(m => m.CreatedAt >= hourAgo)
            .Select(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        if (_options.MessagesPerHour > 0 && timestamps.Count >= _options.MessagesPerHour)
        {
            var retryAfter = RetryAfterSeconds(timestamps.Min().AddHours(1), now);
            _logger.LogWarning(
                "User {UserId} exceeded hourly AI chat limit ({Count}/{Limit})",
                userId, timestamps.Count, _options.MessagesPerHour);
            throw new ChatRateLimitExceededException(
                $"You've reached the AI chat limit of {_options.MessagesPerHour} messages per hour. " +
                $"Please try again in about {FormatWait(retryAfter)}.",
                retryAfter);
        }

        var minuteAgo = now.AddMinutes(-1);
        var lastMinute = timestamps.Where(t => t >= minuteAgo).ToList();
        if (_options.MessagesPerMinute > 0 && lastMinute.Count >= _options.MessagesPerMinute)
        {
            var retryAfter = RetryAfterSeconds(lastMinute.Min().AddMinutes(1), now);
            _logger.LogWarning(
                "User {UserId} exceeded per-minute AI chat limit ({Count}/{Limit})",
                userId, lastMinute.Count, _options.MessagesPerMinute);
            throw new ChatRateLimitExceededException(
                $"You're sending messages too quickly (limit: {_options.MessagesPerMinute} per minute). " +
                $"Please try again in about {FormatWait(retryAfter)}.",
                retryAfter);
        }
    }

    private static int RetryAfterSeconds(DateTime windowResetsAt, DateTime now) =>
        Math.Max(1, (int)Math.Ceiling((windowResetsAt - now).TotalSeconds));

    private static string FormatWait(int seconds) =>
        seconds < 90 ? $"{seconds} seconds" : $"{(int)Math.Ceiling(seconds / 60.0)} minutes";
}
