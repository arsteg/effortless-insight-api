using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of payment method service
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly ApplicationDbContext _context;
    private readonly IRazorpayService _razorpayService;
    private readonly ILogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        ApplicationDbContext context,
        IRazorpayService razorpayService,
        ILogger<PaymentMethodService> logger)
    {
        _context = context;
        _razorpayService = razorpayService;
        _logger = logger;
    }

    public async Task<PaymentMethodListResponse> GetPaymentMethodsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var paymentMethods = await _context.PaymentMethods
            .Where(pm => pm.OrganizationId == organizationId && pm.IsActive)
            .OrderByDescending(pm => pm.IsDefault)
            .ThenByDescending(pm => pm.LastUsedAt)
            .Select(pm => MapToDto(pm))
            .ToListAsync(cancellationToken);

        return new PaymentMethodListResponse(paymentMethods);
    }

    public async Task<PaymentMethodDto?> GetPaymentMethodAsync(Guid organizationId, Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _context.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.Id == paymentMethodId && pm.OrganizationId == organizationId && pm.IsActive, cancellationToken);

        return paymentMethod != null ? MapToDto(paymentMethod) : null;
    }

    public async Task<PaymentMethodDto?> GetDefaultPaymentMethodAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _context.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.OrganizationId == organizationId && pm.IsDefault && pm.IsActive, cancellationToken);

        return paymentMethod != null ? MapToDto(paymentMethod) : null;
    }

    public async Task<PaymentMethodDto> SetDefaultPaymentMethodAsync(Guid organizationId, Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _context.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.Id == paymentMethodId && pm.OrganizationId == organizationId && pm.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Payment method not found");

        // Unset current default
        var currentDefault = await _context.PaymentMethods
            .Where(pm => pm.OrganizationId == organizationId && pm.IsDefault && pm.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var pm in currentDefault)
        {
            pm.IsDefault = false;
            pm.UpdatedAt = DateTime.UtcNow;
        }

        // Set new default
        paymentMethod.IsDefault = true;
        paymentMethod.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Set payment method {PaymentMethodId} as default for organization {OrganizationId}",
            paymentMethodId, organizationId);

        return MapToDto(paymentMethod);
    }

    public async Task<bool> DeletePaymentMethodAsync(Guid organizationId, Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _context.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.Id == paymentMethodId && pm.OrganizationId == organizationId, cancellationToken);

        if (paymentMethod == null)
        {
            return false;
        }

        if (paymentMethod.IsDefault)
        {
            throw new InvalidOperationException("Cannot delete default payment method. Please set another payment method as default first.");
        }

        // Soft delete
        paymentMethod.IsActive = false;
        paymentMethod.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted payment method {PaymentMethodId} for organization {OrganizationId}",
            paymentMethodId, organizationId);

        return true;
    }

    public async Task<PaymentMethodDto> CreateFromRazorpayAsync(Guid organizationId, string razorpayPaymentId, string razorpayCustomerId, bool setAsDefault = false, CancellationToken cancellationToken = default)
    {
        // Fetch payment details from Razorpay
        var paymentDetails = await _razorpayService.GetPaymentAsync(razorpayPaymentId);

        var paymentMethod = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RazorpayCustomerId = razorpayCustomerId,
            RazorpayTokenId = paymentDetails.TokenId,
            IsActive = true,
            LastUsedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Set type-specific fields based on payment method
        switch (paymentDetails.Method?.ToLower())
        {
            case "card":
                paymentMethod.Type = PaymentMethodType.Card;
                paymentMethod.CardLast4 = paymentDetails.Card?.Last4;
                paymentMethod.CardBrand = paymentDetails.Card?.Network;
                paymentMethod.CardExpiryMonth = paymentDetails.Card?.ExpiryMonth;
                paymentMethod.CardExpiryYear = paymentDetails.Card?.ExpiryYear;
                paymentMethod.CardName = paymentDetails.Card?.Name;
                paymentMethod.CardFunding = paymentDetails.Card?.Type;
                break;

            case "upi":
                paymentMethod.Type = PaymentMethodType.Upi;
                paymentMethod.UpiId = paymentDetails.Vpa;
                break;

            case "netbanking":
                paymentMethod.Type = PaymentMethodType.NetBanking;
                break;

            case "wallet":
                paymentMethod.Type = PaymentMethodType.Wallet;
                break;

            default:
                paymentMethod.Type = paymentDetails.Method ?? PaymentMethodType.Card;
                break;
        }

        if (setAsDefault)
        {
            // Unset current default
            var currentDefault = await _context.PaymentMethods
                .Where(pm => pm.OrganizationId == organizationId && pm.IsDefault && pm.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var pm in currentDefault)
            {
                pm.IsDefault = false;
                pm.UpdatedAt = DateTime.UtcNow;
            }

            paymentMethod.IsDefault = true;
        }
        else
        {
            // If this is the first payment method, make it default
            var hasPaymentMethods = await _context.PaymentMethods
                .AnyAsync(pm => pm.OrganizationId == organizationId && pm.IsActive, cancellationToken);

            paymentMethod.IsDefault = !hasPaymentMethods;
        }

        _context.PaymentMethods.Add(paymentMethod);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created payment method {PaymentMethodId} for organization {OrganizationId} from Razorpay payment {RazorpayPaymentId}",
            paymentMethod.Id, organizationId, razorpayPaymentId);

        return MapToDto(paymentMethod);
    }

    public async Task UpdateLastUsedAsync(Guid paymentMethodId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _context.PaymentMethods
            .FirstOrDefaultAsync(pm => pm.Id == paymentMethodId, cancellationToken);

        if (paymentMethod != null)
        {
            paymentMethod.LastUsedAt = DateTime.UtcNow;
            paymentMethod.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static PaymentMethodDto MapToDto(PaymentMethod pm) =>
        new PaymentMethodDto(
            Id: pm.Id,
            Type: pm.Type,
            IsDefault: pm.IsDefault,
            CardLast4: pm.CardLast4,
            CardBrand: pm.CardBrand,
            CardExpiryMonth: pm.CardExpiryMonth,
            CardExpiryYear: pm.CardExpiryYear,
            CardName: pm.CardName,
            UpiId: pm.UpiId,
            LastUsedAt: pm.LastUsedAt
        );
}
