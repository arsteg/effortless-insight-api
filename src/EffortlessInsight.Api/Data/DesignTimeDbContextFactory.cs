using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
