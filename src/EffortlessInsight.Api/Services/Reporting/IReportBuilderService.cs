using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Service interface for the Custom Report Builder (GAP-RPT-003)
/// </summary>
public interface IReportBuilderService
{
    // ==========================================================================
    // Saved Report CRUD
    // ==========================================================================

    /// <summary>
    /// Creates a new saved report
    /// </summary>
    Task<SavedReportDto> SaveReportAsync(Guid organizationId, CreateSavedReportRequest request, Guid userId);

    /// <summary>
    /// Gets a saved report by ID
    /// </summary>
    Task<SavedReportDto> GetReportAsync(Guid reportId, Guid userId);

    /// <summary>
    /// Lists all saved reports for an organization
    /// </summary>
    Task<SavedReportListResponse> ListReportsAsync(
        Guid organizationId,
        Guid userId,
        string? reportType = null,
        bool? isPublic = null,
        string? searchTerm = null,
        int page = 1,
        int pageSize = 20);

    /// <summary>
    /// Updates a saved report
    /// </summary>
    Task<SavedReportDto> UpdateReportAsync(Guid reportId, UpdateSavedReportRequest request, Guid userId);

    /// <summary>
    /// Deletes a saved report (soft delete)
    /// </summary>
    Task DeleteReportAsync(Guid reportId, Guid userId);

    // ==========================================================================
    // Report Execution
    // ==========================================================================

    /// <summary>
    /// Executes a saved report and returns the results
    /// </summary>
    Task<ReportExecutionResponse> ExecuteReportAsync(
        Guid reportId,
        Guid userId,
        ExecuteReportRequest? request = null);

    /// <summary>
    /// Executes a report configuration without saving it (preview)
    /// </summary>
    Task<ReportExecutionResponse> PreviewReportAsync(
        Guid organizationId,
        string reportType,
        ReportConfigurationDto configuration,
        Guid userId,
        int page = 1,
        int pageSize = 50);

    /// <summary>
    /// Exports a report to the specified format
    /// </summary>
    Task<ReportExportResponse> ExportReportAsync(
        Guid reportId,
        Guid userId,
        string exportFormat);

    // ==========================================================================
    // Report Schema
    // ==========================================================================

    /// <summary>
    /// Gets the available columns and configuration options for a report type
    /// </summary>
    Task<ReportSchemaResponse> GetReportSchemaAsync(string reportType);

    /// <summary>
    /// Validates a report configuration
    /// </summary>
    Task<(bool IsValid, List<string> Errors)> ValidateConfigurationAsync(
        string reportType,
        ReportConfigurationDto configuration);
}
