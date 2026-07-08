using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// Service for managing GST client connections.
/// </summary>
public interface IGstClientService
{
    /// <summary>
    /// Get all GST clients for an organization.
    /// </summary>
    Task<List<GstClientDto>> GetClientsAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific GST client by ID.
    /// </summary>
    Task<GstClientDto?> GetClientByIdAsync(Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a GST client by GSTIN within an organization.
    /// </summary>
    Task<GstClientDto?> GetClientByGstinAsync(Guid organizationId, string gstin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new GST client connection.
    /// </summary>
    Task<GstClientDto> CreateClientAsync(Guid organizationId, Guid userId, CreateGstClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a GST client connection.
    /// </summary>
    Task<GstClientDto?> UpdateClientAsync(Guid clientId, UpdateGstClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a GST client connection (soft delete).
    /// </summary>
    Task<bool> DeleteClientAsync(Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause sync for a GST client.
    /// </summary>
    Task<bool> PauseSyncAsync(Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume sync for a GST client.
    /// </summary>
    Task<bool> ResumeSyncAsync(Guid clientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing sync sessions and syncing notices.
/// </summary>
public interface IGstSyncService
{
    /// <summary>
    /// Start a new sync session.
    /// </summary>
    Task<GstSyncSessionDto> StartSessionAsync(Guid organizationId, StartSyncSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync notices from extension/agent.
    /// </summary>
    Task<SyncNoticesResult> SyncNoticesAsync(Guid organizationId, SyncNoticesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete a sync session.
    /// </summary>
    Task<GstSyncSessionDto?> CompleteSessionAsync(Guid organizationId, CompleteSyncSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sync session by ID.
    /// </summary>
    Task<GstSyncSessionDto?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sync history for a GST client.
    /// </summary>
    Task<List<GstSyncSessionDto>> GetSyncHistoryAsync(Guid clientId, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all sync sessions for an organization with pagination.
    /// </summary>
    Task<GstSyncSessionListResponse> GetSessionsAsync(Guid organizationId, Guid? gstClientId, string? status, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sync statistics for an organization.
    /// </summary>
    Task<GstSyncStatisticsDto> GetStatisticsAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for managing raw GST notices.
/// </summary>
public interface IGstNoticeRawService
{
    /// <summary>
    /// Get raw notices for a GST client.
    /// </summary>
    Task<List<GstNoticeRawDto>> GetNoticesAsync(Guid clientId, bool? imported = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get raw notices for an organization.
    /// </summary>
    Task<List<GstNoticeRawDto>> GetNoticesByOrganizationAsync(Guid organizationId, bool? imported = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific raw notice by ID.
    /// </summary>
    Task<GstNoticeRawDto?> GetNoticeByIdAsync(Guid noticeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Import raw notices to the main Notices module.
    /// </summary>
    Task<ImportNoticesResult> ImportNoticesAsync(Guid organizationId, Guid userId, ImportNoticesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get presigned URL for PDF upload.
    /// </summary>
    Task<PdfUploadUrlResponse> GetPdfUploadUrlAsync(Guid organizationId, GetPdfUploadUrlRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirm PDF upload completed.
    /// </summary>
    Task<bool> ConfirmPdfUploadAsync(Guid organizationId, ConfirmPdfUploadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get notices with upcoming or overdue due dates for notifications.
    /// </summary>
    Task<UpcomingDueDatesResponse> GetUpcomingDueDatesAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for extension events and configuration.
/// </summary>
public interface IGstExtensionService
{
    /// <summary>
    /// Log an extension event.
    /// </summary>
    Task LogEventAsync(Guid? organizationId, Guid? userId, LogExtensionEventRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get extension configuration.
    /// </summary>
    Task<ExtensionConfigResponse> GetConfigAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handle extension heartbeat.
    /// </summary>
    Task<ExtensionHeartbeatResponse> HeartbeatAsync(Guid organizationId, Guid userId, ExtensionHeartbeatRequest request, CancellationToken cancellationToken = default);
}
