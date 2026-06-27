using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Reporting;

/// <summary>
/// Interface for building and executing dynamic report queries
/// </summary>
public interface IReportQueryBuilder
{
    /// <summary>
    /// Builds and executes a dynamic query based on the report configuration
    /// </summary>
    Task<ReportResultDto> BuildAndExecuteAsync(
        Guid organizationId,
        string reportType,
        ReportConfiguration configuration,
        int page = 1,
        int pageSize = 50);
}
