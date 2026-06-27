using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Hangfire jobs for notification processing
/// </summary>
public class NotificationJobs
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationJobs> _logger;

    public NotificationJobs(
        IServiceProvider serviceProvider,
        ILogger<NotificationJobs> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Process scheduled notifications that are due
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    [Queue("default")]
    public async Task ProcessScheduledNotificationsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<INotificationEngineService>();

        _logger.LogInformation("Processing scheduled notifications...");
        await engine.ProcessScheduledNotificationsAsync();
        _logger.LogInformation("Scheduled notifications processed");
    }

    /// <summary>
    /// Retry failed notification deliveries
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [Queue("default")]
    public async Task ProcessFailedDeliveriesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<INotificationEngineService>();

        _logger.LogInformation("Processing failed deliveries...");
        await engine.ProcessFailedDeliveriesAsync();
        _logger.LogInformation("Failed deliveries processed");
    }

    /// <summary>
    /// Send deadline reminder notifications
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    [Queue("default")]
    public async Task SendDeadlineRemindersAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<INotificationEngineService>();

        _logger.LogInformation("Sending deadline reminders...");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var in7Days = today.AddDays(7);
        var in3Days = today.AddDays(3);
        var in1Day = today.AddDays(1);

        // Get notices with upcoming deadlines
        var notices = await dbContext.Notices
            .AsNoTracking()
            .Include(n => n.AssignedTo)
            .Where(n => n.DeletedAt == null)
            .Where(n => n.Status != "closed" && n.Status != "responded")
            .Where(n => n.ResponseDeadline != null)
            .Where(n => n.ResponseDeadline >= today && n.ResponseDeadline <= in7Days)
            .ToListAsync();

        var sentCount = 0;

        foreach (var notice in notices)
        {
            if (notice.ResponseDeadline == null || notice.AssignedToId == null)
                continue;

            var deadline = notice.ResponseDeadline.Value;
            var daysRemaining = deadline.DayNumber - today.DayNumber;

            string? notificationType = daysRemaining switch
            {
                0 => NotificationType.DeadlineToday,
                1 => NotificationType.Deadline1Day,
                <= 3 => NotificationType.Deadline3Day,
                <= 7 => NotificationType.Deadline7Day,
                _ => null
            };

            if (notificationType == null)
                continue;

            // Check if we already sent this reminder today
            var todayStart = today.ToDateTime(TimeOnly.MinValue);
            var todayEnd = today.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var alreadySent = await dbContext.Notifications
                .AnyAsync(n => n.UserId == notice.AssignedToId.Value &&
                              n.Type == notificationType &&
                              n.ReferenceId == notice.Id &&
                              n.CreatedAt >= todayStart && n.CreatedAt < todayEnd);

            if (alreadySent)
                continue;

            try
            {
                var request = new SendNotificationRequest(
                    notice.AssignedToId.Value,
                    notificationType,
                    new Dictionary<string, object>
                    {
                        ["noticeId"] = notice.Id.ToString(),
                        ["noticeNumber"] = notice.NoticeNumber ?? "",
                        ["noticeType"] = notice.NoticeType ?? "",
                        ["deadline"] = notice.ResponseDeadline.Value.ToString("dd MMM yyyy"),
                        ["daysRemaining"] = daysRemaining,
                        ["demandAmount"] = notice.TotalDemand ?? 0
                    });

                await engine.SendAsync(request);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send deadline reminder for notice {NoticeId}", notice.Id);
            }
        }

        // Check for missed deadlines
        var missedNotices = await dbContext.Notices
            .AsNoTracking()
            .Where(n => n.DeletedAt == null)
            .Where(n => n.Status != "closed" && n.Status != "responded")
            .Where(n => n.ResponseDeadline != null && n.ResponseDeadline < today)
            .Where(n => n.AssignedToId != null)
            .ToListAsync();

        foreach (var notice in missedNotices)
        {
            // Only send missed deadline notification once
            var alreadySent = await dbContext.Notifications
                .AnyAsync(n => n.UserId == notice.AssignedToId!.Value &&
                              n.Type == NotificationType.DeadlineMissed &&
                              n.ReferenceId == notice.Id);

            if (alreadySent)
                continue;

            try
            {
                var request = new SendNotificationRequest(
                    notice.AssignedToId!.Value,
                    NotificationType.DeadlineMissed,
                    new Dictionary<string, object>
                    {
                        ["noticeId"] = notice.Id.ToString(),
                        ["noticeNumber"] = notice.NoticeNumber ?? "",
                        ["noticeType"] = notice.NoticeType ?? "",
                        ["deadline"] = notice.ResponseDeadline!.Value.ToString("dd MMM yyyy"),
                        ["daysOverdue"] = today.DayNumber - notice.ResponseDeadline.Value.DayNumber,
                        ["demandAmount"] = notice.TotalDemand ?? 0
                    });

                await engine.SendAsync(request);
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send missed deadline notification for notice {NoticeId}", notice.Id);
            }
        }

        _logger.LogInformation("Sent {Count} deadline reminders", sentCount);
    }

    /// <summary>
    /// Send daily digest emails
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [Queue("low")]
    public async Task SendDailyDigestAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailChannelService>();

        _logger.LogInformation("Sending daily digest emails...");

        // Get users with daily digest enabled
        var users = await dbContext.UserNotificationPreferences
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.User.DeletedAt == null)
            .Where(p => p.User.Email != null)
            .ToListAsync();

        var sentCount = 0;

        foreach (var prefs in users)
        {
            try
            {
                // Check if digest is enabled (from JSONB)
                var digestEnabled = prefs.DigestSettings.TryGetValue("daily", out var daily)
                    && daily is System.Text.Json.JsonElement je
                    && je.TryGetProperty("enabled", out var enabled)
                    && enabled.GetBoolean();

                if (!digestEnabled)
                    continue;

                // Get yesterday's activity
                var yesterday = DateTime.UtcNow.Date.AddDays(-1);
                var today = DateTime.UtcNow.Date;

                var notifications = await dbContext.Notifications
                    .AsNoTracking()
                    .Where(n => n.UserId == prefs.UserId)
                    .Where(n => n.CreatedAt >= yesterday && n.CreatedAt < today)
                    .OrderByDescending(n => n.Priority == "critical" ? 0 :
                                           n.Priority == "high" ? 1 :
                                           n.Priority == "medium" ? 2 : 3)
                    .ThenByDescending(n => n.CreatedAt)
                    .Take(20)
                    .ToListAsync();

                if (!notifications.Any())
                    continue;

                // Build digest email
                var digestHtml = BuildDailyDigestHtml(prefs.User, notifications);

                var message = new EmailNotificationMessage(
                    prefs.User.Email!,
                    prefs.User.Name,
                    $"Your Daily Summary - {yesterday:dd MMM yyyy}",
                    digestHtml);

                var result = await emailService.SendAsync(message);

                if (result.Success)
                    sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send daily digest to user {UserId}", prefs.UserId);
            }
        }

        _logger.LogInformation("Sent {Count} daily digest emails", sentCount);
    }

    /// <summary>
    /// Send weekly summary emails to users with weekly digest enabled.
    /// Queries notices from the past 7 days per organization, groups by status/category,
    /// calculates weekly stats, compares to previous week for trends, and sends formatted digest.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [Queue("low")]
    public async Task SendWeeklySummaryAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailChannelService>();
        var templateService = scope.ServiceProvider.GetRequiredService<ITemplateRenderService>();

        _logger.LogInformation("Sending weekly summary emails...");

        // Get users with weekly digest enabled
        var usersWithDigest = await dbContext.UserNotificationPreferences
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.User.DeletedAt == null)
            .Where(p => p.User.Email != null)
            .ToListAsync();

        var sentCount = 0;
        var thisWeekStart = DateTime.UtcNow.Date.AddDays(-7);
        var thisWeekEnd = DateTime.UtcNow.Date;
        var lastWeekStart = thisWeekStart.AddDays(-7);
        var lastWeekEnd = thisWeekStart;

        foreach (var prefs in usersWithDigest)
        {
            try
            {
                // Check if weekly digest is enabled (from JSONB)
                var weeklyEnabled = prefs.DigestSettings.TryGetValue("weekly", out var weekly)
                    && weekly is System.Text.Json.JsonElement je
                    && je.TryGetProperty("enabled", out var enabled)
                    && enabled.GetBoolean();

                if (!weeklyEnabled)
                    continue;

                var user = prefs.User;
                var orgId = user.OrganizationId;

                if (orgId == null)
                    continue;

                // Query notices from the past 7 days for this organization
                var thisWeekNotices = await dbContext.Notices
                    .AsNoTracking()
                    .Where(n => n.OrganizationId == orgId)
                    .Where(n => n.DeletedAt == null)
                    .Where(n => n.CreatedAt >= thisWeekStart && n.CreatedAt < thisWeekEnd)
                    .ToListAsync();

                // Query notices from previous week for comparison
                var lastWeekNotices = await dbContext.Notices
                    .AsNoTracking()
                    .Where(n => n.OrganizationId == orgId)
                    .Where(n => n.DeletedAt == null)
                    .Where(n => n.CreatedAt >= lastWeekStart && n.CreatedAt < lastWeekEnd)
                    .ToListAsync();

                // Get all active notices for status breakdown
                var allActiveNotices = await dbContext.Notices
                    .AsNoTracking()
                    .Where(n => n.OrganizationId == orgId)
                    .Where(n => n.DeletedAt == null)
                    .Where(n => n.Status != "closed" && n.Status != "archived")
                    .ToListAsync();

                // Calculate weekly stats
                var newNoticesThisWeek = thisWeekNotices.Count;
                var newNoticesLastWeek = lastWeekNotices.Count;

                var closedThisWeek = await dbContext.Notices
                    .AsNoTracking()
                    .Where(n => n.OrganizationId == orgId)
                    .Where(n => n.Status == "closed" || n.Status == "responded")
                    .Where(n => n.UpdatedAt >= thisWeekStart && n.UpdatedAt < thisWeekEnd)
                    .CountAsync();

                var closedLastWeek = await dbContext.Notices
                    .AsNoTracking()
                    .Where(n => n.OrganizationId == orgId)
                    .Where(n => n.Status == "closed" || n.Status == "responded")
                    .Where(n => n.UpdatedAt >= lastWeekStart && n.UpdatedAt < lastWeekEnd)
                    .CountAsync();

                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var overdueNotices = allActiveNotices
                    .Where(n => n.ResponseDeadline != null && n.ResponseDeadline < today)
                    .ToList();

                var upcomingDeadlines = allActiveNotices
                    .Where(n => n.ResponseDeadline != null && n.ResponseDeadline >= today && n.ResponseDeadline <= today.AddDays(7))
                    .OrderBy(n => n.ResponseDeadline)
                    .ToList();

                // Group by status
                var statusGroups = allActiveNotices
                    .GroupBy(n => n.Status)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Group by category
                var categoryGroups = allActiveNotices
                    .Where(n => n.NoticeCategory != null)
                    .GroupBy(n => n.NoticeCategory!)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Calculate total demand at risk
                var totalDemandAtRisk = overdueNotices.Sum(n => n.TotalDemand ?? 0);
                var totalUpcomingDemand = upcomingDeadlines.Sum(n => n.TotalDemand ?? 0);

                // Calculate trends
                var newNoticesTrend = CalculateTrend(newNoticesThisWeek, newNoticesLastWeek);
                var closedNoticesTrend = CalculateTrend(closedThisWeek, closedLastWeek);

                // Build digest email
                var digestHtml = BuildWeeklyDigestHtml(
                    user,
                    thisWeekStart,
                    thisWeekEnd,
                    newNoticesThisWeek,
                    newNoticesTrend,
                    closedThisWeek,
                    closedNoticesTrend,
                    overdueNotices.Count,
                    totalDemandAtRisk,
                    upcomingDeadlines,
                    totalUpcomingDemand,
                    statusGroups,
                    categoryGroups,
                    allActiveNotices.Count);

                var message = new EmailNotificationMessage(
                    user.Email!,
                    user.Name,
                    $"Weekly Summary - {thisWeekStart:dd MMM} to {thisWeekEnd:dd MMM yyyy}",
                    digestHtml);

                var result = await emailService.SendAsync(message);

                if (result.Success)
                {
                    sentCount++;
                    _logger.LogDebug("Sent weekly digest to user {UserId}", user.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send weekly digest to user {UserId}", prefs.UserId);
            }
        }

        _logger.LogInformation("Sent {Count} weekly summary emails", sentCount);
    }

    /// <summary>
    /// Calculate trend percentage between two values
    /// </summary>
    private static (int Percentage, string Direction) CalculateTrend(int current, int previous)
    {
        if (previous == 0)
        {
            return current > 0 ? (100, "up") : (0, "same");
        }

        var change = ((double)(current - previous) / previous) * 100;
        var direction = change > 0 ? "up" : change < 0 ? "down" : "same";
        return ((int)Math.Abs(change), direction);
    }

    /// <summary>
    /// Build weekly digest HTML email content
    /// </summary>
    private static string BuildWeeklyDigestHtml(
        ApplicationUser user,
        DateTime weekStart,
        DateTime weekEnd,
        int newNotices,
        (int Percentage, string Direction) newTrend,
        int closedNotices,
        (int Percentage, string Direction) closedTrend,
        int overdueCount,
        decimal totalDemandAtRisk,
        List<Notice> upcomingDeadlines,
        decimal totalUpcomingDemand,
        Dictionary<string, int> statusGroups,
        Dictionary<string, int> categoryGroups,
        int totalActiveNotices)
    {
        var trendIcon = (string direction) => direction switch
        {
            "up" => "\u2191",    // Arrow up
            "down" => "\u2193",  // Arrow down
            _ => "\u2194"        // Horizontal arrow
        };

        var trendColor = (string direction, bool isPositive) =>
        {
            if (direction == "same") return "#6B7280";
            // For "closed" going up is positive, for "new/overdue" going down is positive
            return (direction == "up" && isPositive) || (direction == "down" && !isPositive)
                ? "#10B981"  // Green
                : "#EF4444"; // Red
        };

        var formatCurrency = (decimal amount) =>
            "\u20B9" + amount.ToString("N0", new System.Globalization.CultureInfo("en-IN"));

        var statusColorMap = new Dictionary<string, string>
        {
            ["uploaded"] = "#6B7280",
            ["processing"] = "#F59E0B",
            ["analyzed"] = "#8B5CF6",
            ["in_progress"] = "#3B82F6",
            ["responded"] = "#10B981",
            ["closed"] = "#10B981"
        };

        var statusBars = string.Join("", statusGroups.Select(sg =>
        {
            var color = statusColorMap.GetValueOrDefault(sg.Key, "#6B7280");
            var percentage = totalActiveNotices > 0 ? (double)sg.Value / totalActiveNotices * 100 : 0;
            var statusLabel = sg.Key.Replace("_", " ");
            statusLabel = char.ToUpper(statusLabel[0]) + statusLabel[1..];
            return $@"
            <div style=""margin-bottom: 8px;"">
              <div style=""display: flex; justify-content: space-between; margin-bottom: 4px;"">
                <span style=""font-size: 13px; color: #374151;"">{statusLabel}</span>
                <span style=""font-size: 13px; color: #6B7280;"">{sg.Value}</span>
              </div>
              <div style=""background: #E5E7EB; border-radius: 4px; height: 8px; overflow: hidden;"">
                <div style=""background: {color}; height: 100%; width: {percentage:F0}%;""></div>
              </div>
            </div>";
        }));

        var upcomingRows = string.Join("", upcomingDeadlines.Take(5).Select(n =>
        {
            var daysRemaining = n.ResponseDeadline!.Value.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
            var urgencyColor = daysRemaining <= 1 ? "#EF4444" : daysRemaining <= 3 ? "#F59E0B" : "#3B82F6";
            var demandStr = n.TotalDemand.HasValue ? formatCurrency(n.TotalDemand.Value) : "-";
            return $@"
            <tr>
              <td style=""padding: 12px; border-bottom: 1px solid #E5E7EB;"">
                <div style=""font-weight: 500; color: #111827;"">{n.NoticeNumber ?? "Pending"}</div>
                <div style=""font-size: 12px; color: #6B7280;"">{n.NoticeType ?? "-"}</div>
              </td>
              <td style=""padding: 12px; border-bottom: 1px solid #E5E7EB; text-align: center;"">
                <span style=""background: {urgencyColor}22; color: {urgencyColor}; padding: 4px 8px; border-radius: 12px; font-size: 12px; font-weight: 500;"">
                  {daysRemaining} day{(daysRemaining != 1 ? "s" : "")}
                </span>
              </td>
              <td style=""padding: 12px; border-bottom: 1px solid #E5E7EB; text-align: right; color: #374151;"">{demandStr}</td>
            </tr>";
        }));

        var categoryItems = string.Join("", categoryGroups.Take(5).Select(cg =>
        {
            var label = cg.Key.Replace("_", " ");
            label = char.ToUpper(label[0]) + label[1..];
            return $@"<span style=""background: #F3F4F6; color: #374151; padding: 4px 12px; border-radius: 16px; font-size: 13px; margin: 4px;"">{label}: {cg.Value}</span>";
        }));

        return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Weekly Summary</title>
</head>
<body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; max-width: 640px; margin: 0 auto; padding: 20px; background-color: #f3f4f6;"">
  <div style=""background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.1);"">

    <!-- Header -->
    <div style=""background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%); padding: 32px; text-align: center;"">
      <h1 style=""color: white; margin: 0; font-size: 28px; font-weight: 600;"">EffortlessInsight</h1>
      <p style=""color: #93c5fd; margin: 12px 0 0 0; font-size: 16px;"">Weekly Summary</p>
      <p style=""color: #bfdbfe; margin: 4px 0 0 0; font-size: 14px;"">{weekStart:dd MMM} - {weekEnd:dd MMM yyyy}</p>
    </div>

    <!-- Greeting -->
    <div style=""padding: 24px 32px 0 32px;"">
      <p style=""color: #374151; font-size: 16px; margin: 0;"">Hi {user.Name},</p>
      <p style=""color: #6B7280; font-size: 14px; margin: 8px 0 0 0;"">Here's your weekly compliance summary:</p>
    </div>

    <!-- Key Metrics Grid -->
    <div style=""padding: 24px 32px; display: grid; grid-template-columns: repeat(2, 1fr); gap: 16px;"">

      <!-- New Notices -->
      <div style=""background: #F0FDF4; border-radius: 8px; padding: 16px;"">
        <div style=""font-size: 13px; color: #166534; font-weight: 500; text-transform: uppercase; letter-spacing: 0.5px;"">New Notices</div>
        <div style=""display: flex; align-items: baseline; margin-top: 8px;"">
          <span style=""font-size: 32px; font-weight: 700; color: #166534;"">{newNotices}</span>
          <span style=""font-size: 13px; color: {trendColor(newTrend.Direction, false)}; margin-left: 8px;"">
            {trendIcon(newTrend.Direction)} {newTrend.Percentage}%
          </span>
        </div>
        <div style=""font-size: 12px; color: #4B5563; margin-top: 4px;"">vs last week</div>
      </div>

      <!-- Closed Notices -->
      <div style=""background: #EFF6FF; border-radius: 8px; padding: 16px;"">
        <div style=""font-size: 13px; color: #1E40AF; font-weight: 500; text-transform: uppercase; letter-spacing: 0.5px;"">Closed</div>
        <div style=""display: flex; align-items: baseline; margin-top: 8px;"">
          <span style=""font-size: 32px; font-weight: 700; color: #1E40AF;"">{closedNotices}</span>
          <span style=""font-size: 13px; color: {trendColor(closedTrend.Direction, true)}; margin-left: 8px;"">
            {trendIcon(closedTrend.Direction)} {closedTrend.Percentage}%
          </span>
        </div>
        <div style=""font-size: 12px; color: #4B5563; margin-top: 4px;"">vs last week</div>
      </div>

      <!-- Overdue -->
      <div style=""background: {(overdueCount > 0 ? "#FEF2F2" : "#F9FAFB")}; border-radius: 8px; padding: 16px;"">
        <div style=""font-size: 13px; color: {(overdueCount > 0 ? "#B91C1C" : "#4B5563")}; font-weight: 500; text-transform: uppercase; letter-spacing: 0.5px;"">Overdue</div>
        <div style=""margin-top: 8px;"">
          <span style=""font-size: 32px; font-weight: 700; color: {(overdueCount > 0 ? "#B91C1C" : "#374151")};"">{overdueCount}</span>
        </div>
        <div style=""font-size: 12px; color: #6B7280; margin-top: 4px;"">demand at risk: {formatCurrency(totalDemandAtRisk)}</div>
      </div>

      <!-- Total Active -->
      <div style=""background: #F9FAFB; border-radius: 8px; padding: 16px;"">
        <div style=""font-size: 13px; color: #4B5563; font-weight: 500; text-transform: uppercase; letter-spacing: 0.5px;"">Total Active</div>
        <div style=""margin-top: 8px;"">
          <span style=""font-size: 32px; font-weight: 700; color: #374151;"">{totalActiveNotices}</span>
        </div>
        <div style=""font-size: 12px; color: #6B7280; margin-top: 4px;"">notices pending action</div>
      </div>
    </div>

    {(overdueCount > 0 ? $@"
    <!-- Overdue Alert -->
    <div style=""margin: 0 32px 24px 32px; background: #FEF2F2; border-left: 4px solid #EF4444; padding: 16px; border-radius: 4px;"">
      <div style=""display: flex; align-items: center;"">
        <span style=""font-size: 20px; margin-right: 12px;"">&#9888;</span>
        <div>
          <div style=""font-weight: 600; color: #B91C1C;"">Immediate Attention Required</div>
          <div style=""font-size: 14px; color: #7F1D1D; margin-top: 4px;"">
            You have {overdueCount} overdue notice{(overdueCount != 1 ? "s" : "")} with {formatCurrency(totalDemandAtRisk)} at risk.
          </div>
        </div>
      </div>
    </div>" : "")}

    {(upcomingDeadlines.Any() ? $@"
    <!-- Upcoming Deadlines -->
    <div style=""padding: 0 32px 24px 32px;"">
      <h3 style=""color: #111827; font-size: 16px; font-weight: 600; margin: 0 0 16px 0;"">Upcoming Deadlines</h3>
      <table style=""width: 100%; border-collapse: collapse;"">
        <thead>
          <tr style=""background: #F9FAFB;"">
            <th style=""padding: 10px 12px; text-align: left; font-size: 12px; color: #6B7280; font-weight: 500; text-transform: uppercase;"">Notice</th>
            <th style=""padding: 10px 12px; text-align: center; font-size: 12px; color: #6B7280; font-weight: 500; text-transform: uppercase;"">Due In</th>
            <th style=""padding: 10px 12px; text-align: right; font-size: 12px; color: #6B7280; font-weight: 500; text-transform: uppercase;"">Demand</th>
          </tr>
        </thead>
        <tbody>
          {upcomingRows}
        </tbody>
      </table>
      {(upcomingDeadlines.Count > 5 ? $@"<p style=""font-size: 13px; color: #6B7280; margin: 12px 0 0 0; text-align: center;"">+ {upcomingDeadlines.Count - 5} more upcoming deadline{(upcomingDeadlines.Count - 5 != 1 ? "s" : "")}</p>" : "")}
      <p style=""font-size: 13px; color: #374151; margin: 12px 0 0 0;"">Total upcoming demand: <strong>{formatCurrency(totalUpcomingDemand)}</strong></p>
    </div>" : "")}

    <!-- Status Breakdown -->
    <div style=""padding: 0 32px 24px 32px;"">
      <h3 style=""color: #111827; font-size: 16px; font-weight: 600; margin: 0 0 16px 0;"">Status Breakdown</h3>
      {statusBars}
    </div>

    {(categoryGroups.Any() ? $@"
    <!-- Category Distribution -->
    <div style=""padding: 0 32px 24px 32px;"">
      <h3 style=""color: #111827; font-size: 16px; font-weight: 600; margin: 0 0 12px 0;"">By Category</h3>
      <div style=""display: flex; flex-wrap: wrap; gap: 8px;"">
        {categoryItems}
      </div>
    </div>" : "")}

    <!-- CTA -->
    <div style=""padding: 24px 32px; text-align: center; border-top: 1px solid #E5E7EB;"">
      <a href=""https://app.effortlessinsight.com/dashboard"" style=""background: #1e40af; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; display: inline-block; font-weight: 600; font-size: 15px;"">
        View Dashboard
      </a>
    </div>

    <!-- Footer -->
    <div style=""padding: 24px 32px; text-align: center; color: #6B7280; font-size: 12px; background: #F9FAFB;"">
      <p style=""margin: 0;"">
        <a href=""https://app.effortlessinsight.com/settings/notifications"" style=""color: #1e40af; text-decoration: none;"">Manage Preferences</a>
        &nbsp;|&nbsp;
        <a href=""https://app.effortlessinsight.com/unsubscribe"" style=""color: #1e40af; text-decoration: none;"">Unsubscribe</a>
      </p>
      <p style=""margin: 12px 0 0 0;"">&copy; {DateTime.UtcNow.Year} EffortlessInsight. All rights reserved.</p>
    </div>
  </div>
</body>
</html>";
    }

    /// <summary>
    /// Cleanup old notifications (90 days retention)
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    [Queue("low")]
    public async Task CleanupOldNotificationsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _logger.LogInformation("Cleaning up old notifications...");

        var cutoff = DateTime.UtcNow.AddDays(-90);

        // Delete old read notifications
        var deletedCount = await dbContext.Notifications
            .Where(n => n.IsRead && n.CreatedAt < cutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation("Deleted {Count} old notifications", deletedCount);

        // Delete old delivery records
        var deliveriesDeleted = await dbContext.NotificationDeliveries
            .Where(d => d.CreatedAt < cutoff)
            .ExecuteDeleteAsync();

        _logger.LogInformation("Deleted {Count} old delivery records", deliveriesDeleted);
    }

    /// <summary>
    /// Cleanup inactive push tokens
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    [Queue("low")]
    public async Task CleanupPushTokensAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var pushTokenService = scope.ServiceProvider.GetRequiredService<IPushTokenService>();

        _logger.LogInformation("Cleaning up inactive push tokens...");
        await pushTokenService.CleanupInactiveTokensAsync(90);
        _logger.LogInformation("Push token cleanup complete");
    }

    /// <summary>
    /// Send task reminder notifications (GAP-TASK-002)
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    [Queue("default")]
    public async Task SendTaskRemindersAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<INotificationEngineService>();

        _logger.LogInformation("Processing task reminders...");

        var today = DateTime.UtcNow.Date;
        var sentCount = 0;

        // Get all unsent reminders for tasks with due dates
        var reminders = await dbContext.TaskReminders
            .Include(r => r.Task)
                .ThenInclude(t => t.Notice)
            .Include(r => r.Task)
                .ThenInclude(t => t.Assignees)
            .Where(r => !r.IsSent)
            .Where(r => r.Task.DueDate != null)
            .Where(r => r.Task.Status != "done" && r.Task.Status != "archived" && r.Task.Status != "cancelled")
            .ToListAsync();

        foreach (var reminder in reminders)
        {
            try
            {
                if (reminder.Task.DueDate == null)
                    continue;

                // Calculate when this reminder should fire
                var reminderDate = reminder.Task.DueDate.Value.Date.AddDays(-reminder.DaysBeforeDue);

                // Check if today is the reminder date (or past it)
                if (today < reminderDate)
                    continue;

                // Send reminder to all assignees
                var assigneeIds = reminder.Task.Assignees.Select(a => a.UserId).ToList();

                foreach (var assigneeId in assigneeIds)
                {
                    try
                    {
                        var request = new SendNotificationRequest(
                            assigneeId,
                            "task_reminder",
                            new Dictionary<string, object>
                            {
                                ["taskId"] = reminder.TaskId.ToString(),
                                ["taskTitle"] = reminder.Task.Title,
                                ["noticeId"] = reminder.Task.NoticeId.ToString(),
                                ["daysBeforeDue"] = reminder.DaysBeforeDue,
                                ["dueDate"] = reminder.Task.DueDate.Value.ToString("dd MMM yyyy")
                            });

                        await engine.SendAsync(request);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send task reminder to user {UserId} for task {TaskId}",
                            assigneeId, reminder.TaskId);
                    }
                }

                // Mark reminder as sent
                reminder.IsSent = true;
                reminder.SentAt = DateTime.UtcNow;
                sentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process task reminder {ReminderId}", reminder.Id);
            }
        }

        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Sent {Count} task reminders", sentCount);
    }

    private static string BuildDailyDigestHtml(ApplicationUser user, List<Notification> notifications)
    {
        var criticalItems = notifications.Where(n => n.Priority == "critical").ToList();
        var highItems = notifications.Where(n => n.Priority == "high").ToList();
        var otherItems = notifications.Where(n => n.Priority != "critical" && n.Priority != "high").ToList();

        var yesterday = DateTime.UtcNow.Date.AddDays(-1);

        return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Daily Summary</title>
</head>
<body style=""font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f9fafb;"">
  <div style=""background: white; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.1);"">
    <div style=""background: #1e40af; padding: 20px; text-align: center;"">
      <h1 style=""color: white; margin: 0; font-size: 24px;"">EffortlessInsight</h1>
      <p style=""color: #93c5fd; margin: 8px 0 0 0;"">Daily Summary - {yesterday:dd MMM yyyy}</p>
    </div>

    <div style=""padding: 24px;"">
      <p style=""color: #374151; font-size: 16px;"">Hi {user.Name},</p>
      <p style=""color: #4b5563; font-size: 14px;"">Here's your daily summary of notifications and updates:</p>

      {(criticalItems.Any() ? $@"
      <div style=""background: #fee2e2; border-left: 4px solid #ef4444; padding: 12px; margin: 16px 0; border-radius: 4px;"">
        <h3 style=""margin: 0 0 8px 0; color: #b91c1c;"">Critical Items ({criticalItems.Count})</h3>
        {string.Join("", criticalItems.Select(n => $@"
        <p style=""margin: 4px 0; color: #7f1d1d;"">• {n.Title}</p>
        "))}
      </div>" : "")}

      {(highItems.Any() ? $@"
      <div style=""background: #fef3c7; border-left: 4px solid #f59e0b; padding: 12px; margin: 16px 0; border-radius: 4px;"">
        <h3 style=""margin: 0 0 8px 0; color: #b45309;"">High Priority ({highItems.Count})</h3>
        {string.Join("", highItems.Select(n => $@"
        <p style=""margin: 4px 0; color: #92400e;"">• {n.Title}</p>
        "))}
      </div>" : "")}

      {(otherItems.Any() ? $@"
      <div style=""background: #f3f4f6; border-left: 4px solid #6b7280; padding: 12px; margin: 16px 0; border-radius: 4px;"">
        <h3 style=""margin: 0 0 8px 0; color: #374151;"">Other Updates ({otherItems.Count})</h3>
        {string.Join("", otherItems.Take(5).Select(n => $@"
        <p style=""margin: 4px 0; color: #4b5563;"">• {n.Title}</p>
        "))}
        {(otherItems.Count > 5 ? $"<p style=\"margin: 4px 0; color: #6b7280; font-style: italic;\">... and {otherItems.Count - 5} more</p>" : "")}
      </div>" : "")}

      <div style=""text-align: center; margin: 24px 0;"">
        <a href=""https://app.effortlessinsight.com/notifications"" style=""background: #1e40af; color: white; padding: 12px 32px; text-decoration: none; border-radius: 6px; display: inline-block; font-weight: bold;"">
          View All Notifications
        </a>
      </div>
    </div>

    <div style=""padding: 16px; text-align: center; color: #6b7280; font-size: 12px; border-top: 1px solid #e5e7eb;"">
      <p>
        <a href=""https://app.effortlessinsight.com/settings/notifications"" style=""color: #1e40af;"">Manage Digest Settings</a> |
        <a href=""https://app.effortlessinsight.com/unsubscribe"" style=""color: #1e40af;"">Unsubscribe</a>
      </p>
      <p>© {DateTime.UtcNow.Year} EffortlessInsight. All rights reserved.</p>
    </div>
  </div>
</body>
</html>";
    }
}

/// <summary>
/// Extension methods for registering notification jobs
/// </summary>
public static class NotificationJobsExtensions
{
    /// <summary>
    /// Configure recurring notification jobs
    /// </summary>
    public static void ConfigureNotificationJobs(IApplicationBuilder app)
    {
        // Process scheduled notifications every 5 minutes
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-process-scheduled",
            job => job.ProcessScheduledNotificationsAsync(),
            "*/5 * * * *");

        // Retry failed deliveries every 10 minutes
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-retry-failed",
            job => job.ProcessFailedDeliveriesAsync(),
            "*/10 * * * *");

        // Send deadline reminders at 9:00 AM IST (3:30 AM UTC)
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-deadline-reminders",
            job => job.SendDeadlineRemindersAsync(),
            "30 3 * * *");

        // Send daily digest at 9:00 AM IST (3:30 AM UTC)
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-daily-digest",
            job => job.SendDailyDigestAsync(),
            "30 3 * * *");

        // Send weekly summary on Monday at 9:00 AM IST
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-weekly-summary",
            job => job.SendWeeklySummaryAsync(),
            "30 3 * * 1");

        // Cleanup old notifications daily at 2:00 AM UTC
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-cleanup",
            job => job.CleanupOldNotificationsAsync(),
            "0 2 * * *");

        // Cleanup inactive push tokens weekly on Sunday
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-cleanup-tokens",
            job => job.CleanupPushTokensAsync(),
            "0 3 * * 0");

        // Send task reminders daily at 9:00 AM IST (3:30 AM UTC) (GAP-TASK-002)
        RecurringJob.AddOrUpdate<NotificationJobs>(
            "notifications-task-reminders",
            job => job.SendTaskRemindersAsync(),
            "30 3 * * *");
    }
}
