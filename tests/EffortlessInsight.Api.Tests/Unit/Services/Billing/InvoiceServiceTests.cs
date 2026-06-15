using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Tests.Fixtures;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EffortlessInsight.Api.Tests.Unit.Services.Billing;

public class InvoiceServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly BillingOptions _billingOptions;
    private readonly ILogger<InvoiceService> _logger;
    private readonly InvoiceService _sut;

    public InvoiceServiceTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _fileStorage = Substitute.For<IFileStorageService>();
        _logger = Substitute.For<ILogger<InvoiceService>>();

        _billingOptions = new BillingOptions
        {
            CompanyName = "Test Company",
            CompanyAddress = "123 Test St",
            CompanyGstin = "TEST123456",
            CompanyStateCode = "27",
            InvoicePrefix = "INV",
            HsnCode = "998314",
            GstRate = 18
        };

        var optionsWrapper = Substitute.For<IOptions<BillingOptions>>();
        optionsWrapper.Value.Returns(_billingOptions);

        _sut = new InvoiceService(_dbContext, _fileStorage, optionsWrapper, _logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetInvoicesAsync Tests

    [Fact]
    public async Task GetInvoicesAsync_WithNoInvoices_ShouldReturnEmptyList()
    {
        // Act
        var result = await _sut.GetInvoicesAsync(Guid.NewGuid());

        // Assert
        result.Invoices.Should().BeEmpty();
        result.Pagination.Total.Should().Be(0);
    }

    [Fact]
    public async Task GetInvoicesAsync_ShouldReturnOnlyOrganizationInvoices()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();

        var invoice1 = BillingTestFixture.CreateInvoice(organizationId: orgId);
        var invoice2 = BillingTestFixture.CreateInvoice(organizationId: orgId);
        var invoice3 = BillingTestFixture.CreateInvoice(organizationId: otherOrgId);

        _dbContext.Invoices.AddRange(invoice1, invoice2, invoice3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetInvoicesAsync(orgId);

        // Assert
        result.Invoices.Should().HaveCount(2);
        result.Pagination.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetInvoicesAsync_ShouldOrderByDateDescending()
    {
        // Arrange
        var orgId = Guid.NewGuid();

        var invoice1 = BillingTestFixture.CreateInvoice(organizationId: orgId);
        invoice1.InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));

        var invoice2 = BillingTestFixture.CreateInvoice(organizationId: orgId);
        invoice2.InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var invoice3 = BillingTestFixture.CreateInvoice(organizationId: orgId);
        invoice3.InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        _dbContext.Invoices.AddRange(invoice1, invoice2, invoice3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetInvoicesAsync(orgId);

        // Assert
        result.Invoices[0].Date.Should().Be(invoice2.InvoiceDate);
        result.Invoices[1].Date.Should().Be(invoice3.InvoiceDate);
        result.Invoices[2].Date.Should().Be(invoice1.InvoiceDate);
    }

    [Fact]
    public async Task GetInvoicesAsync_ShouldRespectPagination()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        for (int i = 0; i < 15; i++)
        {
            var invoice = BillingTestFixture.CreateInvoice(organizationId: orgId);
            _dbContext.Invoices.Add(invoice);
        }
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetInvoicesAsync(orgId, page: 2, limit: 5);

        // Assert
        result.Invoices.Should().HaveCount(5);
        result.Pagination.Page.Should().Be(2);
        result.Pagination.Limit.Should().Be(5);
        result.Pagination.Total.Should().Be(15);
        result.Pagination.TotalPages.Should().Be(3);
    }

    #endregion

    #region GetInvoiceByIdAsync Tests

    [Fact]
    public async Task GetInvoiceByIdAsync_WithValidId_ShouldReturnInvoice()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var invoice = BillingTestFixture.CreateInvoice(organizationId: orgId);
        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetInvoiceByIdAsync(invoice.Id, orgId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(invoice.Id);
        result.Number.Should().Be(invoice.InvoiceNumber);
    }

    [Fact]
    public async Task GetInvoiceByIdAsync_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var result = await _sut.GetInvoiceByIdAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInvoiceByIdAsync_WithWrongOrganization_ShouldReturnNull()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var invoice = BillingTestFixture.CreateInvoice(organizationId: orgId);
        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetInvoiceByIdAsync(invoice.Id, Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetInvoicePdfAsync Tests

    [Fact]
    public async Task GetInvoicePdfAsync_WithValidInvoice_ShouldReturnPdfBytes()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var invoice = BillingTestFixture.CreateInvoice(organizationId: orgId);
        invoice.LineItems = new List<InvoiceLineItem>
        {
            new()
            {
                InvoiceId = invoice.Id,
                Type = LineItemType.Subscription,
                Description = "Starter Plan - Monthly",
                Quantity = 1,
                UnitPrice = 99900,
                Amount = 99900,
                HsnCode = "998314"
            }
        };
        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetInvoicePdfAsync(invoice.Id, orgId);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        // PDF files start with %PDF
        result[0].Should().Be((byte)'%');
        result[1].Should().Be((byte)'P');
        result[2].Should().Be((byte)'D');
        result[3].Should().Be((byte)'F');
    }

    [Fact]
    public async Task GetInvoicePdfAsync_WithInvalidInvoice_ShouldThrow()
    {
        // Act
        var act = () => _sut.GetInvoicePdfAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invoice not found");
    }

    #endregion

    #region GenerateInvoiceAsync Tests

    [Fact]
    public async Task GenerateInvoiceAsync_ShouldCreateInvoiceWithTax()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var billingDetails = BillingTestFixture.CreateBillingDetails(orgId);
        billingDetails.StateCode = "27"; // Same state
        _dbContext.BillingDetails.Add(billingDetails);
        await _dbContext.SaveChangesAsync();

        // Act
        var invoice = await _sut.GenerateInvoiceAsync(
            orgId, subscriptionId, 100000, "Test subscription");

        // Assert
        invoice.Should().NotBeNull();
        invoice.Subtotal.Should().Be(100000);
        invoice.TaxRate.Should().Be(18);
        invoice.TaxAmount.Should().Be(18000); // 18% of 100000
        invoice.Total.Should().Be(118000);
        invoice.IsInterState.Should().BeFalse();
        invoice.CgstAmount.Should().Be(9000); // 9%
        invoice.SgstAmount.Should().Be(9000); // 9%
        invoice.IgstAmount.Should().BeNull();
    }

    [Fact]
    public async Task GenerateInvoiceAsync_InterState_ShouldUseIgst()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);

        var billingDetails = BillingTestFixture.CreateBillingDetails(orgId);
        billingDetails.StateCode = "29"; // Different state
        _dbContext.BillingDetails.Add(billingDetails);
        await _dbContext.SaveChangesAsync();

        // Act
        var invoice = await _sut.GenerateInvoiceAsync(
            orgId, subscriptionId, 100000, "Test subscription");

        // Assert
        invoice.IsInterState.Should().BeTrue();
        invoice.IgstAmount.Should().Be(18000); // Full 18%
        invoice.CgstAmount.Should().BeNull();
        invoice.SgstAmount.Should().BeNull();
    }

    [Fact]
    public async Task GenerateInvoiceAsync_WithLineItems_ShouldAddLineItems()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);
        await _dbContext.SaveChangesAsync();

        var lineItems = new List<InvoiceLineItemRequest>
        {
            new InvoiceLineItemRequest
            {
                Type = LineItemType.Subscription,
                Description = "Professional Plan",
                Quantity = 1,
                UnitPrice = 249900,
                Amount = 249900,
                HsnCode = "998314",
                PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow),
                PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
                PlanCode = "professional",
                BillingCycle = "monthly"
            },
            new InvoiceLineItemRequest
            {
                Type = LineItemType.AdditionalSeats,
                Description = "Additional Seats x 3",
                Quantity = 3,
                UnitPrice = 10000,
                Amount = 30000
            }
        };

        // Act
        var invoice = await _sut.GenerateInvoiceAsync(
            orgId, subscriptionId, 279900, "Professional Plan + Seats", lineItems);

        // Assert
        var savedInvoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoice.Id);

        savedInvoice!.LineItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerateInvoiceAsync_WithoutLineItems_ShouldAddDefaultLineItem()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var org = BillingTestFixture.CreateOrganization(orgId);
        _dbContext.Organizations.Add(org);
        await _dbContext.SaveChangesAsync();

        // Act
        var invoice = await _sut.GenerateInvoiceAsync(
            orgId, subscriptionId, 99900, "Starter Plan");

        // Assert
        var savedInvoice = await _dbContext.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoice.Id);

        savedInvoice!.LineItems.Should().HaveCount(1);
        savedInvoice.LineItems.First().Description.Should().Be("Starter Plan");
    }

    #endregion

    #region GenerateInvoiceNumberAsync Tests

    [Fact]
    public async Task GenerateInvoiceNumberAsync_FirstInvoice_ShouldStartWithOne()
    {
        // Act
        var invoiceNumber = await _sut.GenerateInvoiceNumberAsync();

        // Assert
        invoiceNumber.Should().StartWith($"INV-{DateTime.UtcNow.Year}-");
        invoiceNumber.Should().EndWith("000001");
    }

    [Fact]
    public async Task GenerateInvoiceNumberAsync_ShouldIncrementNumber()
    {
        // Arrange
        var existingInvoice = BillingTestFixture.CreateInvoice();
        existingInvoice.InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-000042";
        _dbContext.Invoices.Add(existingInvoice);
        await _dbContext.SaveChangesAsync();

        // Act
        var invoiceNumber = await _sut.GenerateInvoiceNumberAsync();

        // Assert
        invoiceNumber.Should().EndWith("000043");
    }

    #endregion

    #region MarkAsPaidAsync Tests

    [Fact]
    public async Task MarkAsPaidAsync_ShouldUpdateInvoiceStatus()
    {
        // Arrange
        var invoice = BillingTestFixture.CreateInvoice(status: InvoiceStatus.Pending);
        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync();

        _fileStorage.UploadAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("https://storage.example.com/invoice.pdf");

        // Act
        await _sut.MarkAsPaidAsync(invoice.Id, "pay_123456");

        // Assert
        var updatedInvoice = await _dbContext.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be(InvoiceStatus.Paid);
        updatedInvoice.AmountPaid.Should().Be(updatedInvoice.Total);
        updatedInvoice.AmountDue.Should().Be(0);
        updatedInvoice.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkAsPaidAsync_WithInvalidId_ShouldNotThrow()
    {
        // Act
        var act = () => _sut.MarkAsPaidAsync(Guid.NewGuid(), "pay_123456");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region VoidInvoiceAsync Tests

    [Fact]
    public async Task VoidInvoiceAsync_ShouldUpdateInvoiceStatus()
    {
        // Arrange
        var invoice = BillingTestFixture.CreateInvoice(status: InvoiceStatus.Pending);
        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.VoidInvoiceAsync(invoice.Id, "Customer cancelled");

        // Assert
        var updatedInvoice = await _dbContext.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be(InvoiceStatus.Void);
        updatedInvoice.VoidedAt.Should().NotBeNull();
        updatedInvoice.VoidReason.Should().Be("Customer cancelled");
    }

    [Fact]
    public async Task VoidInvoiceAsync_WithInvalidId_ShouldNotThrow()
    {
        // Act
        var act = () => _sut.VoidInvoiceAsync(Guid.NewGuid(), "Reason");

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion
}
