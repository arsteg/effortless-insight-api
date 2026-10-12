using Npgsql;

namespace EffortlessInsight.Api.Services.EmailSync;

// Return only application-owned codes. Exception messages can contain credentials or SQL values.
public static class EmailConnectionFailure
{
    public static string Code(Exception exception, string stage)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException pg)
            {
                var code = pg.SqlState switch
                {
                    "42P01" or "42703" or "42804" => "database_schema_outdated",
                    "28P01" or "28000" => "database_auth_failed",
                    "23503" => "connection_context_invalid",
                    "23505" => "connection_conflict",
                    _ => null
                };
                if (code != null) return code;
            }
        }
        return stage switch
        {
            "connection_lock" => "connection_lock_failed",
            "account_lookup" or "account_reload" => "connection_lookup_failed",
            "account_save" => "connection_save_failed",
            "token_encryption" => "token_encryption_failed",
            "audit" => "connection_audit_failed",
            "profile" => "profile_failed",
            "token_exchange" => "token_exchange_failed",
            _ => "complete_failed"
        };
    }
}
