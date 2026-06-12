using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Service for tracking notification delivery status and analytics
/// </summary>
public class DeliveryTrackingService : IDeliveryTrackingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DeliveryTrackingService> _logger;

    public DeliveryTrackingService(
        ApplicationDbContext dbContext,
        ILogger<DeliveryTrackingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(
        string channel,
        string messageId,
        string status,
        DateTime timestamp,
        string? errorReason = null,
        CancellationToken cancellationToken = default)
    {
        var delivery = await _dbContext.NotificationDeliveries
            .FirstOrDefaultAsync(d => d.Channel == channel && d.ProviderMessageId == messageId, cancellationToken);

        if (delivery == null)
        {
            _logger.LogWarning("Delivery not found for {Channel} message {MessageId}", channel, messageId);
            return;
        }

        // Update status based on event type
        switch (status)
        {
            case DeliveryStatus.Delivered:
                delivery.Status = DeliveryStatus.Delivered;
                delivery.DeliveredAt = timestamp;
                break;

            case DeliveryStatus.Opened:
                delivery.Status = DeliveryStatus.Opened;
                delivery.OpenedAt = timestamp;
                break;

            case DeliveryStatus.Clicked:
                delivery.ClickedAt = timestamp;
                break;

            case DeliveryStatus.Failed:
            case DeliveryStatus.Bounced:
                delivery.Status = status;
                delivery.FailedAt = timestamp;
                delivery.FailureReason = errorReason;
                break;

            case DeliveryStatus.Sent:
            case DeliveryStatus.Queued:
                if (delivery.Status == DeliveryStatus.Pending)
                {
                    delivery.Status = status;
                    delivery.SentAt = timestamp;
                }
                break;
        }

        delivery.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Updated delivery {DeliveryId} status to {Status}", delivery.Id, status);
    }

    /// <inheritdoc />
    public async Task RecordClickAsync(
        string channel,
        string messageId,
        string? url = null,
        CancellationToken cancellationToken = default)
    {
        var delivery = await _dbContext.NotificationDeliveries
            .FirstOrDefaultAsync(d => d.Channel == channel && d.ProviderMessageId == messageId, cancellationToken);

        if (delivery == null)
            return;

        delivery.ClickedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(url))
        {
            delivery.Metadata["lastClickedUrl"] = url;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NotificationMetricsDto> GetMetricsAsync(
        Guid? organizationId = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var query = _dbContext.NotificationDeliveries
            .AsNoTracking()
            .Include(d => d.Notification)
            .Where(d => d.CreatedAt >= fromDate && d.CreatedAt <= toDate);

        if (organizationId.HasValue)
        {
            query = query.Where(d => d.Notification.OrganizationId == organizationId);
        }

        var deliveries = await query.ToListAsync(cancellationToken);

        // Calculate channel metrics
        var byChannel = new Dictionary<string, ChannelMetricsDto>();
        foreach (var channel in NotificationChannel.All)
        {
            var channelDeliveries = deliveries.Where(d => d.Channel == channel).ToList();
            if (!channelDeliveries.Any())
                continue;

            var sent = channelDeliveries.Count;
            var delivered = channelDeliveries.Count(d => d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.Opened);
            var opened = channelDeliveries.Count(d => d.OpenedAt.HasValue);
            var clicked = channelDeliveries.Count(d => d.ClickedAt.HasValue);
            var failed = channelDeliveries.Count(d => d.Status == DeliveryStatus.Failed);
            var bounced = channelDeliveries.Count(d => d.Status == DeliveryStatus.Bounced);

            var deliveryTimes = channelDeliveries
                .Where(d => d.SentAt.HasValue && d.DeliveredAt.HasValue)
                .Select(d => (d.DeliveredAt!.Value - d.SentAt!.Value).TotalMilliseconds)
                .ToList();

            byChannel[channel] = new ChannelMetricsDto(
                sent,
                delivered,
                opened,
                clicked,
                failed,
                bounced,
                sent > 0 ? (double)delivered / sent * 100 : 0,
                delivered > 0 ? (double)opened / delivered * 100 : 0,
                delivered > 0 ? (double)clicked / delivered * 100 : 0,
                sent > 0 ? (double)bounced / sent * 100 : 0,
                deliveryTimes.Any() ? deliveryTimes.Average() : 0);
        }

        // Calculate type metrics
        var byType = new Dictionary<string, TypeMetricsDto>();
        var typeGroups = deliveries.GroupBy(d => d.Notification.Type);

        foreach (var group in typeGroups)
        {
            var typeDeliveries = group.ToList();
            var sent = typeDeliveries.Count;
            var delivered = typeDeliveries.Count(d => d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.Opened);
            var opened = typeDeliveries.Count(d => d.OpenedAt.HasValue);
            var clicked = typeDeliveries.Count(d => d.ClickedAt.HasValue);

            byType[group.Key] = new TypeMetricsDto(
                sent,
                delivered,
                opened,
                clicked,
                delivered > 0 ? (double)(opened + clicked) / delivered * 100 : 0);
        }

        // Overall metrics
        var totalSent = deliveries.Count;
        var totalDelivered = deliveries.Count(d => d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.Opened);
        var totalFailed = deliveries.Count(d => d.Status == DeliveryStatus.Failed);
        var totalOpened = deliveries.Count(d => d.OpenedAt.HasValue);
        var totalClicked = deliveries.Count(d => d.ClickedAt.HasValue);

        return new NotificationMetricsDto(
            byChannel,
            byType,
            totalSent,
            totalDelivered,
            totalFailed,
            totalSent > 0 ? (double)totalDelivered / totalSent * 100 : 0,
            totalDelivered > 0 ? (double)totalOpened / totalDelivered * 100 : 0,
            totalDelivered > 0 ? (double)totalClicked / totalDelivered * 100 : 0);
    }
}

/// <summary>
/// Service for managing notification templates
/// </summary>
public class NotificationTemplateService : INotificationTemplateService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<NotificationTemplateService> _logger;

    public NotificationTemplateService(
        ApplicationDbContext dbContext,
        ILogger<NotificationTemplateService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationTemplate?> GetTemplateAsync(
        string type,
        string channel,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.Type == type && t.Channel == channel && t.Language == language && t.IsActive)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RenderedTemplate> RenderAsync(
        string type,
        string channel,
        Dictionary<string, object> variables,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        var template = await GetTemplateAsync(type, channel, language, cancellationToken);

        if (template == null)
        {
            // Fall back to English if language-specific template not found
            if (language != "en")
            {
                template = await GetTemplateAsync(type, channel, "en", cancellationToken);
            }

            if (template == null)
            {
                throw new InvalidOperationException($"Template not found for {type}/{channel}/{language}");
            }
        }

        var renderedBody = RenderVariables(template.Body, variables);
        var renderedSubject = template.Subject != null ? RenderVariables(template.Subject, variables) : null;
        var title = ExtractTitle(type, variables);

        return new RenderedTemplate(renderedSubject, renderedBody, title);
    }

    /// <inheritdoc />
    public async Task<NotificationTemplateDto> UpsertTemplateAsync(
        UpsertTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.NotificationTemplates
            .Where(t => t.Type == request.Type && t.Channel == request.Channel && t.Language == request.Language)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

        NotificationTemplate template;

        if (existing != null)
        {
            // Create new version
            template = new NotificationTemplate
            {
                Type = request.Type,
                Channel = request.Channel,
                Language = request.Language,
                Version = existing.Version + 1,
                Subject = request.Subject,
                Body = request.Body,
                Metadata = request.Metadata ?? new Dictionary<string, object>(),
                IsActive = true
            };

            // Deactivate old version
            existing.IsActive = false;
        }
        else
        {
            template = new NotificationTemplate
            {
                Type = request.Type,
                Channel = request.Channel,
                Language = request.Language,
                Version = 1,
                Subject = request.Subject,
                Body = request.Body,
                Metadata = request.Metadata ?? new Dictionary<string, object>(),
                IsActive = true
            };
        }

        _dbContext.NotificationTemplates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created template {Type}/{Channel}/{Language} v{Version}",
            template.Type, template.Channel, template.Language, template.Version);

        return MapToDto(template);
    }

    /// <inheritdoc />
    public async Task<List<NotificationTemplateDto>> GetAllTemplatesAsync(
        string? type = null,
        string? channel = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.NotificationTemplates.AsNoTracking().Where(t => t.IsActive);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(t => t.Type == type);

        if (!string.IsNullOrEmpty(channel))
            query = query.Where(t => t.Channel == channel);

        var templates = await query
            .OrderBy(t => t.Type)
            .ThenBy(t => t.Channel)
            .ThenBy(t => t.Language)
            .ToListAsync(cancellationToken);

        return templates.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task SeedDefaultTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var existingCount = await _dbContext.NotificationTemplates.CountAsync(cancellationToken);
        if (existingCount > 0)
            return;

        // Seed basic SMS templates
        var smsTemplates = new[]
        {
            (NotificationType.Deadline1Day, "⚠️ URGENT: Notice #{noticeNumber} due TOMORROW ({deadline}). Risk: ₹{demandAmount}. Take action now: {actionUrl}"),
            (NotificationType.DeadlineToday, "🚨 CRITICAL: Notice #{noticeNumber} due TODAY! Immediate action required. {actionUrl}"),
            (NotificationType.DeadlineMissed, "⛔ OVERDUE: Notice #{noticeNumber} deadline missed ({daysOverdue} days ago). Respond immediately. {actionUrl}"),
            (NotificationType.NoticeHighRisk, "🔴 HIGH RISK: Notice #{noticeNumber} detected. Demand: ₹{demandAmount}. Review now: {actionUrl}"),
            (NotificationType.PasswordReset, "Your EffortlessInsight OTP is {otp}. Valid for 10 minutes. Do not share this code.")
        };

        foreach (var (type, body) in smsTemplates)
        {
            _dbContext.NotificationTemplates.Add(new NotificationTemplate
            {
                Type = type,
                Channel = NotificationChannel.Sms,
                Language = "en",
                Version = 1,
                Body = body,
                IsActive = true
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} default notification templates", smsTemplates.Length);
    }

    private static string RenderVariables(string template, Dictionary<string, object> variables)
    {
        var result = template;

        foreach (var kvp in variables)
        {
            var placeholder = "{{" + kvp.Key + "}}";
            var value = kvp.Value?.ToString() ?? "";
            result = result.Replace(placeholder, value);

            // Also handle {varName} format
            var simplePlaceholder = "{" + kvp.Key + "}";
            result = result.Replace(simplePlaceholder, value);
        }

        return result;
    }

    private static string ExtractTitle(string type, Dictionary<string, object> variables)
    {
        var noticeNumber = variables.GetValueOrDefault("noticeNumber")?.ToString() ?? "";

        return type switch
        {
            NotificationType.Deadline1Day => $"📅 Deadline Tomorrow - Notice #{noticeNumber}",
            NotificationType.Deadline3Day => $"📅 Deadline in 3 Days - Notice #{noticeNumber}",
            NotificationType.Deadline7Day => $"📅 Upcoming Deadline - Notice #{noticeNumber}",
            NotificationType.DeadlineToday => $"🚨 Deadline Today - Notice #{noticeNumber}",
            NotificationType.DeadlineMissed => $"⚠️ Deadline Missed - Notice #{noticeNumber}",
            NotificationType.NoticeHighRisk => $"🔴 High-Risk Notice - #{noticeNumber}",
            NotificationType.NoticeUploaded => $"📤 Notice Uploaded - #{noticeNumber}",
            NotificationType.NoticeAnalyzed => $"✅ Analysis Complete - Notice #{noticeNumber}",
            NotificationType.TaskAssigned => "📋 New Task Assigned",
            NotificationType.TaskOverdue => "⚠️ Task Overdue",
            NotificationType.UserMentioned => "💬 You were mentioned",
            NotificationType.DocumentRequested => "📄 Document Requested",
            NotificationType.Welcome => "👋 Welcome to EffortlessInsight",
            _ => "Notification"
        };
    }

    private static NotificationTemplateDto MapToDto(NotificationTemplate t) =>
        new(t.Id, t.Type, t.Channel, t.Language, t.Version, t.Subject, t.Body, t.Metadata, t.IsActive, t.CreatedAt, t.UpdatedAt);
}
