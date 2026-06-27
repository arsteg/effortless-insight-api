using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin controller for billing and subscription management.
/// </summary>
[Route("api/v1/admin/billing")]
[Authorize(Policy = "AdminFinance")]
public class AdminBillingController : AdminControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAdminAuditService _auditService;

    public AdminBillingController(
        ApplicationDbContext dbContext,
        IAdminAuditService auditService,
        ILogger<AdminBillingController> logger)
        : base(logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    /// <summary>
    /// Get billing overview metrics.
    /// </summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(BillingOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview([FromQuery] string period = "30d")
    {
        var (startDate, endDate) = GetPeriodDates(period);

        // Calculate MRR
        var activeSubscriptions = await _dbContext.BillingSubscriptions
            .Where(s => s.Status == "active")
            .Include(s => s.Plan)
            .ToListAsync();

        var mrr = activeSubscriptions.Sum(s =>
        {
            var basePricing = s.BillingCycle == "monthly"
                ? (s.Plan?.PricingMonthly ?? 0)
                : (s.Plan?.PricingAnnually ?? 0) / 12;

            var seatPricing = s.SeatsAdditional * (s.BillingCycle == "monthly"
                ? (s.Plan?.PerSeatMonthly ?? 0)
                : (s.Plan?.PerSeatAnnually ?? 0) / 12);

            return basePricing + seatPricing;
        });

        // Revenue collected in period
        var revenueCollected = await _dbContext.Payments
            .Where(p => p.Status == "captured" && p.CreatedAt >= startDate)
            .SumAsync(p => p.Amount);

        // Refunds in period
        var refundsIssued = await _dbContext.Payments
            .Where(p => p.Status == "refunded" && p.UpdatedAt >= startDate)
            .SumAsync(p => p.RefundAmount ?? 0);

        // Subscription counts by status
        var subscriptionStats = await _dbContext.BillingSubscriptions
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        // Trial to paid conversion
        var trialsStarted = await _dbContext.BillingSubscriptions
            .CountAsync(s => s.TrialStart >= startDate && s.TrialStart <= endDate);

        var trialsConverted = await _dbContext.BillingSubscriptions
            .CountAsync(s => s.TrialStart >= startDate && s.Status == "active");

        var conversionRate = trialsStarted > 0
            ? Math.Round((double)trialsConverted / trialsStarted * 100, 1)
            : 0;

        return Success(new BillingOverviewResponse
        {
            Period = new PeriodInfo { Start = startDate, End = endDate },
            Mrr = mrr,
            Arr = mrr * 12,
            RevenueCollected = revenueCollected,
            RefundsIssued = refundsIssued,
            ActiveSubscriptions = subscriptionStats.GetValueOrDefault("active", 0),
            TrialSubscriptions = subscriptionStats.GetValueOrDefault("trialing", 0),
            CancelledSubscriptions = subscriptionStats.GetValueOrDefault("cancelled", 0),
            TrialConversionRate = conversionRate
        });
    }

    /// <summary>
    /// List subscriptions with filtering.
    /// </summary>
    [HttpGet("subscriptions")]
    [ProducesResponseType(typeof(SubscriptionListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubscriptions([FromQuery] SubscriptionSearchRequest request)
    {
        var query = _dbContext.BillingSubscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .AsQueryable();

        // Apply status filter
        if (!string.IsNullOrEmpty(request.Status))
        {
            query = query.Where(s => s.Status == request.Status);
        }

        // Apply plan filter
        if (!string.IsNullOrEmpty(request.Plan))
        {
            query = query.Where(s => s.Plan != null && s.Plan.Code == request.Plan);
        }

        // Apply search
        if (!string.IsNullOrEmpty(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(s => s.Organization.Name.ToLower().Contains(searchLower));
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var subscriptions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new AdminSubscriptionListItem
            {
                Id = s.Id,
                OrganizationId = s.OrganizationId,
                OrganizationName = s.Organization.Name,
                PlanCode = s.Plan != null ? s.Plan.Code : "unknown",
                PlanName = s.Plan != null ? s.Plan.Name : "Unknown",
                Status = s.Status,
                BillingCycle = s.BillingCycle,
                CurrentPeriodEnd = s.CurrentPeriodEnd,
                SeatsTotal = s.SeatsIncluded + s.SeatsAdditional,
                CancelAtPeriodEnd = s.CancelAtPeriodEnd,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();

        return Success(new SubscriptionListResponse
        {
            Subscriptions = subscriptions,
            Pagination = new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        });
    }

    /// <summary>
    /// Get subscription details.
    /// </summary>
    [HttpGet("subscriptions/{subscriptionId:guid}")]
    [ProducesResponseType(typeof(AdminSubscriptionDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscription(Guid subscriptionId)
    {
        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Organization)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        if (subscription == null)
        {
            return NotFoundResponse("Subscription not found");
        }

        // Get payment history
        var payments = await _dbContext.Payments
            .Where(p => p.SubscriptionId == subscriptionId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .Select(p => new AdminPaymentInfo
            {
                Id = p.Id,
                Amount = p.Amount,
                Status = p.Status,
                Method = p.PaymentMethod,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        // Get invoices
        var invoices = await _dbContext.Invoices
            .Where(i => i.SubscriptionId == subscriptionId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(10)
            .Select(i => new AdminInvoiceSummary
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                Amount = i.Total,
                Status = i.Status,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return Success(new AdminSubscriptionDetail
        {
            Id = subscription.Id,
            OrganizationId = subscription.OrganizationId,
            OrganizationName = subscription.Organization.Name,
            Plan = subscription.Plan != null ? new AdminPlanInfo
            {
                Code = subscription.Plan.Code,
                Name = subscription.Plan.Name
            } : null,
            Status = subscription.Status,
            BillingCycle = subscription.BillingCycle,
            SeatsIncluded = subscription.SeatsIncluded,
            SeatsAdditional = subscription.SeatsAdditional,
            CurrentPeriodStart = subscription.CurrentPeriodStart,
            CurrentPeriodEnd = subscription.CurrentPeriodEnd,
            TrialStart = subscription.TrialStart,
            TrialEnd = subscription.TrialEnd,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            CancelledAt = subscription.CancelledAt,
            CancellationReason = subscription.CancellationReason,
            Payments = payments,
            Invoices = invoices,
            CreatedAt = subscription.CreatedAt
        });
    }

    /// <summary>
    /// Override subscription plan.
    /// </summary>
    [HttpPost("subscriptions/{subscriptionId:guid}/override-plan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OverridePlan(Guid subscriptionId, [FromBody] OverridePlanRequest request)
    {
        if (!HasPermission(AdminPermissions.BillingOverride))
        {
            return Forbid();
        }

        var subscription = await _dbContext.BillingSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

        if (subscription == null)
        {
            return NotFoundResponse("Subscription not found");
        }

        var newPlan = await _dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.Code == request.PlanCode);

        if (newPlan == null)
        {
            return Error("Invalid plan code", "INVALID_PLAN");
        }

        var oldPlanCode = subscription.Plan?.Code ?? "unknown";
        subscription.PlanId = newPlan.Id;

        // Handle proration option
        if (!request.Prorate)
        {
            // No proration - takes effect at period end
            subscription.ScheduledPlanCode = newPlan.Code;
            subscription.ScheduledChangeDate = subscription.CurrentPeriodEnd;
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.SubscriptionPlanOverridden,
            AuditTargetTypes.Subscription,
            subscriptionId.ToString(),
            $"Plan overridden from {oldPlanCode} to {request.PlanCode}",
            new Dictionary<string, object>
            {
                ["old_plan"] = oldPlanCode,
                ["new_plan"] = request.PlanCode,
                ["reason"] = request.Reason,
                ["prorate"] = request.Prorate
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success<object?>(null, "Plan overridden successfully");
    }

    /// <summary>
    /// Process refund.
    /// </summary>
    [HttpPost("payments/{paymentId:guid}/refund")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessRefund(Guid paymentId, [FromBody] RefundRequest request)
    {
        if (!HasPermission(AdminPermissions.BillingRefund))
        {
            return Forbid();
        }

        var payment = await _dbContext.Payments.FindAsync(paymentId);
        if (payment == null)
        {
            return NotFoundResponse("Payment not found");
        }

        if (payment.Status != "captured")
        {
            return Error("Payment cannot be refunded", "INVALID_PAYMENT_STATUS");
        }

        var refundAmount = request.Amount ?? payment.Amount;
        var currentRefunded = payment.RefundAmount ?? 0;
        if (refundAmount > payment.Amount - currentRefunded)
        {
            return Error("Refund amount exceeds available amount", "REFUND_EXCEEDS_AVAILABLE");
        }

        // In production, call Razorpay refund API
        // For now, update payment record

        payment.RefundAmount = currentRefunded + (int)refundAmount;
        payment.Status = payment.RefundAmount >= payment.Amount ? "refunded" : "partially_refunded";
        payment.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            CurrentAdminId,
            AdminAuditActions.RefundProcessed,
            AuditTargetTypes.Payment,
            paymentId.ToString(),
            $"Refund processed: ₹{refundAmount}",
            new Dictionary<string, object>
            {
                ["amount"] = refundAmount,
                ["reason"] = request.Reason,
                ["original_amount"] = payment.Amount
            },
            ClientIpAddress,
            ClientUserAgent,
            CurrentSessionId);

        return Success(new RefundResponse
        {
            RefundId = Guid.NewGuid().ToString(),
            Amount = refundAmount,
            Status = "processed"
        });
    }

    /// <summary>
    /// List invoices with filtering.
    /// </summary>
    [HttpGet("invoices")]
    [ProducesResponseType(typeof(InvoiceListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvoices([FromQuery] InvoiceSearchRequest request)
    {
        var query = _dbContext.Invoices
            .Include(i => i.Organization)
            .Include(i => i.Payments)
            .AsQueryable();

        // Apply status filter
        if (!string.IsNullOrEmpty(request.Status))
        {
            query = query.Where(i => i.Status == request.Status);
        }

        // Apply search
        if (!string.IsNullOrEmpty(request.Search))
        {
            var searchLower = request.Search.ToLower();
            query = query.Where(i =>
                i.InvoiceNumber.ToLower().Contains(searchLower) ||
                i.Organization.Name.ToLower().Contains(searchLower));
        }

        // Apply date range
        if (request.FromDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt <= request.ToDate.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var invoices = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new AdminInvoiceListItem
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                OrganizationId = i.OrganizationId,
                OrganizationName = i.Organization.Name,
                Subtotal = i.Subtotal,
                TaxAmount = i.TaxAmount,
                TotalAmount = i.Total,
                Status = i.Status,
                DueDate = i.DueDate.ToDateTime(TimeOnly.MinValue),
                PaidAt = i.PaidAt,
                CreatedAt = i.CreatedAt,
                // Get the successful payment ID for refund processing
                PaymentId = i.Payments
                    .Where(p => p.Status == "captured" || p.Status == "paid")
                    .OrderByDescending(p => p.CreatedAt)
                    .Select(p => p.Id)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Success(new InvoiceListResponse
        {
            Invoices = invoices,
            Pagination = new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        });
    }

    /// <summary>
    /// Download invoice as PDF.
    /// </summary>
    [HttpGet("invoices/{invoiceId:guid}/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInvoicePdf(Guid invoiceId)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.Organization)
            .Include(i => i.Subscription)
                .ThenInclude(s => s!.Plan)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null)
        {
            return NotFoundResponse("Invoice not found");
        }

        var pdfBytes = GenerateInvoicePdf(invoice);

        return File(pdfBytes, "application/pdf", $"invoice-{invoice.InvoiceNumber}.pdf");
    }

    private byte[] GenerateInvoicePdf(Invoice invoice)
    {
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);

        // Generate a simple text-based PDF (for proper PDF generation, use a library like QuestPDF)
        // This creates a minimal valid PDF structure
        writer.WriteLine("%PDF-1.4");
        writer.WriteLine("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj");
        writer.WriteLine("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj");

        // Build invoice content
        var content = new System.Text.StringBuilder();
        content.AppendLine($"INVOICE");
        content.AppendLine($"Invoice Number: {invoice.InvoiceNumber}");
        content.AppendLine($"Date: {invoice.CreatedAt:yyyy-MM-dd}");
        content.AppendLine($"Due Date: {invoice.DueDate:yyyy-MM-dd}");
        content.AppendLine();
        content.AppendLine($"Bill To:");
        content.AppendLine($"{invoice.Organization?.Name ?? "N/A"}");
        content.AppendLine();
        content.AppendLine("Items:");

        if (invoice.LineItems != null)
        {
            foreach (var item in invoice.LineItems)
            {
                content.AppendLine($"  {item.Description}: Rs. {item.Amount / 100m:N2}");
            }
        }

        content.AppendLine();
        content.AppendLine($"Subtotal: Rs. {invoice.Subtotal / 100m:N2}");
        content.AppendLine($"Tax (GST): Rs. {invoice.TaxAmount / 100m:N2}");
        content.AppendLine($"Total: Rs. {invoice.Total / 100m:N2}");
        content.AppendLine();
        content.AppendLine($"Status: {invoice.Status}");
        if (invoice.PaidAt.HasValue)
        {
            content.AppendLine($"Paid On: {invoice.PaidAt.Value:yyyy-MM-dd}");
        }

        var contentBytes = System.Text.Encoding.UTF8.GetBytes(content.ToString());
        var contentHex = BitConverter.ToString(contentBytes).Replace("-", "");

        // Page with text content
        writer.WriteLine($"3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj");

        // Content stream - simple text rendering
        var textContent = $"BT /F1 12 Tf 50 750 Td ({EscapePdfString(content.ToString().Replace("\r\n", ") Tj 0 -14 Td (").Replace("\n", ") Tj 0 -14 Td ("))}) Tj ET";
        var streamBytes = System.Text.Encoding.ASCII.GetBytes(textContent);
        writer.WriteLine($"4 0 obj << /Length {streamBytes.Length} >> stream");
        writer.Write(textContent);
        writer.WriteLine("\nendstream endobj");

        // Font
        writer.WriteLine("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Courier >> endobj");

        // Cross-reference table
        writer.WriteLine("xref");
        writer.WriteLine("0 6");
        writer.WriteLine("0000000000 65535 f ");
        writer.WriteLine("0000000009 00000 n ");
        writer.WriteLine("0000000058 00000 n ");
        writer.WriteLine("0000000115 00000 n ");
        writer.WriteLine("0000000270 00000 n ");
        writer.WriteLine("0000000500 00000 n ");

        // Trailer
        writer.WriteLine("trailer << /Size 6 /Root 1 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine("580");
        writer.WriteLine("%%EOF");

        writer.Flush();
        return stream.ToArray();
    }

    private static string EscapePdfString(string input)
    {
        return input
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("\r", "")
            .Replace("\t", "    ");
    }

    private static (DateTime Start, DateTime End) GetPeriodDates(string period)
    {
        var end = DateTime.UtcNow;
        var start = period switch
        {
            "7d" => end.AddDays(-7),
            "30d" => end.AddDays(-30),
            "90d" => end.AddDays(-90),
            "1y" => end.AddYears(-1),
            _ => end.AddDays(-30)
        };
        return (start, end);
    }
}

// DTOs

public record BillingOverviewResponse
{
    public PeriodInfo Period { get; init; } = new();
    public decimal Mrr { get; init; }
    public decimal Arr { get; init; }
    public decimal RevenueCollected { get; init; }
    public decimal RefundsIssued { get; init; }
    public int ActiveSubscriptions { get; init; }
    public int TrialSubscriptions { get; init; }
    public int CancelledSubscriptions { get; init; }
    public double TrialConversionRate { get; init; }
}

public record SubscriptionSearchRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? Plan { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public record SubscriptionListResponse
{
    public List<AdminSubscriptionListItem> Subscriptions { get; init; } = [];
    public PaginationInfo Pagination { get; init; } = new();
}

public record AdminSubscriptionListItem
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string OrganizationName { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string BillingCycle { get; init; } = string.Empty;
    public DateTime CurrentPeriodEnd { get; init; }
    public int SeatsTotal { get; init; }
    public bool CancelAtPeriodEnd { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record AdminSubscriptionDetail
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string OrganizationName { get; init; } = string.Empty;
    public AdminPlanInfo? Plan { get; init; }
    public string Status { get; init; } = string.Empty;
    public string BillingCycle { get; init; } = string.Empty;
    public int SeatsIncluded { get; init; }
    public int SeatsAdditional { get; init; }
    public DateTime CurrentPeriodStart { get; init; }
    public DateTime CurrentPeriodEnd { get; init; }
    public DateTime? TrialStart { get; init; }
    public DateTime? TrialEnd { get; init; }
    public bool CancelAtPeriodEnd { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CancellationReason { get; init; }
    public List<AdminPaymentInfo> Payments { get; init; } = [];
    public List<AdminInvoiceSummary> Invoices { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public record AdminPaymentInfo
{
    public Guid Id { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public record OverridePlanRequest
{
    public string PlanCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool Prorate { get; init; } = true;
}

public record RefundRequest
{
    public decimal? Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public record RefundResponse
{
    public string RefundId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Status { get; init; } = string.Empty;
}

public record InvoiceSearchRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public record InvoiceListResponse
{
    public List<AdminInvoiceListItem> Invoices { get; init; } = [];
    public PaginationInfo Pagination { get; init; } = new();
}

public record AdminInvoiceListItem
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public Guid OrganizationId { get; init; }
    public string OrganizationName { get; init; } = string.Empty;
    public int Subtotal { get; init; }
    public int TaxAmount { get; init; }
    public int TotalAmount { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime DueDate { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>
    /// Payment ID for refund processing (from the successful payment)
    /// </summary>
    public Guid? PaymentId { get; init; }
}

// AdminInvoiceSummary and AdminPlanInfo are defined in AdminOrganizationsController.cs
