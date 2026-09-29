using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Organizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.GstSync;
using EffortlessInsight.Api.Data.Entities.GstSync;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class CaNoticeRoutingTests
{
    [Fact]
    public async Task ExtensionSessionRoutesToAcceptedClient_AndRestoresRequestTenant()
    {
        var (db, tenant, invite, prospect, destination) = CaDistributorHandoverTests.Fixture();
        Accept(db, invite, prospect, destination);
        var client = new GstClient { OrganizationId = destination.OrganizationId, Gstin = Gstin,
            CreatedByUserId = invite.CaUserId, OrganizationGstinId = destination.Id };
        db.GstClients.Add(client);
        db.SaveChanges();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        var current = new Mock<ICurrentOrganizationService>();
        current.SetupGet(c => c.OrganizationId).Returns(invite.CaOrganizationId);
        current.SetupGet(c => c.UserId).Returns(invite.CaUserId);
        var http = new DefaultHttpContext();
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var execution = new ActionExecutingContext(action, filters,
            new Dictionary<string, object?> { ["request"] = new StartSyncSessionRequest { GstClientId = client.Id } }, new object());
        var called = false;
        await new CaGstSyncRoutingFilter(db, current.Object, tenant).OnActionExecutionAsync(execution, () => {
            called = true;
            tenant.OrganizationId.Should().Be(destination.OrganizationId);
            http.Items[CaGstSyncRoutingFilter.OrganizationKey].Should().Be(destination.OrganizationId);
            return Task.FromResult(new ActionExecutedContext(action, filters, execution.Controller));
        });
        called.Should().BeTrue();
        tenant.OrganizationId.Should().Be(invite.CaOrganizationId);
        http.Items.Should().NotContainKey(CaGstSyncRoutingFilter.OrganizationKey);
    }

    [Fact]
    public async Task UnrelatedOrganizationWithSameGstinIsNotAnAutomaticRoute()
    {
        var (db, _, invite, _, _) = CaDistributorHandoverTests.Fixture();
        (await CaNoticeRouting.ResolveAsync(db, invite.CaOrganizationId, invite.CaUserId, Gstin)).Should().BeNull();
    }

    private const string Gstin = "27AABCU9603R1ZN";
    private static void Accept(ApplicationDbContext db, CaClientInvitation invite, CaProspectClient prospect, OrganizationGstin destination)
    {
        invite.Status = "accepted";
        invite.ResultingOrganizationId = destination.OrganizationId;
        prospect.Status = "merged";
        prospect.MergedIntoOrganizationId = destination.OrganizationId;
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = destination.OrganizationId,
            UserId = invite.CaUserId, Role = "ca", Status = "active" });
        db.BillingSubscriptions.Add(new EffortlessInsight.Api.Data.Entities.Billing.BillingSubscription {
            OrganizationId = destination.OrganizationId, Status = "active", CurrentPeriodEnd = DateTime.UtcNow.AddDays(30) });
        db.SaveChanges();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetectedGstinRoutesToAcceptedBo_WithOriginalIdAndRelatedData(bool lateExtraction)
    {
        var (db, tenant, invite, prospect, destination) = CaDistributorHandoverTests.Fixture();
        var notice = CaDistributorHandoverTests.Notice(invite, "automatic-route");
        if (lateExtraction)
        {
            notice.Gstin = null; notice.GstinHash = null;
            db.Notices.Add(notice);
            db.SaveChanges();
            db.NoticeFiles.Add(new NoticeFile { NoticeId = notice.Id, OrganizationId = invite.CaOrganizationId });
        }
        Accept(db, invite, prospect, destination);
        if (!lateExtraction) db.Notices.Add(notice);
        else notice.Gstin = Gstin;
        tenant.SetOrganizationId(invite.CaOrganizationId);
        await db.SaveChangesAsync();
        notice.OrganizationId.Should().Be(destination.OrganizationId);
        notice.GstinId.Should().Be(destination.Id);
        notice.CaProspectClientId.Should().Be(prospect.Id);
        if (lateExtraction) (await db.NoticeFiles.IgnoreQueryFilters().SingleAsync()).OrganizationId.Should().Be(destination.OrganizationId);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("viewer")]
    public async Task AutomaticRoutingRequiresCurrentWriteMembership(string state)
    {
        var (db, tenant, invite, prospect, destination) = CaDistributorHandoverTests.Fixture();
        Accept(db, invite, prospect, destination);
        var member = await db.OrganizationMembers.SingleAsync(m => m.UserId == invite.CaUserId && m.OrganizationId == destination.OrganizationId);
        if (state == "revoked") member.Status = "removed";
        if (state == "expired") member.AccessExpiresAt = DateTime.UtcNow.AddDays(-1);
        if (state == "viewer") member.Role = "viewer";
        db.SaveChanges();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        db.Notices.Add(CaDistributorHandoverTests.Notice(invite, "blocked"));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task AutomaticRoutingRequiresValidBoSubscription()
    {
        var (db, tenant, invite, prospect, destination) = CaDistributorHandoverTests.Fixture();
        Accept(db, invite, prospect, destination);
        (await db.BillingSubscriptions.SingleAsync()).CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1);
        db.SaveChanges();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        db.Notices.Add(CaDistributorHandoverTests.Notice(invite, "unpaid"));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("SUBSCRIPTION_REQUIRED*");
    }

    [Fact]
    public async Task UnknownGstinUploadWaitsForDetection_ThenCreatesProspectWithoutInvitation()
    {
        var (db, tenant, invite, prospect, _) = CaDistributorHandoverTests.Fixture();
        db.CaProspectClients.Remove(prospect);
        db.SaveChanges();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        var notice = CaDistributorHandoverTests.Notice(invite, null);
        notice.Gstin = null; notice.GstinHash = null;
        db.Notices.Add(notice);
        await db.SaveChangesAsync();
        notice.CaProspectClientId.Should().BeNull();
        notice.Gstin = Gstin;
        await db.SaveChangesAsync();
        notice.OrganizationId.Should().Be(invite.CaOrganizationId);
        notice.CaProspectClientId.Should().NotBeNull();
        notice.GstinId.Should().NotBeNull();
        (await db.CaProspectClients.SingleAsync()).CaClientInvitationId.Should().BeNull();
    }

    [Fact]
    public async Task ExistingBoDuplicateIsNotCopied()
    {
        var (db, tenant, invite, prospect, destination) = CaDistributorHandoverTests.Fixture();
        Accept(db, invite, prospect, destination);
        var existing = CaDistributorHandoverTests.Notice(invite, "same-id");
        existing.OrganizationId = destination.OrganizationId;
        db.Notices.Add(existing);
        db.SaveChanges();
        tenant.SetOrganizationId(invite.CaOrganizationId);
        db.Notices.Add(CaDistributorHandoverTests.Notice(invite, "same-id"));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("NOTICE_ALREADY_EXISTS*");
    }
}
