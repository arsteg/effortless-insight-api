using System.Globalization;
using System.Reflection;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EffortlessInsight.Api.Services.Export;

/// <summary>
/// Interface for export services supporting multiple formats.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Export data to CSV format.
    /// </summary>
    Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data, CancellationToken ct = default);

    /// <summary>
    /// Export data to Excel format.
    /// </summary>
    Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string sheetName, CancellationToken ct = default);

    /// <summary>
    /// Export HTML content to PDF format.
    /// </summary>
    Task<byte[]> ExportToPdfAsync(string htmlContent, CancellationToken ct = default);

    /// <summary>
    /// Export structured data to PDF format with title and columns.
    /// </summary>
    Task<byte[]> ExportToPdfAsync<T>(IEnumerable<T> data, string title, CancellationToken ct = default);
}

/// <summary>
/// Implementation of export service supporting CSV, Excel, and PDF formats.
/// </summary>
public class ExportService : IExportService
{
    private readonly ILogger<ExportService> _logger;

    public ExportService(ILogger<ExportService> logger)
    {
        _logger = logger;

        // Set QuestPDF license (Community for free use)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var sb = new StringBuilder();
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                .ToList();

            // Write header row
            var headers = properties.Select(p => EscapeCsvField(GetDisplayName(p)));
            sb.AppendLine(string.Join(",", headers));

            // Write data rows
            foreach (var item in data)
            {
                ct.ThrowIfCancellationRequested();
                var values = properties.Select(p => EscapeCsvField(FormatValue(p.GetValue(item))));
                sb.AppendLine(string.Join(",", values));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            // Add UTF-8 BOM for Excel compatibility
            var bom = Encoding.UTF8.GetPreamble();
            var result = new byte[bom.Length + bytes.Length];
            bom.CopyTo(result, 0);
            bytes.CopyTo(result, bom.Length);

            _logger.LogInformation("Exported {Count} records to CSV", data.Count());
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export data to CSV");
            throw;
        }
    }

    /// <inheritdoc />
    public Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string sheetName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // Use a simple XML-based Excel format (SpreadsheetML)
            // This avoids external dependencies while providing Excel compatibility
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                .ToList();

            var sb = new StringBuilder();

            // XML declaration and workbook start
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
            sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
            sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
            sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");

            // Styles for headers
            sb.AppendLine("<Styles>");
            sb.AppendLine(" <Style ss:ID=\"Header\">");
            sb.AppendLine("  <Font ss:Bold=\"1\"/>");
            sb.AppendLine("  <Interior ss:Color=\"#E0E0E0\" ss:Pattern=\"Solid\"/>");
            sb.AppendLine(" </Style>");
            sb.AppendLine(" <Style ss:ID=\"Date\">");
            sb.AppendLine("  <NumberFormat ss:Format=\"yyyy-mm-dd\"/>");
            sb.AppendLine(" </Style>");
            sb.AppendLine(" <Style ss:ID=\"DateTime\">");
            sb.AppendLine("  <NumberFormat ss:Format=\"yyyy-mm-dd hh:mm:ss\"/>");
            sb.AppendLine(" </Style>");
            sb.AppendLine(" <Style ss:ID=\"Number\">");
            sb.AppendLine("  <NumberFormat ss:Format=\"#,##0.00\"/>");
            sb.AppendLine(" </Style>");
            sb.AppendLine("</Styles>");

            // Worksheet
            sb.AppendLine($"<Worksheet ss:Name=\"{EscapeXml(sheetName)}\">");
            sb.AppendLine("<Table>");

            // Column definitions for auto-sizing
            foreach (var prop in properties)
            {
                sb.AppendLine($" <Column ss:AutoFitWidth=\"1\"/>");
            }

            // Header row
            sb.AppendLine(" <Row ss:StyleID=\"Header\">");
            foreach (var prop in properties)
            {
                sb.AppendLine($"  <Cell><Data ss:Type=\"String\">{EscapeXml(GetDisplayName(prop))}</Data></Cell>");
            }
            sb.AppendLine(" </Row>");

            // Data rows
            foreach (var item in data)
            {
                ct.ThrowIfCancellationRequested();
                sb.AppendLine(" <Row>");
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(item);
                    var (type, formattedValue, styleId) = GetExcelTypeAndValue(value, prop.PropertyType);
                    var styleAttr = !string.IsNullOrEmpty(styleId) ? $" ss:StyleID=\"{styleId}\"" : "";
                    sb.AppendLine($"  <Cell{styleAttr}><Data ss:Type=\"{type}\">{formattedValue}</Data></Cell>");
                }
                sb.AppendLine(" </Row>");
            }

            sb.AppendLine("</Table>");
            sb.AppendLine("</Worksheet>");
            sb.AppendLine("</Workbook>");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());

            _logger.LogInformation("Exported {Count} records to Excel", data.Count());
            return Task.FromResult(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export data to Excel");
            throw;
        }
    }

    /// <inheritdoc />
    public Task<byte[]> ExportToPdfAsync(string htmlContent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            // For HTML content, we'll create a simple PDF with the text content
            // In a production environment, you might want to use a full HTML-to-PDF converter
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Content().Column(col =>
                    {
                        // Strip HTML tags for basic rendering
                        var plainText = StripHtmlTags(htmlContent);
                        col.Item().Text(plainText);
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            });

            var bytes = document.GeneratePdf();

            _logger.LogInformation("Exported HTML content to PDF");
            return Task.FromResult(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export HTML to PDF");
            throw;
        }
    }

    /// <inheritdoc />
    public Task<byte[]> ExportToPdfAsync<T>(IEnumerable<T> data, string title, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var dataList = data.ToList();
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && IsSimpleType(p.PropertyType))
                .ToList();

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    // Header
                    page.Header().Element(c => ComposePdfHeader(c, title));

                    // Content - table
                    page.Content().Element(c => ComposePdfTable(c, dataList, properties, ct));

                    // Footer
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                        text.Span($" | Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                    });
                });
            });

            var bytes = document.GeneratePdf();

            _logger.LogInformation("Exported {Count} records to PDF", dataList.Count);
            return Task.FromResult(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export data to PDF");
            throw;
        }
    }

    private static void ComposePdfHeader(IContainer container, string title)
    {
        container.Column(col =>
        {
            col.Item().Text(title).Bold().FontSize(16);
            col.Item().PaddingVertical(5).LineHorizontal(1);
        });
    }

    private void ComposePdfTable<T>(
        IContainer container,
        List<T> data,
        List<PropertyInfo> properties,
        CancellationToken ct)
    {
        container.Table(table =>
        {
            // Define columns
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in properties)
                {
                    columns.RelativeColumn();
                }
            });

            // Header row
            table.Header(header =>
            {
                foreach (var prop in properties)
                {
                    header.Cell().Background("#E0E0E0").Padding(5).Text(GetDisplayName(prop)).Bold();
                }
            });

            // Data rows
            var rowIndex = 0;
            foreach (var item in data)
            {
                ct.ThrowIfCancellationRequested();
                var bgColor = rowIndex % 2 == 0 ? "#FFFFFF" : "#F5F5F5";

                foreach (var prop in properties)
                {
                    var value = FormatValue(prop.GetValue(item));
                    table.Cell().Background(bgColor).Padding(5).Text(value);
                }

                rowIndex++;
            }
        });
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If the value contains comma, quote, or newline, wrap it in quotes
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            // Escape quotes by doubling them
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
            return "";

        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            decimal dec => dec.ToString("N2", CultureInfo.InvariantCulture),
            double dbl => dbl.ToString("N2", CultureInfo.InvariantCulture),
            float flt => flt.ToString("N2", CultureInfo.InvariantCulture),
            bool b => b ? "Yes" : "No",
            Guid g => g.ToString(),
            _ => value.ToString() ?? ""
        };
    }

    private static (string Type, string Value, string? StyleId) GetExcelTypeAndValue(object? value, Type propertyType)
    {
        if (value == null)
            return ("String", "", null);

        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return value switch
        {
            DateTime dt => ("DateTime", dt.ToString("s"), "DateTime"),
            DateOnly d => ("DateTime", d.ToString("yyyy-MM-dd"), "Date"),
            DateTimeOffset dto => ("DateTime", dto.DateTime.ToString("s"), "DateTime"),
            decimal dec => ("Number", dec.ToString(CultureInfo.InvariantCulture), "Number"),
            double dbl => ("Number", dbl.ToString(CultureInfo.InvariantCulture), "Number"),
            float flt => ("Number", flt.ToString(CultureInfo.InvariantCulture), "Number"),
            int i => ("Number", i.ToString(), null),
            long l => ("Number", l.ToString(), null),
            bool b => ("String", b ? "Yes" : "No", null),
            _ => ("String", EscapeXml(value.ToString() ?? ""), null)
        };
    }

    private static string GetDisplayName(PropertyInfo property)
    {
        // Convert PascalCase to Title Case with spaces
        var name = property.Name;
        var result = new StringBuilder();

        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                result.Append(' ');
            }
            result.Append(name[i]);
        }

        return result.ToString();
    }

    private static bool IsSimpleType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType.IsPrimitive
            || underlyingType == typeof(string)
            || underlyingType == typeof(decimal)
            || underlyingType == typeof(DateTime)
            || underlyingType == typeof(DateOnly)
            || underlyingType == typeof(DateTimeOffset)
            || underlyingType == typeof(TimeSpan)
            || underlyingType == typeof(Guid)
            || underlyingType.IsEnum;
    }

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        // Basic HTML tag stripping - for production use a proper HTML parser
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
        result = System.Net.WebUtility.HtmlDecode(result);
        return result.Trim();
    }
}
