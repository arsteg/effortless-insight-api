using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>Extension requests retain their login token while each captured GSTIN resolves its authorized tenant.</summary>
public sealed class CaGstSyncRoutingFilter(ApplicationDbContext db, ICurrentOrganizationService current,
    ITenantContext tenant) : IAsyncActionFilter
{
    public const string OrganizationKey = "CaGstSyncOrganization";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (current.OrganizationId is not Guid source || current.UserId is not Guid actor) { await next(); return; }
        var ct = context.HttpContext.RequestAborted;
        var candidates = new List<(string Gstin, Guid? Organization)>();
        var clientIds = new List<Guid>();
        var sessionIds = new List<Guid>();
        var noticeIds = new List<Guid>();
        foreach (var (name, value) in context.ActionArguments)
        {
            switch (value)
            {
                case string gstin when name == "gstin" && !string.IsNullOrWhiteSpace(gstin): candidates.Add((gstin, null)); break;
                case CreateGstClientRequest request: candidates.Add((request.Gstin, null)); break;
                case StartSyncSessionRequest request: clientIds.Add(request.GstClientId); break;
                case SyncNoticesRequest request: sessionIds.Add(request.SessionId); break;
                case CompleteSyncSessionRequest request: sessionIds.Add(request.SessionId); break;
                case GetPdfUploadUrlRequest request: noticeIds.Add(request.NoticeId); break;
                case ConfirmPdfUploadRequest request: noticeIds.Add(request.NoticeId); break;
                case ImportNoticesRequest request: noticeIds.AddRange(request.NoticeIds); break;
                case Guid id when name == "clientId": clientIds.Add(id); break;
                case Guid id when name == "sessionId": sessionIds.Add(id); break;
                case Guid id when name == "noticeId": noticeIds.Add(id); break;
            }
        }
        candidates.AddRange((await db.GstClients.IgnoreQueryFilters().Where(c => clientIds.Contains(c.Id) && c.DeletedAt == null)
            .Select(c => new { c.Gstin, c.OrganizationId }).ToListAsync(ct)).Select(c => (c.Gstin, (Guid?)c.OrganizationId)));
        candidates.AddRange((await db.GstSyncSessions.IgnoreQueryFilters().Where(s => sessionIds.Contains(s.Id) && s.DeletedAt == null)
            .Select(s => new { s.Gstin, s.OrganizationId }).ToListAsync(ct)).Select(s => (s.Gstin, (Guid?)s.OrganizationId)));
        candidates.AddRange((await db.GstNoticesRaw.IgnoreQueryFilters().Where(n => noticeIds.Contains(n.Id) && n.DeletedAt == null)
            .Select(n => new { n.Gstin, n.OrganizationId }).ToListAsync(ct)).Select(n => (n.Gstin, (Guid?)n.OrganizationId)));
        try
        {
            var destinations = new HashSet<Guid>();
            foreach (var candidate in candidates)
            {
                var route = await CaNoticeRouting.ResolveAsync(db, source, actor, candidate.Gstin, ct);
                var destination = route?.OrganizationId ?? source;
                if (candidate.Organization.HasValue && candidate.Organization != destination)
                    throw new UnauthorizedAccessException("Client resource does not belong to the authorized organization.");
                destinations.Add(destination);
            }
            if (destinations.Count > 1)
                throw new InvalidOperationException("MIXED_CLIENT_BATCH: Sync one client organization at a time.");
            if (destinations.SingleOrDefault() is var target && target != Guid.Empty && target != source)
            {
                tenant.SetOrganizationId(target);
                context.HttpContext.Items[OrganizationKey] = target;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            context.Result = new ObjectResult(new ApiErrorResponse(false, "CLIENT_ACCESS_DENIED", ex.Message)) { StatusCode = 403 };
            return;
        }
        catch (InvalidOperationException ex)
        {
            context.Result = new ConflictObjectResult(new ApiErrorResponse(false, ex.Message.Split(':')[0], ex.Message));
            return;
        }
        try { await next(); }
        finally { tenant.SetOrganizationId(source); context.HttpContext.Items.Remove(OrganizationKey); }
    }
}
