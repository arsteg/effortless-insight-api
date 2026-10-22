using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// One table in a reports export. Shared by the CSV, Excel and PDF exports.
/// </summary>
public record ReportsExportSection(string Title, string[] Headers, List<object[]> Rows);

public static class ReportsExportSections
{
    public static List<ReportsExportSection> Build(ReportsResponse report)
    {
        var o = report.Overview;

        return
        [
            new("Overview", ["Metric", "Value"],
            [
                ["Total Notices", o.TotalNotices],
                ["Resolved Notices", o.ResolvedNotices],
                ["Pending Notices", o.PendingNotices],
                ["Total Demand", o.TotalDemandAmount],
                ["Resolved Amount", o.ResolvedAmount],
                ["Pending Amount", o.PendingAmount],
                ["Avg Resolution Days", o.AvgResolutionDays],
                ["Compliance Score (%)", o.ComplianceScore]
            ]),
            new("Notices by Status", ["Status", "Count"],
                report.NoticesByStatus.Select(kv => new object[] { kv.Key, kv.Value }).ToList()),
            new("Notices by Priority", ["Priority", "Count"],
                report.NoticesByPriority.Select(kv => new object[] { kv.Key, kv.Value }).ToList()),
            new("Monthly Trend", ["Month", "Notices Received", "Notices Resolved", "Total Demand", "Compliance Score (%)"],
                report.MonthlyTrend.Select(m => new object[]
                    { $"{m.Year}-{int.Parse(m.Month):D2}", m.NoticesReceived, m.NoticesResolved, m.TotalDemand, m.ComplianceScore }).ToList()),
            new("Notice Types", ["Type", "Count", "Percentage", "Total Demand"],
                report.NoticesByType.Select(t => new object[] { t.Type, t.Count, t.Percentage, t.TotalDemand }).ToList()),
            new("GSTIN Summary", ["GSTIN", "State", "Trade Name", "Active Notices", "Resolved Notices", "Pending Amount", "Resolved Amount"],
                report.GstinSummary.Select(g => new object[]
                    { g.Gstin, g.StateName, g.TradeName ?? "", g.ActiveNotices, g.ResolvedNotices, g.PendingAmount, g.ResolvedAmount }).ToList())
        ];
    }
}

/// <summary>
/// Generates PDF exports for the reports page. Separated from controller to avoid namespace conflicts.
/// </summary>
public class ReportsPdfExportGenerator
{
    public byte[] Generate(ReportsResponse report, List<ReportsExportSection> sections)
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
                    column.Item().AlignCenter().Text("Reports")
                        .FontSize(18).Bold();
                    column.Item().AlignCenter().Text($"Period: {report.PeriodStart:yyyy-MM-dd} to {report.PeriodEnd:yyyy-MM-dd}")
                        .FontSize(10);
                    column.Item().AlignCenter().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                        .FontSize(10);
                    column.Item().PaddingBottom(10);
                });

                page.Content().Column(column =>
                {
                    foreach (var section in sections)
                    {
                        column.Item().PaddingTop(10).PaddingBottom(4).Text(section.Title).FontSize(12).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                foreach (var _ in section.Headers)
                                    columns.RelativeColumn();
                            });

                            foreach (var header in section.Headers)
                            {
                                table.Cell().Background(Colors.Grey.Lighten2)
                                    .Padding(3).Text(header).FontSize(8).Bold();
                            }

                            foreach (var row in section.Rows)
                            {
                                foreach (var value in row)
                                {
                                    var text = value is decimal d ? $"{d:N0}" : value?.ToString() ?? "";
                                    table.Cell().Border(0.5f).Padding(2).Text(text).FontSize(7);
                                }
                            }
                        });
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
