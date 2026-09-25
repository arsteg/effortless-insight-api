using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Encryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class CaDistributorHandoverTests
{
    static CaDistributorHandoverTests()
    {
        if (!FieldEncryptionServiceAccessor.IsConfigured)
            FieldEncryptionServiceAccessor.SetInstance(new FieldEncryptionService(new ConfigurationBuilder().Build(), NullLogger<FieldEncryptionService>.Instance));
    }

    private const string Gstin = "27AABCU9603R1ZN";

    internal static (ApplicationDbContext Db, TenantContext Tenant, CaClientInvitation Invite,
        CaProspectClient Prospect, OrganizationGstin Destination) Fixture()
    {
        var tenant = new TenantContext();
        var db = BillingTestDbContextFactory.Create(tenant);
        var ca = new ApplicationUser { Name = "CA", IsCA = true };
        var bo = new ApplicationUser { Name = "BO" };
        db.Users.AddRange(ca, bo);
        var source = new Organization { Name = "CA firm", NameNormalized = "ca firm", State = "Maharashtra" };
        var target = new Organization { Name = "BO", NameNormalized = "bo", State = "Maharashtra" };
        db.Organizations.AddRange(source, target);
        db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = source.Id, UserId = ca.Id, Role = "owner", Status = "active" },
            new OrganizationMember { OrganizationId = target.Id, UserId = bo.Id, Role = "owner", Status = "active" });
        var destination = new OrganizationGstin { OrganizationId = target.Id, Gstin = Gstin,
            StateCode = "27", StateName = "Maharashtra", IsPrimary = false };
        db.OrganizationGstins.Add(destination);
        db.OrganizationGstins.Add(new OrganizationGstin { OrganizationId = target.Id, Gstin = "29AABCU9603R1ZJ",
            StateCode = "29", StateName = "Karnataka", IsPrimary = true });
        var invite = new CaClientInvitation { CaOrganizationId = source.Id, CaUserId = ca.Id,
            Gstin = Gstin, Email = "bo@example.com", EmailNormalized = "BO@EXAMPLE.COM", TokenHash = "token" };
        var prospect = new CaProspectClient { CaUserId = ca.Id, Gstin = Gstin,
            GstinHash = CaWorkspaceWrites.Hash(Gstin), CaClientInvitationId = invite.Id };
        db.CaClientInvitations.Add(invite);
        db.CaProspectClients.Add(prospect);
        db.SaveChanges();
        tenant.SetOrganizationId(target.Id); // exercise the BO tenant filter
        return (db, tenant, invite, prospect, destination);
    }

    [Fact]
    public async Task TransfersUntaggedImportedAndManualNoticesUnderBoTenant_PreservingIdsAndChildren()
    {
        var (db, _, invite, prospect, destination) = Fixture();
        var imported = Notice(invite, "portal-1");
        var manual = Notice(invite, null);
        var unrelated = Notice(invite, "unrelated");
        unrelated.Gstin = "29AABCU9603R1ZJ";
        unrelated.GstinHash = CaWorkspaceWrites.Hash(unrelated.Gstin);
        db.Notices.AddRange(imported, manual, unrelated);
        var client = new GstClient { OrganizationId = invite.CaOrganizationId, Gstin = Gstin,
            CreatedByUserId = invite.CaUserId, StateCode = "27" };
        db.GstClients.Add(client);
        var raw = new GstNoticeRaw { OrganizationId = invite.CaOrganizationId, GstClientId = client.Id,
            Gstin = Gstin, PortalNoticeId = "portal-1", NoticeType = "DRC-01", ImportedToNotices = true,
            ImportedNoticeId = imported.Id };
        db.GstNoticesRaw.Add(raw);
        db.Comments.Add(new Comment { NoticeId = imported.Id, UserId = invite.CaUserId, Content = "Existing work" });
        var file = new NoticeFile { OrganizationId = invite.CaOrganizationId, NoticeId = imported.Id,
            Filename = "response.pdf", OriginalFilename = "response.pdf", MimeType = "application/pdf", StoragePath = "original-key" };
        db.NoticeFiles.Add(file);
        db.SaveChanges(); // seed pre-fix records deliberately without prospect tags

        (await db.Notices.CountAsync()).Should().Be(0, "the request is scoped to the BO tenant");
        var result = await new CaDistributorHandover(db).TransferAsync(invite, prospect, destination);
        await db.SaveChangesAsync();
        result.Transferred.Should().Be(2);
        result.Created.Should().Be(0);
        var visible = await db.Notices.ToListAsync();
        visible.Select(n => n.Id).Should().BeEquivalentTo(new[] { imported.Id, manual.Id });
        visible.Should().OnlyContain(n => n.GstinId == destination.Id && n.CaProspectClientId == prospect.Id);
        (await db.Comments.SingleAsync()).Content.Should().Be("Existing work");
        file.OrganizationId.Should().Be(destination.OrganizationId);
        raw.ImportedNoticeId.Should().Be(imported.Id);
        raw.OrganizationId.Should().Be(destination.OrganizationId);
        unrelated.OrganizationId.Should().Be(invite.CaOrganizationId);
        prospect.Status.Should().Be("merged");
    }

    [Fact]
    public async Task ImportsRawWithoutQuotaAndPreservesOriginalCreator()
    {
        var (db, _, invite, prospect, destination) = Fixture();
        var client = new GstClient { OrganizationId = invite.CaOrganizationId, Gstin = Gstin,
            CreatedByUserId = invite.CaUserId, StateCode = "27" };
        db.GstClients.Add(client);
        db.GstNoticesRaw.Add(new GstNoticeRaw { OrganizationId = invite.CaOrganizationId, GstClientId = client.Id,
            Gstin = Gstin, PortalNoticeId = "raw-only", NoticeType = "DRC-01" });
        db.SaveChanges();
        var result = await new CaDistributorHandover(db).TransferAsync(invite, prospect, destination);
        await db.SaveChangesAsync();
        result.Created.Should().Be(1);
        var notice = await db.Notices.SingleAsync();
        notice.UploadedById.Should().Be(invite.CaUserId);
        notice.GstinId.Should().Be(destination.Id);
        (await db.GstNoticesRaw.SingleAsync()).ImportedNoticeId.Should().Be(notice.Id);
    }

    [Fact]
    public async Task ExistingWorkedDuplicateStopsHandoverBeforeOwnershipChanges()
    {
        var (db, _, invite, prospect, destination) = Fixture();
        var source = Notice(invite, "same");
        var target = Notice(invite, "same");
        target.OrganizationId = destination.OrganizationId;
        db.Notices.AddRange(source, target);
        db.SaveChanges();
        var act = () => new CaDistributorHandover(db).TransferAsync(invite, prospect, destination);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("HANDOVER_NOTICE_CONFLICT*");
        source.OrganizationId.Should().Be(invite.CaOrganizationId);
        prospect.Status.Should().Be("staging");
    }

    [Fact]
    public async Task PreInvitationWritesCreateProspect_AndUnverifiedMergedMappingIsRejected()
    {
        var (db, tenant, invite, prospect, destination) = Fixture();
        db.CaProspectClients.Remove(prospect);
        db.SaveChanges();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        var notice = Notice(invite, "before-invitation");
        db.Notices.Add(notice);
        await db.SaveChangesAsync();
        var created = await db.CaProspectClients.SingleAsync();
        created.CaClientInvitationId.Should().BeNull();
        notice.CaProspectClientId.Should().Be(created.Id);
        created.Status = "merged";
        created.MergedIntoOrganizationId = destination.OrganizationId;
        await db.SaveChangesAsync();
        db.Notices.Add(Notice(invite, "late-write"));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("CLIENT_ROUTING_CONFLICT*");
    }

    [Fact]
    public async Task BoOrganizationWritesDoNotBecomeDistributorProspects()
    {
        var (db, _, invite, prospect, destination) = Fixture();
        var notice = Notice(invite, "bo-notice");
        notice.OrganizationId = destination.OrganizationId;
        notice.GstinId = destination.Id;
        db.Notices.Add(notice);
        await db.SaveChangesAsync();
        notice.CaProspectClientId.Should().BeNull();
        (await db.CaProspectClients.CountAsync()).Should().Be(1);
        prospect.Status.Should().Be("staging");
    }

    [Fact]
    public async Task CustomWorkflowIsCopiedWithoutMovingOtherClientsConfiguration()
    {
        var (db, _, invite, prospect, destination) = Fixture();
        var notice = Notice(invite, "workflow");
        var template = new WorkflowTemplate { OrganizationId = invite.CaOrganizationId, Name = "Client workflow" };
        var stage = new WorkflowStage { WorkflowTemplateId = template.Id, StageKey = "review", Name = "Review" };
        var instance = new NoticeWorkflowInstance { NoticeId = notice.Id, WorkflowTemplateId = template.Id,
            CurrentStageId = stage.Id, CurrentStageKey = "review" };
        db.Notices.Add(notice);
        db.WorkflowTemplates.Add(template);
        db.WorkflowStages.Add(stage);
        db.NoticeWorkflowInstances.Add(instance);
        db.SaveChanges();
        await new CaDistributorHandover(db).TransferAsync(invite, prospect, destination);
        await db.SaveChangesAsync();
        template.OrganizationId.Should().Be(invite.CaOrganizationId);
        instance.WorkflowTemplateId.Should().NotBe(template.Id);
        var copied = await db.WorkflowTemplates.SingleAsync();
        copied.OrganizationId.Should().Be(destination.OrganizationId);
        instance.WorkflowTemplateId.Should().Be(copied.Id);
        (await db.WorkflowStages.SingleAsync(s => s.Id == instance.CurrentStageId)).WorkflowTemplateId.Should().Be(copied.Id);
        (await db.WorkflowTemplates.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task LateGstinExtractionRequiresVerifiedAcceptedRelationship()
    {
        var (db, tenant, invite, prospect, destination) = Fixture();
        var notice = Notice(invite, "legacy-unknown");
        notice.Gstin = null;
        notice.GstinHash = null;
        db.Notices.Add(notice);
        db.SaveChanges();
        prospect.Status = "merged";
        prospect.MergedIntoOrganizationId = destination.OrganizationId;
        db.SaveChanges();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        notice.Gstin = Gstin;
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("CLIENT_ROUTING_CONFLICT*");
    }

    [Fact]
    public async Task DuplicatePortalNoticesWithinOneBatchAreRejected()
    {
        var (db, tenant, invite, _, _) = Fixture();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        db.Notices.AddRange(Notice(invite, "same-portal-id"), Notice(invite, "same-portal-id"));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("NOTICE_ALREADY_EXISTS*");
    }

    internal static Notice Notice(CaClientInvitation invitation, string? portalId) => new()
    {
        OrganizationId = invitation.CaOrganizationId, UploadedById = invitation.CaUserId,
        Gstin = Gstin, GstinHash = CaWorkspaceWrites.Hash(Gstin), GstnNoticeId = portalId,
        FileName = "original.pdf", FileUrl = "original-key", Status = NoticeStatus.Analyzed,
        ProcessingStatus = NoticeProcessingStatus.Completed
    };
}
