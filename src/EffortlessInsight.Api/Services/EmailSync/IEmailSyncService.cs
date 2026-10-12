using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Service for managing connected email accounts and sync operations.
/// </summary>
public interface IEmailSyncService
{
    /// <summary>
    /// Gets all connected email accounts for an organization.
    /// </summary>
    Task<List<ConnectedEmailAccount>> GetAccountsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a connected email account by ID.
    /// </summary>
    Task<ConnectedEmailAccount?> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the OAuth authorization URL for connecting an email account.
    /// </summary>
    Task<OAuthInitiateResult> InitiateOAuthAsync(
        Guid organizationId,
        Guid userId,
        string provider,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the OAuth flow and creates a connected email account.
    /// </summary>
    Task<OAuthCompleteResult> CompleteOAuthAsync(
        Guid organizationId,
        Guid userId,
        string provider,
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects and removes a connected email account.
    /// </summary>
    Task<bool> DisconnectAsync(
        Guid accountId,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a sync operation for a connected email account.
    /// </summary>
    Task<EmailSyncOperationResult> SyncAccountAsync(
        Guid accountId,
        Guid? triggeredById = null,
        string triggerSource = "manual",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the access token for an account if it's expiring soon.
    /// </summary>
    Task<bool> RefreshTokenIfNeededAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sync history for an account.
    /// </summary>
    Task<List<EmailSyncLog>> GetSyncHistoryAsync(
        Guid accountId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates account settings.
    /// </summary>
    Task<ConnectedEmailAccount?> UpdateSettingsAsync(
        Guid accountId,
        bool? autoSyncEnabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all accounts that need token refresh.
    /// </summary>
    Task<List<ConnectedEmailAccount>> GetAccountsNeedingRefreshAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all accounts due for sync.
    /// </summary>
    Task<List<ConnectedEmailAccount>> GetAccountsDueForSyncAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a sync failure for an account.
    /// </summary>
    Task RecordSyncFailureAsync(
        Guid accountId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsuspends accounts that are eligible for retry.
    /// </summary>
    Task<int> UnsuspendEligibleAccountsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of OAuth initiation.
/// </summary>
public record OAuthInitiateResult
{
    public bool Success { get; init; }
    public string? AuthorizationUrl { get; init; }
    public string? State { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of OAuth completion.
/// </summary>
public record OAuthCompleteResult
{
    public string? ReturnUri { get; init; }
    public bool Success { get; init; }
    public ConnectedEmailAccount? Account { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of a sync operation.
/// </summary>
public record EmailSyncOperationResult
{
    public bool Success { get; init; }
    public Guid? SyncLogId { get; init; }
    public int EmailsScanned { get; init; }
    public int NoticesImported { get; init; }
    public int AttachmentsSkipped { get; init; }
    public int DuplicatesSkipped { get; init; }
    public int NoticesFailed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
