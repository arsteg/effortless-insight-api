using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the deadlines command to show upcoming due dates.
/// </summary>
public class DeadlinesCommandHandler : ICommandHandler
{
    private static readonly string[] Triggers = ["deadlines", "due", "urgent", "pending", "upcoming", "view_deadlines"];
    private readonly ApplicationDbContext _db;

    public DeadlinesCommandHandler(ApplicationDbContext db)
    {
        _db = db;
    }

    public string CommandName => "deadlines";
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
            return CommandResult.Text("Please link your account first.");
        }

        var user = await _db.Users.FindAsync([session.UserId.Value], ct);
        if (user?.OrganizationId == null)
        {
            return CommandResult.Text("No organization found.");
        }

        var orgId = user.OrganizationId.Value;
        var today = DateTime.UtcNow.Date;

        var deadlines = await _db.NoticeDeadlines
            .Include(d => d.Notice)
            .Where(d => d.Notice.OrganizationId == orgId &&
                       d.Status != "completed" &&
                       d.EffectiveDeadline >= today.AddDays(-7)) // Include recently overdue
            .OrderBy(d => d.EffectiveDeadline)
            .Take(10)
            .ToListAsync(ct);

        if (!deadlines.Any())
        {
            return CommandResult.Text("No upcoming deadlines found. You're all caught up!");
        }

        var lines = new List<string> { "*Upcoming Deadlines*", "" };

        // Group by urgency
        var overdue = deadlines.Where(d => d.EffectiveDeadline.Date < today).ToList();
        var dueToday = deadlines.Where(d => d.EffectiveDeadline.Date == today).ToList();
        var dueThisWeek = deadlines.Where(d => d.EffectiveDeadline.Date > today && d.EffectiveDeadline.Date <= today.AddDays(7)).ToList();
        var dueLater = deadlines.Where(d => d.EffectiveDeadline.Date > today.AddDays(7)).ToList();

        if (overdue.Any())
        {
            lines.Add("🚨 *OVERDUE*");
            foreach (var d in overdue)
            {
                var daysOverdue = (today - d.EffectiveDeadline.Date).Days;
                lines.Add($"• {d.Notice.NoticeType} - {d.DeadlineType}");
                lines.Add($"  _{daysOverdue} day(s) overdue_");
            }
            lines.Add("");
        }

        if (dueToday.Any())
        {
            lines.Add("⚠️ *DUE TODAY*");
            foreach (var d in dueToday)
            {
                lines.Add($"• {d.Notice.NoticeType} - {d.DeadlineType}");
            }
            lines.Add("");
        }

        if (dueThisWeek.Any())
        {
            lines.Add("📅 *THIS WEEK*");
            foreach (var d in dueThisWeek)
            {
                var daysRemaining = (d.EffectiveDeadline.Date - today).Days;
                lines.Add($"• {d.Notice.NoticeType} - {d.DeadlineType}");
                lines.Add($"  Due: {d.EffectiveDeadline:ddd, MMM d} ({daysRemaining} days)");
            }
            lines.Add("");
        }

        if (dueLater.Any())
        {
            lines.Add("📆 *LATER*");
            foreach (var d in dueLater.Take(3))
            {
                lines.Add($"• {d.Notice.NoticeType} - {d.EffectiveDeadline:MMM d}");
            }
            if (dueLater.Count > 3)
            {
                lines.Add($"  _...and {dueLater.Count - 3} more_");
            }
            lines.Add("");
        }

        lines.Add("_View full details in the app_");

        return CommandResult.Text(string.Join("\n", lines));
    }
}
