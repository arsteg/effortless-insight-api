using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class NoticeResponseDraftServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftWaitsForAudit_AndAuditFailureDoesNotDiscardDraft(bool failAudit)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new DelayedAuditContext(options, failAudit);
        var notice = new Notice { OrganizationId = Guid.NewGuid(), NoticeType = "DRC-01" };
        db.Notices.Add(notice);
        await db.SaveChangesAsync();

        var ai = new Mock<IAiServiceClient>();
        ai.Setup(x => x.GenerateResponseDraftAsync(notice.Id, notice.OrganizationId,
                It.IsAny<GenerateResponseOptions>()))
            .ReturnsAsync(new GenerateResponseResult { Success = true, Draft = "Generated response" });
        var service = new NoticeResponseDraftService(db, ai.Object,
            Mock.Of<IDistributedCache>(), NullLogger<NoticeResponseDraftService>.Instance);

        db.DelayAudit = true;
        var generation = service.GenerateAutoDraftAsync(notice.Id, notice.OrganizationId, Guid.NewGuid());
        try
        {
            await db.AuditStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The old fire-and-forget path returned while the scoped context was
            // still saving. A real relational provider can reject its next query.
            Assert.False(generation.IsCompleted);
        }
        finally
        {
            db.ReleaseAudit.TrySetResult();
        }

        var result = await generation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Success);
        Assert.Equal("Generated response", result.DraftContent);
        Assert.Equal("DRC-01", result.Metadata?.NoticeType);
        Assert.Equal(failAudit ? 0 : 1, await db.AIAuditLogs.CountAsync());
    }

    private sealed class DelayedAuditContext(
        DbContextOptions<ApplicationDbContext> options, bool failAudit)
        : TestableApplicationDbContext(options)
    {
        public bool DelayAudit { get; set; }
        public TaskCompletionSource AuditStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseAudit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (DelayAudit)
            {
                AuditStarted.TrySetResult();
                await ReleaseAudit.Task.WaitAsync(cancellationToken);
                if (failAudit) throw new InvalidOperationException("Simulated audit storage failure");
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
