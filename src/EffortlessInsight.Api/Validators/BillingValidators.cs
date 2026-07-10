using EffortlessInsight.Api.DTOs;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

public class CreatePlanRequestValidator : AbstractValidator<CreatePlanRequest>
{
    public CreatePlanRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Plan code is required")
            .Matches("^[a-z0-9_]+$").WithMessage("Code must contain only lowercase letters, numbers, and underscores")
            .MaximumLength(50).WithMessage("Code must not exceed 50 characters");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Plan name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required")
            .MaximumLength(100).WithMessage("Display name must not exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters")
            .When(x => x.Description != null);

        // Pricing validation
        RuleFor(x => x.PricingMonthly)
            .GreaterThanOrEqualTo(0).WithMessage("Monthly pricing must be 0 or positive")
            .When(x => x.PricingMonthly.HasValue);

        RuleFor(x => x.PricingAnnually)
            .GreaterThanOrEqualTo(0).WithMessage("Annual pricing must be 0 or positive")
            .When(x => x.PricingAnnually.HasValue);

        RuleFor(x => x.PerSeatMonthly)
            .GreaterThanOrEqualTo(0).WithMessage("Per-seat monthly pricing must be 0 or positive")
            .When(x => x.PerSeatMonthly.HasValue);

        RuleFor(x => x.PerSeatAnnually)
            .GreaterThanOrEqualTo(0).WithMessage("Per-seat annual pricing must be 0 or positive")
            .When(x => x.PerSeatAnnually.HasValue);

        RuleFor(x => x.StartingAt)
            .GreaterThanOrEqualTo(0).WithMessage("Starting price must be 0 or positive")
            .When(x => x.StartingAt.HasValue);

        // For contact sales plans, pricing is optional
        // For regular plans, at least one pricing must be provided
        RuleFor(x => x)
            .Must(x => x.ContactSales || x.PricingMonthly.HasValue || x.PricingAnnually.HasValue)
            .WithMessage("Either set ContactSales flag or provide at least monthly or annual pricing");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required")
            .Length(3).WithMessage("Currency must be a 3-letter code (e.g., INR, USD)");

        // Limits validation
        RuleFor(x => x.Limits.NoticesPerMonth)
            .Must(v => v == -1 || v >= 0).WithMessage("Notices per month must be -1 (unlimited) or a positive number");

        RuleFor(x => x.Limits.Users)
            .Must(v => v == -1 || v >= 0).WithMessage("Users must be -1 (unlimited) or a positive number");

        RuleFor(x => x.Limits.StorageGb)
            .Must(v => v == -1 || v >= 0).WithMessage("Storage must be -1 (unlimited) or a positive number");

        RuleFor(x => x.Limits.OrganizationsCount)
            .GreaterThan(0).WithMessage("Organizations count must be at least 1");

        RuleFor(x => x.Limits.ApiCalls)
            .Must(v => v == -1 || v >= 0).WithMessage("API calls must be -1 (unlimited) or a positive number");

        // Settings validation
        RuleFor(x => x.TrialDays)
            .GreaterThanOrEqualTo(0).WithMessage("Trial days must be 0 or positive");

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Sort order must be 0 or positive");

        RuleFor(x => x.Features)
            .NotNull().WithMessage("Features list is required");
    }
}

public class UpdatePlanRequestValidator : AbstractValidator<UpdatePlanRequest>
{
    public UpdatePlanRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters")
            .When(x => x.Name != null);

        RuleFor(x => x.DisplayName)
            .MaximumLength(100).WithMessage("Display name must not exceed 100 characters")
            .When(x => x.DisplayName != null);

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters")
            .When(x => x.Description != null);

        // Pricing validation
        RuleFor(x => x.PricingMonthly)
            .GreaterThanOrEqualTo(0).WithMessage("Monthly pricing must be 0 or positive")
            .When(x => x.PricingMonthly.HasValue);

        RuleFor(x => x.PricingAnnually)
            .GreaterThanOrEqualTo(0).WithMessage("Annual pricing must be 0 or positive")
            .When(x => x.PricingAnnually.HasValue);

        RuleFor(x => x.PerSeatMonthly)
            .GreaterThanOrEqualTo(0).WithMessage("Per-seat monthly pricing must be 0 or positive")
            .When(x => x.PerSeatMonthly.HasValue);

        RuleFor(x => x.PerSeatAnnually)
            .GreaterThanOrEqualTo(0).WithMessage("Per-seat annual pricing must be 0 or positive")
            .When(x => x.PerSeatAnnually.HasValue);

        RuleFor(x => x.StartingAt)
            .GreaterThanOrEqualTo(0).WithMessage("Starting price must be 0 or positive")
            .When(x => x.StartingAt.HasValue);

        RuleFor(x => x.Currency)
            .Length(3).WithMessage("Currency must be a 3-letter code (e.g., INR, USD)")
            .When(x => x.Currency != null);

        // Limits validation (when provided)
        RuleFor(x => x.Limits!.NoticesPerMonth)
            .Must(v => v == -1 || v >= 0).WithMessage("Notices per month must be -1 (unlimited) or a positive number")
            .When(x => x.Limits != null);

        RuleFor(x => x.Limits!.Users)
            .Must(v => v == -1 || v >= 0).WithMessage("Users must be -1 (unlimited) or a positive number")
            .When(x => x.Limits != null);

        RuleFor(x => x.Limits!.StorageGb)
            .Must(v => v == -1 || v >= 0).WithMessage("Storage must be -1 (unlimited) or a positive number")
            .When(x => x.Limits != null);

        RuleFor(x => x.Limits!.OrganizationsCount)
            .GreaterThan(0).WithMessage("Organizations count must be at least 1")
            .When(x => x.Limits != null);

        RuleFor(x => x.Limits!.ApiCalls)
            .Must(v => v == -1 || v >= 0).WithMessage("API calls must be -1 (unlimited) or a positive number")
            .When(x => x.Limits != null);

        // Settings validation
        RuleFor(x => x.TrialDays)
            .GreaterThanOrEqualTo(0).WithMessage("Trial days must be 0 or positive")
            .When(x => x.TrialDays.HasValue);

        RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Sort order must be 0 or positive")
            .When(x => x.SortOrder.HasValue);
    }
}
