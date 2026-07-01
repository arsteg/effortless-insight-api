using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the status/dashboard command to show quick summary.
/// </summary>
public class StatusCommandHandler : ICommandHandler
{
    private static readonly string[] Triggers = ["status", "dashboard", "summary", "home"];
    private readonly ApplicationDbContext _db;

    public StatusCommandHandler(ApplicationDbContext db)
    {
        _db = db;
    }

    public string CommandName => "status";
    public bool RequiresAuth => true;

    public bool CanHandle(string input, WhatsAppSession session)
    {
        if (session.CurrentState != WhatsAppSessionState.Linked)
            return false;

        var normalized = CommandRouter.NormalizeInput(input);
        return Triggers.Contains(normalized);
    }

    public async Task<CommandResult> HandleAsync(
        WhatsAppIncomingMessage message,
        WhatsAppSession session,
        CancellationToken ct = default)
    {
        if (!session.UserId.HasValue)
        {
            return CommandResult.Text("Please link your account first. Reply with your email address.");
        }

        var user = await _db.Users
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == session.UserId.Value, ct);

        if (user?.OrganizationId == null)
        {
            return CommandResult.Text("No organization found. Please log in to the app to complete setup.");
        }

        var orgId = user.OrganizationId.Value;
        var today = DateTime.UtcNow.Date;
        var weekEnd = today.AddDays(7);

        // Get counts
        var pendingNotices = await _db.Notices
            .Where(n => n.OrganizationId == orgId && n.Status == "pending")
            .CountAsync(ct);

        var dueThisWeek = await _db.NoticeDeadlines
            .Where(d => d.Notice.OrganizationId == orgId &&
                       d.EffectiveDeadline >= today &&
                       d.EffectiveDeadline <= weekEnd &&
                       d.Status != "completed")
            .CountAsync(ct);

        var highRisk = await _db.Notices
            .Where(n => n.OrganizationId == orgId &&
                       n.Priority == "high" &&
                       n.Status == "pending")
            .CountAsync(ct);

        // Get next deadline
        var nextDeadline = await _db.NoticeDeadlines
            .Include(d => d.Notice)
            .Where(d => d.Notice.OrganizationId == orgId &&
                       d.EffectiveDeadline >= today &&
                       d.Status != "completed")
            .OrderBy(d => d.EffectiveDeadline)
            .FirstOrDefaultAsync(ct);

        var statusText = $"""
            *Dashboard Summary*

            *Pending Notices:* {pendingNotices}
            *Due This Week:* {dueThisWeek}
            *High Risk:* {highRisk}
            """;

        if (nextDeadline != null)
        {
            var daysRemaining = (nextDeadline.EffectiveDeadline.Date - today).Days;
            var dueText = daysRemaining == 0 ? "Today" :
                         daysRemaining == 1 ? "Tomorrow" :
                         $"{daysRemaining} days";

            statusText += $"""


                *Next Deadline:*
                {nextDeadline.Notice.NoticeType ?? "Notice"} - {nextDeadline.DeadlineType}
                Due: {dueText} ({nextDeadline.EffectiveDeadline:MMM d})
                """;
        }

        statusText += """


            Reply *notices* for details or *deadlines* for all due dates.
            """;

        var buttons = new List<WhatsAppButton>
        {
            new("view_notices", "View Notices"),
            new("view_deadlines", "View Deadlines")
        };

        return CommandResult.WithButtons(statusText, buttons);
    }
}
