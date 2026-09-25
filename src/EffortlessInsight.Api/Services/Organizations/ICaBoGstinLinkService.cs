using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Service for managing CA-BO GSTIN links that enable cross-organization notice visibility.
/// </summary>
/// <remarks>
/// When a CA syncs or uploads notices for a GSTIN from their own organization context,
/// this service ensures the appropriate CaBoGstinLink exists so the connected Business Owner
/// can see those notices through cross-org visibility.
/// </remarks>
public interface ICaBoGstinLinkService
{
    /// <summary>
    /// Ensures CaBoGstinLinks exist for all connected BO organizations that have the given GSTIN.
    /// This is called when a CA uploads/syncs notices from their own organization.
    /// </summary>
    /// <param name="caOrganizationId">The CA's organization ID (where the CA is owner).</param>
    /// <param name="caUserId">The CA user ID performing the action.</param>
    /// <param name="gstin">The GSTIN to link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of new links created.</returns>
    Task<int> EnsureLinksForGstinAsync(
        Guid caOrganizationId,
        Guid caUserId,
        string gstin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a CaBoGstinLink exists for the given GSTIN between specific CA and BO orgs.
    /// Creates one if it doesn't exist, returns existing if it does.
    /// </summary>
    /// <param name="caOrganizationId">The CA's organization ID.</param>
    /// <param name="boOrganizationId">The BO's organization ID.</param>
    /// <param name="gstin">The GSTIN to link.</param>
    /// <param name="caUserId">The CA user ID.</param>
    /// <param name="caMembershipId">The CA's membership ID in the BO's organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The CaBoGstinLink (existing or newly created), or null if prerequisites not met.</returns>
    Task<CaBoGstinLink?> EnsureLinkExistsAsync(
        Guid caOrganizationId,
        Guid boOrganizationId,
        string gstin,
        Guid caUserId,
        Guid caMembershipId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds BO organizations connected to the CA for a given GSTIN.
    /// A BO org is considered connected if the CA has an active membership (role="ca", IsExternal=true)
    /// in that BO's organization, and the BO's organization has the GSTIN registered.
    /// </summary>
    /// <param name="caUserId">The CA user ID.</param>
    /// <param name="gstin">The GSTIN to find connections for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of (BO organization ID, CA membership ID) tuples for connected BOs.</returns>
    Task<List<(Guid BoOrganizationId, Guid CaMembershipId)>> FindConnectedBoOrgsForGstinAsync(
        Guid caUserId,
        string gstin,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links all GSTINs that the CA has in their organization to an existing CA-BO connection.
    /// This is called when a BO accepts a CA invitation, to ensure all relevant GSTINs are linked.
    /// </summary>
    /// <param name="caOrganizationId">The CA's organization ID.</param>
    /// <param name="boOrganizationId">The BO's organization ID.</param>
    /// <param name="caUserId">The CA user ID.</param>
    /// <param name="caMembershipId">The CA's membership ID in the BO's organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of new links created.</returns>
    Task<int> LinkAllClientGstinsAsync(
        Guid caOrganizationId,
        Guid boOrganizationId,
        Guid caUserId,
        Guid caMembershipId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an active CaBoGstinLink for a given CA organization and GSTIN.
    /// Used to determine if auto-import should route GST-synced notices to a BO's organization.
    /// </summary>
    /// <param name="caOrganizationId">The CA's organization ID (where CA is owner).</param>
    /// <param name="gstin">The GSTIN to find a link for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active CaBoGstinLink if one exists, null otherwise.</returns>
    Task<CaBoGstinLink?> FindActiveLinkForGstinAsync(
        Guid caOrganizationId,
        string gstin,
        CancellationToken cancellationToken = default);
}
