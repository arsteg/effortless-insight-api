using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Validators;
using FluentValidation.TestHelper;

namespace EffortlessInsight.Api.Tests.Unit.Validators;

public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator;

    public LoginRequestValidatorTests()
    {
        _validator = new LoginRequestValidator();
    }

    [Fact]
    public void Email_WhenEmpty_ShouldHaveError()
    {
        var request = new LoginRequest("", "password123", false, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("Email is required");
    }

    [Fact]
    public void Email_WhenInvalidFormat_ShouldHaveError()
    {
        var request = new LoginRequest("notanemail", "password123", false, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("Invalid email format");
    }

    [Fact]
    public void Password_WhenEmpty_ShouldHaveError()
    {
        var request = new LoginRequest("test@example.com", "", false, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password is required");
    }

    [Fact]
    public void ValidRequest_ShouldPassValidation()
    {
        var request = new LoginRequest("test@example.com", "password123", true, null);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
