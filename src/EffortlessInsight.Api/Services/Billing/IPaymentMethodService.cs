using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Service for managing organization payment methods
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>
    /// Gets all payment methods for an organization
    /// </summary>
    Task<PaymentMethodListResponse> GetPaymentMethodsAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific payment method
    /// </summary>
    Task<PaymentMethodDto?> GetPaymentMethodAsync(Guid organizationId, Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default payment method for an organization
    /// </summary>
    Task<PaymentMethodDto?> GetDefaultPaymentMethodAsync(Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a payment method as the default
    /// </summary>
    Task<PaymentMethodDto> SetDefaultPaymentMethodAsync(Guid organizationId, Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a payment method
    /// </summary>
    Task<bool> DeletePaymentMethodAsync(Guid organizationId, Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a payment method from a Razorpay token
    /// </summary>
    Task<PaymentMethodDto> CreateFromRazorpayAsync(Guid organizationId, string razorpayPaymentId, string razorpayCustomerId, bool setAsDefault = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last used timestamp for a payment method
    /// </summary>
    Task UpdateLastUsedAsync(Guid paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a payment method when its mandate is cancelled or revoked.
    /// Called from webhook handlers when a token.cancelled event is received.
    /// </summary>
    /// <param name="razorpayTokenId">The Razorpay token ID.</param>
    /// <param name="cancellationReason">Optional reason for cancellation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a payment method was deactivated, false if not found.</returns>
    Task<bool> DeactivateByMandateCancelledAsync(
        string razorpayTokenId,
        string? cancellationReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the mandate status for a payment method.
    /// </summary>
    /// <param name="razorpayTokenId">The Razorpay token ID.</param>
    /// <param name="newStatus">The new mandate status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateMandateStatusAsync(
        string razorpayTokenId,
        string newStatus,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a payment method by Razorpay token ID.
    /// </summary>
    /// <param name="razorpayTokenId">The Razorpay token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payment method DTO if found, null otherwise.</returns>
    Task<PaymentMethodDto?> GetByRazorpayTokenAsync(
        string razorpayTokenId,
        CancellationToken cancellationToken = default);
}
