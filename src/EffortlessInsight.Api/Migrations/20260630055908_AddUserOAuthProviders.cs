using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOAuthProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationGstins_Gstin_Format",
                table: "OrganizationGstins");

            migrationBuilder.AlterColumn<Vector>(
                name: "Vector",
                table: "Embeddings",
                type: "vector(1536)",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(3072)");

            migrationBuilder.CreateTable(
                name: "AdminPasswordHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminPasswordHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminPasswordHistory_AdminUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserOAuthProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AccessTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOAuthProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserOAuthProviders_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationGstins_Gstin_Format",
                table: "OrganizationGstins",
                sql: "\"Gstin\" ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$' OR \"Gstin\" LIKE 'ENC:%'");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Number_Search",
                table: "Notices",
                column: "NoticeNumber",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPasswordHistory_AdminUserId",
                table: "AdminPasswordHistory",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOAuthProviders_Provider_ProviderId",
                table: "UserOAuthProviders",
                columns: new[] { "Provider", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOAuthProviders_UserId",
                table: "UserOAuthProviders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOAuthProviders_UserId_Provider",
                table: "UserOAuthProviders",
                columns: new[] { "UserId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminPasswordHistory");

            migrationBuilder.DropTable(
                name: "UserOAuthProviders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationGstins_Gstin_Format",
                table: "OrganizationGstins");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Number_Search",
                table: "Notices");

            migrationBuilder.AlterColumn<Vector>(
                name: "Vector",
                table: "Embeddings",
                type: "vector(3072)",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationGstins_Gstin_Format",
                table: "OrganizationGstins",
                sql: "\"Gstin\" ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$'");
        }
    }
}
