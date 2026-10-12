using System.Security.Cryptography;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Encryption;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Service for managing connected email accounts and sync operations.
/// </summary>
public class EmailSyncService : IEmailSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly INoticeServiceExtended _notices;
    private readonly IEmailOperationLock _locks;
    private readonly IEmailNoticeInspector _inspector;
    private readonly IEnumerable<IEmailProviderClient> _providerClients;
    private readonly INoticeDetectionService _detectionService;
    private readonly IFieldEncryptionService _encryption;
    private readonly IFileStorageService _storageService;
    private readonly IUsageService _usageService;
    private readonly IAuditService _auditService;
    private readonly IEmailOAuthAttemptStore _attempts;
    private readonly EmailSyncOptions _options;
    private readonly ILogger<EmailSyncService> _logger;


    public EmailSyncService(
        ApplicationDbContext context,
        INoticeServiceExtended notices, IEmailOperationLock locks, IEmailNoticeInspector inspector,
        IEnumerable<IEmailProviderClient> providerClients,
        INoticeDetectionService detectionService,
        IFieldEncryptionService encryption,
        IFileStorageService storageService,
        IUsageService usageService,
        IAuditService auditService,
        IEmailOAuthAttemptStore attempts,
        IOptions<EmailSyncOptions> options,
        ILogger<EmailSyncService> logger)
    {
        _context = context;
        _notices = notices; _locks = locks; _inspector = inspector;
        _providerClients = providerClients;
        _detectionService = detectionService;
        _encryption = encryption;
        _storageService = storageService;
        _usageService = usageService;
        _auditService = auditService;
        _attempts = attempts;
        _options = options.Value;
        _logger = logger;
    }

    private IEmailProviderClient GetProviderClient(string provider)
    {
        var client = _providerClients.FirstOrDefault(c =>
            c.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

        return client ?? throw new ArgumentException($"Unknown email provider: {provider}");
    }

    public async Task<List<ConnectedEmailAccount>> GetAccountsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConnectedEmailAccounts
            .Where(a => a.OrganizationId == organizationId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConnectedEmailAccount?> GetAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConnectedEmailAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
    }

    public async Task<OAuthInitiateResult> InitiateOAuthAsync(Guid organizationId, Guid userId,
        string provider, string redirectUri, CancellationToken cancellationToken = default)
    {
        provider = provider.ToLowerInvariant();
        if (!_options.Enabled || !EmailProvider.IsValid(provider))
            return new() { ErrorCode = "feature_disabled", ErrorMessage = "This email connection is unavailable." };
        if (!(provider == "gmail" ? _options.Gmail.IsConfigured : _options.Outlook.IsConfigured))
            return new() { ErrorCode = "provider_not_configured", ErrorMessage = "Your administrator needs to configure this email provider." };
        if (!_options.AllowedReturnUris.Contains(redirectUri, StringComparer.Ordinal) ||
            !Uri.TryCreate(_options.PublicApiBaseUrl, UriKind.Absolute, out var api) ||
            (api.Scheme != "https" && !(api.Scheme == "http" && api.IsLoopback)))
            return new() { ErrorCode = "redirect_not_configured", ErrorMessage = "The email connection return address is not configured. Contact your administrator." };
        var callback = _options.PublicApiBaseUrl.TrimEnd('/') + "/api/v1/email-sync/callback/" + provider;
        var verifier = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        var state = await _attempts.CreateAsync(new(organizationId, userId, provider, callback, redirectUri, verifier));
        return new() { Success = true, State = state, AuthorizationUrl = GetProviderClient(provider).GetAuthorizationUrl(state, callback, challenge) };
    }

    public async Task<OAuthCompleteResult> CompleteOAuthAsync(
        Guid organizationId,
        Guid userId,
        string provider,
        string code,
        string state,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var attempt = await _attempts.ConsumeAsync(state);
        if (attempt == null || attempt.Provider != provider ||
            (organizationId != Guid.Empty && (attempt.OrganizationId != organizationId || attempt.UserId != userId)))
            return new() { ErrorCode = "invalid_state", ErrorMessage = "This connection request expired or was already used. Please connect again." };
        organizationId = attempt.OrganizationId;
        userId = attempt.UserId;
        if (!_options.Enabled || !await _context.OrganizationMembers.AnyAsync(m =>
            m.OrganizationId == organizationId && m.UserId == userId && m.Status == "active" &&
            m.DeletedAt == null && (m.Role == "owner" || m.Role == "admin"), cancellationToken))
            return new() { ReturnUri = attempt.ReturnUri, ErrorCode = "permission_denied", ErrorMessage = "You no longer have permission to manage this connection." };
        if (string.IsNullOrWhiteSpace(code))
            return new() { ReturnUri = attempt.ReturnUri, ErrorCode = "access_denied", ErrorMessage = "Email access was not granted. You can try again." };

        var stage = "token_exchange";
        try
        {
            var client = GetProviderClient(provider);

            // Exchange code for tokens
            var tokenResult = await client.ExchangeCodeAsync(code, attempt.CallbackUri, attempt.Verifier, cancellationToken);
            if (!tokenResult.Success)
            {
                return new OAuthCompleteResult
                {
                    ReturnUri = attempt.ReturnUri,
                    Success = false,
                    ErrorCode = tokenResult.ErrorCode,
                    ErrorMessage = tokenResult.ErrorMessage
                };
            }

            if (string.IsNullOrWhiteSpace(tokenResult.AccessToken) || string.IsNullOrWhiteSpace(tokenResult.RefreshToken) || tokenResult.ExpiresAt <= DateTime.UtcNow || tokenResult.ExpiresAt == null)
                return new() { ReturnUri = attempt.ReturnUri, ErrorCode = "offline_access_required", ErrorMessage = "Please reconnect and allow ongoing read-only access." };

            // Get user profile
            stage = "profile";
            var profile = await client.GetUserProfileAsync(tokenResult.AccessToken!, cancellationToken);
            profile = profile with { Email = profile.Email.Trim().ToLowerInvariant() };
            if (string.IsNullOrEmpty(profile.Email))
            {
                return new OAuthCompleteResult
                {
                    ReturnUri = attempt.ReturnUri,
                    Success = false,
                    ErrorCode = "profile_failed",
                    ErrorMessage = "Failed to retrieve email address"
                };
            }

            stage = "connection_lock";
            await using var identityLock = await _locks.TryAcquireAsync("identity:" + organizationId + ":" + provider + ":" + profile.Email, cancellationToken);
            if (identityLock == null) return new() { ReturnUri = attempt.ReturnUri, ErrorCode = "connection_in_progress", ErrorMessage = "Another connection is in progress. Please retry." };
            // Check if account already exists
            stage = "account_lookup";
            var existingAccount = await _context.ConnectedEmailAccounts
                .FirstOrDefaultAsync(a =>
                    a.OrganizationId == organizationId &&
                    a.Email == profile.Email &&
                    a.Provider == provider,
                    cancellationToken);

            if (existingAccount != null)
            {
                stage = "connection_lock";
                await using var reconnectLock = await _locks.TryAcquireAsync("account:" + existingAccount.Id, cancellationToken);
                if (reconnectLock == null) return new() { ReturnUri = attempt.ReturnUri, ErrorCode = "sync_in_progress", ErrorMessage = "A sync is in progress. Please reconnect after it finishes." };
                stage = "account_reload";
                await _context.Entry(existingAccount).ReloadAsync(cancellationToken);
                stage = "token_encryption";
                // Update existing account
                existingAccount.AccessTokenEncrypted = _encryption.Encrypt(tokenResult.AccessToken!);
                existingAccount.RefreshTokenEncrypted = tokenResult.RefreshToken != null
                    ? _encryption.Encrypt(tokenResult.RefreshToken)
                    : existingAccount.RefreshTokenEncrypted;
                existingAccount.TokenExpiresAt = tokenResult.ExpiresAt;
                existingAccount.Scopes = tokenResult.Scopes;
                existingAccount.Status = EmailAccountStatus.Connected;
                existingAccount.UserId = userId;
                existingAccount.DisplayName = profile.DisplayName;
                existingAccount.LastError = null;
                existingAccount.ConsecutiveFailures = 0;
                existingAccount.UpdatedAt = DateTime.UtcNow;

                stage = "account_save";
                await _context.SaveChangesAsync(cancellationToken);

                stage = "audit";
                await LogAuditAsync(
                    "email_account.reconnected",
                    "ConnectedEmailAccount",
                    existingAccount.Id,
                    userId,
                    new { provider, email = profile.Email });

                return new OAuthCompleteResult
                {
                    ReturnUri = attempt.ReturnUri,
                    Success = true,
                    Account = existingAccount
                };
            }

            stage = "token_encryption";
            // Create new account
            var account = new ConnectedEmailAccount
            {
                UserId = userId,
                OrganizationId = organizationId,
                Provider = provider,
                Email = profile.Email,
                DisplayName = profile.DisplayName,
                AccessTokenEncrypted = _encryption.Encrypt(tokenResult.AccessToken!),
                RefreshTokenEncrypted = tokenResult.RefreshToken != null
                    ? _encryption.Encrypt(tokenResult.RefreshToken)
                    : null,
                TokenExpiresAt = tokenResult.ExpiresAt,
                Scopes = tokenResult.Scopes,
                Status = EmailAccountStatus.Connected,
                AutoSyncEnabled = true,
                ConnectedAt = DateTime.UtcNow
            };

            _context.ConnectedEmailAccounts.Add(account);
            stage = "account_save";
            await _context.SaveChangesAsync(cancellationToken);

            stage = "audit";
            await LogAuditAsync(
                "email_account.connected",
                "ConnectedEmailAccount",
                account.Id,
                userId,
                new { provider, email = profile.Email });

            return new OAuthCompleteResult
            {
                ReturnUri = attempt.ReturnUri,
                Success = true,
                Account = account
            };
        }
        catch (Exception ex)
        {
            var errorCode = EmailConnectionFailure.Code(ex, stage);
            _logger.LogError("Error completing OAuth for {Provider}: {Type}; Stage={Stage}; Reference={Reference}", provider, ex.GetType().Name, stage, errorCode);
            return new OAuthCompleteResult
            {
                ReturnUri = attempt.ReturnUri,
                Success = false,
                ErrorCode = errorCode,
                ErrorMessage = "Failed to complete OAuth"
            };
        }
    }

    public async Task<bool> DisconnectAsync(
        Guid accountId,
        Guid userId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await using var accountLock = await _locks.TryAcquireAsync("account:" + accountId, cancellationToken);
        if (accountLock == null) return false;
        var account = await _context.ConnectedEmailAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account == null) return false;

        // Clear tokens
        account.AccessTokenEncrypted = null;
        account.RefreshTokenEncrypted = null;
        account.TokenExpiresAt = null;
        account.Status = EmailAccountStatus.Disconnected;
        account.DisconnectedAt = DateTime.UtcNow;
        account.DisconnectionReason = reason ?? "User disconnected";
        account.UpdatedAt = DateTime.UtcNow;
        account.DeletedAt = DateTime.UtcNow; // Soft delete

        await _context.SaveChangesAsync(cancellationToken);

        await LogAuditAsync(
            "email_account.disconnected",
            "ConnectedEmailAccount",
            account.Id,
            userId,
            new { provider = account.Provider, email = account.Email, reason });

        return true;
    }

    public async Task<EmailSyncOperationResult> SyncAccountAsync(Guid accountId, Guid? triggeredById = null,
        string triggerSource = "manual", CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return new() { ErrorCode = "feature_disabled", ErrorMessage = "Email sync is disabled." };
        await using var accountLock = await _locks.TryAcquireAsync("account:" + accountId, cancellationToken);
        if (accountLock == null) return new() { ErrorCode = "sync_in_progress", ErrorMessage = "This email account is already syncing. Please wait." };
        var account = await _context.ConnectedEmailAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account == null || !EmailAccountStatus.CanSync(account.Status))
            return new() { ErrorCode = "reauthentication_required", ErrorMessage = "Please reconnect this email account." };
        await using var orgLock = await _locks.TryAcquireAsync("organization:" + account.OrganizationId, cancellationToken);
        if (orgLock == null) return new() { ErrorCode = "sync_in_progress", ErrorMessage = "Another email account for this business is syncing. Please try again shortly." };
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(_options.SyncTimeZone)).Date;
        if (triggerSource == EmailSyncTrigger.Scheduled)
        {
            if (!account.AutoSyncEnabled || account.LastScheduledSyncDay == today)
                return new() { Success = true };
            // Claim the day before any provider call. Retries never cause another scheduled scan.
            account.LastScheduledSyncDay = DateTime.SpecifyKind(today, DateTimeKind.Utc);
            await _context.SaveChangesAsync(cancellationToken);
        }
        var log = new EmailSyncLog { ConnectedEmailAccountId = accountId, TriggeredById = triggeredById,
            TriggerSource = triggerSource, CursorStart = account.LastSyncCursor };
        _context.EmailSyncLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
        string? errorCode = null, errorMessage = null;
        var allCompleted = true;
        try
        {
            await LogAuditAsync("email_sync.started", "EmailSyncLog", log.Id, triggeredById, new { accountId });
            if (!await RefreshTokenCoreAsync(accountId, cancellationToken))
                throw new EmailSyncException("reauthentication_required", "Please reconnect this email account to resume syncing.");
            var provider = GetProviderClient(account.Provider);
            var token = _encryption.Decrypt(account.AccessTokenEncrypted!);
            var senderPolicyHash = EmailScanCursor.PolicyHash(_options.AllowedSenders);
            var policyCursor = EmailScanCursor.ForPolicy(account.LastSyncCursor,
                DateTime.UtcNow.AddDays(-_options.InitialSyncLookbackDays), senderPolicyHash).Encode();
            if (account.LastSyncCursor != policyCursor)
            {
                // A newly approved sender may have mail before the previous checkpoint.
                // Revisit only the configured lookback; import receipts still prevent duplicates.
                account.LastSyncCursor = policyCursor;
                await _context.SaveChangesAsync(cancellationToken);
            }
            var attempted = 0;
            var inspectionBudget = _options.MaxAiCallsPerSync;
            var scanCursor = account.LastSyncCursor;
            string? quotaCode = null, quotaMessage = null;
            var morePending = false;
            for (var pageNumber = 0; pageNumber < 100; pageNumber++)
            {
                var fetch = await provider.FetchEmailsAsync(token, new() { SyncCursor = scanCursor,
                    SenderFilters = _options.AllowedSenders, MaxResults = _options.MaxEmailsPerSync,
                    AfterDate = DateTime.UtcNow.AddDays(-_options.InitialSyncLookbackDays), OnlyWithAttachments = false }, cancellationToken);
                if (!fetch.Success) throw new EmailSyncException(fetch.ErrorCode ?? "provider_failed", fetch.ErrorMessage ?? "The email provider is temporarily unavailable. Please try again.");
                log.EmailsScanned += fetch.Messages.Count;
                foreach (var email in fetch.Messages)
                {
                    if (!EmailSenderPolicy.IsAllowed(email.Sender, _options.AllowedSenders)) continue;
                    var attachments = email.Attachments.Where(_detectionService.IsPotentialNoticeAttachment).ToList();
                    if (attachments.Count == 0 && !string.IsNullOrWhiteSpace(email.BodyText ?? email.BodyHtml))
                        attachments.Add(new() { AttachmentId = "body", FileName = "email-notice.txt", MimeType = "text/plain" });
                    foreach (var attachment in attachments)
                    {
                        var itemKey = ComputeHash(System.Text.Encoding.UTF8.GetBytes(email.MessageId + "\n" + attachment.AttachmentId));
                        var receipt = await _context.EmailImportReceipts.FirstOrDefaultAsync(r => r.ConnectedEmailAccountId == accountId && r.ItemKey == itemKey, cancellationToken);
                        if (receipt != null && receipt.Outcome is "processed" or "duplicate")
                        {
                            var matches = _context.Notices.IgnoreQueryFilters().Where(n => n.OrganizationId == account.OrganizationId &&
                                (n.Id == receipt.NoticeId || (receipt.AttachmentHash != null &&
                                    (n.FileHash == receipt.AttachmentHash || n.AttachmentHash == receipt.AttachmentHash))));
                            if (await matches.AnyAsync(n => n.DeletedAt != null, cancellationToken) &&
                                !await matches.AnyAsync(n => n.DeletedAt == null, cancellationToken))
                            {
                                receipt.Outcome = "pending";
                                receipt.NoticeId = null;
                            }
                        }
                        if (receipt != null && receipt.Outcome is "processed" or "duplicate" or "ignored")
                        { if (receipt.Outcome is "processed" or "duplicate") log.DuplicatesSkipped++; continue; }
                        if (attempted >= _options.MaxAttachmentsPerSync) { allCompleted = false; break; }
                        attempted++;
                        if (receipt == null)
                        {
                            receipt = new() { OrganizationId = account.OrganizationId, ConnectedEmailAccountId = accountId, ItemKey = itemKey };
                            _context.EmailImportReceipts.Add(receipt);
                        }
                        var importStage = "attachment_download";
                        try
                        {
                            if (attachment.Size > _options.MaxAttachmentSizeBytes)
                                throw new EmailSyncException("attachment_unreadable", "An attachment exceeds the supported size. Please review it manually; pending work is retained.");
                            var download = attachment.AttachmentId == "body"
                                ? new AttachmentDownloadResult { Success = true, Data = System.Text.Encoding.UTF8.GetBytes(email.BodyText ?? email.BodyHtml ?? "") }
                                : await provider.DownloadAttachmentAsync(token, email.MessageId, attachment.AttachmentId, cancellationToken);
                            if (!download.Success || download.Data == null || download.Data.Length == 0 || download.Data.Length > _options.MaxAttachmentSizeBytes)
                                throw new EmailSyncException("attachment_unreadable", "An attachment could not be read. It will be retried on the next sync.");
                            importStage = "duplicate_check";
                            var hash = ComputeHash(download.Data);
                            receipt.AttachmentHash = hash;
                            var duplicate = await _context.Notices.IgnoreQueryFilters().AnyAsync(n => n.DeletedAt == null && n.OrganizationId == account.OrganizationId &&
                                (n.FileHash == hash || n.AttachmentHash == hash), cancellationToken);
                            if (duplicate) { receipt.Outcome = "duplicate"; log.DuplicatesSkipped++; }
                            else
                            {
                                // No AI spend while the plan cannot accept another notice.
                                importStage = "quota_check";
                                var quota = await _usageService.CanCreateNoticeAsync(account.OrganizationId);
                                if (!quota.CanCreate)
                                    throw new EmailSyncException(quota.Reason == "No active subscription" ? "subscription_required" : "monthly_limit_reached",
                                        quota.Reason == "No active subscription" ? "An active subscription is required. Renew or choose a plan, then retry sync. Pending emails are retained." :
                                        "Your monthly notice limit has been reached. Upgrade your plan or retry after the monthly reset. Pending emails are retained.");
                                var allowAi = _options.EnableAiFallback && inspectionBudget > 0;
                                if (allowAi) inspectionBudget--; // Count attempted calls even if the inspection service fails.
                                importStage = "document_inspection";
                                var inspected = await _inspector.InspectAsync(download.Data, attachment.MimeType, email,
                                    account.OrganizationId, allowAi, cancellationToken);
                                receipt.AiUsed = inspected.AiUsed;
                                if (inspected.AiUsed) log.AiDetectionUsed++;
                                if (inspected.Uncertain) throw new EmailSyncException("needs_review", "A possible notice needs review. Please upload it manually or retry sync.");
                                if (!inspected.IsNotice) { receipt.Outcome = "ignored"; log.AttachmentsSkipped++; }
                                else
                                {
                                    importStage = "gstin_matching";
                                    var gstins = await _context.OrganizationGstins.Where(g => g.OrganizationId == account.OrganizationId && g.DeletedAt == null).ToListAsync(cancellationToken);
                                    var matches = gstins.Where(g => inspected.Gstins.Contains(g.Gstin, StringComparer.OrdinalIgnoreCase)).ToList();
                                    if (matches.Count != 1 || inspected.Gstins.Count != 1)
                                    {
                                        // Only display validated identifier-shaped values, never raw document text.
                                        var extracted = inspected.Gstins.Where(g => System.Text.RegularExpressions.Regex.IsMatch(
                                            g, @"\A[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][A-Z0-9]Z[A-Z0-9]\z",
                                            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                                            TimeSpan.FromMilliseconds(100))).Take(3).ToList();
                                        var summary = extracted.Count > 0 ? string.Join(", ", extracted) : "none";
                                        var reason = inspected.Gstins.Count != 1
                                            ? $"The document contains {inspected.Gstins.Count} detected GSTINs; one is required for automatic import."
                                            : matches.Count == 0
                                                ? $"The document GSTIN has no matching registration in the mailbox's business ({gstins.Count} non-deleted registrations)."
                                                : $"The document GSTIN matches {matches.Count} registrations in the mailbox's business; remove duplicate registrations before retrying.";
                                        throw new EmailSyncException("no_gstin_match",
                                            $"{reason} Detected: {summary}. Receipt: {receipt.Id}.");
                                    }
                                    if (inspected.ReferenceNumber != null)
                                    {
                                        var gstinHash = ComputeGstinHash(matches[0].Gstin);
                                        var semanticDuplicate = await _context.Notices.AnyAsync(n => n.OrganizationId == account.OrganizationId &&
                                            n.GstinHash == gstinHash && n.NoticeNumber == inspected.ReferenceNumber, cancellationToken);
                                        if (semanticDuplicate)
                                        {
                                            receipt.Outcome = "duplicate"; receipt.Message = null; log.DuplicatesSkipped++;
                                            await _context.SaveChangesAsync(cancellationToken);
                                            await LogAuditAsync("email_notice.duplicate", "EmailImportReceipt", receipt.Id, triggeredById, new { accountId, receipt.AiUsed });
                                            continue;
                                        }
                                    }
                                    using var stream = new MemoryStream(inspected.Document);
                                    var fileName = inspected.ConvertedToPdf ? Path.GetFileNameWithoutExtension(attachment.FileName) + ".pdf" : attachment.FileName;
                                    importStage = "notice_upload";
                                    var result = await _notices.UploadEmailAsync(stream, fileName, "application/pdf", account.OrganizationId, account.UserId,
                                        matches[0].Gstin, cancellationToken: cancellationToken,
                                        emailSource: new(accountId, account.Provider, account.Email, email.MessageId, attachment.AttachmentId,
                                            email.Sender, email.Subject, email.ReceivedAt, hash, inspected.ReferenceNumber));
                                    if (result.ErrorCode == "DUPLICATE_NOTICE") { receipt.Outcome = "duplicate"; log.DuplicatesSkipped++; }
                                    else if (!result.Success) throw new EmailSyncException(result.ErrorCode ?? "import_failed", result.ErrorMessage ?? "Notice import failed. It will be retried.");
                                    else { receipt.Outcome = "processed"; receipt.NoticeId = result.NoticeId; log.NoticesImported++; log.ImportedNoticeIds ??= []; if (result.NoticeId.HasValue) log.ImportedNoticeIds.Add(result.NoticeId.Value); }
                                }
                            }
                            receipt.Message = null;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            // A failed insert must not poison later receipt/log saves on this DbContext.
                            foreach (var added in _context.ChangeTracker.Entries<Notice>().Where(e => e.State == EntityState.Added && e.Entity.Source == NoticeSource.Email).ToList())
                                added.State = EntityState.Detached;
                            allCompleted = false; log.NoticesFailed++;
                            errorCode = ex is EmailSyncException known ? known.Code : "import_failed";
                            var cause = ex.GetBaseException();
                            var httpStatus = (ex as HttpRequestException)?.StatusCode;
                            var sqlState = (cause as Npgsql.PostgresException)?.SqlState;
                            var reference = $"{importStage}/{ex.GetType().Name}" +
                                (httpStatus.HasValue ? $"/HTTP-{(int)httpStatus.Value}" : "") +
                                (sqlState != null ? $"/SQL-{sqlState}" : "");
                            errorMessage = ex is EmailSyncException ? ex.Message :
                                $"Email notice import failed during {importStage.Replace('_', ' ')}. Reference: {reference}.";
                            // Do not persist raw exception messages or provider bodies: they
                            // can contain document text, credentials, or query parameters.
                            log.ErrorDetails = System.Text.Json.JsonSerializer.Serialize(new {
                                Stage = importStage, ExceptionType = ex.GetType().Name,
                                CauseType = cause.GetType().Name, HttpStatus = httpStatus.HasValue ? (int?)httpStatus.Value : null,
                                SqlState = sqlState, ReceiptId = receipt.Id
                            });
                            _logger.LogWarning("Email notice import failed. SyncLog={SyncLogId}; Receipt={ReceiptId}; Reference={Reference}; Cause={CauseType}",
                                log.Id, receipt.Id, reference, cause.GetType().Name);
                            receipt.Outcome = "deferred"; receipt.Message = errorMessage;
                            if (errorCode is "monthly_limit_reached" or "subscription_required" or "NOTICE_LIMIT_EXCEEDED")
                            {
                                quotaCode = errorCode;
                                quotaMessage = errorMessage;
                            }
                        }
                        receipt.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(cancellationToken);
                        await LogAuditAsync("email_notice." + receipt.Outcome, "EmailImportReceipt", receipt.Id, triggeredById,
                            new { accountId, receipt.NoticeId, receipt.AiUsed });
                    }
                }
                // A failed/deferred item retains the page; receipts prevent reprocessing completed items.
                if (allCompleted && !string.IsNullOrEmpty(fetch.NextSyncCursor))
                    account.LastSyncCursor = (EmailScanCursor.Read(fetch.NextSyncCursor, DateTime.UtcNow.AddDays(-_options.InitialSyncLookbackDays))
                        with { SenderPolicyHash = senderPolicyHash }).Encode();
                morePending = fetch.HasMore;
                if (!fetch.HasMore) break;
                if (quotaCode != null || attempted >= _options.MaxAttachmentsPerSync ||
                    string.IsNullOrEmpty(fetch.NextSyncCursor) || fetch.NextSyncCursor == scanCursor)
                {
                    allCompleted = false;
                    break;
                }
                // Continue scanning after item failures without advancing the durable
                // checkpoint past pending work. Receipts deduplicate successful imports.
                scanCursor = fetch.NextSyncCursor;
            }
            if (morePending) allCompleted = false;
            if (quotaCode != null) { errorCode = quotaCode; errorMessage = quotaMessage; }
            if (errorMessage != null)
                errorMessage = $"Imported {log.NoticesImported} notice(s); {log.NoticesFailed} item(s) pending. {errorMessage}";
            account.LastSyncAt = DateTime.UtcNow;
            account.LastError = errorMessage;
            account.LastSyncResult = new() { EmailsScanned = log.EmailsScanned, NoticesImported = log.NoticesImported,
                DuplicatesSkipped = log.DuplicatesSkipped, AttachmentsSkipped = log.AttachmentsSkipped, FailedToProcess = log.NoticesFailed };
            log.Status = allCompleted ? EmailSyncStatus.Completed : EmailSyncStatus.CompletedWithWarnings;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            errorCode = ex is EmailSyncException known ? known.Code : "sync_failed";
            errorMessage = ex is EmailSyncException ? ex.Message : "Email sync could not finish. Please try again.";
            log.Status = EmailSyncStatus.Failed;
            account.LastError = errorMessage;
        }
        if (account.Status != EmailAccountStatus.ReauthenticationRequired)
            account.Status = log.Status == EmailSyncStatus.Failed ? EmailAccountStatus.SyncFailed : EmailAccountStatus.Connected;
        account.LastSyncResult = new() { Status = log.Status, ErrorCode = errorCode,
            EmailsScanned = log.EmailsScanned, NoticesImported = log.NoticesImported,
            DuplicatesSkipped = log.DuplicatesSkipped, AttachmentsSkipped = log.AttachmentsSkipped,
            FailedToProcess = log.NoticesFailed };
        if (!allCompleted && errorMessage == null)
        {
            errorMessage = "More notices are pending. They will continue on the next daily sync, or you can run a manual sync.";
            account.LastError = errorMessage;
        }
        log.ErrorMessage = errorMessage;
        log.CursorEnd = account.LastSyncCursor;
        log.CompletedAt = DateTime.UtcNow;
        log.DurationMs = (long)(log.CompletedAt.Value - log.StartedAt).TotalMilliseconds;
        await _context.SaveChangesAsync(cancellationToken);
        await LogAuditAsync(log.Status == EmailSyncStatus.Failed ? "email_sync.failed" : "email_sync.completed",
            "EmailSyncLog", log.Id, triggeredById, new { accountId, log.NoticesImported, log.DuplicatesSkipped, errorCode });
        return new() { Success = errorCode == null, SyncLogId = log.Id, EmailsScanned = log.EmailsScanned,
            NoticesImported = log.NoticesImported, DuplicatesSkipped = log.DuplicatesSkipped,
            AttachmentsSkipped = log.AttachmentsSkipped, NoticesFailed = log.NoticesFailed,
            ErrorCode = errorCode, ErrorMessage = errorMessage };
    }

    private sealed class EmailSyncException(string code, string message) : Exception(message) { public string Code { get; } = code; }

    public async Task<bool> RefreshTokenIfNeededAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var accountLock = await _locks.TryAcquireAsync("account:" + accountId, cancellationToken);
        return accountLock != null && await RefreshTokenCoreAsync(accountId, cancellationToken);
    }

    private async Task<bool> RefreshTokenCoreAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.ConnectedEmailAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account == null) return false;

        // Check if refresh is needed
        var refreshThreshold = DateTime.UtcNow.AddMinutes(_options.TokenRefreshBufferMinutes);
        if (!string.IsNullOrEmpty(account.AccessTokenEncrypted) && account.TokenExpiresAt > refreshThreshold)
        {
            return true; // Token is still valid
        }

        if (string.IsNullOrEmpty(account.RefreshTokenEncrypted))
        {
            account.Status = EmailAccountStatus.ReauthenticationRequired;
            account.LastError = "Please reconnect this email account to allow daily sync.";
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            await LogAuditAsync("email_account.reauthentication_required", "ConnectedEmailAccount", accountId, null,
                new { account.Provider, reason = "missing_refresh_token" });
            return false;
        }

        try
        {
            var refreshToken = _encryption.Decrypt(account.RefreshTokenEncrypted);
            var client = GetProviderClient(account.Provider);

            var result = await client.RefreshTokenAsync(refreshToken, cancellationToken);
            if (!result.Success || string.IsNullOrWhiteSpace(result.AccessToken) || result.ExpiresAt == null || result.ExpiresAt <= DateTime.UtcNow)
            {
                account.Status = EmailAccountStatus.ReauthenticationRequired;
                account.LastError = result.ErrorMessage ?? "Token refresh failed";
                account.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                await LogAuditAsync(
                    "email_account.reauthentication_required",
                    "ConnectedEmailAccount",
                    account.Id,
                    null,
                    new { provider = account.Provider, reason = result.ErrorMessage });

                return false;
            }

            // Update tokens
            account.AccessTokenEncrypted = _encryption.Encrypt(result.AccessToken!);
            if (result.RefreshToken != null)
            {
                account.RefreshTokenEncrypted = _encryption.Encrypt(result.RefreshToken);
            }
            account.TokenExpiresAt = result.ExpiresAt;
            account.Scopes = result.Scopes ?? account.Scopes;
            account.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            await LogAuditAsync("email_account.token_refreshed", "ConnectedEmailAccount", accountId, null, new { account.Provider });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error refreshing token for account {AccountId}: {Type}", accountId, ex.GetType().Name);
            account.Status = EmailAccountStatus.ReauthenticationRequired;
            account.LastError = "Please reconnect this email account.";
            account.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            await LogAuditAsync("email_account.reauthentication_required", "ConnectedEmailAccount", accountId, null,
                new { account.Provider, reason = "refresh_failed" });
            return false;
        }
    }

    public async Task<List<EmailSyncLog>> GetSyncHistoryAsync(
        Guid accountId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return await _context.EmailSyncLogs
            .Where(l => l.ConnectedEmailAccountId == accountId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
    }

    public async Task<ConnectedEmailAccount?> UpdateSettingsAsync(
        Guid accountId,
        bool? autoSyncEnabled,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.ConnectedEmailAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account == null) return null;

        if (autoSyncEnabled.HasValue)
        {
            account.AutoSyncEnabled = autoSyncEnabled.Value;
        }

        account.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return account;
    }

    public async Task<List<ConnectedEmailAccount>> GetAccountsNeedingRefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var refreshThreshold = DateTime.UtcNow.AddMinutes(_options.TokenRefreshBufferMinutes);

        return await _context.ConnectedEmailAccounts
            .Where(a =>
                (a.Status == EmailAccountStatus.Connected || a.Status == EmailAccountStatus.SyncFailed) &&
                (a.TokenExpiresAt == null || a.TokenExpiresAt <= refreshThreshold || a.RefreshTokenEncrypted == null))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ConnectedEmailAccount>> GetAccountsDueForSyncAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ConnectedEmailAccounts
            .Where(a =>
                (a.Status == EmailAccountStatus.Connected || a.Status == EmailAccountStatus.SyncFailed) &&
                a.AutoSyncEnabled)
            .ToListAsync(cancellationToken);
    }

    public async Task RecordSyncFailureAsync(
        Guid accountId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.ConnectedEmailAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account == null) return;

        account.ConsecutiveFailures++;
        account.LastError = errorMessage;
        account.UpdatedAt = DateTime.UtcNow;

        if (account.ConsecutiveFailures >= _options.MaxConsecutiveFailures)
        {
            account.Status = EmailAccountStatus.Suspended;
            _logger.LogWarning(
                "Suspended account {AccountId} after {Failures} consecutive failures",
                accountId, account.ConsecutiveFailures);

            await LogAuditAsync(
                "email_account.suspended",
                "ConnectedEmailAccount",
                account.Id,
                null,
                new { reason = "Consecutive sync failures", failures = account.ConsecutiveFailures });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> UnsuspendEligibleAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        // Unsuspend accounts that have been suspended for at least 24 hours
        var suspendedThreshold = DateTime.UtcNow.AddHours(-24);

        var accounts = await _context.ConnectedEmailAccounts
            .Where(a =>
                a.Status == EmailAccountStatus.Suspended &&
                a.UpdatedAt != null &&
                a.UpdatedAt <= suspendedThreshold)
            .ToListAsync(cancellationToken);

        foreach (var account in accounts)
        {
            account.Status = EmailAccountStatus.Connected;
            account.ConsecutiveFailures = 0;
            account.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return accounts.Count;
    }

    private Task LogAuditAsync(string action, string entityType, Guid entityId, Guid? userId, object details)
    {
        Guid? orgId = entityType switch
        {
            "ConnectedEmailAccount" => _context.ConnectedEmailAccounts.Local.FirstOrDefault(a => a.Id == entityId)?.OrganizationId,
            "EmailImportReceipt" => _context.EmailImportReceipts.Local.FirstOrDefault(r => r.Id == entityId)?.OrganizationId,
            "EmailSyncLog" => _context.ConnectedEmailAccounts.Local.FirstOrDefault(a => a.Id ==
                _context.EmailSyncLogs.Local.FirstOrDefault(l => l.Id == entityId)?.ConnectedEmailAccountId)?.OrganizationId,
            _ => null
        };
        return _auditService.LogAsync(new AuditLogEntry { Action = action, EntityType = entityType,
            EntityId = entityId, UserId = userId, OrganizationId = orgId,
            NewValues = details });
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeGstinHash(string gstin)
    {
        var normalized = gstin.ToUpperInvariant();
        var bytes = System.Text.Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

}
