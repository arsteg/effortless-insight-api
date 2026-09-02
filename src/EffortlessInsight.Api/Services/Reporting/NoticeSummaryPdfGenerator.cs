using EffortlessInsight.Api.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Generates a comprehensive PDF summary for a single notice, including all details and AI analysis.
/// </summary>
public class NoticeSummaryPdfGenerator
{
    private static readonly string BrandColor = "#2563eb"; // Blue-600
    private static readonly string MutedColor = "#6b7280"; // Gray-500
    private static readonly string HeaderBgColor = "#f3f4f6"; // Gray-100
    private static readonly string BorderColor = "#e5e7eb"; // Gray-200

    public byte[] Generate(NoticeDetailDto notice)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Header().Element(c => ComposeHeader(c, notice));
                page.Content().Element(c => ComposeContent(c, notice));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private void ComposeHeader(IContainer container, NoticeDetailDto notice)
    {
        container.Column(column =>
        {
            // Brand header
            column.Item().Background(BrandColor).Padding(12).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("EFFORTLESS INSIGHTS")
                        .FontSize(16).Bold().FontColor(Colors.White);
                    c.Item().Text("Notice Summary Report")
                        .FontSize(12).FontColor(Colors.White).Light();
                });
                row.ConstantItem(150).AlignRight().Column(c =>
                {
                    c.Item().Text($"Generated: {DateTime.UtcNow:dd MMM yyyy}")
                        .FontSize(9).FontColor(Colors.White);
                    c.Item().Text($"{DateTime.UtcNow:HH:mm} UTC")
                        .FontSize(9).FontColor(Colors.White);
                });
            });

            column.Item().PaddingVertical(8);
        });
    }

    private void ComposeContent(IContainer container, NoticeDetailDto notice)
    {
        container.Column(column =>
        {
            // Notice Information Section
            column.Item().Element(c => ComposeNoticeInformation(c, notice));
            column.Item().PaddingVertical(8);

            // Financial Summary Section
            column.Item().Element(c => ComposeFinancialSummary(c, notice));
            column.Item().PaddingVertical(8);

            // AI Analysis Section
            if (notice.AiReport != null)
            {
                column.Item().Element(c => ComposeAiAnalysis(c, notice.AiReport));
                column.Item().PaddingVertical(8);

                // Action Items Section
                if (notice.AiReport.ActionItems?.Count > 0)
                {
                    column.Item().Element(c => ComposeActionItems(c, notice.AiReport.ActionItems, notice.ResponseDeadline));
                    column.Item().PaddingVertical(8);
                }

                // Required Documents Section
                if (notice.AiReport.RequiredDocuments?.Count > 0)
                {
                    column.Item().Element(c => ComposeRequiredDocuments(c, notice.AiReport.RequiredDocuments));
                    column.Item().PaddingVertical(8);
                }

                // Legal References Section
                if (notice.AiReport.LegalReferences?.Count > 0)
                {
                    column.Item().Element(c => ComposeLegalReferences(c, notice.AiReport.LegalReferences));
                }
            }
            else
            {
                // AI Analysis not available
                column.Item().Element(c => ComposeNoAiAnalysis(c, notice.ProcessingStatus));
            }
        });
    }

    private void ComposeNoticeInformation(IContainer container, NoticeDetailDto notice)
    {
        container.Border(1).BorderColor(BorderColor).Column(column =>
        {
            // Section header
            column.Item().Background(HeaderBgColor).Padding(8)
                .Text("NOTICE INFORMATION").Bold().FontSize(11);

            // Content
            column.Item().Padding(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(2);
                });

                // Row 1
                AddTableRow(table, "Notice Number", notice.NoticeNumber ?? "-");
                AddTableRow(table, "Type", notice.NoticeType ?? "-");

                // Row 2
                AddTableRow(table, "Category", notice.NoticeCategory ?? "-");
                AddTableRow(table, "GSTIN", notice.Gstin ?? "-");

                // Row 3
                AddTableRow(table, "Issuing Authority", notice.IssuingAuthority ?? "-");
                AddTableRow(table, "Issue Date", FormatDate(notice.IssueDate));

                // Row 4
                AddTableRow(table, "Response Deadline", FormatDeadline(notice.ResponseDeadline, notice.DaysRemaining));
                AddTableRow(table, "Status", FormatStatus(notice.Status));

                // Row 5
                AddTableRow(table, "Priority", notice.Priority);
                AddTableRow(table, "Assigned To", notice.AssignedToName ?? "Unassigned");

                // Row 6 - Period if available
                if (notice.PeriodFrom.HasValue || notice.PeriodTo.HasValue)
                {
                    AddTableRow(table, "Period", FormatPeriod(notice.PeriodFrom, notice.PeriodTo));
                    AddTableRow(table, "", ""); // Empty cell for alignment
                }
            });
        });
    }

    private void ComposeFinancialSummary(IContainer container, NoticeDetailDto notice)
    {
        container.Border(1).BorderColor(BorderColor).Column(column =>
        {
            // Section header
            column.Item().Background(HeaderBgColor).Padding(8)
                .Text("FINANCIAL SUMMARY").Bold().FontSize(11);

            // Content
            column.Item().Padding(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                });

                // Financial rows
                AddFinancialRow(table, "Tax Amount", notice.TaxAmount);
                AddFinancialRow(table, "Penalty", notice.PenaltyAmount);
                AddFinancialRow(table, "Interest", notice.InterestAmount);

                // Separator
                table.Cell().ColumnSpan(2).PaddingVertical(4).BorderBottom(1).BorderColor(BorderColor);

                // Total row
                var total = (notice.TaxAmount ?? 0) + (notice.PenaltyAmount ?? 0) + (notice.InterestAmount ?? 0);
                table.Cell().Padding(4).Text("TOTAL DEMAND").Bold();
                table.Cell().Padding(4).AlignRight().Text(FormatCurrency(total)).Bold().FontSize(12);
            });
        });
    }

    private void ComposeAiAnalysis(IContainer container, NoticeAiReportDto report)
    {
        container.Border(1).BorderColor(BorderColor).Column(column =>
        {
            // Section header
            column.Item().Background(HeaderBgColor).Padding(8)
                .Text("AI ANALYSIS").Bold().FontSize(11);

            // Content
            column.Item().Padding(10).Column(content =>
            {
                // Risk Assessment
                content.Item().Row(row =>
                {
                    row.RelativeItem().Text("Risk Assessment: ").SemiBold();
                    row.RelativeItem(2).Text(text =>
                    {
                        text.Span(report.RiskLevel ?? "Unknown").Bold()
                            .FontColor(GetRiskColor(report.RiskLevel));
                        if (report.ConfidenceScores?.TryGetValue("risk", out var confidence) == true)
                        {
                            text.Span($" (Confidence: {confidence}%)").FontColor(MutedColor);
                        }
                    });
                });

                content.Item().PaddingVertical(8);

                // Summary in English
                if (!string.IsNullOrEmpty(report.SummaryEn))
                {
                    content.Item().Text("Summary (English):").SemiBold();
                    content.Item().PaddingTop(4).Text(report.SummaryEn).LineHeight(1.4f);
                    content.Item().PaddingVertical(6);
                }

                // Summary in Hindi
                if (!string.IsNullOrEmpty(report.SummaryHi))
                {
                    content.Item().Text("Summary (Hindi):").SemiBold();
                    // Use a font that supports Devanagari - Nirmala UI is available on Windows
                    content.Item().PaddingTop(4).Text(report.SummaryHi).LineHeight(1.4f);
                    content.Item().PaddingVertical(6);
                }

                // Plain English Explanation
                if (!string.IsNullOrEmpty(report.PlainEnglish))
                {
                    content.Item().Text("Plain English Explanation:").SemiBold();
                    content.Item().PaddingTop(4).Text(report.PlainEnglish).LineHeight(1.4f);
                }
            });
        });
    }

    private void ComposeNoAiAnalysis(IContainer container, string processingStatus)
    {
        container.Border(1).BorderColor(BorderColor).Column(column =>
        {
            column.Item().Background(HeaderBgColor).Padding(8)
                .Text("AI ANALYSIS").Bold().FontSize(11);

            column.Item().Padding(20).AlignCenter().Column(content =>
            {
                var message = processingStatus.ToLowerInvariant() switch
                {
                    "processing" or "pending" => "AI analysis is currently being processed...",
                    "failed" => "AI analysis failed. Please retry processing.",
                    _ => "AI analysis not available for this notice."
                };
                content.Item().Text(message).FontColor(MutedColor).Italic();
            });
        });
    }

    private void ComposeActionItems(IContainer container, List<ActionItemDto> items, DateOnly? baseDeadline)
    {
        container.Border(1).BorderColor(BorderColor).Column(column =>
        {
            column.Item().Background(HeaderBgColor).Padding(8)
                .Text("RECOMMENDED ACTIONS").Bold().FontSize(11);

            column.Item().Padding(10).Column(content =>
            {
                foreach (var item in items.OrderBy(i => i.Priority))
                {
                    content.Item().PaddingVertical(3).Row(row =>
                    {
                        // Checkbox
                        row.ConstantItem(18).AlignMiddle()
                            .Border(1).BorderColor(Colors.Grey.Medium)
                            .Height(12).Width(12);

                        row.ConstantItem(8); // Spacing

                        // Priority badge
                        var priorityColor = item.Priority switch
                        {
                            1 => "#dc2626", // Red
                            2 => "#f59e0b", // Amber
                            _ => "#6b7280"  // Gray
                        };
                        row.ConstantItem(60).AlignMiddle()
                            .Background(priorityColor).Padding(2)
                            .Text($"P{item.Priority}").FontSize(8).FontColor(Colors.White).AlignCenter();

                        row.ConstantItem(8); // Spacing

                        // Action text with due date
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(text =>
                            {
                                text.Span(item.Action).SemiBold();
                                if (!string.IsNullOrEmpty(item.Description) && item.Description != item.Action)
                                {
                                    text.Span($" - {item.Description}");
                                }
                            });
                            if (item.DueInDays.HasValue && baseDeadline.HasValue)
                            {
                                var dueDate = baseDeadline.Value.AddDays(-item.DueInDays.Value);
                                c.Item().Text($"Due: {dueDate:dd MMM yyyy}")
                                    .FontSize(9).FontColor(MutedColor);
                            }
                        });
                    });
                }
            });
        });
    }

    private void ComposeRequiredDocuments(IContainer container, List<RequiredDocumentDto> documents)
    {
        container.Border(1).BorderColor(BorderColor).Column(column =>
        {
            column.Item().Background(HeaderBgColor).Padding(8)
                .Text("REQUIRED DOCUMENTS").Bold().FontSize(11);

            column.Item().Padding(10).Column(content =>
            {
                var mandatory = documents.Where(d => d.Mandatory).ToList();
                var optional = documents.Where(d => !d.Mandatory).ToList();

                if (mandatory.Count > 0)
                {
                    content.Item().Text("Mandatory:").SemiBold();
                    foreach (var doc in mandatory)
                    {
                        content.Item().PaddingLeft(10).PaddingVertical(2).Row(row =>
                        {
                            row.ConstantItem(15).Text("\u2022"); // Bullet
                            row.RelativeItem().Text(doc.Document);
                        });
                    }
                }

                if (optional.Count > 0)
                {
                    if (mandatory.Count > 0)
                        content.Item().PaddingVertical(4);

                    content.Item().Text("Optional:").SemiBold();
                    foreach (var doc in optional)
                    {
                        content.Item().PaddingLeft(10).PaddingVertical(2).Row(row =>
                        {
                            row.ConstantItem(15).Text("\u2022"); // Bullet
                            row.RelativeItem().Text(doc.Document);
                        });
                    }
                }
            });
        });
    }

    private void ComposeLegalReferences(IContainer container, List<LegalReferenceDto> references)
    {
        container.Border(1).BorderColor(BorderColor).Column(column =>
        {
            column.Item().Background(HeaderBgColor).Padding(8)
                .Text("LEGAL REFERENCES").Bold().FontSize(11);

            column.Item().Padding(10).Column(content =>
            {
                foreach (var reference in references)
                {
                    content.Item().PaddingVertical(2).Row(row =>
                    {
                        row.ConstantItem(15).Text("\u2022"); // Bullet
                        row.RelativeItem().Text(text =>
                        {
                            text.Span(reference.Section).SemiBold();
                            if (!string.IsNullOrEmpty(reference.Description))
                            {
                                text.Span($" - {reference.Description}");
                            }
                        });
                    });
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignCenter().DefaultTextStyle(x => x.FontSize(9)).Text(text =>
        {
            text.Span("Page ");
            text.CurrentPageNumber();
            text.Span(" of ");
            text.TotalPages();
            text.Span(" | Generated by Effortless Insights").FontColor(MutedColor);
        });
    }

    // Helper methods
    private static void AddTableRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(4).Text(label).FontColor(MutedColor);
        table.Cell().Padding(4).Text(value);
    }

    private static void AddFinancialRow(TableDescriptor table, string label, decimal? amount)
    {
        table.Cell().Padding(4).Text(label);
        table.Cell().Padding(4).AlignRight().Text(FormatCurrency(amount ?? 0));
    }

    private static string FormatCurrency(decimal amount)
    {
        return $"\u20b9{amount:N2}"; // ₹ symbol
    }

    private static string FormatDate(DateOnly? date)
    {
        return date?.ToString("dd MMM yyyy") ?? "-";
    }

    private static string FormatDeadline(DateOnly? deadline, int? daysRemaining)
    {
        if (!deadline.HasValue) return "-";

        var dateStr = deadline.Value.ToString("dd MMM yyyy");
        if (!daysRemaining.HasValue) return dateStr;

        var suffix = daysRemaining.Value switch
        {
            < 0 => $" ({Math.Abs(daysRemaining.Value)} days overdue)",
            0 => " (Due today)",
            _ => $" ({daysRemaining.Value} days left)"
        };

        return dateStr + suffix;
    }

    private static string FormatPeriod(DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue && !to.HasValue) return "-";
        var fromStr = from?.ToString("MMM yyyy") ?? "?";
        var toStr = to?.ToString("MMM yyyy") ?? "?";
        return $"{fromStr} to {toStr}";
    }

    private static string FormatStatus(string status)
    {
        return status.Replace("_", " ");
    }

    private static string GetRiskColor(string? riskLevel)
    {
        return riskLevel?.ToUpperInvariant() switch
        {
            "HIGH" => "#dc2626",   // Red
            "MEDIUM" => "#f59e0b", // Amber
            "LOW" => "#16a34a",    // Green
            _ => "#6b7280"         // Gray
        };
    }
}
