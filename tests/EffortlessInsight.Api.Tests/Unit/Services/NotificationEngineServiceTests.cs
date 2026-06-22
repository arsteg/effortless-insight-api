using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class NotificationEngineServiceTests
{
    private readonly Mock<ILogger<NotificationEngineService>> _loggerMock;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly Mock<ISmsSender> _smsSenderMock;
    private readonly Mock<IPushNotificationSender> _pushSenderMock;

    public NotificationEngineServiceTests()
    {
        _loggerMock = new Mock<ILogger<NotificationEngineService>>();
        _emailSenderMock = new Mock<IEmailSender>();
        _smsSenderMock = new Mock<ISmsSender>();
        _pushSenderMock = new Mock<IPushNotificationSender>();
    }

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SendAsync_WithValidRequest_CreatesNotificationRecord()
    {
        // Arrange
        using var context = CreateDbContext();
        var userId = Guid.NewGuid();

        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "test@example.com",
            Name = "Test User"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new NotificationEngineService(
            context,
            _loggerMock.Object,
            _emailSenderMock.Object,
            _smsSenderMock.Object,
            _pushSenderMock.Object);

        var request = new SendNotificationRequest(
            UserId: userId,
            Type: "notice_uploaded",
            Title: "Notice Uploaded",
            Body: "Your notice has been uploaded successfully",
            Channels: new[] { "in_app" },
            Data: null,
            Priority: "normal");

        // Act
        var result = await service.SendAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.NotificationId);

        var notification = await context.Notifications.FirstOrDefaultAsync(n => n.Id == result.NotificationId);
        Assert.NotNull(notification);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal("notice_uploaded", notification.Type);
        Assert.Equal("Notice Uploaded", notification.Title);
    }

    [Fact]
    public async Task SendAsync_WithNonExistentUser_ThrowsKeyNotFoundException()
    {
        // Arrange
        using var context = CreateDbContext();
        var service = new NotificationEngineService(
            context,
            _loggerMock.Object,
            _emailSenderMock.Object,
            _smsSenderMock.Object,
            _pushSenderMock.Object);

        var request = new SendNotificationRequest(
            UserId: Guid.NewGuid(),
            Type: "test",
            Title: "Test",
            Body: "Test body",
            Channels: new[] { "in_app" },
            Data: null,
            Priority: "normal");

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SendAsync(request));
    }

    [Fact]
    public async Task GetUserNotificationsAsync_ReturnsUserNotifications()
    {
        // Arrange
        using var context = CreateDbContext();
        var userId = Guid.NewGuid();

        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "test@example.com",
            Name = "Test User"
        };
        context.Users.Add(user);

        var notification1 = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = "type1",
            Title = "Notification 1",
            Body = "Body 1",
            Status = "unread",
            Priority = "normal",
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        var notification2 = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = "type2",
            Title = "Notification 2",
            Body = "Body 2",
            Status = "unread",
            Priority = "normal",
            CreatedAt = DateTime.UtcNow
        };
        context.Notifications.AddRange(notification1, notification2);
        await context.SaveChangesAsync();

        var service = new NotificationEngineService(
            context,
            _loggerMock.Object,
            _emailSenderMock.Object,
            _smsSenderMock.Object,
            _pushSenderMock.Object);

        // Act
        var result = await service.GetUserNotificationsAsync(userId, null, null, null, 1, 10);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task MarkAsReadAsync_UpdatesNotificationStatus()
    {
        // Arrange
        using var context = CreateDbContext();
        var userId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        var notification = new Notification
        {
            Id = notificationId,
            UserId = userId,
            Type = "test",
            Title = "Test",
            Body = "Body",
            Status = "unread",
            Priority = "normal"
        };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();

        var service = new NotificationEngineService(
            context,
            _loggerMock.Object,
            _emailSenderMock.Object,
            _smsSenderMock.Object,
            _pushSenderMock.Object);

        // Act
        var result = await service.MarkAsReadAsync(notificationId, userId);

        // Assert
        Assert.True(result.Success);

        var updatedNotification = await context.Notifications.FindAsync(notificationId);
        Assert.Equal("read", updatedNotification!.Status);
        Assert.NotNull(updatedNotification.ReadAt);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        using var context = CreateDbContext();
        var userId = Guid.NewGuid();

        context.Notifications.AddRange(
            new Notification { Id = Guid.NewGuid(), UserId = userId, Type = "t", Title = "T", Body = "B", Status = "unread", Priority = "normal" },
            new Notification { Id = Guid.NewGuid(), UserId = userId, Type = "t", Title = "T", Body = "B", Status = "unread", Priority = "normal" },
            new Notification { Id = Guid.NewGuid(), UserId = userId, Type = "t", Title = "T", Body = "B", Status = "read", Priority = "normal" }
        );
        await context.SaveChangesAsync();

        var service = new NotificationEngineService(
            context,
            _loggerMock.Object,
            _emailSenderMock.Object,
            _smsSenderMock.Object,
            _pushSenderMock.Object);

        // Act
        var count = await service.GetUnreadCountAsync(userId);

        // Assert
        Assert.Equal(2, count);
    }
}
