using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Services.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Filters;

/// <summary>
/// Attribute to require a specific feature to be available in the organization's plan.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresFeatureAttribute : Attribute, IAsyncActionFilter
{
    public string FeatureCode { get; }

    public RequiresFeatureAttribute(string featureCode)
    {
        FeatureCode = featureCode;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var featureService = context.HttpContext.RequestServices.GetRequiredService<IFeatureAccessService>();
        var dbContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<RequiresFeatureAttribute>>();

        // Get user ID from claims
        var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? context.HttpContext.User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "User not authenticated" });
            return;
        }

        // Get user's organization
        var organizationId = await GetUserOrganizationIdAsync(dbContext, userId);

        if (organizationId == null)
        {
            logger.LogWarning("User {UserId} has no organization for feature check", userId);
            context.Result = new ObjectResult(new
            {
                message = "No organization found for user",
                code = "NO_ORGANIZATION"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        // Check feature access
        var hasAccess = await featureService.HasFeatureAccessAsync(organizationId.Value, FeatureCode);

        if (!hasAccess)
        {
            logger.LogInformation(
                "Feature '{FeatureCode}' denied for organization {OrganizationId}",
                FeatureCode,
                organizationId);

            context.Result = new ObjectResult(new
            {
                message = $"This feature requires a plan upgrade. '{FormatFeatureName(FeatureCode)}' is not available in your current plan.",
                code = "FEATURE_NOT_AVAILABLE",
                featureCode = FeatureCode,
                upgradeRequired = true
            })
            {
                StatusCode = StatusCodes.Status402PaymentRequired
            };
            return;
        }

        await next();
    }

    private static async Task<Guid?> GetUserOrganizationIdAsync(ApplicationDbContext db, Guid userId)
    {
        // Get user's primary/active organization
        var membership = await db.OrganizationMembers
            .Where(m => m.UserId == userId && m.Status == "active" && m.DeletedAt == null)
            .OrderByDescending(m => m.Role == "owner")
            .ThenByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        return membership?.OrganizationId;
    }

    private static string FormatFeatureName(string featureCode)
    {
        // Convert snake_case to Title Case
        return string.Join(" ", featureCode.Split('_')
            .Select(word => char.ToUpper(word[0]) + word[1..].ToLower()));
    }
}
