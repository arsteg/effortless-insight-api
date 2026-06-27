using EffortlessInsight.Api.Services.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace EffortlessInsight.Api.Data;

/// <summary>
/// Factory for creating ApplicationDbContext during design-time operations (migrations).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Initialize field encryption service for design-time
        // This is required for EF Core value converters to work during migrations
        if (!FieldEncryptionServiceAccessor.IsConfigured)
        {
            var encryptionService = new FieldEncryptionService(
                configuration,
                NullLogger<FieldEncryptionService>.Instance);
            FieldEncryptionServiceAccessor.SetInstance(encryptionService);
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        // Build NpgsqlDataSource with dynamic JSON support for JSONB columns with List<T> types
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        optionsBuilder.UseNpgsql(dataSource, npgsqlOptions =>
        {
            npgsqlOptions.UseVector();
        })
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
