namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Defines all available permissions in the system.
/// Permissions are granular and can be assigned to custom roles.
/// </summary>
public static class Permissions
{
    // ============================================================================
    // Notice Permissions
    // ============================================================================
    public const string NoticesView = "notices.view";
    public const string NoticesViewAll = "notices.view_all";
    public const string NoticesCreate = "notices.create";
    public const string NoticesEdit = "notices.edit";
    public const string NoticesDelete = "notices.delete";
    public const string NoticesAssign = "notices.assign";
    public const string NoticesComment = "notices.comment";
    public const string NoticesDraftResponse = "notices.draft_response";
    public const string NoticesApproveResponse = "notices.approve_response";
    public const string NoticesExport = "notices.export";

    // ============================================================================
    // Task Permissions
    // ============================================================================
    public const string TasksView = "tasks.view";
    public const string TasksCreate = "tasks.create";
    public const string TasksEdit = "tasks.edit";
    public const string TasksDelete = "tasks.delete";
    public const string TasksAssign = "tasks.assign";
    public const string TasksComplete = "tasks.complete";

    // ============================================================================
    // Workflow Permissions
    // ============================================================================
    public const string WorkflowView = "workflow.view";
    public const string WorkflowTransition = "workflow.transition";
    public const string WorkflowAdmin = "workflow.admin";
    public const string WorkflowApprove = "workflow.approve";

    // ============================================================================
    // Organization Member Permissions
    // ============================================================================
    public const string OrgMembersView = "org.members.view";
    public const string OrgMembersInvite = "org.members.invite";
    public const string OrgMembersManage = "org.members.manage";
    public const string OrgMembersRemove = "org.members.remove";
    public const string OrgMembersChangeRole = "org.members.change_role";

    // ============================================================================
    // Organization Settings Permissions
    // ============================================================================
    public const string OrgSettingsView = "org.settings.view";
    public const string OrgSettingsEdit = "org.settings.edit";
    public const string OrgGstinsManage = "org.gstins.manage";
    public const string OrgDelete = "org.delete";
    public const string OrgTransferOwnership = "org.transfer_ownership";

    // ============================================================================
    // Billing Permissions
    // ============================================================================
    public const string BillingView = "billing.view";
    public const string BillingManage = "billing.manage";
    public const string BillingInvoices = "billing.invoices";

    // ============================================================================
    // Reports & Analytics Permissions
    // ============================================================================
    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";
    public const string AuditView = "audit.view";
    public const string AnalyticsView = "analytics.view";

    // ============================================================================
    // Team Permissions
    // ============================================================================
    public const string TeamsView = "teams.view";
    public const string TeamsCreate = "teams.create";
    public const string TeamsEdit = "teams.edit";
    public const string TeamsDelete = "teams.delete";
    public const string TeamsManageMembers = "teams.manage_members";

    // ============================================================================
    // Role Management Permissions
    // ============================================================================
    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";

    // ============================================================================
    // Document Request Permissions
    // ============================================================================
    public const string DocumentRequestsView = "document_requests.view";
    public const string DocumentRequestsCreate = "document_requests.create";
    public const string DocumentRequestsManage = "document_requests.manage";

    // ============================================================================
    // Helper Methods
    // ============================================================================

    /// <summary>
    /// Gets all available permissions in the system.
    /// </summary>
    public static readonly List<string> All =
    [
        // Notices
        NoticesView, NoticesViewAll, NoticesCreate, NoticesEdit, NoticesDelete,
        NoticesAssign, NoticesComment, NoticesDraftResponse, NoticesApproveResponse, NoticesExport,

        // Tasks
        TasksView, TasksCreate, TasksEdit, TasksDelete, TasksAssign, TasksComplete,

        // Workflow
        WorkflowView, WorkflowTransition, WorkflowAdmin, WorkflowApprove,

        // Organization Members
        OrgMembersView, OrgMembersInvite, OrgMembersManage, OrgMembersRemove, OrgMembersChangeRole,

        // Organization Settings
        OrgSettingsView, OrgSettingsEdit, OrgGstinsManage, OrgDelete, OrgTransferOwnership,

        // Billing
        BillingView, BillingManage, BillingInvoices,

        // Reports
        ReportsView, ReportsExport, AuditView, AnalyticsView,

        // Teams
        TeamsView, TeamsCreate, TeamsEdit, TeamsDelete, TeamsManageMembers,

        // Roles
        RolesView, RolesCreate, RolesEdit, RolesDelete,

        // Document Requests
        DocumentRequestsView, DocumentRequestsCreate, DocumentRequestsManage
    ];

    /// <summary>
    /// Gets the default permissions for built-in roles.
    /// </summary>
    public static List<string> GetDefaultPermissionsForRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "owner" => All,

            "admin" =>
            [
                // Notices - full access except delete
                NoticesView, NoticesViewAll, NoticesCreate, NoticesEdit,
                NoticesAssign, NoticesComment, NoticesDraftResponse, NoticesApproveResponse, NoticesExport,

                // Tasks - full access
                TasksView, TasksCreate, TasksEdit, TasksDelete, TasksAssign, TasksComplete,

                // Workflow - full access except admin
                WorkflowView, WorkflowTransition, WorkflowApprove,

                // Members - invite and manage
                OrgMembersView, OrgMembersInvite, OrgMembersManage, OrgMembersRemove, OrgMembersChangeRole,

                // Settings - view and edit
                OrgSettingsView, OrgSettingsEdit, OrgGstinsManage,

                // Billing - view only
                BillingView, BillingInvoices,

                // Reports - full access
                ReportsView, ReportsExport, AuditView, AnalyticsView,

                // Teams - full access
                TeamsView, TeamsCreate, TeamsEdit, TeamsDelete, TeamsManageMembers,

                // Roles - view and manage
                RolesView, RolesCreate, RolesEdit, RolesDelete,

                // Document Requests - full access
                DocumentRequestsView, DocumentRequestsCreate, DocumentRequestsManage
            ],

            "manager" =>
            [
                // Notices - view and manage
                NoticesView, NoticesViewAll, NoticesCreate, NoticesEdit,
                NoticesAssign, NoticesComment, NoticesDraftResponse, NoticesApproveResponse, NoticesExport,

                // Tasks - full access
                TasksView, TasksCreate, TasksEdit, TasksAssign, TasksComplete,

                // Workflow - transitions and approvals
                WorkflowView, WorkflowTransition, WorkflowApprove,

                // Members - view only
                OrgMembersView,

                // Settings - view only
                OrgSettingsView,

                // Reports - view and export
                ReportsView, ReportsExport, AnalyticsView,

                // Teams - view and manage team members
                TeamsView, TeamsManageMembers,

                // Document Requests - full access
                DocumentRequestsView, DocumentRequestsCreate, DocumentRequestsManage
            ],

            "member" =>
            [
                // Notices - basic access
                NoticesView, NoticesCreate, NoticesEdit,
                NoticesComment, NoticesDraftResponse,

                // Tasks - view and complete own
                TasksView, TasksCreate, TasksComplete,

                // Workflow - view and transition
                WorkflowView, WorkflowTransition,

                // Members - view only
                OrgMembersView,

                // Reports - view only
                ReportsView,

                // Teams - view only
                TeamsView,

                // Document Requests - view and create
                DocumentRequestsView, DocumentRequestsCreate
            ],

            "ca" =>
            [
                // Notices - view and comment only
                NoticesView, NoticesComment, NoticesDraftResponse,

                // Tasks - view and complete assigned
                TasksView, TasksComplete,

                // Workflow - view only
                WorkflowView,

                // Document Requests - view only
                DocumentRequestsView
            ],

            "viewer" =>
            [
                // Notices - view only
                NoticesView,

                // Workflow - view only
                WorkflowView,

                // Reports - view only
                ReportsView
            ],

            _ => []
        };
    }

    /// <summary>
    /// Permission categories for UI grouping.
    /// </summary>
    public static readonly Dictionary<string, List<string>> Categories = new()
    {
        ["Notices"] = [NoticesView, NoticesViewAll, NoticesCreate, NoticesEdit, NoticesDelete, NoticesAssign, NoticesComment, NoticesDraftResponse, NoticesApproveResponse, NoticesExport],
        ["Tasks"] = [TasksView, TasksCreate, TasksEdit, TasksDelete, TasksAssign, TasksComplete],
        ["Workflow"] = [WorkflowView, WorkflowTransition, WorkflowAdmin, WorkflowApprove],
        ["Members"] = [OrgMembersView, OrgMembersInvite, OrgMembersManage, OrgMembersRemove, OrgMembersChangeRole],
        ["Settings"] = [OrgSettingsView, OrgSettingsEdit, OrgGstinsManage, OrgDelete, OrgTransferOwnership],
        ["Billing"] = [BillingView, BillingManage, BillingInvoices],
        ["Reports"] = [ReportsView, ReportsExport, AuditView, AnalyticsView],
        ["Teams"] = [TeamsView, TeamsCreate, TeamsEdit, TeamsDelete, TeamsManageMembers],
        ["Roles"] = [RolesView, RolesCreate, RolesEdit, RolesDelete],
        ["Document Requests"] = [DocumentRequestsView, DocumentRequestsCreate, DocumentRequestsManage]
    };
}
