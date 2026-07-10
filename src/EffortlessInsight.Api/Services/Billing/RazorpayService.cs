using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;
using Razorpay.Api;

namespace EffortlessInsight.Api.Services.Billing;

/// <summary>
/// Implementation of Razorpay payment integration.
/// </summary>
public class RazorpayService : IRazorpayService
{
    private readonly RazorpayClient _client;
    private readonly RazorpayOptions _options;
    private readonly ILogger<RazorpayService> _logger;

    public RazorpayService(
        IOptions<RazorpayOptions> options,
        ILogger<RazorpayService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new RazorpayClient(_options.KeyId, _options.KeySecret);
    }

    public async Task<RazorpayOrderDto> CreateOrderAsync(CreateOrderRequest request)
    {
        try
        {
            var orderOptions = new Dictionary<string, object>
            {
                { "amount", request.AmountInPaise },
                { "currency", request.Currency },
                { "receipt", request.Receipt },
                {
                    "notes", new Dictionary<string, string>
                    {
                        { "organization_id", request.OrganizationId.ToString() },
                        { "plan_code", request.PlanCode },
                        { "subscription_id", request.SubscriptionId?.ToString() ?? "" }
                    }
                }
            };

            Order order = await Task.Run(() => _client.Order.Create(orderOptions));

            var orderId = (string)order["id"];
            var amount = Convert.ToInt32(order["amount"]);
            var currency = (string)order["currency"];
            var receipt = (string)order["receipt"];

            _logger.LogInformation(
                "Created Razorpay order {OrderId} for {Amount} {Currency}",
                orderId, request.AmountInPaise, request.Currency);

            return new RazorpayOrderDto(
                Id: orderId,
                Amount: amount,
                Currency: currency,
                Receipt: receipt,
                Key: _options.KeyId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Razorpay order");
            throw new InvalidOperationException("Failed to create payment order", ex);
        }
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        try
        {
            var text = $"{orderId}|{paymentId}";
            var expectedSignature = ComputeHmacSha256(text, _options.KeySecret);

            // Fixes Issue #7: Use constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature ?? ""),
                Encoding.UTF8.GetBytes(expectedSignature));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify payment signature");
            return false;
        }
    }

    public async Task<PaymentResult> CapturePaymentAsync(string paymentId, int? amount = null)
    {
        try
        {
            Payment payment = await Task.Run(() => _client.Payment.Fetch(paymentId));

            var status = (string)payment["status"];
            if (status == "authorized")
            {
                var captureAmount = amount ?? Convert.ToInt32(payment["amount"]);
                var captureOptions = new Dictionary<string, object>
                {
                    { "amount", captureAmount },
                    { "currency", (string)payment["currency"] }
                };
                payment = await Task.Run(() => payment.Capture(captureOptions));
            }

            var paymentIdResult = (string)payment["id"];
            var paymentStatus = (string)payment["status"];
            _logger.LogInformation(
                "Captured payment {PaymentId} with status {Status}",
                paymentIdResult, paymentStatus);

            return MapToPaymentResult(payment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture payment {PaymentId}", paymentId);
            throw new InvalidOperationException("Failed to capture payment", ex);
        }
    }

    public async Task<PaymentResult> GetPaymentAsync(string paymentId)
    {
        try
        {
            Payment payment = await Task.Run(() => _client.Payment.Fetch(paymentId));
            return MapToPaymentResult(payment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch payment {PaymentId}", paymentId);
            throw new InvalidOperationException("Failed to fetch payment", ex);
        }
    }

    public async Task<RazorpaySubscriptionResult> CreateSubscriptionAsync(CreateRazorpaySubscriptionRequest request)
    {
        try
        {
            var subscriptionOptions = new Dictionary<string, object>
            {
                { "plan_id", request.RazorpayPlanId },
                { "customer_id", request.RazorpayCustomerId },
                { "total_count", request.BillingCycle == "monthly" ? 120 : 10 },
                { "quantity", request.Quantity },
                { "customer_notify", 1 },
                {
                    "notes", new Dictionary<string, string>
                    {
                        { "organization_id", request.OrganizationId.ToString() }
                    }
                }
            };

            Subscription subscription = await Task.Run(() => _client.Subscription.Create(subscriptionOptions));

            var subscriptionId = (string)subscription["id"];
            var planId = (string)subscription["plan_id"];
            _logger.LogInformation(
                "Created Razorpay subscription {SubscriptionId} for plan {PlanId}",
                subscriptionId, planId);

            return new RazorpaySubscriptionResult
            {
                SubscriptionId = subscriptionId,
                Status = (string)subscription["status"],
                PlanId = planId,
                ShortUrl = subscription["short_url"]?.ToString() ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Razorpay subscription");
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, bool cancelAtCycleEnd = true)
    {
        try
        {
            // Razorpay SDK Cancel takes Dictionary<string, object> with subscription_id
            var cancelOptions = new Dictionary<string, object>
            {
                { "subscription_id", subscriptionId }
            };
            await Task.Run(() => _client.Subscription.Cancel(cancelOptions));

            _logger.LogInformation(
                "Cancelled Razorpay subscription {SubscriptionId}",
                subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel Razorpay subscription {SubscriptionId}", subscriptionId);
            throw new InvalidOperationException("Failed to cancel subscription", ex);
        }
    }

    public async Task<RazorpayCustomerResult> CreateOrGetCustomerAsync(string name, string email, string? phone)
    {
        try
        {
            // For simplicity, create a new customer
            // In production, you'd want to search by email first
            var customerOptions = new Dictionary<string, object>
            {
                { "name", name },
                { "email", email },
                { "fail_existing", 0 }
            };

            if (!string.IsNullOrEmpty(phone))
            {
                customerOptions["contact"] = phone;
            }

            Customer customer = await Task.Run(() => _client.Customer.Create(customerOptions));

            return new RazorpayCustomerResult
            {
                CustomerId = (string)customer["id"],
                Name = customer["name"]?.ToString() ?? name,
                Email = customer["email"]?.ToString() ?? email,
                Contact = customer["contact"]?.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create/get Razorpay customer");
            throw new InvalidOperationException("Failed to create customer", ex);
        }
    }

    public async Task<RefundResult> CreateRefundAsync(string paymentId, int? amount = null, string? reason = null)
    {
        try
        {
            var refundOptions = new Dictionary<string, object>();

            if (amount.HasValue)
            {
                refundOptions["amount"] = amount.Value;
            }

            if (!string.IsNullOrEmpty(reason))
            {
                refundOptions["notes"] = new Dictionary<string, string>
                {
                    { "reason", reason }
                };
            }

            // Fetch payment first, then call refund on it
            Payment payment = await Task.Run(() => _client.Payment.Fetch(paymentId));
            Refund refund = await Task.Run(() => payment.Refund(refundOptions));

            var refundId = (string)refund["id"];
            _logger.LogInformation(
                "Created refund {RefundId} for payment {PaymentId}",
                refundId, paymentId);

            return new RefundResult
            {
                RefundId = refundId,
                Status = (string)refund["status"],
                Amount = Convert.ToInt32(refund["amount"]),
                PaymentId = paymentId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create refund for payment {PaymentId}", paymentId);
            throw new InvalidOperationException("Failed to create refund", ex);
        }
    }

    public async Task<RecurringPaymentResult> CreateRecurringPaymentAsync(CreateRecurringPaymentRequest request)
    {
        try
        {
            _logger.LogInformation(
                "Creating recurring payment for customer {CustomerId}, amount {Amount} paise",
                request.CustomerId, request.AmountInPaise);

            // Step 1: Create an order for the recurring payment
            var orderOptions = new Dictionary<string, object>
            {
                { "amount", request.AmountInPaise },
                { "currency", request.Currency },
                { "receipt", request.Receipt },
                { "payment_capture", 1 } // Auto-capture the payment
            };

            if (request.Notes != null && request.Notes.Count > 0)
            {
                orderOptions["notes"] = request.Notes;
            }

            Order order = await Task.Run(() => _client.Order.Create(orderOptions));
            var orderId = (string)order["id"];

            _logger.LogInformation("Created order {OrderId} for recurring payment", orderId);

            // Step 2: Create payment using the saved token (recurring payment)
            // This uses Razorpay's "Subsequent Recurring Payments" API
            var paymentOptions = new Dictionary<string, object>
            {
                { "amount", request.AmountInPaise },
                { "currency", request.Currency },
                { "order_id", orderId },
                { "customer_id", request.CustomerId },
                { "token", request.TokenId },
                { "recurring", "1" },
                { "description", request.Description }
            };

            if (!string.IsNullOrEmpty(request.Email))
            {
                paymentOptions["email"] = request.Email;
            }

            if (!string.IsNullOrEmpty(request.Contact))
            {
                paymentOptions["contact"] = request.Contact;
            }

            if (request.Notes != null && request.Notes.Count > 0)
            {
                paymentOptions["notes"] = request.Notes;
            }

            // Create the recurring payment
            Payment payment = await Task.Run(() => _client.Payment.CreateRecurringPayment(paymentOptions));

            var paymentId = (string)payment["id"];
            var status = (string)payment["status"];

            _logger.LogInformation(
                "Created recurring payment {PaymentId} with status {Status} for order {OrderId}",
                paymentId, status, orderId);

            // Check if payment was successful
            var isSuccess = status == "captured" || status == "authorized";

            return new RecurringPaymentResult
            {
                Success = isSuccess,
                PaymentId = paymentId,
                OrderId = orderId,
                Status = status,
                Amount = Convert.ToInt32(payment["amount"]),
                Method = payment["method"]?.ToString(),
                ErrorCode = !isSuccess ? payment["error_code"]?.ToString() : null,
                ErrorDescription = !isSuccess ? payment["error_description"]?.ToString() : null
            };
        }
        catch (Razorpay.Api.Errors.BadRequestError ex)
        {
            _logger.LogWarning(ex,
                "Razorpay bad request for recurring payment. Customer: {CustomerId}, Token: {TokenId}",
                request.CustomerId, request.TokenId);

            return new RecurringPaymentResult
            {
                Success = false,
                Status = "failed",
                Amount = request.AmountInPaise,
                ErrorCode = "BAD_REQUEST",
                ErrorDescription = ex.Message
            };
        }
        catch (Razorpay.Api.Errors.GatewayError ex)
        {
            _logger.LogWarning(ex,
                "Payment gateway error for recurring payment. Customer: {CustomerId}",
                request.CustomerId);

            return new RecurringPaymentResult
            {
                Success = false,
                Status = "failed",
                Amount = request.AmountInPaise,
                ErrorCode = "GATEWAY_ERROR",
                ErrorDescription = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create recurring payment for customer {CustomerId}",
                request.CustomerId);

            return new RecurringPaymentResult
            {
                Success = false,
                Status = "failed",
                Amount = request.AmountInPaise,
                ErrorCode = "INTERNAL_ERROR",
                ErrorDescription = ex.Message
            };
        }
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        try
        {
            var expectedSignature = ComputeHmacSha256(payload, _options.WebhookSecret);

            // Fixes Issue #7: Use constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature ?? ""),
                Encoding.UTF8.GetBytes(expectedSignature));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify webhook signature");
            return false;
        }
    }

    public string GetPublicKey()
    {
        return _options.KeyId;
    }

    private static PaymentResult MapToPaymentResult(Payment payment)
    {
        var result = new PaymentResult
        {
            PaymentId = (string)payment["id"],
            Status = (string)payment["status"],
            Amount = Convert.ToInt32(payment["amount"]),
            Currency = payment["currency"]?.ToString() ?? "INR",
            Method = payment["method"]?.ToString() ?? "",
            Email = payment["email"]?.ToString(),
            Contact = payment["contact"]?.ToString(),
            ErrorCode = payment["error_code"]?.ToString(),
            ErrorDescription = payment["error_description"]?.ToString(),
            TokenId = payment["token_id"]?.ToString(),
            Vpa = payment["vpa"]?.ToString()
        };

        // Extract card details if method is card
        if (result.Method == "card" && payment["card"] != null)
        {
            var cardData = payment["card"];
            result = result with
            {
                Card = new CardDetails
                {
                    Last4 = cardData["last4"]?.ToString(),
                    Network = cardData["network"]?.ToString(),
                    ExpiryMonth = cardData["expiry_month"] != null ? Convert.ToInt32(cardData["expiry_month"]) : null,
                    ExpiryYear = cardData["expiry_year"] != null ? Convert.ToInt32(cardData["expiry_year"]) : null,
                    Name = cardData["name"]?.ToString(),
                    Type = cardData["type"]?.ToString(),
                    Issuer = cardData["issuer"]?.ToString()
                }
            };
        }

        return result;
    }

    private static string ComputeHmacSha256(string message, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
