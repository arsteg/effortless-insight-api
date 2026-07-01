using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for managing WhatsApp message templates.
/// </summary>
public interface IWhatsAppTemplateService
{
    /// <summary>
    /// Sync templates from Meta API.
    /// </summary>
    Task SyncTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all active templates.
    /// </summary>
    Task<List<WhatsAppTemplate>> GetActiveTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get template by name.
    /// </summary>
    Task<WhatsAppTemplate?> GetTemplateByNameAsync(string name, string language = "en", CancellationToken ct = default);

    /// <summary>
    /// Increment usage count for a template.
    /// </summary>
    Task IncrementUsageAsync(string templateName, CancellationToken ct = default);

    /// <summary>
    /// Check if a template is approved and active.
    /// </summary>
    Task<bool> IsTemplateAvailableAsync(string name, string language = "en", CancellationToken ct = default);
}
