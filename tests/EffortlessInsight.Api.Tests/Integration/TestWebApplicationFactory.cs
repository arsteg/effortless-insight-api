using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Services;
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
            // Remove the existing DbContext registration
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

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

            // Add in-memory database for testing
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}");
            });

            // Replace distributed cache with in-memory implementation
            services.RemoveAll<IDistributedCache>();
            services.AddSingleton<IDistributedCache>(sp =>
            {
                var options = Options.Create(new MemoryDistributedCacheOptions());
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
