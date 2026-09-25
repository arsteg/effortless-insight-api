using System.Data;
using System.Security.Cryptography;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Email;
using EffortlessInsight.Api.Services.GstSync;
using EffortlessInsight.Api.Services.Notices;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

public record CreateCaClientInvitationRequest(
    string Gstin,
    string Email,
    string? ClientDisplayName,
    int? AccessDurationDays,
    string? Message
);

public record CaClientInvitationDto(
    Guid Id,
    string Gstin,
    string Email,
    string? ClientDisplayName,
    string Status,
    DateTime ExpiresAt,
    int StagedNoticeCount,
    DateTime CreatedAt
);

public record CaClientInvitationDetailsDto(
    string CaOrganizationName,
    string Gstin,
    string? ClientDisplayName,
    string Email,
    string Status,
    DateTime ExpiresAt,
    string? Message
);

public record CaClientListItemDto(
    Guid Id,
    string Type, // "staged" | "active"
    string? Gstin,
    string DisplayName,
    string Status,
    int NoticeCount,
    int? OverdueCount,
    DateTime? InvitationExpiresAt,
    Guid? OrganizationId,
    Guid? ProspectClientId,
    Guid? InvitationId
);

public record UploadCaStagedNoticeResult(
    Guid Id,
    string? FileName,
    int? FileSize,
    DateTime UploadedAt
);

public record AcceptCaClientInvitationRequest(
    string OrganizationName,
    string? LegalName,
    string? Industry,
    string State,
    string? City,
    string? AnnualTurnoverRange
);

public record AcceptCaClientInvitationResult(
    Guid OrganizationId,
    string OrganizationName,
    int MergedNoticeCount,
    int NewNoticeCount,
    string AccessToken,
    int ExpiresIn
);

public record ExistingOrganizationForGstinDto(
    Guid OrganizationId,
    string OrganizationName,
    string Role
);

public record CaClientInvitationDetailsWithContextDto(
    string CaOrganizationName,
    string Gstin,
    string? ClientDisplayName,
    string Email,
    string Status,
    DateTime ExpiresAt,
    string? Message,
    ExistingOrganizationForGstinDto? ExistingOrganization
);

public record AcceptCaClientInvitationLinkRequest(Guid ExistingOrganizationId);

public record AcceptCaClientInvitationLinkResult(
    Guid OrganizationId,
    string OrganizationName,
    int MergedNoticeCount,
    int NewNoticeCount
);

public interface ICaClientService
{
    Task<CaClientInvitationDto> CreateInvitationAsync(Guid caUserId, CreateCaClientInvitationRequest request);
    Task<CaClientInvitationDetailsDto> GetInvitationByTokenAsync(string token);
    Task<List<CaClientListItemDto>> GetClientsAsync(Guid caUserId);
    Task<CaClientInvitationDto> ResendInvitationAsync(Guid caUserId, Guid invitationId);
    Task CancelInvitationAsync(Guid caUserId, Guid invitationId);
    Task<UploadCaStagedNoticeResult> UploadStagedNoticeAsync(
        Guid caUserId, Guid prospectClientId, Stream fileStream, string fileName, string contentType,
        CancellationToken cancellationToken = default);
    Task<AcceptCaClientInvitationResult> AcceptInvitationAsync(string token, Guid boUserId, AcceptCaClientInvitationRequest request);
    Task DeclineInvitationAsync(string token, Guid boUserId);
    Task<CaClientInvitationDetailsWithContextDto> GetInvitationWithContextAsync(string token, Guid boUserId);
    Task<AcceptCaClientInvitationLinkResult> AcceptInvitationLinkAsync(string token, Guid boUserId, AcceptCaClientInvitationLinkRequest request);
}

public class CaClientService : ICaClientService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGstinValidatorService _gstinValidator;
    private readonly ICaGstinAuthorizationService _caGstinAuth;
    private readonly IOrganizationManagementService _organizationService;
    private readonly ICaBoGstinLinkService _caBoGstinLinkService;
    private readonly IGstNoticeRawService _gstNoticeRawService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IUsageService _usageService;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IAuditService _auditService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CaClientService> _logger;

    private const int InvitationExpiryDays = 14;
    private const int MaxInvitationResends = 3;

    public CaClientService(
        ApplicationDbContext dbContext,
        IGstinValidatorService gstinValidator,
        ICaGstinAuthorizationService caGstinAuth,
        IOrganizationManagementService organizationService,
        ICaBoGstinLinkService caBoGstinLinkService,
        IGstNoticeRawService gstNoticeRawService,
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IFileStorageService fileStorageService,
        IUsageService usageService,
        IBackgroundJobClient backgroundJobs,
        IAuditService auditService,
        IConfiguration configuration,
        ILogger<CaClientService> logger)
    {
        _dbContext = dbContext;
        _gstinValidator = gstinValidator;
        _caGstinAuth = caGstinAuth;
        _organizationService = organizationService;
        _caBoGstinLinkService = caBoGstinLinkService;
        _gstNoticeRawService = gstNoticeRawService;
        _userManager = userManager;
        _emailService = emailService;
        _fileStorageService = fileStorageService;
        _usageService = usageService;
        _backgroundJobs = backgroundJobs;
        _auditService = auditService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<CaClientInvitationDto> CreateInvitationAsync(Guid caUserId, CreateCaClientInvitationRequest request)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new InvalidOperationException("INVALID_EMAIL: Email is required");
        }

        var caUser = await _userManager.FindByIdAsync(caUserId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        if (!caUser.IsCA)
        {
            throw new UnauthorizedAccessException("NOT_A_CA");
        }

        var caOrgId = await _dbContext.OrganizationMembers
            .Where(m => m.UserId == caUserId && m.Role == "owner" && m.Status == "active" && m.Organization.DeletedAt == null)
            .OrderBy(m => m.JoinedAt)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("CA_ORGANIZATION_NOT_FOUND: Create your own organization before inviting clients");

        var gstinResult = _gstinValidator.Validate(request.Gstin);
        if (!gstinResult.IsValid)
        {
            throw new InvalidOperationException($"INVALID_GSTIN: {gstinResult.ErrorMessage}");
        }
        var gstin = gstinResult.Gstin!;

        var authResult = await _caGstinAuth.CheckAsync(gstin, caUserId);
        if (!authResult.IsAllowed)
        {
            throw new InvalidOperationException($"{authResult.ErrorCode}: {authResult.ErrorMessage}");
        }

        var gstinHash = _caGstinAuth.ComputeGstinHash(gstin);
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Upsert the prospect client: a CA may already be staging notices for
        // this GSTIN before ever sending an invitation.
        var prospectClient = await _dbContext.CaProspectClients
            .Include(p => p.CaClientInvitation)
            .FirstOrDefaultAsync(p => p.CaUserId == caUserId && p.GstinHash == gstinHash);

        if (prospectClient != null)
        {
            if (prospectClient.Status != "staging")
            {
                throw new InvalidOperationException("PROSPECT_CLIENT_NOT_STAGING: This client has already been claimed or reassigned");
            }

            if (prospectClient.CaClientInvitation is { Status: "pending" })
            {
                throw new InvalidOperationException("INVITATION_PENDING");
            }

            if (!string.IsNullOrWhiteSpace(request.ClientDisplayName))
            {
                prospectClient.ClientDisplayName = request.ClientDisplayName;
            }
        }
        else
        {
            prospectClient = new CaProspectClient
            {
                CaUserId = caUserId,
                Gstin = gstin,
                GstinHash = gstinHash,
                ClientDisplayName = request.ClientDisplayName,
                Status = "staging"
            };
            _dbContext.CaProspectClients.Add(prospectClient);
        }

        var token = GenerateSecureToken();
        var tokenHash = HashToken(token);

        var invitation = new CaClientInvitation
        {
            CaUserId = caUserId,
            CaOrganizationId = caOrgId,
            Gstin = gstin,
            Email = request.Email.Trim(),
            EmailNormalized = normalizedEmail,
            ClientDisplayName = request.ClientDisplayName,
            TokenHash = tokenHash,
            Status = "pending",
            ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays),
            AccessDurationDays = request.AccessDurationDays,
            Message = request.Message
        };

        _dbContext.CaClientInvitations.Add(invitation);
        await _dbContext.SaveChangesAsync();

        prospectClient.CaClientInvitationId = invitation.Id;
        await _dbContext.SaveChangesAsync();

        await SendInvitationEmailAsync(invitation, caUser, token);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "ca_client_invitation.sent",
            EntityType = "CaClientInvitation",
            EntityId = invitation.Id,
            UserId = caUserId,
            OrganizationId = caOrgId,
            NewValues = new { invitation.Email, request.ClientDisplayName }
        });

        _logger.LogInformation("CA {CaUserId} invited {Email} to claim GSTIN-associated organization", caUserId, request.Email);

        var stagedCount = await _dbContext.Notices
            .CountAsync(n => n.CaProspectClientId == prospectClient.Id && n.DeletedAt == null);

        return ToDto(invitation, stagedCount);
    }

    public async Task<CaClientInvitationDetailsDto> GetInvitationByTokenAsync(string token)
    {
        var tokenHash = HashToken(token);
        var invitation = await _dbContext.CaClientInvitations
            .Include(i => i.CaOrganization)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash)
            ?? throw new KeyNotFoundException("INVALID_INVITATION");

        if (invitation.Status == "pending" && invitation.ExpiresAt < DateTime.UtcNow)
        {
            invitation.Status = "expired";
            await _dbContext.SaveChangesAsync();
        }

        return new CaClientInvitationDetailsDto(
            CaOrganizationName: invitation.CaOrganization.Name,
            Gstin: invitation.Gstin,
            ClientDisplayName: invitation.ClientDisplayName,
            Email: invitation.Email,
            Status: invitation.Status,
            ExpiresAt: invitation.ExpiresAt,
            Message: invitation.Message
        );
    }

    public async Task<List<CaClientListItemDto>> GetClientsAsync(Guid caUserId)
    {
        var activeClients = await _dbContext.OrganizationMembers
            .Where(m => m.UserId == caUserId && m.Role == "ca" && m.Status == "active" && m.Organization.DeletedAt == null)
            .Select(m => new
            {
                m.OrganizationId,
                m.Organization.Name,
                m.ClientReference,
                m.AccessExpiresAt,
                NoticeCount = _dbContext.Notices.Count(n => n.OrganizationId == m.OrganizationId),
                OverdueCount = _dbContext.Notices.Count(n => n.OrganizationId == m.OrganizationId
                    && n.ResponseDeadline != null
                    && n.ResponseDeadline < DateOnly.FromDateTime(DateTime.UtcNow)
                    && n.Status != "closed" && n.Status != "resolved")
            })
            .ToListAsync();

        var activeItems = activeClients.Select(c => new CaClientListItemDto(
            Id: c.OrganizationId,
            Type: "active",
            Gstin: null,
            DisplayName: c.ClientReference ?? c.Name,
            Status: "active",
            NoticeCount: c.NoticeCount,
            OverdueCount: c.OverdueCount,
            InvitationExpiresAt: c.AccessExpiresAt,
            OrganizationId: c.OrganizationId,
            ProspectClientId: null,
            InvitationId: null
        ));

        var stagedClients = await _dbContext.CaProspectClients
            .Include(p => p.CaClientInvitation)
            .Where(p => p.CaUserId == caUserId && p.Status == "staging" && p.DeletedAt == null)
            .ToListAsync();

        var stagedItems = new List<CaClientListItemDto>();
        foreach (var prospect in stagedClients)
        {
            var stagedCount = await _dbContext.Notices
                .CountAsync(n => n.CaProspectClientId == prospect.Id && n.DeletedAt == null);

            stagedItems.Add(new CaClientListItemDto(
                Id: prospect.Id,
                Type: "staged",
                Gstin: prospect.Gstin,
                DisplayName: prospect.ClientDisplayName ?? prospect.Gstin,
                Status: prospect.CaClientInvitation?.Status ?? "no_invitation_sent",
                NoticeCount: stagedCount,
                OverdueCount: null,
                InvitationExpiresAt: prospect.CaClientInvitation?.ExpiresAt,
                OrganizationId: null,
                ProspectClientId: prospect.Id,
                InvitationId: prospect.CaClientInvitation?.Id
            ));
        }

        return activeItems.Concat(stagedItems).ToList();
    }

    public async Task<CaClientInvitationDto> ResendInvitationAsync(Guid caUserId, Guid invitationId)
    {
        var invitation = await _dbContext.CaClientInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.CaUserId == caUserId)
            ?? throw new KeyNotFoundException("INVITATION_NOT_FOUND");

        if (invitation.Status != "pending")
        {
            throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
        }

        if (invitation.SendCount >= MaxInvitationResends)
        {
            throw new InvalidOperationException("MAX_RESENDS_EXCEEDED");
        }

        invitation.SendCount++;
        invitation.LastSentAt = DateTime.UtcNow;
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays);
        await _dbContext.SaveChangesAsync();

        var caUser = await _userManager.FindByIdAsync(caUserId.ToString());
        // Token cannot be re-derived from the stored hash, so a resend must mint
        // a fresh token/hash - the old link stops working, matching how
        // OrganizationInvitation resend also invalidates the previous link.
        var token = GenerateSecureToken();
        invitation.TokenHash = HashToken(token);
        await _dbContext.SaveChangesAsync();

        await SendInvitationEmailAsync(invitation, caUser, token);

        var prospectClient = await _dbContext.CaProspectClients
            .FirstOrDefaultAsync(p => p.CaClientInvitationId == invitation.Id);
        var stagedCount = prospectClient == null
            ? 0
            : await _dbContext.Notices.CountAsync(n => n.CaProspectClientId == prospectClient.Id && n.DeletedAt == null);

        return ToDto(invitation, stagedCount);
    }

    public async Task CancelInvitationAsync(Guid caUserId, Guid invitationId)
    {
        var invitation = await _dbContext.CaClientInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.CaUserId == caUserId)
            ?? throw new KeyNotFoundException("INVITATION_NOT_FOUND");

        if (invitation.Status != "pending")
        {
            throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
        }

        invitation.Status = "cancelled";
        invitation.RespondedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<UploadCaStagedNoticeResult> UploadStagedNoticeAsync(
        Guid caUserId, Guid prospectClientId, Stream fileStream, string fileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        var prospectClient = await _dbContext.CaProspectClients
            .FirstOrDefaultAsync(p => p.Id == prospectClientId && p.CaUserId == caUserId, cancellationToken)
            ?? throw new KeyNotFoundException("PROSPECT_CLIENT_NOT_FOUND");

        if (prospectClient.Status != "staging")
        {
            throw new InvalidOperationException("PROSPECT_CLIENT_NOT_STAGING");
        }

        // Re-check authorization at sync/upload time too, closing the race where
        // the GSTIN was claimed by another CA after this prospect client was created.
        var authResult = await _caGstinAuth.CheckAsync(prospectClient.Gstin, caUserId, cancellationToken);
        if (!authResult.IsAllowed)
        {
            throw new InvalidOperationException($"{authResult.ErrorCode}: {authResult.ErrorMessage}");
        }

        // Get CA's organization ID
        var caOrgId = await _dbContext.OrganizationMembers
            .Where(m => m.UserId == caUserId && m.Role == "owner" && m.Status == "active" && m.Organization.DeletedAt == null)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (caOrgId == default)
        {
            throw new InvalidOperationException("CA_NO_ORGANIZATION");
        }

        using var hashBuffer = new MemoryStream();
        await fileStream.CopyToAsync(hashBuffer, cancellationToken);
        hashBuffer.Position = 0;
        var fileHash = Convert.ToHexString(await SHA256.HashDataAsync(hashBuffer, cancellationToken)).ToLowerInvariant();
        hashBuffer.Position = 0;

        var fileUrl = await _fileStorageService.UploadAsync(hashBuffer, fileName, contentType);

        // Create a Notice (not CaStagedNotice) with CaProspectClientId set
        // This notice will be transferred to BO's org when they accept the invitation
        var notice = new Notice
        {
            OrganizationId = caOrgId,
            UploadedById = caUserId,
            FileName = fileName,
            FileSize = (int)hashBuffer.Length,
            FileMimeType = contentType,
            FileHash = fileHash,
            FileUrl = fileUrl,
            Status = NoticeStatus.Uploaded,
            ProcessingStatus = NoticeProcessingStatus.Queued,
            Priority = NoticePriority.Medium,
            Gstin = prospectClient.Gstin,
            GstinHash = ComputeGstinHash(prospectClient.Gstin),
            GstinId = null, // GSTIN not registered in CA's org
            CaProspectClientId = prospectClientId,
            Source = NoticeSource.Upload
        };

        _dbContext.Notices.Add(notice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Queue AI processing
        _backgroundJobs.Enqueue<INoticeProcessingJob>(job => job.ProcessAsync(notice.Id, cancellationToken));

        _logger.LogInformation(
            "CA {CaUserId} uploaded notice {NoticeId} for prospect client {ProspectClientId}",
            caUserId, notice.Id, prospectClientId);

        return new UploadCaStagedNoticeResult(notice.Id, notice.FileName, notice.FileSize, notice.CreatedAt);
    }

    public async Task<AcceptCaClientInvitationResult> AcceptInvitationAsync(string token, Guid boUserId, AcceptCaClientInvitationRequest request)
    {
        var tokenHash = HashToken(token);

        var invitation = await _dbContext.CaClientInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash)
            ?? throw new KeyNotFoundException("INVALID_INVITATION");

        if (invitation.Status != "pending")
        {
            throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
        }

        if (invitation.ExpiresAt < DateTime.UtcNow)
        {
            invitation.Status = "expired";
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException("INVITATION_EXPIRED");
        }

        var boUser = await _userManager.FindByIdAsync(boUserId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        if (!string.Equals(boUser.NormalizedEmail, invitation.EmailNormalized.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EMAIL_MISMATCH");
        }

        // Race guard: re-check that no other CA has claimed this GSTIN since the
        // invite was sent (e.g. the BO independently accepted a different CA's
        // invitation for the same GSTIN in the interim).
        var authResult = await _caGstinAuth.CheckAsync(invitation.Gstin, invitation.CaUserId);
        if (!authResult.IsAllowed)
        {
            throw new InvalidOperationException($"GSTIN_ALREADY_CLAIMED: {authResult.ErrorMessage}");
        }

        var prospectClient = await _dbContext.CaProspectClients
            .Include(p => p.StagedNotices)
            .FirstOrDefaultAsync(p => p.CaClientInvitationId == invitation.Id);

        // Diagnostic logging to trace merge flow
        var unmergedStagedNotices = prospectClient?.StagedNotices?.Count(n => !n.MergedToNotices) ?? 0;
        _logger.LogInformation(
            "AcceptInvitation: invitationId={InvitationId}, prospectClientFound={Found}, prospectClientId={ProspectClientId}, " +
            "prospectClientStatus={Status}, unmergedStagedNoticeCount={Count}",
            invitation.Id,
            prospectClient != null,
            prospectClient?.Id,
            prospectClient?.Status,
            unmergedStagedNotices);

        Guid newOrganizationId = default;
        string newOrganizationName = string.Empty;
        string accessToken = string.Empty;
        int expiresIn = 0;
        var transferredCount = 0;
        var newNoticeIds = new List<Guid>();
        Guid caOrgIdCaptured = default;

        // Execution strategy handles Npgsql's transient-failure retries; a bare
        // BeginTransactionAsync would break that retry behavior.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            // Re-validate inside the transaction, defending against a concurrent
            // accept of the same invitation (e.g. a double-submitted request).
            await _dbContext.Entry(invitation).ReloadAsync();
            if (invitation.Status != "pending")
            {
                throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
            }

            // Reuses the exact same org-creation code path as normal BO
            // registration, so the resulting organization is indistinguishable
            // from any other BO org (same subscription defaults, trial eligibility).
            // GSTIN comes from the invitation, not the request - the BO never
            // re-enters it, which prevents a mismatch with what the CA staged data for.
            var createRequest = new CreateOrganizationRequest(
                Name: request.OrganizationName,
                LegalName: request.LegalName,
                Gstin: invitation.Gstin,
                Industry: request.Industry,
                State: request.State,
                City: request.City,
                AnnualTurnoverRange: request.AnnualTurnoverRange
            );

            var orgResult = await _organizationService.CreateAsync(createRequest, boUserId);
            newOrganizationId = orgResult.Id;
            newOrganizationName = orgResult.Name;
            accessToken = orgResult.AccessToken!;
            expiresIn = orgResult.ExpiresIn!.Value;

            // Create CA membership in BO's organization
            var caMembership = new OrganizationMember
            {
                OrganizationId = newOrganizationId,
                UserId = invitation.CaUserId,
                Role = "ca",
                IsExternal = true,
                ClientReference = invitation.ClientDisplayName,
                Status = "active",
                InvitedById = invitation.CaUserId,
                JoinedAt = DateTime.UtcNow,
                AccessExpiresAt = invitation.AccessDurationDays.HasValue
                    ? DateTime.UtcNow.AddDays(invitation.AccessDurationDays.Value)
                    : null
            };
            _dbContext.OrganizationMembers.Add(caMembership);

            // Get CA's own organization (for cross-org visibility)
            var caOrgId = await _dbContext.OrganizationMembers
                .Where(m => m.UserId == invitation.CaUserId && m.Role == "owner" && m.Status == "active" && m.Organization.DeletedAt == null)
                .OrderBy(m => m.JoinedAt)
                .Select(m => m.OrganizationId)
                .FirstOrDefaultAsync();
            caOrgIdCaptured = caOrgId;

            // Create CaBoGstinLink for cross-organization notice visibility
            if (caOrgId != default)
            {
                var gstinHash = ComputeGstinHash(invitation.Gstin);
                _dbContext.CaBoGstinLinks.Add(new CaBoGstinLink
                {
                    CaOrganizationId = caOrgId,
                    BoOrganizationId = newOrganizationId,
                    GstinHash = gstinHash,
                    CaUserId = invitation.CaUserId,
                    CaMembershipId = caMembership.Id,
                    IsActive = true
                });

                // Link all additional GSTINs that the CA has for this client
                // This ensures cross-org visibility for all client GSTINs, not just the invitation one
                await _caBoGstinLinkService.LinkAllClientGstinsAsync(
                    caOrgId,
                    newOrganizationId,
                    invitation.CaUserId,
                    caMembership.Id,
                    CancellationToken.None);
            }

            invitation.Status = "accepted";
            invitation.RespondedAt = DateTime.UtcNow;
            invitation.AcceptedUserId = boUserId;
            invitation.ResultingOrganizationId = newOrganizationId;

            await _dbContext.SaveChangesAsync();

            // Transfer notices from CA's org to BO's org
            if (prospectClient != null)
            {
                var primaryGstin = await _dbContext.OrganizationGstins
                    .FirstAsync(g => g.OrganizationId == newOrganizationId && g.IsPrimary);

                // Get the notice IDs before transferring (for AI processing queue)
                var noticesToTransfer = await _dbContext.Notices
                    .Where(n => n.CaProspectClientId == prospectClient.Id && n.DeletedAt == null)
                    .Select(n => new { n.Id, n.ProcessingStatus })
                    .ToListAsync();

                // Transfer notices: update OrganizationId to BO's org, clear CaProspectClientId, set GstinId
                transferredCount = await _dbContext.Notices
                    .Where(n => n.CaProspectClientId == prospectClient.Id && n.DeletedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.OrganizationId, newOrganizationId)
                        .SetProperty(n => n.CaProspectClientId, (Guid?)null)
                        .SetProperty(n => n.GstinId, primaryGstin.Id));

                _logger.LogInformation(
                    "Transferred {Count} notices from CA to BO org {OrgId}",
                    transferredCount, newOrganizationId);

                // Queue AI processing for notices that haven't been processed yet
                foreach (var notice in noticesToTransfer)
                {
                    if (notice.ProcessingStatus == NoticeProcessingStatus.Queued)
                    {
                        newNoticeIds.Add(notice.Id);
                    }
                }

                prospectClient.Status = "merged";
                prospectClient.MergedAt = DateTime.UtcNow;
                prospectClient.MergedIntoOrganizationId = newOrganizationId;
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        // Queue AI processing for transferred notices that need it
        foreach (var noticeId in newNoticeIds)
        {
            _backgroundJobs.Enqueue<INoticeProcessingJob>(job => job.ProcessAsync(noticeId, CancellationToken.None));
        }

        // Auto-import existing GstNoticeRaw records for this GSTIN from the CA's organization
        var autoImportedCount = 0;
        if (caOrgIdCaptured != default)
        {
            try
            {
                var autoImportResult = await _gstNoticeRawService.AutoImportForGstinAsync(
                    caOrgIdCaptured,
                    newOrganizationId,
                    invitation.Gstin,
                    boUserId,
                    CancellationToken.None);

                autoImportedCount = autoImportResult.Imported;
                _logger.LogInformation(
                    "Auto-imported {Count} GST notices on acceptance for org {OrgId} (duplicates: {Duplicates})",
                    autoImportResult.Imported, newOrganizationId, autoImportResult.SkippedAsDuplicate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-import failed for org {OrgId}, GSTIN {Gstin}",
                    newOrganizationId, invitation.Gstin);
            }
        }

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "ca_client_invitation.accepted",
            EntityType = "Organization",
            EntityId = newOrganizationId,
            UserId = boUserId,
            OrganizationId = newOrganizationId,
            NewValues = new
            {
                CaUserId = invitation.CaUserId,
                InvitationId = invitation.Id,
                TransferredNoticeCount = transferredCount
            }
        });

        _logger.LogInformation(
            "BO {BoUserId} accepted CA client invitation {InvitationId}, created organization {OrganizationId} ({TransferredCount} notices transferred)",
            boUserId, invitation.Id, newOrganizationId, transferredCount);

        return new AcceptCaClientInvitationResult(
            OrganizationId: newOrganizationId,
            OrganizationName: newOrganizationName,
            MergedNoticeCount: transferredCount,
            NewNoticeCount: 0,
            AccessToken: accessToken,
            ExpiresIn: expiresIn
        );
    }

    public async Task DeclineInvitationAsync(string token, Guid boUserId)
    {
        var tokenHash = HashToken(token);

        var invitation = await _dbContext.CaClientInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash)
            ?? throw new KeyNotFoundException("INVALID_INVITATION");

        if (invitation.Status != "pending")
        {
            throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
        }

        var boUser = await _userManager.FindByIdAsync(boUserId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        if (!string.Equals(boUser.NormalizedEmail, invitation.EmailNormalized.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EMAIL_MISMATCH");
        }

        invitation.Status = "declined";
        invitation.RespondedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("BO {BoUserId} declined CA client invitation {InvitationId}", boUserId, invitation.Id);

        // Deliberately does NOT touch the CaProspectClient/CaStagedNotice rows -
        // the CA may still hold staged data and re-invite the same or a
        // different email later.
    }

    public async Task<CaClientInvitationDetailsWithContextDto> GetInvitationWithContextAsync(string token, Guid boUserId)
    {
        var tokenHash = HashToken(token);
        var invitation = await _dbContext.CaClientInvitations
            .Include(i => i.CaOrganization)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash)
            ?? throw new KeyNotFoundException("INVALID_INVITATION");

        if (invitation.Status == "pending" && invitation.ExpiresAt < DateTime.UtcNow)
        {
            invitation.Status = "expired";
            await _dbContext.SaveChangesAsync();
        }

        // Check if the authenticated user already has an organization with this GSTIN
        var existingOrg = await FindUserOrganizationByGstinAsync(boUserId, invitation.Gstin);

        return new CaClientInvitationDetailsWithContextDto(
            CaOrganizationName: invitation.CaOrganization.Name,
            Gstin: invitation.Gstin,
            ClientDisplayName: invitation.ClientDisplayName,
            Email: invitation.Email,
            Status: invitation.Status,
            ExpiresAt: invitation.ExpiresAt,
            Message: invitation.Message,
            ExistingOrganization: existingOrg
        );
    }

    public async Task<AcceptCaClientInvitationLinkResult> AcceptInvitationLinkAsync(
        string token, Guid boUserId, AcceptCaClientInvitationLinkRequest request)
    {
        var tokenHash = HashToken(token);

        var invitation = await _dbContext.CaClientInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash)
            ?? throw new KeyNotFoundException("INVALID_INVITATION");

        if (invitation.Status != "pending")
        {
            throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
        }

        if (invitation.ExpiresAt < DateTime.UtcNow)
        {
            invitation.Status = "expired";
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException("INVITATION_EXPIRED");
        }

        var boUser = await _userManager.FindByIdAsync(boUserId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        if (!string.Equals(boUser.NormalizedEmail, invitation.EmailNormalized.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EMAIL_MISMATCH");
        }

        // Verify the user owns/admins the specified organization
        var membership = await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .ThenInclude(o => o.OrganizationGstins)
            .FirstOrDefaultAsync(m =>
                m.UserId == boUserId &&
                m.OrganizationId == request.ExistingOrganizationId &&
                (m.Role == "owner" || m.Role == "admin") &&
                m.Status == "active" &&
                m.Organization.DeletedAt == null)
            ?? throw new InvalidOperationException("ORGANIZATION_NOT_FOUND_OR_NOT_AUTHORIZED");

        // Verify the organization has the matching GSTIN
        var hasMatchingGstin = membership.Organization.OrganizationGstins
            .Any(g => g.Gstin == invitation.Gstin && g.DeletedAt == null);

        if (!hasMatchingGstin)
        {
            throw new InvalidOperationException("GSTIN_MISMATCH");
        }

        // Check if the CA is already a member
        var existingCaMembership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == request.ExistingOrganizationId &&
                m.UserId == invitation.CaUserId &&
                m.Status == "active");

        if (existingCaMembership != null)
        {
            throw new InvalidOperationException("CA_ALREADY_MEMBER");
        }

        var prospectClient = await _dbContext.CaProspectClients
            .Include(p => p.StagedNotices)
            .FirstOrDefaultAsync(p => p.CaClientInvitationId == invitation.Id);

        // Diagnostic logging to trace merge flow
        var unmergedStagedNotices = prospectClient?.StagedNotices?.Count(n => !n.MergedToNotices) ?? 0;
        _logger.LogInformation(
            "AcceptInvitationLink: invitationId={InvitationId}, prospectClientFound={Found}, prospectClientId={ProspectClientId}, " +
            "prospectClientStatus={Status}, unmergedStagedNoticeCount={Count}, targetOrgId={TargetOrgId}",
            invitation.Id,
            prospectClient != null,
            prospectClient?.Id,
            prospectClient?.Status,
            unmergedStagedNotices,
            request.ExistingOrganizationId);

        var transferredCount = 0;
        var newNoticeIds = new List<Guid>();
        var organizationId = request.ExistingOrganizationId;
        var organizationName = membership.Organization.Name;
        Guid caOrgIdCaptured = default;

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            // Re-validate inside the transaction
            await _dbContext.Entry(invitation).ReloadAsync();
            if (invitation.Status != "pending")
            {
                throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
            }

            // Add CA as member with role="ca"
            var caMembership = new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = invitation.CaUserId,
                Role = "ca",
                IsExternal = true,
                ClientReference = invitation.ClientDisplayName,
                Status = "active",
                InvitedById = boUserId,
                JoinedAt = DateTime.UtcNow,
                AccessExpiresAt = invitation.AccessDurationDays.HasValue
                    ? DateTime.UtcNow.AddDays(invitation.AccessDurationDays.Value)
                    : null
            };
            _dbContext.OrganizationMembers.Add(caMembership);

            // Get CA's own organization (for cross-org visibility)
            var caOrgId = await _dbContext.OrganizationMembers
                .Where(m => m.UserId == invitation.CaUserId && m.Role == "owner" && m.Status == "active" && m.Organization.DeletedAt == null)
                .OrderBy(m => m.JoinedAt)
                .Select(m => m.OrganizationId)
                .FirstOrDefaultAsync();
            caOrgIdCaptured = caOrgId;

            // Create CaBoGstinLink for cross-organization notice visibility
            if (caOrgId != default)
            {
                var gstinHash = ComputeGstinHash(invitation.Gstin);
                _dbContext.CaBoGstinLinks.Add(new CaBoGstinLink
                {
                    CaOrganizationId = caOrgId,
                    BoOrganizationId = organizationId,
                    GstinHash = gstinHash,
                    CaUserId = invitation.CaUserId,
                    CaMembershipId = caMembership.Id,
                    IsActive = true
                });

                // Link all additional GSTINs that the CA has for this client
                // This ensures cross-org visibility for all client GSTINs, not just the invitation one
                await _caBoGstinLinkService.LinkAllClientGstinsAsync(
                    caOrgId,
                    organizationId,
                    invitation.CaUserId,
                    caMembership.Id,
                    CancellationToken.None);
            }

            invitation.Status = "accepted";
            invitation.RespondedAt = DateTime.UtcNow;
            invitation.AcceptedUserId = boUserId;
            invitation.ResultingOrganizationId = organizationId;

            await _dbContext.SaveChangesAsync();

            // Transfer notices from CA's org to BO's org
            if (prospectClient != null)
            {
                var primaryGstin = await _dbContext.OrganizationGstins
                    .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.IsPrimary);

                if (primaryGstin != null)
                {
                    // Get the notice IDs before transferring (for AI processing queue)
                    var noticesToTransfer = await _dbContext.Notices
                        .Where(n => n.CaProspectClientId == prospectClient.Id && n.DeletedAt == null)
                        .Select(n => new { n.Id, n.ProcessingStatus })
                        .ToListAsync();

                    // Transfer notices: update OrganizationId to BO's org, clear CaProspectClientId, set GstinId
                    transferredCount = await _dbContext.Notices
                        .Where(n => n.CaProspectClientId == prospectClient.Id && n.DeletedAt == null)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(n => n.OrganizationId, organizationId)
                            .SetProperty(n => n.CaProspectClientId, (Guid?)null)
                            .SetProperty(n => n.GstinId, primaryGstin.Id));

                    _logger.LogInformation(
                        "Transferred {Count} notices from CA to BO org {OrgId}",
                        transferredCount, organizationId);

                    // Queue AI processing for notices that haven't been processed yet
                    foreach (var notice in noticesToTransfer)
                    {
                        if (notice.ProcessingStatus == NoticeProcessingStatus.Queued)
                        {
                            newNoticeIds.Add(notice.Id);
                        }
                    }
                }

                prospectClient.Status = "merged";
                prospectClient.MergedAt = DateTime.UtcNow;
                prospectClient.MergedIntoOrganizationId = organizationId;
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        // Queue AI processing for transferred notices that need it
        foreach (var noticeId in newNoticeIds)
        {
            _backgroundJobs.Enqueue<INoticeProcessingJob>(job => job.ProcessAsync(noticeId, CancellationToken.None));
        }

        // Auto-import existing GstNoticeRaw records for this GSTIN from the CA's organization
        var autoImportedCount = 0;
        if (caOrgIdCaptured != default)
        {
            try
            {
                var autoImportResult = await _gstNoticeRawService.AutoImportForGstinAsync(
                    caOrgIdCaptured,
                    organizationId,
                    invitation.Gstin,
                    boUserId,
                    CancellationToken.None);

                autoImportedCount = autoImportResult.Imported;
                _logger.LogInformation(
                    "Auto-imported {Count} GST notices on link for org {OrgId} (duplicates: {Duplicates})",
                    autoImportResult.Imported, organizationId, autoImportResult.SkippedAsDuplicate);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-import failed for org {OrgId}, GSTIN {Gstin}",
                    organizationId, invitation.Gstin);
            }
        }

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "ca_client_invitation.linked",
            EntityType = "Organization",
            EntityId = organizationId,
            UserId = boUserId,
            OrganizationId = organizationId,
            NewValues = new
            {
                CaUserId = invitation.CaUserId,
                InvitationId = invitation.Id,
                TransferredNoticeCount = transferredCount
            }
        });

        _logger.LogInformation(
            "BO {BoUserId} linked CA {CaUserId} to existing organization {OrganizationId} ({TransferredCount} notices transferred)",
            boUserId, invitation.CaUserId, organizationId, transferredCount);

        return new AcceptCaClientInvitationLinkResult(
            OrganizationId: organizationId,
            OrganizationName: organizationName,
            MergedNoticeCount: transferredCount,
            NewNoticeCount: 0
        );
    }

    private async Task<ExistingOrganizationForGstinDto?> FindUserOrganizationByGstinAsync(Guid userId, string gstin)
    {
        // GSTIN is AES-GCM encrypted with a random nonce, so a SQL WHERE on the
        // column can never match (the query constant encrypts to a different
        // ciphertext every time). Materialize the rows and compare in memory.
        gstin = gstin.Trim().ToUpperInvariant();

        var memberships = await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .ThenInclude(o => o.OrganizationGstins)
            .Where(m =>
                m.UserId == userId &&
                (m.Role == "owner" || m.Role == "admin") &&
                m.Status == "active" &&
                m.Organization.DeletedAt == null)
            .Select(m => new {
                m.OrganizationId,
                m.Organization.Name,
                m.Role,
                Gstins = m.Organization.OrganizationGstins
                    .Where(g => g.DeletedAt == null)
                    .Select(g => g.Gstin)
                    .ToList()
            })
            .ToListAsync();

        // Compare in memory after decryption
        var match = memberships.FirstOrDefault(m =>
            m.Gstins.Any(g => string.Equals(g, gstin, StringComparison.OrdinalIgnoreCase)));

        if (match == null)
        {
            return null;
        }

        return new ExistingOrganizationForGstinDto(
            OrganizationId: match.OrganizationId,
            OrganizationName: match.Name,
            Role: match.Role
        );
    }

    private async Task<Notice?> FindMatchingNoticeAsync(Guid organizationId, CaStagedNotice staged)
    {
        if (!string.IsNullOrWhiteSpace(staged.SourceReferenceNumber))
        {
            var byReference = await _dbContext.Notices
                .FirstOrDefaultAsync(n => n.OrganizationId == organizationId && n.NoticeNumber == staged.SourceReferenceNumber);
            if (byReference != null)
            {
                return byReference;
            }
        }

        if (!string.IsNullOrWhiteSpace(staged.FileHash))
        {
            var byHash = await _dbContext.Notices
                .FirstOrDefaultAsync(n => n.OrganizationId == organizationId && n.FileHash == staged.FileHash);
            if (byHash != null)
            {
                return byHash;
            }
        }

        return null;
    }

    private async Task SendInvitationEmailAsync(CaClientInvitation invitation, ApplicationUser? caUser, string token)
    {
        try
        {
            await _emailService.SendTemplateAsync(invitation.Email, "ca_client_invitation", new Dictionary<string, object>
            {
                ["ca_name"] = caUser?.Name ?? "A Chartered Accountant",
                ["client_display_name"] = invitation.ClientDisplayName ?? "",
                ["gstin"] = invitation.Gstin,
                ["invitation_url"] = $"{_configuration["App:BaseUrl"]?.TrimEnd('/')}/ca-invitations/{token}",
                ["expires_in_days"] = InvitationExpiryDays,
                ["message"] = invitation.Message ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send CA client invitation email to {Email}", invitation.Email);
        }
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeGstinHash(string gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin))
            return string.Empty;

        var normalized = gstin.Trim().ToUpperInvariant();
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static CaClientInvitationDto ToDto(CaClientInvitation invitation, int stagedNoticeCount) => new(
        Id: invitation.Id,
        Gstin: invitation.Gstin,
        Email: invitation.Email,
        ClientDisplayName: invitation.ClientDisplayName,
        Status: invitation.Status,
        ExpiresAt: invitation.ExpiresAt,
        StagedNoticeCount: stagedNoticeCount,
        CreatedAt: invitation.CreatedAt
    );
}
