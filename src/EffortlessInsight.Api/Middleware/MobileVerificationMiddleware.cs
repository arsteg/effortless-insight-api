using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Filters;
using Microsoft.AspNetCore.Authorization;

namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Blocks signed-in users whose mobile number is not verified (e.g. new Google /
/// Microsoft sign-ups) from every authenticated endpoint except the few marked
/// [AllowUnverifiedMobile]. Runs once for all requests, so new controllers are
/// covered without adding a check to each of them.
/// </summary>
public class MobileVerificationMiddleware
{
    public const string ErrorCode = "MOBILE_NOT_VERIFIED";

    private readonly RequestDelegate _next;
    private readonly bool _required;

    public MobileVerificationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        // Same switch that makes /register require a verified mobile.
        _required = configuration.GetValue("MobileVerification:RequiredForSignup", true);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_required || !IsBlocked(context))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
            false, ErrorCode, "Please verify your mobile number to continue"));
    }

    private static bool IsBlocked(HttpContext context)
    {
        var user = context.User;
        if (!(user.Identity?.IsAuthenticated ?? false))
            return false;

        // Admin portal tokens are a separate identity with no mobile requirement
        if (user.HasClaim(c => c.Type == "admin_id"))
            return false;

        // Only an explicit "false" blocks: tokens issued before this claim existed keep
        // working until they expire, and the next refresh adds the real value.
        if (user.FindFirst("mobile_verified")?.Value != "false")
            return false;

        var endpoint = context.GetEndpoint();

        // Public endpoints (login, refresh, signup OTP, OAuth...) don't depend on the caller
        if (endpoint == null || endpoint.Metadata.GetMetadata<IAuthorizeData>() == null ||
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
            return false;

        return endpoint.Metadata.GetMetadata<AllowUnverifiedMobileAttribute>() == null;
    }
}

/// <summary>
/// Extension methods for mobile verification middleware.
/// </summary>
public static class MobileVerificationMiddlewareExtensions
{
    /// <summary>
    /// Adds mobile verification enforcement to the pipeline.
    /// Should be added after authentication/authorization and before subscription enforcement.
    /// </summary>
    public static IApplicationBuilder UseMobileVerification(this IApplicationBuilder app)
    {
        return app.UseMiddleware<MobileVerificationMiddleware>();
    }
}
