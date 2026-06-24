using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <summary>
    /// Creates missing billing tables: Invoices, InvoiceLineItems, Payments, PaymentMethods, WebhookEvents.
    /// Uses conditional SQL to only create tables that don't exist.
    /// </summary>
    public partial class AddMissingBillingTables2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create Invoices table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'Invoices') THEN
                        CREATE TABLE ""Invoices"" (
                            ""Id"" uuid NOT NULL,
                            ""OrganizationId"" uuid NOT NULL,
                            ""SubscriptionId"" uuid,
                            ""InvoiceNumber"" character varying(50) NOT NULL,
                            ""Status"" character varying(20) NOT NULL,
                            ""InvoiceDate"" date NOT NULL,
                            ""DueDate"" date NOT NULL,
                            ""Currency"" character varying(3) NOT NULL DEFAULT 'INR',
                            ""Subtotal"" integer NOT NULL DEFAULT 0,
                            ""Discount"" integer NOT NULL DEFAULT 0,
                            ""DiscountDescription"" character varying(100),
                            ""TaxRate"" numeric NOT NULL DEFAULT 18.00,
                            ""TaxAmount"" integer NOT NULL DEFAULT 0,
                            ""CgstAmount"" integer,
                            ""SgstAmount"" integer,
                            ""IgstAmount"" integer,
                            ""Total"" integer NOT NULL DEFAULT 0,
                            ""AmountPaid"" integer NOT NULL DEFAULT 0,
                            ""AmountDue"" integer NOT NULL DEFAULT 0,
                            ""Description"" text,
                            ""HsnCode"" character varying(10) NOT NULL DEFAULT '998314',
                            ""PlaceOfSupply"" character varying(50),
                            ""PlaceOfSupplyCode"" character varying(2),
                            ""IsInterState"" boolean NOT NULL DEFAULT false,
                            ""BillingDetails"" jsonb NOT NULL DEFAULT '{}'::jsonb,
                            ""Notes"" text,
                            ""RazorpayInvoiceId"" character varying(50),
                            ""PdfUrl"" character varying(500),
                            ""PaidAt"" timestamp with time zone,
                            ""VoidedAt"" timestamp with time zone,
                            ""VoidReason"" text,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_Invoices"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_Invoices_Organizations_OrganizationId"" FOREIGN KEY (""OrganizationId"") REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
                            CONSTRAINT ""FK_Invoices_BillingSubscriptions_SubscriptionId"" FOREIGN KEY (""SubscriptionId"") REFERENCES ""BillingSubscriptions"" (""Id"") ON DELETE SET NULL
                        );
                        CREATE UNIQUE INDEX ""IX_Invoices_InvoiceNumber"" ON ""Invoices"" (""InvoiceNumber"");
                        CREATE INDEX ""IX_Invoices_OrganizationId"" ON ""Invoices"" (""OrganizationId"");
                        CREATE INDEX ""IX_Invoices_SubscriptionId"" ON ""Invoices"" (""SubscriptionId"");
                        CREATE INDEX ""IX_Invoices_Status"" ON ""Invoices"" (""Status"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_Invoices_InvoiceDate"" ON ""Invoices"" (""InvoiceDate"") WHERE ""DeletedAt"" IS NULL;
                    END IF;
                END $$;
            ");

            // Create InvoiceLineItems table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'InvoiceLineItems') THEN
                        CREATE TABLE ""InvoiceLineItems"" (
                            ""Id"" uuid NOT NULL,
                            ""InvoiceId"" uuid NOT NULL,
                            ""Type"" character varying(50) NOT NULL DEFAULT 'subscription',
                            ""Description"" character varying(500) NOT NULL,
                            ""Quantity"" integer NOT NULL DEFAULT 1,
                            ""UnitPrice"" integer NOT NULL DEFAULT 0,
                            ""Amount"" integer NOT NULL DEFAULT 0,
                            ""HsnCode"" character varying(10),
                            ""PeriodStart"" date,
                            ""PeriodEnd"" date,
                            ""PlanCode"" character varying(50),
                            ""BillingCycle"" character varying(10),
                            ""IsProration"" boolean NOT NULL DEFAULT false,
                            ""Metadata"" jsonb,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_InvoiceLineItems"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_InvoiceLineItems_Invoices_InvoiceId"" FOREIGN KEY (""InvoiceId"") REFERENCES ""Invoices"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE INDEX ""IX_InvoiceLineItems_InvoiceId"" ON ""InvoiceLineItems"" (""InvoiceId"");
                    END IF;
                END $$;
            ");

            // Create PaymentMethods table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'PaymentMethods') THEN
                        CREATE TABLE ""PaymentMethods"" (
                            ""Id"" uuid NOT NULL,
                            ""OrganizationId"" uuid NOT NULL,
                            ""Type"" character varying(20) NOT NULL DEFAULT 'card',
                            ""IsDefault"" boolean NOT NULL DEFAULT false,
                            ""Status"" character varying(20) NOT NULL DEFAULT 'active',
                            ""RazorpayTokenId"" character varying(50),
                            ""CardLast4"" character varying(4),
                            ""CardBrand"" character varying(20),
                            ""CardExpMonth"" integer,
                            ""CardExpYear"" integer,
                            ""CardHolderName"" character varying(100),
                            ""BankName"" character varying(100),
                            ""BankAccountLast4"" character varying(4),
                            ""BankIfscCode"" character varying(11),
                            ""UpiId"" character varying(100),
                            ""WalletType"" character varying(50),
                            ""BillingAddress"" jsonb,
                            ""Metadata"" jsonb,
                            ""LastUsedAt"" timestamp with time zone,
                            ""FailedAt"" timestamp with time zone,
                            ""FailureReason"" text,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_PaymentMethods"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_PaymentMethods_Organizations_OrganizationId"" FOREIGN KEY (""OrganizationId"") REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE INDEX ""IX_PaymentMethods_OrganizationId"" ON ""PaymentMethods"" (""OrganizationId"");
                        CREATE INDEX ""IX_PaymentMethods_IsDefault"" ON ""PaymentMethods"" (""IsDefault"") WHERE ""DeletedAt"" IS NULL AND ""IsDefault"" = true;
                        CREATE INDEX ""IX_PaymentMethods_RazorpayTokenId"" ON ""PaymentMethods"" (""RazorpayTokenId"") WHERE ""RazorpayTokenId"" IS NOT NULL;
                    END IF;
                END $$;
            ");

            // Create Payments table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'Payments') THEN
                        CREATE TABLE ""Payments"" (
                            ""Id"" uuid NOT NULL,
                            ""OrganizationId"" uuid NOT NULL,
                            ""InvoiceId"" uuid,
                            ""SubscriptionId"" uuid,
                            ""Amount"" integer NOT NULL DEFAULT 0,
                            ""Currency"" character varying(3) NOT NULL DEFAULT 'INR',
                            ""Status"" character varying(20) NOT NULL DEFAULT 'pending',
                            ""PaymentMethod"" character varying(20) NOT NULL DEFAULT 'card',
                            ""PaymentMethodDetails"" character varying(100),
                            ""RazorpayPaymentId"" character varying(50),
                            ""RazorpayOrderId"" character varying(50),
                            ""RazorpaySignature"" character varying(200),
                            ""FailureReason"" text,
                            ""FailureCode"" character varying(50),
                            ""RefundId"" character varying(50),
                            ""RefundAmount"" integer,
                            ""RefundedAt"" timestamp with time zone,
                            ""RefundReason"" text,
                            ""CapturedAt"" timestamp with time zone,
                            ""ReceiptNumber"" character varying(50),
                            ""Metadata"" jsonb,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_Payments"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_Payments_Organizations_OrganizationId"" FOREIGN KEY (""OrganizationId"") REFERENCES ""Organizations"" (""Id"") ON DELETE CASCADE,
                            CONSTRAINT ""FK_Payments_Invoices_InvoiceId"" FOREIGN KEY (""InvoiceId"") REFERENCES ""Invoices"" (""Id"") ON DELETE SET NULL,
                            CONSTRAINT ""FK_Payments_BillingSubscriptions_SubscriptionId"" FOREIGN KEY (""SubscriptionId"") REFERENCES ""BillingSubscriptions"" (""Id"") ON DELETE SET NULL
                        );
                        CREATE INDEX ""IX_Payments_OrganizationId"" ON ""Payments"" (""OrganizationId"");
                        CREATE INDEX ""IX_Payments_InvoiceId"" ON ""Payments"" (""InvoiceId"");
                        CREATE INDEX ""IX_Payments_SubscriptionId"" ON ""Payments"" (""SubscriptionId"");
                        CREATE INDEX ""IX_Payments_Status"" ON ""Payments"" (""Status"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_Payments_RazorpayPaymentId"" ON ""Payments"" (""RazorpayPaymentId"") WHERE ""RazorpayPaymentId"" IS NOT NULL;
                    END IF;
                END $$;
            ");

            // Create WebhookEvents table if it doesn't exist
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT FROM pg_tables WHERE tablename = 'WebhookEvents') THEN
                        CREATE TABLE ""WebhookEvents"" (
                            ""Id"" uuid NOT NULL,
                            ""Provider"" character varying(20) NOT NULL DEFAULT 'razorpay',
                            ""EventId"" character varying(100) NOT NULL,
                            ""EventType"" character varying(100) NOT NULL,
                            ""Status"" character varying(20) NOT NULL DEFAULT 'pending',
                            ""Payload"" text NOT NULL,
                            ""Signature"" character varying(200),
                            ""RelatedEntityId"" character varying(100),
                            ""RelatedEntityType"" character varying(50),
                            ""ProcessingStartedAt"" timestamp with time zone,
                            ""ProcessedAt"" timestamp with time zone,
                            ""ErrorMessage"" text,
                            ""AttemptCount"" integer NOT NULL DEFAULT 0,
                            ""LastAttemptAt"" timestamp with time zone,
                            ""IpAddress"" character varying(50),
                            ""ProcessingResult"" jsonb,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""UpdatedAt"" timestamp with time zone,
                            ""DeletedAt"" timestamp with time zone,
                            CONSTRAINT ""PK_WebhookEvents"" PRIMARY KEY (""Id"")
                        );
                        CREATE UNIQUE INDEX ""IX_WebhookEvents_Provider_EventId"" ON ""WebhookEvents"" (""Provider"", ""EventId"");
                        CREATE INDEX ""IX_WebhookEvents_Status"" ON ""WebhookEvents"" (""Status"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_WebhookEvents_EventType"" ON ""WebhookEvents"" (""EventType"") WHERE ""DeletedAt"" IS NULL;
                        CREATE INDEX ""IX_WebhookEvents_CreatedAt"" ON ""WebhookEvents"" (""CreatedAt"") WHERE ""DeletedAt"" IS NULL;
                    END IF;
                END $$;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tables in reverse order (respecting FK constraints)
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""WebhookEvents"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Payments"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""PaymentMethods"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""InvoiceLineItems"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Invoices"";");
        }
    }
}
