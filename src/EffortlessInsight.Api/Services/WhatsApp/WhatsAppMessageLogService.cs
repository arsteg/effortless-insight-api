using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for logging WhatsApp messages for audit and analytics.
/// </summary>
public class WhatsAppMessageLogService : IWhatsAppMessageLogService
{
    private readonly ApplicationDbContext _db;
    private readonly IMetaWhatsAppClient _client;
    private readonly ILogger<WhatsAppMessageLogService> _logger;

    public WhatsAppMessageLogService(
        ApplicationDbContext db,
        IMetaWhatsAppClient client,
        ILogger<WhatsAppMessageLogService> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    public async Task<WhatsAppMessageLog> LogIncomingMessageAsync(
        string wamId,
        string phoneNumber,
        string messageType,
        string? content,
        string? command,
        Guid? userId,
        Guid? organizationId,
        CancellationToken ct = default)
    {
        var log = new WhatsAppMessageLog
        {
            WamId = wamId,
            PhoneNumber = _client.MaskPhoneNumber(phoneNumber),
            Direction = WhatsAppMessageDirection.Inbound,
            MessageType = messageType,
            Content = SanitizeContent(content),
            Command = command,
            UserId = userId,
            OrganizationId = organizationId,
            Status = WhatsAppMessageStatus.Delivered
        };

        _db.WhatsAppMessageLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        return log;
    }

    public async Task<WhatsAppMessageLog> LogOutgoingMessageAsync(
        string? wamId,
        string phoneNumber,
        string messageType,
        string? content,
        string? templateName,
        Guid? userId,
        Guid? organizationId,
        string status,
        string? errorCode,
        string? errorMessage,
        int? processingTimeMs,
        CancellationToken ct = default)
    {
        var log = new WhatsAppMessageLog
        {
            WamId = wamId ?? Guid.NewGuid().ToString(),
            PhoneNumber = _client.MaskPhoneNumber(phoneNumber),
            Direction = WhatsAppMessageDirection.Outbound,
            MessageType = messageType,
            Content = SanitizeContent(content),
            TemplateName = templateName,
            UserId = userId,
            OrganizationId = organizationId,
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ProcessingTimeMs = processingTimeMs
        };

        _db.WhatsAppMessageLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        return log;
    }

    public async Task UpdateMessageStatusAsync(
        string wamId,
        string status,
        DateTime timestamp,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct = default)
    {
        var log = await _db.WhatsAppMessageLogs
            .FirstOrDefaultAsync(l => l.WamId == wamId, ct);

        if (log == null)
        {
            _logger.LogDebug("Message log not found for WamId: {WamId}", wamId);
            return;
        }

        log.Status = status;
        log.UpdatedAt = DateTime.UtcNow;

        switch (status.ToLower())
        {
            case "delivered":
                log.DeliveredAt = timestamp;
                break;
            case "read":
                log.ReadAt = timestamp;
                break;
            case "failed":
                log.ErrorCode = errorCode;
                log.ErrorMessage = errorMessage;
                break;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<WhatsAppMessageLog?> GetByWamIdAsync(string wamId, CancellationToken ct = default)
    {
        return await _db.WhatsAppMessageLogs
            .FirstOrDefaultAsync(l => l.WamId == wamId, ct);
    }

    public async Task<List<WhatsAppMessageLog>> GetFailedMessagesForRetryAsync(int maxRetries, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5); // Only retry messages older than 5 minutes

        return await _db.WhatsAppMessageLogs
            .Where(l =>
                l.Direction == WhatsAppMessageDirection.Outbound &&
                l.Status == WhatsAppMessageStatus.Failed &&
                l.RetryCount < maxRetries &&
                l.CreatedAt < cutoff &&
                (l.LastRetryAt == null || l.LastRetryAt < DateTime.UtcNow.AddMinutes(-10)))
            .OrderBy(l => l.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
    }

    public async Task<WhatsAppMessageStats> GetStatisticsAsync(
        DateTime? from,
        DateTime? to,
        Guid? organizationId,
        CancellationToken ct = default)
    {
        var query = _db.WhatsAppMessageLogs.AsQueryable();

        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.CreatedAt <= to.Value);

        if (organizationId.HasValue)
            query = query.Where(l => l.OrganizationId == organizationId.Value);

        var messages = await query.ToListAsync(ct);

        var commandCounts = messages
            .Where(m => !string.IsNullOrEmpty(m.Command))
            .GroupBy(m => m.Command!)
            .ToDictionary(g => g.Key, g => g.Count());

        var templateCounts = messages
            .Where(m => !string.IsNullOrEmpty(m.TemplateName))
            .GroupBy(m => m.TemplateName!)
            .ToDictionary(g => g.Key, g => g.Count());

        return new WhatsAppMessageStats(
            TotalMessages: messages.Count,
            InboundMessages: messages.Count(m => m.Direction == WhatsAppMessageDirection.Inbound),
            OutboundMessages: messages.Count(m => m.Direction == WhatsAppMessageDirection.Outbound),
            FailedMessages: messages.Count(m => m.Status == WhatsAppMessageStatus.Failed),
            UniqueUsers: messages.Where(m => m.UserId.HasValue).Select(m => m.UserId!.Value).Distinct().Count(),
            CommandCounts: commandCounts,
            TemplateCounts: templateCounts
        );
    }

    /// <summary>
    /// Sanitize content to remove PII before logging.
    /// Masks emails, phone numbers, PANs, Aadhaar, bank accounts, and credit cards.
    /// </summary>
    private static string? SanitizeContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Truncate long content first
        if (content.Length > 500)
            content = content[..500] + "...";

        // Mask email addresses (user@domain.com -> u***@d***.com)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            match =>
            {
                var parts = match.Value.Split('@');
                if (parts.Length != 2) return "[EMAIL]";
                var localPart = parts[0].Length > 2 ? parts[0][0] + "***" : "***";
                var domainParts = parts[1].Split('.');
                var domain = domainParts[0].Length > 2 ? domainParts[0][0] + "***" : "***";
                return $"{localPart}@{domain}.{domainParts[^1]}";
            });

        // Mask Indian phone numbers (10 digits, may have +91 prefix)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"(?:\+91[\s-]?)?[6-9]\d{9}",
            match =>
            {
                var digits = new string(match.Value.Where(char.IsDigit).ToArray());
                if (digits.Length >= 10)
                    return $"+91****{digits[^4..]}";
                return "[PHONE]";
            });

        // Mask PAN numbers (ABCDE1234F)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"[A-Z]{5}[0-9]{4}[A-Z]",
            match => $"{match.Value[..2]}***{match.Value[^2..]}");

        // Mask Aadhaar numbers (12 digits, often formatted as XXXX XXXX XXXX)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\d{4}[\s-]?\d{4}[\s-]?\d{4}",
            "XXXX XXXX ****");

        // Mask bank account numbers (9-18 digits)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\b\d{9,18}\b",
            match =>
            {
                var len = match.Value.Length;
                return len > 4 ? new string('*', len - 4) + match.Value[^4..] : "[ACCOUNT]";
            });

        // Mask IFSC codes
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"[A-Z]{4}0[A-Z0-9]{6}",
            match => $"{match.Value[..4]}0******");

        // Mask credit card numbers (13-19 digits, may have spaces/dashes)
        content = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"\b(?:\d[\s-]?){13,19}\b",
            match =>
            {
                var digits = new string(match.Value.Where(char.IsDigit).ToArray());
                return digits.Length >= 4 ? $"************{digits[^4..]}" : "[CARD]";
            });

        return content;
    }
}
