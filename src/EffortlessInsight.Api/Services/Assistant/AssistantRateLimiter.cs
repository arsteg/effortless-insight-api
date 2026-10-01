using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.AIChat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.Assistant;

/// <summary>
/// Per-user rate limiter for the app-wide assistant, mirroring
/// <see cref="ChatRateLimiter"/> but counting <see cref="AssistantMessage"/> rows.
/// Reuses the AIChat rate-limit configuration so there is one knob for AI chat cost.
/// </summary>
public interface IAssistantRateLimiter
{
    /// <summary>Throws <see cref="ChatRateLimitExceededException"/> when over quota.</summary>
    Task EnforceAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class AssistantRateLimiter : IAssistantRateLimiter
{
    private readonly ApplicationDbContext _db;
    private readonly RateLimitingOptions _options;
    private readonly ILogger<AssistantRateLimiter> _logger;

    public AssistantRateLimiter(
        ApplicationDbContext db,
        IOptions<AIChatOptions> options,
        ILogger<AssistantRateLimiter> logger)
    {
        _db = db;
        _options = options.Value.RateLimiting;
        _logger = logger;
    }

    public async Task EnforceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);

        var timestamps = await _db.AssistantMessages
            .Where(m => m.Conversation.UserId == userId)
            .Where(m => m.Role == AssistantMessageRole.Assistant && !m.IsError)
            .Where(m => m.CreatedAt >= hourAgo)
            .Select(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        if (_options.MessagesPerHour > 0 && timestamps.Count >= _options.MessagesPerHour)
        {
            var retryAfter = RetryAfterSeconds(timestamps.Min().AddHours(1), now);
            _logger.LogWarning(
                "User {UserId} exceeded hourly assistant limit ({Count}/{Limit})",
                userId, timestamps.Count, _options.MessagesPerHour);
            throw new ChatRateLimitExceededException(
                $"You've reached the assistant limit of {_options.MessagesPerHour} messages per hour. " +
                "Please try again later.",
                retryAfter);
        }

        var minuteAgo = now.AddMinutes(-1);
        var lastMinute = timestamps.Count(t => t >= minuteAgo);
        if (_options.MessagesPerMinute > 0 && lastMinute >= _options.MessagesPerMinute)
        {
            _logger.LogWarning(
                "User {UserId} exceeded per-minute assistant limit ({Count}/{Limit})",
                userId, lastMinute, _options.MessagesPerMinute);
            throw new ChatRateLimitExceededException(
                "You're sending messages too quickly. Please wait a moment and try again.",
                60);
        }
    }

    private static int RetryAfterSeconds(DateTime availableAt, DateTime now) =>
        Math.Max(1, (int)Math.Ceiling((availableAt - now).TotalSeconds));
}
