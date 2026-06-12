using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

// =============================================================================
// TASK VALIDATORS
// =============================================================================

public class CreateTaskValidator : AbstractValidator<CreateTaskDto>
{
    public CreateTaskValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .Length(5, 200).WithMessage("Title must be between 5 and 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Assignees)
            .Must(a => a == null || (a.Count >= 1 && a.Count <= 5))
            .WithMessage("Must have between 1 and 5 assignees");

        RuleFor(x => x.Priority)
            .Must(p => p == null || TaskPriorityValues.IsValid(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", TaskPriorityValues.All)}");

        RuleFor(x => x.DueDate)
            .Must(d => d == null || d > DateTime.UtcNow)
            .WithMessage("Due date must be in the future")
            .When(x => x.DueDate.HasValue);

        RuleFor(x => x.EstimatedHours)
            .InclusiveBetween(0.25m, 100m)
            .WithMessage("Estimated hours must be between 0.25 and 100")
            .When(x => x.EstimatedHours.HasValue);

        RuleFor(x => x.Labels)
            .Must(l => l == null || l.Count <= 10)
            .WithMessage("Maximum 10 labels allowed");
    }
}

public class UpdateTaskValidator : AbstractValidator<UpdateTaskDto>
{
    public UpdateTaskValidator()
    {
        RuleFor(x => x.Title)
            .Length(5, 200).WithMessage("Title must be between 5 and 200 characters")
            .When(x => x.Title != null);

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Assignees)
            .Must(a => a == null || (a.Count >= 1 && a.Count <= 5))
            .WithMessage("Must have between 1 and 5 assignees");

        RuleFor(x => x.Priority)
            .Must(p => p == null || TaskPriorityValues.IsValid(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", TaskPriorityValues.All)}");

        RuleFor(x => x.Status)
            .Must(s => s == null || TaskStatusValues.IsValid(s))
            .WithMessage($"Status must be one of: {string.Join(", ", TaskStatusValues.All)}");

        RuleFor(x => x.EstimatedHours)
            .InclusiveBetween(0.25m, 100m)
            .WithMessage("Estimated hours must be between 0.25 and 100")
            .When(x => x.EstimatedHours.HasValue);

        RuleFor(x => x.ActualHours)
            .InclusiveBetween(0m, 1000m)
            .WithMessage("Actual hours must be between 0 and 1000")
            .When(x => x.ActualHours.HasValue);

        RuleFor(x => x.Labels)
            .Must(l => l == null || l.Count <= 10)
            .WithMessage("Maximum 10 labels allowed");
    }
}

// =============================================================================
// COMMENT VALIDATORS
// =============================================================================

public class CreateCommentValidator : AbstractValidator<CreateCommentRequestDto>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required")
            .Length(1, 5000).WithMessage("Content must be between 1 and 5000 characters");

        RuleFor(x => x.Visibility)
            .Must(v => v == null || CommentVisibility.IsValid(v))
            .WithMessage($"Visibility must be one of: {string.Join(", ", CommentVisibility.Values)}");

        RuleFor(x => x.AttachmentUrls)
            .Must(a => a == null || a.Count <= 3)
            .WithMessage("Maximum 3 attachments allowed per comment");
    }
}

public class UpdateCommentValidator : AbstractValidator<UpdateCommentDto>
{
    public UpdateCommentValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required")
            .Length(1, 5000).WithMessage("Content must be between 1 and 5000 characters");
    }
}

public class AddReactionValidator : AbstractValidator<AddReactionDto>
{
    public AddReactionValidator()
    {
        RuleFor(x => x.Emoji)
            .NotEmpty().WithMessage("Emoji is required")
            .Must(AllowedReactions.IsValid)
            .WithMessage($"Emoji must be one of: {string.Join(" ", AllowedReactions.All)}");
    }
}

// =============================================================================
// DOCUMENT REQUEST VALIDATORS
// =============================================================================

public class CreateDocumentRequestValidator : AbstractValidator<CreateDocumentRequestDto>
{
    public CreateDocumentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .Length(5, 100).WithMessage("Title must be between 5 and 100 characters");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required")
            .Length(10, 1000).WithMessage("Description must be between 10 and 1000 characters");

        RuleFor(x => x.RequestedFrom)
            .NotEmpty().WithMessage("RequestedFrom is required");

        RuleFor(x => x.DueDate)
            .Must(d => d > DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Due date must be in the future");

        RuleFor(x => x.Priority)
            .Must(p => p == null || TaskPriorityValues.IsValid(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", TaskPriorityValues.All)}");

        RuleFor(x => x.AcceptedFormats)
            .Must(BeValidFormats)
            .WithMessage("Invalid file format specified")
            .When(x => x.AcceptedFormats != null && x.AcceptedFormats.Count > 0);
    }

    private static bool BeValidFormats(List<string>? formats)
    {
        if (formats == null) return true;
        var validFormats = new[] { "pdf", "doc", "docx", "xls", "xlsx", "zip", "jpg", "jpeg", "png", "gif" };
        return formats.All(f => validFormats.Contains(f.ToLowerInvariant()));
    }
}

public class UpdateDocumentRequestValidator : AbstractValidator<UpdateDocumentRequestDto>
{
    public UpdateDocumentRequestValidator()
    {
        RuleFor(x => x.Title)
            .Length(5, 100).WithMessage("Title must be between 5 and 100 characters")
            .When(x => x.Title != null);

        RuleFor(x => x.Description)
            .Length(10, 1000).WithMessage("Description must be between 10 and 1000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Priority)
            .Must(p => p == null || TaskPriorityValues.IsValid(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", TaskPriorityValues.All)}");

        RuleFor(x => x.Status)
            .Must(s => s == null || DocumentRequestStatus.IsValid(s))
            .WithMessage($"Status must be one of: {string.Join(", ", DocumentRequestStatus.All)}");
    }
}

// =============================================================================
// TEMPLATE VALIDATORS
// =============================================================================

public class CreateTaskTemplateValidator : AbstractValidator<CreateTaskTemplateDto>
{
    public CreateTaskTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.DefaultTitle)
            .NotEmpty().WithMessage("Default title is required")
            .MaximumLength(200).WithMessage("Default title must not exceed 200 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.DefaultPriority)
            .Must(p => p == null || TaskPriorityValues.IsValid(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", TaskPriorityValues.All)}");

        RuleFor(x => x.DefaultEstimatedHours)
            .InclusiveBetween(0.25m, 100m)
            .When(x => x.DefaultEstimatedHours.HasValue);
    }
}

public class CreateDocumentRequestTemplateValidator : AbstractValidator<CreateDocumentRequestTemplateDto>
{
    public CreateDocumentRequestTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters");

        RuleFor(x => x.TitleTemplate)
            .NotEmpty().WithMessage("Title template is required")
            .MaximumLength(100).WithMessage("Title template must not exceed 100 characters");

        RuleFor(x => x.DescriptionTemplate)
            .NotEmpty().WithMessage("Description template is required");

        RuleFor(x => x.DefaultPriority)
            .Must(p => p == null || TaskPriorityValues.IsValid(p))
            .WithMessage($"Priority must be one of: {string.Join(", ", TaskPriorityValues.All)}");

        RuleFor(x => x.DefaultDueDays)
            .InclusiveBetween(1, 90)
            .When(x => x.DefaultDueDays.HasValue);
    }
}

// =============================================================================
// FILE VALIDATORS
// =============================================================================

public class CreateFolderValidator : AbstractValidator<CreateFolderDto>
{
    public CreateFolderValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters")
            .Matches(@"^[a-zA-Z0-9_\-\s]+$").WithMessage("Name can only contain letters, numbers, underscores, hyphens, and spaces");
    }
}
