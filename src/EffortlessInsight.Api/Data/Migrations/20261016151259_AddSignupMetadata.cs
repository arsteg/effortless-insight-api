using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSignupMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrowserName",
                table: "UserSessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrowserVersion",
                table: "UserSessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ColorDepth",
                table: "UserSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTouchDevice",
                table: "UserSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "UserSessions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsName",
                table: "UserSessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsVersion",
                table: "UserSessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScreenHeight",
                table: "UserSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScreenWidth",
                table: "UserSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "UserSessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmCampaign",
                table: "SignupAttempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmContent",
                table: "SignupAttempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedium",
                table: "SignupAttempts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "SignupAttempts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmTerm",
                table: "SignupAttempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "AspNetUsers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignupCampaign",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignupMedium",
                table: "AspNetUsers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignupSource",
                table: "AspNetUsers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "AspNetUsers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrowserName",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "BrowserVersion",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "ColorDepth",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "IsTouchDevice",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "OsName",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "OsVersion",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "ScreenHeight",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "ScreenWidth",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "UtmCampaign",
                table: "SignupAttempts");

            migrationBuilder.DropColumn(
                name: "UtmContent",
                table: "SignupAttempts");

            migrationBuilder.DropColumn(
                name: "UtmMedium",
                table: "SignupAttempts");

            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "SignupAttempts");

            migrationBuilder.DropColumn(
                name: "UtmTerm",
                table: "SignupAttempts");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignupCampaign",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignupMedium",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SignupSource",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "AspNetUsers");
        }
    }
}
