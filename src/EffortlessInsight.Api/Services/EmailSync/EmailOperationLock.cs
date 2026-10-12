using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace EffortlessInsight.Api.Services.EmailSync;

public interface IEmailOperationLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, CancellationToken cancellationToken);
}

// A dedicated PostgreSQL session holds the lock for the entire operation (no expiring lease).
public sealed class EmailOperationLock(NpgsqlDataSource dataSource) : IEmailOperationLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, CancellationToken cancellationToken)
    {
        // Reconstructing a connection from EF's redacted connection string loses credentials.
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var id = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("email:" + key)));
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@id)", connection);
            command.Parameters.AddWithValue("id", id);
            if (await command.ExecuteScalarAsync(cancellationToken) is true) return new Handle(connection, id);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class Handle(NpgsqlConnection connection, long id) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@id)", connection);
                command.Parameters.AddWithValue("id", id);
                await command.ExecuteNonQueryAsync();
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
