using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.AIChat;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class ChatRateLimiterTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private static ChatRateLimiter CreateLimiter(
        ApplicationDbContext db,
        int messagesPerMinute = 2,
        int messagesPerHour = 100)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AIChatOptions
        {
            RateLimiting = new RateLimitingOptions
            {
                MessagesPerMinute = messagesPerMinute,
                MessagesPerHour = messagesPerHour,
            }
        });
        return new ChatRateLimiter(db, options, NullLogger<ChatRateLimiter>.Instance);
    }

    private static void SeedMessages(
        ApplicationDbContext db,
        Guid userId,
        int count,
        TimeSpan age,
        string role = MessageRole.Assistant)
    {
        var conversation = new NoticeConversation
        {
            NoticeId = Guid.NewGuid(),
            UserId = userId,
            OrganizationId = Guid.NewGuid(),
        };
        db.NoticeConversations.Add(conversation);

        for (var i = 0; i < count; i++)
        {
            db.NoticeMessages.Add(new NoticeMessage
            {
                ConversationId = conversation.Id,
                Role = role,
                Content = $"message {i}",
                CreatedAt = DateTime.UtcNow - age,
            });
        }

        db.SaveChanges();
    }

    [Fact]
    public async Task Allows_messages_under_the_per_minute_limit()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, _userId, count: 1, age: TimeSpan.FromSeconds(10));
        var limiter = CreateLimiter(db, messagesPerMinute: 2);

        var act = () => limiter.EnforceAsync(_userId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Blocks_the_third_message_within_a_minute_when_limit_is_2()
    {
        using var db = BillingTestDbContextFactory.Create();
        // Two AI replies already produced within the last minute
        SeedMessages(db, _userId, count: 2, age: TimeSpan.FromSeconds(20));
        var limiter = CreateLimiter(db, messagesPerMinute: 2);

        var act = () => limiter.EnforceAsync(_userId);

        var ex = (await act.Should().ThrowAsync<ChatRateLimitExceededException>()).Which;
        ex.Message.Should().Contain("2 per minute");
        ex.RetryAfterSeconds.Should().BeInRange(1, 60);
    }

    [Fact]
    public async Task Messages_older_than_a_minute_do_not_count_toward_the_minute_limit()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, _userId, count: 5, age: TimeSpan.FromMinutes(5));
        var limiter = CreateLimiter(db, messagesPerMinute: 2);

        var act = () => limiter.EnforceAsync(_userId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task User_messages_do_not_count_only_assistant_replies_do()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, _userId, count: 5, age: TimeSpan.FromSeconds(10), role: MessageRole.User);
        var limiter = CreateLimiter(db, messagesPerMinute: 2);

        var act = () => limiter.EnforceAsync(_userId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Other_users_messages_do_not_count()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, Guid.NewGuid(), count: 5, age: TimeSpan.FromSeconds(10));
        var limiter = CreateLimiter(db, messagesPerMinute: 2);

        var act = () => limiter.EnforceAsync(_userId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Blocks_when_hourly_limit_is_reached_even_if_minute_window_is_clear()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, _userId, count: 3, age: TimeSpan.FromMinutes(30));
        var limiter = CreateLimiter(db, messagesPerMinute: 10, messagesPerHour: 3);

        var act = () => limiter.EnforceAsync(_userId);

        var ex = (await act.Should().ThrowAsync<ChatRateLimitExceededException>()).Which;
        ex.Message.Should().Contain("per hour");
        ex.RetryAfterSeconds.Should().BeInRange(1, 3600);
    }

    [Fact]
    public async Task Limit_of_zero_disables_the_window()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, _userId, count: 50, age: TimeSpan.FromSeconds(10));
        var limiter = CreateLimiter(db, messagesPerMinute: 0, messagesPerHour: 0);

        var act = () => limiter.EnforceAsync(_userId);

        await act.Should().NotThrowAsync();
    }
}
