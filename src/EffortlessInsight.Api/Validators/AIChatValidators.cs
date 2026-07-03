using EffortlessInsight.Api.DTOs;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

/// <summary>
/// Validator for SendMessageRequest.
/// </summary>
public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty()
            .WithMessage("Message is required")
            .MaximumLength(10000)
            .WithMessage("Message must be 10,000 characters or less")
            .Must(NotContainPromptInjection)
            .WithMessage("Message contains invalid content");
    }

    private static bool NotContainPromptInjection(string message)
    {
        if (string.IsNullOrEmpty(message))
            return true;

        // Check for common prompt injection patterns
        var lowerMessage = message.ToLowerInvariant();

        // Block attempts to override system instructions
        var dangerousPatterns = new[]
        {
            "ignore all previous",
            "ignore your instructions",
            "disregard your training",
            "forget your instructions",
            "you are now",
            "new instructions:",
            "system prompt:",
            "override:"
        };

        return !dangerousPatterns.Any(pattern => lowerMessage.Contains(pattern));
    }
}

/// <summary>
/// Validator for CreateConversationRequest.
/// </summary>
public class CreateConversationRequestValidator : AbstractValidator<CreateConversationRequest>
{
    public CreateConversationRequestValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(255)
            .WithMessage("Title must be 255 characters or less")
            .When(x => x.Title != null);
    }
}

/// <summary>
/// Validator for MessageFeedbackRequest.
/// </summary>
public class MessageFeedbackRequestValidator : AbstractValidator<MessageFeedbackRequest>
{
    public MessageFeedbackRequestValidator()
    {
        RuleFor(x => x.Rating)
            .Must(r => r == 1 || r == -1)
            .WithMessage("Rating must be 1 (positive) or -1 (negative)");

        RuleFor(x => x.FeedbackText)
            .MaximumLength(2000)
            .WithMessage("Feedback text must be 2,000 characters or less")
            .When(x => x.FeedbackText != null);
    }
}
