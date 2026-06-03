using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Validators;
using FluentValidation.TestHelper;

namespace EffortlessInsight.Api.Tests.Unit.Validators;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator;

    public RegisterRequestValidatorTests()
    {
        _validator = new RegisterRequestValidator();
    }

    #region Email Validation

    [Fact]
    public void Email_WhenEmpty_ShouldHaveError()
    {
        var request = new RegisterRequest("", "SecureP@ss123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("Email is required");
    }

    [Fact]
    public void Email_WhenInvalidFormat_ShouldHaveError()
    {
        var request = new RegisterRequest("notanemail", "SecureP@ss123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("Invalid email format");
    }

    [Fact]
    public void Email_WhenTooLong_ShouldHaveError()
    {
        var longEmail = new string('a', 250) + "@test.com";
        var request = new RegisterRequest(longEmail, "SecureP@ss123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("Email cannot exceed 255 characters");
    }

    [Fact]
    public void Email_WhenValid_ShouldNotHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    #endregion

    #region Name Validation

    [Fact]
    public void Name_WhenEmpty_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name is required");
    }

    [Fact]
    public void Name_WhenTooShort_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "J", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name must be at least 2 characters");
    }

    [Fact]
    public void Name_WhenTooLong_ShouldHaveError()
    {
        var longName = new string('a', 101);
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", longName, null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name cannot exceed 100 characters");
    }

    [Fact]
    public void Name_WhenContainsInvalidCharacters_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "John123", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name contains invalid characters");
    }

    [Theory]
    [InlineData("John Doe")]
    [InlineData("María García")]
    [InlineData("O'Connor")]
    [InlineData("Jean-Pierre")]
    [InlineData("Dr. Smith")]
    public void Name_WhenValid_ShouldNotHaveError(string name)
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", name, null, true);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    #endregion

    #region Password Validation

    [Fact]
    public void Password_WhenEmpty_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password is required");
    }

    [Fact]
    public void Password_WhenTooShort_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "Abc1@", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must be at least 8 characters");
    }

    [Fact]
    public void Password_WhenNoUppercase_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "securep@ss123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one uppercase letter");
    }

    [Fact]
    public void Password_WhenNoLowercase_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SECUREP@SS123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one lowercase letter");
    }

    [Fact]
    public void Password_WhenNoDigit_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one number");
    }

    [Fact]
    public void Password_WhenNoSpecialCharacter_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecurePass123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Password)
            .WithErrorMessage("Password must contain at least one special character");
    }

    [Theory]
    [InlineData("SecureP@ss123")]
    [InlineData("MyP@ssw0rd!")]
    [InlineData("Complex#Pass1")]
    [InlineData("Test$1234abc")]
    public void Password_WhenValid_ShouldNotHaveError(string password)
    {
        var request = new RegisterRequest("test@example.com", password, "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Password);
    }

    #endregion

    #region Mobile Validation

    [Theory]
    [InlineData("1234567890")] // Doesn't start with 6-9
    [InlineData("567890123")] // Too short
    [InlineData("56789012345")] // Too long
    [InlineData("abcdefghij")] // Not digits
    public void Mobile_WhenInvalid_ShouldHaveError(string mobile)
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "John Doe", mobile, true);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Mobile);
    }

    [Theory]
    [InlineData("6123456789")]
    [InlineData("7123456789")]
    [InlineData("8123456789")]
    [InlineData("9123456789")]
    public void Mobile_WhenValid_ShouldNotHaveError(string mobile)
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "John Doe", mobile, true);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Mobile);
    }

    [Fact]
    public void Mobile_WhenNull_ShouldNotHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.Mobile);
    }

    #endregion

    #region AcceptTerms Validation

    [Fact]
    public void AcceptTerms_WhenFalse_ShouldHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "John Doe", null, false);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.AcceptTerms)
            .WithErrorMessage("You must accept the terms and conditions");
    }

    [Fact]
    public void AcceptTerms_WhenTrue_ShouldNotHaveError()
    {
        var request = new RegisterRequest("test@example.com", "SecureP@ss123", "John Doe", null, true);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.AcceptTerms);
    }

    #endregion

    #region Full Validation

    [Fact]
    public void ValidRequest_ShouldPassValidation()
    {
        var request = new RegisterRequest(
            Email: "test@example.com",
            Password: "SecureP@ss123",
            Name: "John Doe",
            Mobile: "9876543210",
            AcceptTerms: true
        );

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    #endregion
}
