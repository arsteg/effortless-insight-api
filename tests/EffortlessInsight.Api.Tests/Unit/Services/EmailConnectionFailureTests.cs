using EffortlessInsight.Api.Services.EmailSync;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class EmailConnectionFailureTests
{
    [Theory]
    [InlineData("42P01", "database_schema_outdated")]
    [InlineData("42703", "database_schema_outdated")]
    [InlineData("28P01", "database_auth_failed")]
    [InlineData("23503", "connection_context_invalid")]
    [InlineData("23505", "connection_conflict")]
    public void NestedDatabaseExceptionsHaveSafeActionableCodes(string state, string expected)
    {
        var exception = new DbUpdateException("private SQL values",
            new PostgresException("private database details", "ERROR", "ERROR", state));
        Assert.Equal(expected, EmailConnectionFailure.Code(exception, "account_save"));
    }

    [Theory]
    [InlineData("connection_lock", "connection_lock_failed")]
    [InlineData("token_encryption", "token_encryption_failed")]
    [InlineData("account_save", "connection_save_failed")]
    [InlineData("account_lookup", "connection_lookup_failed")]
    [InlineData("audit", "connection_audit_failed")]
    [InlineData("profile", "profile_failed")]
    public void UnknownExceptionsUseStageWithoutExposingMessage(string stage, string expected)
    {
        Assert.Equal(expected, EmailConnectionFailure.Code(new Exception("secret token"), stage));
    }
}
