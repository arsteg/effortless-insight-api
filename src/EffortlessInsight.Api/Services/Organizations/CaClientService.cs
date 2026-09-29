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

public record CreateCaProspectRequest(string Gstin, string? ClientDisplayName);

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
    Task<Guid> CreateProspectAsync(Guid caUserId, CreateCaProspectRequest request);
    Task<CaClientInvitationDto> CreateInvitationAsync(Guid caUserId, CreateCaClientInvitationRequest request);
    Task<CaClientInvitationDetailsDto> GetInvitationByTokenAsync(string token);
    Task<List<CaClientListItemDto>> GetClientsAsync(Guid caUserId);
    Task<CaClientInvitationDto> ResendInvitationAsync(Guid caUserId, Guid invitationId);
    Task CancelInvitationAsync(Guid caUserId, Guid invitationId);
    Task<UploadCaStagedNoticeResult> UploadStagedNoticeAsync(
        Guid caUserId, Guid prospectClientId, Stream fileStream, string fileName, string contentType,
        CancellationToken cancellationToken = default);
    Task<ExistingOrganizationForGstinDto> PrepareOrganizationAsync(string token, Guid boUserId, AcceptCaClientInvitationRequest request);
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

    public async Task<Guid> CreateProspectAsync(Guid caUserId, CreateCaProspectRequest request)
    {
        var user = await _userManager.FindByIdAsync(caUserId.ToString());
        if (user?.IsCA != true) throw new UnauthorizedAccessException("NOT_A_CA");
        var orgId = await _dbContext.OrganizationMembers.Where(m => m.UserId == caUserId
                && m.Role == "owner" && m.Status == "active" && m.DeletedAt == null && m.Organization.DeletedAt == null)
            .OrderBy(m => m.JoinedAt).Select(m => (Guid?)m.OrganizationId).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("CA_ORGANIZATION_NOT_FOUND");
        var validation = _gstinValidator.Validate(request.Gstin);
        if (!validation.IsValid) throw new InvalidOperationException($"INVALID_GSTIN: {validation.ErrorMessage}");
        if (request.ClientDisplayName?.Length > 255) throw new InvalidOperationException("INVALID_CLIENT_NAME");
        var gstin = validation.Gstin!;
        var hash = ComputeGstinHash(gstin);
        var attempt = 0;
        return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0) _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            await CaWorkspaceWrites.LockAsync(_dbContext, orgId, gstin);
            var prospect = await _dbContext.CaProspectClients.FirstOrDefaultAsync(p => p.CaUserId == caUserId && p.GstinHash == hash);
            if (prospect != null && (prospect.Status != "staging" || prospect.DeletedAt != null))
                throw new InvalidOperationException("PROSPECT_CLIENT_NOT_STAGING");
            if (prospect == null)
            {
                prospect = new CaProspectClient { CaUserId = caUserId, Gstin = gstin, GstinHash = hash };
                _dbContext.CaProspectClients.Add(prospect);
            }
            if (!string.IsNullOrWhiteSpace(request.ClientDisplayName)) prospect.ClientDisplayName = request.ClientDisplayName.Trim();
            var gstins = await _dbContext.OrganizationGstins.Where(g => g.OrganizationId == orgId && g.DeletedAt == null).ToListAsync();
            if (!gstins.Any(g => g.Gstin == gstin))
                _dbContext.OrganizationGstins.Add(new OrganizationGstin {
                    OrganizationId = orgId, Gstin = gstin, StateCode = gstin[..2],
                    StateName = await _gstinValidator.GetStateNameAsync(gstin[..2]) ?? "Unknown",
                    IsPrimary = gstins.Count == 0, Source = OrganizationGstinSource.Manual, Status = "active"
                });
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return prospect.Id;
        });
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

        await CreateProspectAsync(caUserId, new(gstin, request.ClientDisplayName));

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
            .Where(m => m.UserId == caUserId && m.Role == "ca" && m.Status == "active" && m.DeletedAt == null && m.Organization.DeletedAt == null
                && (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow))
            .Select(m => new
            {
                m.OrganizationId,
                m.Organization.Name,
                m.ClientReference,
                m.AccessExpiresAt,
                NoticeCount = _dbContext.Notices.IgnoreQueryFilters().Count(n => n.OrganizationId == m.OrganizationId && n.DeletedAt == null),
                OverdueCount = _dbContext.Notices.IgnoreQueryFilters().Count(n => n.OrganizationId == m.OrganizationId && n.DeletedAt == null
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

    public async Task<ExistingOrganizationForGstinDto> PrepareOrganizationAsync(
        string token, Guid boUserId, AcceptCaClientInvitationRequest request)
    {
        var invitation = await ValidateRecipientAsync(token, boUserId);
        var existing = await FindUserOrganizationByGstinAsync(boUserId, invitation.Gstin);
        if (existing != null) return existing;
        if (invitation.Status != "pending") throw new InvalidOperationException("INVITATION_ACCEPTED");

        var attempt = 0;
        return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0) _dbContext.ChangeTracker.Clear();
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            await CaWorkspaceWrites.LockAsync(_dbContext, invitation.CaOrganizationId, invitation.Gstin);
            var invitationId = invitation.Id;
            invitation = await _dbContext.CaClientInvitations.SingleAsync(i => i.Id == invitationId);
            await _dbContext.Entry(invitation).ReloadAsync();
            if (invitation.Status != "pending") throw new InvalidOperationException("INVITATION_ACCEPTED");
            var found = await FindUserOrganizationByGstinAsync(boUserId, invitation.Gstin);
            if (found != null) return found;
            var org = await _organizationService.CreateAsync(new CreateOrganizationRequest(
                request.OrganizationName, request.LegalName, invitation.Gstin, request.Industry,
                request.State, request.City, request.AnnualTurnoverRange), boUserId);
            // Preparation does not grant access or move any work. The BO activates
            // this organization's subscription before explicitly accepting.
            invitation.ResultingOrganizationId = org.Id;
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return new ExistingOrganizationForGstinDto(org.Id, org.Name, "owner");
        });
    }

    public async Task<AcceptCaClientInvitationResult> AcceptInvitationAsync(
        string token, Guid boUserId, AcceptCaClientInvitationRequest request)
    {
        var invitation = await ValidateRecipientAsync(token, boUserId);
        var existing = await FindUserOrganizationByGstinAsync(boUserId, invitation.Gstin)
            ?? throw new InvalidOperationException("ORGANIZATION_SETUP_REQUIRED");
        var result = await AcceptInvitationLinkAsync(token, boUserId, new(existing.OrganizationId));
        // The client switches context explicitly using the existing auth endpoint.
        return new(result.OrganizationId, result.OrganizationName, result.MergedNoticeCount,
            result.NewNoticeCount, string.Empty, 0);
    }

    private async Task<CaClientInvitation> ValidateRecipientAsync(string token, Guid boUserId)
    {
        var hash = HashToken(token);
        var invitation = await _dbContext.CaClientInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash && i.DeletedAt == null)
            ?? throw new KeyNotFoundException("INVALID_INVITATION");
        var user = await _userManager.FindByIdAsync(boUserId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");
        if (!string.Equals(user.NormalizedEmail, invitation.EmailNormalized, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EMAIL_MISMATCH");
        if (invitation.Status == "accepted" && invitation.AcceptedUserId == boUserId) return invitation;
        if (invitation.Status != "pending") throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
        if (invitation.ExpiresAt <= DateTime.UtcNow)
        {
            invitation.Status = "expired";
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException("INVITATION_EXPIRED");
        }
        return invitation;
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
        await ValidateRecipientAsync(token, boUserId);
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
        var invitation = await ValidateRecipientAsync(token, boUserId);
        CaHandoverResult? handover = null;
        var attempt = 0;
        var result = await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempt++ > 0) _dbContext.ChangeTracker.Clear();
            handover = null;
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            // Same locks as every notice/sync insert; acquisition order avoids deadlocks.
            foreach (var orgId in new[] { invitation.CaOrganizationId, request.ExistingOrganizationId }.Distinct().Order())
                await CaWorkspaceWrites.LockAsync(_dbContext, orgId, invitation.Gstin);
            var invitationId = invitation.Id;
            invitation = await _dbContext.CaClientInvitations.SingleAsync(i => i.Id == invitationId);
            await _dbContext.Entry(invitation).ReloadAsync();
            var membership = await _dbContext.OrganizationMembers.Include(m => m.Organization)
                .FirstOrDefaultAsync(m => m.OrganizationId == request.ExistingOrganizationId
                    && m.UserId == boUserId && (m.Role == "owner" || m.Role == "admin")
                    && m.Status == "active" && m.DeletedAt == null
                    && (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow));
            if (membership == null) throw new InvalidOperationException("ORGANIZATION_NOT_FOUND_OR_NOT_AUTHORIZED");
            var orgIdTarget = membership.OrganizationId;
            if (invitation.Status == "accepted")
            {
                if (invitation.AcceptedUserId != boUserId || invitation.ResultingOrganizationId != orgIdTarget)
                    throw new InvalidOperationException("INVITATION_ACCEPTED");
                var receipt = await _dbContext.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == invitation.Id
                    && a.OrganizationId == orgIdTarget && a.Action == "ca_client_invitation.handover");
                if (receipt != null)
                    return new AcceptCaClientInvitationLinkResult(orgIdTarget, membership.Organization.Name,
                        ReadReceiptCount(receipt, "Transferred"), ReadReceiptCount(receipt, "Created"));
                // A pre-fix acceptance can be repaired through the same recipient-
                // authorized endpoint, without reinstating a revoked CA membership.

            }
            var repairing = invitation.Status == "accepted";
            if (!repairing && (invitation.Status != "pending" || invitation.ExpiresAt <= DateTime.UtcNow))
                throw new InvalidOperationException("INVITATION_EXPIRED");
            if (orgIdTarget == invitation.CaOrganizationId)
                throw new InvalidOperationException("INVALID_HANDOVER_DESTINATION");

            var now = DateTime.UtcNow;
            var hasPlan = await _dbContext.BillingSubscriptions.AnyAsync(s => s.OrganizationId == orgIdTarget
                && s.DeletedAt == null && ((s.Status == "active" && s.CurrentPeriodEnd > now)
                    || (s.Status == "trialing" && s.TrialEnd > now)));
            if (!hasPlan || membership.Organization.SubscriptionStatus is "paused" or "past_due" or "cancelled" or "expired")
                throw new InvalidOperationException("SUBSCRIPTION_REQUIRED");
            var gstins = await _dbContext.OrganizationGstins.Where(g => g.OrganizationId == orgIdTarget
                && g.DeletedAt == null).ToListAsync();
            var destination = gstins.SingleOrDefault(g => string.Equals(g.Gstin, invitation.Gstin, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("GSTIN_MISMATCH");
            var sourceOwner = await _dbContext.OrganizationMembers.AnyAsync(m => m.OrganizationId == invitation.CaOrganizationId
                && m.UserId == invitation.CaUserId && m.Role == "owner" && m.Status == "active" && m.DeletedAt == null);
            if (!sourceOwner) throw new InvalidOperationException("CA_ORGANIZATION_NOT_FOUND");
            var prospect = await _dbContext.CaProspectClients.FirstOrDefaultAsync(p => p.CaClientInvitationId == invitation.Id
                && p.CaUserId == invitation.CaUserId && p.DeletedAt == null)
                ?? throw new InvalidOperationException("PROSPECT_CLIENT_NOT_FOUND");
            await _dbContext.Entry(prospect).ReloadAsync();
            if (prospect.Status != "staging" && !(repairing && prospect.Status == "merged"
                && prospect.MergedIntoOrganizationId == orgIdTarget))
                throw new InvalidOperationException("PROSPECT_CLIENT_NOT_STAGING");

            if (!repairing)
            {
                var caMember = await _dbContext.OrganizationMembers.FirstOrDefaultAsync(m =>
                    m.OrganizationId == orgIdTarget && m.UserId == invitation.CaUserId);
                if (caMember == null)
                {
                    caMember = new OrganizationMember { OrganizationId = orgIdTarget, UserId = invitation.CaUserId };
                    _dbContext.OrganizationMembers.Add(caMember);
                }
                else if (caMember.Role != "ca") throw new InvalidOperationException("CA_MEMBERSHIP_ROLE_CONFLICT");
                caMember.Role = "ca";
                caMember.IsExternal = true;
                caMember.Status = "active";
                caMember.DeletedAt = null;
                caMember.InvitedById = boUserId;
                caMember.JoinedAt = now;
                caMember.ClientReference = invitation.ClientDisplayName;
                caMember.AccessExpiresAt = invitation.AccessDurationDays.HasValue ? now.AddDays(invitation.AccessDurationDays.Value) : null;

            }

            handover = await new CaDistributorHandover(_dbContext).TransferAsync(invitation, prospect, destination);
            invitation.Status = "accepted";
            invitation.AcceptedUserId = boUserId;
            invitation.RespondedAt = now;
            invitation.ResultingOrganizationId = orgIdTarget;
            await _dbContext.SaveChangesAsync();
            _dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "ca_client_invitation.handover", EntityType = "CaClientInvitation", EntityId = invitation.Id,
                UserId = boUserId, OrganizationId = orgIdTarget,
                NewValues = new Dictionary<string, object> {
                    ["SourceOrganizationId"] = invitation.CaOrganizationId, ["ProspectClientId"] = prospect.Id,
                    ["DestinationGstinId"] = destination.Id, ["Transferred"] = handover.Transferred,
                    ["Created"] = handover.Created, ["Reconciled"] = handover.Reconciled, ["Repair"] = repairing }
            });
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return new AcceptCaClientInvitationLinkResult(orgIdTarget, membership.Organization.Name,
                handover.Transferred, handover.Created);
        });
        if (handover != null)
        {
            foreach (var id in handover.ProcessingIds)
            {
                try { _backgroundJobs.Enqueue<INoticeProcessingJob>(j => j.ProcessAsync(id, CancellationToken.None)); }
                catch (Exception ex) { _logger.LogError(ex, "Notice {NoticeId} remains queued after handover", id); }
            }
        }
        return result;
    }

    private static int ReadReceiptCount(AuditLog receipt, string key)
    {
        if (receipt.NewValues == null || !receipt.NewValues.TryGetValue(key, out var value)) return 0;
        return value is System.Text.Json.JsonElement element ? element.GetInt32() : Convert.ToInt32(value);
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
