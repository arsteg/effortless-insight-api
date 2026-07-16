using System.Reflection;
using System.Text.Json;
using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Features.Workflows.Services;

/// <summary>
/// Service for evaluating workflow conditions against notice data.
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// Evaluates a workflow condition against a notice.
    /// </summary>
    bool Evaluate(WorkflowCondition condition, Notice notice);

    /// <summary>
    /// Evaluates multiple conditions with AND logic.
    /// </summary>
    bool EvaluateAll(IEnumerable<WorkflowCondition> conditions, Notice notice);

    /// <summary>
    /// Evaluates multiple conditions with OR logic.
    /// </summary>
    bool EvaluateAny(IEnumerable<WorkflowCondition> conditions, Notice notice);
}

/// <summary>
/// Implementation of condition evaluator for workflow routing.
/// </summary>
public class ConditionEvaluator : IConditionEvaluator
{
    private readonly ILogger<ConditionEvaluator> _logger;

    public ConditionEvaluator(ILogger<ConditionEvaluator> logger)
    {
        _logger = logger;
    }

    public bool Evaluate(WorkflowCondition condition, Notice notice)
    {
        try
        {
            var fieldValue = GetFieldValue(condition.Field, notice);
            return EvaluateOperator(condition.Operator, fieldValue, condition.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate condition for field {Field}", condition.Field);
            return false;
        }
    }

    public bool EvaluateAll(IEnumerable<WorkflowCondition> conditions, Notice notice)
    {
        return conditions.All(c => Evaluate(c, notice));
    }

    public bool EvaluateAny(IEnumerable<WorkflowCondition> conditions, Notice notice)
    {
        return conditions.Any(c => Evaluate(c, notice));
    }

    /// <summary>
    /// Gets the value of a field from the notice using reflection.
    /// Supports nested properties like "metadata.customField".
    /// </summary>
    private object? GetFieldValue(string field, Notice notice)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        // Handle special computed fields
        var lowerField = field.ToLowerInvariant();
        switch (lowerField)
        {
            case "totalamount":
                return (notice.TaxAmount ?? 0) + (notice.PenaltyAmount ?? 0) + (notice.InterestAmount ?? 0);
            case "daysuntildeadline":
                if (notice.ResponseDeadline.HasValue)
                    return (DateTime.SpecifyKind(notice.ResponseDeadline.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) - DateTime.UtcNow.Date).Days;
                return null;
            case "isoverdue":
                return notice.ResponseDeadline.HasValue &&
                       notice.ResponseDeadline.Value < DateOnly.FromDateTime(DateTime.Today);
            case "hasassignee":
                return notice.AssignedToId.HasValue;
        }

        // Handle metadata fields (e.g., "metadata.customField")
        if (lowerField.StartsWith("metadata.") && notice.Metadata != null)
        {
            var metadataKey = field.Substring("metadata.".Length);
            if (notice.Metadata.TryGetValue(metadataKey, out var metadataValue))
                return metadataValue;
            return null;
        }

        // Standard property lookup using reflection
        var property = typeof(Notice).GetProperty(field,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property == null)
        {
            _logger.LogWarning("Property {Field} not found on Notice entity", field);
            return null;
        }

        return property.GetValue(notice);
    }

    /// <summary>
    /// Evaluates an operator against field and condition values.
    /// </summary>
    private bool EvaluateOperator(string op, object? fieldValue, object? conditionValue)
    {
        var operatorLower = op.ToLowerInvariant();

        // Null handling
        if (fieldValue == null)
        {
            return operatorLower switch
            {
                "eq" or "==" or "equals" => conditionValue == null,
                "neq" or "!=" or "notequals" => conditionValue != null,
                "isnull" or "null" => true,
                "isnotnull" or "notnull" => false,
                _ => false
            };
        }

        // Null checks
        if (operatorLower is "isnull" or "null")
            return fieldValue == null;
        if (operatorLower is "isnotnull" or "notnull")
            return fieldValue != null;

        // Type-specific comparisons
        return operatorLower switch
        {
            "eq" or "==" or "equals" => AreEqual(fieldValue, conditionValue),
            "neq" or "!=" or "notequals" => !AreEqual(fieldValue, conditionValue),
            "gt" or ">" => Compare(fieldValue, conditionValue) > 0,
            "gte" or ">=" => Compare(fieldValue, conditionValue) >= 0,
            "lt" or "<" => Compare(fieldValue, conditionValue) < 0,
            "lte" or "<=" => Compare(fieldValue, conditionValue) <= 0,
            "in" => IsIn(fieldValue, conditionValue),
            "notin" => !IsIn(fieldValue, conditionValue),
            "contains" => Contains(fieldValue, conditionValue),
            "notcontains" => !Contains(fieldValue, conditionValue),
            "startswith" => StartsWith(fieldValue, conditionValue),
            "endswith" => EndsWith(fieldValue, conditionValue),
            "matches" or "regex" => MatchesRegex(fieldValue, conditionValue),
            _ => false
        };
    }

    private bool AreEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // Handle JsonElement from deserialized JSON
        if (b is JsonElement jsonElement)
            b = ConvertJsonElement(jsonElement);

        // Convert to comparable types
        var (normalizedA, normalizedB) = NormalizeTypes(a, b);
        return normalizedA?.Equals(normalizedB) ?? false;
    }

    private int Compare(object? a, object? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;

        // Handle JsonElement
        if (b is JsonElement jsonElement)
            b = ConvertJsonElement(jsonElement);

        var (normalizedA, normalizedB) = NormalizeTypes(a, b);

        if (normalizedA is IComparable comparableA && normalizedB != null)
            return comparableA.CompareTo(normalizedB);

        return 0;
    }

    private bool IsIn(object? fieldValue, object? conditionValue)
    {
        if (fieldValue == null || conditionValue == null)
            return false;

        IEnumerable<object?> collection;

        if (conditionValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            collection = jsonElement.EnumerateArray().Select(e => ConvertJsonElement(e));
        }
        else if (conditionValue is IEnumerable<object> enumerable)
        {
            collection = enumerable;
        }
        else if (conditionValue is string str)
        {
            // Support comma-separated values
            collection = str.Split(',').Select(s => (object?)s.Trim());
        }
        else
        {
            return false;
        }

        var fieldStr = fieldValue.ToString()?.ToLowerInvariant();
        return collection.Any(item => item?.ToString()?.ToLowerInvariant() == fieldStr);
    }

    private bool Contains(object? fieldValue, object? conditionValue)
    {
        if (fieldValue == null || conditionValue == null)
            return false;

        var fieldStr = fieldValue.ToString();
        var conditionStr = conditionValue.ToString();

        return fieldStr?.Contains(conditionStr ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private bool StartsWith(object? fieldValue, object? conditionValue)
    {
        if (fieldValue == null || conditionValue == null)
            return false;

        var fieldStr = fieldValue.ToString();
        var conditionStr = conditionValue.ToString();

        return fieldStr?.StartsWith(conditionStr ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private bool EndsWith(object? fieldValue, object? conditionValue)
    {
        if (fieldValue == null || conditionValue == null)
            return false;

        var fieldStr = fieldValue.ToString();
        var conditionStr = conditionValue.ToString();

        return fieldStr?.EndsWith(conditionStr ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private bool MatchesRegex(object? fieldValue, object? conditionValue)
    {
        if (fieldValue == null || conditionValue == null)
            return false;

        try
        {
            var fieldStr = fieldValue.ToString() ?? "";
            var pattern = conditionValue.ToString() ?? "";
            return System.Text.RegularExpressions.Regex.IsMatch(fieldStr, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt32(out var intVal) => intVal,
            JsonValueKind.Number when element.TryGetInt64(out var longVal) => longVal,
            JsonValueKind.Number when element.TryGetDecimal(out var decVal) => decVal,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private (object?, object?) NormalizeTypes(object a, object b)
    {
        // Handle decimals
        if (a is decimal || b is decimal)
        {
            return (Convert.ToDecimal(a), Convert.ToDecimal(b));
        }

        // Handle doubles/floats
        if (a is double or float || b is double or float)
        {
            return (Convert.ToDouble(a), Convert.ToDouble(b));
        }

        // Handle longs
        if (a is long || b is long)
        {
            if (long.TryParse(a.ToString(), out var aLong) && long.TryParse(b.ToString(), out var bLong))
                return (aLong, bLong);
        }

        // Handle integers
        if (a is int || b is int)
        {
            if (int.TryParse(a.ToString(), out var aInt) && int.TryParse(b.ToString(), out var bInt))
                return (aInt, bInt);
        }

        // Handle DateOnly
        if (a is DateOnly aDate)
        {
            if (b is DateOnly bDate)
                return (aDate, bDate);
            if (DateOnly.TryParse(b.ToString(), out var parsedDate))
                return (aDate, parsedDate);
        }

        // Handle DateTime
        if (a is DateTime aDateTime)
        {
            if (b is DateTime bDateTime)
                return (aDateTime, bDateTime);
            if (DateTime.TryParse(b.ToString(), out var parsedDateTime))
                return (aDateTime, parsedDateTime);
        }

        // Default to string comparison (case-insensitive)
        return (a.ToString()?.ToLowerInvariant(), b.ToString()?.ToLowerInvariant());
    }
}

/// <summary>
/// Operator constants for workflow conditions.
/// </summary>
public static class ConditionOperators
{
    public const string Equal = "eq";
    public const string NotEqual = "neq";
    public const string GreaterThan = "gt";
    public const string GreaterThanOrEqual = "gte";
    public const string LessThan = "lt";
    public const string LessThanOrEqual = "lte";
    public const string In = "in";
    public const string NotIn = "notin";
    public const string Contains = "contains";
    public const string NotContains = "notcontains";
    public const string StartsWith = "startswith";
    public const string EndsWith = "endswith";
    public const string IsNull = "isnull";
    public const string IsNotNull = "isnotnull";
    public const string Matches = "matches";

    public static readonly string[] All =
    [
        Equal, NotEqual, GreaterThan, GreaterThanOrEqual,
        LessThan, LessThanOrEqual, In, NotIn, Contains,
        NotContains, StartsWith, EndsWith, IsNull, IsNotNull, Matches
    ];
}
