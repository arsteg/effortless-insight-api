using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of the invoice service.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly BillingOptions _billingOptions;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorage,
        IOptions<BillingOptions> billingOptions,
        ILogger<InvoiceService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _billingOptions = billingOptions.Value;
        _logger = logger;

        // Set QuestPDF license (Community for free use)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<InvoiceListResponse> GetInvoicesAsync(
        Guid organizationId,
        int page = 1,
        int limit = 10)
    {
        var query = _dbContext.Invoices
            .Where(i => i.OrganizationId == organizationId)
            .OrderByDescending(i => i.InvoiceDate);

        var total = await query.CountAsync();
        var invoices = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var dtos = invoices.Select(i => new InvoiceDto(
            Id: i.Id,
            Number: i.InvoiceNumber,
            Date: i.InvoiceDate,
            DueDate: i.DueDate,
            Status: i.Status,
            Subtotal: i.Subtotal,
            Discount: i.Discount,
            Tax: i.TaxAmount,
            Total: i.Total,
            Currency: i.Currency,
            Description: i.Description,
            PdfUrl: $"/api/v1/invoices/{i.Id}/pdf"
        )).ToList();

        return new InvoiceListResponse(
            Invoices: dtos,
            Pagination: new BillingPaginationDto(
                Page: page,
                Limit: limit,
                Total: total,
                TotalPages: (int)Math.Ceiling((double)total / limit)
            )
        );
    }

    public async Task<InvoiceDetailDto?> GetInvoiceByIdAsync(Guid invoiceId, Guid organizationId)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrganizationId == organizationId);

        if (invoice == null)
            return null;

        return MapToDetailDto(invoice);
    }

    public async Task<byte[]> GetInvoicePdfAsync(Guid invoiceId, Guid organizationId)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrganizationId == organizationId);

        if (invoice == null)
            throw new InvalidOperationException("Invoice not found");

        // Generate PDF
        var pdf = GenerateInvoicePdf(invoice);
        return pdf;
    }

    public async Task<Invoice> GenerateInvoiceAsync(
        Guid organizationId,
        Guid subscriptionId,
        int amount,
        string description,
        List<InvoiceLineItemRequest>? lineItems = null)
    {
        var billingDetails = await _dbContext.BillingDetails
            .FirstOrDefaultAsync(b => b.OrganizationId == organizationId);

        var org = await _dbContext.Organizations.FindAsync(organizationId);

        var invoiceNumber = await GenerateInvoiceNumberAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Determine if inter-state
        var isInterState = billingDetails?.StateCode != _billingOptions.CompanyStateCode;

        // Calculate tax
        var subtotal = amount;
        var taxRate = _billingOptions.GstRate;
        var taxAmount = (int)Math.Round(subtotal * taxRate / 100);
        var total = subtotal + taxAmount;

        int? cgst = null, sgst = null, igst = null;
        if (isInterState)
        {
            igst = taxAmount;
        }
        else
        {
            cgst = taxAmount / 2;
            sgst = taxAmount / 2;
        }

        var invoice = new Invoice
        {
            OrganizationId = organizationId,
            SubscriptionId = subscriptionId,
            InvoiceNumber = invoiceNumber,
            Status = InvoiceStatus.Pending,
            InvoiceDate = today,
            DueDate = today,
            Currency = "INR",
            Subtotal = subtotal,
            Discount = 0,
            TaxRate = taxRate,
            TaxAmount = taxAmount,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            Total = total,
            AmountPaid = 0,
            AmountDue = total,
            Description = description,
            HsnCode = _billingOptions.HsnCode,
            PlaceOfSupply = billingDetails?.State,
            PlaceOfSupplyCode = billingDetails?.StateCode,
            IsInterState = isInterState,
            BillingDetails = new InvoiceBillingDetails
            {
                OrganizationName = billingDetails?.OrganizationName ?? org?.Name ?? "",
                Gstin = billingDetails?.Gstin,
                Address = billingDetails?.Address ?? "",
                City = billingDetails?.City,
                State = billingDetails?.State ?? "",
                StateCode = billingDetails?.StateCode,
                Pincode = billingDetails?.Pincode ?? "",
                Country = billingDetails?.Country ?? "India",
                Email = billingDetails?.Email,
                Phone = billingDetails?.Phone
            }
        };

        _dbContext.Invoices.Add(invoice);

        // Add line items
        if (lineItems != null && lineItems.Count > 0)
        {
            foreach (var item in lineItems)
            {
                var lineItem = new InvoiceLineItem
                {
                    InvoiceId = invoice.Id,
                    Type = item.Type,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Amount = item.Amount,
                    HsnCode = item.HsnCode ?? _billingOptions.HsnCode,
                    PeriodStart = item.PeriodStart,
                    PeriodEnd = item.PeriodEnd,
                    PlanCode = item.PlanCode,
                    BillingCycle = item.BillingCycle
                };
                _dbContext.InvoiceLineItems.Add(lineItem);
            }
        }
        else
        {
            // Add default line item
            var lineItem = new InvoiceLineItem
            {
                InvoiceId = invoice.Id,
                Type = LineItemType.Subscription,
                Description = description,
                Quantity = 1,
                UnitPrice = subtotal,
                Amount = subtotal,
                HsnCode = _billingOptions.HsnCode
            };
            _dbContext.InvoiceLineItems.Add(lineItem);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Generated invoice {InvoiceNumber} for organization {OrganizationId}",
            invoiceNumber, organizationId);

        return invoice;
    }

    public async Task MarkAsPaidAsync(Guid invoiceId, string razorpayPaymentId)
    {
        var invoice = await _dbContext.Invoices.FindAsync(invoiceId);
        if (invoice == null) return;

        invoice.Status = InvoiceStatus.Paid;
        invoice.AmountPaid = invoice.Total;
        invoice.AmountDue = 0;
        invoice.PaidAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        // Generate and upload PDF
        await RegeneratePdfAsync(invoiceId);

        _logger.LogInformation(
            "Marked invoice {InvoiceId} as paid via {PaymentId}",
            invoiceId, razorpayPaymentId);
    }

    public async Task VoidInvoiceAsync(Guid invoiceId, string reason)
    {
        var invoice = await _dbContext.Invoices.FindAsync(invoiceId);
        if (invoice == null) return;

        invoice.Status = InvoiceStatus.Void;
        invoice.VoidedAt = DateTime.UtcNow;
        invoice.VoidReason = reason;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Voided invoice {InvoiceId}. Reason: {Reason}",
            invoiceId, reason);
    }

    public async Task<string> GenerateInvoiceNumberAsync()
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"{_billingOptions.InvoicePrefix}-{year}-";

        var lastInvoice = await _dbContext.Invoices
            .Where(i => i.InvoiceNumber.StartsWith(prefix))
            .OrderByDescending(i => i.InvoiceNumber)
            .FirstOrDefaultAsync();

        int nextNumber = 1;
        if (lastInvoice != null)
        {
            var lastNumberStr = lastInvoice.InvoiceNumber.Replace(prefix, "");
            if (int.TryParse(lastNumberStr, out var lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"{prefix}{nextNumber:D6}";
    }

    public async Task RegeneratePdfAsync(Guid invoiceId)
    {
        var invoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId);

        if (invoice == null) return;

        try
        {
            var pdf = GenerateInvoicePdf(invoice);
            var fileName = $"invoices/{invoice.InvoiceNumber}.pdf";

            // Upload to S3
            using var stream = new MemoryStream(pdf);
            var url = await _fileStorage.UploadAsync(stream, fileName, "application/pdf");

            invoice.PdfUrl = url;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Generated PDF for invoice {InvoiceNumber}",
                invoice.InvoiceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate PDF for invoice {InvoiceId}", invoiceId);
        }
    }

    private byte[] GenerateInvoicePdf(Invoice invoice)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(c => ComposeHeader(c, invoice));
                page.Content().Element(c => ComposeContent(c, invoice));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, Invoice invoice)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(_billingOptions.CompanyName).Bold().FontSize(16);
                col.Item().Text(_billingOptions.CompanyAddress);
                col.Item().Text($"GSTIN: {_billingOptions.CompanyGstin}");
            });

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text("TAX INVOICE").Bold().FontSize(14);
                col.Item().Text($"Invoice #: {invoice.InvoiceNumber}");
                col.Item().Text($"Date: {invoice.InvoiceDate:dd MMM yyyy}");
                col.Item().Text($"Due Date: {invoice.DueDate:dd MMM yyyy}");
            });
        });
    }

    private void ComposeContent(IContainer container, Invoice invoice)
    {
        container.PaddingVertical(20).Column(col =>
        {
            // Bill To
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Bill To:").Bold();
                    c.Item().Text(invoice.BillingDetails.OrganizationName);
                    c.Item().Text(invoice.BillingDetails.Address);
                    if (!string.IsNullOrEmpty(invoice.BillingDetails.City))
                        c.Item().Text($"{invoice.BillingDetails.City}, {invoice.BillingDetails.State} - {invoice.BillingDetails.Pincode}");
                    if (!string.IsNullOrEmpty(invoice.BillingDetails.Gstin))
                        c.Item().Text($"GSTIN: {invoice.BillingDetails.Gstin}");
                });

                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Text($"Place of Supply: {invoice.PlaceOfSupply}");
                    c.Item().Text($"HSN/SAC: {invoice.HsnCode}");
                });
            });

            col.Item().PaddingVertical(15).LineHorizontal(1);

            // Line Items Table
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Description").Bold();
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Qty").Bold();
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Unit Price").Bold();
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Amount").Bold();
                });

                foreach (var item in invoice.LineItems)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(item.Description);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(item.Quantity.ToString());
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(FormatAmount(item.UnitPrice));
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text(FormatAmount(item.Amount));
                }
            });

            col.Item().PaddingTop(15).AlignRight().Width(250).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(100);
                });

                table.Cell().Padding(3).Text("Subtotal:");
                table.Cell().Padding(3).AlignRight().Text(FormatAmount(invoice.Subtotal));

                if (invoice.Discount > 0)
                {
                    table.Cell().Padding(3).Text("Discount:");
                    table.Cell().Padding(3).AlignRight().Text($"-{FormatAmount(invoice.Discount)}");
                }

                if (invoice.IsInterState)
                {
                    table.Cell().Padding(3).Text($"IGST ({invoice.TaxRate}%):");
                    table.Cell().Padding(3).AlignRight().Text(FormatAmount(invoice.IgstAmount ?? 0));
                }
                else
                {
                    table.Cell().Padding(3).Text($"CGST ({invoice.TaxRate / 2}%):");
                    table.Cell().Padding(3).AlignRight().Text(FormatAmount(invoice.CgstAmount ?? 0));
                    table.Cell().Padding(3).Text($"SGST ({invoice.TaxRate / 2}%):");
                    table.Cell().Padding(3).AlignRight().Text(FormatAmount(invoice.SgstAmount ?? 0));
                }

                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Total:").Bold();
                table.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text(FormatAmount(invoice.Total)).Bold();
            });

            // Notes
            if (!string.IsNullOrEmpty(invoice.Notes))
            {
                col.Item().PaddingTop(20).Text("Notes:").Bold();
                col.Item().Text(invoice.Notes);
            }
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span("This is a computer-generated invoice and does not require a signature.");
        });
    }

    private static string FormatAmount(int amountInPaise)
    {
        var amount = amountInPaise / 100.0;
        return $"\u20b9{amount:N2}";
    }

    private static InvoiceDetailDto MapToDetailDto(Invoice invoice)
    {
        return new InvoiceDetailDto(
            Id: invoice.Id,
            Number: invoice.InvoiceNumber,
            Date: invoice.InvoiceDate,
            DueDate: invoice.DueDate,
            Status: invoice.Status,
            Subtotal: invoice.Subtotal,
            Discount: invoice.Discount,
            DiscountDescription: invoice.DiscountDescription,
            TaxRate: invoice.TaxRate,
            TaxAmount: invoice.TaxAmount,
            CgstAmount: invoice.CgstAmount,
            SgstAmount: invoice.SgstAmount,
            IgstAmount: invoice.IgstAmount,
            Total: invoice.Total,
            AmountPaid: invoice.AmountPaid,
            AmountDue: invoice.AmountDue,
            Currency: invoice.Currency,
            Description: invoice.Description,
            HsnCode: invoice.HsnCode,
            PlaceOfSupply: invoice.PlaceOfSupply,
            IsInterState: invoice.IsInterState,
            BillingDetails: new InvoiceBillingDetailsDto(
                OrganizationName: invoice.BillingDetails.OrganizationName,
                Gstin: invoice.BillingDetails.Gstin,
                Address: invoice.BillingDetails.Address,
                City: invoice.BillingDetails.City,
                State: invoice.BillingDetails.State,
                StateCode: invoice.BillingDetails.StateCode,
                Pincode: invoice.BillingDetails.Pincode,
                Country: invoice.BillingDetails.Country,
                Email: invoice.BillingDetails.Email,
                Phone: invoice.BillingDetails.Phone
            ),
            LineItems: invoice.LineItems.Select(li => new InvoiceLineItemDto(
                Type: li.Type,
                Description: li.Description,
                Quantity: li.Quantity,
                UnitPrice: li.UnitPrice,
                Amount: li.Amount,
                HsnCode: li.HsnCode,
                PeriodStart: li.PeriodStart,
                PeriodEnd: li.PeriodEnd
            )).ToList(),
            Notes: invoice.Notes,
            PdfUrl: $"/api/v1/invoices/{invoice.Id}/pdf",
            PaidAt: invoice.PaidAt
        );
    }
}
