using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class EmailSyncStatusSchemaTests
{
    [Fact]
    public void EverySyncStatusFitsTheMappedColumn()
    {
        using var db = BillingTestDbContextFactory.Create();
        var limit = db.Model.FindEntityType(typeof(EmailSyncLog))!
            .FindProperty(nameof(EmailSyncLog.Status))!.GetMaxLength();
        Assert.NotNull(limit);
        Assert.All(EmailSyncStatus.All, status => Assert.True(status.Length <= limit.Value,
            $"Status '{status}' exceeds the mapped limit of {limit}."));
    }
}
