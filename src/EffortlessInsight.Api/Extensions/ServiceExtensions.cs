using System.Text;
using Amazon.S3;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Auth;
using FluentValidation;
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
        // Register auth services
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<ISmsService, ConsoleSmsSer­vice>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IAuthService, AuthService>();

        // Register application services
        services.AddScoped<INoticeService, NoticeService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAiServiceClient, AiServiceClient>();
        services.AddScoped<IFileStorageService, S3FileStorageService>();
        services.AddScoped<IEmailService, SendGridEmailService>();
        services.AddScoped<IAuditService, AuditService>();

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
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
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
        services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        services.AddAWSService<IAmazonS3>();

        return services;
    }

    public static IServiceCollection AddHttpClientServices(this IServiceCollection services, IConfiguration configuration)
    {
        // AI Service HTTP Client
        services.AddHttpClient("AiService", client =>
        {
            var baseUrl = configuration["AiService:BaseUrl"]
                ?? throw new InvalidOperationException("AI Service base URL not configured");
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(120); // AI processing can take time
        });

        return services;
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
