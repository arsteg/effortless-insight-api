using System.Security.Authentication;
using System.Text.Json;
using AspNetCoreRateLimit;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Extensions;
using EffortlessInsight.Api.Middleware;
using Hangfire;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for TLS 1.3 enforcement in production
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ConfigureHttpsDefaults(httpsOptions =>
        {
            // Enforce TLS 1.2 minimum, prefer TLS 1.3
            httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        });
    });
}

// Configure Sentry for error tracking
builder.WebHost.UseSentry(options =>
{
    options.Dsn = builder.Configuration["Sentry:Dsn"] ?? "";
    options.Environment = builder.Environment.EnvironmentName;
    options.TracesSampleRate = builder.Environment.IsDevelopment() ? 1.0 : 0.1;
    options.Debug = builder.Environment.IsDevelopment();
    options.SendDefaultPii = false; // Don't send PII
    options.AttachStacktrace = true;
    options.MaxBreadcrumbs = 50;
    options.MinimumBreadcrumbLevel = LogLevel.Information;
    options.MinimumEventLevel = LogLevel.Error;
});

// Configure Serilog with PII masking
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With<EffortlessInsight.Api.Middleware.PiiMaskingEnricher>() // Mask PII in logs for DPDP compliance
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "EffortlessInsight API", Version = "v1" });

    // Use full type name to avoid schema ID conflicts for types with same name in different namespaces
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);

    // Resolve conflicting actions by taking the first one
    options.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());

    // Custom operation ID generator to avoid conflicts
    options.CustomOperationIds(apiDesc =>
    {
        var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"];
        var actionName = apiDesc.ActionDescriptor.RouteValues["action"];
        var method = apiDesc.HttpMethod?.ToLower() ?? "get";
        return $"{controllerName}_{actionName}_{method}";
    });
});

// Configure DbContext with PostgreSQL + pgvector
// Build NpgsqlDataSource with dynamic JSON support for JSONB columns with List<T> types
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        dataSource,
        npgsqlOptions =>
        {
            npgsqlOptions.UseVector();
            npgsqlOptions.EnableRetryOnFailure(3);
        })
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// Add Identity
builder.Services.AddIdentityServices(builder.Configuration);

// Add custom services
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddAuthenticationServices(builder.Configuration);
builder.Services.AddCachingServices(builder.Configuration);
builder.Services.AddBackgroundJobServices(builder.Configuration);
builder.Services.AddAwsServices(builder.Configuration);
builder.Services.AddHttpClientServices(builder.Configuration);
builder.Services.AddNotificationServices(builder.Configuration);
builder.Services.AddBillingServices(builder.Configuration);
builder.Services.AddAdminServices(builder.Configuration);
builder.Services.AddAdminAuthentication(builder.Configuration);
builder.Services.AddWhatsAppServices(builder.Configuration);

// Add Database Seeders
builder.Services.AddScoped<WorkflowTemplateSeeder>();
builder.Services.AddScoped<AdminUserSeeder>();

// Add AutoMapper
builder.Services.AddAutoMapper(typeof(Program));

// Add FluentValidation
builder.Services.AddValidators();

// Add Rate Limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.HttpStatusCode = 429;
    options.RealIpHeader = "X-Forwarded-For";
    options.GeneralRules = new List<RateLimitRule>
    {
        new() { Endpoint = "POST:/api/v1/auth/register", Period = "1h", Limit = 15 },
        new() { Endpoint = "POST:/api/v1/auth/login", Period = "1m", Limit = 10 },
        new() { Endpoint = "POST:/api/v1/auth/otp/request", Period = "1h", Limit = 15 },
        new() { Endpoint = "POST:/api/v1/auth/forgot-password", Period = "1h", Limit = 10 },
        new() { Endpoint = "POST:/api/v1/auth/2fa/login", Period = "5m", Limit = 10 },
        // WhatsApp rate limits
        new() { Endpoint = "POST:/api/v1/whatsapp/link/request", Period = "1h", Limit = 5 },
        new() { Endpoint = "POST:/api/v1/whatsapp/link/verify", Period = "5m", Limit = 10 },
        new() { Endpoint = "POST:/api/v1/whatsapp/webhook", Period = "1s", Limit = 100 }
    };
    // Add admin rate limit rules
    AdminServiceExtensions.AddAdminRateLimitRules(options);
});
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Add CORS
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
var isDevelopment = builder.Environment.IsDevelopment();
Log.Information("CORS origins configured: {Origins}, IsDevelopment: {IsDev}", string.Join(", ", corsOrigins), isDevelopment);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", policy =>
    {
        if (isDevelopment)
        {
            // In development, allow any localhost origin
            policy.SetIsOriginAllowed(origin =>
                {
                    var uri = new Uri(origin);
                    var isAllowed = uri.Host == "localhost" || uri.Host == "127.0.0.1";
                    Log.Debug("CORS check for origin {Origin}: {IsAllowed}", origin, isAllowed);
                    return isAllowed;
                })
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            Log.Warning("No CORS origins configured for production!");
        }
    });
});

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

var app = builder.Build();

// Initialize field encryption service accessor for EF Core value converters
var encryptionService = app.Services.GetRequiredService<EffortlessInsight.Api.Services.Encryption.IFieldEncryptionService>();
EffortlessInsight.Api.Services.Encryption.FieldEncryptionServiceAccessor.SetInstance(encryptionService);

// Configure the HTTP request pipeline
//if (app.Environment.IsDevelopment())
//{
app.UseDeveloperExceptionPage(); // Show detailed errors
app.UseSwagger();
app.UseSwaggerUI();
//}

// CORS must be early in pipeline so all responses (including errors) have CORS headers
app.UseCors("AllowedOrigins");

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// Add security headers to all responses
app.UseSecurityHeaders();

app.UseIpRateLimiting();

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapNotificationEndpoints();

// Hangfire Dashboard (protected in production)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = app.Environment.IsDevelopment()
        ? []
        : [new HangfireAuthorizationFilter()]
});

// Configure recurring workflow jobs
EffortlessInsight.Api.Jobs.WorkflowJobsExtensions.ConfigureWorkflowJobs(app);

// Configure recurring organization jobs (suspension expiry, invitation cleanup)
EffortlessInsight.Api.Jobs.OrganizationJobsExtensions.ConfigureOrganizationJobs(app);

// Configure recurring collaboration jobs (task & document request notifications)
EffortlessInsight.Api.Jobs.CollaborationJobsExtensions.ConfigureCollaborationJobs(app);

// Configure recurring notification jobs
EffortlessInsight.Api.Jobs.NotificationJobsExtensions.ConfigureNotificationJobs(app);

// Configure recurring billing jobs
EffortlessInsight.Api.Jobs.BillingJobsExtensions.ConfigureBillingJobs(app);

// Configure data retention cleanup jobs
EffortlessInsight.Api.Jobs.DataRetentionJobsExtensions.ConfigureDataRetentionJobs(app);

// Configure scheduled report jobs
EffortlessInsight.Api.Jobs.ReportingJobsExtensions.ConfigureReportingJobs(app);

// Configure GSTN integration jobs (token refresh, notice sync, cleanup)
EffortlessInsight.Api.Jobs.GstnJobsExtensions.ConfigureGstnJobs(app);

// Configure WhatsApp bot jobs (cleanup, template sync, daily digest, reminders)
EffortlessInsight.Api.Jobs.WhatsAppJobsExtensions.ConfigureWhatsAppJobs(app);

// Apply migrations and seed data on startup in development
//if (app.Environment.IsDevelopment())
//{
using var scope = app.Services.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
await dbContext.Database.MigrateAsync();

// Seed default workflow template
var workflowSeeder = scope.ServiceProvider.GetRequiredService<WorkflowTemplateSeeder>();
await workflowSeeder.SeedAsync();

// Seed initial admin user (from environment variables)
var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminUserSeeder>();
await adminSeeder.SeedAsync();

// Seed default notification templates
var templateService = scope.ServiceProvider.GetRequiredService<EffortlessInsight.Api.Services.Notifications.INotificationTemplateService>();
await templateService.SeedDefaultTemplatesAsync();
//}

Log.Information("EffortlessInsight API starting...");

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
