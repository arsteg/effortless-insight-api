using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.GstSync;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

public record CaHandoverResult(int Transferred, int Created, int Reconciled, List<Guid> ProcessingIds);

/// <summary>
/// Called only after recipient/destination authorization, inside the acceptance
/// transaction. Query-filter bypasses are local and always constrain both tenant
/// and GSTIN. Never used by ordinary BO -> CA membership invitations.
/// </summary>
public sealed class CaDistributorHandover(ApplicationDbContext db)
{
    // Late extraction routes only this notice, without replaying acceptance or restoring membership.
    public async Task RouteIdentifiedNoticeAsync(Notice notice, OrganizationGstin destination, Guid prospectId, CancellationToken ct)
    {
        var sourceOrg = notice.OrganizationId;
        await TransferWorkflowConfigurationAsync([notice.Id], sourceOrg, destination.OrganizationId, notice.UploadedById, ct);
        await db.NoticeConversations.IgnoreQueryFilters().Where(c => c.OrganizationId == sourceOrg && c.NoticeId == notice.Id).LoadAsync(ct);
        await db.NoticeFiles.IgnoreQueryFilters().Where(f => f.OrganizationId == sourceOrg && f.NoticeId == notice.Id).LoadAsync(ct);
        await db.ActivityLogs.IgnoreQueryFilters().Where(a => a.OrganizationId == sourceOrg && a.NoticeId == notice.Id).LoadAsync(ct);
        foreach (var item in db.NoticeConversations.Local.Where(c => c.OrganizationId == sourceOrg && c.NoticeId == notice.Id)) item.OrganizationId = destination.OrganizationId;
        foreach (var item in db.NoticeFiles.Local.Where(f => f.OrganizationId == sourceOrg && f.NoticeId == notice.Id)) item.OrganizationId = destination.OrganizationId;
        foreach (var item in db.ActivityLogs.Local.Where(a => a.OrganizationId == sourceOrg && a.NoticeId == notice.Id)) item.OrganizationId = destination.OrganizationId;
        notice.OrganizationId = destination.OrganizationId;
        notice.GstinId = destination.Id;
        notice.CaProspectClientId = prospectId;
        db.AuditLogs.Add(new AuditLog { Action = "notice.client_routed", EntityType = "Notice", EntityId = notice.Id,
            UserId = notice.UploadedById, OrganizationId = destination.OrganizationId,
            NewValues = new Dictionary<string, object> { ["SourceOrganizationId"] = sourceOrg,
                ["DestinationOrganizationId"] = destination.OrganizationId, ["DestinationGstinId"] = destination.Id,
                ["ProspectClientId"] = prospectId } });
    }

    public async Task<CaHandoverResult> TransferAsync(CaClientInvitation invitation,
        CaProspectClient prospect, OrganizationGstin destination, CancellationToken ct = default)
    {
        var sourceOrg = invitation.CaOrganizationId;
        var targetOrg = destination.OrganizationId;
        if (sourceOrg == targetOrg) throw new InvalidOperationException("INVALID_HANDOVER_DESTINATION");
        var hash = CaWorkspaceWrites.Hash(invitation.Gstin);
        if (prospect.CaUserId != invitation.CaUserId || prospect.GstinHash != hash
            || CaWorkspaceWrites.Hash(destination.Gstin) != hash)
            throw new InvalidOperationException("GSTIN_MISMATCH");

        // Include deleted notices so restoring an old notice cannot resurrect CA ownership.
        // Legacy records without a prospect/hash are compared after materialization.
        var sourceGstins = await db.OrganizationGstins.Where(g => g.OrganizationId == sourceOrg).ToListAsync(ct);
        var sourceGstinIds = sourceGstins.Where(g => SameGstin(g.Gstin, invitation.Gstin)).Select(g => g.Id).ToList();
        var importedIds = await db.GstNoticesRaw.IgnoreQueryFilters()
            .Where(r => r.OrganizationId == sourceOrg && r.Gstin == invitation.Gstin && r.ImportedNoticeId != null)
            .Select(r => r.ImportedNoticeId!.Value).ToListAsync(ct);
        var normalizedGstin = invitation.Gstin.Trim().ToUpperInvariant();
        var source = await db.Notices.IgnoreQueryFilters()
            .Where(n => n.OrganizationId == sourceOrg && (n.CaProspectClientId == prospect.Id
                || n.GstinHash == hash || (n.Gstin != null && n.Gstin.Trim().ToUpper() == normalizedGstin)
                || (n.GstinId.HasValue && sourceGstinIds.Contains(n.GstinId.Value)) || importedIds.Contains(n.Id)))
            .ToListAsync(ct);
        if (source.Any(n => n.CaProspectClientId != null && n.CaProspectClientId != prospect.Id))
            throw new InvalidOperationException("HANDOVER_OWNERSHIP_CONFLICT");
        if (source.Any(n => !string.IsNullOrWhiteSpace(n.Gstin) && !SameGstin(n.Gstin, invitation.Gstin)))
            throw new InvalidOperationException("HANDOVER_GSTIN_CONFLICT");
        var canonical = await db.Notices.IgnoreQueryFilters()
            .Where(n => n.OrganizationId == targetOrg && (n.GstinHash == hash
                || (n.Gstin != null && n.Gstin.Trim().ToUpper() == normalizedGstin)
                || (n.GstinHash == null && n.Gstin == null && n.GstinId == destination.Id)))
            .ToListAsync(ct);
        foreach (var notice in canonical.Where(n => n.GstinHash == hash || SameGstin(n.Gstin, invitation.Gstin)))
            notice.GstinId = destination.Id; // repair old primary-GSTIN misassociation

        // Two independently worked notices cannot be silently collapsed: doing so
        // discards status, responses or conflicting documents. Abort atomically and
        // report the IDs for explicit reconciliation instead of copying duplicates.
        foreach (var notice in source.Where(n => n.DeletedAt == null))
        {
            var match = FindMatch(canonical.Where(n => n.DeletedAt == null), notice.GstnNoticeId, notice.FileHash);
            if (match != null)
                throw new InvalidOperationException($"HANDOVER_NOTICE_CONFLICT: {notice.Id} and {match.Id} require reconciliation.");
            canonical.Add(notice);
        }
        canonical.AddRange(source.Where(n => n.DeletedAt != null));

        foreach (var notice in source)
        {
            notice.OrganizationId = targetOrg;
            notice.GstinId = destination.Id;
            notice.Gstin = destination.Gstin;
            notice.GstinHash = hash;
            notice.CaProspectClientId = prospect.Id; // durable provenance
            if (notice.ProcessingStatus != NoticeProcessingStatus.Completed &&
                notice.ProcessingStatus != NoticeProcessingStatus.Failed)
                notice.ProcessingStatus = NoticeProcessingStatus.Queued;
        }
        var ids = source.Concat(canonical).Select(n => n.Id).Distinct().ToList();
        await TransferWorkflowConfigurationAsync(ids, sourceOrg, targetOrg, invitation.CaUserId, ct);
        foreach (var conversation in await db.NoticeConversations.IgnoreQueryFilters()
                     .Where(c => c.OrganizationId == sourceOrg && ids.Contains(c.NoticeId)).ToListAsync(ct))
            conversation.OrganizationId = targetOrg;
        var folderIds = await db.FileFolders.IgnoreQueryFilters().Where(f => ids.Contains(f.NoticeId))
            .Select(f => f.Id).ToListAsync(ct);
        foreach (var file in await db.NoticeFiles.IgnoreQueryFilters()
                     .Where(f => f.OrganizationId == sourceOrg && f.NoticeId != null && ids.Contains(f.NoticeId.Value)).ToListAsync(ct))
        {
            file.OrganizationId = targetOrg;
            // Notice-owned folders follow the unchanged notice ID. Never retain a
            // malformed link to another client's folder.
            if (file.FolderId.HasValue && !folderIds.Contains(file.FolderId.Value)) file.FolderId = null;
        }
        foreach (var activity in await db.ActivityLogs.IgnoreQueryFilters()
                     .Where(a => a.OrganizationId == sourceOrg && a.NoticeId != null && ids.Contains(a.NoticeId.Value)).ToListAsync(ct))
            activity.OrganizationId = targetOrg;

        var created = 0;
        var reconciled = 0;
        var legacy = await db.CaStagedNotices.Where(n => n.CaProspectClientId == prospect.Id
            && !n.MergedToNotices && n.DeletedAt == null).ToListAsync(ct);
        foreach (var staged in legacy)
        {
            var notice = FindMatch(canonical, null, staged.FileHash);
            if (notice == null)
            {
                notice = new Notice
                {
                    OrganizationId = targetOrg, GstinId = destination.Id, Gstin = destination.Gstin,
                    GstinHash = hash, CaProspectClientId = prospect.Id, UploadedById = staged.UploadedByUserId,
                    NoticeNumber = staged.NoticeNumber ?? staged.SourceReferenceNumber,
                    NoticeType = staged.NoticeType, NoticeCategory = staged.NoticeCategory, Summary = staged.Summary,
                    IssueDate = staged.IssueDate, ResponseDeadline = staged.ResponseDeadline,
                    TaxAmount = staged.TaxAmount, InterestAmount = staged.InterestAmount, PenaltyAmount = staged.PenaltyAmount,
                    PeriodFrom = staged.PeriodFrom, PeriodTo = staged.PeriodTo, FinancialYear = staged.FinancialYear,
                    FileHash = staged.FileHash, FileName = staged.FileName ?? "legacy-notice",
                    FileUrl = staged.FileUrl ?? $"legacy://{staged.Id}", FileSize = staged.FileSize ?? 0,
                    FileMimeType = staged.FileMimeType, Status = NoticeStatus.Uploaded,
                    ProcessingStatus = staged.FileUrl != null ? NoticeProcessingStatus.Queued : NoticeProcessingStatus.Completed,
                    Source = NoticeSource.Upload
                };
                db.Notices.Add(notice);
                canonical.Add(notice);
                created++;
            }
            else reconciled++;
            staged.MergedToNotices = true;
            staged.MergedNoticeId = notice.Id;
            staged.MergedAt = DateTime.UtcNow;
        }

        // Move the sync connection and its raw records, including already imported
        // records. The ImportedNoticeId continues to point at the same Notice.
        var clients = await db.GstClients.IgnoreQueryFilters().Where(c => c.OrganizationId == sourceOrg
            && c.Gstin == invitation.Gstin && c.DeletedAt == null).ToListAsync(ct);
        var targetClient = await db.GstClients.IgnoreQueryFilters().FirstOrDefaultAsync(c =>
            c.OrganizationId == targetOrg && c.Gstin == invitation.Gstin && c.DeletedAt == null, ct);
        foreach (var client in clients)
        {
            var originalId = client.Id;
            if (targetClient == null)
            {
                client.OrganizationId = targetOrg;
                client.OrganizationGstinId = destination.Id;
                targetClient = client;
            }
            else if (targetClient.Id != client.Id)
            {
                client.DeletedAt = DateTime.UtcNow;
                client.SyncEnabled = false;
            }
            var rawRecords = await db.GstNoticesRaw.IgnoreQueryFilters()
                .Where(r => r.OrganizationId == sourceOrg && r.GstClientId == originalId).ToListAsync(ct);
            var targetRaw = await db.GstNoticesRaw.IgnoreQueryFilters()
                .Where(r => r.OrganizationId == targetOrg && r.GstClientId == targetClient.Id && r.DeletedAt == null).ToListAsync(ct);
            foreach (var raw in rawRecords)
            {
                if (raw.ImportedNoticeId.HasValue && !canonical.Any(n => n.Id == raw.ImportedNoticeId))
                    throw new InvalidOperationException("HANDOVER_RAW_NOTICE_CONFLICT");
                var duplicateRaw = targetRaw.FirstOrDefault(r => r.PortalNoticeId == raw.PortalNoticeId && r.Id != raw.Id);
                raw.OrganizationId = targetOrg;
                raw.GstClientId = targetClient.Id;
                if (raw.DeletedAt != null) continue;
                var notice = raw.ImportedNoticeId.HasValue
                    ? canonical.First(n => n.Id == raw.ImportedNoticeId)
                    : FindMatch(canonical, raw.PortalNoticeId, null);
                if (notice == null)
                {
                    notice = FromRaw(raw, destination, prospect, client.CreatedByUserId);
                    db.Notices.Add(notice);
                    canonical.Add(notice);
                    created++;
                }
                raw.ImportedToNotices = true;
                raw.ImportedNoticeId = notice.Id;
                raw.ImportedAt ??= DateTime.UtcNow;
                if (duplicateRaw != null)
                {
                    raw.DeletedAt = DateTime.UtcNow; // preserve the original capture as history
                    duplicateRaw.ImportedToNotices = true;
                    duplicateRaw.ImportedNoticeId = notice.Id;
                    duplicateRaw.ImportedAt ??= DateTime.UtcNow;
                }
            }
            foreach (var session in await db.GstSyncSessions.IgnoreQueryFilters()
                         .Where(s => s.OrganizationId == sourceOrg && s.GstClientId == originalId).ToListAsync(ct))
            {
                session.OrganizationId = targetOrg;
                session.GstClientId = targetClient.Id;
            }
            foreach (var reminder in await db.GstSyncReminders.IgnoreQueryFilters()
                         .Where(r => r.OrganizationId == sourceOrg && r.GstClientId == originalId).ToListAsync(ct))
            {
                reminder.OrganizationId = targetOrg;
                reminder.GstClientId = targetClient.Id;
            }
        }

        // Cross-org sharing is not the ownership mechanism for distributor clients.
        foreach (var link in await db.CaBoGstinLinks.Where(l => l.CaOrganizationId == sourceOrg
                     && l.BoOrganizationId == targetOrg && l.GstinHash == hash).ToListAsync(ct))
        {
            link.IsActive = false;
            link.DeactivatedAt = DateTime.UtcNow;
            link.DeactivationReason = "distributor_handover";
        }
        prospect.Status = "merged";
        prospect.MergedAt ??= DateTime.UtcNow;
        prospect.MergedIntoOrganizationId = targetOrg;
        return new CaHandoverResult(source.Count(n => n.DeletedAt == null), created, reconciled,
            canonical.Where(n => n.DeletedAt == null && n.ProcessingStatus == NoticeProcessingStatus.Queued)
                .Select(n => n.Id).Distinct().ToList());
    }

    private static bool SameGstin(string? a, string b) => !string.IsNullOrWhiteSpace(a)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private T Copy<T>(T entity) where T : BaseEntity
    {
        var copy = (T)db.Entry(entity).CurrentValues.ToObject();
        copy.Id = Guid.NewGuid();
        var name = db.Entry(entity).Metadata.FindProperty("Name");
        if (name != null && db.Entry(entity).Property("Name").CurrentValue is string originalName)
        {
            var maximum = name.GetMaxLength() ?? 100;
            var suffix = $" (CA {copy.Id.ToString("N")[..8]})";
            typeof(T).GetProperty("Name")!.SetValue(copy,
                originalName[..Math.Min(originalName.Length, maximum - suffix.Length)] + suffix);
        }
        db.Set<T>().Add(copy);
        return copy;
    }

    private async Task TransferWorkflowConfigurationAsync(List<Guid> noticeIds, Guid sourceOrg, Guid targetOrg,
        Guid caUserId, CancellationToken ct)
    {
        var allowed = (await db.OrganizationMembers.Where(m => m.OrganizationId == targetOrg
                && m.Status == "active" && m.DeletedAt == null
                && (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow))
            .Select(m => m.UserId).ToListAsync(ct)).ToHashSet();
        foreach (var member in db.OrganizationMembers.Local.Where(m => m.OrganizationId == targetOrg
                     && m.Status == "active" && m.DeletedAt == null
                     && (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow))) allowed.Add(member.UserId);
        // Active assignments must not continue granting access to the CA firm's staff.
        foreach (var notice in db.Notices.Local.Where(n => noticeIds.Contains(n.Id)))
            if (notice.AssignedToId.HasValue && !allowed.Contains(notice.AssignedToId.Value))
                notice.AssignedToId = null;
        var tasks = await db.Tasks.IgnoreQueryFilters().Where(t => noticeIds.Contains(t.NoticeId)).ToListAsync(ct);
        foreach (var task in tasks)
        {
            if (task.AssignedToId.HasValue && !allowed.Contains(task.AssignedToId.Value)) task.AssignedToId = null;
            task.AssignedTeamId = null;
        }
        var taskIds = tasks.Select(t => t.Id).ToList();
        var assignees = await db.TaskAssignees.Where(a => taskIds.Contains(a.TaskId)).ToListAsync(ct);
        foreach (var assignee in assignees)
        {
            if (!allowed.Contains(assignee.UserId)) db.TaskAssignees.Remove(assignee);
            else assignee.TeamId = null;
        }

        var instances = await db.NoticeWorkflowInstances.IgnoreQueryFilters()
            .Where(i => noticeIds.Contains(i.NoticeId)).ToListAsync(ct);
        foreach (var instance in instances)
            if (instance.AssignedToId.HasValue && !allowed.Contains(instance.AssignedToId.Value)) instance.AssignedToId = null;
        foreach (var templateId in instances.Select(i => i.WorkflowTemplateId).Distinct().ToList())
        {
            var template = await db.WorkflowTemplates.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.OrganizationId == sourceOrg, ct);
            if (template == null) continue; // shared system template
            var copy = Copy(template);
            copy.OrganizationId = targetOrg;
            // Preserve the configuration without moving templates used by other clients.
            var stages = await db.WorkflowStages.IgnoreQueryFilters().Where(s => s.WorkflowTemplateId == templateId).ToListAsync(ct);
            var stageMap = new Dictionary<Guid, Guid>();
            foreach (var stage in stages)
            {
                var stageCopy = Copy(stage);
                stageCopy.WorkflowTemplateId = copy.Id;
                if (stage.TaskTemplateId.HasValue)
                {
                    var taskTemplate = await db.TaskTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(t =>
                        t.Id == stage.TaskTemplateId && t.OrganizationId == sourceOrg, ct);
                    if (taskTemplate != null)
                    {
                        var taskCopy = Copy(taskTemplate);
                        taskCopy.OrganizationId = targetOrg;
                        stageCopy.TaskTemplateId = taskCopy.Id;
                    }
                }
                stageMap[stage.Id] = stageCopy.Id;
            }
            foreach (var rule in await db.WorkflowAssignmentRules.IgnoreQueryFilters()
                         .Where(r => r.WorkflowTemplateId == templateId).ToListAsync(ct))
            {
                var ruleCopy = Copy(rule);
                ruleCopy.WorkflowTemplateId = copy.Id;
                // Preserve for BO review; embedded user/team targets belong to the source firm.
                ruleCopy.IsEnabled = false;
            }
            foreach (var rule in await db.WorkflowEscalationRules.IgnoreQueryFilters()
                         .Where(r => r.WorkflowTemplateId == templateId).ToListAsync(ct))
            {
                var ruleCopy = Copy(rule);
                ruleCopy.WorkflowTemplateId = copy.Id;
            }
            var moved = instances.Where(i => i.WorkflowTemplateId == templateId).ToList();
            foreach (var instance in moved)
            {
                instance.WorkflowTemplateId = copy.Id;
                if (instance.CurrentStageId.HasValue && stageMap.TryGetValue(instance.CurrentStageId.Value, out var stageId))
                    instance.CurrentStageId = stageId;
            }
            var instanceIds = moved.Select(i => i.Id).ToList();
            foreach (var stage in await db.WorkflowStageInstances.IgnoreQueryFilters()
                         .Where(s => instanceIds.Contains(s.WorkflowInstanceId)).ToListAsync(ct))
                if (stageMap.TryGetValue(stage.StageId, out var stageId)) stage.StageId = stageId;
        }

        var requests = await db.ApprovalRequests.IgnoreQueryFilters().Where(r => noticeIds.Contains(r.NoticeId)).ToListAsync(ct);
        foreach (var chainId in requests.Select(r => r.ApprovalChainId).Distinct().ToList())
        {
            var chain = await db.ApprovalChains.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == chainId && c.OrganizationId == sourceOrg, ct);
            if (chain == null) continue;
            var copy = Copy(chain);
            copy.OrganizationId = targetOrg;
            var stepMap = new Dictionary<Guid, Guid>();
            foreach (var step in await db.ApprovalSteps.IgnoreQueryFilters().Where(s => s.ApprovalChainId == chainId).ToListAsync(ct))
            {
                var stepCopy = Copy(step);
                stepMap[step.Id] = stepCopy.Id;
                stepCopy.ApprovalChainId = copy.Id;
                if (stepCopy.ApproverId.HasValue && !allowed.Contains(stepCopy.ApproverId.Value))
                {
                    stepCopy.ApproverId = null;
                    stepCopy.ApproverType = "role";
                    stepCopy.ApproverRole = "owner";
                }
                if (stepCopy.EscalationUserId.HasValue && !allowed.Contains(stepCopy.EscalationUserId.Value))
                    stepCopy.EscalationUserId = null;
            }
            var movedRequests = requests.Where(r => r.ApprovalChainId == chainId).ToList();
            foreach (var request in movedRequests) request.ApprovalChainId = copy.Id;
            var requestIds = movedRequests.Select(r => r.Id).ToList();
            foreach (var action in await db.ApprovalActions.IgnoreQueryFilters()
                         .Where(a => requestIds.Contains(a.ApprovalRequestId)).ToListAsync(ct))
                if (stepMap.TryGetValue(action.ApprovalStepId, out var stepId)) action.ApprovalStepId = stepId;
        }
    }

    private static Notice? FindMatch(IEnumerable<Notice> notices, string? portalId, string? fileHash) =>
        notices.FirstOrDefault(n => (!string.IsNullOrWhiteSpace(portalId) && n.GstnNoticeId == portalId)
            || (!string.IsNullOrWhiteSpace(fileHash) && n.FileHash == fileHash));

    private static Notice FromRaw(GstNoticeRaw raw, OrganizationGstin gstin, CaProspectClient prospect, Guid creator) => new()
    {
        OrganizationId = gstin.OrganizationId, GstinId = gstin.Id, Gstin = gstin.Gstin,
        GstinHash = prospect.GstinHash, CaProspectClientId = prospect.Id, UploadedById = creator,
        GstnNoticeId = raw.PortalNoticeId, NoticeNumber = raw.ReferenceNumber ?? raw.PortalNoticeId,
        NoticeType = raw.NoticeType, NoticeCategory = raw.NoticeCategory,
        IssueDate = raw.IssueDate, ResponseDeadline = raw.DueDate, TaxAmount = raw.TaxAmount,
        InterestAmount = raw.InterestAmount, PenaltyAmount = raw.PenaltyAmount, FinancialYear = raw.FinancialYear,
        Section = raw.SectionRule, IssuingOfficer = raw.OfficerName, OfficerDesignation = raw.OfficerDesignation,
        Jurisdiction = raw.Jurisdiction, Source = NoticeSource.GstnPortal, Status = NoticeStatus.Uploaded,
        ProcessingStatus = raw.PdfS3Key != null ? NoticeProcessingStatus.Queued : NoticeProcessingStatus.Completed,
        FileUrl = raw.PdfS3Key ?? $"gst-sync-import/{raw.Id}", FileName = $"GST_Notice_{raw.PortalNoticeId}.pdf",
        FileMimeType = "application/pdf", FileSize = raw.PdfSizeBytes ?? 0
    };
}
