using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for managing CA invitations.
/// </summary>
public class CaInvitationService : ICaInvitationService
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CaInvitationService> _logger;

    private const int InvitationExpiryDays = 7;
    private const int MaxResendCount = 3;
    private const int MaxDailyInvitations = 50;

    public CaInvitationService(
        ApplicationDbContext db,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<CaInvitationService> logger)
    {
        _db = db;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<CaInvitationResult> CreateInvitationAsync(
        Guid caUserId,
        CreateCaInvitationRequest request,
        CancellationToken ct = default)
    {
        // Validate CA is active
        var caProfile = await _db.CaProfiles
            .FirstOrDefaultAsync(p => p.UserId == caUserId && p.Status == CaProfileStatus.Active, ct);

        if (caProfile == null)
        {
            return new CaInvitationResult(false, ErrorCode: "CA_NOT_ACTIVE", ErrorMessage: "CA profile is not active");
        }

        // Check daily rate limit
        var dailyCount = await GetDailyInvitationCountAsync(caUserId, ct);
        if (dailyCount >= MaxDailyInvitations)
        {
            return new CaInvitationResult(false, ErrorCode: "RATE_LIMIT_EXCEEDED", ErrorMessage: "Daily invitation limit reached");
        }

        // Check if GSTIN is already claimed by another organization
        var existingGstin = await _db.OrganizationGstins
            .FirstOrDefaultAsync(g => g.Gstin == request.Gstin && g.IsClaimed, ct);

        if (existingGstin != null)
        {
            // GSTIN is claimed - check if there's already an active CA for it
            var existingAuth = await _db.CaGstinAuthorizations
                .Include(a => a.CaClientRelationship)
                .FirstOrDefaultAsync(a =>
                    a.Gstin == request.Gstin &&
                    a.Status == CaGstinAuthorizationStatus.Active &&
                    a.CaClientRelationship.Status == CaClientRelationshipStatus.Active,
                    ct);

            if (existingAuth != null && existingAuth.CaClientRelationship.CaUserId != caUserId)
            {
                return new CaInvitationResult(false, ErrorCode: "GSTIN_HAS_CA", ErrorMessage: "This GSTIN already has an active CA");
            }
        }

        // Check for duplicate pending invitation
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existingInvitation = await _db.CaInvitations
            .FirstOrDefaultAsync(i =>
                i.InviterUserId == caUserId &&
                i.InviteeEmailNormalized == normalizedEmail &&
                i.Gstin == request.Gstin &&
                i.Status == CaInvitationStatus.Pending,
                ct);

        if (existingInvitation != null)
        {
            return new CaInvitationResult(false, ErrorCode: "DUPLICATE_INVITATION", ErrorMessage: "A pending invitation already exists for this email and GSTIN");
        }

        // Generate secure token
        var token = GenerateSecureToken();
        var tokenHash = HashToken(token);

        // Create invitation
        var invitation = new CaInvitation
        {
            InvitationType = CaInvitationType.CaInvitesBo,
            InviterUserId = caUserId,
            InviteeEmail = request.Email.Trim(),
            InviteeEmailNormalized = normalizedEmail,
            Gstin = request.Gstin,
            TokenHash = tokenHash,
            Status = CaInvitationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays),
            Message = request.Message,
            SendCount = 1,
            LastSentAt = DateTime.UtcNow
        };

        _db.CaInvitations.Add(invitation);
        await _db.SaveChangesAsync(ct);

        // Send invitation email
        try
        {
            var ca = await _db.Users.FindAsync([caUserId], ct);
            var invitationUrl = $"{_configuration["App:BaseUrl"]?.TrimEnd('/')}/ca-invitation?token={token}";
            await _emailService.SendTemplateAsync(
                request.Email,
                "ca_invitation",
                new Dictionary<string, object>
                {
                    ["ca_name"] = ca!.Name,
                    ["firm_name"] = caProfile.FirmName ?? "",
                    ["gstin"] = request.Gstin,
                    ["message"] = request.Message ?? "",
                    ["invitation_url"] = invitationUrl,
                    ["expires_in_days"] = InvitationExpiryDays
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send invitation email to {Email}", request.Email);
            // Don't fail the invitation creation if email fails
        }

        _logger.LogInformation("CA invitation created: {InvitationId}, CA: {CaUserId}, GSTIN: {Gstin}", invitation.Id, caUserId, request.Gstin);

        return new CaInvitationResult(true, InvitationId: invitation.Id);
    }

    public async Task<CaInvitationResult> CreateBoInvitationAsync(
        Guid boUserId,
        Guid organizationId,
        BoInviteCaRequest request,
        CancellationToken ct = default)
    {
        // Validate BO owns the GSTINs
        var orgGstins = await _db.OrganizationGstins
            .Where(g => g.OrganizationId == organizationId && request.Gstins.Contains(g.Gstin))
            .Select(g => g.Gstin)
            .ToListAsync(ct);

        var missingGstins = request.Gstins.Except(orgGstins).ToList();
        if (missingGstins.Any())
        {
            return new CaInvitationResult(false, ErrorCode: "GSTIN_NOT_OWNED", ErrorMessage: $"You don't own these GSTINs: {string.Join(", ", missingGstins)}");
        }

        var normalizedEmail = request.CaEmail.Trim().ToLowerInvariant();
        var createdInvitations = new List<Guid>();

        foreach (var gstin in request.Gstins)
        {
            // Check for existing active CA
            var existingAuth = await _db.CaGstinAuthorizations
                .Include(a => a.CaClientRelationship)
                .FirstOrDefaultAsync(a =>
                    a.Gstin == gstin &&
                    a.Status == CaGstinAuthorizationStatus.Active &&
                    a.CaClientRelationship.Status == CaClientRelationshipStatus.Active,
                    ct);

            if (existingAuth != null)
            {
                continue; // Skip GSTINs that already have a CA
            }

            // Check for duplicate pending invitation
            var existingInvitation = await _db.CaInvitations
                .FirstOrDefaultAsync(i =>
                    i.InviterUserId == boUserId &&
                    i.InviteeEmailNormalized == normalizedEmail &&
                    i.Gstin == gstin &&
                    i.Status == CaInvitationStatus.Pending,
                    ct);

            if (existingInvitation != null)
            {
                continue; // Skip duplicates
            }

            var token = GenerateSecureToken();
            var tokenHash = HashToken(token);

            var invitation = new CaInvitation
            {
                InvitationType = CaInvitationType.BoInvitesCa,
                InviterUserId = boUserId,
                InviterOrganizationId = organizationId,
                InviteeEmail = request.CaEmail.Trim(),
                InviteeEmailNormalized = normalizedEmail,
                Gstin = gstin,
                TokenHash = tokenHash,
                Status = CaInvitationStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays),
                Message = request.Message,
                SendCount = 1,
                LastSentAt = DateTime.UtcNow
            };

            _db.CaInvitations.Add(invitation);
            createdInvitations.Add(invitation.Id);
        }

        if (!createdInvitations.Any())
        {
            return new CaInvitationResult(false, ErrorCode: "NO_INVITATIONS_CREATED", ErrorMessage: "No new invitations were created");
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("BO invitations created: {Count}, BO: {BoUserId}, CA Email: {Email}", createdInvitations.Count, boUserId, request.CaEmail);

        return new CaInvitationResult(true, InvitationId: createdInvitations.First());
    }

    public async Task<CaInvitation?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = HashToken(token);
        return await _db.CaInvitations
            .Include(i => i.InviterUser)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);
    }

    public async Task<List<CaInvitationDto>> GetPendingByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        var invitations = await _db.CaInvitations
            .Include(i => i.InviterUser)
                .ThenInclude(u => u.CaProfile)
            .Where(i =>
                i.InviteeEmailNormalized == normalizedEmail &&
                i.Status == CaInvitationStatus.Pending &&
                i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        return invitations.Select(MapToDto).ToList();
    }

    public async Task<CaInvitationListResponse> GetSentByCaAsync(
        Guid caUserId,
        string? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.CaInvitations
            .Include(i => i.AcceptedUser)
            .Where(i => i.InviterUserId == caUserId);

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(i => i.Status == status);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new CaInvitationListResponse(
            Items: items.Select(MapToDto).ToList(),
            Total: total,
            Page: page,
            PageSize: pageSize,
            TotalPages: (int)Math.Ceiling((double)total / pageSize)
        );
    }

    public async Task<AcceptInvitationResult> AcceptAsync(
        string token,
        Guid acceptingUserId,
        CancellationToken ct = default)
    {
        var tokenHash = HashToken(token);
        var invitation = await _db.CaInvitations
            .Include(i => i.InviterUser)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, ct);

        if (invitation == null)
        {
            return new AcceptInvitationResult(false, ErrorCode: "INVALID_TOKEN", ErrorMessage: "Invalid invitation token");
        }

        if (invitation.Status != CaInvitationStatus.Pending)
        {
            return new AcceptInvitationResult(false, ErrorCode: "INVITATION_NOT_PENDING", ErrorMessage: $"Invitation is {invitation.Status}");
        }

        if (invitation.ExpiresAt <= DateTime.UtcNow)
        {
            invitation.Status = CaInvitationStatus.Expired;
            await _db.SaveChangesAsync(ct);
            return new AcceptInvitationResult(false, ErrorCode: "INVITATION_EXPIRED", ErrorMessage: "Invitation has expired");
        }

        using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Update invitation
            invitation.Status = CaInvitationStatus.Accepted;
            invitation.RespondedAt = DateTime.UtcNow;
            invitation.AcceptedUserId = acceptingUserId;

            // Determine CA and client based on invitation type
            Guid caUserId, clientUserId;
            if (invitation.InvitationType == CaInvitationType.CaInvitesBo)
            {
                caUserId = invitation.InviterUserId;
                clientUserId = acceptingUserId;
            }
            else // BoInvitesCa
            {
                caUserId = acceptingUserId;
                clientUserId = invitation.InviterUserId;

                // Mark accepting user as CA if not already
                var acceptingUser = await _db.Users.FindAsync([acceptingUserId], ct);
                if (acceptingUser != null && !acceptingUser.IsCa)
                {
                    acceptingUser.IsCa = true;

                    // Create CA profile if it doesn't exist
                    var existingProfile = await _db.CaProfiles.FirstOrDefaultAsync(p => p.UserId == acceptingUserId, ct);
                    if (existingProfile == null)
                    {
                        _db.CaProfiles.Add(new CaProfile
                        {
                            UserId = acceptingUserId,
                            Status = CaProfileStatus.Active
                        });
                    }
                }
            }

            // Find or create relationship
            var relationship = await _db.CaClientRelationships
                .FirstOrDefaultAsync(r =>
                    r.CaUserId == caUserId &&
                    r.ClientUserId == clientUserId,
                    ct);

            if (relationship == null)
            {
                // Get client's organization if they have one
                var clientOrg = await _db.OrganizationMembers
                    .Where(m => m.UserId == clientUserId && m.Role == "owner")
                    .Select(m => m.OrganizationId)
                    .FirstOrDefaultAsync(ct);

                relationship = new CaClientRelationship
                {
                    CaUserId = caUserId,
                    ClientUserId = clientUserId,
                    OrganizationId = clientOrg != default ? clientOrg : null,
                    Status = clientOrg != default ? CaClientRelationshipStatus.Active : CaClientRelationshipStatus.PendingInvitation,
                    InvitedAt = invitation.CreatedAt,
                    AcceptedAt = DateTime.UtcNow
                };
                _db.CaClientRelationships.Add(relationship);
                await _db.SaveChangesAsync(ct);
            }

            // Create or update authorization for this GSTIN
            var existingAuth = await _db.CaGstinAuthorizations
                .FirstOrDefaultAsync(a =>
                    a.CaClientRelationshipId == relationship.Id &&
                    a.Gstin == invitation.Gstin,
                    ct);

            if (existingAuth == null)
            {
                // Find OrganizationGstin if claimed
                var orgGstin = await _db.OrganizationGstins
                    .FirstOrDefaultAsync(g =>
                        g.Gstin == invitation.Gstin &&
                        g.OrganizationId == relationship.OrganizationId,
                        ct);

                var authorization = new CaGstinAuthorization
                {
                    CaClientRelationshipId = relationship.Id,
                    Gstin = invitation.Gstin,
                    OrganizationGstinId = orgGstin?.Id,
                    Status = orgGstin != null ? CaGstinAuthorizationStatus.Active : CaGstinAuthorizationStatus.PendingClaim,
                    Permissions = CaPermission.DefaultPermissions.ToList(),
                    GrantedAt = DateTime.UtcNow
                };
                _db.CaGstinAuthorizations.Add(authorization);
            }
            else if (existingAuth.Status == CaGstinAuthorizationStatus.Revoked)
            {
                // Reactivate revoked authorization
                existingAuth.Status = CaGstinAuthorizationStatus.Active;
                existingAuth.RevokedAt = null;
                existingAuth.RevokedById = null;
                existingAuth.RevocationReason = null;
                existingAuth.GrantedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Invitation accepted: {InvitationId}, Relationship: {RelationshipId}", invitation.Id, relationship.Id);

            return new AcceptInvitationResult(
                Success: true,
                RelationshipId: relationship.Id,
                Gstin: invitation.Gstin,
                RequiresOrganizationSetup: relationship.OrganizationId == null
            );
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to accept invitation: {InvitationId}", invitation.Id);
            throw;
        }
    }

    public async Task<bool> DeclineAsync(string token, string? reason = null, CancellationToken ct = default)
    {
        var tokenHash = HashToken(token);
        var invitation = await _db.CaInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash && i.Status == CaInvitationStatus.Pending, ct);

        if (invitation == null)
            return false;

        invitation.Status = CaInvitationStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;
        invitation.ResponseReason = reason;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Invitation declined: {InvitationId}", invitation.Id);

        return true;
    }

    public async Task<bool> CancelAsync(Guid invitationId, Guid userId, CancellationToken ct = default)
    {
        var invitation = await _db.CaInvitations
            .FirstOrDefaultAsync(i =>
                i.Id == invitationId &&
                i.InviterUserId == userId &&
                i.Status == CaInvitationStatus.Pending,
                ct);

        if (invitation == null)
            return false;

        invitation.Status = CaInvitationStatus.Cancelled;
        invitation.CancelledAt = DateTime.UtcNow;
        invitation.CancelledById = userId;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Invitation cancelled: {InvitationId}", invitation.Id);

        return true;
    }

    public async Task<bool> ResendAsync(Guid invitationId, Guid caUserId, string? updatedMessage = null, CancellationToken ct = default)
    {
        var invitation = await _db.CaInvitations
            .Include(i => i.InviterUser)
                .ThenInclude(u => u.CaProfile)
            .FirstOrDefaultAsync(i =>
                i.Id == invitationId &&
                i.InviterUserId == caUserId &&
                i.Status == CaInvitationStatus.Pending,
                ct);

        if (invitation == null)
            return false;

        if (invitation.SendCount >= MaxResendCount)
        {
            _logger.LogWarning("Resend limit reached for invitation: {InvitationId}", invitationId);
            return false;
        }

        // Generate new token
        var newToken = GenerateSecureToken();
        invitation.TokenHash = HashToken(newToken);
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays);
        invitation.SendCount++;
        invitation.LastSentAt = DateTime.UtcNow;

        if (updatedMessage != null)
        {
            invitation.Message = updatedMessage;
        }

        await _db.SaveChangesAsync(ct);

        // Send email
        try
        {
            var invitationUrl = $"{_configuration["App:BaseUrl"]?.TrimEnd('/')}/ca-invitation?token={newToken}";
            await _emailService.SendTemplateAsync(
                invitation.InviteeEmail,
                "ca_invitation",
                new Dictionary<string, object>
                {
                    ["ca_name"] = invitation.InviterUser.Name,
                    ["firm_name"] = invitation.InviterUser.CaProfile?.FirmName ?? "",
                    ["gstin"] = invitation.Gstin,
                    ["message"] = invitation.Message ?? "",
                    ["invitation_url"] = invitationUrl,
                    ["expires_in_days"] = InvitationExpiryDays
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend invitation email: {InvitationId}", invitationId);
        }

        _logger.LogInformation("Invitation resent: {InvitationId}, Count: {Count}", invitationId, invitation.SendCount);
        return true;
    }

    public async Task<CaInvitationDto?> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        var invitation = await GetByTokenAsync(token, ct);
        if (invitation == null)
            return null;

        if (invitation.Status != CaInvitationStatus.Pending)
            return null;

        if (invitation.ExpiresAt <= DateTime.UtcNow)
            return null;

        return MapToDto(invitation);
    }

    public async Task ExpirePendingInvitationsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiredInvitations = await _db.CaInvitations
            .Where(i => i.Status == CaInvitationStatus.Pending && i.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (var invitation in expiredInvitations)
        {
            invitation.Status = CaInvitationStatus.Expired;
        }

        if (expiredInvitations.Any())
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} pending invitations", expiredInvitations.Count);
        }
    }

    public async Task<int> GetDailyInvitationCountAsync(Guid caUserId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        return await _db.CaInvitations
            .CountAsync(i =>
                i.InviterUserId == caUserId &&
                i.CreatedAt >= today,
                ct);
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static CaInvitationDto MapToDto(CaInvitation invitation)
    {
        CaInviterDto? inviter = null;
        if (invitation.InviterUser != null)
        {
            inviter = new CaInviterDto(
                UserId: invitation.InviterUserId,
                Name: invitation.InviterUser.Name,
                Email: invitation.InviterUser.Email!,
                FirmName: invitation.InviterUser.CaProfile?.FirmName,
                IsVerified: invitation.InviterUser.CaProfile?.IsVerified ?? false
            );
        }

        AcceptedUserDto? acceptedUser = null;
        if (invitation.AcceptedUser != null)
        {
            acceptedUser = new AcceptedUserDto(
                UserId: invitation.AcceptedUserId!.Value,
                Name: invitation.AcceptedUser.Name,
                Email: invitation.AcceptedUser.Email!
            );
        }

        return new CaInvitationDto(
            Id: invitation.Id,
            InvitationType: invitation.InvitationType,
            InviteeEmail: invitation.InviteeEmail,
            Gstin: invitation.Gstin,
            Status: invitation.Status,
            ExpiresAt: invitation.ExpiresAt,
            RespondedAt: invitation.RespondedAt,
            Message: invitation.Message,
            SendCount: invitation.SendCount,
            LastSentAt: invitation.LastSentAt,
            StagedNoticeCount: invitation.StagedNoticeCount,
            CreatedAt: invitation.CreatedAt,
            Inviter: inviter,
            AcceptedUser: acceptedUser
        );
    }
}
