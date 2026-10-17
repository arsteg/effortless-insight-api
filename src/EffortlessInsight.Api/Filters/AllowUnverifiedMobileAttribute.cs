namespace EffortlessInsight.Api.Filters;

/// <summary>
/// Marks an [Authorize] endpoint that a signed-in user may call before verifying
/// their mobile number (e.g. the complete-signup OTP flow, /auth/me, logout).
/// Every other authenticated endpoint is blocked by MobileVerificationMiddleware.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowUnverifiedMobileAttribute : Attribute;
