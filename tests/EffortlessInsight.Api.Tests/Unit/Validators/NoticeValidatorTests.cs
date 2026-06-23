using EffortlessInsight.Api.Controllers;
using EffortlessInsight.Api.Validators;
using FluentAssertions;

namespace EffortlessInsight.Api.Tests.Unit.Validators;

public class PresignedUploadRequestValidatorTests
{
    private readonly PresignedUploadRequestValidator _validator = new();

    [Theory]
    [InlineData("document.pdf", "application/pdf", 1024)]
    [InlineData("notice.PDF", "application/pdf", 1024 * 1024)]
    [InlineData("scan.jpg", "image/jpeg", 500000)]
    [InlineData("photo.jpeg", "image/jpeg", 1000000)]
    [InlineData("image.png", "image/png", 2000000)]
    [InlineData("photo.heic", "image/heic", 5000000)]
    public void Validate_ValidRequest_PassesValidation(string fileName, string contentType, long contentLength)
    {
        // Arrange
        var request = new PresignedUploadRequest(fileName, contentType, contentLength);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "application/pdf", 1024, "FileName")] // Empty file name
    [InlineData("document.txt", "application/pdf", 1024, "FileName")] // Invalid extension
    [InlineData("document.exe", "application/pdf", 1024, "FileName")] // Invalid extension
    [InlineData("document.docx", "application/pdf", 1024, "FileName")] // Invalid extension
    public void Validate_InvalidFileName_FailsValidation(string fileName, string contentType, long contentLength, string expectedError)
    {
        // Arrange
        var request = new PresignedUploadRequest(fileName, contentType, contentLength);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == expectedError);
    }

    [Theory]
    [InlineData("document.pdf", "", 1024)]
    [InlineData("document.pdf", "text/plain", 1024)]
    [InlineData("document.pdf", "application/octet-stream", 1024)]
    public void Validate_InvalidContentType_FailsValidation(string fileName, string contentType, long contentLength)
    {
        // Arrange
        var request = new PresignedUploadRequest(fileName, contentType, contentLength);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentType");
    }

    [Theory]
    [InlineData(0)] // Zero
    [InlineData(-1)] // Negative
    [InlineData(26 * 1024 * 1024)] // Over 25MB
    [InlineData(100 * 1024 * 1024)] // Way over limit
    public void Validate_InvalidContentLength_FailsValidation(long contentLength)
    {
        // Arrange
        var request = new PresignedUploadRequest("document.pdf", "application/pdf", contentLength);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentLength");
    }

    [Fact]
    public void Validate_MaxAllowedSize_PassesValidation()
    {
        // Arrange
        var maxSize = 25 * 1024 * 1024; // 25MB
        var request = new PresignedUploadRequest("document.pdf", "application/pdf", maxSize);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TooLongFileName_FailsValidation()
    {
        // Arrange
        var longName = new string('a', 252) + ".pdf"; // 252 + 4 = 256 chars, exceeds 255 max
        var request = new PresignedUploadRequest(longName, "application/pdf", 1024);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileName");
    }
}

public class ConfirmUploadRequestValidatorTests
{
    private readonly ConfirmUploadRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_PassesValidation()
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: "org123/notices/notice456/document.pdf",
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024000,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: null,
            Tags: null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidRequestWithGstin_PassesValidation()
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: "org123/notices/notice456/document.pdf",
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024000,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: "27AABCU9603R1ZN",
            Tags: ["urgent", "gst"]);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyS3Key_FailsValidation(string s3Key)
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: s3Key,
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: null,
            Tags: null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "S3Key");
    }

    [Theory]
    [InlineData("../etc/passwd")] // Path traversal
    [InlineData("//double/slash")]
    [InlineData("/absolute/path")]
    public void Validate_InvalidS3Key_FailsValidation(string s3Key)
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: s3Key,
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: null,
            Tags: null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "S3Key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("not-hex-chars-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Validate_InvalidFileHash_FailsValidation(string fileHash)
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: "org/notices/doc.pdf",
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024,
            FileHash: fileHash,
            Gstin: null,
            Tags: null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileHash");
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("27AABCU9603R1Z")] // Too short
    [InlineData("27AABCU9603R1ZNN")] // Too long
    [InlineData("27aabcu9603r1zn")] // Lowercase
    public void Validate_InvalidGstin_FailsValidation(string gstin)
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: "org/notices/doc.pdf",
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: gstin,
            Tags: null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Gstin");
    }

    [Fact]
    public void Validate_TooManyTags_FailsValidation()
    {
        // Arrange
        var tags = Enumerable.Range(1, 21).Select(i => $"tag{i}").ToList();
        var request = new ConfirmUploadRequest(
            S3Key: "org/notices/doc.pdf",
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: null,
            Tags: tags);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Tags");
    }

    [Fact]
    public void Validate_EmptyTag_FailsValidation()
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: "org/notices/doc.pdf",
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: 1024,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: null,
            Tags: ["valid", "", "another"]);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Tags");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(26 * 1024 * 1024)] // Over 25MB
    public void Validate_InvalidFileSize_FailsValidation(int fileSize)
    {
        // Arrange
        var request = new ConfirmUploadRequest(
            S3Key: "org/notices/doc.pdf",
            FileName: "document.pdf",
            ContentType: "application/pdf",
            FileSize: fileSize,
            FileHash: "a".PadRight(64, 'a'),
            Gstin: null,
            Tags: null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileSize");
    }
}

public class UpdateNoticeStatusRequestValidatorTests
{
    private readonly UpdateNoticeStatusRequestValidator _validator = new();

    [Theory]
    [InlineData("uploaded")]
    [InlineData("processing")]
    [InlineData("analyzed")]
    [InlineData("in_progress")]
    [InlineData("responded")]
    [InlineData("closed")]
    [InlineData("archived")]
    [InlineData("failed")]
    public void Validate_ValidStatus_PassesValidation(string status)
    {
        // Arrange
        var request = new UpdateNoticeStatusRequest(status, null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("pending")]  // Not a valid status transition target
    [InlineData("draft")]    // Not a valid status transition target
    [InlineData("random_status")]
    public void Validate_InvalidStatus_FailsValidation(string status)
    {
        // Arrange
        var request = new UpdateNoticeStatusRequest(status, null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    [Fact]
    public void Validate_WithReason_PassesValidation()
    {
        // Arrange
        var request = new UpdateNoticeStatusRequest("closed", "Notice resolved successfully");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TooLongReason_FailsValidation()
    {
        // Arrange
        var longReason = new string('x', 1001); // Over 1000 chars
        var request = new UpdateNoticeStatusRequest("closed", longReason);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Reason");
    }
}

public class AssignNoticeRequestValidatorTests
{
    private readonly AssignNoticeRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidAssigneeId_PassesValidation()
    {
        // Arrange
        var request = new AssignNoticeRequest(Guid.NewGuid());

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyAssigneeId_FailsValidation()
    {
        // Arrange
        var request = new AssignNoticeRequest(Guid.Empty);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "AssigneeId");
    }
}

// Phase C Validator Tests

public class UpdateNoticeDetailsRequestValidatorTests
{
    private readonly UpdateNoticeDetailsRequestValidator _validator = new();

    [Fact]
    public void Validate_AllEmpty_PassesValidation()
    {
        // Arrange - empty update is valid (no changes)
        var request = new UpdateNoticeDetailsRequest();

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("critical")]
    public void Validate_ValidPriority_PassesValidation(string priority)
    {
        // Arrange
        var request = new UpdateNoticeDetailsRequest(Priority: priority);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidPriority_FailsValidation()
    {
        // Arrange
        var request = new UpdateNoticeDetailsRequest(Priority: "invalid");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Priority");
    }

    [Fact]
    public void Validate_NegativeAmount_FailsValidation()
    {
        // Arrange
        var request = new UpdateNoticeDetailsRequest(TaxAmount: -100);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TaxAmount");
    }

    [Fact]
    public void Validate_ValidGstin_PassesValidation()
    {
        // Arrange
        var request = new UpdateNoticeDetailsRequest(Gstin: "27AABCU9603R1ZN");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidGstin_FailsValidation()
    {
        // Arrange
        var request = new UpdateNoticeDetailsRequest(Gstin: "INVALID");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Gstin");
    }
}

public class AddCommentRequestValidatorTests
{
    private readonly AddCommentRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidComment_PassesValidation()
    {
        // Arrange
        var request = new AddCommentRequest("This is a valid comment");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyContent_FailsValidation()
    {
        // Arrange
        var request = new AddCommentRequest("");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Content");
    }

    [Fact]
    public void Validate_TooLongContent_FailsValidation()
    {
        // Arrange
        var longContent = new string('x', 10001);
        var request = new AddCommentRequest(longContent);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Content");
    }
}

public class CreateNoticeTaskRequestValidatorTests
{
    private readonly CreateNoticeTaskRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidTask_PassesValidation()
    {
        // Arrange
        var request = new CreateNoticeTaskRequest(
            Title: "Review documents",
            Description: "Review all submitted documents",
            DueDate: DateTime.UtcNow.AddDays(7),
            Priority: "high");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyTitle_FailsValidation()
    {
        // Arrange
        var request = new CreateNoticeTaskRequest(Title: "");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public void Validate_TooLongTitle_FailsValidation()
    {
        // Arrange
        var longTitle = new string('x', 256);
        var request = new CreateNoticeTaskRequest(Title: longTitle);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Title");
    }

    [Fact]
    public void Validate_InvalidPriority_FailsValidation()
    {
        // Arrange
        var request = new CreateNoticeTaskRequest(
            Title: "Valid title",
            Priority: "invalid");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Priority");
    }
}

public class UpdateNoticeTaskRequestValidatorTests
{
    private readonly UpdateNoticeTaskRequestValidator _validator = new();

    [Fact]
    public void Validate_AllEmpty_PassesValidation()
    {
        // Arrange - empty update is valid (no changes)
        var request = new UpdateNoticeTaskRequest();

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("in_progress")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public void Validate_ValidStatus_PassesValidation(string status)
    {
        // Arrange
        var request = new UpdateNoticeTaskRequest(Status: status);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidStatus_FailsValidation()
    {
        // Arrange
        var request = new UpdateNoticeTaskRequest(Status: "invalid");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Status");
    }
}

#region Phase D Validators

public class SaveDraftRequestValidatorTests
{
    private readonly SaveDraftRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidContent_PassesValidation()
    {
        // Arrange
        var request = new SaveDraftRequest("This is the draft content for the response.");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmptyContent_FailsValidation(string? content)
    {
        // Arrange
        var request = new SaveDraftRequest(content!);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DraftContent");
    }

    [Fact]
    public void Validate_ContentTooLong_FailsValidation()
    {
        // Arrange
        var longContent = new string('a', 100001);
        var request = new SaveDraftRequest(longContent);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DraftContent");
    }
}

public class MarkSubmittedRequestValidatorTests
{
    private readonly MarkSubmittedRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyRequest_PassesValidation()
    {
        // Arrange - both fields are optional
        var request = new MarkSubmittedRequest();

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("REF-123-456")]
    [InlineData("ACK/2026/001234")]
    public void Validate_ValidReference_PassesValidation(string reference)
    {
        // Arrange
        var request = new MarkSubmittedRequest(SubmissionReference: reference);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReferenceTooLong_FailsValidation()
    {
        // Arrange
        var longRef = new string('a', 101);
        var request = new MarkSubmittedRequest(SubmissionReference: longRef);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SubmissionReference");
    }

    [Theory]
    [InlineData("https://example.com/proof.pdf")]
    [InlineData("https://storage.googleapis.com/bucket/file")]
    public void Validate_ValidUrl_PassesValidation(string url)
    {
        // Arrange
        var request = new MarkSubmittedRequest(SubmissionProofUrl: url);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://invalid")]
    public void Validate_InvalidUrl_FailsValidation(string url)
    {
        // Arrange
        var request = new MarkSubmittedRequest(SubmissionProofUrl: url);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SubmissionProofUrl");
    }
}

public class CreateReminderRequestValidatorTests
{
    private readonly CreateReminderRequestValidator _validator = new();

    [Theory]
    [InlineData("email")]
    [InlineData("sms")]
    [InlineData("push")]
    [InlineData("whatsapp")]
    [InlineData("EMAIL")]
    public void Validate_ValidReminderType_PassesValidation(string reminderType)
    {
        // Arrange
        var request = new CreateReminderRequest(reminderType, DateTime.UtcNow.AddDays(1));

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("telegram")]
    [InlineData("slack")]
    public void Validate_InvalidReminderType_FailsValidation(string reminderType)
    {
        // Arrange
        var request = new CreateReminderRequest(reminderType, DateTime.UtcNow.AddDays(1));

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ReminderType");
    }

    [Fact]
    public void Validate_FutureRemindAt_PassesValidation()
    {
        // Arrange
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddHours(1));

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PastRemindAt_FailsValidation()
    {
        // Arrange
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddHours(-1));

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RemindAt");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(365)]
    public void Validate_ValidDaysBefore_PassesValidation(int daysBefore)
    {
        // Arrange
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddDays(1), daysBefore);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void Validate_InvalidDaysBefore_FailsValidation(int daysBefore)
    {
        // Arrange
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddDays(1), daysBefore);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DaysBefore");
    }

    [Fact]
    public void Validate_NullDaysBefore_PassesValidation()
    {
        // Arrange - DaysBefore is optional
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddDays(1), null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_BoundaryDaysBefore_PassesValidation()
    {
        // Arrange - Test boundary values
        var request1 = new CreateReminderRequest("email", DateTime.UtcNow.AddDays(1), 1);
        var request365 = new CreateReminderRequest("email", DateTime.UtcNow.AddDays(1), 365);

        // Act
        var result1 = _validator.Validate(request1);
        var result365 = _validator.Validate(request365);

        // Assert
        result1.IsValid.Should().BeTrue();
        result365.IsValid.Should().BeTrue();
    }
}

#endregion

#region Additional Edge Case Tests

public class SaveDraftRequestEdgeCaseTests
{
    private readonly SaveDraftRequestValidator _validator = new();

    [Fact]
    public void Validate_WhitespaceOnlyContent_FailsValidation()
    {
        // Arrange
        var request = new SaveDraftRequest("   ");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ExactlyMaxLength_PassesValidation()
    {
        // Arrange - 100000 characters is the max
        var content = new string('a', 100000);
        var request = new SaveDraftRequest(content);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_SingleCharacter_PassesValidation()
    {
        // Arrange
        var request = new SaveDraftRequest("x");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnicodeContent_PassesValidation()
    {
        // Arrange
        var request = new SaveDraftRequest("यह हिंदी में ड्राफ्ट सामग्री है। 这是中文草稿内容。");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

public class MarkSubmittedRequestEdgeCaseTests
{
    private readonly MarkSubmittedRequestValidator _validator = new();

    [Theory]
    [InlineData("http://example.com/proof")]
    [InlineData("https://example.com/proof")]
    [InlineData("https://s3.amazonaws.com/bucket/key")]
    public void Validate_ValidHttpUrls_PassesValidation(string url)
    {
        // Arrange
        var request = new MarkSubmittedRequest(SubmissionProofUrl: url);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("file:///local/path")]
    [InlineData("ftp://server/file")]
    [InlineData("mailto:test@test.com")]
    public void Validate_NonHttpUrls_FailsValidation(string url)
    {
        // Arrange
        var request = new MarkSubmittedRequest(SubmissionProofUrl: url);

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_BothFieldsProvided_PassesValidation()
    {
        // Arrange
        var request = new MarkSubmittedRequest(
            SubmissionReference: "REF-001",
            SubmissionProofUrl: "https://example.com/proof.pdf");

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ExactlyMaxLength_PassesValidation()
    {
        // Arrange
        var request = new MarkSubmittedRequest(
            SubmissionReference: new string('a', 100),
            SubmissionProofUrl: "https://example.com/" + new string('a', 473)); // Total ~500

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

public class CreateReminderRequestEdgeCaseTests
{
    private readonly CreateReminderRequestValidator _validator = new();

    [Theory]
    [InlineData("Email")]
    [InlineData("SMS")]
    [InlineData("Push")]
    [InlineData("WhatsApp")]
    [InlineData("EMAIL")]
    [InlineData("WHATSAPP")]
    public void Validate_CaseInsensitiveType_PassesValidation(string type)
    {
        // Arrange
        var request = new CreateReminderRequest(type, DateTime.UtcNow.AddDays(1));

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RemindAtJustAfterNow_PassesValidation()
    {
        // Arrange - 5 minutes grace period
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddMinutes(1));

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RemindAtFarFuture_PassesValidation()
    {
        // Arrange
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddYears(1));

        // Act
        var result = _validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

#endregion
