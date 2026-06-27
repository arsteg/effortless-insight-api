using System.Text.RegularExpressions;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Serilog enricher that masks PII (Personally Identifiable Information) in log messages.
/// Implements DPDP Act compliance requirements for logging.
/// </summary>
public partial class PiiMaskingEnricher : ILogEventEnricher
{
    // Regex patterns for PII detection
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b[6-9]\d{9}\b", RegexOptions.Compiled)]
    private static partial Regex IndianMobileRegex();

    [GeneratedRegex(@"\b\d{2}[A-Z]{5}\d{4}[A-Z]{1}[A-Z\d]{1}[Z]{1}[A-Z\d]{1}\b", RegexOptions.Compiled)]
    private static partial Regex GstinRegex();

    [GeneratedRegex(@"\b[A-Z]{5}\d{4}[A-Z]{1}\b", RegexOptions.Compiled)]
    private static partial Regex PanRegex();

    [GeneratedRegex(@"\b\d{12}\b", RegexOptions.Compiled)]
    private static partial Regex AadhaarRegex();

    [GeneratedRegex(@"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();

    // Properties that commonly contain PII
    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "email",
        "emailaddress",
        "mail",
        "mobile",
        "mobileNumber",
        "phone",
        "phoneNumber",
        "gstin",
        "pan",
        "panNumber",
        "aadhaar",
        "aadhar",
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "authorization",
        "creditcard",
        "cardnumber",
        "cvv",
        "ssn",
        "name",
        "firstname",
        "lastname",
        "fullname",
        "address",
        "ipaddress",
        "ip",
        "useragent"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Mask sensitive properties
        var propertiesToUpdate = new List<(string Key, LogEventPropertyValue Value)>();

        foreach (var property in logEvent.Properties)
        {
            if (IsSensitiveProperty(property.Key))
            {
                var maskedValue = MaskPropertyValue(property.Value);
                propertiesToUpdate.Add((property.Key, maskedValue));
            }
            else if (property.Value is ScalarValue scalarValue && scalarValue.Value is string stringValue)
            {
                // Check if string content contains PII patterns
                var maskedString = MaskPiiInString(stringValue);
                if (maskedString != stringValue)
                {
                    propertiesToUpdate.Add((property.Key, new ScalarValue(maskedString)));
                }
            }
        }

        foreach (var (key, value) in propertiesToUpdate)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(key, value));
        }

        // Also mask the message template rendered text
        // Note: We can't modify the message template itself, but we can add a masked version
        var renderedMessage = logEvent.RenderMessage();
        var maskedMessage = MaskPiiInString(renderedMessage);
        if (maskedMessage != renderedMessage)
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("MaskedMessage", maskedMessage));
        }
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        return SensitivePropertyNames.Contains(propertyName) ||
               propertyName.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("key", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("credential", StringComparison.OrdinalIgnoreCase);
    }

    private static LogEventPropertyValue MaskPropertyValue(LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue scalar when scalar.Value is string str => new ScalarValue(MaskString(str)),
            ScalarValue scalar => new ScalarValue(MaskGeneric(scalar.Value?.ToString())),
            SequenceValue sequence => new SequenceValue(sequence.Elements.Select(MaskPropertyValue)),
            StructureValue structure => new StructureValue(
                structure.Properties.Select(p => new LogEventProperty(p.Name, MaskPropertyValue(p.Value)))),
            DictionaryValue dict => new DictionaryValue(
                dict.Elements.Select(kvp => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    kvp.Key, MaskPropertyValue(kvp.Value)))),
            _ => new ScalarValue("***MASKED***")
        };
    }

    private static string MaskString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "***";

        if (value.Length <= 4)
            return "***";

        // Show first 2 and last 2 characters
        return $"{value[..2]}***{value[^2..]}";
    }

    private static string MaskGeneric(string? value)
    {
        return string.IsNullOrEmpty(value) ? "***" : "***MASKED***";
    }

    private static string MaskPiiInString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = input;

        // Mask email addresses
        result = EmailRegex().Replace(result, match =>
        {
            var email = match.Value;
            var atIndex = email.IndexOf('@');
            if (atIndex > 2)
            {
                return $"{email[..2]}***@{email[(atIndex + 1)..]}";
            }
            return "***@***.***";
        });

        // Mask Indian mobile numbers
        result = IndianMobileRegex().Replace(result, match =>
        {
            var number = match.Value;
            return $"{number[..2]}****{number[^2..]}";
        });

        // Mask GSTIN (show first 2 and last 2 characters)
        result = GstinRegex().Replace(result, match =>
        {
            var gstin = match.Value;
            return $"{gstin[..2]}***{gstin[^2..]}";
        });

        // Mask PAN (show first 2 and last character)
        result = PanRegex().Replace(result, match =>
        {
            var pan = match.Value;
            return $"{pan[..2]}*****{pan[^1..]}";
        });

        // Mask Aadhaar (show last 4 digits only)
        result = AadhaarRegex().Replace(result, match =>
        {
            var aadhaar = match.Value;
            return $"****-****-{aadhaar[^4..]}";
        });

        // Mask credit card numbers
        result = CreditCardRegex().Replace(result, match =>
        {
            return "****-****-****-****";
        });

        return result;
    }
}

/// <summary>
/// Extension methods for configuring PII masking in Serilog.
/// </summary>
public static class PiiMaskingExtensions
{
    /// <summary>
    /// Adds PII masking enricher to Serilog configuration.
    /// </summary>
    public static LoggerConfiguration WithPiiMasking(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        if (enrichmentConfiguration == null)
            throw new ArgumentNullException(nameof(enrichmentConfiguration));

        return enrichmentConfiguration.With<PiiMaskingEnricher>();
    }
}
