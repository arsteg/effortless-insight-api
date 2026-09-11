namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for mobile verification during user registration.
/// </summary>
public class MobileVerificationOptions
{
    public const string SectionName = "MobileVerification";

    /// <summary>
    /// Whether mobile OTP verification is required during signup.
    /// When true, users must provide a valid mobile number and verify it via OTP.
    /// When false, mobile verification is bypassed and users can register without a phone number.
    /// Default: true
    /// </summary>
    public bool RequiredForSignup { get; set; } = true;
}
