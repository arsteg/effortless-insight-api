using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Service for managing GSTN portal connections.
/// </summary>
public interface IGstnConnectionService
{
    /// <summary>
    /// Gets the connection for a GSTIN.
    /// </summary>
    Task<GstnConnection?> GetConnectionAsync(Guid organizationGstinId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all connections for an organization.
    /// </summary>
    Task<List<GstnConnection>> GetConnectionsForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the connection status summary for a GSTIN.
    /// </summary>
    Task<GstnConnectionStatusDto?> GetConnectionStatusAsync(Guid organizationGstinId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates connection settings.
    /// </summary>
    Task<GstnConnection> UpdateSettingsAsync(
        Guid organizationGstinId,
        UpdateGstnConnectionSettingsRequest settings,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the GSTN portal.
    /// </summary>
    Task DisconnectAsync(
        Guid organizationGstinId,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a connection as having a failure.
    /// </summary>
    Task RecordSyncFailureAsync(
        Guid connectionId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the connection status after successful sync.
    /// </summary>
    Task RecordSyncSuccessAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connections that need token refresh.
    /// </summary>
    Task<List<GstnConnection>> GetConnectionsNeedingRefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connections due for sync.
    /// </summary>
    Task<List<GstnConnection>> GetConnectionsDueForSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsuspends connections that can be retried.
    /// </summary>
    Task<int> UnsuspendEligibleConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached connection status for a GSTIN.
    /// Call this after modifying connection state outside of this service.
    /// </summary>
    void InvalidateConnectionStatusCache(Guid organizationGstinId);
}

/// <summary>
/// Connection status DTO.
/// </summary>
public record GstnConnectionStatusDto(
    Guid OrganizationGstinId,
    string Gstin,
    string Status,
    bool IsConnected,
    DateTime? ConnectedAt,
    DateTime? LastSyncAt,
    DateTime? NextScheduledSyncAt,
    bool AutoSyncEnabled,
    int SyncIntervalHours,
    int ConsecutiveFailures,
    string? LastSyncError,
    string? ConnectedByName
);

/// <summary>
/// Request to update connection settings.
/// </summary>
public record UpdateGstnConnectionSettingsRequest(
    bool? AutoSyncEnabled,
    int? SyncIntervalHours
);
