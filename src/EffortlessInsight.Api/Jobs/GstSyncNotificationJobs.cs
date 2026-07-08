using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.Services.GstSync;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background jobs for GST sync notifications
/// </summary>
public class GstSyncNotificationJobs
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GstSyncNotificationJobs> _logger;

    public GstSyncNotificationJobs(IServiceProvider serviceProvider, ILogger<GstSyncNotificationJobs> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Send daily digest emails for all organizations (run at 9 AM IST)
    /// </summary>
    public async Task SendDailyDigestsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting GST sync daily digest job");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<IGstSyncNotificationService>();

        // Get all organizations with active GST sync clients
        var organizationIds = await context.GstClients
            .Where(c => c.Status == GstClientStatus.Active)
            .Select(c => c.OrganizationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Sending daily digests to {Count} organizations", organizationIds.Count);

        foreach (var orgId in organizationIds)
        {
            try
            {
                await notificationService.SendDailyDigestAsync(orgId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send daily digest for organization {OrganizationId}", orgId);
            }
        }

        _logger.LogInformation("Completed GST sync daily digest job");
    }

    /// <summary>
    /// Process due date reminders (run daily at 8 AM IST)
    /// </summary>
    public async Task ProcessDueDateRemindersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting GST sync due date reminder job");

        using var scope = _serviceProvider.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<IGstSyncNotificationService>();

        try
        {
            await notificationService.ProcessDueDateRemindersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process due date reminders");
        }

        _logger.LogInformation("Completed GST sync due date reminder job");
    }

    /// <summary>
    /// Check for disconnected extensions (run hourly)
    /// </summary>
    public async Task CheckDisconnectedExtensionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting disconnected extension check job");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<IGstSyncNotificationService>();

        // Find users who haven't sent a heartbeat in the last 24 hours
        // but have active GST clients
        var threshold = DateTime.UtcNow.AddHours(-24);

        var disconnectedUsers = await context.GstExtensionEvents
            .Where(e => e.EventType == "heartbeat")
            .GroupBy(e => new { e.OrganizationId, e.UserId })
            .Where(g => g.Max(e => e.CreatedAt) < threshold)
            .Select(g => new { g.Key.OrganizationId, g.Key.UserId })
            .ToListAsync(cancellationToken);

        // Filter to only users with active GST clients
        foreach (var user in disconnectedUsers)
        {
            if (user.OrganizationId == null || user.UserId == null) continue;

            var hasActiveClients = await context.GstClients
                .AnyAsync(c => c.OrganizationId == user.OrganizationId.Value &&
                              c.Status == GstClientStatus.Active,
                         cancellationToken);

            if (hasActiveClients)
            {
                try
                {
                    await notificationService.NotifyExtensionDisconnectedAsync(
                        user.OrganizationId.Value,
                        user.UserId.Value,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send disconnection notification to user {UserId}", user.UserId);
                }
            }
        }

        _logger.LogInformation("Completed disconnected extension check job");
    }
}
