using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Service for managing WhatsApp message templates.
/// </summary>
public class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private readonly ApplicationDbContext _db;
    private readonly IMetaWhatsAppClient _client;
    private readonly ILogger<WhatsAppTemplateService> _logger;

    public WhatsAppTemplateService(
        ApplicationDbContext db,
        IMetaWhatsAppClient client,
        ILogger<WhatsAppTemplateService> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    public async Task SyncTemplatesAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting WhatsApp template sync");

            var metaTemplates = await _client.GetTemplatesAsync(ct);

            foreach (var metaTemplate in metaTemplates)
            {
                if (metaTemplate.Name == null || metaTemplate.Id == null)
                    continue;

                var existing = await _db.WhatsAppTemplates
                    .FirstOrDefaultAsync(t =>
                        t.TemplateId == metaTemplate.Id ||
                        (t.Name == metaTemplate.Name && t.Language == (metaTemplate.Language ?? "en")),
                        ct);

                if (existing != null)
                {
                    // Update existing
                    existing.TemplateId = metaTemplate.Id;
                    existing.Status = metaTemplate.Status ?? "UNKNOWN";
                    existing.Category = metaTemplate.Category ?? "UTILITY";
                    existing.SyncedAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;

                    // Update components
                    UpdateTemplateComponents(existing, metaTemplate);
                }
                else
                {
                    // Create new
                    var template = new WhatsAppTemplate
                    {
                        TemplateId = metaTemplate.Id,
                        Name = metaTemplate.Name,
                        Category = metaTemplate.Category ?? "UTILITY",
                        Language = metaTemplate.Language ?? "en",
                        Status = metaTemplate.Status ?? "UNKNOWN",
                        IsActive = metaTemplate.Status == "APPROVED",
                        SyncedAt = DateTime.UtcNow
                    };

                    UpdateTemplateComponents(template, metaTemplate);
                    _db.WhatsAppTemplates.Add(template);
                }
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Synced {Count} WhatsApp templates", metaTemplates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync WhatsApp templates");
            throw;
        }
    }

    public async Task<List<WhatsAppTemplate>> GetActiveTemplatesAsync(CancellationToken ct = default)
    {
        return await _db.WhatsAppTemplates
            .Where(t => t.IsActive && t.Status == WhatsAppTemplateStatus.Approved)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    public async Task<WhatsAppTemplate?> GetTemplateByNameAsync(
        string name,
        string language = "en",
        CancellationToken ct = default)
    {
        return await _db.WhatsAppTemplates
            .FirstOrDefaultAsync(t =>
                t.Name == name &&
                t.Language == language &&
                t.IsActive,
                ct);
    }

    public async Task IncrementUsageAsync(string templateName, CancellationToken ct = default)
    {
        var template = await _db.WhatsAppTemplates
            .FirstOrDefaultAsync(t => t.Name == templateName, ct);

        if (template != null)
        {
            template.UsageCount++;
            template.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> IsTemplateAvailableAsync(
        string name,
        string language = "en",
        CancellationToken ct = default)
    {
        return await _db.WhatsAppTemplates
            .AnyAsync(t =>
                t.Name == name &&
                t.Language == language &&
                t.IsActive &&
                t.Status == WhatsAppTemplateStatus.Approved,
                ct);
    }

    private static void UpdateTemplateComponents(
        WhatsAppTemplate template,
        DTOs.WhatsAppTemplateInfo metaTemplate)
    {
        var components = metaTemplate.Components ?? [];

        foreach (var component in components)
        {
            switch (component.Type?.ToUpper())
            {
                case "HEADER":
                    template.HeaderFormat = component.Format;
                    template.HeaderText = component.Text;
                    break;

                case "BODY":
                    template.BodyText = component.Text ?? "";
                    // Extract variables
                    template.Variables = ExtractVariables(component.Text);
                    break;

                case "FOOTER":
                    template.FooterText = component.Text;
                    break;

                case "BUTTONS":
                    template.Buttons = component.Buttons?
                        .Select(b => new WhatsAppTemplateButton
                        {
                            Type = b.Type ?? "QUICK_REPLY",
                            Text = b.Text ?? "",
                            Url = b.Url,
                            PhoneNumber = b.PhoneNumber
                        })
                        .ToList() ?? [];
                    break;
            }
        }
    }

    private static List<string> ExtractVariables(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var variables = new List<string>();
        var i = 1;

        while (text.Contains($"{{{{{i}}}}}"))
        {
            variables.Add($"var{i}");
            i++;
        }

        return variables;
    }
}
