using EffortlessInsight.Api.DTOs;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

public class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Organization name is required")
            .MinimumLength(2).WithMessage("Organization name must be at least 2 characters")
            .MaximumLength(255).WithMessage("Organization name cannot exceed 255 characters");

        RuleFor(x => x.LegalName)
            .MaximumLength(255).WithMessage("Legal name cannot exceed 255 characters")
            .When(x => !string.IsNullOrEmpty(x.LegalName));

        RuleFor(x => x.Gstin)
            .NotEmpty().WithMessage("GSTIN is required")
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
            .WithMessage("Invalid GSTIN format");

        RuleFor(x => x.Industry)
            .MaximumLength(100).WithMessage("Industry cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.Industry));

        RuleFor(x => x.State)
            .NotEmpty().WithMessage("State is required")
            .MaximumLength(50).WithMessage("State cannot exceed 50 characters");

        RuleFor(x => x.City)
            .MaximumLength(100).WithMessage("City cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.City));

        RuleFor(x => x.AnnualTurnoverRange)
            .Must(BeValidTurnoverRange).WithMessage("Invalid annual turnover range")
            .When(x => !string.IsNullOrEmpty(x.AnnualTurnoverRange));
    }

    private static bool BeValidTurnoverRange(string? range)
    {
        if (string.IsNullOrEmpty(range)) return true;
        var validRanges = new[] { "0-40L", "40L-1.5Cr", "1.5Cr-5Cr", "5Cr-25Cr", "25Cr+" };
        return validRanges.Contains(range);
    }
}

public class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(2).WithMessage("Organization name must be at least 2 characters")
            .MaximumLength(255).WithMessage("Organization name cannot exceed 255 characters")
            .When(x => !string.IsNullOrEmpty(x.Name));

        RuleFor(x => x.LegalName)
            .MaximumLength(255).WithMessage("Legal name cannot exceed 255 characters")
            .When(x => !string.IsNullOrEmpty(x.LegalName));

        RuleFor(x => x.DisplayName)
            .MaximumLength(100).WithMessage("Display name cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.DisplayName));

        RuleFor(x => x.Industry)
            .MaximumLength(100).WithMessage("Industry cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.Industry));

        RuleFor(x => x.SubIndustry)
            .MaximumLength(100).WithMessage("Sub-industry cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.SubIndustry));

        RuleFor(x => x.BusinessType)
            .Must(BeValidBusinessType).WithMessage("Invalid business type")
            .When(x => !string.IsNullOrEmpty(x.BusinessType));

        RuleFor(x => x.AnnualTurnoverRange)
            .Must(BeValidTurnoverRange).WithMessage("Invalid annual turnover range")
            .When(x => !string.IsNullOrEmpty(x.AnnualTurnoverRange));

        RuleFor(x => x.EmployeeCountRange)
            .Must(BeValidEmployeeRange).WithMessage("Invalid employee count range")
            .When(x => !string.IsNullOrEmpty(x.EmployeeCountRange));

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email cannot exceed 255 characters")
            .When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.Phone)
            .MaximumLength(20).WithMessage("Phone cannot exceed 20 characters")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        RuleFor(x => x.Website)
            .MaximumLength(255).WithMessage("Website cannot exceed 255 characters")
            .Must(BeValidUrl).WithMessage("Invalid website URL")
            .When(x => !string.IsNullOrEmpty(x.Website));

        RuleFor(x => x.Pan)
            .Matches(@"^[A-Z]{5}[0-9]{4}[A-Z]{1}$").WithMessage("Invalid PAN format")
            .When(x => !string.IsNullOrEmpty(x.Pan));

        RuleFor(x => x.Tan)
            .MaximumLength(10).WithMessage("TAN cannot exceed 10 characters")
            .When(x => !string.IsNullOrEmpty(x.Tan));

        When(x => x.Address != null, () =>
        {
            RuleFor(x => x.Address!.Line1)
                .MaximumLength(255).WithMessage("Address line 1 cannot exceed 255 characters")
                .When(x => !string.IsNullOrEmpty(x.Address?.Line1));

            RuleFor(x => x.Address!.Line2)
                .MaximumLength(255).WithMessage("Address line 2 cannot exceed 255 characters")
                .When(x => !string.IsNullOrEmpty(x.Address?.Line2));

            RuleFor(x => x.Address!.City)
                .MaximumLength(100).WithMessage("City cannot exceed 100 characters")
                .When(x => !string.IsNullOrEmpty(x.Address?.City));

            RuleFor(x => x.Address!.State)
                .MaximumLength(50).WithMessage("State cannot exceed 50 characters")
                .When(x => !string.IsNullOrEmpty(x.Address?.State));

            RuleFor(x => x.Address!.PinCode)
                .MaximumLength(10).WithMessage("PIN code cannot exceed 10 characters")
                .Matches(@"^\d{6}$").WithMessage("Invalid PIN code (6 digits required)")
                .When(x => !string.IsNullOrEmpty(x.Address?.PinCode));
        });
    }

    private static bool BeValidBusinessType(string? type)
    {
        if (string.IsNullOrEmpty(type)) return true;
        var validTypes = new[] { "proprietorship", "partnership", "llp", "pvt_ltd", "public_ltd", "trust", "society", "other" };
        return validTypes.Contains(type);
    }

    private static bool BeValidTurnoverRange(string? range)
    {
        if (string.IsNullOrEmpty(range)) return true;
        var validRanges = new[] { "0-40L", "40L-1.5Cr", "1.5Cr-5Cr", "5Cr-25Cr", "25Cr+" };
        return validRanges.Contains(range);
    }

    private static bool BeValidEmployeeRange(string? range)
    {
        if (string.IsNullOrEmpty(range)) return true;
        var validRanges = new[] { "1-10", "11-50", "51-200", "200+" };
        return validRanges.Contains(range);
    }

    private static bool BeValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var result)
            && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }
}

public class DeleteOrganizationRequestValidator : AbstractValidator<DeleteOrganizationRequest>
{
    public DeleteOrganizationRequestValidator()
    {
        RuleFor(x => x.Confirmation)
            .NotEmpty().WithMessage("Confirmation is required");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
}

public class AddGstinRequestValidator : AbstractValidator<AddGstinRequest>
{
    public AddGstinRequestValidator()
    {
        RuleFor(x => x.Gstin)
            .NotEmpty().WithMessage("GSTIN is required")
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
            .WithMessage("Invalid GSTIN format");

        RuleFor(x => x.TradeName)
            .MaximumLength(255).WithMessage("Trade name cannot exceed 255 characters")
            .When(x => !string.IsNullOrEmpty(x.TradeName));
    }
}

public class InviteMemberRequestValidator : AbstractValidator<InviteMemberRequest>
{
    public InviteMemberRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email cannot exceed 255 characters");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required")
            .Must(BeValidRole).WithMessage("Invalid role. Must be one of: admin, manager, member, ca, viewer");

        RuleFor(x => x.AccessDurationDays)
            .GreaterThan(0).WithMessage("Access duration must be positive")
            .LessThanOrEqualTo(365 * 5).WithMessage("Access duration cannot exceed 5 years")
            .When(x => x.AccessDurationDays.HasValue);

        RuleFor(x => x.ClientReference)
            .MaximumLength(100).WithMessage("Client reference cannot exceed 100 characters")
            .When(x => !string.IsNullOrEmpty(x.ClientReference));

        RuleFor(x => x.Message)
            .MaximumLength(500).WithMessage("Message cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Message));
    }

    private static bool BeValidRole(string role)
    {
        var validRoles = new[] { "admin", "manager", "member", "ca", "viewer" };
        return validRoles.Contains(role.ToLowerInvariant());
    }
}

public class ChangeMemberRoleRequestValidator : AbstractValidator<ChangeMemberRoleRequest>
{
    public ChangeMemberRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required")
            .Must(BeValidRole).WithMessage("Invalid role. Must be one of: admin, manager, member, ca, viewer");
    }

    private static bool BeValidRole(string role)
    {
        var validRoles = new[] { "admin", "manager", "member", "ca", "viewer" };
        return validRoles.Contains(role.ToLowerInvariant());
    }
}

public class TransferOwnershipRequestValidator : AbstractValidator<TransferOwnershipRequest>
{
    public TransferOwnershipRequestValidator()
    {
        RuleFor(x => x.NewOwnerId)
            .NotEmpty().WithMessage("New owner ID is required");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required for verification");
    }
}

public class UpdateOrganizationSettingsRequestValidator : AbstractValidator<UpdateOrganizationSettingsRequest>
{
    public UpdateOrganizationSettingsRequestValidator()
    {
        RuleFor(x => x.DefaultReminderDays)
            .Must(days => days == null || days.Count <= 5)
            .WithMessage("Maximum 5 reminder days allowed")
            .Must(days => days == null || days.All(d => d >= 1 && d <= 30))
            .WithMessage("Reminder days must be between 1 and 30")
            .When(x => x.DefaultReminderDays != null);

        RuleFor(x => x.Timezone)
            .Must(BeValidTimezone).WithMessage("Invalid timezone")
            .When(x => !string.IsNullOrEmpty(x.Timezone));

        RuleFor(x => x.Language)
            .Must(lang => new[] { "en", "hi" }.Contains(lang))
            .WithMessage("Language must be 'en' or 'hi'")
            .When(x => !string.IsNullOrEmpty(x.Language));

        RuleFor(x => x.DateFormat)
            .Must(format => new[] { "DD/MM/YYYY", "MM/DD/YYYY", "YYYY-MM-DD" }.Contains(format))
            .WithMessage("Invalid date format")
            .When(x => !string.IsNullOrEmpty(x.DateFormat));
    }

    private static bool BeValidTimezone(string? timezone)
    {
        if (string.IsNullOrEmpty(timezone)) return true;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch
        {
            // Also accept IANA timezone IDs
            return timezone.Contains('/') || timezone == "UTC";
        }
    }
}

public class SwitchOrganizationRequestValidator : AbstractValidator<SwitchOrganizationRequest>
{
    public SwitchOrganizationRequestValidator()
    {
        RuleFor(x => x.OrganizationId)
            .NotEmpty().WithMessage("Organization ID is required");
    }
}
