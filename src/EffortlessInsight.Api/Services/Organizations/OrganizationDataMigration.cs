using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Migrates existing organization data to the new multi-tenancy structure.
/// This should be run once after deploying the new schema.
/// </summary>
public interface IOrganizationDataMigrationService
{
    /// <summary>
    /// Migrates existing GSTIN data from Organization.Gstins JSONB to OrganizationGstins table.
    /// </summary>
    Task MigrateGstinsAsync();

    /// <summary>
    /// Creates OrganizationMember records for existing user-organization relationships.
    /// </summary>
    Task MigrateMembershipsAsync();

    /// <summary>
    /// Populates NameNormalized for existing organizations.
    /// </summary>
    Task MigrateNormalizedNamesAsync();
}

public class OrganizationDataMigrationService : IOrganizationDataMigrationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGstinValidatorService _gstinValidator;
    private readonly ILogger<OrganizationDataMigrationService> _logger;

    public OrganizationDataMigrationService(
        ApplicationDbContext dbContext,
        IGstinValidatorService gstinValidator,
        ILogger<OrganizationDataMigrationService> logger)
    {
        _dbContext = dbContext;
        _gstinValidator = gstinValidator;
        _logger = logger;
    }

    public async Task MigrateGstinsAsync()
    {
        _logger.LogInformation("Starting GSTIN migration...");

        // Get all organizations with legacy GSTINs that don't have migrated records
#pragma warning disable CS0618 // Type or member is obsolete
        var organizations = await _dbContext.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.Gstins != null && o.Gstins.Count > 0)
            .Include(o => o.OrganizationGstins)
            .ToListAsync();
#pragma warning restore CS0618

        var migratedCount = 0;
        var errorCount = 0;

        foreach (var org in organizations)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var legacyGstins = org.Gstins ?? [];
#pragma warning restore CS0618

            var existingGstins = org.OrganizationGstins.Select(g => g.Gstin).ToHashSet();
            var isFirst = !existingGstins.Any();

            foreach (var gstin in legacyGstins)
            {
                if (existingGstins.Contains(gstin))
                {
                    continue; // Already migrated
                }

                try
                {
                    var validation = _gstinValidator.Validate(gstin);
                    if (!validation.IsValid)
                    {
                        _logger.LogWarning("Skipping invalid GSTIN {Gstin} for organization {OrgId}: {Error}",
                            gstin, org.Id, validation.ErrorMessage);
                        errorCount++;
                        continue;
                    }

                    var stateName = await _gstinValidator.GetStateNameAsync(validation.StateCode!) ?? validation.StateName!;

                    var gstinEntity = new OrganizationGstin
                    {
                        OrganizationId = org.Id,
                        Gstin = validation.Gstin!,
                        StateCode = validation.StateCode!,
                        StateName = stateName,
                        IsPrimary = isFirst,
                        Status = "active"
                    };

                    _dbContext.OrganizationGstins.Add(gstinEntity);
                    isFirst = false;
                    migratedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to migrate GSTIN {Gstin} for organization {OrgId}", gstin, org.Id);
                    errorCount++;
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("GSTIN migration completed. Migrated: {Migrated}, Errors: {Errors}",
            migratedCount, errorCount);
    }

    public async Task MigrateMembershipsAsync()
    {
        _logger.LogInformation("Starting membership migration...");

        // Get all users with organization assignments that don't have membership records
        var usersWithOrgs = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.OrganizationId != null)
            .ToListAsync();

        var existingMemberships = await _dbContext.OrganizationMembers
            .Select(m => new { m.OrganizationId, m.UserId })
            .ToListAsync();

        var existingSet = existingMemberships
            .Select(m => $"{m.OrganizationId}:{m.UserId}")
            .ToHashSet();

        var newMemberships = new List<OrganizationMember>();

        foreach (var user in usersWithOrgs)
        {
            var key = $"{user.OrganizationId}:{user.Id}";
            if (existingSet.Contains(key))
            {
                continue; // Already has membership
            }

            newMemberships.Add(new OrganizationMember
            {
                OrganizationId = user.OrganizationId!.Value,
                UserId = user.Id,
                Role = user.Role,
                IsExternal = user.Role == "ca",
                Status = "active",
                JoinedAt = user.CreatedAt
            });
        }

        if (newMemberships.Count > 0)
        {
            _dbContext.OrganizationMembers.AddRange(newMemberships);
            await _dbContext.SaveChangesAsync();
        }

        _logger.LogInformation("Membership migration completed. Created {Count} memberships", newMemberships.Count);
    }

    public async Task MigrateNormalizedNamesAsync()
    {
        _logger.LogInformation("Starting name normalization migration...");

        var organizations = await _dbContext.Organizations
            .IgnoreQueryFilters()
            .Where(o => string.IsNullOrEmpty(o.NameNormalized))
            .ToListAsync();

        foreach (var org in organizations)
        {
            org.NameNormalized = org.Name.Trim().ToLowerInvariant();
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Name normalization completed. Updated {Count} organizations", organizations.Count);
    }
}
