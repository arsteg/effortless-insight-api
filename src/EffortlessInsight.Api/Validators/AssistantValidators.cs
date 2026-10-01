using EffortlessInsight.Api.DTOs;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

/// <summary>
/// Validator for SendAssistantMessageRequest.
/// </summary>
public class SendAssistantMessageRequestValidator : AbstractValidator<SendAssistantMessageRequest>
{
    public SendAssistantMessageRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .WithMessage("Message is required")
            .MaximumLength(8000)
            .WithMessage("Message must be 8,000 characters or less");

        RuleFor(x => x.Context!.Route)
            .MaximumLength(300)
            .When(x => x.Context?.Route != null);
    }
}

/// <summary>
/// Validator for CreateAssistantConversationRequest.
/// </summary>
public class CreateAssistantConversationRequestValidator : AbstractValidator<CreateAssistantConversationRequest>
{
    public CreateAssistantConversationRequestValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(255)
            .When(x => x.Title != null);

        RuleFor(x => x.Platform)
            .Must(p => p is null or "web" or "mobile")
            .WithMessage("Platform must be 'web' or 'mobile'");
    }
}

/// <summary>
/// Validator for UpdateAssistantConversationRequest.
/// </summary>
public class UpdateAssistantConversationRequestValidator : AbstractValidator<UpdateAssistantConversationRequest>
{
    public UpdateAssistantConversationRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(255);
    }
}
