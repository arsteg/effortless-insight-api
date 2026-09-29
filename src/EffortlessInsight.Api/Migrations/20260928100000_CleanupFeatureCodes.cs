using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <summary>
    /// Cleans up feature codes in SubscriptionPlans:
    /// 1. Maps legacy codes to their new equivalents
    /// 2. Removes duplicate and invalid codes
    ///
    /// Legacy code mappings:
    /// - whatsapp_integration -> whatsapp_assistant
    /// - advanced_workflows -> workflows
    /// - sso_integration -> sso
    /// - advanced_reporting -> advanced_analytics
    ///
    /// Removed codes (not implemented or marketing-only):
    /// - full_ai_analysis, custom_roles, audit_trail, sla_guarantee, priority_support
    /// - dedicated_account_manager, custom_integrations, custom_branding, audit_logs
    /// - priority_processing
    /// </summary>
    public partial class CleanupFeatureCodes : Migration
    {
        private static readonly string[] ValidFeatureCodes = new[]
        {
            "notice_detection", "email_notifications", "push_notifications",
            "ai_explanation", "draft_reply", "whatsapp_assistant", "multilingual_support",
            "collaboration", "advanced_analytics",
            "workflows", "bulk_operations", "data_export",
            "ca_client_management", "sso", "api_access"
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Map legacy codes to new codes
            // Uses jsonb_set to replace values in the Features JSONB array

            // whatsapp_integration -> whatsapp_assistant
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""whatsapp_integration""' THEN '""whatsapp_assistant""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""whatsapp_integration""]'::jsonb;
            ");

            // advanced_workflows -> workflows
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""advanced_workflows""' THEN '""workflows""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""advanced_workflows""]'::jsonb;
            ");

            // sso_integration -> sso
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""sso_integration""' THEN '""sso""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""sso_integration""]'::jsonb;
            ");

            // advanced_reporting -> advanced_analytics
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""advanced_reporting""' THEN '""advanced_analytics""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""advanced_reporting""]'::jsonb;
            ");

            // Step 2: Remove invalid codes and deduplicate
            // Keep only valid feature codes
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT COALESCE(jsonb_agg(DISTINCT elem ORDER BY elem), '[]'::jsonb)
                    FROM jsonb_array_elements(""Features"") AS elem
                    WHERE elem::text IN (
                        '""notice_detection""', '""email_notifications""', '""push_notifications""',
                        '""ai_explanation""', '""draft_reply""', '""whatsapp_assistant""', '""multilingual_support""',
                        '""collaboration""', '""advanced_analytics""',
                        '""workflows""', '""bulk_operations""', '""data_export""',
                        '""ca_client_management""', '""sso""', '""api_access""'
                    )
                )
                WHERE ""Features"" IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse mappings (new codes back to legacy)
            // Note: This won't restore removed invalid codes

            // whatsapp_assistant -> whatsapp_integration
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""whatsapp_assistant""' THEN '""whatsapp_integration""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""whatsapp_assistant""]'::jsonb;
            ");

            // workflows -> advanced_workflows
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""workflows""' THEN '""advanced_workflows""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""workflows""]'::jsonb;
            ");

            // sso -> sso_integration
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""sso""' THEN '""sso_integration""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""sso""]'::jsonb;
            ");

            // advanced_analytics -> advanced_reporting
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Features"" = (
                    SELECT jsonb_agg(
                        CASE
                            WHEN elem = '""advanced_analytics""' THEN '""advanced_reporting""'
                            ELSE elem
                        END
                    )
                    FROM jsonb_array_elements(""Features"") AS elem
                )
                WHERE ""Features"" @> '[""advanced_analytics""]'::jsonb;
            ");
        }
    }
}
