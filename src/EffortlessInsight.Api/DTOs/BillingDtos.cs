namespace EffortlessInsight.Api.DTOs;

// ============================================================================
// Plan DTOs
// ============================================================================

public record PlanDto(
    Guid Id,
    string Code,
    string Name,
    string DisplayName,
    string? Description,
    PlanPricingDto Pricing,
    PlanLimitsDto Limits,
    List<string> Features,
    bool IsPopular,
    int TrialDays,
    bool ContactSales
);

public record PlanPricingDto(
    int? Monthly,
    int? Annually,
    string Currency,
    int? AnnualDiscount,
    PerSeatPricingDto? PerSeat
);

public record PerSeatPricingDto(
    int? Monthly,
    int? Annually
);

public record PlanLimitsDto(
    int NoticesPerMonth,
    int Users,
    int StorageGb,
    int OrganizationsCount,
    bool AdditionalUsersAllowed,
    int ApiCalls
);

public record PlansListResponse(
    List<PlanDto> Plans,
    List<AddOnDto>? AddOns
);

public record AddOnDto(
    string Id,
    string Name,
    string Description,
    int Price,
    string Period
);

// ============================================================================
// Subscription DTOs
// ============================================================================

public record CurrentSubscriptionResponse(
    SubscriptionDto Subscription,
    UsageDto Usage
);

public record SubscriptionDto(
    Guid Id,
    string PlanCode,
    string PlanName,
    string Status,
    string BillingCycle,
    DateTime CurrentPeriodStart,
    DateTime CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTime? TrialEnd,
    SeatsDto Seats,
    SubscriptionPricingDto Pricing,
    DateOnly NextBillingDate,
    PaymentMethodSummaryDto? PaymentMethod,
    string? RazorpaySubscriptionId,
    ScheduledChangeDto? ScheduledChange
);

public record SeatsDto(
    int Included,
    int Additional,
    int Used
);

public record SubscriptionPricingDto(
    int BaseAmount,
    int AdditionalSeatsAmount,
    int Subtotal,
    decimal GstRate,
    int GstAmount,
    int Total,
    string Currency
);

public record PaymentMethodSummaryDto(
    string Type,
    string? Last4,
    string? Brand,
    int? ExpiryMonth,
    int? ExpiryYear,
    string? UpiId
);

public record ScheduledChangeDto(
    string PlanCode,
    string? BillingCycle,
    DateTime EffectiveDate
);

public record UsageDto(
    UsagePeriodDto Period,
    UsageMetricDto Notices,
    UsageMetricDto Users,
    StorageUsageDto Storage,
    UsageMetricDto? ApiCalls,
    List<UsageAlertDto>? Alerts
);

public record UsagePeriodDto(
    DateOnly Start,
    DateOnly End
);

public record UsageMetricDto(
    int Used,
    int Limit,
    int Percentage,
    int Remaining
);

public record StorageUsageDto(
    long UsedBytes,
    int UsedGb,
    int LimitGb,
    int Percentage,
    int RemainingGb
);

public record UsageAlertDto(
    string Type,
    string Level,
    string Message
);

// ============================================================================
// Create Subscription DTOs
// ============================================================================

public record CreateSubscriptionRequest(
    string PlanCode,
    string BillingCycle,
    int AdditionalSeats,
    BillingDetailsRequest BillingDetails,
    string? CouponCode,
    bool AutoRenew
);

public record BillingDetailsRequest(
    string OrganizationName,
    string? Gstin,
    string Address,
    string? AddressLine2,
    string? City,
    string State,
    string Pincode,
    string? Email,
    string? Phone
);

public record CreateSubscriptionResponse(
    Guid SubscriptionId,
    RazorpayOrderDto RazorpayOrder,
    CheckoutOptionsDto CheckoutOptions
);

public record RazorpayOrderDto(
    string Id,
    int Amount,
    string Currency,
    string Receipt,
    string Key
);

public record CheckoutOptionsDto(
    string Key,
    int Amount,
    string Currency,
    string Name,
    string Description,
    string OrderId,
    CheckoutPrefillDto Prefill,
    CheckoutThemeDto Theme
);

public record CheckoutPrefillDto(
    string? Name,
    string? Email,
    string? Contact
);

public record CheckoutThemeDto(
    string Color
);

// ============================================================================
// Verify Payment DTOs
// ============================================================================

public record VerifyPaymentRequest(
    string RazorpayPaymentId,
    string RazorpayOrderId,
    string RazorpaySignature
);

public record VerifyPaymentResponse(
    bool Success,
    SubscriptionActivatedDto Subscription,
    InvoiceSummaryDto? Invoice
);

public record SubscriptionActivatedDto(
    Guid Id,
    string Status,
    string PlanCode,
    DateTime ActivatedAt
);

public record InvoiceSummaryDto(
    Guid Id,
    string Number,
    string DownloadUrl
);

// ============================================================================
// Plan Change DTOs
// ============================================================================

public record ChangePlanRequest(
    string NewPlanCode,
    string BillingCycle,
    int? AdditionalSeats,
    string EffectiveDate // "immediate" or "period_end"
);

public record ChangePlanResponse(
    string Type, // "upgrade" or "downgrade"
    int? ProrationAmount,
    int? NewPlanAmount,
    int? TotalDue,
    bool EffectiveImmediately,
    RazorpayOrderDto? RazorpayOrder,
    string? ScheduledPlanCode,
    DateTime? EffectiveDate,
    string? Message
);

// ============================================================================
// Cancel Subscription DTOs
// ============================================================================

public record CancelSubscriptionRequest(
    string Reason,
    string? Feedback,
    bool CancelImmediately
);

public record CancelSubscriptionResponse(
    SubscriptionCancelledDto Subscription,
    string Message
);

public record SubscriptionCancelledDto(
    Guid Id,
    string Status,
    bool CancelAtPeriodEnd,
    DateTime? CancellationDate
);

// ============================================================================
// Add Seats DTOs
// ============================================================================

public record AddSeatsRequest(
    int AdditionalSeats
);

public record AddSeatsResponse(
    int TotalSeats,
    int ProrationAmount,
    RazorpayOrderDto? RazorpayOrder
);

// ============================================================================
// Invoice DTOs
// ============================================================================

public record InvoiceListResponse(
    List<InvoiceDto> Invoices,
    BillingPaginationDto Pagination
);

public record InvoiceDto(
    Guid Id,
    string Number,
    DateOnly Date,
    DateOnly DueDate,
    string Status,
    int Subtotal,
    int Discount,
    int Tax,
    int Total,
    string Currency,
    string? Description,
    string PdfUrl
);

public record InvoiceDetailDto(
    Guid Id,
    string Number,
    DateOnly Date,
    DateOnly DueDate,
    string Status,
    int Subtotal,
    int Discount,
    string? DiscountDescription,
    decimal TaxRate,
    int TaxAmount,
    int? CgstAmount,
    int? SgstAmount,
    int? IgstAmount,
    int Total,
    int AmountPaid,
    int AmountDue,
    string Currency,
    string? Description,
    string HsnCode,
    string? PlaceOfSupply,
    bool IsInterState,
    InvoiceBillingDetailsDto BillingDetails,
    List<InvoiceLineItemDto> LineItems,
    string? Notes,
    string PdfUrl,
    DateTime? PaidAt
);

public record InvoiceBillingDetailsDto(
    string OrganizationName,
    string? Gstin,
    string Address,
    string? City,
    string State,
    string? StateCode,
    string Pincode,
    string Country,
    string? Email,
    string? Phone
);

public record InvoiceLineItemDto(
    string Type,
    string Description,
    int Quantity,
    int UnitPrice,
    int Amount,
    string? HsnCode,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd
);

public record BillingPaginationDto(
    int Page,
    int Limit,
    int Total,
    int TotalPages
);

// ============================================================================
// Coupon DTOs
// ============================================================================

public record ValidateCouponRequest(
    string Code,
    string PlanCode,
    string BillingCycle
);

public record ValidateCouponResponse(
    bool IsValid,
    string? ErrorMessage,
    CouponDetailsDto? Coupon
);

public record CouponDetailsDto(
    string Code,
    string? Description,
    string DiscountType,
    int DiscountValue,
    int? MaxDiscountAmount,
    int CalculatedDiscount
);

// ============================================================================
// Payment Method DTOs
// ============================================================================

public record PaymentMethodListResponse(
    List<PaymentMethodDto> PaymentMethods
);

public record PaymentMethodDto(
    Guid Id,
    string Type,
    bool IsDefault,
    string? CardLast4,
    string? CardBrand,
    int? CardExpiryMonth,
    int? CardExpiryYear,
    string? CardName,
    string? UpiId,
    DateTime? LastUsedAt
);

public record SetDefaultPaymentMethodRequest(
    Guid PaymentMethodId
);

// ============================================================================
// Analytics DTOs
// ============================================================================

public record BillingAnalyticsDto(
    decimal Mrr,
    decimal Arr,
    decimal ChurnRate,
    decimal TrialConversionRate,
    decimal NetRevenueRetention,
    int ActiveSubscriptions,
    int TrialingSubscriptions,
    int CancelledSubscriptions
);

// ============================================================================
// Webhook DTOs
// ============================================================================

public record RazorpayWebhookPayload
{
    public string? Event { get; init; }
    public WebhookPayloadData? Payload { get; init; }
    public string? AccountId { get; init; }
    public bool? Contains { get; init; }
    public long? CreatedAt { get; init; }
}

public record WebhookPayloadData
{
    public WebhookPayment? Payment { get; init; }
    public WebhookSubscription? Subscription { get; init; }
    public WebhookInvoice? Invoice { get; init; }
    public WebhookRefund? Refund { get; init; }
}

public record WebhookPayment
{
    public WebhookEntity? Entity { get; init; }
}

public record WebhookSubscription
{
    public WebhookEntity? Entity { get; init; }
}

public record WebhookInvoice
{
    public WebhookEntity? Entity { get; init; }
}

public record WebhookRefund
{
    public WebhookEntity? Entity { get; init; }
}

public record WebhookEntity
{
    public string? Id { get; init; }
    public string? Entity { get; init; }
    public int? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Status { get; init; }
    public string? OrderId { get; init; }
    public string? Method { get; init; }
    public string? Email { get; init; }
    public string? Contact { get; init; }
    public Dictionary<string, object>? Notes { get; init; }
    public long? CreatedAt { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorDescription { get; init; }
    public string? PlanId { get; init; }
    public string? CustomerId { get; init; }
    public int? TotalCount { get; init; }
    public int? PaidCount { get; init; }
    public long? ChargeAt { get; init; }
    public long? StartAt { get; init; }
    public long? EndAt { get; init; }
    public long? EndedAt { get; init; }
    public int? ShortUrl { get; init; }
}
