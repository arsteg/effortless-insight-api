using EffortlessInsight.Api.DTOs;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
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
            .Must(HaveSpecialCharacter).WithMessage("Password must contain at least one special character");

        RuleFor(x => x.AcceptTerms)
            .Equal(true).WithMessage("You must accept the terms and conditions");
    }

    private static bool HaveUppercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsUpper);
    private static bool HaveLowercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsLower);
    private static bool HaveDigit(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);
    private static bool HaveSpecialCharacter(string password) => !string.IsNullOrEmpty(password) && password.Any(c => !char.IsLetterOrDigit(c));
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
}

public class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required");
    }
}

public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required");
    }
}

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(128).WithMessage("Password cannot exceed 128 characters")
            .Must(HaveUppercase).WithMessage("Password must contain at least one uppercase letter")
            .Must(HaveLowercase).WithMessage("Password must contain at least one lowercase letter")
            .Must(HaveDigit).WithMessage("Password must contain at least one number")
            .Must(HaveSpecialCharacter).WithMessage("Password must contain at least one special character");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required")
            .Equal(x => x.Password).WithMessage("Passwords do not match");
    }

    private static bool HaveUppercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsUpper);
    private static bool HaveLowercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsLower);
    private static bool HaveDigit(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);
    private static bool HaveSpecialCharacter(string password) => !string.IsNullOrEmpty(password) && password.Any(c => !char.IsLetterOrDigit(c));
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(128).WithMessage("Password cannot exceed 128 characters")
            .Must(HaveUppercase).WithMessage("Password must contain at least one uppercase letter")
            .Must(HaveLowercase).WithMessage("Password must contain at least one lowercase letter")
            .Must(HaveDigit).WithMessage("Password must contain at least one number")
            .Must(HaveSpecialCharacter).WithMessage("Password must contain at least one special character")
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from current password");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Confirm password is required")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }

    private static bool HaveUppercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsUpper);
    private static bool HaveLowercase(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsLower);
    private static bool HaveDigit(string password) => !string.IsNullOrEmpty(password) && password.Any(char.IsDigit);
    private static bool HaveSpecialCharacter(string password) => !string.IsNullOrEmpty(password) && password.Any(c => !char.IsLetterOrDigit(c));
}

public class OtpRequestValidator : AbstractValidator<OtpRequestRequest>
{
    public OtpRequestValidator()
    {
        RuleFor(x => x.Mobile)
            .NotEmpty().WithMessage("Mobile number is required")
            .Matches(@"^[6-9]\d{9}$").WithMessage("Invalid Indian mobile number (10 digits, starting with 6-9)");

        RuleFor(x => x.Purpose)
            .NotEmpty().WithMessage("Purpose is required")
            .Must(p => p == "login" || p == "verify").WithMessage("Purpose must be 'login' or 'verify'");
    }
}

public class OtpVerifyValidator : AbstractValidator<OtpVerifyRequest>
{
    public OtpVerifyValidator()
    {
        RuleFor(x => x.Mobile)
            .NotEmpty().WithMessage("Mobile number is required")
            .Matches(@"^[6-9]\d{9}$").WithMessage("Invalid Indian mobile number (10 digits, starting with 6-9)");

        RuleFor(x => x.Otp)
            .NotEmpty().WithMessage("OTP is required")
            .Matches(@"^\d{6}$").WithMessage("OTP must be 6 digits");
    }
}

public class TwoFactorVerifySetupValidator : AbstractValidator<TwoFactorVerifySetupRequest>
{
    public TwoFactorVerifySetupValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required")
            .Matches(@"^\d{6}$").WithMessage("Code must be 6 digits");
    }
}

public class TwoFactorLoginValidator : AbstractValidator<TwoFactorLoginRequest>
{
    public TwoFactorLoginValidator()
    {
        RuleFor(x => x.PartialToken)
            .NotEmpty().WithMessage("Partial token is required");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code is required")
            .Matches(@"^\d{6}$|^[A-Za-z0-9]{8}$").WithMessage("Code must be 6 digits (TOTP) or 8 characters (backup code)");
    }
}

public class TwoFactorDisableValidator : AbstractValidator<TwoFactorDisableRequest>
{
    public TwoFactorDisableValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
}
