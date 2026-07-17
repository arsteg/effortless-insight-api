using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Middleware for collecting HTTP request metrics for Prometheus/OpenTelemetry.
/// Tracks request count, duration, and status codes.
/// </summary>
public class PrometheusMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PrometheusMetricsMiddleware> _logger;

    // Metrics using .NET 8+ Meter API (compatible with OpenTelemetry)
    private static readonly Meter Meter = new("EffortlessInsight.Api", "1.0.0");

    // Request counter
    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>(
        "http_requests_total",
        description: "Total number of HTTP requests");

    // Request duration histogram
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "http_request_duration_seconds",
        unit: "s",
        description: "HTTP request duration in seconds");

    // Active requests gauge
    private static readonly UpDownCounter<long> ActiveRequests = Meter.CreateUpDownCounter<long>(
        "http_requests_in_progress",
        description: "Number of HTTP requests currently being processed");

    // Error counter
    private static readonly Counter<long> ErrorCounter = Meter.CreateCounter<long>(
        "http_errors_total",
        description: "Total number of HTTP errors");

    public PrometheusMetricsMiddleware(RequestDelegate next, ILogger<PrometheusMetricsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip metrics endpoint itself
        if (context.Request.Path.StartsWithSegments("/metrics"))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = GetNormalizedPath(context.Request.Path);

        ActiveRequests.Add(1, new KeyValuePair<string, object?>("method", method));

        try
        {
            await _next(context);
        }
        catch (Exception)
        {
            // Record exception
            ErrorCounter.Add(1,
                new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("path", path),
                new KeyValuePair<string, object?>("exception_type", "unhandled"));
            throw;
        }
        finally
        {
            stopwatch.Stop();
            ActiveRequests.Add(-1, new KeyValuePair<string, object?>("method", method));

            var statusCode = context.Response.StatusCode;
            var statusCategory = GetStatusCategory(statusCode);

            // Record request metrics
            RequestCounter.Add(1,
                new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("path", path),
                new KeyValuePair<string, object?>("status_code", statusCode.ToString()),
                new KeyValuePair<string, object?>("status_category", statusCategory));

            RequestDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("path", path),
                new KeyValuePair<string, object?>("status_category", statusCategory));

            // Track errors
            if (statusCode >= 400)
            {
                ErrorCounter.Add(1,
                    new KeyValuePair<string, object?>("method", method),
                    new KeyValuePair<string, object?>("path", path),
                    new KeyValuePair<string, object?>("status_code", statusCode.ToString()));
            }
        }
    }

    /// <summary>
    /// Normalize path to prevent cardinality explosion.
    /// Replaces GUIDs and numeric IDs with placeholders.
    /// </summary>
    private static string GetNormalizedPath(PathString path)
    {
        var pathValue = path.Value ?? "/";

        // Replace GUIDs with placeholder
        pathValue = System.Text.RegularExpressions.Regex.Replace(
            pathValue,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "{id}");

        // Replace numeric IDs with placeholder
        pathValue = System.Text.RegularExpressions.Regex.Replace(
            pathValue,
            @"/\d+",
            "/{id}");

        return pathValue;
    }

    private static string GetStatusCategory(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 => "5xx",
        _ => "unknown"
    };
}

/// <summary>
/// Extension methods for Prometheus metrics middleware
/// </summary>
public static class PrometheusMetricsMiddlewareExtensions
{
    public static IApplicationBuilder UsePrometheusMetrics(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PrometheusMetricsMiddleware>();
    }

    public static IServiceCollection AddPrometheusMetrics(this IServiceCollection services)
    {
        // Add OpenTelemetry metrics exporter for Prometheus
        // This can be configured with prometheus-net or OpenTelemetry.Exporter.Prometheus
        return services;
    }
}

/// <summary>
/// Custom metrics for business logic tracking
/// </summary>
public static class BusinessMetrics
{
    private static readonly Meter Meter = new("EffortlessInsight.Business", "1.0.0");

    // Notice processing metrics
    public static readonly Counter<long> NoticesProcessed = Meter.CreateCounter<long>(
        "notices_processed_total",
        description: "Total number of notices processed");

    public static readonly Histogram<double> NoticeProcessingDuration = Meter.CreateHistogram<double>(
        "notice_processing_duration_seconds",
        unit: "s",
        description: "Notice processing duration in seconds");

    // AI service metrics
    public static readonly Counter<long> AiRequestsTotal = Meter.CreateCounter<long>(
        "ai_requests_total",
        description: "Total AI service requests");

    public static readonly Histogram<double> AiRequestDuration = Meter.CreateHistogram<double>(
        "ai_request_duration_seconds",
        unit: "s",
        description: "AI service request duration");

    // Authentication metrics
    public static readonly Counter<long> LoginAttempts = Meter.CreateCounter<long>(
        "login_attempts_total",
        description: "Total login attempts");

    public static readonly Counter<long> FailedLogins = Meter.CreateCounter<long>(
        "failed_logins_total",
        description: "Total failed login attempts");

    // Workflow metrics
    public static readonly Counter<long> WorkflowTransitions = Meter.CreateCounter<long>(
        "workflow_transitions_total",
        description: "Total workflow stage transitions");

    // Storage metrics
    public static readonly Counter<long> FilesUploaded = Meter.CreateCounter<long>(
        "files_uploaded_total",
        description: "Total files uploaded");

    public static readonly Counter<long> FileBytesUploaded = Meter.CreateCounter<long>(
        "file_bytes_uploaded_total",
        unit: "bytes",
        description: "Total bytes uploaded");

    // WhatsApp metrics
    public static readonly Counter<long> WhatsAppMessagesSent = Meter.CreateCounter<long>(
        "whatsapp_messages_sent_total",
        description: "Total WhatsApp messages sent");

    public static readonly Counter<long> WhatsAppMessagesReceived = Meter.CreateCounter<long>(
        "whatsapp_messages_received_total",
        description: "Total WhatsApp messages received");

    public static readonly Counter<long> WhatsAppMessagesFailed = Meter.CreateCounter<long>(
        "whatsapp_messages_failed_total",
        description: "Total WhatsApp messages that failed");

    public static readonly Counter<long> WhatsAppMessagesRetried = Meter.CreateCounter<long>(
        "whatsapp_messages_retried_total",
        description: "Total WhatsApp messages retried");

    public static readonly Histogram<double> WhatsAppMessageLatency = Meter.CreateHistogram<double>(
        "whatsapp_message_latency_seconds",
        unit: "s",
        description: "WhatsApp message processing latency");

    public static readonly Counter<long> WhatsAppSessionsLinked = Meter.CreateCounter<long>(
        "whatsapp_sessions_linked_total",
        description: "Total WhatsApp sessions linked");

    public static readonly Counter<long> WhatsAppSessionsUnlinked = Meter.CreateCounter<long>(
        "whatsapp_sessions_unlinked_total",
        description: "Total WhatsApp sessions unlinked");

    /// <summary>
    /// Record a notice processing event
    /// </summary>
    public static void RecordNoticeProcessed(string status, string noticeType, double durationSeconds)
    {
        NoticesProcessed.Add(1,
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("notice_type", noticeType));

        NoticeProcessingDuration.Record(durationSeconds,
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>("notice_type", noticeType));
    }

    /// <summary>
    /// Record an AI service request
    /// </summary>
    public static void RecordAiRequest(string service, string status, double durationSeconds)
    {
        AiRequestsTotal.Add(1,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("status", status));

        AiRequestDuration.Record(durationSeconds,
            new KeyValuePair<string, object?>("service", service),
            new KeyValuePair<string, object?>("status", status));
    }

    /// <summary>
    /// Record a WhatsApp message sent
    /// </summary>
    public static void RecordWhatsAppMessageSent(string messageType, string status, double latencySeconds)
    {
        WhatsAppMessagesSent.Add(1,
            new KeyValuePair<string, object?>("type", messageType),
            new KeyValuePair<string, object?>("status", status));

        if (status == "failed")
        {
            WhatsAppMessagesFailed.Add(1,
                new KeyValuePair<string, object?>("type", messageType));
        }

        WhatsAppMessageLatency.Record(latencySeconds,
            new KeyValuePair<string, object?>("type", messageType),
            new KeyValuePair<string, object?>("direction", "outbound"));
    }

    /// <summary>
    /// Record a WhatsApp message received
    /// </summary>
    public static void RecordWhatsAppMessageReceived(string messageType, string command)
    {
        WhatsAppMessagesReceived.Add(1,
            new KeyValuePair<string, object?>("type", messageType),
            new KeyValuePair<string, object?>("command", command ?? "unknown"));
    }

    /// <summary>
    /// Record a WhatsApp message retry
    /// </summary>
    public static void RecordWhatsAppMessageRetry(string messageType, bool success)
    {
        WhatsAppMessagesRetried.Add(1,
            new KeyValuePair<string, object?>("type", messageType),
            new KeyValuePair<string, object?>("success", success.ToString()));
    }

    /// <summary>
    /// Record a WhatsApp session linked
    /// </summary>
    public static void RecordWhatsAppSessionLinked()
    {
        WhatsAppSessionsLinked.Add(1);
    }

    /// <summary>
    /// Record a WhatsApp session unlinked
    /// </summary>
    public static void RecordWhatsAppSessionUnlinked()
    {
        WhatsAppSessionsUnlinked.Add(1);
    }
}
