using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// Service for extension events and configuration.
/// </summary>
public class GstExtensionService : IGstExtensionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GstExtensionService> _logger;
    private readonly IConfiguration _configuration;

    // Current extension version (can be configured)
    private const string LatestExtensionVersion = "1.0.0";

    public GstExtensionService(
        ApplicationDbContext context,
        ILogger<GstExtensionService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task LogEventAsync(Guid? organizationId, Guid? userId, LogExtensionEventRequest request, CancellationToken cancellationToken = default)
    {
        var evt = new GstExtensionEvent
        {
            OrganizationId = organizationId,
            UserId = userId,
            EventType = request.EventType,
            EventData = request.EventData,
            ExtensionVersion = request.ExtensionVersion,
            BrowserInfo = request.BrowserInfo,
            ErrorType = request.ErrorType,
            ErrorMessage = request.ErrorMessage,
            ErrorStack = request.ErrorStack,
            PageUrl = request.PageUrl
        };

        _context.GstExtensionEvents.Add(evt);
        await _context.SaveChangesAsync(cancellationToken);

        // Log errors at warning level
        if (request.EventType == GstExtensionEventType.Error)
        {
            _logger.LogWarning("Extension error from org {OrgId}: {ErrorType} - {ErrorMessage}",
                organizationId, request.ErrorType, request.ErrorMessage);
        }
        else
        {
            _logger.LogDebug("Extension event {EventType} from org {OrgId}",
                request.EventType, organizationId);
        }
    }

    public async Task<ExtensionConfigResponse> GetConfigAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Get all active GST clients for this organization
        var clients = await _context.GstClients
            .Where(c => c.OrganizationId == organizationId && c.SyncEnabled && c.Status != GstClientStatus.Disabled)
            .Select(c => c.Gstin)
            .ToListAsync(cancellationToken);

        // Get DOM selectors from configuration (can be updated without code changes)
        var selectors = _configuration.GetSection("GstSync:DomSelectors").Get<Dictionary<string, string>>()
            ?? GetDefaultSelectors();

        return new ExtensionConfigResponse
        {
            EnabledGstins = clients,
            AutoCapture = true,
            CaptureOnNoticesPage = true,
            AutoDownloadPdf = true,
            ShowNotifications = true,
            SyncIntervalMinutes = 5,
            Selectors = selectors
        };
    }

    public async Task<ExtensionHeartbeatResponse> HeartbeatAsync(Guid organizationId, Guid userId, ExtensionHeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        // Log heartbeat event
        await LogEventAsync(organizationId, userId, new LogExtensionEventRequest
        {
            EventType = GstExtensionEventType.Heartbeat,
            ExtensionVersion = request.ExtensionVersion,
            BrowserInfo = request.BrowserInfo,
            EventData = new Dictionary<string, object>
            {
                ["lastActivity"] = request.LastActivity?.ToString("O") ?? ""
            }
        }, cancellationToken);

        // Check if update is available
        var updateAvailable = !string.IsNullOrEmpty(request.ExtensionVersion) &&
                              CompareVersions(request.ExtensionVersion, LatestExtensionVersion) < 0;

        return new ExtensionHeartbeatResponse
        {
            Status = "ok",
            UpdateAvailable = updateAvailable,
            ConfigChanged = false, // Could implement config versioning
            LatestVersion = updateAvailable ? LatestExtensionVersion : null
        };
    }

    /// <summary>
    /// Compare semantic versions. Returns -1 if v1 &lt; v2, 0 if equal, 1 if v1 &gt; v2.
    /// </summary>
    private static int CompareVersions(string v1, string v2)
    {
        var parts1 = v1.Split('.').Select(int.Parse).ToArray();
        var parts2 = v2.Split('.').Select(int.Parse).ToArray();

        for (var i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : 0;
            var p2 = i < parts2.Length ? parts2[i] : 0;

            if (p1 < p2) return -1;
            if (p1 > p2) return 1;
        }

        return 0;
    }

    /// <summary>
    /// Default DOM selectors for GST portal parsing.
    /// These can be overridden via configuration for quick fixes without code changes.
    /// </summary>
    private static Dictionary<string, string> GetDefaultSelectors()
    {
        return new Dictionary<string, string>
        {
            ["noticeTable"] = "#notice-table, .notice-table, table[id*='notice']",
            ["noticeRow"] = "tbody tr, .notice-row",
            ["noticeId"] = "td:nth-child(1), .notice-id",
            ["noticeType"] = "td:nth-child(2), .notice-type",
            ["issueDate"] = "td:nth-child(3), .issue-date",
            ["dueDate"] = "td:nth-child(4), .due-date",
            ["demandAmount"] = "td:nth-child(5), .demand-amount",
            ["status"] = "td:nth-child(6), .notice-status",
            ["pdfLink"] = "a[href*='pdf'], a[href*='download'], .pdf-link"
        };
    }
}
