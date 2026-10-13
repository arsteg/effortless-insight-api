using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EffortlessInsight.Api.Tests.Integration;

public class NoticeIdentityPostgresTests
{
    [NoticePostgresFact]
    public async Task MigrationRejectsConcurrentIdentityContentAndPortalDuplicates()
    {
        var admin = Environment.GetEnvironmentVariable("EI_NOTICE_TEST_POSTGRES")!;
        var schema = "notice_test_" + Guid.NewGuid().ToString("N");
        await using var setup = new NpgsqlConnection(admin);
        await setup.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", setup).ExecuteNonQueryAsync();
        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(admin) { SearchPath = schema, Pooling = false }.ConnectionString;
            await using var first = new NpgsqlConnection(scoped);
            await using var second = new NpgsqlConnection(scoped);
            await first.OpenAsync(); await second.OpenAsync();
            await new NpgsqlCommand("""
                CREATE TABLE "Notices" (
                    "Id" uuid PRIMARY KEY, "OrganizationId" uuid NOT NULL, "Gstin" text,
                    "NoticeNumber" text, "GstnNoticeId" text, "GstnReferenceNumber" text,
                    "Source" text, "FileHash" text, "DeletedAt" timestamptz)
                """, first).ExecuteNonQueryAsync();
            // Execute the actual EF migration, not a copy of its constraints.
            await using var db = new DbContext(new DbContextOptionsBuilder<DbContext>().UseNpgsql(scoped).Options);
            var commands = db.GetService<IMigrationsSqlGenerator>().Generate(new UniqueNoticeIdentity().UpOperations);
            foreach (var command in commands)
                await new NpgsqlCommand(command.CommandText, first).ExecuteNonQueryAsync();

            foreach (var kind in new[] { "identity", "content", "portal" })
            {
                var org = Guid.NewGuid();
                await using var transaction = await first.BeginTransactionAsync();
                await Insert(first, transaction, org, kind, false).ExecuteNonQueryAsync();
                // Request 2 overlaps an uncommitted request 1, so its unique check must wait.
                var pending = Insert(second, null, org, kind, true).ExecuteNonQueryAsync();
                await Task.Delay(100);
                Assert.False(pending.IsCompleted);
                await transaction.CommitAsync();
                var error = await Assert.ThrowsAsync<PostgresException>(async () => await pending);
                Assert.Equal("23505", error.SqlState);
                Assert.Equal(kind switch { "identity" => "UX_Notices_Identity", "content" => "UX_Notices_Content", _ => "UX_Notices_Portal" }, error.ConstraintName);
                await using var count = new NpgsqlCommand("SELECT count(*) FROM \"Notices\" WHERE \"OrganizationId\" = @org", first);
                count.Parameters.AddWithValue("org", org);
                Assert.Equal(1L, await count.ExecuteScalarAsync());
            }

            // Incomplete identity and identical numbers in other GSTINs/organizations remain valid.
            var sharedOrg = Guid.NewGuid();
            await Insert(first, null, sharedOrg, "identity", false).ExecuteNonQueryAsync();
            var differentGstin = Insert(first, null, sharedOrg, "identity", false);
            differentGstin.Parameters["gst"].Value = "29AABCU9603R1ZN";
            await differentGstin.ExecuteNonQueryAsync();
            await Insert(first, null, Guid.NewGuid(), "identity", false).ExecuteNonQueryAsync();
            await new NpgsqlCommand("UPDATE \"Notices\" SET \"DeletedAt\" = now()", first).ExecuteNonQueryAsync();
            await Insert(first, null, sharedOrg, "identity", false).ExecuteNonQueryAsync();
        }
        finally
        {
            // schema is generated locally above and contains test data only.
            await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", setup).ExecuteNonQueryAsync();
        }
    }

    private static NpgsqlCommand Insert(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid org, string kind, bool variant)
    {
        var command = new NpgsqlCommand("""
            INSERT INTO "Notices" ("Id", "OrganizationId", "Gstin", "NoticeNumber", "GstnNoticeId", "FileHash")
            VALUES (@id, @org, @gst, @number, @portal, @hash)
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("org", org);
        command.Parameters.AddWithValue("gst", kind == "content" ? DBNull.Value : (object)(variant ? " 27aabcu9603r1zn " : "27AABCU9603R1ZN"));
        command.Parameters.AddWithValue("number", kind == "identity" ? (object)(variant ? " ref-123 " : "REF-123") : DBNull.Value);
        command.Parameters.AddWithValue("portal", kind == "portal" ? (object)(variant ? " portal-123 " : "PORTAL-123") : DBNull.Value);
        command.Parameters.AddWithValue("hash", kind == "content" ? (object)"same-content" : DBNull.Value);
        return command;
    }
}

public sealed class NoticePostgresFactAttribute : FactAttribute
{
    public NoticePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EI_NOTICE_TEST_POSTGRES")))
            Skip = "Set EI_NOTICE_TEST_POSTGRES to a disposable PostgreSQL test server.";
    }
}
