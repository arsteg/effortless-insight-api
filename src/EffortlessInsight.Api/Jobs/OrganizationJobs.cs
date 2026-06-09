using EffortlessInsight.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Jobs;

/// <summary>
/// Background jobs for organization management tasks.
/// </summary>
public class OrganizationJobs
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<OrganizationJobs> _logger;

    public OrganizationJobs(ApplicationDbContext dbContext, ILogger<OrganizationJobs> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Mark expired invitations as expired.
    /// Should be scheduled to run hourly.
    /// </summary>
    public async Task ExpireInvitationsAsync()
    {
        try
        {
            var now = DateTime.UtcNow;

            var expiredInvitations = await _dbContext.OrganizationInvitations
                .Where(i => i.Status == "pending" && i.ExpiresAt < now)
                .ToListAsync();

            if (expiredInvitations.Count == 0)
            {
                return;
            }

            foreach (var invitation in expiredInvitations)
            {
                invitation.Status = "expired";
                invitation.RespondedAt = now;
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Marked {Count} invitations as expired", expiredInvitations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire invitations");
            throw;
        }
    }

    /// <summary>
    /// Expire CA access that has passed its expiration date.
    /// Should be scheduled to run hourly.
    /// </summary>
    public async Task ExpireCaAccessAsync()
    {
        try
        {
            var now = DateTime.UtcNow;

            var expiredAccess = await _dbContext.OrganizationMembers
                .Where(m => m.IsExternal && m.Status == "active" && m.AccessExpiresAt != null && m.AccessExpiresAt < now)
                .ToListAsync();

            if (expiredAccess.Count == 0)
            {
                return;
            }

            foreach (var member in expiredAccess)
            {
                member.Status = "suspended";
                member.SuspendedAt = now;
                member.SuspensionReason = "Access expired";
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Suspended {Count} expired external access memberships", expiredAccess.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire CA access");
            throw;
        }
    }

    /// <summary>
    /// Clean up old cancelled/declined invitations (older than 30 days).
    /// Should be scheduled to run daily.
    /// </summary>
    public async Task CleanupOldInvitationsAsync()
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-30);

            var oldInvitations = await _dbContext.OrganizationInvitations
                .Where(i => (i.Status == "cancelled" || i.Status == "declined" || i.Status == "expired") &&
                            i.UpdatedAt < cutoffDate)
                .ToListAsync();

            if (oldInvitations.Count == 0)
            {
                return;
            }

            _dbContext.OrganizationInvitations.RemoveRange(oldInvitations);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} old invitations", oldInvitations.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old invitations");
            throw;
        }
    }

    /// <summary>
    /// Permanently delete soft-deleted organizations after 30 days.
    /// Should be scheduled to run daily.
    /// </summary>
    public async Task PermanentlyDeleteOrganizationsAsync()
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-30);

            // Get organizations that have been soft deleted for more than 30 days
            var orgsToDelete = await _dbContext.Organizations
                .IgnoreQueryFilters()
                .Where(o => o.DeletedAt != null && o.DeletedAt < cutoffDate)
                .ToListAsync();

            if (orgsToDelete.Count == 0)
            {
                return;
            }

            foreach (var org in orgsToDelete)
            {
                // Due to cascade deletes, this will remove all related data
                _dbContext.Organizations.Remove(org);
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Permanently deleted {Count} organizations", orgsToDelete.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to permanently delete organizations");
            throw;
        }
    }
}
