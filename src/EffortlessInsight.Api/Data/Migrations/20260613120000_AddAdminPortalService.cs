using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminPortalService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AdminUsers table
            migrationBuilder.CreateTable(
                name: "AdminUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Permissions = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MfaEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MfaSecretEncrypted = table.Column<byte[]>(type: "bytea", nullable: true),
                    BackupCodesHash = table.Column<string>(type: "jsonb", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastFailedLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IpWhitelist = table.Column<string>(type: "jsonb", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    LastLoginUserAgent = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUsers", x => x.Id);
                });

            // AdminSessions table
            migrationBuilder.CreateTable(
                name: "AdminSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefreshToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeviceFingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    InvalidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InvalidationReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminSessions_AdminUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // AdminAuditLogs table
            migrationBuilder.CreateTable(
                name: "AdminAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "jsonb", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    RequestId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAuditLogs_AdminUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ImpersonationSessions table
            migrationBuilder.CreateTable(
                name: "ImpersonationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Permissions = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PagesVisited = table.Column<string>(type: "jsonb", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpersonationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImpersonationSessions_AdminUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImpersonationSessions_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImpersonationSessions_Organizations_TargetOrganizationId",
                        column: x => x.TargetOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // OrganizationCredits table
            migrationBuilder.CreateTable(
                name: "OrganizationCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "INR"),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GrantedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationCredits_AdminUsers_GrantedById",
                        column: x => x.GrantedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrganizationCredits_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // CreditUsageRecords table
            migrationBuilder.CreateTable(
                name: "CreditUsageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationCreditId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditUsageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditUsageRecords_OrganizationCredits_OrganizationCreditId",
                        column: x => x.OrganizationCreditId,
                        principalTable: "OrganizationCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // SystemAlerts table
            migrationBuilder.CreateTable(
                name: "SystemAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: true),
                    AcknowledgedById = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    NotifiedEmails = table.Column<string>(type: "jsonb", nullable: true),
                    AutoResolve = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AutoResolveAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemAlerts_AdminUsers_AcknowledgedById",
                        column: x => x.AcknowledgedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SystemAlerts_AdminUsers_ResolvedById",
                        column: x => x.ResolvedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ContentPages table
            migrationBuilder.CreateTable(
                name: "ContentPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tags = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MetaTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MetaDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FeaturedImage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ViewCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    HelpfulCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NotHelpfulCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedById = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentPages_AdminUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentPages_AdminUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ContentPages_AdminUsers_PublishedById",
                        column: x => x.PublishedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ContentPageVersions table
            migrationBuilder.CreateTable(
                name: "ContentPageVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ChangeNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPageVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentPageVersions_ContentPages_ContentPageId",
                        column: x => x.ContentPageId,
                        principalTable: "ContentPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentPageVersions_AdminUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // PromptVersions table
            migrationBuilder.CreateTable(
                name: "PromptVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PromptTemplate = table.Column<string>(type: "text", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: true),
                    Variables = table.Column<string>(type: "jsonb", nullable: false),
                    ModelConfig = table.Column<string>(type: "jsonb", nullable: false),
                    OutputSchema = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TestResults = table.Column<string>(type: "jsonb", nullable: true),
                    AvgLatencyMs = table.Column<double>(type: "double precision", nullable: true),
                    AvgTokens = table.Column<int>(type: "integer", nullable: true),
                    ErrorRate = table.Column<double>(type: "double precision", nullable: true),
                    UsageCount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptVersions_AdminUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromptVersions_AdminUsers_ActivatedById",
                        column: x => x.ActivatedById,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Create indexes for AdminUsers
            migrationBuilder.CreateIndex(
                name: "IX_AdminUsers_Email",
                table: "AdminUsers",
                column: "EmailNormalized",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUsers_Role",
                table: "AdminUsers",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUsers_IsActive",
                table: "AdminUsers",
                column: "IsActive",
                filter: "\"IsActive\" = true AND \"DeletedAt\" IS NULL");

            // Create indexes for AdminSessions
            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_AdminUserId",
                table: "AdminSessions",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_RefreshToken_Active",
                table: "AdminSessions",
                column: "RefreshToken",
                unique: true,
                filter: "\"IsActive\" = true AND \"RefreshToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_IsActive",
                table: "AdminSessions",
                column: "IsActive",
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_ExpiresAt",
                table: "AdminSessions",
                column: "ExpiresAt",
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_AdminSessions_Admin_Active_Expires",
                table: "AdminSessions",
                columns: new[] { "AdminUserId", "IsActive", "ExpiresAt" });

            // Create indexes for AdminAuditLogs
            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_AdminUserId",
                table: "AdminAuditLogs",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Action",
                table: "AdminAuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Target",
                table: "AdminAuditLogs",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_CreatedAt",
                table: "AdminAuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Admin_Created",
                table: "AdminAuditLogs",
                columns: new[] { "AdminUserId", "CreatedAt" });

            // Create indexes for ImpersonationSessions
            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_TokenHash_Active",
                table: "ImpersonationSessions",
                column: "TokenHash",
                unique: true,
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_AdminUserId",
                table: "ImpersonationSessions",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_TargetUserId",
                table: "ImpersonationSessions",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_Status",
                table: "ImpersonationSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_ExpiresAt",
                table: "ImpersonationSessions",
                column: "ExpiresAt",
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_TargetOrganizationId",
                table: "ImpersonationSessions",
                column: "TargetOrganizationId");

            // Create indexes for OrganizationCredits
            migrationBuilder.CreateIndex(
                name: "IX_OrganizationCredits_OrganizationId",
                table: "OrganizationCredits",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationCredits_Status",
                table: "OrganizationCredits",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationCredits_ExpiresAt",
                table: "OrganizationCredits",
                column: "ExpiresAt",
                filter: "\"Status\" = 'active' AND \"ExpiresAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCredits_Org_Active",
                table: "OrganizationCredits",
                columns: new[] { "OrganizationId", "Status" },
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationCredits_GrantedById",
                table: "OrganizationCredits",
                column: "GrantedById");

            // Create indexes for CreditUsageRecords
            migrationBuilder.CreateIndex(
                name: "IX_CreditUsageRecords_OrganizationCreditId",
                table: "CreditUsageRecords",
                column: "OrganizationCreditId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditUsageRecords_InvoiceId",
                table: "CreditUsageRecords",
                column: "InvoiceId");

            // Create indexes for SystemAlerts
            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Status",
                table: "SystemAlerts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_AlertType",
                table: "SystemAlerts",
                column: "AlertType");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Category",
                table: "SystemAlerts",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Priority",
                table: "SystemAlerts",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_CreatedAt",
                table: "SystemAlerts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Active_Priority",
                table: "SystemAlerts",
                columns: new[] { "Status", "Priority" },
                filter: "\"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_Source_Status",
                table: "SystemAlerts",
                columns: new[] { "Source", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_AcknowledgedById",
                table: "SystemAlerts",
                column: "AcknowledgedById");

            migrationBuilder.CreateIndex(
                name: "IX_SystemAlerts_ResolvedById",
                table: "SystemAlerts",
                column: "ResolvedById");

            // Create indexes for ContentPages
            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_Slug_Unique",
                table: "ContentPages",
                column: "Slug",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_ContentType",
                table: "ContentPages",
                column: "ContentType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_Status",
                table: "ContentPages",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_Category",
                table: "ContentPages",
                column: "Category",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_Published_Order",
                table: "ContentPages",
                columns: new[] { "ContentType", "Status", "DisplayOrder" },
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'published'");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_CreatedById",
                table: "ContentPages",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_UpdatedById",
                table: "ContentPages",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPages_PublishedById",
                table: "ContentPages",
                column: "PublishedById");

            // Create indexes for ContentPageVersions
            migrationBuilder.CreateIndex(
                name: "IX_ContentPageVersions_ContentPageId",
                table: "ContentPageVersions",
                column: "ContentPageId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPageVersions_Page_Version",
                table: "ContentPageVersions",
                columns: new[] { "ContentPageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPageVersions_CreatedById",
                table: "ContentPageVersions",
                column: "CreatedById");

            // Create indexes for PromptVersions
            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_PromptKey",
                table: "PromptVersions",
                column: "PromptKey");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_Status",
                table: "PromptVersions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_Key_Active_Unique",
                table: "PromptVersions",
                columns: new[] { "PromptKey", "IsActive" },
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_Key_Version",
                table: "PromptVersions",
                columns: new[] { "PromptKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_CreatedById",
                table: "PromptVersions",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PromptVersions_ActivatedById",
                table: "PromptVersions",
                column: "ActivatedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PromptVersions");
            migrationBuilder.DropTable(name: "ContentPageVersions");
            migrationBuilder.DropTable(name: "ContentPages");
            migrationBuilder.DropTable(name: "SystemAlerts");
            migrationBuilder.DropTable(name: "CreditUsageRecords");
            migrationBuilder.DropTable(name: "OrganizationCredits");
            migrationBuilder.DropTable(name: "ImpersonationSessions");
            migrationBuilder.DropTable(name: "AdminAuditLogs");
            migrationBuilder.DropTable(name: "AdminSessions");
            migrationBuilder.DropTable(name: "AdminUsers");
        }
    }
}
