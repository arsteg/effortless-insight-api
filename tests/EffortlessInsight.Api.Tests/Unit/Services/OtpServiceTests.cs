using EffortlessInsight.Api.Services.Auth;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class OtpServiceTests
{
    private readonly IDistributedCache _cache;
    private readonly Mock<ISmsService> _smsService;
    private readonly OtpService _otpService;
    private string? _lastSentOtp;

    public OtpServiceTests()
    {
        // Fully qualified: the API project has an EffortlessInsight.Api.Options namespace
        _cache = new MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

        _smsService = new Mock<ISmsService>();
        _smsService
            .Setup(s => s.SendOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, string, int>((_, otp, _) => _lastSentOtp = otp)
            .Returns(Task.CompletedTask);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Otp:ExpiryMinutes", "5" },
                { "Otp:MaxAttempts", "3" },
                { "Otp:Length", "6" },
                { "Otp:MaxRequestsPerHour", "5" }
            })
            .Build();

        _otpService = new OtpService(
            _cache,
            _smsService.Object,
            new Mock<ILogger<OtpService>>().Object,
            configuration);
    }

    #region RequestOtpAsync

    [Fact]
    public async Task RequestOtpAsync_ShouldSendOtpViaSmsService()
    {
        var response = await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");

        response.Message.Should().Be("OTP sent successfully");
        response.MaskedMobile.Should().Be("******3210");
        response.ExpiresIn.Should().Be(300);
        _lastSentOtp.Should().NotBeNullOrEmpty().And.HaveLength(6);
        _smsService.Verify(s => s.SendOtpAsync("9876543210", It.IsAny<string>(), 5), Times.Once);
    }

    [Fact]
    public async Task RequestOtpAsync_WithinCooldown_ShouldNotResend()
    {
        await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");
        var second = await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");

        second.Message.Should().Contain("already sent");
        _smsService.Verify(
            s => s.SendOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestOtpAsync_ShouldNormalizeCountryCode()
    {
        await _otpService.RequestOtpAsync("+91 98765 43210", "signup", "127.0.0.1");

        _smsService.Verify(s => s.SendOtpAsync("9876543210", It.IsAny<string>(), 5), Times.Once);
    }

    #endregion

    #region VerifyOtpAsync

    [Fact]
    public async Task VerifyOtpAsync_WithCorrectOtp_ShouldReturnTrue()
    {
        await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");

        var result = await _otpService.VerifyOtpAsync("9876543210", _lastSentOtp!, "signup");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyOtpAsync_WithWrongOtp_ShouldReturnFalse()
    {
        await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");
        var wrong = _lastSentOtp == "000000" ? "111111" : "000000";

        var result = await _otpService.VerifyOtpAsync("9876543210", wrong, "signup");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtpAsync_IsSingleUse()
    {
        await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");

        (await _otpService.VerifyOtpAsync("9876543210", _lastSentOtp!, "signup")).Should().BeTrue();
        (await _otpService.VerifyOtpAsync("9876543210", _lastSentOtp!, "signup")).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtpAsync_WithWrongPurpose_ShouldReturnFalse()
    {
        await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");

        var result = await _otpService.VerifyOtpAsync("9876543210", _lastSentOtp!, "login");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtpAsync_AfterMaxAttempts_ShouldThrowAndInvalidate()
    {
        await _otpService.RequestOtpAsync("9876543210", "signup", "127.0.0.1");
        var wrong = _lastSentOtp == "000000" ? "111111" : "000000";

        for (var i = 0; i < 3; i++)
        {
            await _otpService.VerifyOtpAsync("9876543210", wrong, "signup");
        }

        // 4th attempt hits the attempt cap — even the correct OTP is rejected
        var act = () => _otpService.VerifyOtpAsync("9876543210", _lastSentOtp!, "signup");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("MAX_ATTEMPTS_EXCEEDED");

        // OTP was removed, so a further attempt just finds nothing
        (await _otpService.VerifyOtpAsync("9876543210", _lastSentOtp!, "signup")).Should().BeFalse();
    }

    #endregion

    #region Verification tokens

    [Fact]
    public async Task VerificationToken_IssueThenValidate_ShouldSucceed()
    {
        var token = await _otpService.IssueVerificationTokenAsync("9876543210", "signup");

        token.Should().NotBeNullOrEmpty();
        (await _otpService.ValidateVerificationTokenAsync("9876543210", "signup", token)).Should().BeTrue();
    }

    [Fact]
    public async Task VerificationToken_ValidateDoesNotConsume()
    {
        var token = await _otpService.IssueVerificationTokenAsync("9876543210", "signup");

        await _otpService.ValidateVerificationTokenAsync("9876543210", "signup", token);

        (await _otpService.ValidateVerificationTokenAsync("9876543210", "signup", token)).Should().BeTrue();
    }

    [Fact]
    public async Task VerificationToken_ConsumeIsSingleUse()
    {
        var token = await _otpService.IssueVerificationTokenAsync("9876543210", "signup");

        (await _otpService.ConsumeVerificationTokenAsync("9876543210", "signup", token)).Should().BeTrue();
        (await _otpService.ValidateVerificationTokenAsync("9876543210", "signup", token)).Should().BeFalse();
        (await _otpService.ConsumeVerificationTokenAsync("9876543210", "signup", token)).Should().BeFalse();
    }

    [Fact]
    public async Task VerificationToken_WrongToken_ShouldFail()
    {
        await _otpService.IssueVerificationTokenAsync("9876543210", "signup");

        (await _otpService.ValidateVerificationTokenAsync("9876543210", "signup", "not-the-token")).Should().BeFalse();
        (await _otpService.ValidateVerificationTokenAsync("9876543210", "signup", "")).Should().BeFalse();
    }

    [Fact]
    public async Task VerificationToken_IsBoundToMobileAndPurpose()
    {
        var token = await _otpService.IssueVerificationTokenAsync("9876543210", "signup");

        // Different mobile — the proof doesn't transfer
        (await _otpService.ValidateVerificationTokenAsync("9123456789", "signup", token)).Should().BeFalse();
        // Different purpose — a signup proof can't be reused for login
        (await _otpService.ValidateVerificationTokenAsync("9876543210", "login", token)).Should().BeFalse();
    }

    [Fact]
    public async Task VerificationToken_ShouldNormalizeMobileFormats()
    {
        var token = await _otpService.IssueVerificationTokenAsync("+91 98765 43210", "signup");

        (await _otpService.ValidateVerificationTokenAsync("9876543210", "signup", token)).Should().BeTrue();
    }

    #endregion
}
