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
    private readonly BillingOptions _billingOptions;
    private readonly ILogger<RazorpayService> _logger;

    public RazorpayService(
        IOptions<RazorpayOptions> options,
        IOptions<BillingOptions> billingOptions,
        ILogger<RazorpayService> logger)
    {
        _options = options.Value;
        _billingOptions = billingOptions.Value;
        _logger = logger;
        _client = new RazorpayClient(_options.KeyId, _options.KeySecret);
    }

    public async Task<RazorpayOrderDto> CreateOrderAsync(CreateOrderRequest request)
    {
        try
        {
            var notes = new Dictionary<string, string>
            {
                { "organization_id", request.OrganizationId.ToString() },
                { "plan_code", request.PlanCode },
                { "subscription_id", request.SubscriptionId?.ToString() ?? "" },
                { "payment_type", request.PaymentType }
            };

            // Add optional fields for upgrades
            if (!string.IsNullOrEmpty(request.BillingCycle))
                notes["billing_cycle"] = request.BillingCycle;
            if (request.AdditionalSeats.HasValue)
                notes["additional_seats"] = request.AdditionalSeats.Value.ToString();

            var orderOptions = new Dictionary<string, object>
            {
                { "amount", request.AmountInPaise },
                { "currency", request.Currency },
                { "receipt", request.Receipt },
                { "notes", notes }
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

    public async Task<OrderDetails> GetOrderAsync(string orderId)
    {
        try
        {
            Order order = await Task.Run(() => _client.Order.Fetch(orderId));

            var notes = new Dictionary<string, string>();
            if (order.Attributes.ContainsKey("notes"))
            {
                var orderNotes = order["notes"];
                if (orderNotes is Newtonsoft.Json.Linq.JObject jNotes)
                {
                    foreach (var prop in jNotes.Properties())
                    {
                        notes[prop.Name] = prop.Value?.ToString() ?? "";
                    }
                }
                else if (orderNotes is IDictionary<string, object> dictNotes)
                {
                    foreach (var kvp in dictNotes)
                    {
                        notes[kvp.Key] = kvp.Value?.ToString() ?? "";
                    }
                }
            }

            return new OrderDetails
            {
                OrderId = (string)order["id"],
                Amount = Convert.ToInt32(order["amount"]),
                Currency = (string)order["currency"],
                Receipt = (string)order["receipt"],
                Status = (string)order["status"],
                Notes = notes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Razorpay order {OrderId}", orderId);
            throw new InvalidOperationException("Failed to fetch payment order", ex);
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
            // Calculate mandate expiry timestamp for autopay authorization
            var expireBy = DateTimeOffset.UtcNow.AddYears(_billingOptions.MandateExpiryYears).ToUnixTimeSeconds();

            var subscriptionOptions = new Dictionary<string, object>
            {
                { "plan_id", request.RazorpayPlanId },
                { "customer_id", request.RazorpayCustomerId },
                { "total_count", request.BillingCycle == "monthly" ? 120 : 10 },
                { "quantity", request.Quantity },
                { "customer_notify", 1 },
                { "expire_by", expireBy },
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
            // The Razorpay .NET SDK's Cancel method doesn't work correctly
            // Use direct HTTP call to the Razorpay API instead
            using var httpClient = new HttpClient();

            var authBytes = Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}");
            var authBase64 = Convert.ToBase64String(authBytes);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);

            var requestBody = new { cancel_at_cycle_end = cancelAtCycleEnd ? 1 : 0 };
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync(
                $"https://api.razorpay.com/v1/subscriptions/{subscriptionId}/cancel",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Razorpay API returned error for subscription cancel: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);
                throw new InvalidOperationException($"Razorpay API error: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation(
                "Cancelled Razorpay subscription {SubscriptionId}, cancelAtCycleEnd: {CancelAtCycleEnd}",
                subscriptionId, cancelAtCycleEnd);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel Razorpay subscription {SubscriptionId}", subscriptionId);
            throw new InvalidOperationException("Failed to cancel subscription", ex);
        }
    }

    public async Task<bool> ReactivateSubscriptionAsync(string subscriptionId)
    {
        try
        {
            // First, check the current status of the subscription
            var status = await GetSubscriptionStatusAsync(subscriptionId);

            // Only reactivate if the subscription is still active (just scheduled for cancellation)
            // If it's already cancelled, we can't reactivate via API - user needs a new subscription
            if (status.Status == "cancelled" || status.Status == "completed" || status.Status == "expired")
            {
                _logger.LogWarning(
                    "Cannot reactivate Razorpay subscription {SubscriptionId} - status is {Status}. User needs to create a new subscription.",
                    subscriptionId, status.Status);
                throw new InvalidOperationException(
                    "SUBSCRIPTION_FULLY_CANCELLED: This subscription has been fully cancelled and cannot be reactivated. Please create a new subscription.");
            }

            // Use PATCH request to remove the scheduled cancellation by clearing end_at
            using var httpClient = new HttpClient();

            var authBytes = Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}");
            var authBase64 = Convert.ToBase64String(authBytes);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);

            // Setting end_at to 0 or removing it clears the scheduled cancellation
            var requestBody = new { end_at = (int?)null };
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PatchAsync(
                $"https://api.razorpay.com/v1/subscriptions/{subscriptionId}",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Razorpay API returned error for subscription reactivation: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);

                // Check if it's because the subscription can't be updated
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    throw new InvalidOperationException(
                        "CANNOT_REACTIVATE: The subscription cannot be reactivated. It may have already been fully cancelled. Please create a new subscription.");
                }

                throw new InvalidOperationException($"Razorpay API error: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation(
                "Reactivated Razorpay subscription {SubscriptionId} by clearing scheduled cancellation",
                subscriptionId);

            return true;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reactivate Razorpay subscription {SubscriptionId}", subscriptionId);
            throw new InvalidOperationException("Failed to reactivate subscription", ex);
        }
    }

    public async Task<bool> UpdateSubscriptionPlanAsync(string subscriptionId, string newRazorpayPlanId)
    {
        try
        {
            // The Razorpay .NET SDK doesn't have a direct Update method for subscriptions
            // We need to make a direct HTTP call to the Razorpay API
            using var httpClient = new HttpClient();

            // Set up Basic Auth with Razorpay credentials
            var authBytes = Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}");
            var authBase64 = Convert.ToBase64String(authBytes);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);

            var requestBody = new
            {
                plan_id = newRazorpayPlanId,
                schedule_change_at = "cycle_end" // Change takes effect at next billing cycle
            };

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PatchAsync(
                $"https://api.razorpay.com/v1/subscriptions/{subscriptionId}",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Razorpay API returned error for subscription update: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);
                throw new InvalidOperationException($"Razorpay API error: {response.StatusCode}");
            }

            _logger.LogInformation(
                "Updated Razorpay subscription {SubscriptionId} to plan {NewPlanId}",
                subscriptionId, newRazorpayPlanId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update Razorpay subscription {SubscriptionId} to plan {NewPlanId}",
                subscriptionId, newRazorpayPlanId);
            throw new InvalidOperationException("Failed to update subscription plan", ex);
        }
    }

    public async Task<bool> UpdateSubscriptionStartAtAsync(string subscriptionId, long startAtTimestamp)
    {
        try
        {
            // The Razorpay .NET SDK doesn't have a direct method for updating start_at
            // We need to make a direct HTTP call to the Razorpay API
            using var httpClient = new HttpClient();

            // Set up Basic Auth with Razorpay credentials
            var authBytes = Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}");
            var authBase64 = Convert.ToBase64String(authBytes);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);

            var requestBody = new
            {
                start_at = startAtTimestamp
            };

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PatchAsync(
                $"https://api.razorpay.com/v1/subscriptions/{subscriptionId}",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Razorpay API returned error for subscription start_at update: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);
                throw new InvalidOperationException($"Razorpay API error: {response.StatusCode}");
            }

            _logger.LogInformation(
                "Updated Razorpay subscription {SubscriptionId} start_at to {StartAt}",
                subscriptionId, startAtTimestamp);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update Razorpay subscription {SubscriptionId} start_at to {StartAt}",
                subscriptionId, startAtTimestamp);
            throw new InvalidOperationException("Failed to update subscription start time", ex);
        }
    }

    public async Task<RazorpaySubscriptionResult> CreateSubscriptionWithTrialAsync(CreateRazorpaySubscriptionWithTrialRequest request)
    {
        try
        {
            // Calculate mandate expiry timestamp for autopay authorization
            var expireBy = DateTimeOffset.UtcNow.AddYears(_billingOptions.MandateExpiryYears).ToUnixTimeSeconds();

            var subscriptionOptions = new Dictionary<string, object>
            {
                { "plan_id", request.RazorpayPlanId },
                { "customer_id", request.RazorpayCustomerId },
                { "total_count", request.BillingCycle == "monthly" ? 120 : 10 }, // Max billing cycles
                { "quantity", request.Quantity },
                { "customer_notify", request.NotifyCustomer ? 1 : 0 },
                { "expire_by", expireBy }
            };

            // If trial days > 0, set start_at to delay first charge
            if (request.TrialDays > 0)
            {
                var startAt = DateTimeOffset.UtcNow.AddDays(request.TrialDays).ToUnixTimeSeconds();
                subscriptionOptions["start_at"] = startAt;
            }

            // Add notes
            var notes = new Dictionary<string, string>
            {
                { "organization_id", request.OrganizationId.ToString() },
                { "billing_cycle", request.BillingCycle }
            };

            if (request.Notes != null)
            {
                foreach (var (key, value) in request.Notes)
                {
                    notes[key] = value;
                }
            }

            subscriptionOptions["notes"] = notes;

            // Add callback URL for mandate authentication
            if (!string.IsNullOrEmpty(request.CallbackUrl))
            {
                subscriptionOptions["callback_url"] = request.CallbackUrl;
            }

            Subscription subscription = await Task.Run(() => _client.Subscription.Create(subscriptionOptions));

            var subscriptionId = (string)subscription["id"];
            var planId = (string)subscription["plan_id"];
            var status = (string)subscription["status"];

            _logger.LogInformation(
                "Created Razorpay subscription {SubscriptionId} for plan {PlanId} with trial {TrialDays} days, status: {Status}",
                subscriptionId, planId, request.TrialDays, status);

            return new RazorpaySubscriptionResult
            {
                SubscriptionId = subscriptionId,
                Status = status,
                PlanId = planId,
                ShortUrl = subscription["short_url"]?.ToString() ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Razorpay subscription with trial");
            throw new InvalidOperationException("Failed to create subscription with trial", ex);
        }
    }

    public async Task<RazorpaySubscriptionStatus> GetSubscriptionStatusAsync(string subscriptionId)
    {
        try
        {
            Subscription subscription = await Task.Run(() => _client.Subscription.Fetch(subscriptionId));

            var status = (string)subscription["status"];
            var planId = subscription["plan_id"]?.ToString() ?? "";

            return new RazorpaySubscriptionStatus
            {
                SubscriptionId = subscriptionId,
                Status = status,
                PlanId = planId,
                CurrentStart = subscription["current_start"] != null ? Convert.ToInt64(subscription["current_start"]) : null,
                CurrentEnd = subscription["current_end"] != null ? Convert.ToInt64(subscription["current_end"]) : null,
                PaidCount = subscription["paid_count"] != null ? Convert.ToInt32(subscription["paid_count"]) : null,
                RemainingCount = subscription["remaining_count"] != null ? Convert.ToInt32(subscription["remaining_count"]) : null,
                // For UPI autopay, payment_method may not be set but paid_count > 0 or auth_type present
                // indicates a valid payment authorization exists
                HasPaymentMethod = subscription["payment_method"] != null
                    || (subscription["paid_count"] != null && Convert.ToInt32(subscription["paid_count"]) > 0)
                    || subscription["auth_type"] != null,
                ShortUrl = subscription["short_url"]?.ToString(),
                EndAt = subscription["end_at"] != null ? Convert.ToInt64(subscription["end_at"]) : null,
                ChargeAt = subscription["charge_at"] != null ? Convert.ToInt64(subscription["charge_at"]) : null,
                IsPaused = status == "paused"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Razorpay subscription status {SubscriptionId}", subscriptionId);
            throw new InvalidOperationException("Failed to get subscription status", ex);
        }
    }

    public async Task PauseSubscriptionAsync(string subscriptionId)
    {
        try
        {
            // The Razorpay .NET SDK's Pause method doesn't work correctly
            // Use direct HTTP call to the Razorpay API instead
            using var httpClient = new HttpClient();

            var authBytes = Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}");
            var authBase64 = Convert.ToBase64String(authBytes);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);

            var requestBody = new { pause_at = "now" };
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync(
                $"https://api.razorpay.com/v1/subscriptions/{subscriptionId}/pause",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Razorpay API returned error for subscription pause: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);
                throw new InvalidOperationException($"Razorpay API error: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation("Paused Razorpay subscription {SubscriptionId}", subscriptionId);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause Razorpay subscription {SubscriptionId}", subscriptionId);
            throw new InvalidOperationException("Failed to pause subscription", ex);
        }
    }

    public async Task ResumeSubscriptionAsync(string subscriptionId)
    {
        try
        {
            // The Razorpay .NET SDK's Resume method doesn't work correctly
            // Use direct HTTP call to the Razorpay API instead
            using var httpClient = new HttpClient();

            var authBytes = Encoding.UTF8.GetBytes($"{_options.KeyId}:{_options.KeySecret}");
            var authBase64 = Convert.ToBase64String(authBytes);
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authBase64);

            var requestBody = new { resume_at = "now" };
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync(
                $"https://api.razorpay.com/v1/subscriptions/{subscriptionId}/resume",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Razorpay API returned error for subscription resume: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);

                // Handle specific error codes
                if (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired)
                {
                    throw new PaymentRequiredException(
                        "Your subscription requires payment to resume. The billing period may have ended during the pause. Please update your payment method or create a new subscription.");
                }

                throw new InvalidOperationException($"Razorpay API error: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation("Resumed Razorpay subscription {SubscriptionId}", subscriptionId);
        }
        catch (PaymentRequiredException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume Razorpay subscription {SubscriptionId}", subscriptionId);
            throw new InvalidOperationException("Failed to resume subscription", ex);
        }
    }

    /// <summary>
    /// Custom exception for payment required scenarios.
    /// </summary>
    public class PaymentRequiredException : Exception
    {
        public PaymentRequiredException(string message) : base(message) { }
    }

    public bool VerifySubscriptionSignature(string subscriptionId, string paymentId, string signature)
    {
        try
        {
            var text = $"{paymentId}|{subscriptionId}";
            var expectedSignature = ComputeHmacSha256(text, _options.KeySecret);

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature ?? ""),
                Encoding.UTF8.GetBytes(expectedSignature));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify subscription signature");
            return false;
        }
    }

    public async Task<RazorpayCustomerResult> CreateOrGetCustomerAsync(string name, string email, string? phone)
    {
        try
        {
            // First, try to find existing customer by email
            var existingCustomer = await FindCustomerByEmailAsync(email);
            if (existingCustomer != null)
            {
                _logger.LogInformation("Found existing Razorpay customer {CustomerId} for email {Email}",
                    existingCustomer.CustomerId, email);
                return existingCustomer;
            }

            // Customer doesn't exist, create a new one
            var customerOptions = new Dictionary<string, object>
            {
                { "name", name },
                { "email", email }
            };

            if (!string.IsNullOrEmpty(phone))
            {
                customerOptions["contact"] = phone;
            }

            Customer customer = await Task.Run(() => _client.Customer.Create(customerOptions));

            _logger.LogInformation("Created new Razorpay customer {CustomerId} for email {Email}",
                (string)customer["id"], email);

            return new RazorpayCustomerResult
            {
                CustomerId = (string)customer["id"],
                Name = customer["name"]?.ToString() ?? name,
                Email = customer["email"]?.ToString() ?? email,
                Contact = customer["contact"]?.ToString()
            };
        }
        catch (Razorpay.Api.Errors.BadRequestError ex) when (ex.Message.Contains("Customer already exists"))
        {
            // Race condition: customer was created between our check and create
            // Try to fetch the customer again
            _logger.LogWarning("Race condition detected - customer created between check and create for {Email}", email);
            var existingCustomer = await FindCustomerByEmailAsync(email);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }

            throw new InvalidOperationException("Failed to create or retrieve customer", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create/get Razorpay customer for {Email}", email);
            throw new InvalidOperationException("Failed to create customer", ex);
        }
    }

    private async Task<RazorpayCustomerResult?> FindCustomerByEmailAsync(string email)
    {
        try
        {
            // Razorpay API: GET /customers with email filter
            // The .NET SDK's Customer.All() supports filtering
            var options = new Dictionary<string, object>
            {
                { "email", email }
            };

            List<Customer> customers = await Task.Run(() => _client.Customer.All(options));

            if (customers != null && customers.Count > 0)
            {
                var customer = customers[0];
                return new RazorpayCustomerResult
                {
                    CustomerId = (string)customer["id"],
                    Name = customer["name"]?.ToString() ?? "",
                    Email = customer["email"]?.ToString() ?? email,
                    Contact = customer["contact"]?.ToString()
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search for existing customer by email {Email}", email);
            return null;
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
            CustomerId = payment["customer_id"]?.ToString(),
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
