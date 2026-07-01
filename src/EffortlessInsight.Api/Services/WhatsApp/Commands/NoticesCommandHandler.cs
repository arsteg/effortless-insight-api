using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the notices command to list recent notices.
/// </summary>
public class NoticesCommandHandler : ICommandHandler
{
    private static readonly string[] Triggers = ["notices", "list", "gst", "notice", "view_notices"];
    private const int PageSize = 5;
    private readonly ApplicationDbContext _db;

    public NoticesCommandHandler(ApplicationDbContext db)
    {
        _db = db;
    }

    public string CommandName => "notices";
    public bool RequiresAuth => true;

    public bool CanHandle(string input, WhatsAppSession session)
    {
        if (session.CurrentState != WhatsAppSessionState.Linked)
            return false;

        var normalized = CommandRouter.NormalizeInput(input);

        // Handle command triggers
        if (Triggers.Contains(normalized))
            return true;

        // Handle "more" for pagination
        if (normalized == "more" && session.Context.ContainsKey("lastCommand") &&
            session.Context["lastCommand"]?.ToString() == "notices")
            return true;

        // Handle notice number selection (1-9)
        if (int.TryParse(normalized, out var num) && num >= 1 && num <= 9 &&
            session.Context.ContainsKey("noticeIds"))
            return true;

        return false;
    }

    public async Task<CommandResult> HandleAsync(
        WhatsAppIncomingMessage message,
        WhatsAppSession session,
        CancellationToken ct = default)
    {
        if (!session.UserId.HasValue)
        {
            return CommandResult.Text("Please link your account first.");
        }

        var user = await _db.Users.FindAsync([session.UserId.Value], ct);
        if (user?.OrganizationId == null)
        {
            return CommandResult.Text("No organization found.");
        }

        var input = message.Text?.Trim() ?? message.ButtonReplyId ?? "";
        var normalized = CommandRouter.NormalizeInput(input);
        var orgId = user.OrganizationId.Value;

        // Handle notice detail view
        if (int.TryParse(normalized, out var noticeNum) && noticeNum >= 1 && noticeNum <= 9 &&
            session.Context.TryGetValue("noticeIds", out var noticeIdsObj))
        {
            return await HandleNoticeDetailAsync(noticeNum, noticeIdsObj, orgId, ct);
        }

        // Handle pagination
        var page = normalized == "more" ? session.CurrentPage + 1 : 0;

        // Get notices first
        var noticeList = await _db.Notices
            .Where(n => n.OrganizationId == orgId && n.Status == "pending")
            .OrderByDescending(n => n.CreatedAt)
            .Skip(page * PageSize)
            .Take(PageSize + 1) // Take extra to check if there are more
            .Select(n => new
            {
                n.Id,
                n.NoticeType,
                n.Priority
            })
            .ToListAsync(ct);

        var noticeIds = noticeList.Select(n => n.Id).ToList();

        // Get next deadline for each notice
        var deadlinesByNotice = await _db.NoticeDeadlines
            .Where(d => noticeIds.Contains(d.NoticeId) && d.Status != "completed")
            .GroupBy(d => d.NoticeId)
            .Select(g => new
            {
                NoticeId = g.Key,
                NextDeadline = g.OrderBy(d => d.EffectiveDeadline).Select(d => d.EffectiveDeadline).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.NoticeId, x => x.NextDeadline, ct);

        var notices = noticeList.Select(n => new
        {
            n.Id,
            n.NoticeType,
            RiskLevel = n.Priority,
            DueDate = deadlinesByNotice.GetValueOrDefault(n.Id)
        }).ToList();

        var totalCount = await _db.Notices
            .Where(n => n.OrganizationId == orgId && n.Status == "pending")
            .CountAsync(ct);

        if (!notices.Any())
        {
            return CommandResult.Text("No pending notices found.");
        }

        var hasMore = notices.Count > PageSize;
        var displayNotices = notices.Take(PageSize).ToList();

        var today = DateTime.UtcNow.Date;
        var lines = new List<string> { $"*Recent Notices* (showing {page * PageSize + 1}-{page * PageSize + displayNotices.Count} of {totalCount})", "" };

        for (var i = 0; i < displayNotices.Count; i++)
        {
            var n = displayNotices[i];
            var riskEmoji = n.RiskLevel?.ToLower() switch
            {
                "high" => "🔴",
                "medium" => "🟡",
                "low" => "🟢",
                _ => "⚪"
            };

            var dueText = n.DueDate != default
                ? $"Due: {n.DueDate:MMM d}"
                : "No deadline";

            lines.Add($"{i + 1}️⃣ *{n.NoticeType ?? "Notice"}*");
            lines.Add($"   Risk: {riskEmoji} {n.RiskLevel ?? "Unknown"} | {dueText}");
            lines.Add("");
        }

        lines.Add("Reply with notice number (1-5) for details");
        if (hasMore)
        {
            lines.Add("or *more* to see next page.");
        }

        // Store notice IDs in context for detail view
        var displayNoticeIds = displayNotices.Select(n => n.Id.ToString()).ToList();

        return new CommandResult
        {
            TextResponse = string.Join("\n", lines),
            ContextUpdate = new Dictionary<string, object>
            {
                ["lastCommand"] = "notices",
                ["noticeIds"] = displayNoticeIds,
                ["currentPage"] = page
            }
        };
    }

    private async Task<CommandResult> HandleNoticeDetailAsync(
        int noticeNum,
        object noticeIdsObj,
        Guid orgId,
        CancellationToken ct)
    {
        // Handle various types that context might be deserialized to
        var noticeIdsList = ExtractNoticeIdsList(noticeIdsObj);

        if (noticeIdsList.Count == 0 || noticeNum > noticeIdsList.Count)
        {
            return CommandResult.Text($"Invalid selection. Please enter a number between 1 and {noticeIdsList.Count}.");
        }

        var noticeIdStr = noticeIdsList[noticeNum - 1];
        if (!Guid.TryParse(noticeIdStr, out var noticeId))
        {
            return CommandResult.Text("Invalid notice selection.");
        }

        var notice = await _db.Notices
            .Include(n => n.AiReport)
            .Include(n => n.AssignedTo)
            .FirstOrDefaultAsync(n => n.Id == noticeId && n.OrganizationId == orgId, ct);

        if (notice == null)
        {
            return CommandResult.Text("Notice not found.");
        }

        // Get next deadline separately
        var nextDeadline = await _db.NoticeDeadlines
            .Where(d => d.NoticeId == noticeId && d.Status != "completed")
            .OrderBy(d => d.EffectiveDeadline)
            .FirstOrDefaultAsync(ct);

        var riskEmoji = notice.Priority?.ToLower() switch
        {
            "high" => "🔴",
            "medium" => "🟡",
            "low" => "🟢",
            _ => "⚪"
        };

        var lines = new List<string>
        {
            $"*{notice.NoticeType ?? "Notice"} Details*",
            "",
            $"*Risk Level:* {riskEmoji} {notice.Priority ?? "Unknown"}",
            $"*Status:* {notice.Status}",
            $"*Date Received:* {notice.IssueDate?.ToString("MMM d, yyyy") ?? "N/A"}"
        };

        if (notice.TaxAmount.HasValue)
        {
            lines.Add($"*Tax Amount:* ₹{notice.TaxAmount:N0}");
        }

        if (nextDeadline != null)
        {
            var daysRemaining = (nextDeadline.EffectiveDeadline.Date - DateTime.UtcNow.Date).Days;
            var dueText = daysRemaining <= 0 ? "Overdue!" :
                         daysRemaining == 1 ? "Tomorrow" :
                         $"{daysRemaining} days remaining";
            lines.Add($"*Next Deadline:* {nextDeadline.EffectiveDeadline:MMM d} ({dueText})");
        }

        if (notice.AssignedTo != null)
        {
            lines.Add($"*Assigned To:* {notice.AssignedTo.Name}");
        }

        if (!string.IsNullOrEmpty(notice.AiReport?.SummaryEn))
        {
            var summary = notice.AiReport.SummaryEn.Length > 200
                ? notice.AiReport.SummaryEn[..200] + "..."
                : notice.AiReport.SummaryEn;
            lines.Add("");
            lines.Add($"*Summary:*\n{summary}");
        }

        lines.Add("");
        lines.Add("_View full details in the app_");

        return CommandResult.Text(string.Join("\n", lines));
    }

    /// <summary>
    /// Extract notice IDs list from context object, handling various deserialization types.
    /// </summary>
    private static List<string> ExtractNoticeIdsList(object? obj)
    {
        if (obj == null)
            return [];

        // Handle List<string> directly
        if (obj is List<string> stringList)
            return stringList;

        // Handle List<object> (common with JSON deserialization)
        if (obj is List<object> objectList)
            return objectList.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();

        // Handle IEnumerable (covers arrays and other collections)
        if (obj is System.Collections.IEnumerable enumerable)
        {
            var result = new List<string>();
            foreach (var item in enumerable)
            {
                var str = item?.ToString();
                if (!string.IsNullOrEmpty(str))
                    result.Add(str);
            }
            return result;
        }

        // Handle System.Text.Json.JsonElement (from JSONB deserialization)
        if (obj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var result = new List<string>();
            foreach (var element in jsonElement.EnumerateArray())
            {
                var str = element.GetString();
                if (!string.IsNullOrEmpty(str))
                    result.Add(str);
            }
            return result;
        }

        return [];
    }
}
