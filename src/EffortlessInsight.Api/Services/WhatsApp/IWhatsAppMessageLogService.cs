using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for logging WhatsApp messages for audit and analytics.
/// </summary>
public interface IWhatsAppMessageLogService
{
    /// <summary>
    /// Log an incoming message.
    /// </summary>
    Task<WhatsAppMessageLog> LogIncomingMessageAsync(
        string wamId,
        string phoneNumber,
        string messageType,
        string? content,
        string? command,
        Guid? userId,
        Guid? organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Log an outgoing message.
    /// </summary>
    Task<WhatsAppMessageLog> LogOutgoingMessageAsync(
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
        CancellationToken ct = default);

    /// <summary>
    /// Update message status from webhook callback.
    /// </summary>
    Task UpdateMessageStatusAsync(
        string wamId,
        string status,
        DateTime timestamp,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Get message by WamId.
    /// </summary>
    Task<WhatsAppMessageLog?> GetByWamIdAsync(string wamId, CancellationToken ct = default);

    /// <summary>
    /// Log a template message with full parameters for retry capability.
    /// </summary>
    Task<WhatsAppMessageLog> LogTemplateMessageAsync(
        string? wamId,
        string phoneNumber,
        string templateName,
        string templateLanguage,
        List<string> templateParameters,
        Guid? userId,
        Guid? organizationId,
        string status,
        string? errorCode,
        string? errorMessage,
        int? processingTimeMs,
        string? referenceType = null,
        Guid? referenceId = null,
        string? correlationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get message by ID.
    /// </summary>
    Task<WhatsAppMessageLog?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Get failed messages for retry.
    /// </summary>
    Task<List<WhatsAppMessageLog>> GetFailedMessagesForRetryAsync(int maxRetries, CancellationToken ct = default);

    /// <summary>
    /// Mark a retry attempt on a message.
    /// </summary>
    Task MarkRetryAttemptAsync(
        Guid messageId,
        bool success,
        string? newWamId,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Get message statistics for analytics.
    /// </summary>
    Task<WhatsAppMessageStats> GetStatisticsAsync(
        DateTime? from,
        DateTime? to,
        Guid? organizationId,
        CancellationToken ct = default);
}

/// <summary>
/// WhatsApp message statistics.
/// </summary>
public record WhatsAppMessageStats(
    int TotalMessages,
    int InboundMessages,
    int OutboundMessages,
    int FailedMessages,
    int UniqueUsers,
    Dictionary<string, int> CommandCounts,
    Dictionary<string, int> TemplateCounts
);
