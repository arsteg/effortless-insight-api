using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexesAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Critical: Add index for payment method lookup during renewals
            // Fixes Issue #1: Missing Database Index on Critical Queries
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_PaymentMethods_OrganizationId_IsDefault_IsActive""
                ON ""PaymentMethods""(""OrganizationId"", ""IsDefault"", ""IsActive"")
                WHERE ""DeletedAt"" IS NULL;
            ");

            // Critical: Add unique constraint to prevent duplicate subscriptions per organization
            // Fixes Issue #2: Race Condition in Duplicate Subscription Creation
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BillingSubscriptions_OrganizationId_Unique""
                ON ""BillingSubscriptions""(""OrganizationId"")
                WHERE ""DeletedAt"" IS NULL;
            ");

            // Performance: Add index for subscription renewal queries
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_BillingSubscriptions_Status_CurrentPeriodEnd""
                ON ""BillingSubscriptions""(""Status"", ""CurrentPeriodEnd"")
                WHERE ""DeletedAt"" IS NULL;
            ");

            // Performance: Add index for grace period expiration queries
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_BillingSubscriptions_Status_GracePeriodEndAt""
                ON ""BillingSubscriptions""(""Status"", ""GracePeriodEndAt"")
                WHERE ""DeletedAt"" IS NULL AND ""GracePeriodEndAt"" IS NOT NULL;
            ");

            // Performance: Add index for Razorpay subscription ID lookups
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_BillingSubscriptions_RazorpaySubscriptionId""
                ON ""BillingSubscriptions""(""RazorpaySubscriptionId"")
                WHERE ""RazorpaySubscriptionId"" IS NOT NULL AND ""DeletedAt"" IS NULL;
            ");

            // Performance: Add index for webhook event idempotency checks
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_WebhookEvents_Provider_EventId""
                ON ""WebhookEvents""(""Provider"", ""EventId"");
            ");

            // Performance: Add index for payment lookups by Razorpay IDs
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Payments_RazorpayPaymentId""
                ON ""Payments""(""RazorpayPaymentId"")
                WHERE ""RazorpayPaymentId"" IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Payments_RazorpayOrderId""
                ON ""Payments""(""RazorpayOrderId"")
                WHERE ""RazorpayOrderId"" IS NOT NULL;
            ");

            // Performance: Add composite index for subscription + invoice lookups
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Payments_SubscriptionId_Status""
                ON ""Payments""(""SubscriptionId"", ""Status"")
                WHERE ""SubscriptionId"" IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Payments_SubscriptionId_Status"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Payments_RazorpayOrderId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Payments_RazorpayPaymentId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_WebhookEvents_Provider_EventId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_BillingSubscriptions_RazorpaySubscriptionId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_BillingSubscriptions_Status_GracePeriodEndAt"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_BillingSubscriptions_Status_CurrentPeriodEnd"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_BillingSubscriptions_OrganizationId_Unique"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_PaymentMethods_OrganizationId_IsDefault_IsActive"";");
        }
    }
}
