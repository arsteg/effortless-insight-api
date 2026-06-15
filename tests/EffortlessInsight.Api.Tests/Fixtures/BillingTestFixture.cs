using Bogus;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Tests.Fixtures;

/// <summary>
/// Test fixture for creating billing-related test data.
/// </summary>
public static class BillingTestFixture
{
    private static readonly Faker Faker = new("en");

    public static SubscriptionPlan CreatePlan(
        string? code = null,
        string? displayName = null,
        int? monthlyPrice = null,
        int? annualPrice = null,
        bool isActive = true)
    {
        return new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Code = code ?? Faker.Random.Word().ToLower(),
            Name = displayName ?? Faker.Commerce.ProductName(),
            DisplayName = displayName ?? Faker.Commerce.ProductName(),
            Description = Faker.Lorem.Sentence(),
            PricingMonthly = monthlyPrice ?? Faker.Random.Int(100, 10000) * 100,
            PricingAnnually = annualPrice ?? Faker.Random.Int(1000, 100000) * 100,
            Currency = "INR",
            Limits = new PlanLimits
            {
                NoticesPerMonth = Faker.Random.Int(10, 1000),
                Users = Faker.Random.Int(1, 50),
                StorageGb = Faker.Random.Int(1, 100),
                OrganizationsCount = Faker.Random.Int(1, 10),
                AdditionalUsersAllowed = true,
                ApiCalls = Faker.Random.Int(100, 10000)
            },
            Features = new List<string> { "basic_ai_analysis", "email_support" },
            TrialDays = 14,
            IsActive = isActive,
            IsPopular = false,
            SortOrder = 1,
            ContactSales = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static SubscriptionPlan CreateFreePlan()
    {
        return CreatePlan(
            code: "free",
            displayName: "Free",
            monthlyPrice: 0,
            annualPrice: 0);
    }

    public static SubscriptionPlan CreateStarterPlan()
    {
        var plan = CreatePlan(
            code: "starter",
            displayName: "Starter",
            monthlyPrice: 99900,
            annualPrice: 999900);
        plan.Limits.NoticesPerMonth = 100;
        plan.Limits.Users = 5;
        plan.Limits.StorageGb = 10;
        plan.PerSeatMonthly = 10000;
        plan.PerSeatAnnually = 100000;
        return plan;
    }

    public static SubscriptionPlan CreateProfessionalPlan()
    {
        var plan = CreatePlan(
            code: "professional",
            displayName: "Professional",
            monthlyPrice: 249900,
            annualPrice: 2499900);
        plan.Limits.NoticesPerMonth = 500;
        plan.Limits.Users = 25;
        plan.Limits.StorageGb = 50;
        plan.Features.Add("full_ai_analysis");
        plan.Features.Add("priority_support");
        plan.IsPopular = true;
        plan.PerSeatMonthly = 10000;
        plan.PerSeatAnnually = 100000;
        return plan;
    }

    public static SubscriptionPlan CreateEnterprisePlan()
    {
        var plan = CreatePlan(
            code: "enterprise",
            displayName: "Enterprise",
            monthlyPrice: 0,
            annualPrice: 0);
        plan.Limits.NoticesPerMonth = -1; // Unlimited
        plan.Limits.Users = -1;
        plan.Limits.StorageGb = -1;
        plan.ContactSales = true;
        return plan;
    }

    public static BillingSubscription CreateSubscription(
        Guid? organizationId = null,
        Guid? planId = null,
        string? planCode = null,
        string status = SubscriptionStatus.Active,
        string billingCycle = "monthly")
    {
        var now = DateTime.UtcNow;
        return new BillingSubscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            PlanId = planId ?? Guid.NewGuid(),
            PlanCode = planCode ?? "starter",
            Status = status,
            BillingCycle = billingCycle,
            SeatsIncluded = 5,
            SeatsAdditional = 0,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = billingCycle == "monthly" ? now.AddMonths(1) : now.AddYears(1),
            CancelAtPeriodEnd = false,
            BaseAmount = 999.00m,
            TaxAmount = 179.82m,
            TotalAmount = 1178.82m,
            Currency = "INR",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static BillingSubscription CreateTrialSubscription(
        Guid? organizationId = null,
        Guid? planId = null,
        int trialDaysRemaining = 14)
    {
        var subscription = CreateSubscription(organizationId, planId, status: SubscriptionStatus.Trialing);
        subscription.TrialStart = DateTime.UtcNow;
        subscription.TrialEnd = DateTime.UtcNow.AddDays(trialDaysRemaining);
        return subscription;
    }

    public static UsageRecord CreateUsageRecord(
        Guid? organizationId = null,
        int noticesCount = 0,
        int usersCount = 1,
        long storageBytes = 0)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        return new UsageRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            PeriodStart = new DateOnly(now.Year, now.Month, 1),
            PeriodEnd = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)),
            NoticesCount = noticesCount,
            UsersCount = usersCount,
            StorageBytes = storageBytes,
            ApiCalls = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Coupon CreateCoupon(
        string? code = null,
        string discountType = CouponDiscountType.Percent,
        int discountValue = 10,
        bool isActive = true)
    {
        return new Coupon
        {
            Id = Guid.NewGuid(),
            Code = code ?? Faker.Random.AlphaNumeric(8).ToUpper(),
            Description = Faker.Lorem.Sentence(),
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountAmount = discountType == CouponDiscountType.Percent ? 100000 : null,
            MinPurchaseAmount = 0,
            MaxRedemptions = 100,
            TimesRedeemed = 0,
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidUntil = DateTime.UtcNow.AddMonths(1),
            IsActive = isActive,
            ApplicablePlans = new List<string> { "*" },
            ApplicableCycles = new List<string> { "*" },
            FirstTimeOnly = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Invoice CreateInvoice(
        Guid? organizationId = null,
        Guid? subscriptionId = null,
        string status = InvoiceStatus.Paid,
        int total = 117882)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new Invoice
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{Faker.Random.Int(1, 999999):D6}",
            Status = status,
            InvoiceDate = today,
            DueDate = today,
            Currency = "INR",
            Subtotal = (int)(total / 1.18),
            TaxRate = 18,
            TaxAmount = total - (int)(total / 1.18),
            Total = total,
            AmountPaid = status == InvoiceStatus.Paid ? total : 0,
            AmountDue = status == InvoiceStatus.Paid ? 0 : total,
            HsnCode = "998314",
            IsInterState = false,
            BillingDetails = new InvoiceBillingDetails
            {
                OrganizationName = Faker.Company.CompanyName(),
                Address = Faker.Address.StreetAddress(),
                City = Faker.Address.City(),
                State = "Maharashtra",
                Pincode = "400001",
                Country = "India"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static PaymentMethod CreatePaymentMethod(
        Guid? organizationId = null,
        string type = PaymentMethodType.Card,
        bool isDefault = true)
    {
        return new PaymentMethod
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            Type = type,
            IsDefault = isDefault,
            CardLast4 = type == PaymentMethodType.Card ? Faker.Random.Int(1000, 9999).ToString() : null,
            CardBrand = type == PaymentMethodType.Card ? "visa" : null,
            CardExpiryMonth = type == PaymentMethodType.Card ? Faker.Random.Int(1, 12) : null,
            CardExpiryYear = type == PaymentMethodType.Card ? DateTime.UtcNow.Year + 2 : null,
            UpiId = type == PaymentMethodType.Upi ? $"{Faker.Internet.UserName()}@upi" : null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static BillingDetails CreateBillingDetails(Guid? organizationId = null)
    {
        return new BillingDetails
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            OrganizationName = Faker.Company.CompanyName(),
            Address = Faker.Address.StreetAddress(),
            City = Faker.Address.City(),
            State = "Maharashtra",
            StateCode = "27",
            Pincode = "400001",
            Country = "India",
            Email = Faker.Internet.Email(),
            Phone = Faker.Phone.PhoneNumber("##########"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static Organization CreateOrganization(Guid? id = null)
    {
        var name = Faker.Company.CompanyName();
        return new Organization
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            NameNormalized = name.ToLower(),
            SubscriptionStatus = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static ApplicationUser CreateUser(
        Guid? id = null,
        Guid? organizationId = null,
        string role = "owner")
    {
        var email = Faker.Internet.Email();
        return new ApplicationUser
        {
            Id = id ?? Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            Email = email,
            UserName = email,
            Name = Faker.Name.FullName(),
            Role = role,
            IsActive = true
        };
    }

    public static CreateSubscriptionRequest CreateSubscriptionRequest(
        string planCode = "starter",
        string billingCycle = "monthly")
    {
        return new CreateSubscriptionRequest(
            PlanCode: planCode,
            BillingCycle: billingCycle,
            AdditionalSeats: 0,
            BillingDetails: new BillingDetailsRequest(
                OrganizationName: Faker.Company.CompanyName(),
                Gstin: null,
                Address: Faker.Address.StreetAddress(),
                AddressLine2: null,
                City: Faker.Address.City(),
                State: "Maharashtra",
                Pincode: "400001",
                Email: Faker.Internet.Email(),
                Phone: Faker.Phone.PhoneNumber("##########")
            ),
            CouponCode: null,
            AutoRenew: true
        );
    }
}
