using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Service for managing GSTN portal connections.
/// </summary>
public class GstnConnectionService : IGstnConnectionService
{
    private readonly ApplicationDbContext _context;
    private readonly IGspClient _gspClient;
    private readonly IFieldEncryptionService _encryption;
    private readonly IMemoryCache _cache;
    private readonly GstnOptions _options;
    private readonly ILogger<GstnConnectionService> _logger;

    // Cache durations
    private static readonly TimeSpan ConnectionStatusCacheDuration = TimeSpan.FromSeconds(30);

    public GstnConnectionService(
        ApplicationDbContext context,
        IGspClient gspClient,
        IFieldEncryptionService encryption,
        IMemoryCache cache,
        IOptions<GstnOptions> options,
        ILogger<GstnConnectionService> logger)
    {
        _context = context;
        _gspClient = gspClient;
        _encryption = encryption;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GstnConnection?> GetConnectionAsync(Guid organizationGstinId, CancellationToken cancellationToken = default)
    {
        return await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .Include(c => c.ConnectedBy)
            .FirstOrDefaultAsync(c => c.OrganizationGstinId == organizationGstinId, cancellationToken);
    }

    public async Task<List<GstnConnection>> GetConnectionsForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .Include(c => c.ConnectedBy)
            .Where(c => c.OrganizationGstin.OrganizationId == organizationId)
            .OrderBy(c => c.OrganizationGstin.Gstin)
            .ToListAsync(cancellationToken);
    }

    public async Task<GstnConnectionStatusDto?> GetConnectionStatusAsync(Guid organizationGstinId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{GstnConstants.CacheKeys.ConnectionStatusPrefix}{organizationGstinId}";

        // Try to get from cache first
        if (_cache.TryGetValue(cacheKey, out GstnConnectionStatusDto? cachedStatus))
        {
            return cachedStatus;
        }

        var connection = await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .Include(c => c.ConnectedBy)
            .FirstOrDefaultAsync(c => c.OrganizationGstinId == organizationGstinId, cancellationToken);

        GstnConnectionStatusDto? status;

        if (connection == null)
        {
            var gstin = await _context.OrganizationGstins
                .FirstOrDefaultAsync(g => g.Id == organizationGstinId, cancellationToken);

            if (gstin == null) return null;

            status = new GstnConnectionStatusDto(
                OrganizationGstinId: organizationGstinId,
                Gstin: gstin.Gstin,
                Status: GstnConnectionStatus.Disconnected,
                IsConnected: false,
                ConnectedAt: null,
                LastSyncAt: null,
                NextScheduledSyncAt: null,
                AutoSyncEnabled: false,
                SyncIntervalHours: _options.DefaultSyncIntervalHours,
                ConsecutiveFailures: 0,
                LastSyncError: null,
                ConnectedByName: null
            );
        }
        else
        {
            status = new GstnConnectionStatusDto(
                OrganizationGstinId: organizationGstinId,
                Gstin: connection.OrganizationGstin.Gstin,
                Status: connection.Status,
                IsConnected: connection.Status == GstnConnectionStatus.Connected,
                ConnectedAt: connection.ConnectedAt,
                LastSyncAt: connection.LastSyncAt,
                NextScheduledSyncAt: connection.NextScheduledSyncAt,
                AutoSyncEnabled: connection.AutoSyncEnabled,
                SyncIntervalHours: connection.SyncIntervalHours,
                ConsecutiveFailures: connection.ConsecutiveFailures,
                LastSyncError: connection.LastSyncError,
                ConnectedByName: connection.ConnectedBy?.Name
            );
        }

        // Cache the result
        _cache.Set(cacheKey, status, ConnectionStatusCacheDuration);

        return status;
    }

    public async Task<GstnConnection> UpdateSettingsAsync(
        Guid organizationGstinId,
        UpdateGstnConnectionSettingsRequest settings,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _context.GstnConnections
            .FirstOrDefaultAsync(c => c.OrganizationGstinId == organizationGstinId, cancellationToken)
            ?? throw new InvalidOperationException("Connection not found");

        if (settings.AutoSyncEnabled.HasValue)
        {
            connection.AutoSyncEnabled = settings.AutoSyncEnabled.Value;

            if (settings.AutoSyncEnabled.Value && connection.Status == GstnConnectionStatus.Connected)
            {
                connection.NextScheduledSyncAt = DateTime.UtcNow.AddHours(connection.SyncIntervalHours);
            }
            else if (!settings.AutoSyncEnabled.Value)
            {
                connection.NextScheduledSyncAt = null;
            }
        }

        if (settings.SyncIntervalHours.HasValue)
        {
            var interval = Math.Clamp(
                settings.SyncIntervalHours.Value,
                _options.MinSyncIntervalHours,
                _options.MaxSyncIntervalHours);

            connection.SyncIntervalHours = interval;

            if (connection.AutoSyncEnabled && connection.Status == GstnConnectionStatus.Connected)
            {
                connection.NextScheduledSyncAt = DateTime.UtcNow.AddHours(interval);
            }
        }

        connection.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cached status
        InvalidateConnectionStatusCache(organizationGstinId);

        _logger.LogInformation(
            "Updated connection settings for GSTIN {GstinId}: AutoSync={AutoSync}, Interval={Interval}h",
            organizationGstinId,
            connection.AutoSyncEnabled,
            connection.SyncIntervalHours);

        return connection;
    }

    public async Task DisconnectAsync(
        Guid organizationGstinId,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .FirstOrDefaultAsync(c => c.OrganizationGstinId == organizationGstinId, cancellationToken);

        if (connection == null) return;

        // Revoke tokens on GSP side before clearing locally
        if (!string.IsNullOrEmpty(connection.EncryptedAccessToken))
        {
            try
            {
                var accessToken = _encryption.Decrypt(connection.EncryptedAccessToken);
                await _gspClient.RevokeTokenAsync(
                    accessToken,
                    connection.OrganizationGstin.Gstin,
                    cancellationToken);

                _logger.LogInformation(
                    "Revoked GSP tokens for GSTIN {GstinId}",
                    organizationGstinId);
            }
            catch (Exception ex)
            {
                // Log but don't fail - we still want to disconnect locally
                _logger.LogWarning(ex,
                    "Failed to revoke GSP tokens for GSTIN {GstinId}, proceeding with local disconnect",
                    organizationGstinId);
            }
        }

        connection.Status = GstnConnectionStatus.Disconnected;
        connection.EncryptedAccessToken = null;
        connection.EncryptedRefreshToken = null;
        connection.TokenExpiresAt = null;
        connection.RefreshTokenExpiresAt = null;
        connection.GspSessionId = null;
        connection.DisconnectedById = userId;
        connection.DisconnectedAt = DateTime.UtcNow;
        connection.DisconnectionReason = reason;
        connection.NextScheduledSyncAt = null;
        connection.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cached status
        InvalidateConnectionStatusCache(organizationGstinId);

        _logger.LogInformation(
            "Disconnected GSTIN {GstinId} from GSTN portal by user {UserId}",
            organizationGstinId,
            userId);
    }

    public async Task RecordSyncFailureAsync(
        Guid connectionId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var connection = await _context.GstnConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection == null) return;

        connection.ConsecutiveFailures++;
        connection.LastSyncError = errorMessage;
        connection.UpdatedAt = DateTime.UtcNow;

        if (connection.ConsecutiveFailures >= _options.MaxConsecutiveFailures)
        {
            connection.Status = GstnConnectionStatus.Suspended;
            _logger.LogWarning(
                "Connection {ConnectionId} suspended after {Failures} consecutive failures",
                connectionId,
                connection.ConsecutiveFailures);
        }
        else if (connection.AutoSyncEnabled)
        {
            // Schedule retry with exponential backoff, capped at MaxBackoffHours
            var backoffMinutes = Math.Min(
                Math.Pow(2, Math.Min(connection.ConsecutiveFailures, 10)) * 15, // Cap exponent to prevent overflow
                _options.MaxBackoffHours * 60);
            connection.NextScheduledSyncAt = DateTime.UtcNow.AddMinutes(backoffMinutes);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cached status
        InvalidateConnectionStatusCache(connection.OrganizationGstinId);
    }

    public async Task RecordSyncSuccessAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection == null) return;

        connection.ConsecutiveFailures = 0;
        connection.LastSyncError = null;
        connection.LastSyncAt = DateTime.UtcNow;
        connection.UpdatedAt = DateTime.UtcNow;

        if (connection.AutoSyncEnabled)
        {
            connection.NextScheduledSyncAt = DateTime.UtcNow.AddHours(connection.SyncIntervalHours);
        }

        // Update the GSTIN's last sync timestamp
        connection.OrganizationGstin.LastSyncedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cached status
        InvalidateConnectionStatusCache(connection.OrganizationGstinId);
    }

    public async Task<List<GstnConnection>> GetConnectionsNeedingRefreshAsync(CancellationToken cancellationToken = default)
    {
        var refreshThreshold = DateTime.UtcNow.AddMinutes(_options.TokenRefreshThresholdMinutes);

        return await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .Where(c => c.Status == GstnConnectionStatus.Connected)
            .Where(c => c.TokenExpiresAt != null && c.TokenExpiresAt <= refreshThreshold)
            .Where(c => c.EncryptedRefreshToken != null)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<GstnConnection>> GetConnectionsDueForSyncAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .Where(c => c.Status == GstnConnectionStatus.Connected)
            .Where(c => c.AutoSyncEnabled)
            .Where(c => c.NextScheduledSyncAt != null && c.NextScheduledSyncAt <= now)
            .OrderBy(c => c.NextScheduledSyncAt)
            .Take(50) // Process in batches
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void InvalidateConnectionStatusCache(Guid organizationGstinId)
    {
        var cacheKey = $"{GstnConstants.CacheKeys.ConnectionStatusPrefix}{organizationGstinId}";
        _cache.Remove(cacheKey);
    }

    public async Task<int> UnsuspendEligibleConnectionsAsync(CancellationToken cancellationToken = default)
    {
        // Unsuspend connections that have been suspended for at least 24 hours
        var eligibleThreshold = DateTime.UtcNow.AddHours(-24);

        var suspendedConnections = await _context.GstnConnections
            .Where(c => c.Status == GstnConnectionStatus.Suspended)
            .Where(c => c.UpdatedAt <= eligibleThreshold)
            .ToListAsync(cancellationToken);

        foreach (var connection in suspendedConnections)
        {
            connection.Status = GstnConnectionStatus.Connected;
            connection.ConsecutiveFailures = 0;
            connection.NextScheduledSyncAt = DateTime.UtcNow;
            connection.UpdatedAt = DateTime.UtcNow;
        }

        if (suspendedConnections.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);

            // Invalidate cached status for all unsuspended connections
            foreach (var connection in suspendedConnections)
            {
                InvalidateConnectionStatusCache(connection.OrganizationGstinId);
            }

            _logger.LogInformation("Unsuspended {Count} eligible connections", suspendedConnections.Count);
        }

        return suspendedConnections.Count;
    }
}
