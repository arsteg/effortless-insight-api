using EffortlessInsight.Api.Data.Entities;
using FluentAssertions;

namespace EffortlessInsight.Api.Tests.Unit.Services;

/// <summary>
/// Phase 5 hardening: Permissions.GetDefaultPermissionsForRole("ca") must match
/// "member"'s ceiling plus NoticesViewAll, and stay in sync with the
/// hand-maintained equivalent branches in CurrentOrganizationService.HasPermission.
/// </summary>
public class PermissionsCaTests
{
    [Fact]
    public void GetDefaultPermissionsForRole_Ca_MatchesMemberCeilingPlusViewAll()
    {
        var caPermissions = Permissions.GetDefaultPermissionsForRole("ca");
        var memberPermissions = Permissions.GetDefaultPermissionsForRole("member");

        // "ca" should be exactly "member" plus NoticesViewAll.
        var expected = new HashSet<string>(memberPermissions) { Permissions.NoticesViewAll };
        new HashSet<string>(caPermissions).Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(Permissions.NoticesDelete)]
    [InlineData(Permissions.NoticesApproveResponse)]
    [InlineData(Permissions.WorkflowApprove)]
    [InlineData(Permissions.OrgMembersInvite)]
    [InlineData(Permissions.OrgMembersManage)]
    [InlineData(Permissions.OrgMembersRemove)]
    [InlineData(Permissions.OrgMembersChangeRole)]
    [InlineData(Permissions.OrgSettingsEdit)]
    [InlineData(Permissions.OrgGstinsManage)]
    [InlineData(Permissions.BillingManage)]
    [InlineData(Permissions.OrgDelete)]
    [InlineData(Permissions.OrgTransferOwnership)]
    public void GetDefaultPermissionsForRole_Ca_ExcludesGovernancePermissions(string permission)
    {
        Permissions.GetDefaultPermissionsForRole("ca").Should().NotContain(permission);
    }

    [Theory]
    [InlineData(Permissions.NoticesView)]
    [InlineData(Permissions.NoticesViewAll)]
    [InlineData(Permissions.NoticesCreate)]
    [InlineData(Permissions.NoticesEdit)]
    [InlineData(Permissions.NoticesComment)]
    [InlineData(Permissions.NoticesDraftResponse)]
    [InlineData(Permissions.TasksView)]
    [InlineData(Permissions.WorkflowView)]
    [InlineData(Permissions.WorkflowTransition)]
    [InlineData(Permissions.OrgMembersView)]
    [InlineData(Permissions.ReportsView)]
    [InlineData(Permissions.TeamsView)]
    [InlineData(Permissions.DocumentRequestsView)]
    [InlineData(Permissions.DocumentRequestsCreate)]
    public void GetDefaultPermissionsForRole_Ca_IncludesMemberLevelAccess(string permission)
    {
        Permissions.GetDefaultPermissionsForRole("ca").Should().Contain(permission);
    }
}
