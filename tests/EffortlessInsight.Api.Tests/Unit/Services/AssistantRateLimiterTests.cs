using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.AIChat;
using EffortlessInsight.Api.Services.Assistant;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class AssistantRateLimiterTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private static AssistantRateLimiter CreateLimiter(
        ApplicationDbContext db, int perMinute = 2, int perHour = 100)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AIChatOptions
        {
            RateLimiting = new RateLimitingOptions
            {
                MessagesPerMinute = perMinute,
                MessagesPerHour = perHour,
            }
        });
        return new AssistantRateLimiter(db, options, NullLogger<AssistantRateLimiter>.Instance);
    }

    private void SeedMessages(
        ApplicationDbContext db, int count, TimeSpan age,
        string role = AssistantMessageRole.Assistant, bool isError = false)
    {
        var conversation = new AssistantConversation
        {
            OrganizationId = Guid.NewGuid(),
            UserId = _userId,
        };
        db.AssistantConversations.Add(conversation);
        for (var i = 0; i < count; i++)
        {
            db.AssistantMessages.Add(new AssistantMessage
            {
                ConversationId = conversation.Id,
                Role = role,
                Content = $"m{i}",
                IsError = isError,
                CreatedAt = DateTime.UtcNow - age,
            });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task Allows_under_limit()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, 1, TimeSpan.FromSeconds(10));
        await CreateLimiter(db).Invoking(l => l.EnforceAsync(_userId)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Blocks_over_per_minute_limit()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, 2, TimeSpan.FromSeconds(10));
        await CreateLimiter(db, perMinute: 2)
            .Invoking(l => l.EnforceAsync(_userId))
            .Should().ThrowAsync<ChatRateLimitExceededException>();
    }

    [Fact]
    public async Task User_and_error_messages_do_not_count()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, 5, TimeSpan.FromSeconds(10), role: AssistantMessageRole.User);
        SeedMessages(db, 5, TimeSpan.FromSeconds(10), isError: true);
        await CreateLimiter(db, perMinute: 2)
            .Invoking(l => l.EnforceAsync(_userId))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Blocks_over_hourly_limit_with_retry_after()
    {
        using var db = BillingTestDbContextFactory.Create();
        SeedMessages(db, 3, TimeSpan.FromMinutes(30));
        var act = () => CreateLimiter(db, perMinute: 10, perHour: 3).EnforceAsync(_userId);
        var ex = (await act.Should().ThrowAsync<ChatRateLimitExceededException>()).Which;
        ex.RetryAfterSeconds.Should().BeInRange(1, 3600);
    }
}
