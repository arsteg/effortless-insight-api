using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing organizations.
/// </summary>
[Route("api/v1/admin/organizations")]
[Authorize(Policy = "AdminAuthenticated")]
public class AdminOrganizationsController : AdminControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAdminAuditService _auditService;

    public AdminOrganizationsController(
        ApplicationDbContext dbContext,
        IAdminAuditService auditService,
        ILogger<AdminOrganizationsController> logger)
        : base(logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    /// <summary>
    /// Search and list organizations with filtering.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(OrganizationListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrganizations([FromQuery] OrganizationSearchRequest request)
    {
        if (!HasPermission(AdminPermissions.OrganizationsView))
        {
            return Forbid();
        }

        var query = _dbContext.Organizations
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrEmpty(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(o =>
                o.Name.ToLower().Contains(searchLower) ||
                o.NameNormalized.Contains(searchLower));
        }

        // Apply status filter
        if (!string.IsNullOrEmpty(request.Status))
        {
            query = request.Status.ToLower() switch
            {
                "active" => query.Where(o => o.DeletedAt == null && o.SubscriptionStatus != "suspended"),
                "suspended" => query.Where(o => o.SubscriptionStatus == "suspended"),
                "deleted" => query.Where(o => o.DeletedAt != null),
                "trial" => query.Where(o => o.SubscriptionStatus == "trial"),
                _ => query
            };
        }

        // Apply plan filter
        // TODO: Implement plan filter using BillingSubscriptions table
        // if (!string.IsNullOrEmpty(request.Plan))
        // {
        //     query = query.Where(o => o.SubscriptionStatus == "active");
        // }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply sorting
        query = request.SortBy?.ToLower() switch
        {
            "name" => request.SortDesc ? query.OrderByDescending(o => o.Name) : query.OrderBy(o => o.Name),
            "members" => request.SortDesc
                ? query.OrderByDescending(o => o.Members.Count)
                : query.OrderBy(o => o.Members.Count),
            _ => query.OrderByDescending(o => o.CreatedAt)
        };

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var organizations = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrganizationListItem
            {
                Id = o.Id,
                Name = o.Name,
                Status = o.DeletedAt != null ? "deleted" : (o.SubscriptionStatus == "suspended" ? "suspended" : "active"),
                PlanCode = null, // TODO: Get from BillingSubscription
                PlanName = null, // TODO: Get from BillingSubscription
                SubscriptionStatus = o.SubscriptionStatus,
                MemberCount = o.Members.Count,
                NoticeCount = o.Notices.Count,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync();

        return Success(new OrganizationListResponse
        {
            Organizations = organizations,
            Pagination = new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        });
    }

    /// <summary>
    /// Get organization details.
    /// </summary>
    [HttpGet("{orgId:guid}")]
    [ProducesResponseType(typeof(AdminOrganizationDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrganization(Guid orgId)
    {
        if (!HasPermission(AdminPermissions.OrganizationsView))
        {
            return Forbid();
        }

        var org = await _dbContext.Organizations
            .Include(o => o.Members)
            .ThenInclude(m => m.User)
            .Include(o => o.OrganizationGstins)
            .FirstOrDefaultAsync(o => o.Id == orgId);

        if (org == null)
        {
            return NotFoundResponse("Organization not found");
        }

        // Get subscription if exists
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId);

        // Get usage stats
        var noticeCount = await _dbContext.Notices
            .CountAsync(n => n.OrganizationId == orgId);

        var storageUsed = await _dbContext.Attachments
            .Where(a => a.Notice.OrganizationId == orgId)
            .SumAsync(a => (long)a.FileSize);

        // Get active credits
        var activeCredits = await _dbContext.OrganizationCredits
            .Where(c => c.OrganizationId == orgId && c.Status == CreditStatus.Active)
            .SumAsync(c => c.RemainingAmount);

        // Get recent invoices
        var recentInvoices = await _dbContext.Invoices
            .Where(i => i.OrganizationId == orgId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new AdminInvoiceSummary
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                Amount = i.Total,
                Status = i.Status,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return Success(new AdminOrganizationDetail
        {
            Id = org.Id,
            Name = org.Name,
            Status = org.DeletedAt != null ? "deleted" : (org.SubscriptionStatus == "suspended" ? "suspended" : "active"),
            Industry = org.Industry,
            Website = org.Website,
            Plan = null, // TODO: Get from BillingSubscription
            Subscription = subscription != null ? new AdminSubscriptionInfo
            {
                Status = subscription.Status,
                BillingCycle = subscription.BillingCycle,
                CurrentPeriodStart = subscription.CurrentPeriodStart,
                CurrentPeriodEnd = subscription.CurrentPeriodEnd,
                SeatsIncluded = subscription.SeatsIncluded,
                SeatsAdditional = subscription.SeatsAdditional,
                CancelAtPeriodEnd = subscription.CancelAtPeriodEnd
            } : null,
            Members = org.Members.Select(m => new AdminMemberInfo
            {
                UserId = m.UserId,
                Email = m.User.Email ?? "",
                Name = m.User.Name,
                Role = m.Role,
                JoinedAt = m.JoinedAt
            }).ToList(),
            GstinList = org.OrganizationGstins.Select(g => new AdminGstinInfo
            {
                Id = g.Id,
                Gstin = g.Gstin,
                LegalName = g.LegalName ?? "",
                State = g.StateName
            }).ToList(),
            Usage = new AdminUsageInfo
            {
                NoticeCount = noticeCount,
                StorageUsedMb = Math.Round(storageUsed / 1024.0 / 1024.0, 2),
                ActiveCredits = activeCredits
            },
            RecentInvoices = recentInvoices,
            CreatedAt = org.CreatedAt
        });
    }

    /// <summary>
    /// Update organization details.
    /// </summary>
    [HttpPatch("{orgId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOrganization(Guid orgId, [FromBody] UpdateOrganizationRequest request)
    {
        if (!HasPermission(AdminPermissions.OrganizationsUpdate))
        {
            return Forbid();
        }

        var org = await _dbContext.Organizations.FindAsync(orgId);
        if (org == null)
        {
            return NotFoundResponse("Organization not found");
        }

        var changes = new Dictionary<string, object>();

        if (request.Name != null && request.Name != org.Name)
        {
            changes["name"] = new { before = org.Name, after = request.Name };
            org.Name = request.Name;
            org.NameNormalized = request.Name.ToLowerInvariant().Trim();
        }

        if (request.Industry != null && request.Industry != org.Industry)
        {
            changes["industry"] = new { before = org.Industry, after = request.Industry };
            org.Industry = request.Industry;
        }

        if (request.Website != null && request.Website != org.Website)
        {
            changes["website"] = new { before = org.Website, after = request.Website };
            org.Website = request.Website;
        }

        org.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.OrganizationUpdated,
            AuditTargetTypes.Organization,
            orgId.ToString(),
            "Organization details updated",
            changes,
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "Organization updated successfully");
    }

    /// <summary>
    /// Suspend an organization.
    /// </summary>
    [HttpPost("{orgId:guid}/suspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendOrganization(Guid orgId, [FromBody] SuspendOrgRequest request)
    {
        if (!HasPermission(AdminPermissions.OrganizationsUpdate))
        {
            return Forbid();
        }

        var org = await _dbContext.Organizations.FindAsync(orgId);
        if (org == null)
        {
            return NotFoundResponse("Organization not found");
        }

        if (org.DeletedAt != null)
        {
            return Error("Organization is already deleted", "ALREADY_DELETED");
        }

        // Mark as suspended using SubscriptionStatus
        org.SubscriptionStatus = "suspended";
        org.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.OrganizationSuspended,
            AuditTargetTypes.Organization,
            orgId.ToString(),
            $"Organization suspended: {request.Reason}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason,
                ["notes"] = request.Notes ?? ""
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "Organization suspended successfully");
    }

    /// <summary>
    /// Unsuspend an organization.
    /// </summary>
    [HttpPost("{orgId:guid}/unsuspend")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnsuspendOrganization(Guid orgId)
    {
        if (!HasPermission(AdminPermissions.OrganizationsUpdate))
        {
            return Forbid();
        }

        var org = await _dbContext.Organizations.FindAsync(orgId);
        if (org == null)
        {
            return NotFoundResponse("Organization not found");
        }

        if (org.SubscriptionStatus != "suspended")
        {
            return Error("Organization is not suspended", "NOT_SUSPENDED");
        }

        // Restore status
        org.SubscriptionStatus = "active";
        org.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.OrganizationUnsuspended,
            AuditTargetTypes.Organization,
            orgId.ToString(),
            "Organization unsuspended",
            ipAddress: ClientIpAddress,
            userAgent: ClientUserAgent,
            sessionId: CurrentSessionId);

        return Success<object?>(null, "Organization unsuspended successfully");
    }

    /// <summary>
    /// Apply credit to organization.
    /// </summary>
    [HttpPost("{orgId:guid}/credits")]
    [ProducesResponseType(typeof(AdminCreditResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyCredit(Guid orgId, [FromBody] ApplyCreditRequest request)
    {
        if (!HasPermission(AdminPermissions.OrganizationsCredits))
        {
            return Forbid();
        }

        var org = await _dbContext.Organizations.FindAsync(orgId);
        if (org == null)
        {
            return NotFoundResponse("Organization not found");
        }

        var credit = new OrganizationCredit
        {
            OrganizationId = orgId,
            Amount = request.Amount,
            RemainingAmount = request.Amount,
            CreditType = request.Type ?? CreditTypes.Compensation,
            Reason = request.Reason,
            Status = CreditStatus.Active,
            GrantedById = CurrentAdminId,
            ExpiresAt = request.ExpiresAt
        };

        _dbContext.OrganizationCredits.Add(credit);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.CreditApplied,
            AuditTargetTypes.Organization,
            orgId.ToString(),
            $"Credit applied: ₹{request.Amount}",
            new Dictionary<string, object>
            {
                ["amount"] = request.Amount,
                ["reason"] = request.Reason,
                ["expires_at"] = request.ExpiresAt?.ToString("O") ?? "never",
                ["credit_id"] = credit.Id
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success(new AdminCreditResponse
        {
            CreditId = credit.Id,
            Amount = request.Amount,
            ExpiresAt = credit.ExpiresAt
        });
    }

    /// <summary>
    /// Get organization credits.
    /// </summary>
    [HttpGet("{orgId:guid}/credits")]
    [ProducesResponseType(typeof(List<AdminCreditInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCredits(Guid orgId)
    {
        if (!HasPermission(AdminPermissions.OrganizationsView))
        {
            return Forbid();
        }

        var credits = await _dbContext.OrganizationCredits
            .Include(c => c.GrantedBy)
            .Where(c => c.OrganizationId == orgId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new AdminCreditInfo
            {
                Id = c.Id,
                Amount = c.Amount,
                RemainingAmount = c.RemainingAmount,
                Type = c.CreditType,
                Reason = c.Reason,
                Status = c.Status,
                GrantedBy = c.GrantedBy != null ? c.GrantedBy.Name : "System",
                ExpiresAt = c.ExpiresAt,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();

        return Success(credits);
    }

    /// <summary>
    /// Delete organization (GDPR compliance).
    /// </summary>
    [HttpDelete("{orgId:guid}")]
    [Authorize(Policy = "AdminSuperAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOrganization(Guid orgId, [FromBody] DeleteOrgRequest request)
    {
        if (!HasPermission(AdminPermissions.OrganizationsDelete))
        {
            return Forbid();
        }

        var org = await _dbContext.Organizations.FindAsync(orgId);
        if (org == null)
        {
            return NotFoundResponse("Organization not found");
        }

        if (!request.Confirmed)
        {
            return Error("Deletion must be confirmed", "CONFIRMATION_REQUIRED");
        }

        // Soft delete
        org.DeletedAt = DateTime.UtcNow;
        org.SubscriptionStatus = "deleted";
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.OrganizationDeleted,
            AuditTargetTypes.Organization,
            orgId.ToString(),
            $"Organization deleted: {request.Reason}",
            new Dictionary<string, object>
            {
                ["reason"] = request.Reason,
                ["gdpr_request"] = request.GdprRequest
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "Organization deleted successfully");
    }
}

// DTOs

public record OrganizationSearchRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? Plan { get; init; }
    public string? SortBy { get; init; }
    public bool SortDesc { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public record OrganizationListResponse
{
    public List<AdminOrganizationListItem> Organizations { get; init; } = [];
    public PaginationInfo Pagination { get; init; } = new();
}

public record AdminOrganizationListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string SubscriptionStatus { get; init; } = string.Empty;
    public int MemberCount { get; init; }
    public int NoticeCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record AdminOrganizationDetail
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Industry { get; init; }
    public string? Website { get; init; }
    public AdminPlanInfo? Plan { get; init; }
    public AdminSubscriptionInfo? Subscription { get; init; }
    public List<AdminMemberInfo> Members { get; init; } = [];
    public List<AdminGstinInfo> GstinList { get; init; } = [];
    public AdminUsageInfo Usage { get; init; } = new();
    public List<AdminInvoiceSummary> RecentInvoices { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public record AdminPlanInfo
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public record AdminSubscriptionInfo
{
    public string Status { get; init; } = string.Empty;
    public string BillingCycle { get; init; } = string.Empty;
    public DateTime CurrentPeriodStart { get; init; }
    public DateTime CurrentPeriodEnd { get; init; }
    public int SeatsIncluded { get; init; }
    public int SeatsAdditional { get; init; }
    public bool CancelAtPeriodEnd { get; init; }
}

public record AdminMemberInfo
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public DateTime JoinedAt { get; init; }
}

public record AdminGstinInfo
{
    public Guid Id { get; init; }
    public string Gstin { get; init; } = string.Empty;
    public string LegalName { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}

public record AdminUsageInfo
{
    public int NoticeCount { get; init; }
    public double StorageUsedMb { get; init; }
    public decimal ActiveCredits { get; init; }
}

public record AdminInvoiceSummary
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public int Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record UpdateOrganizationRequest
{
    public string? Name { get; init; }
    public string? Industry { get; init; }
    public string? Website { get; init; }
}

public record SuspendOrgRequest
{
    public string Reason { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

public record ApplyCreditRequest
{
    public decimal Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? Type { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public record AdminCreditResponse
{
    public Guid CreditId { get; init; }
    public decimal Amount { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public record AdminCreditInfo
{
    public Guid Id { get; init; }
    public decimal Amount { get; init; }
    public decimal RemainingAmount { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string GrantedBy { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record DeleteOrgRequest
{
    public string Reason { get; init; } = string.Empty;
    public bool GdprRequest { get; init; }
    public bool Confirmed { get; init; }
}
