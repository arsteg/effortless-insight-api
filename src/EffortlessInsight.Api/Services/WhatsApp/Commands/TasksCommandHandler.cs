using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the tasks command to show assigned tasks.
/// </summary>
public class TasksCommandHandler : ICommandHandler
{
    private static readonly string[] Triggers = ["tasks", "mytasks", "assigned", "todo"];
    private readonly ApplicationDbContext _db;

    public TasksCommandHandler(ApplicationDbContext db)
    {
        _db = db;
    }

    public string CommandName => "tasks";
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

        var userId = session.UserId.Value;
        var today = DateTime.UtcNow.Date;

        // Get tasks assigned to this user
        var tasks = await _db.Tasks
            .Include(t => t.Notice)
            .Where(t => t.AssignedToId == userId &&
                       t.Status != "completed" &&
                       t.Status != "cancelled")
            .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
            .Take(10)
            .ToListAsync(ct);

        if (!tasks.Any())
        {
            return CommandResult.Text("No pending tasks assigned to you. Great work!");
        }

        var lines = new List<string> { "*Your Assigned Tasks*", "" };

        var overdue = tasks.Where(t => t.DueDate.HasValue && t.DueDate.Value.Date < today).ToList();
        var dueToday = tasks.Where(t => t.DueDate.HasValue && t.DueDate.Value.Date == today).ToList();
        var upcoming = tasks.Where(t => !t.DueDate.HasValue || t.DueDate.Value.Date > today).ToList();

        if (overdue.Any())
        {
            lines.Add("🚨 *OVERDUE*");
            foreach (var t in overdue)
            {
                var daysOverdue = (today - t.DueDate!.Value.Date).Days;
                lines.Add($"• {TruncateTitle(t.Title)}");
                lines.Add($"  Notice: {t.Notice?.NoticeType ?? "Unknown"} | _{daysOverdue}d overdue_");
            }
            lines.Add("");
        }

        if (dueToday.Any())
        {
            lines.Add("⚠️ *DUE TODAY*");
            foreach (var t in dueToday)
            {
                lines.Add($"• {TruncateTitle(t.Title)}");
                lines.Add($"  Notice: {t.Notice?.NoticeType ?? "Unknown"}");
            }
            lines.Add("");
        }

        if (upcoming.Any())
        {
            lines.Add("📋 *UPCOMING*");
            foreach (var t in upcoming.Take(5))
            {
                var dueText = t.DueDate.HasValue
                    ? $"Due: {t.DueDate.Value:MMM d}"
                    : "No due date";
                lines.Add($"• {TruncateTitle(t.Title)}");
                lines.Add($"  {dueText}");
            }
            if (upcoming.Count > 5)
            {
                lines.Add($"  _...and {upcoming.Count - 5} more_");
            }
        }

        lines.Add("");
        lines.Add("_Complete tasks in the app_");

        return CommandResult.Text(string.Join("\n", lines));
    }

    private static string TruncateTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
            return "Untitled Task";

        return title.Length > 40 ? title[..37] + "..." : title;
    }
}
