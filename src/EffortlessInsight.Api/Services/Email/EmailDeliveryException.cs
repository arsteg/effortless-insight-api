namespace EffortlessInsight.Api.Services.Email;

/// <summary>
/// Thrown when an email cannot be delivered to the provider — invalid
/// recipient, provider rejection, or a transport failure. Wraps the
/// provider-specific exception as <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    /// <summary>Provider error code (e.g. SES "MessageRejected"), when available.</summary>
    public string? ErrorCode { get; }

    public EmailDeliveryException(string message, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
