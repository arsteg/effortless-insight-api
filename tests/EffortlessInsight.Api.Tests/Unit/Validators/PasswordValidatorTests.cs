using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Validators;
using FluentValidation.TestHelper;

namespace EffortlessInsight.Api.Tests.Unit.Validators;

public class ResetPasswordRequestValidatorTests
{
    private readonly ResetPasswordRequestValidator _validator;

    public ResetPasswordRequestValidatorTests()
    {
        _validator = new ResetPasswordRequestValidator();
    }

    [Fact]
    public void Token_WhenEmpty_ShouldHaveError()
    {
        var request = new ResetPasswordRequest("", "SecureP@ss123", "SecureP@ss123");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Token)
            .WithErrorMessage("Token is required");
    }

    [Fact]
    public void Password_WhenEmpty_ShouldHaveError()
    {
        var request = new ResetPasswordRequest("token123", "", "");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password is required");
    }

    [Fact]
    public void ConfirmPassword_WhenDoesNotMatch_ShouldHaveError()
    {
        var request = new ResetPasswordRequest("token123", "SecureP@ss123", "DifferentP@ss123");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword)
            .WithErrorMessage("Passwords do not match");
    }

    [Fact]
    public void ValidRequest_ShouldPassValidation()
    {
        var request = new ResetPasswordRequest("token123", "SecureP@ss123", "SecureP@ss123");
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}

public class ChangePasswordRequestValidatorTests
{
    private readonly ChangePasswordRequestValidator _validator;

    public ChangePasswordRequestValidatorTests()
    {
        _validator = new ChangePasswordRequestValidator();
    }

    [Fact]
    public void CurrentPassword_WhenEmpty_ShouldHaveError()
    {
        var request = new ChangePasswordRequest("", "NewP@ss123", "NewP@ss123");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CurrentPassword)
            .WithErrorMessage("Current password is required");
    }

    [Fact]
    public void NewPassword_WhenSameAsCurrent_ShouldHaveError()
    {
        var request = new ChangePasswordRequest("SecureP@ss123", "SecureP@ss123", "SecureP@ss123");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.NewPassword)
            .WithErrorMessage("New password must be different from current password");
    }

    [Fact]
    public void ConfirmPassword_WhenDoesNotMatch_ShouldHaveError()
    {
        var request = new ChangePasswordRequest("OldP@ss123", "NewP@ss123", "DifferentP@ss123");
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.ConfirmPassword)
            .WithErrorMessage("Passwords do not match");
    }

    [Fact]
    public void ValidRequest_ShouldPassValidation()
    {
        var request = new ChangePasswordRequest("OldP@ss123", "NewP@ss123", "NewP@ss123");
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
