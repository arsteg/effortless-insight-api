using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Export;
using EffortlessInsight.Api.Services.Storage;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Implementation of report export service supporting PDF, Excel, and CSV formats.
/// </summary>
public class ReportExportService : IReportExportService
{
    private readonly IExportService _exportService;
    private readonly IFileStorageServiceExtended _storageService;
    private readonly ILogger<ReportExportService> _logger;

    public ReportExportService(
        IExportService exportService,
        IFileStorageServiceExtended storageService,
        ILogger<ReportExportService> logger)
    {
        _exportService = exportService;
        _storageService = storageService;
        _logger = logger;
    }

    public async Task<ReportExportResponse> ExportAsync(
        string reportName,
        string reportType,
        ReportResultDto results,
        string exportFormat)
    {
        var format = exportFormat.ToLowerInvariant();
        if (!ExportFormats.IsValid(format))
        {
            throw new InvalidOperationException($"INVALID_EXPORT_FORMAT: {format}");
        }

        _logger.LogInformation(
            "Exporting report '{ReportName}' ({ReportType}) to {Format} with {RowCount} rows",
            reportName, reportType, format, results.Rows.Count);

        // Convert dictionary rows to exportable records
        var exportData = results.Rows.Select(row => new ReportRowExport(row)).ToList();

        byte[] fileBytes;
        string contentType;
        string fileExtension;

        switch (format)
        {
            case ExportFormats.Csv:
                fileBytes = await ExportToCsvAsync(results);
                contentType = "text/csv";
                fileExtension = "csv";
                break;

            case ExportFormats.Excel:
                fileBytes = await ExportToExcelAsync(results, reportName);
                contentType = "application/vnd.ms-excel";
                fileExtension = "xml"; // SpreadsheetML format
                break;

            case ExportFormats.Pdf:
                fileBytes = await ExportToPdfAsync(results, reportName);
                contentType = "application/pdf";
                fileExtension = "pdf";
                break;

            default:
                throw new InvalidOperationException($"UNSUPPORTED_FORMAT: {format}");
        }

        // Generate unique filename
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var sanitizedName = SanitizeFileName(reportName);
        var fileName = $"{sanitizedName}_{timestamp}.{fileExtension}";

        // Upload to S3 reports bucket
        var downloadUrl = await _storageService.UploadReportAsync(
            fileName,
            contentType,
            fileBytes);

        var expiresAt = DateTime.UtcNow.AddMinutes(60); // 1 hour expiry

        _logger.LogInformation(
            "Successfully exported report '{ReportName}' to {Format}, size: {Size} bytes",
            reportName, format, fileBytes.Length);

        return new ReportExportResponse(
            ReportId: Guid.Empty, // Will be set by caller if needed
            FileName: fileName,
            ContentType: contentType,
            DownloadUrl: downloadUrl,
            ExpiresAt: expiresAt
        );
    }

    private Task<byte[]> ExportToCsvAsync(ReportResultDto results)
    {
        var sb = new System.Text.StringBuilder();

        // Write header row
        sb.AppendLine(string.Join(",", results.Columns.Select(EscapeCsvField)));

        // Write data rows
        foreach (var row in results.Rows)
        {
            var values = results.Columns.Select(col =>
            {
                row.TryGetValue(col, out var value);
                return EscapeCsvField(FormatValue(value));
            });
            sb.AppendLine(string.Join(",", values));
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());

        // Add UTF-8 BOM for Excel compatibility
        var bom = System.Text.Encoding.UTF8.GetPreamble();
        var result = new byte[bom.Length + bytes.Length];
        bom.CopyTo(result, 0);
        bytes.CopyTo(result, bom.Length);

        return Task.FromResult(result);
    }

    private Task<byte[]> ExportToExcelAsync(ReportResultDto results, string sheetName)
    {
        var sb = new System.Text.StringBuilder();

        // XML declaration and workbook start
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");

        // Styles
        sb.AppendLine("<Styles>");
        sb.AppendLine(" <Style ss:ID=\"Header\">");
        sb.AppendLine("  <Font ss:Bold=\"1\"/>");
        sb.AppendLine("  <Interior ss:Color=\"#E0E0E0\" ss:Pattern=\"Solid\"/>");
        sb.AppendLine(" </Style>");
        sb.AppendLine(" <Style ss:ID=\"Number\">");
        sb.AppendLine("  <NumberFormat ss:Format=\"#,##0.00\"/>");
        sb.AppendLine(" </Style>");
        sb.AppendLine("</Styles>");

        // Worksheet
        sb.AppendLine($"<Worksheet ss:Name=\"{EscapeXml(sheetName)}\">");
        sb.AppendLine("<Table>");

        // Column definitions
        foreach (var _ in results.Columns)
        {
            sb.AppendLine(" <Column ss:AutoFitWidth=\"1\"/>");
        }

        // Header row
        sb.AppendLine(" <Row ss:StyleID=\"Header\">");
        foreach (var col in results.Columns)
        {
            sb.AppendLine($"  <Cell><Data ss:Type=\"String\">{EscapeXml(FormatColumnHeader(col))}</Data></Cell>");
        }
        sb.AppendLine(" </Row>");

        // Data rows
        foreach (var row in results.Rows)
        {
            sb.AppendLine(" <Row>");
            foreach (var col in results.Columns)
            {
                row.TryGetValue(col, out var value);
                var (type, formattedValue) = GetExcelTypeAndValue(value);
                sb.AppendLine($"  <Cell><Data ss:Type=\"{type}\">{formattedValue}</Data></Cell>");
            }
            sb.AppendLine(" </Row>");
        }

        sb.AppendLine("</Table>");
        sb.AppendLine("</Worksheet>");
        sb.AppendLine("</Workbook>");

        return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private Task<byte[]> ExportToPdfAsync(ReportResultDto results, string title)
    {
        // Use QuestPDF for PDF generation
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                // Header
                page.Header().Column(col =>
                {
                    col.Item().Text(title).Bold().FontSize(16);
                    col.Item().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(10);
                    col.Item().PaddingVertical(5).LineHorizontal(1);
                });

                // Content - table
                page.Content().Table(table =>
                {
                    // Define columns
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in results.Columns)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    // Header row
                    table.Header(header =>
                    {
                        foreach (var col in results.Columns)
                        {
                            header.Cell().Background("#E0E0E0").Padding(5)
                                .Text(FormatColumnHeader(col)).Bold();
                        }
                    });

                    // Data rows
                    var rowIndex = 0;
                    foreach (var row in results.Rows)
                    {
                        var bgColor = rowIndex % 2 == 0 ? "#FFFFFF" : "#F5F5F5";

                        foreach (var col in results.Columns)
                        {
                            row.TryGetValue(col, out var value);
                            table.Cell().Background(bgColor).Padding(5).Text(FormatValue(value));
                        }

                        rowIndex++;
                    }
                });

                // Footer
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
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
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            decimal dec => dec.ToString("N2"),
            double dbl => dbl.ToString("N2"),
            float flt => flt.ToString("N2"),
            bool b => b ? "Yes" : "No",
            System.Text.Json.JsonElement je => FormatJsonElement(je),
            _ => value.ToString() ?? ""
        };
    }

    private static string FormatJsonElement(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString() ?? "",
            System.Text.Json.JsonValueKind.Number => element.GetDecimal().ToString("N2"),
            System.Text.Json.JsonValueKind.True => "Yes",
            System.Text.Json.JsonValueKind.False => "No",
            System.Text.Json.JsonValueKind.Null => "",
            _ => element.ToString()
        };
    }

    private static string FormatColumnHeader(string column)
    {
        // Convert camelCase or snake_case to Title Case
        var result = new System.Text.StringBuilder();
        var prevWasUpper = false;

        for (int i = 0; i < column.Length; i++)
        {
            var c = column[i];

            if (c == '_')
            {
                result.Append(' ');
                prevWasUpper = false;
            }
            else if (i == 0)
            {
                result.Append(char.ToUpper(c));
                prevWasUpper = char.IsUpper(c);
            }
            else if (char.IsUpper(c) && !prevWasUpper)
            {
                result.Append(' ');
                result.Append(c);
                prevWasUpper = true;
            }
            else
            {
                result.Append(c);
                prevWasUpper = char.IsUpper(c);
            }
        }

        return result.ToString();
    }

    private static (string Type, string Value) GetExcelTypeAndValue(object? value)
    {
        if (value == null)
            return ("String", "");

        return value switch
        {
            DateTime dt => ("DateTime", dt.ToString("s")),
            DateOnly d => ("DateTime", d.ToString("yyyy-MM-dd")),
            decimal dec => ("Number", dec.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            double dbl => ("Number", dbl.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            float flt => ("Number", flt.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            int i => ("Number", i.ToString()),
            long l => ("Number", l.ToString()),
            bool b => ("String", b ? "Yes" : "No"),
            System.Text.Json.JsonElement je => GetExcelTypeForJsonElement(je),
            _ => ("String", EscapeXml(value.ToString() ?? ""))
        };
    }

    private static (string Type, string Value) GetExcelTypeForJsonElement(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => ("Number", element.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            System.Text.Json.JsonValueKind.True => ("String", "Yes"),
            System.Text.Json.JsonValueKind.False => ("String", "No"),
            System.Text.Json.JsonValueKind.Null => ("String", ""),
            _ => ("String", EscapeXml(element.ToString()))
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new System.Text.StringBuilder();

        foreach (var c in name)
        {
            if (!invalid.Contains(c) && c != ' ')
            {
                sanitized.Append(c);
            }
            else if (c == ' ')
            {
                sanitized.Append('_');
            }
        }

        return sanitized.ToString();
    }
}

/// <summary>
/// Helper class for exporting report rows.
/// </summary>
internal class ReportRowExport
{
    private readonly Dictionary<string, object?> _data;

    public ReportRowExport(Dictionary<string, object?> data)
    {
        _data = data;
    }
}
