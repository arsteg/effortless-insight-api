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
            // FREE PLAN
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "free",
                Name = "Free",
                DisplayName = "Free Plan",
                Description = "Perfect for getting started with basic GST notice management",
                PricingMonthly = 0,
                PricingAnnually = 0,
                PerSeatMonthly = null,
                PerSeatAnnually = null,
                Currency = "INR",
                Limits = new PlanLimits
                {
                    NoticesPerMonth = 10,
                    Users = 1,
                    StorageGb = 1,
                    OrganizationsCount = 1,
                    AdditionalUsersAllowed = false,
                    ApiCalls = 1000
                },
                Features = new List<string>
                {
                    "basic_ai_analysis",
                    "email_notifications"
                },
                IsActive = true,
                IsPopular = false,
                TrialDays = 0, // Free plan doesn't need trial
                SortOrder = 10,
                ContactSales = false,
                CreatedAt = now
            },

            // STARTER PLAN
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "starter",
                Name = "Starter",
                DisplayName = "Starter Plan",
                Description = "For small teams managing regular GST compliance",
                PricingMonthly = 99900, // ₹999 in paise
                PricingAnnually = 999000, // ₹9,990 in paise (16.67% discount)
                PerSeatMonthly = null,
                PerSeatAnnually = null,
                Currency = "INR",
                Limits = new PlanLimits
                {
                    NoticesPerMonth = 50,
                    Users = 3,
                    StorageGb = 10,
                    OrganizationsCount = 1,
                    AdditionalUsersAllowed = false,
                    ApiCalls = 5000
                },
                Features = new List<string>
                {
                    "full_ai_analysis",
                    "priority_processing",
                    "email_notifications",
                    "sms_notifications",
                    "calendar_sync"
                },
                IsActive = true,
                IsPopular = false,
                TrialDays = defaultTrialDays,
                SortOrder = 20,
                ContactSales = false,
                CreatedAt = now
            },

            // PROFESSIONAL PLAN (MOST POPULAR)
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "professional",
                Name = "Professional",
                DisplayName = "Professional Plan",
                Description = "For growing businesses with advanced compliance needs",
                PricingMonthly = 299900, // ₹2,999 in paise
                PricingAnnually = 2999000, // ₹29,990 in paise (16.67% discount)
                PerSeatMonthly = 49900, // ₹499 per additional user/month
                PerSeatAnnually = 499000, // ₹4,990 per additional user/year
                Currency = "INR",
                Limits = new PlanLimits
                {
                    NoticesPerMonth = 200,
                    Users = 10,
                    StorageGb = 50,
                    OrganizationsCount = 1,
                    AdditionalUsersAllowed = true,
                    ApiCalls = 20000
                },
                Features = new List<string>
                {
                    "full_ai_analysis",
                    "priority_processing",
                    "email_notifications",
                    "sms_notifications",
                    "calendar_sync",
                    "whatsapp_notifications",
                    "advanced_analytics",
                    "custom_workflows",
                    "api_access",
                    "priority_support"
                },
                IsActive = true,
                IsPopular = true, // Marked as most popular
                TrialDays = defaultTrialDays,
                SortOrder = 30,
                ContactSales = false,
                CreatedAt = now
            },

            // ENTERPRISE PLAN
            new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Code = "enterprise",
                Name = "Enterprise",
                DisplayName = "Enterprise Plan",
                Description = "Unlimited access with dedicated support for large organizations",
                PricingMonthly = null, // Contact sales
                PricingAnnually = null, // Contact sales
                PerSeatMonthly = null,
                PerSeatAnnually = null,
                Currency = "INR",
                StartingAt = 999900, // Starting at ₹9,999
                Limits = new PlanLimits
                {
                    NoticesPerMonth = -1, // Unlimited
                    Users = -1, // Unlimited
                    StorageGb = -1, // Unlimited
                    OrganizationsCount = -1, // Unlimited
                    AdditionalUsersAllowed = true,
                    ApiCalls = -1 // Unlimited
                },
                Features = new List<string>
                {
                    "full_ai_analysis",
                    "priority_processing",
                    "email_notifications",
                    "sms_notifications",
                    "calendar_sync",
                    "whatsapp_notifications",
                    "advanced_analytics",
                    "custom_workflows",
                    "api_access",
                    "priority_support",
                    "dedicated_account_manager",
                    "custom_integrations",
                    "sla_guarantee",
                    "audit_reports",
                    "white_labeling"
                },
                IsActive = true,
                IsPopular = false,
                TrialDays = 30, // Extended trial for enterprise evaluation
                SortOrder = 40,
                ContactSales = true,
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
