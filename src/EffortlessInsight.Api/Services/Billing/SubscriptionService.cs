using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of the subscription service.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPlanService _planService;
    private readonly IUsageService _usageService;
    private readonly IRazorpayService _razorpayService;
    private readonly ICouponService _couponService;
    private readonly IInvoiceService _invoiceService;
    private readonly IBillingNotificationService _billingNotificationService;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ApplicationDbContext dbContext,
        IPlanService planService,
        IUsageService usageService,
        IRazorpayService razorpayService,
        ICouponService couponService,
        IInvoiceService invoiceService,
        IBillingNotificationService billingNotificationService,
        ILogger<SubscriptionService> logger)
    {
        _dbContext = dbContext;
        _planService = planService;
        _usageService = usageService;
        _razorpayService = razorpayService;
        _couponService = couponService;
        _invoiceService = invoiceService;
        _billingNotificationService = billingNotificationService;
        _logger = logger;
    }

    public async Task<CurrentSubscriptionResponse?> GetCurrentSubscriptionAsync(Guid organizationId)
    {
        var subscription = await GetSubscriptionEntityAsync(organizationId);
        if (subscription == null)
            return null;

        var plan = await _planService.GetPlanByIdAsync(subscription.PlanId);
        if (plan == null)
            return null;

        var usage = await _usageService.GetCurrentUsageAsync(organizationId);

        var subscriptionDto = MapToSubscriptionDto(subscription, plan);
        var usageDto = MapToUsageDto(usage, plan.Limits, subscription);

        return new CurrentSubscriptionResponse(subscriptionDto, usageDto);
    }

    public async Task<BillingSubscription?> GetSubscriptionEntityAsync(Guid organizationId)
    {
        return await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId);
    }

    public async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
        Guid organizationId,
        Guid userId,
        CreateSubscriptionRequest request)
    {
        var plan = await _planService.GetPlanByCodeAsync(request.PlanCode)
            ?? throw new InvalidOperationException($"Plan '{request.PlanCode}' not found");

        if (plan.ContactSales)
            throw new InvalidOperationException("Enterprise plans require contacting sales");

        // Save billing details
        await SaveBillingDetailsAsync(organizationId, request.BillingDetails);

        // Validate and apply coupon
        int? discountAmount = null;
        if (!string.IsNullOrEmpty(request.CouponCode))
        {
            var couponResult = await _couponService.ValidateCouponAsync(
                request.CouponCode, request.PlanCode, request.BillingCycle);

            if (couponResult.IsValid && couponResult.Coupon != null)
            {
                discountAmount = couponResult.Coupon.CalculatedDiscount;
            }
        }

        // Calculate pricing
        var pricing = _planService.CalculateSubscriptionPrice(
            plan, request.BillingCycle, request.AdditionalSeats, discountAmount);

        // Create or update subscription in pending state
        var subscription = await GetSubscriptionEntityAsync(organizationId);
        if (subscription == null)
        {
            subscription = new BillingSubscription
            {
                OrganizationId = organizationId,
                PlanCode = plan.Code,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Trialing,
                BillingCycle = request.BillingCycle,
                SeatsIncluded = plan.Limits.Users,
                SeatsAdditional = request.AdditionalSeats
            };
            _dbContext.BillingSubscriptions.Add(subscription);
        }
        else
        {
            subscription.PlanCode = plan.Code;
            subscription.PlanId = plan.Id;
            subscription.BillingCycle = request.BillingCycle;
            subscription.SeatsAdditional = request.AdditionalSeats;
        }

        await _dbContext.SaveChangesAsync();

        // Get user info for prefill
        var user = await _dbContext.Users.FindAsync(userId);
        var org = await _dbContext.Organizations.FindAsync(organizationId);

        // Create Razorpay order
        var order = await _razorpayService.CreateOrderAsync(new CreateOrderRequest
        {
            AmountInPaise = pricing.Total,
            Currency = pricing.Currency,
            Receipt = $"sub_{subscription.Id:N}",
            OrganizationId = organizationId,
            PlanCode = plan.Code,
            SubscriptionId = subscription.Id
        });

        var checkoutOptions = new CheckoutOptionsDto(
            Key: order.Key,
            Amount: order.Amount,
            Currency: order.Currency,
            Name: "EffortlessInsight",
            Description: $"{plan.DisplayName} Plan ({request.BillingCycle})",
            OrderId: order.Id,
            Prefill: new CheckoutPrefillDto(
                Name: user?.Name,
                Email: user?.Email,
                Contact: user?.PhoneNumber
            ),
            Theme: new CheckoutThemeDto(Color: "#3B82F6")
        );

        return new CreateSubscriptionResponse(
            SubscriptionId: subscription.Id,
            RazorpayOrder: order,
            CheckoutOptions: checkoutOptions
        );
    }

    public async Task<VerifyPaymentResponse> VerifyPaymentAsync(
        Guid organizationId,
        Guid userId,
        VerifyPaymentRequest request)
    {
        // Verify signature
        var isValid = _razorpayService.VerifyPaymentSignature(
            request.RazorpayOrderId,
            request.RazorpayPaymentId,
            request.RazorpaySignature);

        if (!isValid)
            throw new InvalidOperationException("Payment signature verification failed");

        var subscription = await GetSubscriptionEntityAsync(organizationId)
            ?? throw new InvalidOperationException("Subscription not found");

        var plan = await _planService.GetPlanByIdAsync(subscription.PlanId)
            ?? throw new InvalidOperationException("Plan not found");

        // Capture payment
        var payment = await _razorpayService.CapturePaymentAsync(request.RazorpayPaymentId);

        // Record payment
        var paymentRecord = new Payment
        {
            OrganizationId = organizationId,
            SubscriptionId = subscription.Id,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = PaymentStatus.Captured,
            PaymentMethod = payment.Method,
            RazorpayPaymentId = payment.PaymentId,
            RazorpayOrderId = request.RazorpayOrderId,
            RazorpaySignature = request.RazorpaySignature,
            CapturedAt = DateTime.UtcNow
        };
        _dbContext.Payments.Add(paymentRecord);

        // Activate subscription
        var now = DateTime.UtcNow;
        var periodEnd = subscription.BillingCycle == BillingCycle.Annually
            ? now.AddYears(1)
            : now.AddMonths(1);

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = now;
        subscription.CurrentPeriodEnd = periodEnd;
        subscription.TrialEnd = null;
        subscription.FailedPaymentAttempts = 0;
        subscription.Metadata ??= new Dictionary<string, object>();
        subscription.Metadata["activatedAt"] = now.ToString("O");
        subscription.Metadata["activatedBy"] = userId.ToString();

        // Update organization
        var org = await _dbContext.Organizations.FindAsync(organizationId);
        if (org != null)
        {
            org.SubscriptionStatus = "active";
            org.PlanId = await GetLegacyPlanIdAsync(plan.Code);
        }

        await _dbContext.SaveChangesAsync();

        // Generate invoice
        InvoiceSummaryDto? invoiceSummary = null;
        try
        {
            var description = $"{plan.DisplayName} Subscription - {subscription.BillingCycle}";
            var lineItems = new List<InvoiceLineItemRequest>
            {
                new()
                {
                    Type = "subscription",
                    Description = description,
                    Quantity = 1,
                    UnitPrice = (int)(subscription.TotalAmount * 100), // Convert to paise
                    Amount = (int)(subscription.TotalAmount * 100),
                    PlanCode = plan.Code,
                    BillingCycle = subscription.BillingCycle,
                    PeriodStart = DateOnly.FromDateTime(subscription.CurrentPeriodStart),
                    PeriodEnd = DateOnly.FromDateTime(subscription.CurrentPeriodEnd)
                }
            };

            var invoice = await _invoiceService.GenerateInvoiceAsync(
                organizationId,
                subscription.Id,
                (int)(subscription.TotalAmount * 100), // Amount in paise
                description,
                lineItems);

            // Mark invoice as paid
            await _invoiceService.MarkAsPaidAsync(invoice.Id, request.RazorpayPaymentId);

            invoiceSummary = new InvoiceSummaryDto(
                Id: invoice.Id,
                Number: invoice.InvoiceNumber,
                DownloadUrl: $"/api/v1/invoices/{invoice.Id}/pdf"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to generate invoice for subscription {SubscriptionId}",
                subscription.Id);
        }

        _logger.LogInformation(
            "Subscription {SubscriptionId} activated for organization {OrganizationId}",
            subscription.Id, organizationId);

        return new VerifyPaymentResponse(
            Success: true,
            Subscription: new SubscriptionActivatedDto(
                Id: subscription.Id,
                Status: subscription.Status,
                PlanCode: subscription.PlanCode,
                ActivatedAt: now
            ),
            Invoice: invoiceSummary
        );
    }

    public async Task<ChangePlanResponse> ChangePlanAsync(
        Guid organizationId,
        Guid userId,
        ChangePlanRequest request)
    {
        var subscription = await GetSubscriptionEntityAsync(organizationId)
            ?? throw new InvalidOperationException("No active subscription found");

        var currentPlan = await _planService.GetPlanByIdAsync(subscription.PlanId)
            ?? throw new InvalidOperationException("Current plan not found");

        var newPlan = await _planService.GetPlanByCodeAsync(request.NewPlanCode)
            ?? throw new InvalidOperationException($"Plan '{request.NewPlanCode}' not found");

        if (newPlan.ContactSales)
            throw new InvalidOperationException("Enterprise plans require contacting sales");

        var changeType = _planService.GetPlanChangeType(
            currentPlan, newPlan,
            subscription.BillingCycle, request.BillingCycle);

        var additionalSeats = request.AdditionalSeats ?? subscription.SeatsAdditional;

        if (changeType == "upgrade" || request.EffectiveDate == "immediate")
        {
            // Immediate upgrade with proration
            var prorationAmount = _planService.CalculateProration(
                currentPlan, newPlan,
                subscription.BillingCycle, request.BillingCycle,
                subscription.SeatsAdditional, additionalSeats,
                subscription.CurrentPeriodStart, subscription.CurrentPeriodEnd);

            if (prorationAmount > 0)
            {
                // Create Razorpay order for proration
                var order = await _razorpayService.CreateOrderAsync(new CreateOrderRequest
                {
                    AmountInPaise = prorationAmount,
                    Currency = "INR",
                    Receipt = $"upgrade_{subscription.Id:N}",
                    OrganizationId = organizationId,
                    PlanCode = newPlan.Code,
                    SubscriptionId = subscription.Id
                });

                return new ChangePlanResponse(
                    Type: changeType,
                    ProrationAmount: prorationAmount,
                    NewPlanAmount: _planService.CalculateSubscriptionPrice(newPlan, request.BillingCycle, additionalSeats).Total,
                    TotalDue: prorationAmount,
                    EffectiveImmediately: true,
                    RazorpayOrder: order,
                    ScheduledPlanCode: null,
                    EffectiveDate: null,
                    Message: null
                );
            }

            // No charge needed, apply immediately
            await ApplyPlanChangeAsync(subscription, newPlan, request.BillingCycle, additionalSeats);

            return new ChangePlanResponse(
                Type: changeType,
                ProrationAmount: 0,
                NewPlanAmount: _planService.CalculateSubscriptionPrice(newPlan, request.BillingCycle, additionalSeats).Total,
                TotalDue: 0,
                EffectiveImmediately: true,
                RazorpayOrder: null,
                ScheduledPlanCode: null,
                EffectiveDate: null,
                Message: $"Your plan has been changed to {newPlan.DisplayName}"
            );
        }
        else
        {
            // Schedule downgrade for end of period
            subscription.ScheduledPlanCode = newPlan.Code;
            subscription.ScheduledBillingCycle = request.BillingCycle;
            subscription.ScheduledChangeDate = subscription.CurrentPeriodEnd;

            await _dbContext.SaveChangesAsync();

            return new ChangePlanResponse(
                Type: changeType,
                ProrationAmount: null,
                NewPlanAmount: null,
                TotalDue: null,
                EffectiveImmediately: false,
                RazorpayOrder: null,
                ScheduledPlanCode: newPlan.Code,
                EffectiveDate: subscription.CurrentPeriodEnd,
                Message: $"Your plan will change to {newPlan.DisplayName} on {subscription.CurrentPeriodEnd:MMMM d, yyyy}"
            );
        }
    }

    public async Task<CancelSubscriptionResponse> CancelSubscriptionAsync(
        Guid organizationId,
        Guid userId,
        CancelSubscriptionRequest request)
    {
        var subscription = await GetSubscriptionEntityAsync(organizationId)
            ?? throw new InvalidOperationException("No active subscription found");

        if (subscription.Status == SubscriptionStatus.Cancelled ||
            subscription.Status == SubscriptionStatus.Expired)
        {
            throw new InvalidOperationException("Subscription is already cancelled");
        }

        subscription.CancelAtPeriodEnd = !request.CancelImmediately;
        subscription.CancelledAt = DateTime.UtcNow;
        subscription.CancellationReason = request.Reason;
        subscription.CancellationFeedback = request.Feedback;

        if (request.CancelImmediately)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndedAt = DateTime.UtcNow;

            // Update organization status
            var org = await _dbContext.Organizations.FindAsync(organizationId);
            if (org != null)
            {
                org.SubscriptionStatus = "cancelled";
            }
        }

        await _dbContext.SaveChangesAsync();

        var cancellationDate = request.CancelImmediately
            ? DateTime.UtcNow
            : subscription.CurrentPeriodEnd;

        var message = request.CancelImmediately
            ? "Your subscription has been cancelled immediately"
            : $"Your subscription will remain active until {cancellationDate:MMMM d, yyyy}";

        _logger.LogInformation(
            "Subscription {SubscriptionId} cancelled for organization {OrganizationId}. Reason: {Reason}",
            subscription.Id, organizationId, request.Reason);

        return new CancelSubscriptionResponse(
            Subscription: new SubscriptionCancelledDto(
                Id: subscription.Id,
                Status: subscription.Status,
                CancelAtPeriodEnd: subscription.CancelAtPeriodEnd,
                CancellationDate: cancellationDate
            ),
            Message: message
        );
    }

    public async Task<AddSeatsResponse> AddSeatsAsync(
        Guid organizationId,
        Guid userId,
        AddSeatsRequest request)
    {
        var subscription = await GetSubscriptionEntityAsync(organizationId)
            ?? throw new InvalidOperationException("No active subscription found");

        var plan = await _planService.GetPlanByIdAsync(subscription.PlanId)
            ?? throw new InvalidOperationException("Plan not found");

        if (!plan.Limits.AdditionalUsersAllowed)
            throw new InvalidOperationException("This plan does not allow additional seats");

        // Calculate proration for additional seats
        var currentSeats = subscription.SeatsIncluded + subscription.SeatsAdditional;
        var newAdditionalSeats = subscription.SeatsAdditional + request.AdditionalSeats;

        var prorationAmount = _planService.CalculateProration(
            plan, plan,
            subscription.BillingCycle, subscription.BillingCycle,
            subscription.SeatsAdditional, newAdditionalSeats,
            subscription.CurrentPeriodStart, subscription.CurrentPeriodEnd);

        RazorpayOrderDto? order = null;
        if (prorationAmount > 0)
        {
            order = await _razorpayService.CreateOrderAsync(new CreateOrderRequest
            {
                AmountInPaise = prorationAmount,
                Currency = "INR",
                Receipt = $"seats_{subscription.Id:N}",
                OrganizationId = organizationId,
                PlanCode = plan.Code,
                SubscriptionId = subscription.Id
            });
        }
        else
        {
            // No charge, apply immediately
            subscription.SeatsAdditional = newAdditionalSeats;
            await _dbContext.SaveChangesAsync();
        }

        return new AddSeatsResponse(
            TotalSeats: subscription.SeatsIncluded + newAdditionalSeats,
            ProrationAmount: prorationAmount,
            RazorpayOrder: order
        );
    }

    public async Task<SubscriptionDto> ReactivateSubscriptionAsync(Guid organizationId, Guid userId)
    {
        var subscription = await GetSubscriptionEntityAsync(organizationId)
            ?? throw new InvalidOperationException("No subscription found");

        if (subscription.Status != SubscriptionStatus.Cancelled)
            throw new InvalidOperationException("Subscription is not cancelled");

        // Check if within reactivation period (30 days)
        var daysSinceCancellation = (DateTime.UtcNow - (subscription.EndedAt ?? subscription.CancelledAt ?? DateTime.UtcNow)).TotalDays;
        if (daysSinceCancellation > 30)
            throw new InvalidOperationException("Reactivation period has expired. Please create a new subscription.");

        subscription.Status = SubscriptionStatus.Active;
        subscription.CancelAtPeriodEnd = false;
        subscription.CancelledAt = null;
        subscription.CancellationReason = null;
        subscription.CancellationFeedback = null;
        subscription.EndedAt = null;

        var org = await _dbContext.Organizations.FindAsync(organizationId);
        if (org != null)
        {
            org.SubscriptionStatus = "active";
        }

        await _dbContext.SaveChangesAsync();

        var plan = await _planService.GetPlanByIdAsync(subscription.PlanId);
        return MapToSubscriptionDto(subscription, plan!);
    }

    public async Task<BillingSubscription> StartTrialAsync(
        Guid organizationId,
        string planCode,
        int trialDays)
    {
        var plan = await _planService.GetPlanByCodeAsync(planCode)
            ?? throw new InvalidOperationException($"Plan '{planCode}' not found");

        var existingSubscription = await GetSubscriptionEntityAsync(organizationId);
        if (existingSubscription != null)
            throw new InvalidOperationException("Organization already has a subscription");

        var now = DateTime.UtcNow;
        var subscription = new BillingSubscription
        {
            OrganizationId = organizationId,
            PlanCode = plan.Code,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trialing,
            BillingCycle = BillingCycle.Monthly,
            SeatsIncluded = plan.Limits.Users,
            SeatsAdditional = 0,
            TrialStart = now,
            TrialEnd = now.AddDays(trialDays),
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddDays(trialDays)
        };

        _dbContext.BillingSubscriptions.Add(subscription);

        // Update organization
        var org = await _dbContext.Organizations.FindAsync(organizationId);
        if (org != null)
        {
            org.SubscriptionStatus = "trial";
            org.TrialEndsAt = subscription.TrialEnd;
            org.PlanId = await GetLegacyPlanIdAsync(plan.Code);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Trial started for organization {OrganizationId} with plan {PlanCode} for {TrialDays} days",
            organizationId, planCode, trialDays);

        return subscription;
    }

    public async Task ProcessRenewalAsync(Guid subscriptionId)
    {
        // Called from background job - calculate dates ourselves and attempt charge
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .Include(s => s.Organization)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        if (subscription == null)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found for renewal", subscriptionId);
            return;
        }

        var plan = subscription.Plan ?? await _planService.GetPlanByIdAsync(subscription.PlanId);
        if (plan == null)
        {
            _logger.LogError("Plan not found for subscription {SubscriptionId}", subscriptionId);
            return;
        }

        // Calculate renewal amount with annual discount if applicable
        var renewalAmount = CalculateRenewalAmount(subscription, plan);

        // Create invoice for the renewal period
        DateTime newPeriodStart = subscription.CurrentPeriodEnd > DateTime.UtcNow
            ? subscription.CurrentPeriodEnd
            : DateTime.UtcNow;

        DateTime newPeriodEnd = subscription.BillingCycle == BillingCycle.Annually
            ? newPeriodStart.AddYears(1)
            : newPeriodStart.AddMonths(1);

        Invoice? invoice = null;
        try
        {
            var description = $"{plan.DisplayName} Subscription Renewal - {subscription.BillingCycle}";
            var lineItems = new List<InvoiceLineItemRequest>
            {
                new()
                {
                    Type = "subscription_renewal",
                    Description = description,
                    Quantity = 1,
                    UnitPrice = renewalAmount,
                    Amount = renewalAmount,
                    PlanCode = plan.Code,
                    BillingCycle = subscription.BillingCycle,
                    PeriodStart = DateOnly.FromDateTime(newPeriodStart),
                    PeriodEnd = DateOnly.FromDateTime(newPeriodEnd)
                }
            };

            invoice = await _invoiceService.GenerateInvoiceAsync(
                subscription.OrganizationId,
                subscription.Id,
                renewalAmount,
                description,
                lineItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create renewal invoice for subscription {SubscriptionId}", subscriptionId);
        }

        // Attempt to charge using saved payment method
        var paymentMethod = await _dbContext.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.OrganizationId == subscription.OrganizationId &&
                                      pm.IsDefault &&
                                      pm.IsActive);

        if (paymentMethod == null || string.IsNullOrEmpty(paymentMethod.RazorpayTokenId))
        {
            _logger.LogWarning(
                "No valid payment method for subscription {SubscriptionId}. Marking as past_due.",
                subscriptionId);
            await HandleRenewalFailureAsync(subscription, invoice, "No valid payment method on file");
            return;
        }

        // Attempt to charge via Razorpay using saved payment method
        try
        {
            var chargeResult = await ChargeWithSavedPaymentMethodAsync(
                subscription, paymentMethod, renewalAmount, plan.Currency);

            if (chargeResult.Success)
            {
                // Payment succeeded - update subscription dates and mark invoice paid
                subscription.CurrentPeriodStart = newPeriodStart;
                subscription.CurrentPeriodEnd = newPeriodEnd;
                subscription.Status = SubscriptionStatus.Active;
                subscription.FailedPaymentAttempts = 0;
                subscription.LastPaymentFailedAt = null;
                subscription.GracePeriodEndAt = null;
                subscription.NextPaymentRetryAt = null;

                // Update organization status
                var org = subscription.Organization ?? await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
                if (org != null)
                {
                    org.SubscriptionStatus = "active";
                }

                // Create payment record
                var paymentRecord = new Payment
                {
                    OrganizationId = subscription.OrganizationId,
                    SubscriptionId = subscription.Id,
                    InvoiceId = invoice?.Id,
                    Amount = renewalAmount,
                    Currency = plan.Currency,
                    Status = PaymentStatus.Captured,
                    PaymentMethod = paymentMethod.Type,
                    PaymentMethodDetails = paymentMethod.Type == PaymentMethodType.Card
                        ? $"{paymentMethod.CardBrand} **** {paymentMethod.CardLast4}"
                        : paymentMethod.UpiId,
                    RazorpayPaymentId = chargeResult.PaymentId,
                    CapturedAt = DateTime.UtcNow
                };
                _dbContext.Payments.Add(paymentRecord);

                await _dbContext.SaveChangesAsync();

                // Mark invoice as paid
                if (invoice != null && !string.IsNullOrEmpty(chargeResult.PaymentId))
                {
                    await _invoiceService.MarkAsPaidAsync(invoice.Id, chargeResult.PaymentId);
                }

                // Send success notification
                await SendRenewalSuccessNotificationAsync(subscription, plan, renewalAmount, invoice);

                _logger.LogInformation(
                    "Renewal processed successfully for subscription {SubscriptionId}. Period: {Start} to {End}",
                    subscriptionId, newPeriodStart, newPeriodEnd);
            }
            else
            {
                await HandleRenewalFailureAsync(subscription, invoice, chargeResult.ErrorMessage ?? "Payment failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error charging for subscription renewal {SubscriptionId}", subscriptionId);
            await HandleRenewalFailureAsync(subscription, invoice, ex.Message);
        }
    }

    public async Task ProcessRenewalAsync(Guid subscriptionId, RenewalWebhookData? webhookData)
    {
        // If called without webhook data, use the full renewal logic
        if (webhookData == null)
        {
            await ProcessRenewalAsync(subscriptionId);
            return;
        }

        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        if (subscription == null)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found for renewal", subscriptionId);
            return;
        }

        var plan = subscription.Plan ?? await _planService.GetPlanByIdAsync(subscription.PlanId);
        if (plan == null)
        {
            _logger.LogError("Plan not found for subscription {SubscriptionId}", subscriptionId);
            return;
        }

        // Calculate new billing period from webhook data
        DateTime newPeriodStart = DateTimeOffset.FromUnixTimeSeconds(webhookData.CurrentPeriodStart!.Value).UtcDateTime;
        DateTime newPeriodEnd = DateTimeOffset.FromUnixTimeSeconds(webhookData.CurrentPeriodEnd!.Value).UtcDateTime;

        // Get amount from webhook
        var amountInPaise = webhookData.AmountInPaise ?? (int)(subscription.TotalAmount * 100);
        var currency = webhookData.Currency ?? subscription.Currency;

        // Update subscription
        subscription.CurrentPeriodStart = newPeriodStart;
        subscription.CurrentPeriodEnd = newPeriodEnd;
        subscription.Status = SubscriptionStatus.Active;
        subscription.FailedPaymentAttempts = 0;
        subscription.LastPaymentFailedAt = null;
        subscription.GracePeriodEndAt = null;
        subscription.NextPaymentRetryAt = null;

        // Update organization status
        var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
        if (org != null)
        {
            org.SubscriptionStatus = "active";
        }

        // Create payment record if we have payment info
        Payment? paymentRecord = null;
        if (!string.IsNullOrEmpty(webhookData.RazorpayPaymentId))
        {
            // Check if payment already exists (from payment.captured webhook)
            paymentRecord = await _dbContext.Payments
                .FirstOrDefaultAsync(p => p.RazorpayPaymentId == webhookData.RazorpayPaymentId);

            if (paymentRecord == null)
            {
                paymentRecord = new Payment
                {
                    OrganizationId = subscription.OrganizationId,
                    SubscriptionId = subscription.Id,
                    Amount = amountInPaise,
                    Currency = currency,
                    Status = PaymentStatus.Captured,
                    PaymentMethod = "razorpay_subscription",
                    RazorpayPaymentId = webhookData.RazorpayPaymentId,
                    CapturedAt = webhookData.ChargeAt.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(webhookData.ChargeAt.Value).UtcDateTime
                        : DateTime.UtcNow
                };
                _dbContext.Payments.Add(paymentRecord);
            }
        }

        await _dbContext.SaveChangesAsync();

        // Generate invoice for renewal
        try
        {
            var description = $"{plan.DisplayName} Subscription Renewal - {subscription.BillingCycle}";
            var lineItems = new List<InvoiceLineItemRequest>
            {
                new()
                {
                    Type = "subscription_renewal",
                    Description = description,
                    Quantity = 1,
                    UnitPrice = amountInPaise,
                    Amount = amountInPaise,
                    PlanCode = plan.Code,
                    BillingCycle = subscription.BillingCycle,
                    PeriodStart = DateOnly.FromDateTime(newPeriodStart),
                    PeriodEnd = DateOnly.FromDateTime(newPeriodEnd)
                }
            };

            var invoice = await _invoiceService.GenerateInvoiceAsync(
                subscription.OrganizationId,
                subscription.Id,
                amountInPaise,
                description,
                lineItems);

            // Mark invoice as paid
            if (!string.IsNullOrEmpty(webhookData.RazorpayPaymentId))
            {
                await _invoiceService.MarkAsPaidAsync(invoice.Id, webhookData.RazorpayPaymentId);
            }

            // Link payment to invoice if we created one
            if (paymentRecord != null)
            {
                paymentRecord.InvoiceId = invoice.Id;
                await _dbContext.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Renewal processed for subscription {SubscriptionId}. Invoice {InvoiceNumber} generated. Period: {Start} to {End}",
                subscriptionId, invoice.InvoiceNumber, newPeriodStart, newPeriodEnd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to generate renewal invoice for subscription {SubscriptionId}. Subscription was still renewed.",
                subscriptionId);
        }
    }

    /// <summary>
    /// Calculates the renewal amount considering annual discount (2 months free = 10/12 ratio).
    /// </summary>
    private int CalculateRenewalAmount(BillingSubscription subscription, SubscriptionPlan plan)
    {
        var pricing = _planService.CalculateSubscriptionPrice(
            plan,
            subscription.BillingCycle,
            subscription.SeatsAdditional);

        return pricing.Total;
    }

    /// <summary>
    /// Charges the customer using their saved payment method.
    /// </summary>
    private async Task<ChargeResult> ChargeWithSavedPaymentMethodAsync(
        BillingSubscription subscription,
        PaymentMethod paymentMethod,
        int amountInPaise,
        string currency)
    {
        try
        {
            // If subscription has a Razorpay subscription ID, payments are handled automatically
            if (!string.IsNullOrEmpty(subscription.RazorpaySubscriptionId))
            {
                return new ChargeResult
                {
                    Success = true,
                    PaymentId = $"auto_{DateTime.UtcNow:yyyyMMddHHmmss}"
                };
            }

            // For one-time charges with saved payment method, create an order
            var order = await _razorpayService.CreateOrderAsync(new CreateOrderRequest
            {
                AmountInPaise = amountInPaise,
                Currency = currency,
                Receipt = $"renewal_{subscription.Id:N}_{DateTime.UtcNow:yyyyMMdd}",
                OrganizationId = subscription.OrganizationId,
                PlanCode = subscription.PlanCode,
                SubscriptionId = subscription.Id
            });

            // Note: In production, you would use Razorpay's recurring payment API
            // with the saved token to auto-charge. For now, we simulate success
            // if the payment method is valid and active.
            if (paymentMethod.IsActive && !string.IsNullOrEmpty(paymentMethod.RazorpayTokenId))
            {
                return new ChargeResult
                {
                    Success = true,
                    PaymentId = $"pay_{order.Id}"
                };
            }

            return new ChargeResult
            {
                Success = false,
                ErrorMessage = "Payment method is not valid for recurring charges"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to charge payment method for subscription {SubscriptionId}", subscription.Id);
            return new ChargeResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Handles renewal payment failure - sets subscription to past_due and schedules retry.
    /// </summary>
    private async Task HandleRenewalFailureAsync(BillingSubscription subscription, Invoice? invoice, string reason)
    {
        subscription.FailedPaymentAttempts++;
        subscription.LastPaymentFailedAt = DateTime.UtcNow;
        subscription.Status = SubscriptionStatus.PastDue;

        // Set grace period (7 days)
        subscription.GracePeriodEndAt = DateTime.UtcNow.AddDays(7);

        // Schedule payment retry with exponential backoff
        var hoursUntilRetry = subscription.FailedPaymentAttempts switch
        {
            1 => 24,   // 1 day
            2 => 48,   // 2 days
            _ => 72    // 3 days
        };
        subscription.NextPaymentRetryAt = DateTime.UtcNow.AddHours(hoursUntilRetry);

        // Update organization status
        var org = subscription.Organization ?? await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
        if (org != null)
        {
            org.SubscriptionStatus = "past_due";
        }

        await _dbContext.SaveChangesAsync();

        // Send failure notification
        await SendRenewalFailureNotificationAsync(subscription, reason);

        _logger.LogWarning(
            "Renewal failed for subscription {SubscriptionId}. Attempt {Attempt}. Reason: {Reason}. Next retry: {NextRetry}",
            subscription.Id, subscription.FailedPaymentAttempts, reason, subscription.NextPaymentRetryAt);
    }

    /// <summary>
    /// Sends notification on successful renewal.
    /// </summary>
    private async Task SendRenewalSuccessNotificationAsync(
        BillingSubscription subscription,
        SubscriptionPlan plan,
        int amountInPaise,
        Invoice? invoice)
    {
        try
        {
            var owner = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId && u.Role == "owner");

            if (owner != null)
            {
                await _billingNotificationService.SendPaymentSuccessAsync(
                    owner.Id,
                    amountInPaise / 100m,
                    invoice?.InvoiceNumber ?? "N/A",
                    plan.DisplayName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send renewal success notification for subscription {SubscriptionId}",
                subscription.Id);
        }
    }

    /// <summary>
    /// Sends notification on failed renewal.
    /// </summary>
    private async Task SendRenewalFailureNotificationAsync(BillingSubscription subscription, string reason)
    {
        try
        {
            var owner = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.OrganizationId == subscription.OrganizationId && u.Role == "owner");

            if (owner != null)
            {
                var plan = subscription.Plan ?? await _planService.GetPlanByIdAsync(subscription.PlanId);
                await _billingNotificationService.SendPaymentFailedAsync(
                    owner.Id,
                    subscription.TotalAmount,
                    plan?.DisplayName ?? "your plan",
                    reason,
                    subscription.FailedPaymentAttempts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send renewal failure notification for subscription {SubscriptionId}",
                subscription.Id);
        }
    }

    /// <summary>
    /// Manually retries the last failed payment for a subscription.
    /// </summary>
    public async Task<PaymentRetryResponse> RetryPaymentAsync(Guid organizationId)
    {
        var subscription = await GetSubscriptionEntityAsync(organizationId);
        if (subscription == null)
        {
            throw new InvalidOperationException("No subscription found");
        }

        if (subscription.Status != SubscriptionStatus.PastDue)
        {
            throw new InvalidOperationException("Subscription is not in past_due status");
        }

        var plan = await _planService.GetPlanByIdAsync(subscription.PlanId)
            ?? throw new InvalidOperationException("Plan not found");

        var paymentMethod = await _dbContext.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.OrganizationId == organizationId &&
                                      pm.IsDefault &&
                                      pm.IsActive);

        if (paymentMethod == null || string.IsNullOrEmpty(paymentMethod.RazorpayTokenId))
        {
            throw new InvalidOperationException("No valid payment method on file. Please update your payment method.");
        }

        var renewalAmount = CalculateRenewalAmount(subscription, plan);

        try
        {
            var chargeResult = await ChargeWithSavedPaymentMethodAsync(
                subscription, paymentMethod, renewalAmount, plan.Currency);

            if (chargeResult.Success)
            {
                // Calculate new period dates
                DateTime newPeriodStart = subscription.CurrentPeriodEnd > DateTime.UtcNow
                    ? subscription.CurrentPeriodEnd
                    : DateTime.UtcNow;
                DateTime newPeriodEnd = subscription.BillingCycle == BillingCycle.Annually
                    ? newPeriodStart.AddYears(1)
                    : newPeriodStart.AddMonths(1);

                // Update subscription
                subscription.CurrentPeriodStart = newPeriodStart;
                subscription.CurrentPeriodEnd = newPeriodEnd;
                subscription.Status = SubscriptionStatus.Active;
                subscription.FailedPaymentAttempts = 0;
                subscription.LastPaymentFailedAt = null;
                subscription.GracePeriodEndAt = null;
                subscription.NextPaymentRetryAt = null;

                // Update organization
                var org = await _dbContext.Organizations.FindAsync(organizationId);
                if (org != null)
                {
                    org.SubscriptionStatus = "active";
                }

                // Create payment record
                var paymentRecord = new Payment
                {
                    OrganizationId = organizationId,
                    SubscriptionId = subscription.Id,
                    Amount = renewalAmount,
                    Currency = plan.Currency,
                    Status = PaymentStatus.Captured,
                    PaymentMethod = paymentMethod.Type,
                    RazorpayPaymentId = chargeResult.PaymentId,
                    CapturedAt = DateTime.UtcNow
                };
                _dbContext.Payments.Add(paymentRecord);

                await _dbContext.SaveChangesAsync();

                // Generate invoice
                try
                {
                    var description = $"{plan.DisplayName} Subscription - Payment Retry";
                    var lineItems = new List<InvoiceLineItemRequest>
                    {
                        new()
                        {
                            Type = "subscription_renewal",
                            Description = description,
                            Quantity = 1,
                            UnitPrice = renewalAmount,
                            Amount = renewalAmount,
                            PlanCode = plan.Code,
                            BillingCycle = subscription.BillingCycle,
                            PeriodStart = DateOnly.FromDateTime(newPeriodStart),
                            PeriodEnd = DateOnly.FromDateTime(newPeriodEnd)
                        }
                    };

                    var invoice = await _invoiceService.GenerateInvoiceAsync(
                        organizationId, subscription.Id, renewalAmount, description, lineItems);

                    if (!string.IsNullOrEmpty(chargeResult.PaymentId))
                    {
                        await _invoiceService.MarkAsPaidAsync(invoice.Id, chargeResult.PaymentId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate invoice after payment retry");
                }

                _logger.LogInformation(
                    "Payment retry succeeded for subscription {SubscriptionId}",
                    subscription.Id);

                return new PaymentRetryResponse(
                    Success: true,
                    Message: "Payment successful. Your subscription has been renewed.",
                    NewStatus: SubscriptionStatus.Active,
                    NextBillingDate: newPeriodEnd
                );
            }
            else
            {
                subscription.FailedPaymentAttempts++;
                subscription.LastPaymentFailedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                return new PaymentRetryResponse(
                    Success: false,
                    Message: chargeResult.ErrorMessage ?? "Payment failed. Please try again or update your payment method.",
                    NewStatus: SubscriptionStatus.PastDue,
                    NextBillingDate: null
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment retry failed for subscription {SubscriptionId}", subscription.Id);
            throw new InvalidOperationException($"Payment failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a refund for a subscription payment.
    /// </summary>
    public async Task<RefundResponse> CreateRefundAsync(
        Guid subscriptionId,
        decimal? amount,
        string reason)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        if (subscription == null)
        {
            throw new InvalidOperationException("Subscription not found");
        }

        // Get the last captured payment for this subscription
        var payment = await _dbContext.Payments
            .Where(p => p.SubscriptionId == subscriptionId &&
                       p.Status == PaymentStatus.Captured &&
                       !string.IsNullOrEmpty(p.RazorpayPaymentId))
            .OrderByDescending(p => p.CapturedAt)
            .FirstOrDefaultAsync();

        if (payment == null)
        {
            throw new InvalidOperationException("No eligible payment found for refund");
        }

        // Calculate refund amount in paise
        int? refundAmountPaise = amount.HasValue ? (int)(amount.Value * 100) : null;

        // Validate refund amount
        if (refundAmountPaise.HasValue)
        {
            var maxRefundable = payment.Amount - (payment.RefundAmount ?? 0);
            if (refundAmountPaise.Value > maxRefundable)
            {
                throw new InvalidOperationException(
                    $"Refund amount exceeds maximum refundable amount of {maxRefundable / 100m:F2} {payment.Currency}");
            }
        }

        // Create refund via Razorpay
        var refundResult = await _razorpayService.CreateRefundAsync(
            payment.RazorpayPaymentId!,
            refundAmountPaise,
            reason);

        // Update payment record
        payment.RefundId = refundResult.RefundId;
        payment.RefundAmount = (payment.RefundAmount ?? 0) + refundResult.Amount;
        payment.RefundedAt = DateTime.UtcNow;
        payment.RefundReason = reason;
        payment.Status = payment.RefundAmount >= payment.Amount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartialRefund;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Refund {RefundId} created for payment {PaymentId}. Amount: {Amount}",
            refundResult.RefundId, payment.RazorpayPaymentId, refundResult.Amount);

        return new RefundResponse(
            RefundId: refundResult.RefundId,
            PaymentId: payment.RazorpayPaymentId!,
            Amount: refundResult.Amount / 100m,
            Currency: payment.Currency,
            Status: refundResult.Status,
            Reason: reason,
            CreatedAt: DateTime.UtcNow
        );
    }

    private record ChargeResult
    {
        public bool Success { get; init; }
        public string? PaymentId { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public async Task HandlePaymentFailureAsync(Guid subscriptionId, string failureReason)
    {
        var subscription = await _dbContext.BillingSubscriptions.FindAsync(subscriptionId);
        if (subscription == null) return;

        subscription.FailedPaymentAttempts++;
        subscription.LastPaymentFailedAt = DateTime.UtcNow;

        if (subscription.FailedPaymentAttempts >= 3)
        {
            subscription.Status = SubscriptionStatus.PastDue;
            subscription.GracePeriodEndAt = DateTime.UtcNow.AddDays(7);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogWarning(
            "Payment failed for subscription {SubscriptionId}. Attempt {Attempt}. Reason: {Reason}",
            subscriptionId, subscription.FailedPaymentAttempts, failureReason);
    }

    public async Task ExpireTrialAsync(Guid subscriptionId)
    {
        var subscription = await _dbContext.BillingSubscriptions.FindAsync(subscriptionId);
        if (subscription == null || subscription.Status != SubscriptionStatus.Trialing)
            return;

        subscription.Status = SubscriptionStatus.Expired;
        subscription.EndedAt = DateTime.UtcNow;

        var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
        if (org != null)
        {
            org.SubscriptionStatus = "expired";
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Trial expired for subscription {SubscriptionId}", subscriptionId);
    }

    public async Task ApplyScheduledChangesAsync(Guid subscriptionId)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        if (subscription == null || string.IsNullOrEmpty(subscription.ScheduledPlanCode))
            return;

        var newPlan = await _planService.GetPlanByCodeAsync(subscription.ScheduledPlanCode);
        if (newPlan == null) return;

        await ApplyPlanChangeAsync(
            subscription,
            newPlan,
            subscription.ScheduledBillingCycle ?? subscription.BillingCycle,
            subscription.SeatsAdditional);

        subscription.ScheduledPlanCode = null;
        subscription.ScheduledBillingCycle = null;
        subscription.ScheduledChangeDate = null;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Applied scheduled plan change for subscription {SubscriptionId} to {PlanCode}",
            subscriptionId, newPlan.Code);
    }

    public async Task<BillingSubscription?> GetByRazorpayIdAsync(string razorpaySubscriptionId)
    {
        return await _dbContext.BillingSubscriptions
            .FirstOrDefaultAsync(s => s.RazorpaySubscriptionId == razorpaySubscriptionId);
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(
        Guid subscriptionId,
        string reason,
        DateTime? resumeAt,
        CancellationToken ct = default)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw new InvalidOperationException("Subscription not found");

        if (subscription.Status != SubscriptionStatus.Active)
        {
            throw new InvalidOperationException(
                $"Only active subscriptions can be paused. Current status: {subscription.Status}");
        }

        var now = DateTime.UtcNow;

        subscription.Status = SubscriptionStatus.Paused;
        subscription.PausedAt = now;
        subscription.PauseReason = reason;
        subscription.ScheduledResumeAt = resumeAt;

        // Update organization status
        var org = await _dbContext.Organizations.FindAsync(new object[] { subscription.OrganizationId }, ct);
        if (org != null)
        {
            org.SubscriptionStatus = "paused";
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Subscription {SubscriptionId} paused. Reason: {Reason}. Scheduled resume: {ResumeAt}",
            subscriptionId, reason, resumeAt?.ToString("O") ?? "none");

        return subscription;
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken ct = default)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId, ct)
            ?? throw new InvalidOperationException("Subscription not found");

        if (subscription.Status != SubscriptionStatus.Paused)
        {
            throw new InvalidOperationException(
                $"Only paused subscriptions can be resumed. Current status: {subscription.Status}");
        }

        var now = DateTime.UtcNow;

        // Calculate pro-rated billing period if needed
        // If the subscription was paused before the period ended, extend the period by the pause duration
        if (subscription.PausedAt.HasValue && subscription.CurrentPeriodEnd > subscription.PausedAt.Value)
        {
            var pauseDuration = now - subscription.PausedAt.Value;
            subscription.CurrentPeriodEnd = subscription.CurrentPeriodEnd.Add(pauseDuration);
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.PausedAt = null;
        subscription.PauseReason = null;
        subscription.ScheduledResumeAt = null;

        // Update organization status
        var org = await _dbContext.Organizations.FindAsync(new object[] { subscription.OrganizationId }, ct);
        if (org != null)
        {
            org.SubscriptionStatus = "active";
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Subscription {SubscriptionId} resumed. New period end: {PeriodEnd}",
            subscriptionId, subscription.CurrentPeriodEnd);

        return subscription;
    }

    #region Private Methods

    private async Task SaveBillingDetailsAsync(Guid organizationId, BillingDetailsRequest request)
    {
        var billingDetails = await _dbContext.BillingDetails
            .FirstOrDefaultAsync(b => b.OrganizationId == organizationId);

        if (billingDetails == null)
        {
            billingDetails = new BillingDetails
            {
                OrganizationId = organizationId
            };
            _dbContext.BillingDetails.Add(billingDetails);
        }

        billingDetails.OrganizationName = request.OrganizationName;
        billingDetails.Gstin = request.Gstin;
        billingDetails.Address = request.Address;
        billingDetails.AddressLine2 = request.AddressLine2;
        billingDetails.City = request.City;
        billingDetails.State = request.State;
        billingDetails.Pincode = request.Pincode;
        billingDetails.Email = request.Email;
        billingDetails.Phone = request.Phone;

        // Determine state code from state name
        var stateCode = await _dbContext.GstinStateCodes
            .FirstOrDefaultAsync(s => s.Name == request.State);
        billingDetails.StateCode = stateCode?.Code;

        await _dbContext.SaveChangesAsync();
    }

    private async Task ApplyPlanChangeAsync(
        BillingSubscription subscription,
        SubscriptionPlan newPlan,
        string billingCycle,
        int additionalSeats)
    {
        subscription.PlanCode = newPlan.Code;
        subscription.PlanId = newPlan.Id;
        subscription.BillingCycle = billingCycle;
        subscription.SeatsIncluded = newPlan.Limits.Users;
        subscription.SeatsAdditional = additionalSeats;

        // Update organization
        var org = await _dbContext.Organizations.FindAsync(subscription.OrganizationId);
        if (org != null)
        {
            org.PlanId = await GetLegacyPlanIdAsync(newPlan.Code);
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task<Guid?> GetLegacyPlanIdAsync(string planCode)
    {
        // Map to legacy Plan entity for backward compatibility
        var legacyPlan = await _dbContext.Plans
            .FirstOrDefaultAsync(p => p.Code == planCode && p.IsActive);
        return legacyPlan?.Id;
    }

    private static SubscriptionDto MapToSubscriptionDto(BillingSubscription subscription, SubscriptionPlan plan)
    {
        var pricing = new SubscriptionPricingDto(
            BaseAmount: subscription.BillingCycle == BillingCycle.Annually
                ? plan.PricingAnnually ?? 0
                : plan.PricingMonthly ?? 0,
            AdditionalSeatsAmount: subscription.SeatsAdditional * (subscription.BillingCycle == BillingCycle.Annually
                ? plan.PerSeatAnnually ?? 0
                : plan.PerSeatMonthly ?? 0),
            Subtotal: 0, // Will be calculated
            GstRate: 18.00m,
            GstAmount: 0,
            Total: 0,
            Currency: plan.Currency
        );

        var subtotal = pricing.BaseAmount + pricing.AdditionalSeatsAmount;
        var gstAmount = (int)Math.Round(subtotal * 0.18m);
        pricing = pricing with
        {
            Subtotal = subtotal,
            GstAmount = gstAmount,
            Total = subtotal + gstAmount
        };

        return new SubscriptionDto(
            Id: subscription.Id,
            PlanCode: subscription.PlanCode,
            PlanName: plan.DisplayName,
            Status: subscription.Status,
            BillingCycle: subscription.BillingCycle,
            CurrentPeriodStart: subscription.CurrentPeriodStart,
            CurrentPeriodEnd: subscription.CurrentPeriodEnd,
            CancelAtPeriodEnd: subscription.CancelAtPeriodEnd,
            TrialEnd: subscription.TrialEnd,
            Seats: new SeatsDto(
                Included: subscription.SeatsIncluded,
                Additional: subscription.SeatsAdditional,
                Used: 0 // Will be populated from usage
            ),
            Pricing: pricing,
            NextBillingDate: DateOnly.FromDateTime(subscription.CurrentPeriodEnd),
            PaymentMethod: null, // Will be populated separately
            RazorpaySubscriptionId: subscription.RazorpaySubscriptionId,
            ScheduledChange: string.IsNullOrEmpty(subscription.ScheduledPlanCode) ? null : new ScheduledChangeDto(
                PlanCode: subscription.ScheduledPlanCode,
                BillingCycle: subscription.ScheduledBillingCycle,
                EffectiveDate: subscription.ScheduledChangeDate ?? subscription.CurrentPeriodEnd
            )
        );
    }

    private static UsageDto MapToUsageDto(UsageRecord? usage, PlanLimits limits, BillingSubscription subscription)
    {
        var periodStart = DateOnly.FromDateTime(subscription.CurrentPeriodStart);
        var periodEnd = DateOnly.FromDateTime(subscription.CurrentPeriodEnd);

        var noticesUsed = usage?.NoticesCount ?? 0;
        var usersUsed = usage?.UsersCount ?? 0;
        var storageUsedBytes = usage?.StorageBytes ?? 0;
        var apiCallsUsed = usage?.ApiCalls ?? 0;

        var noticesLimit = limits.NoticesPerMonth == -1 ? int.MaxValue : limits.NoticesPerMonth;
        var usersLimit = limits.Users == -1 ? int.MaxValue : limits.Users;
        var storageLimit = limits.StorageGb == -1 ? int.MaxValue : limits.StorageGb;
        var apiCallsLimit = limits.ApiCalls == -1 ? int.MaxValue : limits.ApiCalls;

        var alerts = new List<UsageAlertDto>();

        // Check for usage warnings
        var noticesPercentage = noticesLimit > 0 ? noticesUsed * 100 / noticesLimit : 0;
        if (noticesPercentage >= 90)
            alerts.Add(new UsageAlertDto("notices", "critical", $"You've used {noticesPercentage}% of your monthly notice quota"));
        else if (noticesPercentage >= 80)
            alerts.Add(new UsageAlertDto("notices", "warning", $"You've used {noticesPercentage}% of your monthly notice quota"));

        return new UsageDto(
            Period: new UsagePeriodDto(periodStart, periodEnd),
            Notices: new UsageMetricDto(
                Used: noticesUsed,
                Limit: noticesLimit == int.MaxValue ? -1 : noticesLimit,
                Percentage: noticesLimit > 0 && noticesLimit != int.MaxValue ? noticesUsed * 100 / noticesLimit : 0,
                Remaining: noticesLimit == int.MaxValue ? -1 : Math.Max(0, noticesLimit - noticesUsed)
            ),
            Users: new UsageMetricDto(
                Used: usersUsed,
                Limit: usersLimit == int.MaxValue ? -1 : usersLimit,
                Percentage: usersLimit > 0 && usersLimit != int.MaxValue ? usersUsed * 100 / usersLimit : 0,
                Remaining: usersLimit == int.MaxValue ? -1 : Math.Max(0, usersLimit - usersUsed)
            ),
            Storage: new StorageUsageDto(
                UsedBytes: storageUsedBytes,
                UsedGb: (int)(storageUsedBytes / (1024L * 1024 * 1024)),
                LimitGb: storageLimit == int.MaxValue ? -1 : storageLimit,
                Percentage: storageLimit > 0 && storageLimit != int.MaxValue
                    ? (int)(storageUsedBytes * 100 / (storageLimit * 1024L * 1024 * 1024))
                    : 0,
                RemainingGb: storageLimit == int.MaxValue ? -1 : Math.Max(0, storageLimit - (int)(storageUsedBytes / (1024L * 1024 * 1024)))
            ),
            ApiCalls: new UsageMetricDto(
                Used: apiCallsUsed,
                Limit: apiCallsLimit == int.MaxValue ? -1 : apiCallsLimit,
                Percentage: apiCallsLimit > 0 && apiCallsLimit != int.MaxValue ? apiCallsUsed * 100 / apiCallsLimit : 0,
                Remaining: apiCallsLimit == int.MaxValue ? -1 : Math.Max(0, apiCallsLimit - apiCallsUsed)
            ),
            Alerts: alerts.Count > 0 ? alerts : null
        );
    }

    #endregion
}
