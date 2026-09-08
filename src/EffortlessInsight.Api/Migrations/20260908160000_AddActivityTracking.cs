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
    /// re-creates the tables.
    /// </remarks>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260908160000_AddActivityTracking")]
    public partial class AddActivityTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Visitors" (
                    "Id" uuid NOT NULL,
                    "VisitorId" character varying(64) NOT NULL,
                    "UserId" uuid NULL,
                    "LinkedAt" timestamp with time zone NULL,
                    "FirstSeenAt" timestamp with time zone NOT NULL,
                    "LastSeenAt" timestamp with time zone NOT NULL,
                    "FirstReferrer" character varying(500) NULL,
                    "LandingPage" character varying(300) NULL,
                    "UserAgent" character varying(300) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "DeletedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_Visitors" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Visitors_VisitorId" ON "Visitors" ("VisitorId");
                """);
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Visitors_UserId" ON "Visitors" ("UserId");
                """);
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Visitors_LastSeenAt" ON "Visitors" ("LastSeenAt");
                """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "ActivityEvents" (
                    "Id" uuid NOT NULL,
                    "VisitorId" character varying(64) NOT NULL,
                    "UserId" uuid NULL,
                    "SessionId" character varying(64) NOT NULL,
                    "EventType" character varying(50) NOT NULL,
                    "EventName" character varying(150) NULL,
                    "Page" character varying(300) NULL,
                    "Referrer" character varying(500) NULL,
                    "EntityType" character varying(50) NULL,
                    "EntityId" uuid NULL,
                    "MetadataJson" character varying(2000) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "DeletedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_ActivityEvents" PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ActivityEvents_CreatedAt" ON "ActivityEvents" ("CreatedAt");
                """);
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ActivityEvents_VisitorId_CreatedAt" ON "ActivityEvents" ("VisitorId", "CreatedAt");
                """);
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ActivityEvents_UserId_CreatedAt" ON "ActivityEvents" ("UserId", "CreatedAt");
                """);
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_ActivityEvents_EventType_CreatedAt" ON "ActivityEvents" ("EventType", "CreatedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "ActivityEvents";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "Visitors";""");
        }
    }
}
