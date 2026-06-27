using EffortlessInsight.Api.Data;
using Microsoft.EntityFrameworkCore;
using Scriban;
using Scriban.Runtime;

namespace EffortlessInsight.Api.Services.Notifications;

/// <summary>
/// Interface for template rendering using Scriban templating engine.
/// Supports variables, conditionals, loops, and filters.
/// </summary>
public interface ITemplateRenderService
{
    /// <summary>
    /// Render a template string with the provided model data.
    /// Supports Scriban syntax:
    /// - Variables: {{ user.name }}, {{ notice.number }}
    /// - Conditionals: {% if condition %} ... {% endif %}
    /// - Loops: {% for item in collection %} ... {% endfor %}
    /// - Filters: {{ date | date.to_string '%Y-%m-%d' }}
    /// </summary>
    /// <param name="templateContent">The template string to render</param>
    /// <param name="model">The data model object</param>
    /// <returns>The rendered template string</returns>
    Task<string> RenderAsync(string templateContent, object model);

    /// <summary>
    /// Render a template by its database ID with the provided model data.
    /// </summary>
    /// <param name="templateId">The ID of the template in the database</param>
    /// <param name="model">The data model object</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The rendered template string</returns>
    Task<string> RenderTemplateByIdAsync(Guid templateId, object model, CancellationToken ct);

    /// <summary>
    /// Render a template by type, channel, and language with the provided model data.
    /// Falls back to English if the specified language template is not found.
    /// </summary>
    /// <param name="type">Notification type (e.g., "deadline_1_day")</param>
    /// <param name="channel">Notification channel (e.g., "email", "sms")</param>
    /// <param name="model">The data model object</param>
    /// <param name="language">Language code (defaults to "en")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tuple of (subject, body) - subject may be null for non-email channels</returns>
    Task<(string? Subject, string Body)> RenderByTypeAsync(
        string type,
        string channel,
        object model,
        string language = "en",
        CancellationToken ct = default);
}

/// <summary>
/// Scriban-based template rendering service for notifications.
/// Provides powerful templating capabilities including variables, conditionals, loops, and filters.
/// </summary>
public class TemplateRenderService : ITemplateRenderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<TemplateRenderService> _logger;

    // Cache compiled templates for performance
    private static readonly Dictionary<string, Template> _templateCache = new();
    private static readonly object _cacheLock = new();

    public TemplateRenderService(
        ApplicationDbContext dbContext,
        ILogger<TemplateRenderService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> RenderAsync(string templateContent, object model)
    {
        if (string.IsNullOrWhiteSpace(templateContent))
        {
            return string.Empty;
        }

        try
        {
            var template = GetOrCompileTemplate(templateContent);
            var scriptObject = BuildScriptObject(model);
            var context = new TemplateContext { MemberRenamer = member => member.Name };
            context.PushGlobal(scriptObject);

            var result = await template.RenderAsync(context);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render template");
            // Return the original template content with simple variable replacement as fallback
            return FallbackRender(templateContent, model);
        }
    }

    /// <inheritdoc />
    public async Task<string> RenderTemplateByIdAsync(Guid templateId, object model, CancellationToken ct)
    {
        var template = await _dbContext.NotificationTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, ct);

        if (template == null)
        {
            throw new KeyNotFoundException($"Template with ID {templateId} not found or inactive");
        }

        return await RenderAsync(template.Body, model);
    }

    /// <inheritdoc />
    public async Task<(string? Subject, string Body)> RenderByTypeAsync(
        string type,
        string channel,
        object model,
        string language = "en",
        CancellationToken ct = default)
    {
        // Try to find template for specified language
        var template = await _dbContext.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.Type == type && t.Channel == channel && t.Language == language && t.IsActive)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(ct);

        // Fallback to English if language-specific template not found
        if (template == null && language != "en")
        {
            template = await _dbContext.NotificationTemplates
                .AsNoTracking()
                .Where(t => t.Type == type && t.Channel == channel && t.Language == "en" && t.IsActive)
                .OrderByDescending(t => t.Version)
                .FirstOrDefaultAsync(ct);
        }

        if (template == null)
        {
            throw new KeyNotFoundException($"Template not found for type={type}, channel={channel}, language={language}");
        }

        var renderedBody = await RenderAsync(template.Body, model);
        var renderedSubject = !string.IsNullOrEmpty(template.Subject)
            ? await RenderAsync(template.Subject, model)
            : null;

        return (renderedSubject, renderedBody);
    }

    /// <summary>
    /// Get a compiled template from cache or compile and cache it.
    /// </summary>
    private static Template GetOrCompileTemplate(string templateContent)
    {
        var cacheKey = templateContent.GetHashCode().ToString();

        lock (_cacheLock)
        {
            if (_templateCache.TryGetValue(cacheKey, out var cachedTemplate))
            {
                return cachedTemplate;
            }

            var template = Template.Parse(templateContent);

            if (template.HasErrors)
            {
                throw new InvalidOperationException(
                    $"Template parsing errors: {string.Join(", ", template.Messages)}");
            }

            // Limit cache size to prevent memory issues
            if (_templateCache.Count > 1000)
            {
                _templateCache.Clear();
            }

            _templateCache[cacheKey] = template;
            return template;
        }
    }

    /// <summary>
    /// Build a Scriban ScriptObject from the model, adding custom functions and utilities.
    /// </summary>
    private static ScriptObject BuildScriptObject(object model)
    {
        var scriptObject = new ScriptObject();

        // Import the model object
        if (model is IDictionary<string, object> dict)
        {
            // Handle dictionary models
            foreach (var kvp in dict)
            {
                scriptObject[ConvertToSnakeCase(kvp.Key)] = kvp.Value;
                // Also add camelCase version for compatibility
                scriptObject[kvp.Key] = kvp.Value;
            }
        }
        else
        {
            // Handle object models by reflecting properties
            scriptObject.Import(model, renamer: member => ConvertToSnakeCase(member.Name));
            // Also import with original names
            scriptObject.Import(model);
        }

        // Add built-in Scriban functions
        scriptObject.Import(typeof(Scriban.Functions.BuiltinFunctions));

        // Add custom helper functions
        AddCustomFunctions(scriptObject);

        return scriptObject;
    }

    /// <summary>
    /// Add custom helper functions to the script context.
    /// </summary>
    private static void AddCustomFunctions(ScriptObject scriptObject)
    {
        // Format currency (Indian Rupees)
        scriptObject.Import("format_currency", new Func<decimal?, string>(amount =>
        {
            if (amount == null) return "-";
            return amount.Value.ToString("N2", new System.Globalization.CultureInfo("en-IN"));
        }));

        // Format currency with symbol
        scriptObject.Import("format_inr", new Func<decimal?, string>(amount =>
        {
            if (amount == null) return "-";
            return "\u20B9" + amount.Value.ToString("N2", new System.Globalization.CultureInfo("en-IN"));
        }));

        // Format date in Indian format
        scriptObject.Import("format_date_in", new Func<object?, string>(date =>
        {
            if (date == null) return "-";
            if (date is DateTime dt)
                return dt.ToString("dd MMM yyyy");
            if (date is DateOnly dOnly)
                return dOnly.ToString("dd MMM yyyy");
            if (DateTime.TryParse(date.ToString(), out var parsed))
                return parsed.ToString("dd MMM yyyy");
            return date.ToString() ?? "-";
        }));

        // Calculate days remaining/overdue
        scriptObject.Import("days_until", new Func<object?, int>(date =>
        {
            if (date == null) return 0;
            var targetDate = date switch
            {
                DateTime dt => DateOnly.FromDateTime(dt),
                DateOnly dOnly => dOnly,
                _ when DateOnly.TryParse(date.ToString(), out var parsed) => parsed,
                _ => DateOnly.FromDateTime(DateTime.UtcNow)
            };
            return targetDate.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        }));

        // Pluralize helper
        scriptObject.Import("pluralize", new Func<int, string, string, string>((count, singular, plural) =>
            count == 1 ? singular : plural));

        // Truncate text
        scriptObject.Import("truncate", new Func<string?, int, string>((text, maxLength) =>
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text ?? "";
            return text[..(maxLength - 3)] + "...";
        }));

        // Safe default value
        scriptObject.Import("default_if_empty", new Func<object?, object?, object?>((value, defaultValue) =>
            value == null || (value is string s && string.IsNullOrEmpty(s)) ? defaultValue : value));

        // Priority badge color
        scriptObject.Import("priority_color", new Func<string?, string>(priority =>
            priority?.ToLower() switch
            {
                "critical" => "#EF4444",
                "high" => "#F59E0B",
                "medium" => "#3B82F6",
                "low" => "#10B981",
                _ => "#6B7280"
            }));

        // Status badge color
        scriptObject.Import("status_color", new Func<string?, string>(status =>
            status?.ToLower() switch
            {
                "responded" or "closed" => "#10B981",
                "in_progress" => "#3B82F6",
                "analyzed" => "#8B5CF6",
                "uploaded" or "processing" => "#F59E0B",
                "overdue" or "failed" => "#EF4444",
                _ => "#6B7280"
            }));

        // URL encode
        scriptObject.Import("url_encode", new Func<string?, string>(text =>
            string.IsNullOrEmpty(text) ? "" : Uri.EscapeDataString(text)));
    }

    /// <summary>
    /// Convert a string from camelCase/PascalCase to snake_case.
    /// </summary>
    private static string ConvertToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLower(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Fallback simple variable replacement when Scriban parsing fails.
    /// Handles {{ variable }} and {variable} syntax.
    /// </summary>
    private string FallbackRender(string template, object model)
    {
        var result = template;

        Dictionary<string, object?> values;
        if (model is IDictionary<string, object> dict)
        {
            values = dict.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
        }
        else
        {
            values = model.GetType()
                .GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(model));
        }

        foreach (var kvp in values)
        {
            var value = kvp.Value?.ToString() ?? "";
            // Handle {{ variable }} syntax
            result = result.Replace("{{" + kvp.Key + "}}", value);
            result = result.Replace("{{ " + kvp.Key + " }}", value);
            // Handle {variable} syntax
            result = result.Replace("{" + kvp.Key + "}", value);
            // Handle snake_case versions
            var snakeKey = ConvertToSnakeCase(kvp.Key);
            result = result.Replace("{{" + snakeKey + "}}", value);
            result = result.Replace("{{ " + snakeKey + " }}", value);
            result = result.Replace("{" + snakeKey + "}", value);
        }

        return result;
    }
}

/// <summary>
/// Extension methods for template rendering in notification contexts.
/// </summary>
public static class TemplateRenderExtensions
{
    /// <summary>
    /// Build a standard notification model from a dictionary of data.
    /// </summary>
    public static Dictionary<string, object> BuildNotificationModel(
        this Dictionary<string, object> data,
        string? userName = null,
        string? organizationName = null,
        string? actionUrl = null)
    {
        var model = new Dictionary<string, object>(data);

        if (userName != null)
            model["userName"] = userName;

        if (organizationName != null)
            model["organizationName"] = organizationName;

        if (actionUrl != null)
            model["actionUrl"] = actionUrl;

        // Add common computed values
        model["currentDate"] = DateTime.UtcNow.ToString("dd MMM yyyy");
        model["currentYear"] = DateTime.UtcNow.Year;
        model["appUrl"] = "https://app.effortlessinsight.com";

        return model;
    }
}
