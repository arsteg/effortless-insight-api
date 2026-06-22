using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Services.Storage;
using EffortlessInsight.Api.Jobs;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Features.Workflows.Services;
using EffortlessInsight.Api.Services.Collaboration;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Filters;
using FluentValidation;
using Polly;
using Polly.Extensions.Http;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace EffortlessInsight.Api.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register tenant context for defense-in-depth isolation
        services.AddScoped<ITenantContext, TenantContext>();

        // Register auth services
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<ISmsService, ConsoleSmsSer­vice>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IAuthService, AuthService>();

        // Register application services
        services.AddScoped<INoticeService, NoticeServiceImpl>();
        services.AddScoped<INoticeServiceExtended, NoticeServiceImpl>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAiServiceClient, AiServiceClientImpl>();
        services.AddScoped<IFileStorageService, S3FileStorageServiceImpl>();
        services.AddScoped<IFileStorageServiceExtended, S3FileStorageServiceImpl>();
        services.AddScoped<IEmailService, ResendEmailServiceImpl>();
        services.AddScoped<IAuditService, AuditServiceImpl>();

        // Register organization management services
        services.AddScoped<IGstinValidatorService, GstinValidatorService>();
        services.AddScoped<ICurrentOrganizationService, CurrentOrganizationService>();
        services.AddScoped<IOrganizationManagementService, OrganizationManagementService>();
        services.AddScoped<IOrganizationDataMigrationService, OrganizationDataMigrationService>();

        // Register notice services
        services.AddScoped<IFileValidationService, FileValidationService>();
        services.AddScoped<INoticeWorkflowService, NoticeWorkflowService>();

        // Register workflow engine services
        services.AddScoped<IWorkflowEngineService, WorkflowEngineService>();

        // Register Task & Collaboration services
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IDocumentRequestService, DocumentRequestService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<INotificationService, NotificationService>();

        // Register background jobs
        services.AddScoped<Jobs.OrganizationJobs>();
        services.AddScoped<INoticeProcessingJob, Jobs.NoticeProcessingJob>();
        services.AddScoped<Jobs.WorkflowSlaMonitorJob>();
        services.AddScoped<Jobs.OverdueNotificationJob>();

        // Register billing services
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IUsageService, UsageService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IRazorpayService, RazorpayService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IBillingNotificationService, BillingNotificationService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<Jobs.BillingJobs>();

        // Register filters
        services.AddScoped<InternalApiKeyAuthFilter>();

        return services;
    }

    public static IServiceCollection AddBillingServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure Razorpay options
        services.Configure<RazorpayOptions>(configuration.GetSection(RazorpayOptions.SectionName));

        // Configure Billing options
        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));

        return services;
    }

    public static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            // Password settings
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 8;
            options.Password.RequiredUniqueChars = 4;

            // Lockout settings (handled manually in AuthService for more control)
            options.Lockout.AllowedForNewUsers = false;

            // User settings
            options.User.RequireUniqueEmail = true;
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

            // SignIn settings
            options.SignIn.RequireConfirmedEmail = false; // We handle this in AuthService
            options.SignIn.RequireConfirmedPhoneNumber = false;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        return services;
    }

    public static IServiceCollection AddAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured"));

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            // Disable default claim type mapping so JWT claims are preserved as-is
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero,
                // Map standard claim names
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireOwner", policy => policy.RequireRole("owner"));
            options.AddPolicy("RequireAdmin", policy => policy.RequireRole("owner", "admin"));
            options.AddPolicy("RequireManager", policy => policy.RequireRole("owner", "admin", "manager"));
        });

        return services;
    }

    public static IServiceCollection AddCachingServices(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string not configured");

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "EffortlessInsight:";
        });

        // Register IConnectionMultiplexer for direct Redis access
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnection));

        return services;
    }

    public static IServiceCollection AddBackgroundJobServices(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string not configured");

        services.AddHangfire(config =>
        {
            config.UseRedisStorage(redisConnection, new RedisStorageOptions
            {
                Prefix = "hangfire:",
                SucceededListSize = 500,
                DeletedListSize = 500
            });
        });

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = Environment.ProcessorCount * 2;
            options.Queues = ["critical", "default", "low"];
        });

        return services;
    }

    public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var awsSection = configuration.GetSection("AWS");
        var region = awsSection["Region"] ?? "ap-south-1";
        var accessKeyId = awsSection["AccessKeyId"];
        var secretAccessKey = awsSection["SecretAccessKey"];

        // Register S3 client with explicit credentials if provided
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var config = new AmazonS3Config
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
            };

            if (!string.IsNullOrEmpty(accessKeyId) && !string.IsNullOrEmpty(secretAccessKey))
            {
                // Use explicit credentials from config
                var credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);
                return new AmazonS3Client(credentials, config);
            }

            // Fall back to default credential chain (environment, IAM role, etc.)
            return new AmazonS3Client(config);
        });

        // Configure S3 storage options
        services.Configure<S3StorageOptions>(options =>
        {
            var s3Section = configuration.GetSection("AWS:S3");
            options.BucketName = s3Section["BucketName"] ?? "effortlessinsight-uploads";
            options.ReportsBucket = s3Section["ReportsBucket"] ?? "effortlessinsight-reports";
            options.Region = region;
            options.UploadUrlExpiryMinutes = 15;
            options.DownloadUrlExpiryMinutes = 15;
        });

        return services;
    }

    public static IServiceCollection AddHttpClientServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure AI Service options
        services.Configure<AiServiceOptions>(configuration.GetSection(AiServiceOptions.SectionName));

        // Get options for HttpClient configuration
        var aiOptions = configuration.GetSection(AiServiceOptions.SectionName).Get<AiServiceOptions>()
            ?? new AiServiceOptions();

        // AI Service HTTP Client with resilience policies
        services.AddHttpClient("AiService", client =>
        {
            var baseUrl = aiOptions.BaseUrl;
            if (string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = "http://localhost:8000";
            }

            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(aiOptions.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "EffortlessInsight-API/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Allow self-signed certs in development
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        .AddPolicyHandler(GetRetryPolicy(aiOptions))
        .AddPolicyHandler(GetCircuitBreakerPolicy());

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(AiServiceOptions options)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                options.MaxRetries,
                retryAttempt => TimeSpan.FromSeconds(
                    Math.Pow(options.RetryDelaySeconds, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    // Logging handled in the client itself
                });
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));
    }

    public static IServiceCollection AddValidators(this IServiceCollection services)
    {
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<Program>();

        return services;
    }
}

// Hangfire authorization filter for production
public class HangfireAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        // Get HttpContext from the AspNetCore context
        if (context is Hangfire.Dashboard.AspNetCoreDashboardContext aspNetContext)
        {
            return aspNetContext.HttpContext.User.Identity?.IsAuthenticated == true
                && aspNetContext.HttpContext.User.IsInRole("owner");
        }
        return false;
    }
}
