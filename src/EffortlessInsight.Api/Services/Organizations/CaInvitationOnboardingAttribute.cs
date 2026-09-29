namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>Subscription-exempt onboarding only; recipient authorization remains in the service.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CaInvitationOnboardingAttribute : Attribute;
