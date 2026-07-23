using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.AIChat;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class ConversationTruncationTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    private sealed class StubTenantContext : ITenantContext
    {
        public Guid? OrganizationId { get; private set; }
        public bool BypassTenantFilter => false;
        public void SetOrganizationId(Guid organizationId) => OrganizationId = organizationId;
        public void DisableTenantFilter() { }
    }

    private ConversationService CreateService(ApplicationDbContext db)
    {
        var tenant = new StubTenantContext();
        tenant.SetOrganizationId(_orgId);
        return new ConversationService(
            db,
            Mock.Of<IPromptBuilderService>(),
            tenant,
            NullLogger<ConversationService>.Instance);
    }

    private (NoticeConversation conversation, List<NoticeMessage> messages) SeedConversation(
        ApplicationDbContext db,
        int messagePairs)
    {
        var conversation = new NoticeConversation
        {
            NoticeId = Guid.NewGuid(),
            UserId = _userId,
            OrganizationId = _orgId,
            MessageCount = messagePairs * 2,
            TotalTokens = messagePairs * 2 * 10,
        };
        db.NoticeConversations.Add(conversation);

        var messages = new List<NoticeMessage>();
        var baseTime = DateTime.UtcNow.AddMinutes(-messagePairs * 2);
        for (var i = 0; i < messagePairs; i++)
        {
            messages.Add(new NoticeMessage
            {
                ConversationId = conversation.Id,
                Role = MessageRole.User,
                Content = $"question {i}",
                TokenCount = 10,
                CreatedAt = baseTime.AddMinutes(i * 2),
            });
            messages.Add(new NoticeMessage
            {
                ConversationId = conversation.Id,
                Role = MessageRole.Assistant,
                Content = $"answer {i}",
                TokenCount = 10,
                CreatedAt = baseTime.AddMinutes(i * 2 + 1),
            });
        }
        db.NoticeMessages.AddRange(messages);
        conversation.LastMessageAt = messages[^1].CreatedAt;
        db.SaveChanges();

        return (conversation, messages);
    }

    [Fact]
    public async Task Removes_the_edited_message_and_everything_after_it()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (conversation, messages) = SeedConversation(db, messagePairs: 3);
        var service = CreateService(db);

        // Edit the second question (index 2): it and the 3 messages after it go
        await service.TruncateFromMessageAsync(conversation.Id, messages[2].Id, _userId);

        var remaining = await db.NoticeMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        remaining.Should().HaveCount(2);
        remaining.Select(m => m.Content).Should().Equal("question 0", "answer 0");

        var updated = await db.NoticeConversations.FindAsync(conversation.Id);
        updated!.MessageCount.Should().Be(2);
        updated.TotalTokens.Should().Be(20);
        updated.LastMessageAt.Should().Be(remaining[^1].CreatedAt);
    }

    [Fact]
    public async Task Editing_the_first_message_empties_the_conversation()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (conversation, messages) = SeedConversation(db, messagePairs: 2);
        var service = CreateService(db);

        await service.TruncateFromMessageAsync(conversation.Id, messages[0].Id, _userId);

        var remaining = await db.NoticeMessages
            .CountAsync(m => m.ConversationId == conversation.Id);
        remaining.Should().Be(0);

        var updated = await db.NoticeConversations.FindAsync(conversation.Id);
        updated!.MessageCount.Should().Be(0);
        updated.TotalTokens.Should().Be(0);
        updated.LastMessageAt.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_editing_an_assistant_message()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (conversation, messages) = SeedConversation(db, messagePairs: 1);
        var service = CreateService(db);

        var act = () => service.TruncateFromMessageAsync(conversation.Id, messages[1].Id, _userId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*user messages*");
    }

    [Fact]
    public async Task Throws_not_found_for_another_users_conversation()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (conversation, messages) = SeedConversation(db, messagePairs: 1);
        var service = CreateService(db);

        var act = () => service.TruncateFromMessageAsync(conversation.Id, messages[0].Id, Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Preserves_audit_logs_by_detaching_them_from_removed_messages()
    {
        using var db = BillingTestDbContextFactory.Create();
        var (conversation, messages) = SeedConversation(db, messagePairs: 1);

        var audit = new AIAuditLog
        {
            ConversationId = conversation.Id,
            MessageId = messages[1].Id,
            UserId = _userId,
            OrganizationId = _orgId,
            ModelId = "gpt-4o",
        };
        db.AIAuditLogs.Add(audit);
        db.SaveChanges();

        var service = CreateService(db);
        await service.TruncateFromMessageAsync(conversation.Id, messages[0].Id, _userId);

        var survivingAudit = await db.AIAuditLogs.FindAsync(audit.Id);
        survivingAudit.Should().NotBeNull();
        survivingAudit!.MessageId.Should().BeNull();
        survivingAudit.ConversationId.Should().Be(conversation.Id);
    }
}
