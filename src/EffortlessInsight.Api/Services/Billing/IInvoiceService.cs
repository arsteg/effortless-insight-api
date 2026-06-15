using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for managing invoices.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Gets invoices for an organization.
    /// </summary>
    Task<InvoiceListResponse> GetInvoicesAsync(
        Guid organizationId,
        int page = 1,
        int limit = 10);

    /// <summary>
    /// Gets an invoice by ID.
    /// </summary>
    Task<InvoiceDetailDto?> GetInvoiceByIdAsync(Guid invoiceId, Guid organizationId);

    /// <summary>
    /// Gets the PDF for an invoice.
    /// </summary>
    Task<byte[]> GetInvoicePdfAsync(Guid invoiceId, Guid organizationId);

    /// <summary>
    /// Generates a new invoice for a payment.
    /// </summary>
    Task<Invoice> GenerateInvoiceAsync(
        Guid organizationId,
        Guid subscriptionId,
        int amount,
        string description,
        List<InvoiceLineItemRequest>? lineItems = null);

    /// <summary>
    /// Marks an invoice as paid.
    /// </summary>
    Task MarkAsPaidAsync(Guid invoiceId, string razorpayPaymentId);

    /// <summary>
    /// Voids an invoice.
    /// </summary>
    Task VoidInvoiceAsync(Guid invoiceId, string reason);

    /// <summary>
    /// Generates the next invoice number.
    /// </summary>
    Task<string> GenerateInvoiceNumberAsync();

    /// <summary>
    /// Regenerates the PDF for an invoice.
    /// </summary>
    Task RegeneratePdfAsync(Guid invoiceId);
}

/// <summary>
/// Request for creating an invoice line item.
/// </summary>
public record InvoiceLineItemRequest
{
    public string Type { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Quantity { get; init; } = 1;
    public int UnitPrice { get; init; }
    public int Amount { get; init; }
    public string? HsnCode { get; init; }
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
    public string? PlanCode { get; init; }
    public string? BillingCycle { get; init; }
}
