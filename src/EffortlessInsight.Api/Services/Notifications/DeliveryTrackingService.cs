using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Lightweight projection of a delivery row for metrics aggregation (BE-25).
/// </summary>
internal sealed record DeliveryMetricRow(
    string Channel,
    string Status,
    DateTime? OpenedAt,
    DateTime? ClickedAt,
    DateTime? SentAt,
    DateTime? DeliveredAt,
    string Type);

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
            .Where(d => d.CreatedAt >= fromDate && d.CreatedAt <= toDate);

        if (organizationId.HasValue)
        {
            query = query.Where(d => d.Notification.OrganizationId == organizationId);
        }

        // Project to only the columns the aggregation needs, so we don't load
        // full delivery entities plus the whole Notification navigation into
        // memory for a wide date range (audit BE-25).
        var deliveries = await query
            .Select(d => new DeliveryMetricRow(
                d.Channel, d.Status, d.OpenedAt, d.ClickedAt, d.SentAt, d.DeliveredAt, d.Notification.Type))
            .ToListAsync(cancellationToken);

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
        var typeGroups = deliveries.GroupBy(d => d.Type);

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
        return await TryRenderAsync(type, channel, variables, language, cancellationToken)
            ?? throw new InvalidOperationException($"Template not found for {type}/{channel}/{language}");
    }

    /// <inheritdoc />
    public async Task<RenderedTemplate?> TryRenderAsync(
        string type,
        string channel,
        Dictionary<string, object> variables,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        var template = await GetTemplateAsync(type, channel, language, cancellationToken);

        // Fall back to English if a language-specific template isn't found.
        if (template == null && language != "en")
        {
            template = await GetTemplateAsync(type, channel, "en", cancellationToken);
        }

        if (template == null)
            return null;

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
        // Idempotent per (type, channel, language): existing rows are left
        // untouched (including admin edits), only missing combinations are
        // seeded. This lets new template types ship to existing deployments.
        var existingKeys = (await _dbContext.NotificationTemplates
                .AsNoTracking()
                .Select(t => new { t.Type, t.Channel, t.Language })
                .ToListAsync(cancellationToken))
            .Select(k => (k.Type, k.Channel, k.Language))
            .ToHashSet();

        var seeded = 0;

        void AddIfMissing(string type, string channel, string language, string? subject, string body)
        {
            if (!existingKeys.Add((type, channel, language)))
                return;

            _dbContext.NotificationTemplates.Add(new NotificationTemplate
            {
                Type = type,
                Channel = channel,
                Language = language,
                Version = 1,
                Subject = subject,
                Body = body,
                IsActive = true
            });
            seeded++;
        }

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
            AddIfMissing(type, NotificationChannel.Sms, "en", null, body);
        }

        // Seed channel-agnostic ("default") content so the engine's primary
        // render actually resolves a template instead of always falling back to
        // hardcoded content (audit BE-08). English + Hindi.
        var defaultTemplates = new (string Type, string Subject, string BodyEn, string BodyHi)[]
        {
            (NotificationType.Deadline1Day, "Deadline Tomorrow",
                "Notice #{noticeNumber} is due tomorrow ({deadline}). Take action to avoid penalties.",
                "नोटिस #{noticeNumber} की समय-सीमा कल ({deadline}) है। जुर्माने से बचने के लिए कार्रवाई करें।"),
            (NotificationType.DeadlineToday, "Deadline Today",
                "Notice #{noticeNumber} is due TODAY. Immediate action required.",
                "नोटिस #{noticeNumber} की समय-सीमा आज है। तुरंत कार्रवाई आवश्यक है।"),
            (NotificationType.DeadlineMissed, "Deadline Missed",
                "Notice #{noticeNumber} deadline has passed. Please respond immediately.",
                "नोटिस #{noticeNumber} की समय-सीमा बीत चुकी है। कृपया तुरंत उत्तर दें।"),
            (NotificationType.NoticeHighRisk, "High-Risk Notice Detected",
                "High-risk notice #{noticeNumber} detected. Review required.",
                "उच्च-जोखिम वाला नोटिस #{noticeNumber} पाया गया। समीक्षा आवश्यक है।"),
            (NotificationType.TaskAssigned, "Task Assigned",
                "You have been assigned a new task for Notice #{noticeNumber}.",
                "आपको नोटिस #{noticeNumber} के लिए एक नया कार्य सौंपा गया है।"),
        };

        foreach (var t in defaultTemplates)
        {
            AddIfMissing(t.Type, NotificationChannel.Default, "en", t.Subject, t.BodyEn);
            AddIfMissing(t.Type, NotificationChannel.Default, "hi", t.Subject, t.BodyHi);
        }


        // Billing notification content (channel-agnostic "default" channel —
        // the engine renders this once and reuses it for email/in-app/push).
        // Placeholders match the data keys emitted by BillingNotificationService;
        // amounts arrive pre-formatted (e.g. "₹1,999.00").
        var billingTemplates = new (string Type, string Subject, string Body)[]
        {
            ("trial_started", "Your {{planName}} trial has started",
                "Welcome! Your free trial of the {{planName}} plan is now active until {{trialEndDate}} ({{trialDays}} days). Explore all features and set up your organization."),
            ("trial_ending", "Your trial ends in {{daysRemaining}} day(s)",
                "Your {{planName}} trial ends on {{trialEndDate}} — {{daysRemaining}} day(s) left. Subscribe now to keep uninterrupted access to your GST notices and reports."),
            ("trial_ended", "Your {{planName}} trial has ended",
                "Your free trial of the {{planName}} plan has ended. Subscribe to regain full access to your data and features."),
            ("subscription_activated", "Subscription activated: {{planName}}",
                "Your {{planName}} subscription ({{billingCycle}}) is now active at {{amount}}. Thank you for subscribing!"),
            ("subscription_cancelled", "Subscription cancelled",
                "Your {{planName}} subscription has been cancelled. You keep access until {{endDate}} ({{daysRemaining}} day(s) remaining). You can reactivate any time before then."),
            ("subscription_reactivated", "Subscription reactivated",
                "Welcome back! Your {{planName}} subscription has been reactivated and billing will continue as before."),
            ("plan_upgraded", "Plan upgraded to {{newPlanName}}",
                "Your plan has been upgraded from {{oldPlanName}} to {{newPlanName}}. A prorated charge of {{proratedAmount}} applies for the current period."),
            ("plan_downgraded", "Plan change scheduled: {{newPlanName}}",
                "Your plan will change from {{oldPlanName}} to {{newPlanName}} effective {{effectiveDate}}. You keep {{oldPlanName}} features until then."),
            ("payment_success", "Payment received: {{amount}}",
                "We received your payment of {{amount}} for the {{planName}} plan. Invoice {{invoiceNumber}} is available in your billing section."),
            ("payment_failed", "Payment failed for {{planName}}",
                "Your payment of {{amount}} for the {{planName}} plan failed ({{reason}}). Attempt {{retryCount}} of {{maxRetries}}. Please update your payment method to avoid interruption."),
            ("payment_retry", "Payment retry scheduled",
                "We could not charge {{amount}} for your {{planName}} plan. We will retry on {{nextRetryDate}} (attempt {{attemptNumber}} of {{maxAttempts}}). Update your payment method if needed."),
            ("invoice_ready", "Invoice {{invoiceNumber}} is ready",
                "Your invoice {{invoiceNumber}} for {{amount}} is ready. You can download it from the billing section of your account."),
            ("usage_warning_80", "You've used 80% of your {{resourceType}} limit",
                "You have used {{currentUsage}} of {{limit}} {{resourceType}} ({{percentage}}%). {{remaining}} remaining this cycle. Consider upgrading if you need more."),
            ("usage_warning_90", "You've used 90% of your {{resourceType}} limit",
                "You have used {{currentUsage}} of {{limit}} {{resourceType}} ({{percentage}}%). Only {{remaining}} remaining this cycle. Upgrade to avoid hitting the limit."),
            ("usage_limit_reached", "{{resourceType}} limit reached",
                "You have reached your limit of {{limit}} {{resourceType}} for this billing cycle. Upgrade your plan to continue without interruption."),
            ("renewal_reminder", "Your subscription renews on {{renewalDate}}",
                "Your {{planName}} subscription renews on {{renewalDate}} ({{daysUntilRenewal}} day(s) from now) for {{amount}}. No action is needed if you wish to continue."),
            ("seats_added", "{{seatsAdded}} seat(s) added",
                "{{seatsAdded}} seat(s) were added to your subscription, for a total of {{totalSeats}}. Additional cost: {{additionalCost}}."),
        };

        foreach (var t in billingTemplates)
        {
            AddIfMissing(t.Type, NotificationChannel.Default, "en", t.Subject, t.Body);
        }

        // GST sync notification content. The SQL seed file
        // (20260707_AddGstSyncNotificationTemplates.sql) targeted a schema that
        // does not exist (snake_case table, title column) and the 'email'
        // channel the engine never renders, so these are seeded here instead.
        var gstSyncTemplates = new (string Type, string Subject, string Body)[]
        {
            (NotificationType.GstSyncNoticesSynced, "{{totalCount}} GST notice(s) synced for {{clientName}}",
                "We synced {{totalCount}} notice(s) from the GST portal for {{clientName}} ({{gstin}}): {{newCount}} new, {{updatedCount}} updated. Review them in your dashboard."),
            (NotificationType.GstSyncDailyDigest, "GST notice digest for {{date}}",
                "Daily GST sync summary for {{date}}: {{newNotices}} new notice(s) captured. {{upcomingDueDates}} notice(s) have due dates in the next 7 days."),
            (NotificationType.GstSyncFailed, "GST portal sync failed for {{clientName}}",
                "Syncing notices for {{clientName}} ({{gstin}}) has failed {{consecutiveFailures}} time(s) in a row: {{errorMessage}}. Please check the portal credentials in your sync settings."),
            (NotificationType.GstSyncDueDateReminder, "GST notice due in {{daysUntilDue}} day(s) - {{clientName}}",
                "Notice {{noticeId}} ({{noticeType}}) for {{clientName}} ({{gstin}}) is due on {{dueDate}} — {{daysUntilDue}} day(s) from now. Respond in time to avoid penalties."),
            (NotificationType.GstSyncDueDateOverdue, "OVERDUE: GST notice for {{clientName}}",
                "Notice {{noticeId}} ({{noticeType}}) for {{clientName}} ({{gstin}}) was due on {{dueDate}} and is now {{daysOverdue}} day(s) overdue. Immediate action is required."),
            (NotificationType.GstSyncExtensionDisconnected, "GST Notice Guard extension disconnected",
                "Your GST Notice Guard browser extension has disconnected, so notices are no longer being captured automatically. Open the GST portal with the extension installed to reconnect."),
            (NotificationType.GstSyncPaused, "GST sync paused for {{clientName}}",
                "Automatic notice sync for {{clientName}} ({{gstin}}) has been paused: {{reason}}. Resume it from your sync settings."),
            (NotificationType.GstSyncImportCompleted, "GST notice import completed",
                "Your notice import has finished: {{importedCount}} imported, {{failedCount}} failed out of {{totalCount}}. View them in your notices list."),
            (NotificationType.GstSyncStaleClients, "{{staleCount}} client(s) need a GST portal visit",
                "Notices for {{staleCount}} client(s) haven't refreshed in {{thresholdDays}}+ days: {{clientList}}. Log into their GST portals so the extension can capture any new notices."),
            (NotificationType.GstSyncWeeklyDigest, "Weekly GST notice summary",
                "This week across your clients: {{newNotices}} new notice(s), {{dueThisWeek}} due within 7 days, {{overdueCount}} overdue, {{staleCount}} client(s) needing a portal visit."),
        };

        foreach (var t in gstSyncTemplates)
        {
            AddIfMissing(t.Type, NotificationChannel.Default, "en", t.Subject, t.Body);
        }

        if (seeded > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Seeded {Count} notification templates", seeded);
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
