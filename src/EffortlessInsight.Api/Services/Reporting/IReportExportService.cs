using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Interface for exporting reports to various formats.
/// </summary>
public interface IReportExportService
{
    /// <summary>
    /// Exports report data to the specified format and returns a download response.
    /// </summary>
    /// <param name="reportName">Name of the report (used for filename)</param>
    /// <param name="reportType">Type of report being exported</param>
    /// <param name="results">The report execution results to export</param>
    /// <param name="exportFormat">Target format: pdf, excel, csv</param>
    /// <returns>Export response with download URL</returns>
    Task<ReportExportResponse> ExportAsync(
        string reportName,
        string reportType,
        ReportResultDto results,
        string exportFormat);
}
