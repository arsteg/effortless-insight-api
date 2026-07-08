using System.ComponentModel.DataAnnotations;
using EffortlessInsight.Api.Data.Entities.GstSync;

namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// GST CLIENT DTOs
// ============================================================================

/// <summary>
/// Request to add a new GSTIN for monitoring.
/// </summary>
public record CreateGstClientRequest
{
    /// <summary>
    /// The GSTIN to monitor (15 characters).
    /// </summary>
    [Required]
    [RegularExpression(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$",
        ErrorMessage = "Invalid GSTIN format")]
    public string Gstin { get; init; } = null!;

    /// <summary>
    /// Trade name associated with this GSTIN.
    /// </summary>
    [MaxLength(255)]
    public string? TradeName { get; init; }

    /// <summary>
    /// Legal name of the entity.
    /// </summary>
    [MaxLength(255)]
    public string? LegalName { get; init; }

    /// <summary>
    /// Hours between automatic syncs (1 to 168).
    /// </summary>
    [Range(1, 168)]
    public int SyncFrequencyHours { get; init; } = 6;

    /// <summary>
    /// Whether to automatically import synced notices.
    /// </summary>
    public bool AutoImportToNotices { get; init; } = true;
}

/// <summary>
/// Request to update a GST client connection.
/// </summary>
public record UpdateGstClientRequest
{
    /// <summary>
    /// Trade name associated with this GSTIN.
    /// </summary>
    [MaxLength(255)]
    public string? TradeName { get; init; }

    /// <summary>
    /// Legal name of the entity.
    /// </summary>
    [MaxLength(255)]
    public string? LegalName { get; init; }

    /// <summary>
    /// Whether automatic sync is enabled.
    /// </summary>
    public bool? SyncEnabled { get; init; }

    /// <summary>
    /// Hours between automatic syncs (1 to 168).
    /// </summary>
    [Range(1, 168)]
    public int? SyncFrequencyHours { get; init; }

    /// <summary>
    /// Whether to automatically import synced notices.
    /// </summary>
    public bool? AutoImportToNotices { get; init; }
}

/// <summary>
/// Response containing GST client details.
/// </summary>
public record GstClientDto
{
    public Guid Id { get; init; }
    public string Gstin { get; init; } = null!;
    public string? TradeName { get; init; }
    public string? LegalName { get; init; }
    public string? StateCode { get; init; }
    public bool SyncEnabled { get; init; }
    public int SyncFrequencyHours { get; init; }
    public bool AutoImportToNotices { get; init; }
    public string Status { get; init; } = null!;
    public string? StatusMessage { get; init; }
    public DateTime? LastSyncAt { get; init; }
    public string? LastSyncSource { get; init; }
    public DateTime? LastSuccessfulSyncAt { get; init; }
    public DateTime? NextSyncDueAt { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int TotalNoticesSynced { get; init; }
    public int TotalSyncsPerformed { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
}

/// <summary>
/// Response for listing GST clients.
/// </summary>
public record GstClientListResponse(
    List<GstClientDto> Items,
    int TotalCount);

// ============================================================================
// SYNC SESSION DTOs
// ============================================================================

/// <summary>
/// Request from extension/agent to start a sync session.
/// </summary>
public record StartSyncSessionRequest
{
    /// <summary>
    /// ID of the GST client connection to sync.
    /// </summary>
    [Required]
    public Guid GstClientId { get; init; }

    /// <summary>
    /// Source of the sync (chrome_extension, desktop_agent, manual_upload).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string SyncSource { get; init; } = null!;

    /// <summary>
    /// Version of the extension/agent.
    /// </summary>
    [MaxLength(20)]
    public string? SourceVersion { get; init; }

    /// <summary>
    /// Additional metadata (browser info, etc.).
    /// </summary>
    public Dictionary<string, object>? SourceMetadata { get; init; }
}

/// <summary>
/// Request to sync notices (batch upload from extension/agent).
/// </summary>
public record SyncNoticesRequest
{
    /// <summary>
    /// ID of the active sync session.
    /// </summary>
    [Required]
    public Guid SessionId { get; init; }

    /// <summary>
    /// List of notices captured from the portal.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<SyncNoticeData> Notices { get; init; } = [];
}

/// <summary>
/// Individual notice data from the portal.
/// </summary>
public record SyncNoticeData
{
    /// <summary>
    /// Unique identifier from the GST portal.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string PortalNoticeId { get; init; } = null!;

    /// <summary>
    /// Reference number of the notice.
    /// </summary>
    [MaxLength(100)]
    public string? ReferenceNumber { get; init; }

    /// <summary>
    /// Type of notice (DRC-01, ASMT-10, etc.).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string NoticeType { get; init; } = null!;

    /// <summary>
    /// Date when the notice was issued (YYYY-MM-DD).
    /// </summary>
    [Required]
    public DateOnly IssueDate { get; init; }

    /// <summary>
    /// Due date for response/compliance.
    /// </summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// Status as shown on the GST portal.
    /// </summary>
    [MaxLength(50)]
    public string? StatusOnPortal { get; init; }

    /// <summary>
    /// Total demand amount.
    /// </summary>
    public decimal? DemandAmount { get; init; }

    /// <summary>
    /// Tax component.
    /// </summary>
    public decimal? TaxAmount { get; init; }

    /// <summary>
    /// Interest component.
    /// </summary>
    public decimal? InterestAmount { get; init; }

    /// <summary>
    /// Penalty component.
    /// </summary>
    public decimal? PenaltyAmount { get; init; }

    /// <summary>
    /// Tax period (e.g., "Apr-2026").
    /// </summary>
    [MaxLength(50)]
    public string? TaxPeriod { get; init; }

    /// <summary>
    /// Financial year (e.g., "2025-26").
    /// </summary>
    [MaxLength(10)]
    public string? FinancialYear { get; init; }

    /// <summary>
    /// Section/rule under which notice was issued.
    /// </summary>
    [MaxLength(100)]
    public string? SectionRule { get; init; }

    /// <summary>
    /// Name of the issuing officer.
    /// </summary>
    [MaxLength(255)]
    public string? OfficerName { get; init; }

    /// <summary>
    /// Designation of the officer.
    /// </summary>
    [MaxLength(255)]
    public string? OfficerDesignation { get; init; }

    /// <summary>
    /// Jurisdiction (ward/division/zone).
    /// </summary>
    [MaxLength(255)]
    public string? Jurisdiction { get; init; }

    /// <summary>
    /// Whether PDF is available for download.
    /// </summary>
    public bool PdfAvailable { get; init; }

    /// <summary>
    /// Raw data from the portal for debugging.
    /// </summary>
    public Dictionary<string, object>? RawData { get; init; }
}

/// <summary>
/// Request to complete a sync session.
/// </summary>
public record CompleteSyncSessionRequest
{
    /// <summary>
    /// ID of the sync session to complete.
    /// </summary>
    [Required]
    public Guid SessionId { get; init; }

    /// <summary>
    /// Final status (completed, failed, partial).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; init; } = null!;

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code if failed.
    /// </summary>
    [MaxLength(50)]
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Response containing sync session details.
/// </summary>
public record GstSyncSessionDto
{
    public Guid Id { get; init; }
    public Guid GstClientId { get; init; }
    public string Gstin { get; init; } = null!;
    public string SyncSource { get; init; } = null!;
    public string? SourceVersion { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int? DurationMs { get; init; }
    public string Status { get; init; } = null!;
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public int NoticesFound { get; init; }
    public int NoticesNew { get; init; }
    public int NoticesUpdated { get; init; }
    public int NoticesUnchanged { get; init; }
    public int PdfsDownloaded { get; init; }
    public int PdfsFailed { get; init; }
}

/// <summary>
/// Result of syncing notices.
/// </summary>
public record SyncNoticesResult
{
    public int NoticesProcessed { get; init; }
    public int NoticesNew { get; init; }
    public int NoticesUpdated { get; init; }
    public int NoticesUnchanged { get; init; }
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Response for listing sync sessions with pagination.
/// </summary>
public record GstSyncSessionListResponse
{
    public List<GstSyncSessionDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>
/// GST Sync statistics for the organization.
/// </summary>
public record GstSyncStatisticsDto
{
    /// <summary>
    /// Total number of GST clients registered.
    /// </summary>
    public int TotalClients { get; init; }

    /// <summary>
    /// Number of clients with active sync enabled.
    /// </summary>
    public int ActiveClients { get; init; }

    /// <summary>
    /// Number of clients with sync paused.
    /// </summary>
    public int PausedClients { get; init; }

    /// <summary>
    /// Number of clients with errors.
    /// </summary>
    public int ErrorClients { get; init; }

    /// <summary>
    /// Total notices synced from all clients.
    /// </summary>
    public int TotalNoticesSynced { get; init; }

    /// <summary>
    /// Notices synced in the last 24 hours.
    /// </summary>
    public int NoticesSyncedToday { get; init; }

    /// <summary>
    /// Notices pending import to main notices module.
    /// </summary>
    public int PendingImports { get; init; }

    /// <summary>
    /// Time of last successful sync across all clients.
    /// </summary>
    public DateTime? LastSyncTime { get; init; }

    /// <summary>
    /// Total sync sessions performed.
    /// </summary>
    public int TotalSyncSessions { get; init; }

    /// <summary>
    /// Sync sessions in the last 24 hours.
    /// </summary>
    public int SyncSessionsToday { get; init; }
}

// ============================================================================
// RAW NOTICE DTOs
// ============================================================================

/// <summary>
/// Response containing raw notice details.
/// </summary>
public record GstNoticeRawDto
{
    public Guid Id { get; init; }
    public Guid GstClientId { get; init; }
    public string Gstin { get; init; } = null!;
    public string PortalNoticeId { get; init; } = null!;
    public string? ReferenceNumber { get; init; }
    public string NoticeType { get; init; } = null!;
    public string? NoticeCategory { get; init; }
    public DateOnly IssueDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public string? StatusOnPortal { get; init; }
    public decimal? DemandAmount { get; init; }
    public decimal? TaxAmount { get; init; }
    public decimal? InterestAmount { get; init; }
    public decimal? PenaltyAmount { get; init; }
    public string? TaxPeriod { get; init; }
    public string? FinancialYear { get; init; }
    public string? SectionRule { get; init; }
    public string? OfficerName { get; init; }
    public string? OfficerDesignation { get; init; }
    public string? Jurisdiction { get; init; }
    public bool PdfAvailable { get; init; }
    public string? PdfS3Key { get; init; }
    public int? PdfSizeBytes { get; init; }
    public bool ImportedToNotices { get; init; }
    public Guid? ImportedNoticeId { get; init; }
    public DateTime FirstSyncedAt { get; init; }
    public DateTime LastSyncedAt { get; init; }
    public int SyncCount { get; init; }
}

/// <summary>
/// Response for listing raw notices.
/// </summary>
public record GstNoticeRawListResponse
{
    public List<GstNoticeRawDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>
/// Request to import raw notices to the main Notices module.
/// </summary>
public record ImportNoticesRequest
{
    /// <summary>
    /// IDs of raw notices to import.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<Guid> NoticeIds { get; init; } = [];

    /// <summary>
    /// Optional user ID to assign the imported notices to.
    /// </summary>
    public Guid? AssignToUserId { get; init; }
}

/// <summary>
/// Result of importing notices.
/// </summary>
public record ImportNoticesResult
{
    public int Imported { get; init; }
    public int AlreadyImported { get; init; }
    public int Failed { get; init; }
    public List<ImportedNoticeInfo> ImportedNotices { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

public record ImportedNoticeInfo(Guid RawNoticeId, Guid ImportedNoticeId);

// ============================================================================
// EXTENSION EVENT DTOs
// ============================================================================

/// <summary>
/// Request to log an extension event.
/// </summary>
public record LogExtensionEventRequest
{
    /// <summary>
    /// Type of event.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EventType { get; init; } = null!;

    /// <summary>
    /// Additional event data.
    /// </summary>
    public Dictionary<string, object>? EventData { get; init; }

    /// <summary>
    /// Version of the extension.
    /// </summary>
    [MaxLength(20)]
    public string? ExtensionVersion { get; init; }

    /// <summary>
    /// Browser information.
    /// </summary>
    [MaxLength(255)]
    public string? BrowserInfo { get; init; }

    /// <summary>
    /// Error type if this is an error event.
    /// </summary>
    [MaxLength(100)]
    public string? ErrorType { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Stack trace.
    /// </summary>
    public string? ErrorStack { get; init; }

    /// <summary>
    /// Page URL where the event occurred.
    /// </summary>
    public string? PageUrl { get; init; }
}

// ============================================================================
// EXTENSION CONFIG DTOs
// ============================================================================

/// <summary>
/// Configuration for the Chrome extension.
/// </summary>
public record ExtensionConfigResponse
{
    /// <summary>
    /// List of GSTINs enabled for sync.
    /// </summary>
    public List<string> EnabledGstins { get; init; } = [];

    /// <summary>
    /// Whether automatic capture is enabled.
    /// </summary>
    public bool AutoCapture { get; init; } = true;

    /// <summary>
    /// Whether to capture on notices page.
    /// </summary>
    public bool CaptureOnNoticesPage { get; init; } = true;

    /// <summary>
    /// Whether to auto-download PDFs.
    /// </summary>
    public bool AutoDownloadPdf { get; init; } = true;

    /// <summary>
    /// Whether to show browser notifications.
    /// </summary>
    public bool ShowNotifications { get; init; } = true;

    /// <summary>
    /// Minutes between sync operations.
    /// </summary>
    public int SyncIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// DOM selectors for parsing (can be updated server-side).
    /// </summary>
    public Dictionary<string, string> Selectors { get; init; } = new();
}

/// <summary>
/// Request for extension heartbeat.
/// </summary>
public record ExtensionHeartbeatRequest
{
    [MaxLength(20)]
    public string? ExtensionVersion { get; init; }

    [MaxLength(255)]
    public string? BrowserInfo { get; init; }

    public DateTime? LastActivity { get; init; }
}

/// <summary>
/// Response for extension heartbeat.
/// </summary>
public record ExtensionHeartbeatResponse
{
    public string Status { get; init; } = "ok";
    public bool UpdateAvailable { get; init; }
    public bool ConfigChanged { get; init; }
    public string? LatestVersion { get; init; }
}

// ============================================================================
// PDF UPLOAD DTOs
// ============================================================================

/// <summary>
/// Request to get a presigned URL for PDF upload.
/// </summary>
public record GetPdfUploadUrlRequest
{
    /// <summary>
    /// ID of the raw notice to upload PDF for.
    /// </summary>
    [Required]
    public Guid NoticeId { get; init; }

    /// <summary>
    /// File name of the PDF.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string FileName { get; init; } = null!;

    /// <summary>
    /// Size of the file in bytes.
    /// </summary>
    [Required]
    [Range(1, 52428800)] // Max 50MB
    public int FileSize { get; init; }
}

/// <summary>
/// Response with presigned URL for PDF upload.
/// </summary>
public record PdfUploadUrlResponse
{
    public string UploadUrl { get; init; } = null!;
    public string S3Key { get; init; } = null!;
    public DateTime ExpiresAt { get; init; }
}

/// <summary>
/// Request to confirm PDF upload completed.
/// </summary>
public record ConfirmPdfUploadRequest
{
    [Required]
    public Guid NoticeId { get; init; }

    [Required]
    public string S3Key { get; init; } = null!;

    [Required]
    [Range(1, 52428800)]
    public int FileSize { get; init; }
}

// ============================================================================
// NOTIFICATION DTOs
// ============================================================================

/// <summary>
/// Response for upcoming due dates (for extension notifications).
/// </summary>
public record UpcomingDueDatesResponse
{
    /// <summary>
    /// Notices with upcoming or overdue dates.
    /// </summary>
    public List<UpcomingDueDateNotice> Notices { get; init; } = [];

    /// <summary>
    /// Total count of notices with due dates in the range.
    /// </summary>
    public int TotalCount { get; init; }
}

/// <summary>
/// Notice with due date info for notifications.
/// </summary>
public record UpcomingDueDateNotice
{
    /// <summary>
    /// Notice ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// GSTIN of the notice.
    /// </summary>
    public string Gstin { get; init; } = null!;

    /// <summary>
    /// GST Client ID.
    /// </summary>
    public Guid GstClientId { get; init; }

    /// <summary>
    /// Client/Trade name.
    /// </summary>
    public string? ClientName { get; init; }

    /// <summary>
    /// Type of notice.
    /// </summary>
    public string NoticeType { get; init; } = null!;

    /// <summary>
    /// Due date.
    /// </summary>
    public DateOnly DueDate { get; init; }

    /// <summary>
    /// Demand amount if any.
    /// </summary>
    public decimal? DemandAmount { get; init; }

    /// <summary>
    /// Days until due (negative if overdue).
    /// </summary>
    public int DaysUntilDue { get; init; }

    /// <summary>
    /// Whether the notice is overdue.
    /// </summary>
    public bool IsOverdue { get; init; }
}
