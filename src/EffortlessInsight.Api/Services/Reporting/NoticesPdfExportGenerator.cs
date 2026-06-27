using EffortlessInsight.Api.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Generates PDF exports for notices. Separated from controller to avoid namespace conflicts.
/// </summary>
public class NoticesPdfExportGenerator
{
    public byte[] Generate(List<NoticeDto> notices)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, Unit.Centimetre);

                page.Header().Column(column =>
                {
                    column.Item().AlignCenter().Text("Notices Export")
                        .FontSize(18).Bold();
                    column.Item().AlignCenter().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                        .FontSize(10);
                    column.Item().PaddingBottom(10);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);  // Notice #
                        columns.RelativeColumn(2);  // Type
                        columns.RelativeColumn(2);  // Category
                        columns.RelativeColumn(2);  // GSTIN
                        columns.RelativeColumn(1);  // Status
                        columns.RelativeColumn(1);  // Priority
                        columns.RelativeColumn(1);  // Issue Date
                        columns.RelativeColumn(1);  // Deadline
                        columns.RelativeColumn(1);  // Tax
                        columns.RelativeColumn(1);  // Penalty
                    });

                    // Header row
                    var headers = new[] { "Notice #", "Type", "Category", "GSTIN", "Status", "Priority",
                        "Issue Date", "Deadline", "Tax", "Penalty" };
                    foreach (var header in headers)
                    {
                        table.Cell().Background(Colors.Grey.Lighten2)
                            .Padding(3).Text(header).FontSize(8).Bold();
                    }

                    // Data rows
                    foreach (var notice in notices)
                    {
                        table.Cell().Border(0.5f).Padding(2).Text(notice.NoticeNumber ?? "").FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text(notice.NoticeType ?? "").FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text(notice.NoticeCategory ?? "").FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text(notice.Gstin ?? "").FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text(notice.Status).FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text(notice.Priority).FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text(notice.IssueDate?.ToString("MM/dd/yy") ?? "").FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text(notice.ResponseDeadline?.ToString("MM/dd/yy") ?? "").FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text($"{notice.TaxAmount ?? 0:N0}").FontSize(7);
                        table.Cell().Border(0.5f).Padding(2).Text($"{notice.PenaltyAmount ?? 0:N0}").FontSize(7);
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
