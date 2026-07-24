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

// Add Local configuration as optional overlay (for personal developer settings)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

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

    // Add JWT Bearer authentication to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.\n\nExample: 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...'",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
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
builder.Services.AddScoped<PlanSeeder>();

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
        new() { Endpoint = "POST:/api/v1/whatsapp/webhook", Period = "1s", Limit = 100 },

        // Razorpay webhook rate limits (Fixes Issue #11: Rate Limiting for Webhook Endpoint)
        // Allow burst of webhooks but prevent abuse
        new() { Endpoint = "POST:/api/webhooks/razorpay", Period = "1s", Limit = 50 },
        new() { Endpoint = "POST:/api/webhooks/razorpay", Period = "1m", Limit = 200 },
        new() { Endpoint = "POST:/api/webhooks/razorpay", Period = "1h", Limit = 1000 },

        // AI-related endpoints rate limits (CRIT-002: Prevent DoS and cost explosion)
        // Auto-draft generation (high cost - GPT-4 calls)
        new() { Endpoint = "POST:/api/v1/notices/*/responses/auto-draft", Period = "1m", Limit = 5 },
        new() { Endpoint = "POST:/api/v1/notices/*/responses/auto-draft", Period = "1h", Limit = 20 },

        // AI conversation endpoints (streaming and sync)
        new() { Endpoint = "POST:/api/v1/conversations/*/messages", Period = "1m", Limit = 10 },
        new() { Endpoint = "POST:/api/v1/conversations/*/messages", Period = "1h", Limit = 100 },
        new() { Endpoint = "POST:/api/v1/conversations/*/messages/sync", Period = "1m", Limit = 10 },
        new() { Endpoint = "POST:/api/v1/conversations/*/messages/sync", Period = "1h", Limit = 100 },

        // Message regeneration (high cost)
        new() { Endpoint = "POST:/api/v1/conversations/*/messages/*/regenerate", Period = "1m", Limit = 5 },
        new() { Endpoint = "POST:/api/v1/conversations/*/messages/*/regenerate", Period = "1h", Limit = 30 },

        // Suggested questions generation
        new() { Endpoint = "GET:/api/v1/notices/*/suggested-questions", Period = "1m", Limit = 20 },
        new() { Endpoint = "GET:/api/v1/notices/*/suggested-questions", Period = "1h", Limit = 200 },

        // Notice reprocessing (very high cost - full AI pipeline)
        new() { Endpoint = "POST:/api/v1/notices/*/report/retry", Period = "1m", Limit = 3 },
        new() { Endpoint = "POST:/api/v1/notices/*/report/retry", Period = "1h", Limit = 10 },

        // Notice upload (moderate cost)
        new() { Endpoint = "POST:/api/v1/notices/upload", Period = "1m", Limit = 10 },
        new() { Endpoint = "POST:/api/v1/notices/upload", Period = "1h", Limit = 50 },
        new() { Endpoint = "POST:/api/v1/notices/upload/batch", Period = "1m", Limit = 3 },
        new() { Endpoint = "POST:/api/v1/notices/upload/batch", Period = "1h", Limit = 20 }
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
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!)
    .AddWhatsAppHealthCheck();

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

// Only use HTTPS redirection in production
// Skip for Development and Local environments to avoid CORS issues
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

// Tenant context (must come after authentication so the org_id claim is available)
app.UseTenantContext();

// Subscription enforcement (must come after authentication/authorization)
app.UseSubscriptionEnforcement();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapNotificationEndpoints();
app.MapNoticeEndpoints();

// Hangfire Dashboard (protected in production)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Local"
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
// MigrateAsync is relational-only; skip it under a non-relational provider
// (e.g. the InMemory database used by integration tests).
if (dbContext.Database.IsRelational())
{
    await dbContext.Database.MigrateAsync();
}

// Seed default workflow template
var workflowSeeder = scope.ServiceProvider.GetRequiredService<WorkflowTemplateSeeder>();
await workflowSeeder.SeedAsync();

// Seed initial admin user (from environment variables)
var adminSeeder = scope.ServiceProvider.GetRequiredService<AdminUserSeeder>();
await adminSeeder.SeedAsync();

// Seed default subscription plans
var planSeeder = scope.ServiceProvider.GetRequiredService<PlanSeeder>();
await planSeeder.SeedAsync();

// Seed default notification templates
var templateService = scope.ServiceProvider.GetRequiredService<EffortlessInsight.Api.Services.Notifications.INotificationTemplateService>();
await templateService.SeedDefaultTemplatesAsync();
//}

Log.Information("EffortlessInsight API starting...");

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
