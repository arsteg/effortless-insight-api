using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EffortlessInsight.Api.Services.Organizations;

public interface IOrganizationManagementService
{
    // Organization CRUD
    Task<CreateOrganizationResponse> CreateAsync(CreateOrganizationRequest request, Guid userId);
    Task<OrganizationDetailResponse> GetByIdAsync(Guid organizationId, Guid userId);
    Task<OrganizationListResponse> GetUserOrganizationsAsync(Guid userId);
    Task<OrganizationDetailResponse> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, Guid userId);
    Task DeleteAsync(Guid organizationId, DeleteOrganizationRequest request, Guid userId);

    // GSTIN Management
    Task<GstinDto> AddGstinAsync(Guid organizationId, AddGstinRequest request, Guid userId);
    Task RemoveGstinAsync(Guid organizationId, Guid gstinId, Guid userId);
    Task SetPrimaryGstinAsync(Guid organizationId, Guid gstinId, Guid userId);

    // Member Management
    Task<MemberListResponse> GetMembersAsync(Guid organizationId, Guid userId, string? role = null, string? status = null, int page = 1, int limit = 20);
    Task<ChangeMemberRoleResponse> ChangeMemberRoleAsync(Guid organizationId, Guid memberId, ChangeMemberRoleRequest request, Guid userId);
    Task RemoveMemberAsync(Guid organizationId, Guid memberId, Guid userId);
    Task LeaveOrganizationAsync(Guid organizationId, Guid userId);
    Task<TransferOwnershipResponse> TransferOwnershipAsync(Guid organizationId, TransferOwnershipRequest request, Guid userId);

    // Invitation Management
    Task<InvitationDto> InviteMemberAsync(Guid organizationId, InviteMemberRequest request, Guid userId);
    Task<InvitationListResponse> GetInvitationsAsync(Guid organizationId, Guid userId);
    Task<ResendInvitationResponse> ResendInvitationAsync(Guid organizationId, Guid invitationId, Guid userId);
    Task CancelInvitationAsync(Guid organizationId, Guid invitationId, Guid userId);
    Task<AcceptInvitationResponse> AcceptInvitationAsync(string token, Guid userId);
    Task DeclineInvitationAsync(string token, Guid userId);

    // Settings
    Task<OrganizationSettingsDto> UpdateSettingsAsync(Guid organizationId, UpdateOrganizationSettingsRequest request, Guid userId);

    // Organization Switching
    Task<SwitchOrganizationResponse> SwitchOrganizationAsync(SwitchOrganizationRequest request, Guid userId, string ipAddress, string userAgent);
}

public class OrganizationManagementService : IOrganizationManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGstinValidatorService _gstinValidator;
    private readonly ICurrentOrganizationService _currentOrg;
    private readonly IJwtService _jwtService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IAuditService _auditService;
    private readonly ILogger<OrganizationManagementService> _logger;

    private const int InvitationExpiryDays = 7;
    private const int MaxInvitationResends = 3;
    private const int DefaultMaxMembers = 1; // Default limit when no plan

    public OrganizationManagementService(
        ApplicationDbContext dbContext,
        IGstinValidatorService gstinValidator,
        ICurrentOrganizationService currentOrg,
        IJwtService jwtService,
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IAuditService auditService,
        ILogger<OrganizationManagementService> logger)
    {
        _dbContext = dbContext;
        _gstinValidator = gstinValidator;
        _currentOrg = currentOrg;
        _jwtService = jwtService;
        _userManager = userManager;
        _emailService = emailService;
        _auditService = auditService;
        _logger = logger;
    }

    #region Organization CRUD

    public async Task<CreateOrganizationResponse> CreateAsync(CreateOrganizationRequest request, Guid userId)
    {
        // Validate GSTIN
        var gstinResult = _gstinValidator.Validate(request.Gstin);
        if (!gstinResult.IsValid)
        {
            throw new InvalidOperationException($"INVALID_GSTIN: {gstinResult.ErrorMessage}");
        }

        // Check if GSTIN already exists
        if (await _gstinValidator.ExistsAsync(request.Gstin))
        {
            throw new InvalidOperationException("GSTIN_EXISTS");
        }

        // Check if organization name already exists
        var normalizedName = request.Name.Trim().ToLowerInvariant();
        if (await _dbContext.Organizations.AnyAsync(o => o.NameNormalized == normalizedName && o.DeletedAt == null))
        {
            throw new InvalidOperationException("ORG_NAME_EXISTS");
        }

        // Get user
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        // Get default free plan
        var freePlan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.Code == "free" && p.IsActive);

        // Get state name from database
        var stateName = await _gstinValidator.GetStateNameAsync(gstinResult.StateCode!) ?? gstinResult.StateName!;

        // Create GSTIN entity
        var gstin = new OrganizationGstin
        {
            Gstin = gstinResult.Gstin!,
            StateCode = gstinResult.StateCode!,
            StateName = stateName,
            IsPrimary = true,
            Status = "active"
        };

        // Create owner membership entity
        var membership = new OrganizationMember
        {
            UserId = userId,
            Role = "owner",
            IsExternal = false,
            Status = "active",
            JoinedAt = DateTime.UtcNow
        };

        // Create organization with related entities using navigation properties
        // EF Core will automatically set the OrganizationId on related entities
        var organization = new Organization
        {
            Name = request.Name.Trim(),
            NameNormalized = normalizedName,
            LegalName = request.LegalName?.Trim(),
            Industry = request.Industry,
            State = request.State,
            City = request.City,
            AnnualTurnoverRange = request.AnnualTurnoverRange,
            PlanId = freePlan?.Id,
            SubscriptionStatus = "trial",
            TrialEndsAt = DateTime.UtcNow.AddDays(14),
            Settings = new Dictionary<string, object>
            {
                ["default_reminder_days"] = new[] { 7, 3, 1 },
                ["notification_email"] = true,
                ["notification_sms"] = true,
                ["allow_ca_access"] = true,
                ["require_response_approval"] = false,
                ["timezone"] = "Asia/Kolkata",
                ["language"] = "en",
                ["date_format"] = "DD/MM/YYYY"
            },
            // Add related entities via navigation properties
            OrganizationGstins = { gstin },
            Members = { membership }
        };

        _dbContext.Organizations.Add(organization);

        // Save to get the organization ID
        await _dbContext.SaveChangesAsync();

        // Now update user's default organization (organization.Id is now set)
        user.OrganizationId = organization.Id;
        user.Role = "owner";
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Organization {OrganizationId} created by user {UserId}", organization.Id, userId);

        // Audit logging (C2 fix) - outside transaction
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "organization.created",
            EntityType = "Organization",
            EntityId = organization.Id,
            UserId = userId,
            OrganizationId = organization.Id,
            NewValues = new
            {
                organization.Name,
                organization.LegalName,
                organization.Industry,
                organization.State,
                Gstin = gstin.Gstin
            }
        });

        // Generate new access token with organization context
        var accessToken = _jwtService.GenerateAccessToken(user, organization, "owner");
        var expiresIn = _jwtService.GetAccessTokenExpiryMinutes() * 60;

        return new CreateOrganizationResponse(
            Id: organization.Id,
            Name: organization.Name,
            LegalName: organization.LegalName,
            Gstins: [new GstinDto(
                gstin.Id,
                gstin.Gstin,
                gstin.TradeName,
                gstin.StateCode,
                gstin.StateName,
                gstin.Status,
                gstin.IsPrimary,
                gstin.IsVerified,
                gstin.VerifiedAt
            )],
            Industry: organization.Industry,
            State: organization.State,
            City: organization.City,
            SubscriptionStatus: organization.SubscriptionStatus,
            TrialEndsAt: organization.TrialEndsAt,
            MemberCount: 1,
            CurrentUserRole: "owner",
            CreatedAt: organization.CreatedAt,
            AccessToken: accessToken,
            ExpiresIn: expiresIn
        );
    }

    public async Task<OrganizationDetailResponse> GetByIdAsync(Guid organizationId, Guid userId)
    {
        // Validate membership
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Status == "active")
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        var organization = await _dbContext.Organizations
            .Include(o => o.OrganizationGstins)
            .Include(o => o.Plan)
            .Include(o => o.Members.Where(m => m.Status == "active"))
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null)
            ?? throw new KeyNotFoundException("ORGANIZATION_NOT_FOUND");

        // Calculate usage
        var noticesThisMonth = await _dbContext.Notices
            .CountAsync(n => n.OrganizationId == organizationId &&
                n.CreatedAt >= new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc));

        return MapToDetailResponse(organization, membership.Role, noticesThisMonth);
    }

    public async Task<OrganizationListResponse> GetUserOrganizationsAsync(Guid userId)
    {
        var memberships = await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
                .ThenInclude(o => o.OrganizationGstins)
            .Include(m => m.Organization)
                .ThenInclude(o => o.Members.Where(mem => mem.Status == "active"))
            .Where(m =>
                m.UserId == userId &&
                m.Status == "active" &&
                m.Organization.DeletedAt == null &&
                (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow))
            .ToListAsync();

        var orgIds = memberships.Select(m => m.OrganizationId).ToList();

        // Get notice counts
        var noticeCounts = await _dbContext.Notices
            .Where(n => orgIds.Contains(n.OrganizationId))
            .GroupBy(n => n.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Total = g.Count(), Pending = g.Count(n => n.Status == "pending") })
            .ToDictionaryAsync(x => x.OrganizationId, x => (x.Total, x.Pending));

        var items = memberships.Select(m =>
        {
            noticeCounts.TryGetValue(m.OrganizationId, out var counts);
            return new OrganizationListItemDto(
                Id: m.OrganizationId,
                Name: m.Organization.Name,
                LogoUrl: m.Organization.LogoUrl,
                Role: m.Role,
                IsExternal: m.IsExternal,
                NoticeCount: counts.Total,
                PendingNoticeCount: counts.Pending,
                MemberCount: m.Organization.Members.Count,
                GstinCount: m.Organization.OrganizationGstins.Count,
                SubscriptionStatus: m.Organization.SubscriptionStatus
            );
        }).ToList();

        return new OrganizationListResponse(items, items.Count);
    }

    public async Task<OrganizationDetailResponse> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, Guid userId)
    {
        // Validate membership and permissions
        var membership = await ValidateAdminAccessAsync(organizationId, userId);

        var organization = await _dbContext.Organizations
            .Include(o => o.OrganizationGstins)
            .Include(o => o.Plan)
            .Include(o => o.Members.Where(m => m.Status == "active"))
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null)
            ?? throw new KeyNotFoundException("ORGANIZATION_NOT_FOUND");

        // Check unique name if changing
        if (!string.IsNullOrEmpty(request.Name) && request.Name.Trim().ToLowerInvariant() != organization.NameNormalized)
        {
            var normalizedName = request.Name.Trim().ToLowerInvariant();
            if (await _dbContext.Organizations.AnyAsync(o => o.NameNormalized == normalizedName && o.Id != organizationId && o.DeletedAt == null))
            {
                throw new InvalidOperationException("ORG_NAME_EXISTS");
            }
            organization.Name = request.Name.Trim();
            organization.NameNormalized = normalizedName;
        }

        // Update fields
        if (request.LegalName != null) organization.LegalName = request.LegalName.Trim();
        if (request.DisplayName != null) organization.DisplayName = request.DisplayName.Trim();
        if (request.Industry != null) organization.Industry = request.Industry;
        if (request.SubIndustry != null) organization.SubIndustry = request.SubIndustry;
        if (request.BusinessType != null) organization.BusinessType = request.BusinessType;
        if (request.AnnualTurnoverRange != null) organization.AnnualTurnoverRange = request.AnnualTurnoverRange;
        if (request.EmployeeCountRange != null) organization.EmployeeCountRange = request.EmployeeCountRange;
        if (request.Email != null) organization.Email = request.Email;
        if (request.Phone != null) organization.Phone = request.Phone;
        if (request.Website != null) organization.Website = request.Website;
        if (request.Pan != null) organization.Pan = request.Pan.ToUpperInvariant();
        if (request.Tan != null) organization.Tan = request.Tan.ToUpperInvariant();

        if (request.Address != null)
        {
            organization.AddressLine1 = request.Address.Line1;
            organization.AddressLine2 = request.Address.Line2;
            organization.City = request.Address.City;
            organization.State = request.Address.State;
            organization.PinCode = request.Address.PinCode;
            organization.Country = request.Address.Country ?? "India";
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Organization {OrganizationId} updated by user {UserId}", organizationId, userId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "organization.updated",
            EntityType = "Organization",
            EntityId = organizationId,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new
            {
                organization.Name,
                organization.LegalName,
                organization.DisplayName,
                organization.Industry,
                organization.Email,
                organization.Phone
            }
        });

        var noticesThisMonth = await _dbContext.Notices
            .CountAsync(n => n.OrganizationId == organizationId &&
                n.CreatedAt >= new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc));

        return MapToDetailResponse(organization, membership.Role, noticesThisMonth);
    }

    public async Task DeleteAsync(Guid organizationId, DeleteOrganizationRequest request, Guid userId)
    {
        // Only owner can delete
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Role == "owner" && m.Status == "active")
            ?? throw new UnauthorizedAccessException("OWNER_ONLY");

        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null)
            ?? throw new KeyNotFoundException("ORGANIZATION_NOT_FOUND");

        // Verify confirmation matches organization name
        if (!string.Equals(request.Confirmation.Trim(), organization.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CONFIRMATION_MISMATCH");
        }

        // Verify password
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            throw new UnauthorizedAccessException("INVALID_PASSWORD");
        }

        // Soft delete organization
        organization.DeletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Organization {OrganizationId} soft deleted by owner {UserId}", organizationId, userId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "organization.deleted",
            EntityType = "Organization",
            EntityId = organizationId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { organization.Name, organization.LegalName },
            NewValues = new { DeletedAt = organization.DeletedAt }
        });
    }

    #endregion

    #region GSTIN Management

    public async Task<GstinDto> AddGstinAsync(Guid organizationId, AddGstinRequest request, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        // Validate GSTIN
        var gstinResult = _gstinValidator.Validate(request.Gstin);
        if (!gstinResult.IsValid)
        {
            throw new InvalidOperationException($"INVALID_GSTIN: {gstinResult.ErrorMessage}");
        }

        // Check if GSTIN already exists
        if (await _gstinValidator.ExistsAsync(request.Gstin))
        {
            throw new InvalidOperationException("GSTIN_EXISTS");
        }

        // Check plan limits
        var organization = await _dbContext.Organizations
            .Include(o => o.Plan)
            .Include(o => o.OrganizationGstins)
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null)
            ?? throw new KeyNotFoundException("ORGANIZATION_NOT_FOUND");

        if (organization.Plan?.GstinLimit != null && organization.OrganizationGstins.Count >= organization.Plan.GstinLimit)
        {
            throw new InvalidOperationException($"GSTIN_LIMIT_EXCEEDED: Maximum {organization.Plan.GstinLimit} GSTINs allowed on your plan");
        }

        // Get state name
        var stateName = await _gstinValidator.GetStateNameAsync(gstinResult.StateCode!) ?? gstinResult.StateName!;

        // Handle primary flag
        if (request.IsPrimary)
        {
            // Clear existing primary
            var existingPrimary = organization.OrganizationGstins.FirstOrDefault(g => g.IsPrimary);
            if (existingPrimary != null)
            {
                existingPrimary.IsPrimary = false;
            }
        }

        var gstin = new OrganizationGstin
        {
            OrganizationId = organizationId,
            Gstin = gstinResult.Gstin!,
            TradeName = request.TradeName?.Trim(),
            StateCode = gstinResult.StateCode!,
            StateName = stateName,
            IsPrimary = request.IsPrimary || !organization.OrganizationGstins.Any(),
            Status = "active"
        };

        _dbContext.OrganizationGstins.Add(gstin);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("GSTIN {Gstin} added to organization {OrganizationId} by user {UserId}",
            gstin.Gstin, organizationId, userId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "gstin.added",
            EntityType = "OrganizationGstin",
            EntityId = gstin.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new
            {
                gstin.Gstin,
                gstin.TradeName,
                gstin.StateCode,
                gstin.IsPrimary
            }
        });

        return new GstinDto(
            gstin.Id,
            gstin.Gstin,
            gstin.TradeName,
            gstin.StateCode,
            gstin.StateName,
            gstin.Status,
            gstin.IsPrimary,
            gstin.IsVerified,
            gstin.VerifiedAt
        );
    }

    public async Task RemoveGstinAsync(Guid organizationId, Guid gstinId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var gstin = await _dbContext.OrganizationGstins
            .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("GSTIN_NOT_FOUND");

        if (gstin.IsPrimary)
        {
            throw new InvalidOperationException("CANNOT_DELETE_PRIMARY");
        }

        // Check if GSTIN has associated notices
        var hasNotices = await _dbContext.Notices
            .AnyAsync(n => n.OrganizationId == organizationId && n.Gstin == gstin.Gstin && n.DeletedAt == null);

        if (hasNotices)
        {
            throw new InvalidOperationException("GSTIN_HAS_NOTICES");
        }

        var removedGstin = gstin.Gstin; // Capture before removal
        _dbContext.OrganizationGstins.Remove(gstin);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("GSTIN {Gstin} removed from organization {OrganizationId} by user {UserId}",
            removedGstin, organizationId, userId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "gstin.removed",
            EntityType = "OrganizationGstin",
            EntityId = gstinId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new
            {
                Gstin = removedGstin,
                gstin.TradeName,
                gstin.StateCode
            }
        });
    }

    public async Task SetPrimaryGstinAsync(Guid organizationId, Guid gstinId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        // Verify GSTIN exists before starting transaction
        var targetGstin = await _dbContext.OrganizationGstins
            .FirstOrDefaultAsync(g => g.Id == gstinId && g.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("GSTIN_NOT_FOUND");

        // Use transaction with serializable isolation to prevent race conditions (C1 fix)
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Get previous primary for audit logging
            var previousPrimary = await _dbContext.OrganizationGstins
                .Where(g => g.OrganizationId == organizationId && g.IsPrimary)
                .Select(g => g.Gstin)
                .FirstOrDefaultAsync();

            // Use direct SQL UPDATE to avoid race condition - clear all primary flags
            await _dbContext.OrganizationGstins
                .Where(g => g.OrganizationId == organizationId && g.IsPrimary)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsPrimary, false));

            // Set new primary
            await _dbContext.OrganizationGstins
                .Where(g => g.Id == gstinId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.IsPrimary, true));

            await transaction.CommitAsync();

            _logger.LogInformation("GSTIN {Gstin} set as primary for organization {OrganizationId} by user {UserId}",
                targetGstin.Gstin, organizationId, userId);

            // Audit logging
            await _auditService.LogAsync(new AuditLogEntry
            {
                Action = "gstin.primary_changed",
                EntityType = "OrganizationGstin",
                EntityId = gstinId,
                UserId = userId,
                OrganizationId = organizationId,
                OldValues = new { PrimaryGstin = previousPrimary },
                NewValues = new { PrimaryGstin = targetGstin.Gstin }
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    #endregion

    #region Member Management

    public async Task<MemberListResponse> GetMembersAsync(Guid organizationId, Guid userId, string? role = null, string? status = null, int page = 1, int limit = 20)
    {
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Status == "active")
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        // CA cannot view members
        if (membership.Role == "ca")
        {
            throw new UnauthorizedAccessException("CA_CANNOT_VIEW_MEMBERS");
        }

        var query = _dbContext.OrganizationMembers
            .Include(m => m.User)
            .Where(m => m.OrganizationId == organizationId);

        if (!string.IsNullOrEmpty(role))
        {
            query = query.Where(m => m.Role == role.ToLowerInvariant());
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(m => m.Status == status.ToLowerInvariant());
        }
        else
        {
            query = query.Where(m => m.Status == "active");
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)total / limit);

        var members = await query
            .OrderBy(m => m.Role == "owner" ? 0 : m.Role == "admin" ? 1 : 2)
            .ThenBy(m => m.JoinedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(m => new MemberDto(
                m.Id,
                new MemberUserDto(m.User.Id, m.User.Name, m.User.Email!, m.User.AvatarUrl),
                m.Role,
                m.IsExternal,
                m.Status,
                m.AccessExpiresAt,
                m.ClientReference,
                m.JoinedAt,
                m.LastActiveAt
            ))
            .ToListAsync();

        return new MemberListResponse(members, total, page, limit, totalPages);
    }

    public async Task<ChangeMemberRoleResponse> ChangeMemberRoleAsync(Guid organizationId, Guid memberId, ChangeMemberRoleRequest request, Guid userId)
    {
        var actorMembership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Status == "active")
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        var targetMembership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("MEMBER_NOT_FOUND");

        var newRole = request.Role.ToLowerInvariant();

        // Cannot change owner's role
        if (targetMembership.Role == "owner")
        {
            throw new InvalidOperationException("CANNOT_CHANGE_OWNER_ROLE");
        }

        // Admin can only modify lower roles
        if (actorMembership.Role == "admin")
        {
            if (targetMembership.Role == "admin")
            {
                throw new UnauthorizedAccessException("ADMIN_CANNOT_MODIFY_ADMIN");
            }
            if (newRole == "admin")
            {
                throw new UnauthorizedAccessException("ADMIN_CANNOT_PROMOTE_TO_ADMIN");
            }
        }

        // Only owner can manage admins
        if (actorMembership.Role != "owner" && (newRole == "admin" || targetMembership.Role == "admin"))
        {
            throw new UnauthorizedAccessException("OWNER_ONLY");
        }

        var previousRole = targetMembership.Role;
        targetMembership.Role = newRole;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Member {MemberId} role changed from {OldRole} to {NewRole} in organization {OrganizationId} by user {UserId}",
            memberId, previousRole, newRole, organizationId, userId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "member.role_changed",
            EntityType = "OrganizationMember",
            EntityId = memberId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { Role = previousRole },
            NewValues = new { Role = newRole },
            Metadata = new Dictionary<string, object>
            {
                ["target_user_id"] = targetMembership.UserId.ToString()
            }
        });

        return new ChangeMemberRoleResponse(memberId, previousRole, newRole);
    }

    public async Task RemoveMemberAsync(Guid organizationId, Guid memberId, Guid userId)
    {
        var actorMembership = await ValidateAdminAccessAsync(organizationId, userId);

        var targetMembership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("MEMBER_NOT_FOUND");

        // Cannot remove owner
        if (targetMembership.Role == "owner")
        {
            throw new InvalidOperationException("CANNOT_REMOVE_OWNER");
        }

        // Admin cannot remove other admins
        if (actorMembership.Role == "admin" && targetMembership.Role == "admin")
        {
            throw new UnauthorizedAccessException("ADMIN_CANNOT_REMOVE_ADMIN");
        }

        var removedUserId = targetMembership.UserId;
        var removedRole = targetMembership.Role;
        _dbContext.OrganizationMembers.Remove(targetMembership);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Member {MemberId} removed from organization {OrganizationId} by user {UserId}",
            memberId, organizationId, userId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "member.removed",
            EntityType = "OrganizationMember",
            EntityId = memberId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { Role = removedRole },
            Metadata = new Dictionary<string, object>
            {
                ["removed_user_id"] = removedUserId.ToString()
            }
        });
    }

    public async Task LeaveOrganizationAsync(Guid organizationId, Guid userId)
    {
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Status == "active")
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        if (membership.Role == "owner")
        {
            throw new InvalidOperationException("OWNER_CANNOT_LEAVE");
        }

        _dbContext.OrganizationMembers.Remove(membership);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {UserId} left organization {OrganizationId}", userId, organizationId);
    }

    public async Task<TransferOwnershipResponse> TransferOwnershipAsync(Guid organizationId, TransferOwnershipRequest request, Guid userId)
    {
        var ownerMembership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.Role == "owner" && m.Status == "active")
            ?? throw new UnauthorizedAccessException("OWNER_ONLY");

        var newOwnerMembership = await _dbContext.OrganizationMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == request.NewOwnerId && m.Status == "active")
            ?? throw new KeyNotFoundException("NEW_OWNER_NOT_MEMBER");

        // Verify password
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            throw new UnauthorizedAccessException("INVALID_PASSWORD");
        }

        // Transfer ownership
        ownerMembership.Role = "admin";
        newOwnerMembership.Role = "owner";

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Ownership of organization {OrganizationId} transferred from {OldOwnerId} to {NewOwnerId}",
            organizationId, userId, request.NewOwnerId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "ownership.transferred",
            EntityType = "Organization",
            EntityId = organizationId,
            UserId = userId,
            OrganizationId = organizationId,
            OldValues = new { OwnerId = userId },
            NewValues = new { OwnerId = request.NewOwnerId },
            Metadata = new Dictionary<string, object>
            {
                ["previous_owner_new_role"] = "admin",
                ["new_owner_name"] = newOwnerMembership.User.Name
            }
        });

        return new TransferOwnershipResponse(
            Message: "Ownership transferred successfully",
            NewOwner: new MemberUserDto(
                newOwnerMembership.User.Id,
                newOwnerMembership.User.Name,
                newOwnerMembership.User.Email!,
                newOwnerMembership.User.AvatarUrl
            ),
            YourNewRole: "admin"
        );
    }

    #endregion

    #region Invitation Management

    public async Task<InvitationDto> InviteMemberAsync(Guid organizationId, InviteMemberRequest request, Guid userId)
    {
        var actorMembership = await ValidateAdminAccessAsync(organizationId, userId);
        var actor = await _userManager.FindByIdAsync(userId.ToString());

        var organization = await _dbContext.Organizations
            .Include(o => o.Plan)
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null)
            ?? throw new KeyNotFoundException("ORGANIZATION_NOT_FOUND");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var role = request.Role.ToLowerInvariant();

        // Check if user is already a member
        var existingMember = await _dbContext.OrganizationMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.User.NormalizedEmail == normalizedEmail.ToUpperInvariant());

        if (existingMember != null)
        {
            throw new InvalidOperationException("USER_ALREADY_MEMBER");
        }

        // Check for pending invitation
        var existingInvitation = await _dbContext.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.OrganizationId == organizationId && i.EmailNormalized == normalizedEmail && i.Status == "pending");

        if (existingInvitation != null)
        {
            throw new InvalidOperationException("INVITATION_PENDING");
        }

        // Check plan limits with atomic database count (C4 fix)
        // Use database count instead of loaded collection to prevent race condition
        var currentMemberCount = await _dbContext.OrganizationMembers
            .CountAsync(m => m.OrganizationId == organizationId && m.Status == "active");

        var pendingInvitationCount = await _dbContext.OrganizationInvitations
            .CountAsync(i => i.OrganizationId == organizationId && i.Status == "pending");

        var effectiveCount = currentMemberCount + pendingInvitationCount;
        var maxMembers = organization.Plan?.UserLimit ?? DefaultMaxMembers;

        if (effectiveCount >= maxMembers)
        {
            throw new InvalidOperationException($"USER_LIMIT_EXCEEDED: Maximum {maxMembers} users allowed on your plan (including pending invitations)");
        }

        // Admin cannot invite admin or owner
        if (actorMembership.Role == "admin" && role is "admin" or "owner")
        {
            throw new UnauthorizedAccessException("CANNOT_INVITE_HIGHER_ROLE");
        }

        // Generate secure token
        var token = GenerateSecureToken();
        var tokenHash = HashToken(token);

        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId,
            InvitedById = userId,
            Email = request.Email.Trim(),
            EmailNormalized = normalizedEmail,
            Role = role,
            IsExternal = request.IsExternal,
            AccessDurationDays = request.AccessDurationDays,
            TokenHash = tokenHash,
            Status = "pending",
            ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays),
            Message = request.Message
        };

        _dbContext.OrganizationInvitations.Add(invitation);
        await _dbContext.SaveChangesAsync();

        // Send invitation email
        try
        {
            await _emailService.SendTemplateAsync(request.Email, "org_invitation", new Dictionary<string, object>
            {
                ["organization_name"] = organization.Name,
                ["inviter_name"] = actor?.Name ?? "A team member",
                ["role"] = role,
                ["invitation_url"] = $"/invitations/{token}",
                ["expires_in_days"] = InvitationExpiryDays,
                ["message"] = request.Message ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send invitation email to {Email}", request.Email);
        }

        _logger.LogInformation("Invitation sent to {Email} for organization {OrganizationId} by user {UserId}",
            request.Email, organizationId, userId);

        // Audit logging
        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "invitation.sent",
            EntityType = "OrganizationInvitation",
            EntityId = invitation.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new
            {
                invitation.Email,
                invitation.Role,
                invitation.IsExternal,
                invitation.ExpiresAt
            }
        });

        return new InvitationDto(
            invitation.Id,
            invitation.Email,
            invitation.Role,
            invitation.IsExternal,
            invitation.Status,
            new InvitedByDto(userId, actor?.Name ?? "Unknown"),
            invitation.ExpiresAt,
            invitation.LastSentAt,
            invitation.SendCount,
            invitation.CreatedAt
        );
    }

    public async Task<InvitationListResponse> GetInvitationsAsync(Guid organizationId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var invitations = await _dbContext.OrganizationInvitations
            .Include(i => i.InvitedBy)
            .Where(i => i.OrganizationId == organizationId && i.Status == "pending")
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationDto(
                i.Id,
                i.Email,
                i.Role,
                i.IsExternal,
                i.Status,
                new InvitedByDto(i.InvitedBy.Id, i.InvitedBy.Name),
                i.ExpiresAt,
                i.LastSentAt,
                i.SendCount,
                i.CreatedAt
            ))
            .ToListAsync();

        return new InvitationListResponse(invitations, invitations.Count);
    }

    public async Task<ResendInvitationResponse> ResendInvitationAsync(Guid organizationId, Guid invitationId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var invitation = await _dbContext.OrganizationInvitations
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.OrganizationId == organizationId && i.Status == "pending")
            ?? throw new KeyNotFoundException("INVITATION_NOT_FOUND");

        if (invitation.SendCount >= MaxInvitationResends)
        {
            throw new InvalidOperationException("MAX_RESENDS_EXCEEDED");
        }

        var actor = await _userManager.FindByIdAsync(userId.ToString());

        // Generate new token
        var token = GenerateSecureToken();
        invitation.TokenHash = HashToken(token);
        invitation.SendCount++;
        invitation.LastSentAt = DateTime.UtcNow;
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays);

        await _dbContext.SaveChangesAsync();

        // Send invitation email
        try
        {
            await _emailService.SendTemplateAsync(invitation.Email, "org_invitation", new Dictionary<string, object>
            {
                ["organization_name"] = invitation.Organization.Name,
                ["inviter_name"] = actor?.Name ?? "A team member",
                ["role"] = invitation.Role,
                ["invitation_url"] = $"/invitations/{token}",
                ["expires_in_days"] = InvitationExpiryDays,
                ["message"] = invitation.Message ?? ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resend invitation email to {Email}", invitation.Email);
        }

        return new ResendInvitationResponse("Invitation resent", invitation.SendCount, invitation.ExpiresAt);
    }

    public async Task CancelInvitationAsync(Guid organizationId, Guid invitationId, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var invitation = await _dbContext.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.OrganizationId == organizationId && i.Status == "pending")
            ?? throw new KeyNotFoundException("INVITATION_NOT_FOUND");

        invitation.Status = "cancelled";
        invitation.RespondedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Invitation {InvitationId} cancelled by user {UserId}", invitationId, userId);
    }

    public async Task<AcceptInvitationResponse> AcceptInvitationAsync(string token, Guid userId)
    {
        var tokenHash = HashToken(token);

        var invitation = await _dbContext.OrganizationInvitations
            .Include(i => i.Organization)
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

        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        // Verify email matches
        if (!string.Equals(user.NormalizedEmail, invitation.EmailNormalized.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EMAIL_MISMATCH");
        }

        // Check if already a member
        if (await _dbContext.OrganizationMembers.AnyAsync(m => m.OrganizationId == invitation.OrganizationId && m.UserId == userId))
        {
            throw new InvalidOperationException("ALREADY_MEMBER");
        }

        // Use transaction to ensure atomic plan limit check and membership creation (C4 fix)
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Re-check plan limits atomically within transaction
            var organization = await _dbContext.Organizations
                .Include(o => o.Plan)
                .FirstOrDefaultAsync(o => o.Id == invitation.OrganizationId);

            if (organization != null)
            {
                var currentMemberCount = await _dbContext.OrganizationMembers
                    .CountAsync(m => m.OrganizationId == invitation.OrganizationId && m.Status == "active");

                var maxMembers = organization.Plan?.UserLimit ?? DefaultMaxMembers;

                if (currentMemberCount >= maxMembers)
                {
                    throw new InvalidOperationException($"USER_LIMIT_EXCEEDED: Organization has reached maximum {maxMembers} members");
                }
            }

            // Create membership
            var membership = new OrganizationMember
            {
                OrganizationId = invitation.OrganizationId,
                UserId = userId,
                Role = invitation.Role,
                IsExternal = invitation.IsExternal,
                ClientReference = invitation.IsExternal ? $"INV-{invitation.Id:N}" : null,
                Status = "active",
                InvitedById = invitation.InvitedById,
                JoinedAt = DateTime.UtcNow,
                AccessExpiresAt = invitation.AccessDurationDays.HasValue
                    ? DateTime.UtcNow.AddDays(invitation.AccessDurationDays.Value)
                    : null
            };

            _dbContext.OrganizationMembers.Add(membership);

            // Update invitation status
            invitation.Status = "accepted";
            invitation.RespondedAt = DateTime.UtcNow;
            invitation.AcceptedUserId = userId;

            // Update user's organization if they don't have one
            if (user.OrganizationId == null)
            {
                user.OrganizationId = invitation.OrganizationId;
                user.Role = invitation.Role;
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("User {UserId} accepted invitation and joined organization {OrganizationId}",
                userId, invitation.OrganizationId);

            // Audit logging
            await _auditService.LogAsync(new AuditLogEntry
            {
                Action = "invitation.accepted",
                EntityType = "OrganizationMember",
                EntityId = membership.Id,
                UserId = userId,
                OrganizationId = invitation.OrganizationId,
                NewValues = new
                {
                    membership.Role,
                    membership.IsExternal,
                    membership.JoinedAt,
                    InvitationId = invitation.Id
                }
            });

            return new AcceptInvitationResponse(
                Message: "Successfully joined organization",
                Organization: new OrganizationBasicDto(
                    invitation.Organization.Id,
                    invitation.Organization.Name,
                    invitation.Role
                )
            );
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DeclineInvitationAsync(string token, Guid userId)
    {
        var tokenHash = HashToken(token);

        var invitation = await _dbContext.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash)
            ?? throw new KeyNotFoundException("INVALID_INVITATION");

        if (invitation.Status != "pending")
        {
            throw new InvalidOperationException($"INVITATION_{invitation.Status.ToUpperInvariant()}");
        }

        // Verify email matches
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        if (!string.Equals(user.NormalizedEmail, invitation.EmailNormalized.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EMAIL_MISMATCH");
        }

        invitation.Status = "declined";
        invitation.RespondedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {UserId} declined invitation {InvitationId}", userId, invitation.Id);
    }

    #endregion

    #region Settings

    public async Task<OrganizationSettingsDto> UpdateSettingsAsync(Guid organizationId, UpdateOrganizationSettingsRequest request, Guid userId)
    {
        await ValidateAdminAccessAsync(organizationId, userId);

        var organization = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.DeletedAt == null)
            ?? throw new KeyNotFoundException("ORGANIZATION_NOT_FOUND");

        var settings = organization.Settings ?? new Dictionary<string, object>();

        if (request.DefaultReminderDays != null)
            settings["default_reminder_days"] = request.DefaultReminderDays;
        if (request.NotificationEmail.HasValue)
            settings["notification_email"] = request.NotificationEmail.Value;
        if (request.NotificationSms.HasValue)
            settings["notification_sms"] = request.NotificationSms.Value;
        if (request.AllowCaAccess.HasValue)
            settings["allow_ca_access"] = request.AllowCaAccess.Value;
        if (request.RequireResponseApproval.HasValue)
            settings["require_response_approval"] = request.RequireResponseApproval.Value;
        if (!string.IsNullOrEmpty(request.Timezone))
            settings["timezone"] = request.Timezone;
        if (!string.IsNullOrEmpty(request.Language))
            settings["language"] = request.Language;
        if (!string.IsNullOrEmpty(request.DateFormat))
            settings["date_format"] = request.DateFormat;

        organization.Settings = settings;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Organization {OrganizationId} settings updated by user {UserId}", organizationId, userId);

        return MapSettingsToDto(settings);
    }

    #endregion

    #region Organization Switching

    public async Task<SwitchOrganizationResponse> SwitchOrganizationAsync(SwitchOrganizationRequest request, Guid userId, string ipAddress, string userAgent)
    {
        var membership = await _dbContext.OrganizationMembers
            .Include(m => m.Organization)
            .Include(m => m.User)
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == request.OrganizationId &&
                m.UserId == userId &&
                m.Status == "active" &&
                (m.AccessExpiresAt == null || m.AccessExpiresAt > DateTime.UtcNow))
            ?? throw new UnauthorizedAccessException("NOT_A_MEMBER");

        var user = membership.User;
        var organization = membership.Organization;

        // Update user's current organization
        user.OrganizationId = organization.Id;
        user.Role = membership.Role;

        // Update last active
        membership.LastActiveAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        // Generate new tokens with organization context
        var accessToken = _jwtService.GenerateAccessToken(user, organization);
        var (refreshToken, jti, expiresAt) = _jwtService.GenerateRefreshToken();

        // Create session
        var session = new UserSession
        {
            UserId = user.Id,
            RefreshTokenHash = HashToken(refreshToken),
            RefreshTokenJti = jti,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Platform = "web",
            ExpiresAt = expiresAt,
            LastActiveAt = DateTime.UtcNow
        };

        _dbContext.UserSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {UserId} switched to organization {OrganizationId}", userId, organization.Id);

        return new SwitchOrganizationResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            Organization: new OrganizationBasicDto(organization.Id, organization.Name, membership.Role)
        );
    }

    #endregion

    #region Helper Methods

    private async Task<OrganizationMember> ValidateAdminAccessAsync(Guid organizationId, Guid userId)
    {
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.Status == "active" &&
                (m.Role == "owner" || m.Role == "admin"))
            ?? throw new UnauthorizedAccessException("ADMIN_REQUIRED");

        return membership;
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
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private OrganizationDetailResponse MapToDetailResponse(Organization org, string currentUserRole, int noticesThisMonth)
    {
        return new OrganizationDetailResponse(
            Id: org.Id,
            Name: org.Name,
            LegalName: org.LegalName,
            DisplayName: org.DisplayName,
            Industry: org.Industry,
            SubIndustry: org.SubIndustry,
            BusinessType: org.BusinessType,
            AnnualTurnoverRange: org.AnnualTurnoverRange,
            EmployeeCountRange: org.EmployeeCountRange,
            Email: org.Email,
            Phone: org.Phone,
            Website: org.Website,
            Address: new AddressDto(
                org.AddressLine1,
                org.AddressLine2,
                org.City,
                org.State,
                org.PinCode,
                org.Country
            ),
            Pan: org.Pan,
            Gstins: org.OrganizationGstins.Select(g => new GstinDto(
                g.Id,
                g.Gstin,
                g.TradeName,
                g.StateCode,
                g.StateName,
                g.Status,
                g.IsPrimary,
                g.IsVerified,
                g.VerifiedAt
            )).ToList(),
            Subscription: new SubscriptionInfoDto(
                org.SubscriptionStatus,
                org.Plan != null ? new PlanInfoDto(
                    org.Plan.Id,
                    org.Plan.Name,
                    org.Plan.NoticeLimit,
                    org.Plan.UserLimit,
                    org.Plan.GstinLimit
                ) : null,
                org.TrialEndsAt,
                new UsageInfoDto(
                    noticesThisMonth,
                    org.Members.Count,
                    org.OrganizationGstins.Count
                )
            ),
            Settings: MapSettingsToDto(org.Settings ?? new Dictionary<string, object>()),
            LogoUrl: org.LogoUrl,
            MemberCount: org.Members.Count,
            CurrentUserRole: currentUserRole,
            CreatedAt: org.CreatedAt,
            UpdatedAt: org.UpdatedAt
        );
    }

    private static OrganizationSettingsDto MapSettingsToDto(Dictionary<string, object> settings)
    {
        return new OrganizationSettingsDto(
            DefaultReminderDays: settings.TryGetValue("default_reminder_days", out var days) && days is IEnumerable<object> enumerable
                ? enumerable.Select(d => Convert.ToInt32(d)).ToList()
                : [7, 3, 1],
            NotificationEmail: settings.TryGetValue("notification_email", out var email) && email is bool b1 ? b1 : true,
            NotificationSms: settings.TryGetValue("notification_sms", out var sms) && sms is bool b2 ? b2 : true,
            AllowCaAccess: settings.TryGetValue("allow_ca_access", out var ca) && ca is bool b3 ? b3 : true,
            RequireResponseApproval: settings.TryGetValue("require_response_approval", out var approval) && approval is bool b4 && b4,
            Timezone: settings.TryGetValue("timezone", out var tz) ? tz?.ToString() ?? "Asia/Kolkata" : "Asia/Kolkata",
            Language: settings.TryGetValue("language", out var lang) ? lang?.ToString() ?? "en" : "en",
            DateFormat: settings.TryGetValue("date_format", out var df) ? df?.ToString() ?? "DD/MM/YYYY" : "DD/MM/YYYY"
        );
    }

    #endregion
}
