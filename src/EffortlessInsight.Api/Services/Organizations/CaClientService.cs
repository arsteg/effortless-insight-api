using System.Data;
using System.Security.Cryptography;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Email;
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
}

public class CaClientService : ICaClientService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGstinValidatorService _gstinValidator;
    private readonly ICaGstinAuthorizationService _caGstinAuth;
    private readonly IOrganizationManagementService _organizationService;
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

        var stagedCount = await _dbContext.CaStagedNotices
            .CountAsync(n => n.CaProspectClientId == prospectClient.Id && !n.MergedToNotices);

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
            var stagedCount = await _dbContext.CaStagedNotices
                .CountAsync(n => n.CaProspectClientId == prospect.Id && !n.MergedToNotices);

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
            : await _dbContext.CaStagedNotices.CountAsync(n => n.CaProspectClientId == prospectClient.Id && !n.MergedToNotices);

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

        using var hashBuffer = new MemoryStream();
        await fileStream.CopyToAsync(hashBuffer, cancellationToken);
        hashBuffer.Position = 0;
        var fileHash = Convert.ToHexString(await SHA256.HashDataAsync(hashBuffer, cancellationToken)).ToLowerInvariant();
        hashBuffer.Position = 0;

        var fileUrl = await _fileStorageService.UploadAsync(hashBuffer, fileName, contentType);

        var stagedNotice = new CaStagedNotice
        {
            CaProspectClientId = prospectClientId,
            Gstin = prospectClient.Gstin,
            FileUrl = fileUrl,
            FileName = fileName,
            FileSize = (int)hashBuffer.Length,
            FileMimeType = contentType,
            FileHash = fileHash,
            UploadedByUserId = caUserId,
            UploadedAt = DateTime.UtcNow,
            Source = "manual_upload"
        };

        _dbContext.CaStagedNotices.Add(stagedNotice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "CA {CaUserId} staged notice upload {FileName} for prospect client {ProspectClientId}",
            caUserId, fileName, prospectClientId);

        return new UploadCaStagedNoticeResult(stagedNotice.Id, stagedNotice.FileName, stagedNotice.FileSize, stagedNotice.UploadedAt);
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

        Guid newOrganizationId = default;
        string newOrganizationName = string.Empty;
        string accessToken = string.Empty;
        int expiresIn = 0;
        var mergedCount = 0;
        var newNoticeIds = new List<Guid>();

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

            _dbContext.OrganizationMembers.Add(new OrganizationMember
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
            });

            invitation.Status = "accepted";
            invitation.RespondedAt = DateTime.UtcNow;
            invitation.AcceptedUserId = boUserId;
            invitation.ResultingOrganizationId = newOrganizationId;

            await _dbContext.SaveChangesAsync();

            if (prospectClient != null)
            {
                var primaryGstin = await _dbContext.OrganizationGstins
                    .FirstAsync(g => g.OrganizationId == newOrganizationId && g.IsPrimary);

                foreach (var staged in prospectClient.StagedNotices.Where(n => !n.MergedToNotices))
                {
                    var existingNotice = await FindMatchingNoticeAsync(newOrganizationId, staged);
                    if (existingNotice != null)
                    {
                        // Idempotent no-op merge - same semantics as
                        // GstNoticeRaw.ImportedToNotices, guards against retries
                        // ever creating a duplicate.
                        staged.MergedToNotices = true;
                        staged.MergedNoticeId = existingNotice.Id;
                        staged.MergedAt = DateTime.UtcNow;
                        mergedCount++;
                        continue;
                    }

                    var notice = new Notice
                    {
                        OrganizationId = newOrganizationId,
                        UploadedById = staged.UploadedByUserId,
                        NoticeNumber = staged.NoticeNumber,
                        NoticeType = staged.NoticeType,
                        NoticeCategory = staged.NoticeCategory,
                        Summary = staged.Summary,
                        Gstin = staged.Gstin,
                        GstinId = primaryGstin.Id,
                        IssueDate = staged.IssueDate,
                        ResponseDeadline = staged.ResponseDeadline,
                        TaxAmount = staged.TaxAmount,
                        PenaltyAmount = staged.PenaltyAmount,
                        InterestAmount = staged.InterestAmount,
                        PeriodFrom = staged.PeriodFrom,
                        PeriodTo = staged.PeriodTo,
                        FinancialYear = staged.FinancialYear,
                        FileUrl = staged.FileUrl ?? string.Empty,
                        FileName = staged.FileName ?? string.Empty,
                        FileSize = staged.FileSize ?? 0,
                        FileMimeType = staged.FileMimeType,
                        FileHash = staged.FileHash,
                        Metadata = new Dictionary<string, object> { ["source_ca_staged_notice_id"] = staged.Id.ToString() }
                    };
                    _dbContext.Notices.Add(notice);
                    // Needs Notice.Id assigned before it can be referenced below.
                    await _dbContext.SaveChangesAsync();

                    staged.MergedToNotices = true;
                    staged.MergedNoticeId = notice.Id;
                    staged.MergedAt = DateTime.UtcNow;
                    newNoticeIds.Add(notice.Id);
                }

                prospectClient.Status = "merged";
                prospectClient.MergedAt = DateTime.UtcNow;
                prospectClient.MergedIntoOrganizationId = newOrganizationId;
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        // Usage counting and AI-processing enqueue happen after commit, mirroring
        // NoticeService.UploadAsync - a rolled-back transaction must never have
        // already queued a job for a Notice row that no longer exists.
        foreach (var noticeId in newNoticeIds)
        {
            await _usageService.IncrementNoticeCountAsync(newOrganizationId);
            _backgroundJobs.Enqueue<INoticeProcessingJob>(job => job.ProcessAsync(noticeId, CancellationToken.None));
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
                MergedNoticeCount = mergedCount,
                NewNoticeCount = newNoticeIds.Count
            }
        });

        _logger.LogInformation(
            "BO {BoUserId} accepted CA client invitation {InvitationId}, created organization {OrganizationId} ({MergedCount} merged, {NewCount} new notices)",
            boUserId, invitation.Id, newOrganizationId, mergedCount, newNoticeIds.Count);

        return new AcceptCaClientInvitationResult(
            OrganizationId: newOrganizationId,
            OrganizationName: newOrganizationName,
            MergedNoticeCount: mergedCount,
            NewNoticeCount: newNoticeIds.Count,
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
