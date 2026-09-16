using EffortlessInsight.Api.DTOs.Admin;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for admin management of CA profiles, relationships, and invitations.
/// </summary>
public interface IAdminCaService
{
    // ========================================================================
    // CA Profile Management
    // ========================================================================

    /// <summary>
    /// Search and list CA profiles.
    /// </summary>
    Task<AdminCaListResponse> ListCasAsync(AdminCaSearchParams searchParams);

    /// <summary>
    /// Get detailed CA profile by ID.
    /// </summary>
    Task<AdminCaProfileDetailDto?> GetCaAsync(Guid caProfileId);

    /// <summary>
    /// Verify a CA.
    /// </summary>
    Task VerifyCaAsync(Guid caProfileId, Guid adminUserId, AdminVerifyCaRequest request);

    /// <summary>
    /// Revoke CA verification.
    /// </summary>
    Task RevokeCaVerificationAsync(Guid caProfileId, Guid adminUserId, string? reason);

    /// <summary>
    /// Suspend a CA.
    /// </summary>
    Task SuspendCaAsync(Guid caProfileId, Guid adminUserId, AdminSuspendCaRequest request);

    /// <summary>
    /// Unsuspend a CA.
    /// </summary>
    Task UnsuspendCaAsync(Guid caProfileId, Guid adminUserId);

    // ========================================================================
    // CA Relationship Management
    // ========================================================================

    /// <summary>
    /// Search and list CA-client relationships.
    /// </summary>
    Task<AdminCaRelationshipListResponse> ListRelationshipsAsync(AdminCaRelationshipSearchParams searchParams);

    /// <summary>
    /// Get detailed CA-client relationship.
    /// </summary>
    Task<AdminCaRelationshipDetailDto?> GetRelationshipAsync(Guid relationshipId);

    /// <summary>
    /// Revoke a CA-client relationship (admin override).
    /// </summary>
    Task RevokeRelationshipAsync(Guid relationshipId, Guid adminUserId, string reason);

    // ========================================================================
    // CA Invitation Management
    // ========================================================================

    /// <summary>
    /// Search and list CA invitations.
    /// </summary>
    Task<AdminCaInvitationListResponse> ListInvitationsAsync(AdminCaInvitationSearchParams searchParams);

    /// <summary>
    /// Cancel a pending invitation (admin override).
    /// </summary>
    Task CancelInvitationAsync(Guid invitationId, Guid adminUserId, string reason);
}
