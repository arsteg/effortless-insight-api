using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for managing CA client context.
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
        CancellationToken ct = default)
    {
        // Validate relationship exists and is active
        var relationship = await _db.CaClientRelationships
            .Include(r => r.ClientUser)
            .Include(r => r.Organization)
            .Include(r => r.GstinAuthorizations)
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

        if (relationship.OrganizationId == null)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "ORGANIZATION_NOT_SET",
                ErrorMessage: "Client has not set up their organization yet");
        }

        // Get authorized GSTINs
        var authorizedGstins = relationship.GstinAuthorizations
            .Where(a => a.Status == CaGstinAuthorizationStatus.Active)
            .Select(a => a.Gstin)
            .ToList();

        // Get combined permissions
        var permissions = relationship.GstinAuthorizations
            .Where(a => a.Status == CaGstinAuthorizationStatus.Active)
            .SelectMany(a => a.Permissions)
            .Distinct()
            .ToList();

        // Get CA user for token generation
        var caUser = await _db.Users.FindAsync([caUserId], ct);
        if (caUser == null)
        {
            return new SelectClientContextResult(
                false,
                ErrorCode: "USER_NOT_FOUND",
                ErrorMessage: "CA user not found");
        }

        // Generate new tokens with client's organization context
        // The CA context (relationship, GSTINs, permissions) is returned in the response DTO
        // and should be stored by the frontend for subsequent requests
        var accessToken = _jwtService.GenerateAccessToken(caUser, relationship.Organization);
        var (refreshTokenValue, _, _) = _jwtService.GenerateRefreshToken();

        var context = new CaContextDto(
            CaUserId: caUserId,
            CaName: caUser.Name,
            SelectedClientRelationshipId: relationshipId,
            SelectedOrganizationId: relationship.OrganizationId,
            SelectedClientName: relationship.ClientUser.Name,
            SelectedOrganizationName: relationship.Organization?.Name,
            AuthorizedGstins: authorizedGstins,
            Permissions: permissions,
            ContextSetAt: DateTime.UtcNow
        );

        _logger.LogInformation("CA context selected: CA={CaUserId}, Client={ClientId}, Org={OrgId}",
            caUserId, relationship.ClientUserId, relationship.OrganizationId);

        return new SelectClientContextResult(
            Success: true,
            AccessToken: accessToken,
            RefreshToken: refreshTokenValue,
            Context: context
        );
    }

    public async Task<CaContextDto?> GetCurrentContextAsync(Guid caUserId, CancellationToken ct = default)
    {
        // Get CA user
        var caUser = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == caUserId && u.IsCa, ct);

        if (caUser == null)
            return null;

        // Return base context without selected client
        return new CaContextDto(
            CaUserId: caUserId,
            CaName: caUser.Name,
            SelectedClientRelationshipId: null,
            SelectedOrganizationId: null,
            SelectedClientName: null,
            SelectedOrganizationName: null,
            AuthorizedGstins: [],
            Permissions: [],
            ContextSetAt: null
        );
    }

    public Task ClearContextAsync(Guid caUserId, CancellationToken ct = default)
    {
        // Context is stored in JWT, clearing means client should discard token
        // and request a new one without CA context
        _logger.LogInformation("CA context cleared: {CaUserId}", caUserId);
        return Task.CompletedTask;
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
                r.Status == CaClientRelationshipStatus.Active,
                ct);
    }
}
