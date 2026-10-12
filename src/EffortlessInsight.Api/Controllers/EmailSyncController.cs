using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.EmailSync;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for email sync integration (Gmail and Outlook).
/// </summary>
[ApiController]
[Route("api/v1/email-sync")]
[Authorize]
public class EmailSyncController : ControllerBase
{
    private readonly IEmailSyncService _emailSyncService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly EmailSyncOptions _options;
    private readonly ILogger<EmailSyncController> _logger;

    public EmailSyncController(
        IEmailSyncService emailSyncService,
        ApplicationDbContext dbContext,
        ICurrentOrganizationService currentOrg,
        IOptions<EmailSyncOptions> options,
        ILogger<EmailSyncController> logger)
    {
        _emailSyncService = emailSyncService;
        _dbContext = dbContext;
        _currentOrg = currentOrg;
        _options = options.Value;
        _logger = logger;
    }

    #region Account Management

    /// <summary>
    /// Get all connected email accounts for the organization.
    /// </summary>
    [HttpGet("accounts")]
    [ProducesResponseType(typeof(ApiResponse<EmailAccountListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccounts(CancellationToken cancellationToken)
    {
        var orgId = GetCurrentOrganizationId();

        var accounts = await _emailSyncService.GetAccountsAsync(orgId, cancellationToken);

        var dtos = accounts.Select(EmailAccountDto.FromEntity).ToList();

        return Ok(new ApiResponse<EmailAccountListResponse>(true,
            new EmailAccountListResponse(dtos, dtos.Count)));
    }

    /// <summary>
    /// Get a specific connected email account.
    /// </summary>
    [HttpGet("accounts/{accountId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<EmailAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccount(Guid accountId, CancellationToken cancellationToken)
    {
        var orgId = GetCurrentOrganizationId();

        var account = await _emailSyncService.GetAccountAsync(accountId, cancellationToken);

        if (account == null || account.OrganizationId != orgId)
        {
            return NotFound(new ApiErrorResponse(false, "ACCOUNT_NOT_FOUND", "Email account not found"));
        }

        return Ok(new ApiResponse<EmailAccountDto>(true, EmailAccountDto.FromEntity(account)));
    }

    #endregion

    #region OAuth Connection

    /// <summary>
    /// Initiate OAuth connection for an email provider.
    /// </summary>
    [HttpPost("connect/{provider}")]
    [ProducesResponseType(typeof(ApiResponse<EmailConnectInitiateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InitiateConnection(
        string provider,
        [FromQuery] string? redirectUri,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return BadRequest(new ApiErrorResponse(false, "FEATURE_DISABLED", "Email sync is not enabled"));
        }

        // Check permission
        if (!_currentOrg.HasPermission("integrations.manage"))
        {
            return Forbid();
        }

        var orgId = GetCurrentOrganizationId();
        var userId = GetCurrentUserId();

        // Use provided redirect URI or construct from request
        var callbackUri = redirectUri ?? $"{Request.Scheme}://{Request.Host}/api/v1/email-sync/callback/{provider}";

        var result = await _emailSyncService.InitiateOAuthAsync(
            orgId, userId, provider.ToLowerInvariant(), callbackUri, cancellationToken);

        var response = new EmailConnectInitiateResponse(
            Success: result.Success,
            AuthorizationUrl: result.AuthorizationUrl,
            State: result.State,
            ErrorCode: result.ErrorCode,
            ErrorMessage: result.ErrorMessage
        );

        if (!result.Success)
        {
            return BadRequest(new ApiResponse<EmailConnectInitiateResponse>(false, response));
        }

        return Ok(new ApiResponse<EmailConnectInitiateResponse>(true, response));
    }

    /// <summary>
    /// OAuth callback endpoint for completing the connection.
    /// </summary>
    [HttpGet("callback/{provider}")]
    [AllowAnonymous] // OAuth callbacks don't include auth headers
    [ProducesResponseType(typeof(ApiResponse<EmailConnectCallbackResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OAuthCallback(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? error_description,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrWhiteSpace(state))
            return BadRequest(new ApiErrorResponse(false, "INVALID_STATE", "Connection request expired. Please connect again."));
        var result = await _emailSyncService.CompleteOAuthAsync(Guid.Empty, Guid.Empty,
            provider.ToLowerInvariant(), string.IsNullOrEmpty(error) ? code ?? "" : "", state, "", cancellationToken);
        if (result.ReturnUri == null)
            return BadRequest(new ApiErrorResponse(false, "INVALID_STATE", result.ErrorMessage ?? "Please connect again."));
        return Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(result.ReturnUri,
            "email_sync", result.Success ? "connected" : result.ErrorCode ?? "connection_failed"));
    }

    /// <summary>
    /// Complete OAuth connection with code (for frontend-driven flow).
    /// </summary>
    [HttpPost("callback/{provider}")]
    [ProducesResponseType(typeof(ApiResponse<EmailConnectCallbackResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompleteConnection(
        string provider,
        [FromBody] EmailConnectCallbackRequest request,
        [FromQuery] string? redirectUri,
        CancellationToken cancellationToken)
    {
        if (!_currentOrg.HasPermission("integrations.manage")) return Forbid();
        var orgId = GetCurrentOrganizationId();
        var userId = GetCurrentUserId();

        // Use provided redirect URI or construct from request
        var callbackUri = redirectUri ?? $"{Request.Scheme}://{Request.Host}/api/v1/email-sync/callback/{provider}";

        var result = await _emailSyncService.CompleteOAuthAsync(
            orgId,
            userId,
            provider.ToLowerInvariant(),
            request.Code,
            request.State,
            callbackUri,
            cancellationToken);

        var response = new EmailConnectCallbackResponse(
            Success: result.Success,
            Account: result.Account != null ? EmailAccountDto.FromEntity(result.Account) : null,
            ErrorCode: result.ErrorCode,
            ErrorMessage: result.ErrorMessage
        );

        if (!result.Success)
        {
            return BadRequest(new ApiResponse<EmailConnectCallbackResponse>(false, response));
        }

        return Ok(new ApiResponse<EmailConnectCallbackResponse>(true, response));
    }

    /// <summary>
    /// Disconnect an email account.
    /// </summary>
    [HttpDelete("accounts/{accountId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DisconnectAccount(Guid accountId, CancellationToken cancellationToken)
    {
        // Check permission
        if (!_currentOrg.HasPermission("integrations.manage"))
        {
            return Forbid();
        }

        var orgId = GetCurrentOrganizationId();
        var userId = GetCurrentUserId();

        // Verify account belongs to organization
        var account = await _emailSyncService.GetAccountAsync(accountId, cancellationToken);
        if (account == null || account.OrganizationId != orgId)
        {
            return NotFound(new ApiErrorResponse(false, "ACCOUNT_NOT_FOUND", "Email account not found"));
        }

        var success = await _emailSyncService.DisconnectAsync(accountId, userId, "User disconnected", cancellationToken);

        if (!success)
        {
            return NotFound(new ApiErrorResponse(false, "DISCONNECT_FAILED", "Failed to disconnect account"));
        }

        return NoContent();
    }

    #endregion

    #region Sync Operations

    /// <summary>
    /// Trigger a manual sync for an email account.
    /// </summary>
    [HttpPost("accounts/{accountId:guid}/sync")]
    [ProducesResponseType(typeof(ApiResponse<EmailSyncTriggerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TriggerSync(Guid accountId, CancellationToken cancellationToken)
    {
        // Check permission
        if (!_currentOrg.HasPermission("integrations.manage"))
        {
            return Forbid();
        }

        var orgId = GetCurrentOrganizationId();
        var userId = GetCurrentUserId();

        // Verify account belongs to organization
        var account = await _emailSyncService.GetAccountAsync(accountId, cancellationToken);
        if (account == null || account.OrganizationId != orgId)
        {
            return NotFound(new ApiErrorResponse(false, "ACCOUNT_NOT_FOUND", "Email account not found"));
        }

        var result = await _emailSyncService.SyncAccountAsync(
            accountId, userId, "manual", cancellationToken);

        var response = new EmailSyncTriggerResponse(
            Success: result.Success,
            SyncLogId: result.SyncLogId,
            EmailsScanned: result.EmailsScanned,
            NoticesImported: result.NoticesImported,
            AttachmentsSkipped: result.AttachmentsSkipped,
            DuplicatesSkipped: result.DuplicatesSkipped,
            NoticesFailed: result.NoticesFailed,
            ErrorCode: result.ErrorCode,
            ErrorMessage: result.ErrorMessage
        );

        if (!result.Success)
        {
            return Ok(new ApiResponse<EmailSyncTriggerResponse>(false, response));
        }

        return Ok(new ApiResponse<EmailSyncTriggerResponse>(true, response));
    }

    /// <summary>
    /// Get sync history for an email account.
    /// </summary>
    [HttpGet("accounts/{accountId:guid}/history")]
    [ProducesResponseType(typeof(ApiResponse<EmailSyncHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSyncHistory(
        Guid accountId,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var orgId = GetCurrentOrganizationId();

        // Verify account belongs to organization
        var account = await _emailSyncService.GetAccountAsync(accountId, cancellationToken);
        if (account == null || account.OrganizationId != orgId)
        {
            return NotFound(new ApiErrorResponse(false, "ACCOUNT_NOT_FOUND", "Email account not found"));
        }

        var logs = await _emailSyncService.GetSyncHistoryAsync(accountId, limit, cancellationToken);

        // Get triggered by names
        var triggeredByIds = logs
            .Where(l => l.TriggeredById.HasValue)
            .Select(l => l.TriggeredById!.Value)
            .Distinct()
            .ToList();

        var userNames = await _dbContext.Users
            .Where(u => triggeredByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        var dtos = logs.Select(l => EmailSyncLogDto.FromEntity(l,
            l.TriggeredById.HasValue && userNames.TryGetValue(l.TriggeredById.Value, out var name) ? name : null
        )).ToList();

        return Ok(new ApiResponse<EmailSyncHistoryResponse>(true,
            new EmailSyncHistoryResponse(dtos, dtos.Count)));
    }

    #endregion

    #region Settings

    /// <summary>
    /// Update settings for an email account.
    /// </summary>
    [HttpPatch("accounts/{accountId:guid}/settings")]
    [ProducesResponseType(typeof(ApiResponse<EmailAccountSettingsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateSettings(
        Guid accountId,
        [FromBody] EmailAccountSettingsRequest request,
        CancellationToken cancellationToken)
    {
        // Check permission
        if (!_currentOrg.HasPermission("integrations.manage"))
        {
            return Forbid();
        }

        var orgId = GetCurrentOrganizationId();

        // Verify account belongs to organization
        var account = await _emailSyncService.GetAccountAsync(accountId, cancellationToken);
        if (account == null || account.OrganizationId != orgId)
        {
            return NotFound(new ApiErrorResponse(false, "ACCOUNT_NOT_FOUND", "Email account not found"));
        }

        var updated = await _emailSyncService.UpdateSettingsAsync(
            accountId, request.AutoSyncEnabled, cancellationToken);

        if (updated == null)
        {
            return NotFound(new ApiErrorResponse(false, "UPDATE_FAILED", "Failed to update settings"));
        }

        return Ok(new ApiResponse<EmailAccountSettingsResponse>(true,
            new EmailAccountSettingsResponse(updated.AutoSyncEnabled)));
    }

    #endregion

    #region Helper Methods

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        return userId;
    }

    private Guid GetCurrentOrganizationId()
    {
        var orgId = _currentOrg.OrganizationId;
        if (!orgId.HasValue)
        {
            throw new InvalidOperationException("No organization context");
        }
        return orgId.Value;
    }

    #endregion
}
