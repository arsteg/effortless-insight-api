using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Notifications;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class BillingNotificationServiceTests
{
    private readonly INotificationEngineService _notificationEngine;
    private readonly ILogger<BillingNotificationService> _logger;
    private readonly BillingNotificationService _sut;

    public BillingNotificationServiceTests()
    {
        _notificationEngine = Substitute.For<INotificationEngineService>();
        _logger = Substitute.For<ILogger<BillingNotificationService>>();

        _sut = new BillingNotificationService(_notificationEngine, _logger);

        // Default success response
        _notificationEngine.SendAsync(Arg.Any<SendNotificationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendNotificationResponse(Guid.NewGuid(), new List<DeliveryResultDto>()));
    }

    #region Trial Notifications

    [Fact]
    public async Task SendTrialStartedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var trialEndDate = DateTime.UtcNow.AddDays(14);

        // Act
        await _sut.SendTrialStartedAsync(userId, "Professional", trialEndDate);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "trial_started" &&
                r.Data.ContainsKey("planName") &&
                r.Data["planName"].ToString() == "Professional"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTrialEndingAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var trialEndDate = DateTime.UtcNow.AddDays(3);

        // Act
        await _sut.SendTrialEndingAsync(userId, "Starter", trialEndDate, 3);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "trial_ending" &&
                (int)r.Data["daysRemaining"] == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendTrialEndedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendTrialEndedAsync(userId, "Starter");

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "trial_ended"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Subscription Notifications

    [Fact]
    public async Task SendSubscriptionActivatedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendSubscriptionActivatedAsync(userId, "Professional", 2499.00m, "monthly");

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "subscription_activated" &&
                r.Data["billingCycle"].ToString() == "monthly"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendSubscriptionCancelledAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var endDate = DateTime.UtcNow.AddDays(30);

        // Act
        await _sut.SendSubscriptionCancelledAsync(userId, "Starter", endDate);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "subscription_cancelled" &&
                r.Data.ContainsKey("daysRemaining")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendSubscriptionReactivatedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendSubscriptionReactivatedAsync(userId, "Professional");

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "subscription_reactivated"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Plan Change Notifications

    [Fact]
    public async Task SendPlanUpgradedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendPlanUpgradedAsync(userId, "Starter", "Professional", 1500.00m);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "plan_upgraded" &&
                r.Data["oldPlanName"].ToString() == "Starter" &&
                r.Data["newPlanName"].ToString() == "Professional"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPlanDowngradedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var effectiveDate = DateTime.UtcNow.AddDays(30);

        // Act
        await _sut.SendPlanDowngradedAsync(userId, "Professional", "Starter", effectiveDate);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "plan_downgraded"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Payment Notifications

    [Fact]
    public async Task SendPaymentSuccessAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendPaymentSuccessAsync(userId, 2499.00m, "INV-2026-000001", "Professional");

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "payment_success" &&
                r.Data["invoiceNumber"].ToString() == "INV-2026-000001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPaymentFailedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendPaymentFailedAsync(userId, 999.00m, "Starter", "Card declined", 2);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "payment_failed" &&
                r.Data["reason"].ToString() == "Card declined" &&
                (int)r.Data["retryCount"] == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPaymentRetryAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var nextRetryDate = DateTime.UtcNow.AddDays(1);

        // Act
        await _sut.SendPaymentRetryAsync(userId, 999.00m, "Starter", nextRetryDate, 1);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "payment_retry" &&
                (int)r.Data["attemptNumber"] == 1),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Invoice Notifications

    [Fact]
    public async Task SendInvoiceReadyAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendInvoiceReadyAsync(userId, "INV-2026-000001", 2499.00m, "/api/v1/invoices/123/pdf");

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "invoice_ready" &&
                r.Data["downloadUrl"].ToString() == "/api/v1/invoices/123/pdf"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Usage Warning Notifications

    [Fact]
    public async Task SendUsageWarning80Async_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendUsageWarning80Async(userId, "notices", 80, 100);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "usage_warning_80" &&
                r.Data["resourceType"].ToString() == "notices" &&
                (int)r.Data["currentUsage"] == 80 &&
                (int)r.Data["limit"] == 100 &&
                (int)r.Data["percentage"] == 80),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendUsageWarning90Async_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendUsageWarning90Async(userId, "storage", 45, 50);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "usage_warning_90" &&
                (int)r.Data["percentage"] == 90),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendUsageLimitReachedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendUsageLimitReachedAsync(userId, "notices", 100);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "usage_limit_reached" &&
                r.Data["resourceType"].ToString() == "notices" &&
                (int)r.Data["limit"] == 100),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Other Notifications

    [Fact]
    public async Task SendRenewalReminderAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var renewalDate = DateTime.UtcNow.AddDays(7);

        // Act
        await _sut.SendRenewalReminderAsync(userId, "Professional", 2499.00m, renewalDate);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "renewal_reminder" &&
                r.Data.ContainsKey("daysUntilRenewal")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendSeatsAddedAsync_ShouldSendCorrectNotification()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendSeatsAddedAsync(userId, 3, 8, 300.00m);

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.UserId == userId &&
                r.Type == "seats_added" &&
                (int)r.Data["seatsAdded"] == 3 &&
                (int)r.Data["totalSeats"] == 8),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task SendNotification_WhenEngineFails_ShouldNotThrow()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _notificationEngine.SendAsync(Arg.Any<SendNotificationRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Notification service unavailable"));

        // Act
        var act = () => _sut.SendTrialStartedAsync(userId, "Starter", DateTime.UtcNow.AddDays(14));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendNotification_ShouldNotOverridePreferences()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendPaymentSuccessAsync(userId, 999.00m, "INV-123", "Starter");

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r => r.OverridePreferences == false),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Amount Formatting

    [Fact]
    public async Task SendPaymentSuccessAsync_ShouldFormatAmountWithRupeeSymbol()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _sut.SendPaymentSuccessAsync(userId, 2499.99m, "INV-123", "Professional");

        // Assert
        await _notificationEngine.Received(1).SendAsync(
            Arg.Is<SendNotificationRequest>(r =>
                r.Data["amount"].ToString()!.Contains("2,499.99")),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
