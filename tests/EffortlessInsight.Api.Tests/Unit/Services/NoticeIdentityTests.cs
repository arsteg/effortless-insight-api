using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class NoticeIdentityTests
{
    internal static Notice NewNotice(Guid org, string? number = "REF-123", string? gstin = "27AABCU9603R1ZN") => new()
    {
        OrganizationId = org, UploadedById = Guid.NewGuid(), Gstin = gstin, NoticeNumber = number,
        FileName = "notice.pdf", FileUrl = "test/notice.pdf", ProcessingStatus = NoticeProcessingStatus.Queued
    };

    [Theory]
    [InlineData("manual", "manual")]
    [InlineData("manual", "gstn_portal")]
    [InlineData("gstn_portal", "manual")]
    [InlineData("gstn_portal", "gstn_portal")]
    public async Task SameIdentityAcrossSourcesIsRejected(string firstSource, string secondSource)
    {
        using var db = BillingTestDbContextFactory.Create();
        var first = NewNotice(Guid.NewGuid()); first.Source = firstSource;
        db.Notices.Add(first); await db.SaveChangesAsync();
        var second = NewNotice(first.OrganizationId, " ref-123 ", "27aabcu9603r1zn");
        second.Source = secondSource; second.FileName = "renamed.pdf";
        db.Notices.Add(second);
        var error = await Assert.ThrowsAsync<DuplicateNoticeException>(() => db.SaveChangesAsync());
        Assert.Equal(first.Id, error.ExistingNoticeId);
        Assert.Equal(1, await db.Notices.CountAsync());
    }

    [Fact]
    public async Task SameReferenceForDifferentGstinOrOrganizationIsAllowed()
    {
        using var db = BillingTestDbContextFactory.Create();
        var org = Guid.NewGuid();
        db.Notices.AddRange(NewNotice(org), NewNotice(org, gstin: "29AABCU9603R1ZN"), NewNotice(Guid.NewGuid()));
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.Notices.CountAsync());
    }

    [Fact]
    public async Task SameFileBeforeExtractionIsRejectedEvenWhenRenamed()
    {
        using var db = BillingTestDbContextFactory.Create();
        var first = NewNotice(Guid.NewGuid(), null, null); first.FileHash = "sha256-test";
        db.Notices.Add(first); await db.SaveChangesAsync();
        var second = NewNotice(first.OrganizationId, null, null); second.FileHash = first.FileHash;
        second.FileName = "renamed.pdf"; db.Notices.Add(second);
        await Assert.ThrowsAsync<DuplicateNoticeException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task IdenticalFilenameWithoutIdentityDoesNotCauseFalsePositive()
    {
        using var db = BillingTestDbContextFactory.Create(); var org = Guid.NewGuid();
        db.Notices.AddRange(NewNotice(org, null, null), NewNotice(org, null, null));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Notices.CountAsync());
    }

    [Fact]
    public async Task DeletedNoticeCanBeReimported()
    {
        using var db = BillingTestDbContextFactory.Create(); var org = Guid.NewGuid();
        var deleted = NewNotice(org); deleted.DeletedAt = DateTime.UtcNow;
        db.Notices.AddRange(deleted, NewNotice(org)); await db.SaveChangesAsync();
        Assert.Equal(1, await db.Notices.CountAsync());
    }

    [Fact]
    public async Task ReferenceDiscoveredAfterUploadIsCheckedOnUpdate()
    {
        using var db = BillingTestDbContextFactory.Create(); var org = Guid.NewGuid();
        var pending = NewNotice(org, null);
        db.Notices.AddRange(NewNotice(org), pending); await db.SaveChangesAsync();
        pending.NoticeNumber = "REF-123";
        await Assert.ThrowsAsync<DuplicateNoticeException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateWithinSameBatchIsRejected()
    {
        using var db = BillingTestDbContextFactory.Create(); var org = Guid.NewGuid();
        db.Notices.AddRange(NewNotice(org), NewNotice(org));
        await Assert.ThrowsAsync<DuplicateNoticeException>(() => db.SaveChangesAsync());
        Assert.Empty(await db.Notices.ToListAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BusinessOwnerCannotPersistUnregisteredGstin(bool ownerAlsoCa)
    {
        using var db = BillingTestDbContextFactory.Create();
        var owner = new ApplicationUser { IsCA = ownerAlsoCa, Name = "Owner" };
        var org = Guid.NewGuid();
        db.Users.Add(owner);
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org, UserId = owner.Id,
            Role = "owner", Status = "active" });
        if (ownerAlsoCa)
            db.CaProspectClients.Add(new CaProspectClient { CaUserId = Guid.NewGuid(), Gstin = "27AABCU9603R1ZN",
                GstinHash = "registered-hash", Status = "merged", MergedIntoOrganizationId = org });
        await db.SaveChangesAsync();
        db.Notices.Add(NewNotice(org));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("INVALID_GSTIN", error.Message);
        Assert.Empty(await db.Notices.ToListAsync());
    }

    [Theory]
    [InlineData("UX_Notices_Identity", true)]
    [InlineData("UX_Notices_Content", true)]
    [InlineData("UX_Notices_Portal", true)]
    [InlineData("PK_Notices", true)]
    [InlineData("IX_Notices_OrganizationId_EmailImportKey", true)]
    [InlineData("Some_Unrelated_Constraint", false)]
    public void OnlyKnownDatabaseConflictsAreReportedAsDuplicates(string constraint, bool duplicate)
    {
        var postgres = new Npgsql.PostgresException("duplicate key", "ERROR", "ERROR", "23505", constraintName: constraint);
        Assert.Equal(duplicate, NoticeIdentity.IsDuplicate(new DbUpdateException("insert failed", postgres)));
    }

}
