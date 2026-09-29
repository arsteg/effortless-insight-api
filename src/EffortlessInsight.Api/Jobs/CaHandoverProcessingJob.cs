using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Notices;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>Recovers the commit-to-Hangfire gap using the durable queued notice state.</summary>
public sealed class CaHandoverProcessingJob(ApplicationDbContext db, IBackgroundJobClient jobs)
{
    [DisableConcurrentExecution(300)]
    public async Task RecoverAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var ids = await db.Notices.IgnoreQueryFilters().Where(n => n.DeletedAt == null
                && n.CaProspectClientId != null && n.CaProspectClient!.Status == "merged"
                && n.OrganizationId == n.CaProspectClient.MergedIntoOrganizationId
                && n.ProcessingStatus == NoticeProcessingStatus.Queued && n.UpdatedAt < cutoff)
            .OrderBy(n => n.UpdatedAt).Take(100).Select(n => n.Id).ToListAsync();
        foreach (var id in ids)
            jobs.Enqueue<INoticeProcessingJob>(j => j.ProcessAsync(id, CancellationToken.None));
    }
}
