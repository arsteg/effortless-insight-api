using System.Text;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace EffortlessInsight.Api.Extensions;

/// <summary>
/// Extension methods for configuring admin portal services.
/// </summary>
public static class AdminServiceExtensions
{
    /// <summary>
    /// Adds admin portal services to the service collection.
    /// </summary>
    public static IServiceCollection AddAdminServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure admin auth options
        services.Configure<AdminAuthOptions>(configuration.GetSection(AdminAuthOptions.SectionName));

        // Register admin authentication services
        services.AddScoped<IAdminAuthService, AdminAuthService>();
        services.AddScoped<IAdminJwtService, AdminJwtService>();
        services.AddScoped<IAdminAuditService, AdminAuditService>();
        services.AddScoped<IAdminMfaService, AdminMfaService>();
        services.AddScoped<IAdminSessionService, AdminSessionService>();

        return services;
    }

    /// <summary>
    /// Adds admin-specific authentication scheme.
    /// Admin portal uses a separate JWT scheme for security isolation.
    /// </summary>
    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var adminAuthSection = configuration.GetSection(AdminAuthOptions.SectionName);
        var jwtSecret = adminAuthSection["JwtSecret"];

        if (string.IsNullOrEmpty(jwtSecret) || jwtSecret.Length < 32)
        {
            // In development, allow startup without admin auth configured
            // In production, this should fail
            return services;
        }

        var key = Encoding.UTF8.GetBytes(jwtSecret);

        services.AddAuthentication()
            .AddJwtBearer("AdminBearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = adminAuthSection["JwtIssuer"] ?? "EffortlessInsight-Admin",
                    ValidAudience = adminAuthSection["JwtAudience"] ?? "EffortlessInsight-Admin-Portal",
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.Headers.Append("X-Token-Expired", "true");
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        // Add admin-specific authorization policies
        services.AddAuthorization(options =>
        {
            // Super Admin - full access
            options.AddPolicy("AdminSuperAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add("AdminBearer");
                policy.RequireClaim("admin_role", "super_admin");
            });

            // Operations Admin - system monitoring and user management
            options.AddPolicy("AdminOperations", policy =>
            {
                policy.AuthenticationSchemes.Add("AdminBearer");
                policy.RequireClaim("admin_role", "super_admin", "operations_admin");
            });

            // Finance Admin - billing and subscription management
            options.AddPolicy("AdminFinance", policy =>
            {
                policy.AuthenticationSchemes.Add("AdminBearer");
                policy.RequireClaim("admin_role", "super_admin", "finance_admin");
            });

            // Support Admin - customer support operations
            options.AddPolicy("AdminSupport", policy =>
            {
                policy.AuthenticationSchemes.Add("AdminBearer");
                policy.RequireClaim("admin_role", "super_admin", "support_admin");
            });

            // Content Admin - content management
            options.AddPolicy("AdminContent", policy =>
            {
                policy.AuthenticationSchemes.Add("AdminBearer");
                policy.RequireClaim("admin_role", "super_admin", "content_admin");
            });

            // Any authenticated admin
            options.AddPolicy("AdminAuthenticated", policy =>
            {
                policy.AuthenticationSchemes.Add("AdminBearer");
                policy.RequireAuthenticatedUser();
            });

            // MFA verified admin (for sensitive operations)
            options.AddPolicy("AdminMfaVerified", policy =>
            {
                policy.AuthenticationSchemes.Add("AdminBearer");
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("mfa_verified", "true");
            });
        });

        return services;
    }

    /// <summary>
    /// Adds admin-specific rate limiting rules.
    /// </summary>
    public static void AddAdminRateLimitRules(AspNetCoreRateLimit.IpRateLimitOptions options)
    {
        options.GeneralRules.Add(new AspNetCoreRateLimit.RateLimitRule
        {
            Endpoint = "POST:/api/v1/admin/auth/login",
            Period = "1m",
            Limit = 5
        });

        options.GeneralRules.Add(new AspNetCoreRateLimit.RateLimitRule
        {
            Endpoint = "POST:/api/v1/admin/auth/mfa/verify",
            Period = "5m",
            Limit = 5
        });

        options.GeneralRules.Add(new AspNetCoreRateLimit.RateLimitRule
        {
            Endpoint = "POST:/api/v1/admin/auth/refresh",
            Period = "1m",
            Limit = 10
        });
    }
}
