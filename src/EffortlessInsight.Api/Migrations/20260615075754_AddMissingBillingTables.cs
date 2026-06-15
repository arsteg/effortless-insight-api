using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <summary>
    /// This migration adds billing-related tables and syncs the model snapshot.
    /// Uses conditional SQL to only create tables that don't exist.
    /// Also configures PlanLimits as owned JSON type (no schema change needed).
    /// </summary>
    public partial class AddMissingBillingTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create SubscriptionPlans table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'SubscriptionPlans') THEN
                        CREATE TABLE ""SubscriptionPlans"" (
                            ""Id"" uuid NOT NULL,
                            ""Code"" character varying(50) NOT NULL,
                            ""Name"" character varying(100) NOT NULL,
                            ""DisplayName"" character varying(100) NOT NULL,
                            ""Description"" character varying(500),
                            ""PricingMonthly"" integer,
                            ""PricingAnnually"" integer,
                            ""PerSeatMonthly"" integer,
                            ""PerSeatAnnually"" integer,
                            ""Currency"" character varying(3) NOT NULL DEFAULT 'INR',
                            ""Features"" jsonb NOT NULL DEFAULT '[]'::jsonb,
                            ""IsActive"" boolean NOT NULL DEFAULT true,
                            ""IsPopular"" boolean NOT NULL DEFAULT false,
                            ""TrialDays"" integer NOT NULL DEFAULT 0,
                            ""SortOrder"" integer NOT NULL DEFAULT 0,
                            ""ContactSales"" boolean NOT NULL DEFAULT false,
                            ""StartingAt"" integer,
                            ""RazorpayPlanIdMonthly"" character varying(50),
                            ""RazorpayPlanIdAnnually"" character varying(50),
                            ""Metadata"" jsonb,
                            ""Limits"" jsonb NOT NULL DEFAULT '{}'::jsonb,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_SubscriptionPlans"" PRIMARY KEY (""Id"")
                        );
                        CREATE UNIQUE INDEX ""IX_SubscriptionPlans_Code_Unique"" ON ""SubscriptionPlans"" (""Code"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_SubscriptionPlans_IsActive"" ON ""SubscriptionPlans"" (""IsActive"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_SubscriptionPlans_SortOrder"" ON ""SubscriptionPlans"" (""SortOrder"") WHERE ""DeletedAt"" IS NULL AND ""IsActive"" = true;
                    END IF;
                END $$;
            ");

            // Create BillingDetails table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'BillingDetails') THEN
                        CREATE TABLE ""BillingDetails"" (
                            ""Id"" uuid NOT NULL,
                            ""OrganizationId"" uuid NOT NULL,
                            ""OrganizationName"" character varying(200) NOT NULL,
                            ""Gstin"" character varying(15),
                            ""Address"" character varying(500) NOT NULL,
                            ""AddressLine2"" character varying(200),
                            ""City"" character varying(100),
                            ""State"" character varying(50) NOT NULL,
                            ""StateCode"" character varying(2),
                            ""Pincode"" character varying(10) NOT NULL,
                            ""Country"" character varying(50) NOT NULL DEFAULT 'India',
                            ""Email"" character varying(255),
                            ""Phone"" character varying(20),
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_BillingDetails"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_BillingDetails_Organizations_OrganizationId"" FOREIGN KEY (""OrganizationId"") REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE UNIQUE INDEX ""IX_BillingDetails_Org_Unique"" ON ""BillingDetails"" (""OrganizationId"") WHERE ""DeletedAt"" IS NULL;
                    END IF;
                END $$;
            ");

            // Create BillingSubscriptions table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'BillingSubscriptions') THEN
                        CREATE TABLE ""BillingSubscriptions"" (
                            ""Id"" uuid NOT NULL,
                            ""OrganizationId"" uuid NOT NULL,
                            ""PlanCode"" character varying(50) NOT NULL,
                            ""PlanId"" uuid NOT NULL,
                            ""Status"" character varying(20) NOT NULL,
                            ""BillingCycle"" character varying(10) NOT NULL,
                            ""SeatsIncluded"" integer NOT NULL DEFAULT 1,
                            ""SeatsAdditional"" integer NOT NULL DEFAULT 0,
                            ""CurrentPeriodStart"" timestamp with time zone NOT NULL,
                            ""CurrentPeriodEnd"" timestamp with time zone NOT NULL,
                            ""TrialStart"" timestamp with time zone,
                            ""TrialEnd"" timestamp with time zone,
                            ""CancelAtPeriodEnd"" boolean NOT NULL DEFAULT false,
                            ""CancelledAt"" timestamp with time zone,
                            ""CancellationReason"" character varying(50),
                            ""CancellationFeedback"" text,
                            ""EndedAt"" timestamp with time zone,
                            ""ScheduledPlanCode"" character varying(50),
                            ""ScheduledBillingCycle"" character varying(10),
                            ""ScheduledChangeDate"" timestamp with time zone,
                            ""RazorpaySubscriptionId"" character varying(50),
                            ""RazorpayCustomerId"" character varying(50),
                            ""FailedPaymentAttempts"" integer NOT NULL DEFAULT 0,
                            ""PaymentRetryCount"" integer NOT NULL DEFAULT 0,
                            ""NextPaymentRetryAt"" timestamp with time zone,
                            ""LastPaymentFailedAt"" timestamp with time zone,
                            ""GracePeriodEndAt"" timestamp with time zone,
                            ""BaseAmount"" numeric NOT NULL DEFAULT 0,
                            ""AdditionalSeatsAmount"" numeric NOT NULL DEFAULT 0,
                            ""TaxAmount"" numeric NOT NULL DEFAULT 0,
                            ""TotalAmount"" numeric NOT NULL DEFAULT 0,
                            ""Currency"" character varying(3) NOT NULL DEFAULT 'INR',
                            ""Metadata"" jsonb,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_BillingSubscriptions"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_BillingSubscriptions_Organizations_OrganizationId"" FOREIGN KEY (""OrganizationId"") REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
                            CONSTRAINT ""FK_BillingSubscriptions_SubscriptionPlans_PlanId"" FOREIGN KEY (""PlanId"") REFERENCES ""SubscriptionPlans"" (""Id"") ON DELETE RESTRICT
                        );
                        CREATE UNIQUE INDEX ""IX_BillingSubscriptions_Org_Unique"" ON ""BillingSubscriptions"" (""OrganizationId"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_BillingSubscriptions_Status"" ON ""BillingSubscriptions"" (""Status"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_BillingSubscriptions_CurrentPeriodEnd"" ON ""BillingSubscriptions"" (""CurrentPeriodEnd"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_BillingSubscriptions_PlanId"" ON ""BillingSubscriptions"" (""PlanId"");
                        CREATE INDEX ""IX_BillingSubscriptions_RazorpaySubscriptionId"" ON ""BillingSubscriptions"" (""RazorpaySubscriptionId"") WHERE ""RazorpaySubscriptionId"" IS NOT NULL;
                    END IF;
                END $$;
            ");

            // Seed subscription plans
            migrationBuilder.Sql(@"
                INSERT INTO ""SubscriptionPlans"" (
                    ""Id"", ""Code"", ""Name"", ""DisplayName"", ""Description"",
                    ""PricingMonthly"", ""PricingAnnually"", ""PerSeatMonthly"", ""PerSeatAnnually"",
                    ""Currency"", ""Features"", ""IsActive"", ""IsPopular"", ""TrialDays"",
                    ""SortOrder"", ""ContactSales"", ""StartingAt"", ""Limits"", ""CreatedAt""
                ) VALUES
                (
                    '00000001-0001-0001-0001-000000000001', 'free', 'Free', 'Free Forever',
                    'For individuals getting started', 0, 0, NULL, NULL, 'INR', '[]'::jsonb,
                    true, false, 0, 1, false, NULL,
                    '{""NoticesPerMonth"":5,""Users"":1,""StorageGb"":1,""OrganizationsCount"":1,""AdditionalUsersAllowed"":false,""ApiCalls"":100}'::jsonb,
                    NOW()
                ),
                (
                    '00000001-0001-0001-0001-000000000002', 'starter', 'Starter', 'Starter',
                    'For small businesses', 99900, 999000, NULL, NULL, 'INR', '[]'::jsonb,
                    true, false, 14, 2, false, NULL,
                    '{""NoticesPerMonth"":50,""Users"":3,""StorageGb"":10,""OrganizationsCount"":1,""AdditionalUsersAllowed"":false,""ApiCalls"":1000}'::jsonb,
                    NOW()
                ),
                (
                    '00000001-0001-0001-0001-000000000003', 'professional', 'Professional', 'Professional',
                    'For CA firms and growing teams', 299900, 2999000, 49900, 499000, 'INR', '[]'::jsonb,
                    true, true, 14, 3, false, NULL,
                    '{""NoticesPerMonth"":500,""Users"":10,""StorageGb"":100,""OrganizationsCount"":3,""AdditionalUsersAllowed"":true,""ApiCalls"":10000}'::jsonb,
                    NOW()
                ),
                (
                    '00000001-0001-0001-0001-000000000004', 'enterprise', 'Enterprise', 'Enterprise',
                    'For large organizations', NULL, NULL, NULL, NULL, 'INR', '[]'::jsonb,
                    true, false, 30, 4, true, 1500000,
                    '{""NoticesPerMonth"":-1,""Users"":-1,""StorageGb"":-1,""OrganizationsCount"":-1,""AdditionalUsersAllowed"":true,""ApiCalls"":-1}'::jsonb,
                    NOW()
                )
                ON CONFLICT (""Id"") DO NOTHING;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove seeded subscription plans
            migrationBuilder.Sql(@"
                DELETE FROM ""SubscriptionPlans""
                WHERE ""Id"" IN (
                    '00000001-0001-0001-0001-000000000001',
                    '00000001-0001-0001-0001-000000000002',
                    '00000001-0001-0001-0001-000000000003',
                    '00000001-0001-0001-0001-000000000004'
                );
            ");

            // Drop tables in reverse order (respecting FK constraints)
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""BillingSubscriptions"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""BillingDetails"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""SubscriptionPlans"";");
        }
    }
}
