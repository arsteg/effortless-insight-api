using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Hangfire;
using Hangfire.MemoryStorage;

namespace EffortlessInsight.Api.Tests.Integration;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove ALL existing DbContext and EF Core registrations to avoid provider conflicts
            // This includes Npgsql, pooling, and any related services
            var descriptorsToRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(ApplicationDbContext) ||
                d.ServiceType.FullName?.Contains("Npgsql") == true ||
                d.ServiceType.FullName?.Contains("EntityFrameworkCore.PostgreSQL") == true ||
                d.ServiceType.FullName?.Contains("IDbContextPool") == true ||
                d.ServiceType.FullName?.Contains("IDbContextFactory") == true ||
                (d.ImplementationType?.FullName?.Contains("Npgsql") == true) ||
                (d.ImplementationType?.FullName?.Contains("PostgreSQL") == true)
            ).ToList();

            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            // Also explicitly remove using RemoveAll for better coverage
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            // Remove Redis connection
            var redisDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConnectionMultiplexer));
            if (redisDescriptor != null)
                services.Remove(redisDescriptor);

            // Remove Hangfire services
            var hangfireDescriptors = services.Where(
                d => d.ServiceType.FullName?.Contains("Hangfire") == true).ToList();
            foreach (var descriptor in hangfireDescriptors)
                services.Remove(descriptor);

            // Add in-memory database using TestableApplicationDbContext which has JSON converters
            // Use a unique database name for test isolation
            var dbName = $"IntegrationTestDb_{Guid.NewGuid()}";
            services.AddScoped<ApplicationDbContext>(sp =>
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(dbName)
                    .Options;
                return new TestableApplicationDbContext(options);
            });

            // Replace distributed cache with in-memory implementation
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(sp =>
            {
                var options = Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions());
                return new MemoryDistributedCache(options);
            });

            // Add fake Redis connection for services that need it
            services.AddSingleton<IConnectionMultiplexer>(sp =>
                throw new InvalidOperationException("Redis is not available in tests"));

            // Replace email service with mock
            services.RemoveAll<IEmailService>();
            services.AddScoped<IEmailService, MockEmailService>();

            // Add in-memory Hangfire
            services.AddHangfire(config => config.UseMemoryStorage());
        });
    }
}

/// <summary>
/// Mock email service for testing - does nothing but track sent emails
/// </summary>
public class MockEmailService : IEmailService
{
    public List<(string To, string Subject, string Body)> SentEmails { get; } = new();
    public List<(string To, string TemplateId, Dictionary<string, object> Data)> SentTemplateEmails { get; } = new();

    public Task SendAsync(string to, string subject, string htmlBody)
    {
        SentEmails.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }

    public Task SendTemplateAsync(string to, string templateId, Dictionary<string, object> data)
    {
        SentTemplateEmails.Add((to, templateId, data));
        return Task.CompletedTask;
    }

    public Task SendBulkAsync(List<string> recipients, string subject, string htmlBody)
    {
        foreach (var recipient in recipients)
        {
            SentEmails.Add((recipient, subject, htmlBody));
        }
        return Task.CompletedTask;
    }
}
