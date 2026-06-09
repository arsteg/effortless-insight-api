using EffortlessInsight.Api.Controllers;
using EffortlessInsight.Api.Data.Entities;
using FluentValidation;

namespace EffortlessInsight.Api.Validators;

public class PresignedUploadRequestValidator : AbstractValidator<PresignedUploadRequest>
{
    private static readonly string[] AllowedContentTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/heic",
        "image/heif"
    ];

    private static readonly string[] AllowedExtensions =
    [
        ".pdf", ".jpg", ".jpeg", ".png", ".heic", ".heif"
    ];

    private const long MaxFileSize = 25 * 1024 * 1024; // 25MB

    public PresignedUploadRequestValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required")
            .MaximumLength(255).WithMessage("File name cannot exceed 255 characters")
            .Must(HaveAllowedExtension)
            .WithMessage($"File must have one of the following extensions: {string.Join(", ", AllowedExtensions)}");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Content type is required")
            .Must(BeAllowedContentType)
            .WithMessage($"Content type must be one of: {string.Join(", ", AllowedContentTypes)}");

        RuleFor(x => x.ContentLength)
            .GreaterThan(0).WithMessage("Content length must be greater than 0")
            .LessThanOrEqualTo(MaxFileSize)
            .WithMessage($"File size cannot exceed {MaxFileSize / (1024 * 1024)}MB");
    }

    private static bool HaveAllowedExtension(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return AllowedExtensions.Contains(extension);
    }

    private static bool BeAllowedContentType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        return AllowedContentTypes.Contains(contentType.ToLowerInvariant());
    }
}

public class ConfirmUploadRequestValidator : AbstractValidator<ConfirmUploadRequest>
{
    public ConfirmUploadRequestValidator()
    {
        RuleFor(x => x.S3Key)
            .NotEmpty().WithMessage("S3 key is required")
            .MaximumLength(500).WithMessage("S3 key cannot exceed 500 characters")
            .Must(BeValidS3Key).WithMessage("Invalid S3 key format");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required")
            .MaximumLength(255).WithMessage("File name cannot exceed 255 characters");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Content type is required")
            .MaximumLength(100).WithMessage("Content type cannot exceed 100 characters");

        RuleFor(x => x.FileSize)
            .GreaterThan(0).WithMessage("File size must be greater than 0")
            .LessThanOrEqualTo(25 * 1024 * 1024).WithMessage("File size cannot exceed 25MB");

        RuleFor(x => x.FileHash)
            .NotEmpty().WithMessage("File hash is required")
            .Length(64).WithMessage("File hash must be 64 characters (SHA-256)")
            .Matches("^[a-fA-F0-9]+$").WithMessage("File hash must be a valid hexadecimal string");

        RuleFor(x => x.Gstin)
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
            .WithMessage("Invalid GSTIN format")
            .When(x => !string.IsNullOrEmpty(x.Gstin));

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 20)
            .WithMessage("Maximum 20 tags allowed")
            .Must(tags => tags == null || tags.All(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 50))
            .WithMessage("Each tag must be non-empty and at most 50 characters")
            .When(x => x.Tags != null);
    }

    private static bool BeValidS3Key(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        // S3 key should follow pattern: {org_id}/notices/{notice_id}/{filename}
        // Just basic validation - should not contain dangerous characters
        return !key.Contains("..") && !key.StartsWith("/") && !key.Contains("//");
    }
}

public class UpdateNoticeStatusRequestValidator : AbstractValidator<UpdateNoticeStatusRequest>
{
    public UpdateNoticeStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required")
            .Must(BeValidStatus)
            .WithMessage($"Status must be one of: {string.Join(", ", NoticeStatus.All)}");

        RuleFor(x => x.Reason)
            .MaximumLength(1000).WithMessage("Reason cannot exceed 1000 characters")
            .When(x => !string.IsNullOrEmpty(x.Reason));
    }

    private static bool BeValidStatus(string status)
    {
        if (string.IsNullOrEmpty(status)) return false;
        return NoticeStatus.All.Contains(status.ToLowerInvariant());
    }
}

public class AssignNoticeRequestValidator : AbstractValidator<AssignNoticeRequest>
{
    public AssignNoticeRequestValidator()
    {
        RuleFor(x => x.AssigneeId)
            .NotEmpty().WithMessage("Assignee ID is required");
    }
}

public class UploadNoticeRequestValidator : AbstractValidator<UploadNoticeRequest>
{
    public UploadNoticeRequestValidator()
    {
        RuleFor(x => x.Gstin)
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
            .WithMessage("Invalid GSTIN format")
            .When(x => !string.IsNullOrEmpty(x.Gstin));

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 20)
            .WithMessage("Maximum 20 tags allowed")
            .Must(tags => tags == null || tags.All(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 50))
            .WithMessage("Each tag must be non-empty and at most 50 characters")
            .When(x => x.Tags != null);
    }
}

// Phase C Validators

public class UpdateNoticeDetailsRequestValidator : AbstractValidator<UpdateNoticeDetailsRequest>
{
    private static readonly string[] ValidPriorities = ["low", "medium", "high", "critical"];

    public UpdateNoticeDetailsRequestValidator()
    {
        RuleFor(x => x.NoticeNumber)
            .MaximumLength(100).WithMessage("Notice number cannot exceed 100 characters")
            .When(x => x.NoticeNumber != null);

        RuleFor(x => x.NoticeType)
            .MaximumLength(50).WithMessage("Notice type cannot exceed 50 characters")
            .When(x => x.NoticeType != null);

        RuleFor(x => x.NoticeCategory)
            .MaximumLength(50).WithMessage("Notice category cannot exceed 50 characters")
            .When(x => x.NoticeCategory != null);

        RuleFor(x => x.Gstin)
            .Length(15).WithMessage("GSTIN must be exactly 15 characters")
            .Matches(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$")
            .WithMessage("Invalid GSTIN format")
            .When(x => !string.IsNullOrEmpty(x.Gstin));

        RuleFor(x => x.TaxAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Tax amount cannot be negative")
            .When(x => x.TaxAmount.HasValue);

        RuleFor(x => x.PenaltyAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Penalty amount cannot be negative")
            .When(x => x.PenaltyAmount.HasValue);

        RuleFor(x => x.InterestAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Interest amount cannot be negative")
            .When(x => x.InterestAmount.HasValue);

        RuleFor(x => x.IssuingAuthority)
            .MaximumLength(200).WithMessage("Issuing authority cannot exceed 200 characters")
            .When(x => x.IssuingAuthority != null);

        RuleFor(x => x.Priority)
            .Must(p => ValidPriorities.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Priority must be one of: {string.Join(", ", ValidPriorities)}")
            .When(x => x.Priority != null);

        RuleFor(x => x.Tags)
            .Must(tags => tags == null || tags.Count <= 20)
            .WithMessage("Maximum 20 tags allowed")
            .Must(tags => tags == null || tags.All(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 50))
            .WithMessage("Each tag must be non-empty and at most 50 characters")
            .When(x => x.Tags != null);
    }
}

public class AddCommentRequestValidator : AbstractValidator<AddCommentRequest>
{
    public AddCommentRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Comment content is required")
            .MaximumLength(10000).WithMessage("Comment cannot exceed 10,000 characters");
    }
}

public class CreateNoticeTaskRequestValidator : AbstractValidator<CreateNoticeTaskRequest>
{
    private static readonly string[] ValidPriorities = ["low", "medium", "high", "critical"];

    public CreateNoticeTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Task title is required")
            .MaximumLength(255).WithMessage("Task title cannot exceed 255 characters");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Task description cannot exceed 2,000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Priority)
            .Must(p => p == null || ValidPriorities.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Priority must be one of: {string.Join(", ", ValidPriorities)}");

        RuleFor(x => x.DueDate)
            .GreaterThan(DateTime.UtcNow.AddMinutes(-5))
            .WithMessage("Due date must be in the future")
            .When(x => x.DueDate.HasValue);
    }
}

public class UpdateNoticeTaskRequestValidator : AbstractValidator<UpdateNoticeTaskRequest>
{
    private static readonly string[] ValidPriorities = ["low", "medium", "high", "critical"];
    private static readonly string[] ValidStatuses = ["pending", "in_progress", "completed", "cancelled"];

    public UpdateNoticeTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(255).WithMessage("Task title cannot exceed 255 characters")
            .When(x => x.Title != null);

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Task description cannot exceed 2,000 characters")
            .When(x => x.Description != null);

        RuleFor(x => x.Priority)
            .Must(p => ValidPriorities.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Priority must be one of: {string.Join(", ", ValidPriorities)}")
            .When(x => x.Priority != null);

        RuleFor(x => x.Status)
            .Must(s => ValidStatuses.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}")
            .When(x => x.Status != null);
    }
}

// Phase D Validators

public class SaveDraftRequestValidator : AbstractValidator<SaveDraftRequest>
{
    public SaveDraftRequestValidator()
    {
        RuleFor(x => x.DraftContent)
            .NotEmpty().WithMessage("Draft content is required")
            .MaximumLength(100000).WithMessage("Draft content cannot exceed 100,000 characters");
    }
}

public class MarkSubmittedRequestValidator : AbstractValidator<MarkSubmittedRequest>
{
    public MarkSubmittedRequestValidator()
    {
        RuleFor(x => x.SubmissionReference)
            .MaximumLength(100).WithMessage("Submission reference cannot exceed 100 characters")
            .When(x => x.SubmissionReference != null);

        RuleFor(x => x.SubmissionProofUrl)
            .MaximumLength(500).WithMessage("Submission proof URL cannot exceed 500 characters")
            .Must(BeValidHttpUrl)
            .WithMessage("Submission proof URL must be a valid HTTP or HTTPS URL")
            .When(x => x.SubmissionProofUrl != null);
    }

    private static bool BeValidHttpUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

public class CreateReminderRequestValidator : AbstractValidator<CreateReminderRequest>
{
    private static readonly string[] ValidReminderTypes = ["email", "sms", "push", "whatsapp"];

    public CreateReminderRequestValidator()
    {
        RuleFor(x => x.ReminderType)
            .NotEmpty().WithMessage("Reminder type is required")
            .Must(t => ValidReminderTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Reminder type must be one of: {string.Join(", ", ValidReminderTypes)}");

        RuleFor(x => x.RemindAt)
            .GreaterThan(DateTime.UtcNow.AddMinutes(-5))
            .WithMessage("Reminder time must be in the future");

        RuleFor(x => x.DaysBefore)
            .InclusiveBetween(1, 365).WithMessage("Days before must be between 1 and 365")
            .When(x => x.DaysBefore.HasValue);
    }
}
