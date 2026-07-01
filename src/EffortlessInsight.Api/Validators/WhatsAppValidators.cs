using EffortlessInsight.Api.Controllers;
using EffortlessInsight.Api.DTOs;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

/// <summary>
/// Validator for WhatsApp link request.
/// </summary>
public class WhatsAppLinkRequestValidator : AbstractValidator<WhatsAppLinkRequest>
{
    public WhatsAppLinkRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required")
            .MinimumLength(10).WithMessage("Phone number must be at least 10 digits")
            .MaximumLength(15).WithMessage("Phone number cannot exceed 15 characters")
            .Matches(@"^[\d\s\+\-\(\)]+$").WithMessage("Phone number contains invalid characters")
            .Must(BeValidIndianPhoneNumber).WithMessage("Invalid phone number format. Use format: +919876543210 or 9876543210");
    }

    private static bool BeValidIndianPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return false;

        // Remove all non-digit characters
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());

        // Check for valid Indian phone number patterns:
        // - 10 digits starting with 6-9 (e.g., 9876543210)
        // - 12 digits starting with 91 (e.g., 919876543210)
        // - 11 digits starting with 0 (e.g., 09876543210)
        return digits.Length switch
        {
            10 => digits[0] >= '6' && digits[0] <= '9',
            11 => digits[0] == '0' && digits[1] >= '6' && digits[1] <= '9',
            12 => digits.StartsWith("91") && digits[2] >= '6' && digits[2] <= '9',
            _ => false
        };
    }
}

/// <summary>
/// Validator for WhatsApp verification code request.
/// </summary>
public class VerifyCodeRequestValidator : AbstractValidator<VerifyCodeRequest>
{
    public VerifyCodeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required")
            .Length(6).WithMessage("Verification code must be exactly 6 digits")
            .Matches(@"^\d{6}$").WithMessage("Verification code must contain only digits");
    }
}

/// <summary>
/// Validator for WhatsApp opt-in request.
/// </summary>
public class OptInRequestValidator : AbstractValidator<OptInRequest>
{
    public OptInRequestValidator()
    {
        // OptIn is a required boolean - no additional validation needed
        // FluentValidation will handle the binding validation
    }
}

/// <summary>
/// Validator for WhatsApp preferences update request.
/// </summary>
public class WhatsAppPreferencesRequestValidator : AbstractValidator<WhatsAppPreferencesRequest>
{
    public WhatsAppPreferencesRequestValidator()
    {
        // All fields are nullable booleans - no validation needed
        // At least one field should be provided for the update to make sense
        RuleFor(x => x)
            .Must(HaveAtLeastOneField)
            .WithMessage("At least one preference field must be provided");
    }

    private static bool HaveAtLeastOneField(WhatsAppPreferencesRequest request)
    {
        return request.DeadlineReminders.HasValue ||
               request.HighRiskAlerts.HasValue ||
               request.TaskAssignments.HasValue ||
               request.DailyDigest.HasValue;
    }
}
