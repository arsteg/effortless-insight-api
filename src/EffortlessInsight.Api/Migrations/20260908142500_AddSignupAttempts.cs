using System;
using EffortlessInsight.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Hand-authored (dotnet ef unavailable while the API runs under VS), so
    /// the Migration/DbContext attributes live here instead of a Designer
    /// file; the model snapshot was updated in the same commit. Guarded with
    /// IF NOT EXISTS so it is safe even if a future scaffolded migration
    /// re-creates the table.
    /// </remarks>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260908142500_AddSignupAttempts")]
    public partial class AddSignupAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "SignupAttempts" (
                    "Id" uuid NOT NULL,
                    "Mobile" character varying(20) NOT NULL,
                    "MobileNormalized" character varying(10) NOT NULL,
                    "Name" character varying(100) NULL,
                    "Email" character varying(256) NULL,
                    "Source" character varying(20) NOT NULL,
                    "MobileVerifiedAt" timestamp with time zone NOT NULL,
                    "ContactedAt" timestamp with time zone NULL,
                    "ContactNotes" character varying(1000) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "DeletedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_SignupAttempts" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SignupAttempts_MobileNormalized"
                    ON "SignupAttempts" ("MobileNormalized");
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_SignupAttempts_MobileVerifiedAt"
                    ON "SignupAttempts" ("MobileVerifiedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "SignupAttempts";""");
        }
    }
}
