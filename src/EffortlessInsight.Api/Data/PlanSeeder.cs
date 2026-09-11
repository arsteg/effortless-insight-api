using EffortlessInsight.Api.Data.Entities.Billing;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Data;

/// <summary>
/// Seeds default subscription plans on application startup.
/// Plans are idempotent - won't create duplicates on subsequent runs.
/// </summary>
public class PlanSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PlanSeeder> _logger;
    private readonly IConfiguration _configuration;

    public PlanSeeder(
        ApplicationDbContext context,
        ILogger<PlanSeeder> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Check if seeding is enabled in configuration
        var seedEnabled = _configuration.GetValue("Billing:SeedPlansOnStartup", true);
        if (!seedEnabled)
        {
            _logger.LogInformation("Plan seeding is disabled in configuration");
            return;
        }

        // Check if any plans already exist
        var existingPlans = await _context.SubscriptionPlans
            .IgnoreQueryFilters()
            .AnyAsync(cancellationToken);

        if (existingPlans)
        {
            _logger.LogInformation("Subscription plans already exist, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding default subscription plans...");

        var now = DateTime.UtcNow;
        var defaultTrialDays = _configuration.GetValue("Billing:DefaultTrialDays", 14);

        var plans = new List<SubscriptionPlan>
        {
            // ============================================================================
            // FREE PLAN - "Notify Only"
            // Detection + email/push reminders only. No AI, no WhatsApp.
            // ============================================================================
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "free",
                Name = "Notify Only",
                DisplayName = "Free",
                Description = "Notice detection + email/push reminders only",
                PricingWeekly = 0,
                PricingMonthly = 0,
                PricingAnnually = 0,
                PerSeatWeekly = null,
                PerSeatMonthly = null,
                PerSeatAnnually = null,
                Currency = "INR",
                AllowedBillingCycles = new List<string> { "annually" }, // Free doesn't need cycle options
                DefaultBillingCycle = "annually",
                IsCaOperatorPlan = false,
                Limits = new PlanLimits
                {
                    NoticesPerMonth = -1, // Unlimited detection
                    Users = 1,
                    StorageGb = 1,
                    OrganizationsCount = 1,
                    AdditionalUsersAllowed = false,
                    ApiCalls = 100,
                    GstinsAllowed = 1
                },
                Features = new List<string>
                {
                    "notice_detection",
                    "email_notifications",
                    "push_notifications"
                    // NO: ai_explanation, draft_reply, whatsapp_assistant
                },
                IsActive = true,
                IsPopular = false,
                TrialDays = 0, // Free plan doesn't need trial
                SortOrder = 10,
                ContactSales = false,
                CreatedAt = now
            },

            // ============================================================================
            // UNDERSTAND & RESPOND - Rs 999/year
            // AI explanation + draft reply + WhatsApp
            // ============================================================================
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "understand_respond",
                Name = "Understand & Respond",
                DisplayName = "₹999/year",
                Description = "AI-powered notice explanation, draft replies, and WhatsApp assistant",
                PricingWeekly = 2500,      // Rs 25 in paise
                PricingMonthly = 9900,     // Rs 99 in paise
                PricingAnnually = 99900,   // Rs 999 in paise
                PerSeatWeekly = null,
                PerSeatMonthly = null,
                PerSeatAnnually = null,
                Currency = "INR",
                AllowedBillingCycles = new List<string> { "weekly", "monthly", "annually" },
                DefaultBillingCycle = "annually",
                IsCaOperatorPlan = false,
                Limits = new PlanLimits
                {
                    NoticesPerMonth = -1, // Unlimited
                    Users = 1,
                    StorageGb = 10,
                    OrganizationsCount = 1,
                    AdditionalUsersAllowed = false,
                    ApiCalls = 5000,
                    GstinsAllowed = 1
                },
                Features = new List<string>
                {
                    "notice_detection",
                    "email_notifications",
                    "push_notifications",
                    "ai_explanation",
                    "draft_reply",
                    "whatsapp_assistant",
                    "multilingual_support"
                },
                IsActive = true,
                IsPopular = true, // Marked as recommended
                TrialDays = defaultTrialDays,
                SortOrder = 20,
                ContactSales = false,
                CreatedAt = now
            },

            // ============================================================================
            // TEAM & MULTI-ENTITY - Rs 1,999/year
            // Collaboration, multiple GSTINs, custom roles
            // ============================================================================
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "team",
                Name = "Team & Multi-entity",
                DisplayName = "₹1,999/year",
                Description = "For teams managing multiple GSTINs with collaboration features",
                PricingWeekly = 5000,      // Rs 50 in paise
                PricingMonthly = 19900,    // Rs 199 in paise
                PricingAnnually = 199900,  // Rs 1,999 in paise
                PerSeatWeekly = 1500,      // Rs 15 per additional user/week
                PerSeatMonthly = 4900,     // Rs 49 per additional user/month
                PerSeatAnnually = 49900,   // Rs 499 per additional user/year
                Currency = "INR",
                AllowedBillingCycles = new List<string> { "weekly", "monthly", "annually" },
                DefaultBillingCycle = "annually",
                IsCaOperatorPlan = false,
                Limits = new PlanLimits
                {
                    NoticesPerMonth = -1, // Unlimited
                    Users = 5,
                    StorageGb = 50,
                    OrganizationsCount = 1,
                    AdditionalUsersAllowed = true,
                    ApiCalls = 20000,
                    GstinsAllowed = 10
                },
                Features = new List<string>
                {
                    // All of understand_respond +
                    "notice_detection",
                    "email_notifications",
                    "push_notifications",
                    "ai_explanation",
                    "draft_reply",
                    "whatsapp_assistant",
                    "multilingual_support",
                    "collaboration",
                    "custom_roles",
                    "audit_trail",
                    "advanced_analytics"
                },
                IsActive = true,
                IsPopular = false,
                TrialDays = defaultTrialDays,
                SortOrder = 30,
                ContactSales = false,
                CreatedAt = now
            },

            // ============================================================================
            // ENTERPRISE - Custom pricing
            // SSO, API access, workflows, SLA guarantee
            // ============================================================================
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "enterprise",
                Name = "Enterprise",
                DisplayName = "Enterprise",
                Description = "Unlimited access with dedicated support for large organizations",
                PricingWeekly = null, // Contact sales
                PricingMonthly = null, // Contact sales
                PricingAnnually = null, // Contact sales
                PerSeatWeekly = null,
                PerSeatMonthly = null,
                PerSeatAnnually = null,
                Currency = "INR",
                StartingAt = 999900, // Starting at ₹9,999
                AllowedBillingCycles = new List<string> { "annually" }, // Enterprise typically annual
                DefaultBillingCycle = "annually",
                IsCaOperatorPlan = false,
                Limits = new PlanLimits
                {
                    NoticesPerMonth = -1, // Unlimited
                    Users = -1, // Unlimited
                    StorageGb = -1, // Unlimited
                    OrganizationsCount = -1, // Unlimited
                    AdditionalUsersAllowed = true,
                    ApiCalls = -1, // Unlimited
                    GstinsAllowed = -1 // Unlimited
                },
                Features = new List<string>
                {
                    // All features
                    "notice_detection",
                    "email_notifications",
                    "push_notifications",
                    "ai_explanation",
                    "draft_reply",
                    "whatsapp_assistant",
                    "multilingual_support",
                    "collaboration",
                    "custom_roles",
                    "audit_trail",
                    "advanced_analytics",
                    "sso",
                    "api_access",
                    "workflows",
                    "sla_guarantee",
                    "priority_support",
                    "dedicated_account_manager",
                    "custom_integrations"
                },
                IsActive = true,
                IsPopular = false,
                TrialDays = 30, // Extended trial for enterprise evaluation
                SortOrder = 40,
                ContactSales = true,
                CreatedAt = now
            },

            // ============================================================================
            // CA OPERATOR - Free for verified Chartered Accountants
            // Full access to all features. Manually assigned by admin after CA verification.
            // ============================================================================
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "ca_operator",
                Name = "CA Operator",
                DisplayName = "CA Professional",
                Description = "Full access for verified Chartered Accountants managing client GST notices",
                PricingWeekly = 0,
                PricingMonthly = 0,
                PricingAnnually = 0,
                PerSeatWeekly = null,
                PerSeatMonthly = null,
                PerSeatAnnually = null,
                Currency = "INR",
                AllowedBillingCycles = new List<string> { "annually" },
                DefaultBillingCycle = "annually",
                IsCaOperatorPlan = true, // Special flag for CA operators
                Limits = new PlanLimits
                {
                    NoticesPerMonth = -1, // Unlimited
                    Users = -1, // Unlimited
                    StorageGb = -1, // Unlimited
                    OrganizationsCount = -1, // Unlimited (can manage many client orgs)
                    AdditionalUsersAllowed = true,
                    ApiCalls = -1, // Unlimited
                    GstinsAllowed = -1 // Unlimited (managing client GSTINs)
                },
                Features = new List<string>
                {
                    // Full access to all features
                    "notice_detection",
                    "email_notifications",
                    "push_notifications",
                    "ai_explanation",
                    "draft_reply",
                    "whatsapp_assistant",
                    "multilingual_support",
                    "collaboration",
                    "custom_roles",
                    "audit_trail",
                    "advanced_analytics",
                    "api_access",
                    "workflows",
                    "priority_support",
                    "ca_client_management" // Special CA-only feature
                },
                IsActive = true,
                IsPopular = false,
                TrialDays = 0, // No trial needed - admin assigns after verification
                SortOrder = 50, // Hidden from regular pricing page
                ContactSales = false,
                CreatedAt = now
            }
        };

        _context.SubscriptionPlans.AddRange(plans);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Successfully seeded {Count} subscription plans: {PlanCodes}",
            plans.Count,
            string.Join(", ", plans.Select(p => p.Code)));
    }
}
