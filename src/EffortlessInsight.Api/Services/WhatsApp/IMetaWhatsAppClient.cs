using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Client for Meta WhatsApp Cloud API (Graph API).
/// </summary>
public interface IMetaWhatsAppClient
{
    #region Sending Messages

    /// <summary>
    /// Send a text message.
    /// </summary>
    Task<WhatsAppSendResult> SendTextMessageAsync(
        string to,
        string text,
        bool previewUrl = false,
        CancellationToken ct = default);

    /// <summary>
    /// Send a template message.
    /// </summary>
    Task<WhatsAppSendResult> SendTemplateMessageAsync(
        string to,
        string templateName,
        string language,
        List<TemplateParameter>? bodyParameters = null,
        List<TemplateParameter>? headerParameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send an interactive message with buttons.
    /// </summary>
    Task<WhatsAppSendResult> SendInteractiveButtonsAsync(
        string to,
        string bodyText,
        List<WhatsAppButton> buttons,
        string? headerText = null,
        string? footerText = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send an interactive message with a list.
    /// </summary>
    Task<WhatsAppSendResult> SendInteractiveListAsync(
        string to,
        string bodyText,
        string buttonText,
        List<WhatsAppListSection> sections,
        string? headerText = null,
        string? footerText = null,
        CancellationToken ct = default);

    #endregion

    #region Media

    /// <summary>
    /// Upload media to WhatsApp.
    /// </summary>
    Task<string?> UploadMediaAsync(
        Stream content,
        string mimeType,
        string fileName,
        CancellationToken ct = default);

    /// <summary>
    /// Send an image message.
    /// </summary>
    Task<WhatsAppSendResult> SendImageAsync(
        string to,
        string mediaId,
        string? caption = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send a document message.
    /// </summary>
    Task<WhatsAppSendResult> SendDocumentAsync(
        string to,
        string mediaId,
        string? caption = null,
        string? fileName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get media information (URL, MIME type, size) for a media ID.
    /// </summary>
    Task<WhatsAppMediaInfo?> GetMediaInfoAsync(string mediaId, CancellationToken ct = default);

    /// <summary>
    /// Download media content from a URL.
    /// </summary>
    Task<Stream?> DownloadMediaAsync(string mediaUrl, CancellationToken ct = default);

    #endregion

    #region Templates

    /// <summary>
    /// Get all templates from Meta.
    /// </summary>
    Task<List<WhatsAppTemplateInfo>> GetTemplatesAsync(CancellationToken ct = default);

    #endregion

    #region Read Receipts

    /// <summary>
    /// Mark a message as read.
    /// </summary>
    Task MarkAsReadAsync(string messageId, CancellationToken ct = default);

    #endregion

    #region Phone Number Formatting

    /// <summary>
    /// Format phone number to E.164 format for WhatsApp.
    /// </summary>
    string FormatPhoneNumber(string phone);

    /// <summary>
    /// Mask phone number for logging (e.g., +91****3210).
    /// </summary>
    string MaskPhoneNumber(string phone);

    #endregion
}
