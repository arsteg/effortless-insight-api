using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for admin management of CA profiles, relationships, and invitations.
/// </summary>
public class AdminCaService : IAdminCaService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAdminAuditService _auditService;
    private readonly ILogger<AdminCaService> _logger;

    public AdminCaService(
        ApplicationDbContext dbContext,
        IAdminAuditService auditService,
        ILogger<AdminCaService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    // ========================================================================
    // CA Profile Management
    // ========================================================================

    public async Task<AdminCaListResponse> ListCasAsync(AdminCaSearchParams searchParams)
    {
        var query = _dbContext.CaProfiles
            .Include(cp => cp.User)
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrEmpty(searchParams.Search))
        {
            var searchLower = searchParams.Search.ToLower();
            query = query.Where(cp =>
                cp.User.Name.ToLower().Contains(searchLower) ||
                (cp.User.Email != null && cp.User.Email.ToLower().Contains(searchLower)) ||
                (cp.FirmName != null && cp.FirmName.ToLower().Contains(searchLower)) ||
                (cp.MembershipNumber != null && cp.MembershipNumber.ToLower().Contains(searchLower)));
        }

        // Apply status filter
        if (!string.IsNullOrEmpty(searchParams.Status))
        {
            query = query.Where(cp => cp.Status == searchParams.Status);
        }

        // Apply verified filter
        if (searchParams.IsVerified.HasValue)
        {
            query = query.Where(cp => cp.IsVerified == searchParams.IsVerified.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = searchParams.SortBy?.ToLower() switch
        {
            "name" => searchParams.SortDesc
                ? query.OrderByDescending(cp => cp.User.Name)
                : query.OrderBy(cp => cp.User.Name),
            "firmname" => searchParams.SortDesc
                ? query.OrderByDescending(cp => cp.FirmName)
                : query.OrderBy(cp => cp.FirmName),
            "verifiedat" => searchParams.SortDesc
                ? query.OrderByDescending(cp => cp.VerifiedAt)
                : query.OrderBy(cp => cp.VerifiedAt),
            _ => searchParams.SortDesc
                ? query.OrderByDescending(cp => cp.CreatedAt)
                : query.OrderBy(cp => cp.CreatedAt)
        };

        // Apply pagination
        var page = Math.Max(1, searchParams.Page);
        var pageSize = Math.Clamp(searchParams.PageSize, 1, 100);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var caProfiles = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Get client counts and invitation counts for each CA
        var caProfileIds = caProfiles.Select(cp => cp.UserId).ToList();

        var activeClientCounts = await _dbContext.CaClientRelationships
            .Where(r => caProfileIds.Contains(r.CaUserId) && r.Status == CaClientRelationshipStatus.Active)
            .GroupBy(r => r.CaUserId)
            .Select(g => new { CaUserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CaUserId, x => x.Count);

        var pendingInvitationCounts = await _dbContext.CaInvitations
            .Where(i => caProfileIds.Contains(i.InviterUserId) && i.Status == CaInvitationStatus.Pending)
            .GroupBy(i => i.InviterUserId)
            .Select(g => new { CaUserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CaUserId, x => x.Count);

        var items = caProfiles.Select(cp => new AdminCaProfileListItemDto(
            Id: cp.Id,
            UserId: cp.UserId,
            UserName: cp.User.Name,
            UserEmail: cp.User.Email ?? "",
            FirmName: cp.FirmName,
            MembershipNumber: cp.MembershipNumber,
            IsVerified: cp.IsVerified,
            VerifiedAt: cp.VerifiedAt,
            Status: cp.Status,
            ActiveClientCount: activeClientCounts.GetValueOrDefault(cp.UserId, 0),
            PendingInvitationCount: pendingInvitationCounts.GetValueOrDefault(cp.UserId, 0),
            CreatedAt: cp.CreatedAt
        )).ToList();

        return new AdminCaListResponse(
            Cas: items,
            Pagination: new AdminPaginationDto(page, pageSize, totalCount, totalPages)
        );
    }

    public async Task<AdminCaProfileDetailDto?> GetCaAsync(Guid caProfileId)
    {
        var caProfile = await _dbContext.CaProfiles
            .Include(cp => cp.User)
            .FirstOrDefaultAsync(cp => cp.Id == caProfileId);

        if (caProfile == null)
        {
            return null;
        }

        // Get verified by admin name
        string? verifiedByAdminName = null;
        // Note: We'll need to add VerifiedByAdminId to CaProfile entity if we want to track who verified

        // Get relationships
        var relationships = await _dbContext.CaClientRelationships
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
            .Where(r => r.CaUserId == caProfile.UserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        var relationshipSummaries = relationships.Select(r => new AdminCaRelationshipSummaryDto(
            Id: r.Id,
            ClientUserId: r.ClientUserId,
            ClientUserName: r.ClientUser.Name,
            ClientUserEmail: r.ClientUser.Email ?? "",
            OrganizationId: r.OrganizationId,
            OrganizationName: r.Organization?.Name,
            Status: r.Status,
            GstinCount: r.GstinAuthorizations.Count,
            NoticeCount: 0, // Would need to count notices for authorized GSTINs
            InvitedAt: r.InvitedAt,
            AcceptedAt: r.AcceptedAt
        )).ToList();

        // Get recent invitations
        var invitations = await _dbContext.CaInvitations
            .Where(i => i.InviterUserId == caProfile.UserId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(10)
            .ToListAsync();

        var invitationSummaries = invitations.Select(i => new AdminCaInvitationSummaryDto(
            Id: i.Id,
            InviteeEmail: i.InviteeEmail,
            Gstin: i.Gstin,
            Status: i.Status,
            CreatedAt: i.CreatedAt,
            ExpiresAt: i.ExpiresAt,
            RespondedAt: i.RespondedAt
        )).ToList();

        // Calculate totals
        var activeClientCount = relationships.Count(r => r.Status == CaClientRelationshipStatus.Active);
        var pendingInvitationCount = invitations.Count(i => i.Status == CaInvitationStatus.Pending);
        var totalAuthorizedGstins = relationships
            .Where(r => r.Status == CaClientRelationshipStatus.Active)
            .SelectMany(r => r.GstinAuthorizations)
            .Count(a => a.Status == CaGstinAuthorizationStatus.Active);

        return new AdminCaProfileDetailDto(
            Id: caProfile.Id,
            UserId: caProfile.UserId,
            UserName: caProfile.User.Name,
            UserEmail: caProfile.User.Email ?? "",
            UserMobile: caProfile.User.Mobile,
            FirmName: caProfile.FirmName,
            MembershipNumber: caProfile.MembershipNumber,
            IsVerified: caProfile.IsVerified,
            VerifiedAt: caProfile.VerifiedAt,
            VerifiedByAdminId: null, // Would need to add to entity
            VerifiedByAdminName: verifiedByAdminName,
            Status: caProfile.Status,
            SuspendedAt: caProfile.SuspendedAt,
            SuspendedByAdminId: null, // Would need to add to entity
            SuspendedReason: caProfile.SuspensionReason,
            ActiveClientCount: activeClientCount,
            PendingInvitationCount: pendingInvitationCount,
            TotalAuthorizedGstins: totalAuthorizedGstins,
            Relationships: relationshipSummaries,
            RecentInvitations: invitationSummaries,
            CreatedAt: caProfile.CreatedAt,
            UpdatedAt: caProfile.UpdatedAt
        );
    }

    public async Task VerifyCaAsync(Guid caProfileId, Guid adminUserId, AdminVerifyCaRequest request)
    {
        var caProfile = await _dbContext.CaProfiles
            .Include(cp => cp.User)
            .FirstOrDefaultAsync(cp => cp.Id == caProfileId);

        if (caProfile == null)
        {
            throw new KeyNotFoundException($"CA profile not found: {caProfileId}");
        }

        if (caProfile.IsVerified)
        {
            throw new InvalidOperationException("CA profile is already verified");
        }

        caProfile.IsVerified = true;
        caProfile.VerifiedAt = DateTime.UtcNow;
        caProfile.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            adminUserId,
            "ca_verified",
            "CaProfile",
            caProfileId.ToString(),
            $"CA verified: {caProfile.User.Name}",
            new Dictionary<string, object>
            {
                ["membership_verified"] = request.MembershipVerified,
                ["notes"] = request.Notes ?? ""
            });

        _logger.LogInformation("CA profile verified: {CaProfileId} by admin: {AdminId}", caProfileId, adminUserId);
    }

    public async Task RevokeCaVerificationAsync(Guid caProfileId, Guid adminUserId, string? reason)
    {
        var caProfile = await _dbContext.CaProfiles
            .Include(cp => cp.User)
            .FirstOrDefaultAsync(cp => cp.Id == caProfileId);

        if (caProfile == null)
        {
            throw new KeyNotFoundException($"CA profile not found: {caProfileId}");
        }

        if (!caProfile.IsVerified)
        {
            throw new InvalidOperationException("CA profile is not verified");
        }

        caProfile.IsVerified = false;
        caProfile.VerifiedAt = null;
        caProfile.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            adminUserId,
            "ca_verification_revoked",
            "CaProfile",
            caProfileId.ToString(),
            $"CA verification revoked: {caProfile.User.Name}",
            new Dictionary<string, object>
            {
                ["reason"] = reason ?? ""
            });

        _logger.LogInformation("CA verification revoked: {CaProfileId} by admin: {AdminId}", caProfileId, adminUserId);
    }

    public async Task SuspendCaAsync(Guid caProfileId, Guid adminUserId, AdminSuspendCaRequest request)
    {
        var caProfile = await _dbContext.CaProfiles
            .Include(cp => cp.User)
            .FirstOrDefaultAsync(cp => cp.Id == caProfileId);

        if (caProfile == null)
        {
            throw new KeyNotFoundException($"CA profile not found: {caProfileId}");
        }

        if (caProfile.Status == CaProfileStatus.Suspended)
        {
            throw new InvalidOperationException("CA profile is already suspended");
        }

        caProfile.Status = CaProfileStatus.Suspended;
        caProfile.SuspendedAt = DateTime.UtcNow;
        caProfile.SuspensionReason = request.Reason;
        caProfile.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            adminUserId,
            "ca_suspended",
            "CaProfile",
            caProfileId.ToString(),
            $"CA suspended: {caProfile.User.Name}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason,
                ["notes"] = request.Notes ?? ""
            });

        _logger.LogInformation("CA profile suspended: {CaProfileId} by admin: {AdminId}", caProfileId, adminUserId);
    }

    public async Task UnsuspendCaAsync(Guid caProfileId, Guid adminUserId)
    {
        var caProfile = await _dbContext.CaProfiles
            .Include(cp => cp.User)
            .FirstOrDefaultAsync(cp => cp.Id == caProfileId);

        if (caProfile == null)
        {
            throw new KeyNotFoundException($"CA profile not found: {caProfileId}");
        }

        if (caProfile.Status != CaProfileStatus.Suspended)
        {
            throw new InvalidOperationException("CA profile is not suspended");
        }

        caProfile.Status = CaProfileStatus.Active;
        caProfile.SuspendedAt = null;
        caProfile.SuspensionReason = null;
        caProfile.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            adminUserId,
            "ca_unsuspended",
            "CaProfile",
            caProfileId.ToString(),
            $"CA unsuspended: {caProfile.User.Name}");

        _logger.LogInformation("CA profile unsuspended: {CaProfileId} by admin: {AdminId}", caProfileId, adminUserId);
    }

    // ========================================================================
    // CA Relationship Management
    // ========================================================================

    public async Task<AdminCaRelationshipListResponse> ListRelationshipsAsync(AdminCaRelationshipSearchParams searchParams)
    {
        var query = _dbContext.CaClientRelationships
            .Include(r => r.CaUser)
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrEmpty(searchParams.Search))
        {
            var searchLower = searchParams.Search.ToLower();
            query = query.Where(r =>
                r.CaUser.Name.ToLower().Contains(searchLower) ||
                (r.CaUser.Email != null && r.CaUser.Email.ToLower().Contains(searchLower)) ||
                r.ClientUser.Name.ToLower().Contains(searchLower) ||
                (r.ClientUser.Email != null && r.ClientUser.Email.ToLower().Contains(searchLower)) ||
                (r.Organization != null && r.Organization.Name.ToLower().Contains(searchLower)));
        }

        // Apply CA filter
        if (searchParams.CaUserId.HasValue)
        {
            query = query.Where(r => r.CaUserId == searchParams.CaUserId.Value);
        }

        // Apply client filter
        if (searchParams.ClientUserId.HasValue)
        {
            query = query.Where(r => r.ClientUserId == searchParams.ClientUserId.Value);
        }

        // Apply organization filter
        if (searchParams.OrganizationId.HasValue)
        {
            query = query.Where(r => r.OrganizationId == searchParams.OrganizationId.Value);
        }

        // Apply status filter
        if (!string.IsNullOrEmpty(searchParams.Status))
        {
            query = query.Where(r => r.Status == searchParams.Status);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = searchParams.SortBy?.ToLower() switch
        {
            "caname" => searchParams.SortDesc
                ? query.OrderByDescending(r => r.CaUser.Name)
                : query.OrderBy(r => r.CaUser.Name),
            "clientname" => searchParams.SortDesc
                ? query.OrderByDescending(r => r.ClientUser.Name)
                : query.OrderBy(r => r.ClientUser.Name),
            "acceptedat" => searchParams.SortDesc
                ? query.OrderByDescending(r => r.AcceptedAt)
                : query.OrderBy(r => r.AcceptedAt),
            _ => searchParams.SortDesc
                ? query.OrderByDescending(r => r.InvitedAt)
                : query.OrderBy(r => r.InvitedAt)
        };

        // Apply pagination
        var page = Math.Max(1, searchParams.Page);
        var pageSize = Math.Clamp(searchParams.PageSize, 1, 100);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var relationships = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Get CA firm names
        var caUserIds = relationships.Select(r => r.CaUserId).Distinct().ToList();
        var caProfiles = await _dbContext.CaProfiles
            .Where(cp => caUserIds.Contains(cp.UserId))
            .ToDictionaryAsync(cp => cp.UserId, cp => cp.FirmName);

        var items = relationships.Select(r => new AdminCaRelationshipListItemDto(
            Id: r.Id,
            CaUserId: r.CaUserId,
            CaUserName: r.CaUser.Name,
            CaUserEmail: r.CaUser.Email ?? "",
            CaFirmName: caProfiles.GetValueOrDefault(r.CaUserId),
            ClientUserId: r.ClientUserId,
            ClientUserName: r.ClientUser.Name,
            ClientUserEmail: r.ClientUser.Email ?? "",
            OrganizationId: r.OrganizationId,
            OrganizationName: r.Organization?.Name,
            Status: r.Status,
            GstinCount: r.GstinAuthorizations.Count,
            NoticeCount: 0, // Would need to calculate
            InvitedAt: r.InvitedAt,
            AcceptedAt: r.AcceptedAt,
            RevokedAt: r.RevokedAt
        )).ToList();

        return new AdminCaRelationshipListResponse(
            Relationships: items,
            Pagination: new AdminPaginationDto(page, pageSize, totalCount, totalPages)
        );
    }

    public async Task<AdminCaRelationshipDetailDto?> GetRelationshipAsync(Guid relationshipId)
    {
        var relationship = await _dbContext.CaClientRelationships
            .Include(r => r.CaUser)
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
                .ThenInclude(a => a.OrganizationGstin)
            .Include(r => r.RevokedBy)
            .FirstOrDefaultAsync(r => r.Id == relationshipId);

        if (relationship == null)
        {
            return null;
        }

        // Get CA profile
        var caProfile = await _dbContext.CaProfiles
            .FirstOrDefaultAsync(cp => cp.UserId == relationship.CaUserId);

        var caProfileBrief = new AdminCaProfileBriefDto(
            Id: caProfile?.Id ?? Guid.Empty,
            UserId: relationship.CaUserId,
            UserName: relationship.CaUser.Name,
            UserEmail: relationship.CaUser.Email ?? "",
            FirmName: caProfile?.FirmName,
            IsVerified: caProfile?.IsVerified ?? false
        );

        var clientBrief = new AdminClientBriefDto(
            UserId: relationship.ClientUserId,
            UserName: relationship.ClientUser.Name,
            UserEmail: relationship.ClientUser.Email ?? "",
            OrganizationId: relationship.OrganizationId,
            OrganizationName: relationship.Organization?.Name
        );

        var gstinAuthorizations = relationship.GstinAuthorizations.Select(a => new AdminCaGstinAuthorizationDto(
            Id: a.Id,
            Gstin: a.Gstin,
            TradeName: a.OrganizationGstin?.TradeName,
            LegalName: a.OrganizationGstin?.LegalName,
            StateCode: a.Gstin.Substring(0, 2),
            StateName: null, // Would need state mapping
            Status: a.Status,
            Permissions: a.Permissions,
            GrantedAt: a.GrantedAt,
            RevokedAt: a.RevokedAt,
            NoticeCount: 0 // Would need to calculate
        )).ToList();

        // Determine who revoked
        string? revokedBy = null;
        if (relationship.RevokedById.HasValue)
        {
            revokedBy = relationship.RevokedBy?.Name ?? "Unknown";
        }

        return new AdminCaRelationshipDetailDto(
            Id: relationship.Id,
            CaProfile: caProfileBrief,
            Client: clientBrief,
            Status: relationship.Status,
            GstinAuthorizations: gstinAuthorizations,
            InvitedAt: relationship.InvitedAt,
            AcceptedAt: relationship.AcceptedAt,
            RevokedAt: relationship.RevokedAt,
            RevokedBy: revokedBy,
            RevokeReason: relationship.RevocationReason,
            NoticeCount: 0, // Would need to calculate
            LastActivityAt: null // Would need to track
        );
    }

    public async Task RevokeRelationshipAsync(Guid relationshipId, Guid adminUserId, string reason)
    {
        var relationship = await _dbContext.CaClientRelationships
            .Include(r => r.CaUser)
            .Include(r => r.ClientUser)
            .FirstOrDefaultAsync(r => r.Id == relationshipId);

        if (relationship == null)
        {
            throw new KeyNotFoundException($"Relationship not found: {relationshipId}");
        }

        if (relationship.Status == CaClientRelationshipStatus.Revoked)
        {
            throw new InvalidOperationException("Relationship is already revoked");
        }

        relationship.Status = CaClientRelationshipStatus.Revoked;
        relationship.RevokedAt = DateTime.UtcNow;
        relationship.RevocationReason = $"[Admin] {reason}";
        relationship.UpdatedAt = DateTime.UtcNow;

        // Revoke all GSTIN authorizations
        var authorizations = await _dbContext.CaGstinAuthorizations
            .Where(a => a.CaClientRelationshipId == relationshipId && a.Status == CaGstinAuthorizationStatus.Active)
            .ToListAsync();

        foreach (var auth in authorizations)
        {
            auth.Status = CaGstinAuthorizationStatus.Revoked;
            auth.RevokedAt = DateTime.UtcNow;
            auth.RevocationReason = $"[Admin] Parent relationship revoked: {reason}";
            auth.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            adminUserId,
            "ca_relationship_revoked",
            "CaClientRelationship",
            relationshipId.ToString(),
            $"CA relationship revoked: {relationship.CaUser.Name} -> {relationship.ClientUser.Name}",
            new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["ca_user_id"] = relationship.CaUserId.ToString(),
                ["client_user_id"] = relationship.ClientUserId.ToString(),
                ["authorizations_revoked"] = authorizations.Count
            });

        _logger.LogInformation(
            "CA relationship revoked: {RelationshipId} by admin: {AdminId}",
            relationshipId, adminUserId);
    }

    // ========================================================================
    // CA Invitation Management
    // ========================================================================

    public async Task<AdminCaInvitationListResponse> ListInvitationsAsync(AdminCaInvitationSearchParams searchParams)
    {
        var query = _dbContext.CaInvitations
            .Include(i => i.InviterUser)
            .Include(i => i.AcceptedUser)
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrEmpty(searchParams.Search))
        {
            var searchLower = searchParams.Search.ToLower();
            query = query.Where(i =>
                i.InviterUser.Name.ToLower().Contains(searchLower) ||
                (i.InviterUser.Email != null && i.InviterUser.Email.ToLower().Contains(searchLower)) ||
                i.InviteeEmail.ToLower().Contains(searchLower) ||
                i.Gstin.ToLower().Contains(searchLower));
        }

        // Apply CA filter
        if (searchParams.CaUserId.HasValue)
        {
            query = query.Where(i => i.InviterUserId == searchParams.CaUserId.Value);
        }

        // Apply status filter
        if (!string.IsNullOrEmpty(searchParams.Status))
        {
            query = query.Where(i => i.Status == searchParams.Status);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = searchParams.SortBy?.ToLower() switch
        {
            "inviteremail" => searchParams.SortDesc
                ? query.OrderByDescending(i => i.InviterUser.Email)
                : query.OrderBy(i => i.InviterUser.Email),
            "inviteeemail" => searchParams.SortDesc
                ? query.OrderByDescending(i => i.InviteeEmail)
                : query.OrderBy(i => i.InviteeEmail),
            "expiresat" => searchParams.SortDesc
                ? query.OrderByDescending(i => i.ExpiresAt)
                : query.OrderBy(i => i.ExpiresAt),
            _ => searchParams.SortDesc
                ? query.OrderByDescending(i => i.CreatedAt)
                : query.OrderBy(i => i.CreatedAt)
        };

        // Apply pagination
        var page = Math.Max(1, searchParams.Page);
        var pageSize = Math.Clamp(searchParams.PageSize, 1, 100);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var invitations = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Get CA firm names
        var caUserIds = invitations.Select(i => i.InviterUserId).Distinct().ToList();
        var caProfiles = await _dbContext.CaProfiles
            .Where(cp => caUserIds.Contains(cp.UserId))
            .ToDictionaryAsync(cp => cp.UserId, cp => cp.FirmName);

        var items = invitations.Select(i => new AdminCaInvitationListItemDto(
            Id: i.Id,
            InvitationType: i.InvitationType,
            CaUserId: i.InviterUserId,
            CaUserName: i.InviterUser.Name,
            CaUserEmail: i.InviterUser.Email ?? "",
            CaFirmName: caProfiles.GetValueOrDefault(i.InviterUserId),
            InviteeEmail: i.InviteeEmail,
            Gstin: i.Gstin,
            Status: i.Status,
            SendCount: i.SendCount,
            LastSentAt: i.LastSentAt,
            CreatedAt: i.CreatedAt,
            ExpiresAt: i.ExpiresAt,
            RespondedAt: i.RespondedAt,
            AcceptedUserId: i.AcceptedUserId,
            AcceptedUserName: i.AcceptedUser?.Name
        )).ToList();

        return new AdminCaInvitationListResponse(
            Invitations: items,
            Pagination: new AdminPaginationDto(page, pageSize, totalCount, totalPages)
        );
    }

    public async Task CancelInvitationAsync(Guid invitationId, Guid adminUserId, string reason)
    {
        var invitation = await _dbContext.CaInvitations
            .Include(i => i.InviterUser)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation == null)
        {
            throw new KeyNotFoundException($"Invitation not found: {invitationId}");
        }

        if (invitation.Status != CaInvitationStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot cancel invitation with status: {invitation.Status}");
        }

        invitation.Status = CaInvitationStatus.Cancelled;
        invitation.CancelledAt = DateTime.UtcNow;
        invitation.ResponseReason = $"[Admin] {reason}";
        invitation.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            adminUserId,
            "ca_invitation_cancelled",
            "CaInvitation",
            invitationId.ToString(),
            $"CA invitation cancelled: {invitation.InviterUser.Name} -> {invitation.InviteeEmail}",
            new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["ca_user_id"] = invitation.InviterUserId.ToString(),
                ["invitee_email"] = invitation.InviteeEmail,
                ["gstin"] = invitation.Gstin
            });

        _logger.LogInformation(
            "CA invitation cancelled: {InvitationId} by admin: {AdminId}",
            invitationId, adminUserId);
    }
}
