using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Encryption;
using EffortlessInsight.Api.Services.Organizations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EffortlessInsight.Api.Tests.Integration;

/// <summary>
/// Opt-in against a TEST PostgreSQL server with pgvector and CREATEDB privileges.
/// Always creates a uniquely named disposable database; never migrates application data.
/// </summary>
public class CaHandoverPostgresTests
{
    [PostgresFact]
    public async Task HandoverUsesRealTenantFiltersAndRollsBackAsAUnit()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var tenant = new TenantContext();
        await using var db = fixture.Context(tenant);
        var ca = new ApplicationUser { Name = "CA", Email = "ca@test.invalid", UserName = "ca@test.invalid", IsCA = true };
        var bo = new ApplicationUser { Name = "BO", Email = "bo@test.invalid", UserName = "bo@test.invalid" };
        var source = new Organization { Name = "Source", NameNormalized = "source", State = "Maharashtra" };
        var target = new Organization { Name = "Target", NameNormalized = "target", State = "Maharashtra" };
        db.Users.AddRange(ca, bo);
        db.Organizations.AddRange(source, target);
        db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = source.Id, UserId = ca.Id, Role = "owner", Status = "active" },
            new OrganizationMember { OrganizationId = target.Id, UserId = bo.Id, Role = "owner", Status = "active" });
        var gstin = new OrganizationGstin { OrganizationId = target.Id, Gstin = "27AABCU9603R1ZN", StateCode = "27", StateName = "Maharashtra" };
        var invitation = new CaClientInvitation { CaOrganizationId = source.Id, CaUserId = ca.Id, Gstin = gstin.Gstin,
            Email = bo.Email!, EmailNormalized = bo.Email!.ToUpperInvariant(), TokenHash = Guid.NewGuid().ToString("N") };
        var prospect = new CaProspectClient { CaUserId = ca.Id, Gstin = gstin.Gstin,
            GstinHash = CaWorkspaceWrites.Hash(gstin.Gstin), CaClientInvitationId = invitation.Id };
        var notice = new Notice { OrganizationId = source.Id, UploadedById = ca.Id, Gstin = gstin.Gstin,
            FileName = "notice.pdf", FileUrl = "original", ProcessingStatus = NoticeProcessingStatus.Completed };
        db.OrganizationGstins.Add(gstin);
        db.CaClientInvitations.Add(invitation);
        db.CaProspectClients.Add(prospect);
        db.Notices.Add(notice);
        await db.SaveChangesAsync();
        tenant.SetOrganizationId(target.Id);
        (await db.Notices.CountAsync()).Should().Be(0);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await CaWorkspaceWrites.LockAsync(db, source.Id, gstin.Gstin);
            await new CaDistributorHandover(db).TransferAsync(invitation, prospect, gstin);
            await db.SaveChangesAsync();
            (await db.Notices.CountAsync()).Should().Be(1);
            await transaction.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        (await db.Notices.CountAsync()).Should().Be(0);
        (await db.Notices.IgnoreQueryFilters().SingleAsync()).OrganizationId.Should().Be(source.Id);
        (await db.CaProspectClients.SingleAsync()).Status.Should().Be("staging");
    }

    [PostgresFact]
    public async Task WorkspaceLockSerializesHandoverAgainstNoticeWrites()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        await using var first = fixture.Context(new TenantContext());
        await using var second = fixture.Context(new TenantContext());
        var organization = Guid.NewGuid();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await CaWorkspaceWrites.LockAsync(first, organization, "27AABCU9603R1ZN");
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        await second.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '250ms'");
        var blocked = () => CaWorkspaceWrites.LockAsync(second, organization, "27AABCU9603R1ZN");
        (await blocked.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("55P03");
        await secondTransaction.RollbackAsync();
        await firstTransaction.CommitAsync();
        await using var retry = await second.Database.BeginTransactionAsync();
        await CaWorkspaceWrites.LockAsync(second, organization, "27AABCU9603R1ZN");
        await retry.CommitAsync();
    }

    private sealed class DatabaseFixture(string adminConnection, string database, string testConnection) : IAsyncDisposable
    {
        public ApplicationDbContext Context(ITenantContext tenant) => new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(testConnection, o => o.UseVector()).Options, tenant);

        public static async Task<DatabaseFixture> CreateAsync()
        {
            if (!FieldEncryptionServiceAccessor.IsConfigured)
                FieldEncryptionServiceAccessor.SetInstance(new FieldEncryptionService(new ConfigurationBuilder().Build(), NullLogger<FieldEncryptionService>.Instance));
            var admin = Environment.GetEnvironmentVariable("EI_CA_TEST_POSTGRES")!;
            var database = "ei_ca_test_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await command.ExecuteNonQueryAsync();
            var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database, Pooling = false }.ConnectionString;
            var fixture = new DatabaseFixture(admin, database, connectionString);
            try
            {
                await using var db = fixture.Context(new TenantContext());
                await db.Database.EnsureCreatedAsync();
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            if (!database.StartsWith("ei_ca_test_", StringComparison.Ordinal) || database.Length != 43)
                throw new InvalidOperationException("Refusing to remove a non-test database");
            await using var connection = new NpgsqlConnection(adminConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EI_CA_TEST_POSTGRES")))
            Skip = "Set EI_CA_TEST_POSTGRES to an isolated PostgreSQL test server with pgvector and CREATEDB privileges.";
    }
}
