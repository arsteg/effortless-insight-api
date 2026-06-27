using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;

namespace EffortlessInsight.Api.Extensions;

/// <summary>
/// Extensions for configuring CloudWatch-compatible logging.
/// Uses Serilog with JSON formatting for CloudWatch Logs Insights compatibility.
/// </summary>
public static class CloudWatchLoggingExtensions
{
    /// <summary>
    /// Configures Serilog for CloudWatch-compatible JSON logging.
    /// In AWS ECS/EKS, stdout/stderr logs are automatically captured by CloudWatch.
    /// </summary>
    public static LoggerConfiguration ConfigureForCloudWatch(
        this LoggerConfiguration config,
        IConfiguration configuration,
        string? logGroupName = null)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var serviceName = configuration["ServiceName"] ?? "EffortlessInsight.Api";
        var effectiveLogGroup = logGroupName ?? $"/ecs/{serviceName}/{environment}";

        return config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .Enrich.WithProperty("Environment", environment)
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("LogGroup", effectiveLogGroup)
            .WriteTo.Console(new CloudWatchJsonFormatter());
    }

    /// <summary>
    /// Custom JSON formatter optimized for CloudWatch Logs Insights queries.
    /// </summary>
    private class CloudWatchJsonFormatter : ITextFormatter
    {
        public void Format(LogEvent logEvent, TextWriter output)
        {
            var properties = new Dictionary<string, object?>
            {
                ["timestamp"] = logEvent.Timestamp.UtcDateTime.ToString("O"),
                ["level"] = logEvent.Level.ToString(),
                ["message"] = logEvent.RenderMessage(),
                ["messageTemplate"] = logEvent.MessageTemplate.Text
            };

            // Add exception details if present
            if (logEvent.Exception != null)
            {
                properties["exception"] = new
                {
                    type = logEvent.Exception.GetType().FullName,
                    message = logEvent.Exception.Message,
                    stackTrace = logEvent.Exception.StackTrace,
                    innerException = logEvent.Exception.InnerException?.Message
                };
            }

            // Add all log properties
            foreach (var property in logEvent.Properties)
            {
                properties[property.Key] = ConvertPropertyValue(property.Value);
            }

            // Serialize to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(properties, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            output.WriteLine(json);
        }

        private static object? ConvertPropertyValue(LogEventPropertyValue propertyValue)
        {
            return propertyValue switch
            {
                ScalarValue scalar => scalar.Value,
                SequenceValue sequence => sequence.Elements.Select(ConvertPropertyValue).ToArray(),
                StructureValue structure => structure.Properties.ToDictionary(p => p.Name, p => ConvertPropertyValue(p.Value)),
                DictionaryValue dictionary => dictionary.Elements.ToDictionary(
                    kvp => ConvertPropertyValue(kvp.Key)?.ToString() ?? "",
                    kvp => ConvertPropertyValue(kvp.Value)),
                _ => propertyValue.ToString()
            };
        }
    }
}

/// <summary>
/// Configuration options for CloudWatch logging
/// </summary>
public class CloudWatchLoggingOptions
{
    /// <summary>
    /// The CloudWatch log group name
    /// </summary>
    public string LogGroupName { get; set; } = "/ecs/effortless-insight-api";

    /// <summary>
    /// Whether to include request body in logs (for debugging)
    /// </summary>
    public bool LogRequestBody { get; set; } = false;

    /// <summary>
    /// Whether to include response body in logs (for debugging)
    /// </summary>
    public bool LogResponseBody { get; set; } = false;

    /// <summary>
    /// Maximum length of logged body content
    /// </summary>
    public int MaxBodyLength { get; set; } = 1024;

    /// <summary>
    /// Fields to mask in logs (for PII compliance)
    /// </summary>
    public List<string> MaskedFields { get; set; } = new()
    {
        "password", "token", "secret", "key", "authorization",
        "pan", "gstin", "aadhaar", "phone", "email", "mobile"
    };
}

/// <summary>
/// Request logging middleware for CloudWatch
/// </summary>
public class CloudWatchRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CloudWatchRequestLoggingMiddleware> _logger;
    private readonly CloudWatchLoggingOptions _options;

    public CloudWatchRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<CloudWatchRequestLoggingMiddleware> logger,
        IOptions<CloudWatchLoggingOptions>? options = null)
    {
        _next = next;
        _logger = logger;
        _options = options?.Value ?? new CloudWatchLoggingOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        var startTime = DateTime.UtcNow;

        // Add request context to logs
        using (Serilog.Context.LogContext.PushProperty("RequestId", requestId))
        using (Serilog.Context.LogContext.PushProperty("Method", context.Request.Method))
        using (Serilog.Context.LogContext.PushProperty("Path", context.Request.Path.Value))
        using (Serilog.Context.LogContext.PushProperty("UserAgent", context.Request.Headers.UserAgent.ToString()))
        using (Serilog.Context.LogContext.PushProperty("ClientIP", context.Connection.RemoteIpAddress?.ToString()))
        {
            try
            {
                await _next(context);
            }
            finally
            {
                var duration = DateTime.UtcNow - startTime;

                // Log request completion
                _logger.LogInformation(
                    "HTTP {Method} {Path} responded {StatusCode} in {Duration}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    duration.TotalMilliseconds);
            }
        }
    }
}

/// <summary>
/// Extension methods for CloudWatch request logging
/// </summary>
public static class CloudWatchRequestLoggingExtensions
{
    public static IApplicationBuilder UseCloudWatchRequestLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CloudWatchRequestLoggingMiddleware>();
    }

    public static IServiceCollection AddCloudWatchLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CloudWatchLoggingOptions>(
            configuration.GetSection("CloudWatch"));
        return services;
    }
}
