using System.Text.Json;
using AspNetCoreRateLimit;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Extensions;
using EffortlessInsight.Api.Middleware;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "EffortlessInsight API", Version = "v1" });

    // Use full type name to avoid schema ID conflicts for types with same name in different namespaces
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);
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

// Add Database Seeders
builder.Services.AddScoped<WorkflowTemplateSeeder>();

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
        new() { Endpoint = "POST:/api/v1/auth/2fa/login", Period = "5m", Limit = 10 }
    };
    // Add admin rate limit rules
    AdminServiceExtensions.AddAdminRateLimitRules(options);
});
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!);

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseIpRateLimiting();

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();
app.UseCors("AllowedOrigins");

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

// Configure recurring collaboration jobs (task & document request notifications)
EffortlessInsight.Api.Jobs.CollaborationJobsExtensions.ConfigureCollaborationJobs(app);

// Configure recurring notification jobs
EffortlessInsight.Api.Jobs.NotificationJobsExtensions.ConfigureNotificationJobs(app);

// Configure recurring billing jobs
EffortlessInsight.Api.Jobs.BillingJobsExtensions.ConfigureBillingJobs(app);

// Apply migrations and seed data on startup in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();

    // Seed default workflow template
    var workflowSeeder = scope.ServiceProvider.GetRequiredService<WorkflowTemplateSeeder>();
    await workflowSeeder.SeedAsync();

    // Seed default notification templates
    var templateService = scope.ServiceProvider.GetRequiredService<EffortlessInsight.Api.Services.Notifications.INotificationTemplateService>();
    await templateService.SeedDefaultTemplatesAsync();
}

Log.Information("EffortlessInsight API starting...");

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
