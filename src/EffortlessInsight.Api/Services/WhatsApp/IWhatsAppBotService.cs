using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Main service for orchestrating WhatsApp bot interactions.
/// </summary>
public interface IWhatsAppBotService
{
    /// <summary>
    /// Process incoming WhatsApp message from webhook.
    /// </summary>
    Task ProcessIncomingMessageAsync(
        WhatsAppIncomingMessage message,
        CancellationToken ct = default);

    /// <summary>
    /// Send proactive message to user (checks 24h window).
    /// </summary>
    Task<WhatsAppSendResult> SendToUserAsync(
        Guid userId,
        string content,
        CancellationToken ct = default);

    /// <summary>
    /// Send template message to user (works outside 24h window).
    /// Parameters are stored for retry capability.
    /// </summary>
    Task<WhatsAppSendResult> SendTemplateToUserAsync(
        Guid userId,
        string templateName,
        Dictionary<string, string> variables,
        string language = "en",
        string? referenceType = null,
        Guid? referenceId = null,
        string? correlationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send interactive buttons to user.
    /// </summary>
    Task<WhatsAppSendResult> SendButtonsToUserAsync(
        Guid userId,
        string bodyText,
        List<WhatsAppButton> buttons,
        CancellationToken ct = default);
}
