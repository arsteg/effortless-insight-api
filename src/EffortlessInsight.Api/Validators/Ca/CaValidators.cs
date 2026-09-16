using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Auth;
using FluentValidation;

namespace EffortlessInsight.Api.Validators.Ca;

/// <summary>
/// Validator for CA registration requests.
/// </summary>
public class CaRegisterRequestValidator : AbstractValidator<CaRegisterRequest>
{
    private readonly IPasswordStrengthService? _passwordStrengthService;

    public CaRegisterRequestValidator() : this(null) { }

    public CaRegisterRequestValidator(IPasswordStrengthService? passwordStrengthService)
    {
        _passwordStrengthService = passwordStrengthService;

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email cannot exceed 255 characters");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters")
            .Matches(@"^[\p{L}\s\-'\.]+$").WithMessage("Name contains invalid characters");

        RuleFor(x => x.Mobile)
            .Matches(@"^[6-9]\d{9}$").WithMessage("Invalid Indian mobile number (10 digits, starting with 6-9)")
            .When(x => !string.IsNullOrEmpty(x.Mobile));

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(128).WithMessage("Password cannot exceed 128 characters")
            .Must(HaveUppercase).WithMessage("Password must contain at least one uppercase letter")
            .Must(HaveLowercase).WithMessage("Password must contain at least one lowercase letter")
            .Must(HaveDigit).WithMessage("Password must contain at least one number")
            .Must(HaveSpecialCharacter).WithMessage("Password must contain at least one special character")
            .Must(NotBeCommonPassword).WithMessage("This password is too common. Please choose a more unique password.")
            .Must((request, password) => NotContainUserInfo(password, request.Email, request.Name))
                .WithMessage("Password should not contain your name or email address.");

        RuleFor(x => x.AcceptTerms)
            .Equal(true).WithMessage("You must accept the terms and conditions");

        RuleFor(x => x.FirmName)
            .MaximumLength(255).WithMessage("Firm name cannot exceed 255 characters")
            .When(x => !string.IsNullOrEmpty(x.FirmName));

        RuleFor(x => x.MembershipNumber)
            .MaximumLength(50).WithMessage("Membership number cannot exceed 50 characters")
            .Matches(@"^[A-Z0-9-]+$").WithMessage("Membership number can only contain letters, numbers, and hyphens")
            .When(x => !string.IsNullOrEmpty(x.MembershipNumber));
    }

    private static bool HaveUppercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsUpper);
    private static bool HaveLowercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsLower);
    private static bool HaveDigit(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);
    private static bool HaveSpecialCharacter(string password) => !string.IsNullOrEmpty(password) && password.Any(c => !char.IsLetterOrDigit(c));

    private bool NotBeCommonPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return true;
        return _passwordStrengthService == null || !_passwordStrengthService.IsCommonPassword(password);
    }

    private bool NotContainUserInfo(string password, string? email, string? name)
    {
        if (string.IsNullOrEmpty(password)) return true;
        return _passwordStrengthService == null || !_passwordStrengthService.ContainsUserInfo(password, email, name);
    }
}

/// <summary>
/// Validator for CA profile updates.
/// </summary>
public class UpdateCaProfileRequestValidator : AbstractValidator<UpdateCaProfileRequest>
{
    public UpdateCaProfileRequestValidator()
    {
        RuleFor(x => x.FirmName)
            .MaximumLength(255).WithMessage("Firm name cannot exceed 255 characters")
            .When(x => x.FirmName != null);

        RuleFor(x => x.MembershipNumber)
            .MaximumLength(50).WithMessage("Membership number cannot exceed 50 characters")
            .Matches(@"^[A-Z0-9-]+$").WithMessage("Membership number can only contain letters, numbers, and hyphens")
            .When(x => !string.IsNullOrEmpty(x.MembershipNumber));
    }
}

/// <summary>
/// Validator for CA invitation creation.
/// </summary>
public class CreateCaInvitationRequestValidator : AbstractValidator<CreateCaInvitationRequest>
{
    public CreateCaInvitationRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email cannot exceed 255 characters");

        RuleFor(x => x.Gstin)
            .NotEmpty().WithMessage("GSTIN is required")
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
                .WithMessage("Invalid GSTIN format");

        RuleFor(x => x.Message)
            .MaximumLength(1000).WithMessage("Message cannot exceed 1000 characters")
            .When(x => !string.IsNullOrEmpty(x.Message));
    }
}

/// <summary>
/// Validator for BO inviting CA.
/// </summary>
public class BoInviteCaRequestValidator : AbstractValidator<BoInviteCaRequest>
{
    public BoInviteCaRequestValidator()
    {
        RuleFor(x => x.CaEmail)
            .NotEmpty().WithMessage("CA email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email cannot exceed 255 characters");

        RuleFor(x => x.Gstins)
            .NotEmpty().WithMessage("At least one GSTIN is required")
            .Must(gstins => gstins != null && gstins.Count <= 10)
                .WithMessage("Cannot invite for more than 10 GSTINs at once");

        RuleForEach(x => x.Gstins)
            .NotEmpty().WithMessage("GSTIN cannot be empty")
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
                .WithMessage("Invalid GSTIN format");

        RuleFor(x => x.Message)
            .MaximumLength(1000).WithMessage("Message cannot exceed 1000 characters")
            .When(x => !string.IsNullOrEmpty(x.Message));
    }
}

/// <summary>
/// Validator for accepting CA invitation.
/// </summary>
public class AcceptCaInvitationRequestValidator : AbstractValidator<AcceptCaInvitationRequest>
{
    public AcceptCaInvitationRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required")
            .MaximumLength(500).WithMessage("Token is invalid");
    }
}

/// <summary>
/// Validator for declining CA invitation.
/// </summary>
public class DeclineCaInvitationRequestValidator : AbstractValidator<DeclineCaInvitationRequest>
{
    public DeclineCaInvitationRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required")
            .MaximumLength(500).WithMessage("Token is invalid");

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Reason));
    }
}

/// <summary>
/// Validator for updating client reference.
/// </summary>
public class UpdateCaClientRequestValidator : AbstractValidator<UpdateCaClientRequest>
{
    public UpdateCaClientRequestValidator()
    {
        RuleFor(x => x.ClientReference)
            .MaximumLength(100).WithMessage("Client reference cannot exceed 100 characters")
            .When(x => x.ClientReference != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000).WithMessage("Notes cannot exceed 1000 characters")
            .When(x => x.Notes != null);
    }
}

/// <summary>
/// Validator for revoking CA relationship.
/// </summary>
public class RevokeCaRelationshipRequestValidator : AbstractValidator<RevokeCaRelationshipRequest>
{
    public RevokeCaRelationshipRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Reason));
    }
}

/// <summary>
/// Validator for updating GSTIN permissions.
/// </summary>
public class UpdateCaGstinPermissionsRequestValidator : AbstractValidator<UpdateCaGstinPermissionsRequest>
{
    public UpdateCaGstinPermissionsRequestValidator()
    {
        RuleFor(x => x.Permissions)
            .NotEmpty().WithMessage("At least one permission is required")
            .Must(perms => perms != null && perms.All(p => CaPermission.IsValid(p)))
                .WithMessage($"Invalid permission. Valid permissions: {string.Join(", ", CaPermission.All)}");
    }
}

/// <summary>
/// Validator for revoking GSTIN authorization.
/// </summary>
public class RevokeCaGstinAuthorizationRequestValidator : AbstractValidator<RevokeCaGstinAuthorizationRequest>
{
    public RevokeCaGstinAuthorizationRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Reason));
    }
}

/// <summary>
/// Validator for selecting client context.
/// </summary>
public class SelectClientContextRequestValidator : AbstractValidator<SelectClientContextRequest>
{
    public SelectClientContextRequestValidator()
    {
        RuleFor(x => x.ClientRelationshipId)
            .NotEmpty().WithMessage("Client relationship ID is required");
    }
}

/// <summary>
/// Validator for notice filter.
/// </summary>
public class CaNoticeFilterDtoValidator : AbstractValidator<CaNoticeFilterDto>
{
    public CaNoticeFilterDtoValidator()
    {
        RuleFor(x => x.Gstin)
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
                .WithMessage("Invalid GSTIN format")
            .When(x => !string.IsNullOrEmpty(x.Gstin));

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Page size must be between 1 and 100");

        RuleFor(x => x.SearchTerm)
            .MaximumLength(200).WithMessage("Search term cannot exceed 200 characters")
            .When(x => !string.IsNullOrEmpty(x.SearchTerm));
    }
}

/// <summary>
/// Validator for starting sync.
/// </summary>
public class CaStartSyncRequestValidator : AbstractValidator<CaStartSyncRequest>
{
    public CaStartSyncRequestValidator()
    {
        RuleFor(x => x.Gstin)
            .NotEmpty().WithMessage("GSTIN is required")
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
                .WithMessage("Invalid GSTIN format");
    }
}

/// <summary>
/// Validator for resending invitation.
/// </summary>
public class ResendCaInvitationRequestValidator : AbstractValidator<ResendCaInvitationRequest>
{
    public ResendCaInvitationRequestValidator()
    {
        RuleFor(x => x.UpdatedMessage)
            .MaximumLength(1000).WithMessage("Message cannot exceed 1000 characters")
            .When(x => !string.IsNullOrEmpty(x.UpdatedMessage));
    }
}
