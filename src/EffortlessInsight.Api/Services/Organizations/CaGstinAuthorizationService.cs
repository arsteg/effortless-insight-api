using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

public record CaGstinAuthorizationResult(bool IsAllowed, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static readonly CaGstinAuthorizationResult Allowed = new(true);
}

/// <summary>
/// Enforces "one active CA per GSTIN at a time" - checked both when a CA creates
/// a client invitation and again whenever they stage/sync notices for a GSTIN,
/// closing the race where two CAs both start working the same client concurrently.
/// </summary>
public interface ICaGstinAuthorizationService
{
    /// <summary>
    /// SHA-256 hex hash of the normalized GSTIN. Deterministic stand-in used to
    /// index/query CaProspectClient.Gstin, which is otherwise non-deterministic
    /// AES-GCM ciphertext.
    /// </summary>
    string ComputeGstinHash(string gstin);

    /// <summary>
    /// Checks whether the given CA is allowed to claim/work the given GSTIN -
    /// i.e. no other CA already has an active membership or in-progress staging
    /// claim on it.
    /// </summary>
    Task<CaGstinAuthorizationResult> CheckAsync(string gstin, Guid requestingCaUserId, CancellationToken cancellationToken = default);
}

public class CaGstinAuthorizationService : ICaGstinAuthorizationService
{
    private readonly ApplicationDbContext _dbContext;

    public CaGstinAuthorizationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string ComputeGstinHash(string gstin)
    {
        var normalized = gstin.Trim().ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<CaGstinAuthorizationResult> CheckAsync(string gstin, Guid requestingCaUserId, CancellationToken cancellationToken = default)
    {
        var normalized = gstin.Trim().ToUpperInvariant();
        var gstinHash = ComputeGstinHash(normalized);

        // 1. Is this GSTIN already an existing organization's primary GSTIN, with
        // an active CA membership held by a *different* CA? Gstin is AES-GCM
        // encrypted with a random nonce, so - same as GstinValidatorService -
        // materialize and decrypt-compare in memory rather than a SQL WHERE.
        var primaryGstinOrgs = await _dbContext.OrganizationGstins
            .Where(g => g.IsPrimary && g.DeletedAt == null && g.Organization.DeletedAt == null)
            .Select(g => new { g.Gstin, g.OrganizationId })
            .ToListAsync(cancellationToken);

        var matchingOrgId = primaryGstinOrgs
            .Where(g => string.Equals(g.Gstin, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.OrganizationId)
            .FirstOrDefault();

        if (matchingOrgId != Guid.Empty)
        {
            var hasOtherActiveCa = await _dbContext.OrganizationMembers
                .AnyAsync(m => m.OrganizationId == matchingOrgId
                    && m.Role == "ca"
                    && m.Status == "active"
                    && m.UserId != requestingCaUserId
                    && (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow),
                    cancellationToken);

            if (hasOtherActiveCa)
            {
                return new CaGstinAuthorizationResult(false, "GSTIN_HAS_ACTIVE_CA",
                    "This GSTIN already has an active Chartered Accountant associated with it.");
            }
        }

        // 2. Is another CA already staging this GSTIN? GstinHash is deterministic,
        // so this check can run as a normal indexed query.
        var stagedByOtherCa = await _dbContext.CaProspectClients
            .AnyAsync(p => p.GstinHash == gstinHash
                && p.Status == "staging"
                && p.CaUserId != requestingCaUserId
                && p.DeletedAt == null,
                cancellationToken);

        if (stagedByOtherCa)
        {
            return new CaGstinAuthorizationResult(false, "GSTIN_HAS_ACTIVE_CA",
                "Another Chartered Accountant is already working with this GSTIN.");
        }

        return CaGstinAuthorizationResult.Allowed;
    }
}
