using EffortlessInsight.Api.Data.Entities.Billing;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for tracking and enforcing usage limits.
/// </summary>
public interface IUsageService
{
    /// <summary>
    /// Gets the current usage record for an organization.
    /// </summary>
    Task<UsageRecord?> GetCurrentUsageAsync(Guid organizationId);

    /// <summary>
    /// Gets or creates the usage record for the current billing period.
    /// </summary>
    Task<UsageRecord> GetOrCreateCurrentUsageAsync(Guid organizationId);

    /// <summary>
    /// Increments the notice count for an organization.
    /// </summary>
    Task IncrementNoticeCountAsync(Guid organizationId);

    /// <summary>
    /// Decrements the notice count for an organization.
    /// </summary>
    Task DecrementNoticeCountAsync(Guid organizationId);

    /// <summary>
    /// Updates the user count for an organization.
    /// </summary>
    Task UpdateUserCountAsync(Guid organizationId, int count);

    /// <summary>
    /// Updates the storage usage for an organization.
    /// </summary>
    Task UpdateStorageUsageAsync(Guid organizationId, long bytesChange);

    /// <summary>
    /// Increments the API call count for an organization.
    /// </summary>
    Task IncrementApiCallsAsync(Guid organizationId);

    /// <summary>
    /// Checks if an organization can create a new notice.
    /// </summary>
    Task<(bool CanCreate, string? Reason)> CanCreateNoticeAsync(Guid organizationId);

    /// <summary>
    /// Checks if an organization can add a new user.
    /// </summary>
    Task<(bool CanAdd, string? Reason)> CanAddUserAsync(Guid organizationId);

    /// <summary>
    /// Checks if an organization can upload a file of the specified size.
    /// </summary>
    Task<(bool CanUpload, string? Reason)> CanUploadFileAsync(Guid organizationId, long fileSize);

    /// <summary>
    /// Checks if an organization can make an API call.
    /// </summary>
    Task<(bool CanCall, string? Reason)> CanMakeApiCallAsync(Guid organizationId);

    /// <summary>
    /// Gets the usage percentage for a specific metric.
    /// </summary>
    Task<int> GetUsagePercentageAsync(Guid organizationId, string metric);

    /// <summary>
    /// Resets usage for a new billing period.
    /// </summary>
    Task ResetUsageForPeriodAsync(Guid organizationId, DateOnly periodStart, DateOnly periodEnd);

    /// <summary>
    /// Recalculates all usage metrics for an organization.
    /// </summary>
    Task RecalculateUsageAsync(Guid organizationId);
}
