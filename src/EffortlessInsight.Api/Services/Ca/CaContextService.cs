using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for managing CA client context.
///
/// Selecting a client mints a token whose org_id is the client's organization, so every
/// ordinary endpoint scopes to that client with no CA-specific code path. The CA carries
/// role "ca" and is_external true there, which is what <see cref="Organizations.CurrentOrganizationService"/>
/// reads to decide what they may do.
/// </summary>
public class CaContextService : ICaContextService
{
    private readonly ApplicationDbContext _db;
    private readonly IJwtService _jwtService;
    private readonly ILogger<CaContextService> _logger;

    public CaContextService(
        ApplicationDbContext db,
        IJwtService jwtService,
        ILogger<CaContextService> logger)
    {
        _db = db;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<SelectClientContextResult> SelectClientAsync(
        Guid caUserId,
        Guid relationshipId,
        string ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Validate relationship exists, belongs to this CA, and is live
        var relationship = await _db.CaClientRelationships
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .FirstOrDefaultAsync(r =>
                r.Id == relationshipId &&
                r.CaUserId == caUserId &&
                r.Status == CaClientRelationshipStatus.Active,
                ct);

        if (relationship == null)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "RELATIONSHIP_NOT_FOUND",
                ErrorMessage: "Client relationship not found or not active");
        }

        if (relationship.ExpiresAt != null && relationship.ExpiresAt <= DateTime.UtcNow)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "RELATIONSHIP_EXPIRED",
                ErrorMessage: "This engagement has ended");
        }

        if (relationship.OrganizationId == null || relationship.Organization == null)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "ORGANIZATION_NOT_SET",
                ErrorMessage: "Client has not set up their organization yet");
        }

        if (relationship.Organization.DeletedAt != null)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "ORGANIZATION_NOT_FOUND",
                ErrorMessage: "Client organization no longer exists");
        }

        var caUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == caUserId, ct);
        if (caUser == null)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "USER_NOT_FOUND",
                ErrorMessage: "CA user not found");
        }

        var caProfile = await _db.CaProfiles
            .FirstOrDefaultAsync(p => p.UserId == caUserId, ct);

        if (caProfile == null || caProfile.Status != CaProfileStatus.Active)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "CA_NOT_ACTIVE",
                ErrorMessage: "Your CA account is not active");
        }

        // Mint tokens scoped to the client's organization.
        //
        // Deliberately NOT mirroring SwitchOrganizationAsync's user mutation: writing
        // caUser.OrganizationId here would destroy the only pointer back to the CA's own
        // firm (CaProfile has no organization column, and CaProfileService refuses to
        // create a second one), stranding them permanently. The acting context is
        // session-scoped instead.
        var accessToken = _jwtService.GenerateAccessToken(
            caUser,
            relationship.Organization,
            roleOverride: "ca",
            isExternal: true,
            additionalClaims: [new Claim(CaClaimTypes.ClientRelationshipId, relationshipId.ToString())]);

        var (refreshToken, jti, refreshExpiresAt) = _jwtService.GenerateRefreshToken();

        _db.UserSessions.Add(new UserSession
        {
            UserId = caUser.Id,
            RefreshTokenHash = HashToken(refreshToken),
            RefreshTokenJti = jti,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Platform = "web",
            ExpiresAt = refreshExpiresAt,
            LastActiveAt = DateTime.UtcNow,
            OrganizationId = relationship.OrganizationId,
            Role = "ca",
            IsExternal = true,
            CaClientRelationshipId = relationshipId
        });

        await _db.SaveChangesAsync(ct);

        var context = await BuildContextAsync(caUser, relationship, ct);

        _logger.LogInformation("CA context selected: CA={CaUserId}, Client={ClientId}, Org={OrgId}",
            caUserId, relationship.ClientUserId, relationship.OrganizationId);

        return new SelectClientContextResult(
            Success: true,
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            Context: context);
    }

    public async Task<CaContextDto?> GetCurrentContextAsync(
        Guid caUserId,
        Guid? selectedRelationshipId = null,
        CancellationToken ct = default)
    {
        var caUser = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == caUserId && u.IsCa, ct);

        if (caUser == null)
            return null;

        if (selectedRelationshipId is { } relationshipId)
        {
            var relationship = await _db.CaClientRelationships
                .Include(r => r.ClientUser)
                .Include(r => r.Organization)
                .FirstOrDefaultAsync(r =>
                    r.Id == relationshipId &&
                    r.CaUserId == caUserId &&
                    r.Status == CaClientRelationshipStatus.Active,
                    ct);

            if (relationship != null)
            {
                return await BuildContextAsync(caUser, relationship, ct);
            }
        }

        // No client selected (or the selection is no longer valid)
        return new CaContextDto(
            CaUserId: caUserId,
            CaName: caUser.Name,
            SelectedClientRelationshipId: null,
            SelectedOrganizationId: null,
            SelectedClientName: null,
            SelectedOrganizationName: null,
            AuthorizedGstins: [],
            Permissions: [],
            ContextSetAt: null);
    }

    public async Task<bool> ValidateContextAsync(
        Guid caUserId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        return await _db.CaClientRelationships
            .AnyAsync(r =>
                r.CaUserId == caUserId &&
                r.OrganizationId == organizationId &&
                r.Status == CaClientRelationshipStatus.Active &&
                (r.ExpiresAt == null || r.ExpiresAt > DateTime.UtcNow),
                ct);
    }

    /// <summary>
    /// GSTIN authorizations are reported for display only. Access is granted at organization
    /// level, so this list tells the CA what the client recorded, not what is enforced.
    /// </summary>
    private async Task<CaContextDto> BuildContextAsync(
        ApplicationUser caUser,
        CaClientRelationship relationship,
        CancellationToken ct)
    {
        var authorizedGstins = await _db.CaGstinAuthorizations
            .Where(a =>
                a.CaClientRelationshipId == relationship.Id &&
                a.Status == CaGstinAuthorizationStatus.Active)
            .Select(a => a.Gstin)
            .ToListAsync(ct);

        return new CaContextDto(
            CaUserId: caUser.Id,
            CaName: caUser.Name,
            SelectedClientRelationshipId: relationship.Id,
            SelectedOrganizationId: relationship.OrganizationId,
            SelectedClientName: relationship.ClientUser.Name,
            SelectedOrganizationName: relationship.Organization?.Name,
            AuthorizedGstins: authorizedGstins,
            Permissions: [],
            ContextSetAt: DateTime.UtcNow);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
