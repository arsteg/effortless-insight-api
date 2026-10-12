using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Notices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

#region FileValidationService Tests

public class FileValidationServiceTests
{
    private readonly FileValidationService _service;

    public FileValidationServiceTests()
    {
        var logger = new Mock<ILogger<FileValidationService>>();
        _service = new FileValidationService(logger.Object);
    }

    [Theory]
    [InlineData("document.pdf", "application/pdf", 1024)]
    [InlineData("image.jpg", "image/jpeg", 500000)]
    [InlineData("scan.jpeg", "image/jpeg", 1000000)]
    [InlineData("photo.png", "image/png", 2000000)]
    [InlineData("notice.heic", "image/heic", 5000000)]
    [InlineData("notice.heif", "image/heif", 1000)]
    public void ValidateMetadata_ValidInput_ReturnsValid(string fileName, string contentType, long fileSize)
    {
        // Act
        var result = _service.ValidateMetadata(fileName, contentType, fileSize);

        // Assert
        result.IsValid.Should().BeTrue();
        result.SanitizedFileName.Should().NotBeNullOrEmpty();
        result.DetectedMimeType.Should().Be(contentType);
    }

    [Theory]
    [InlineData("document.txt", "text/plain", 1024)]
    [InlineData("script.exe", "application/octet-stream", 1024)]
    [InlineData("file.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 1024)]
    public void ValidateMetadata_InvalidContentType_ReturnsInvalid(string fileName, string contentType, long fileSize)
    {
        // Act
        var result = _service.ValidateMetadata(fileName, contentType, fileSize);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("document.txt")]
    [InlineData("script.exe")]
    [InlineData("file.docx")]
    [InlineData("archive.zip")]
    public void ValidateMetadata_InvalidExtension_ReturnsInvalid(string fileName)
    {
        // Act
        var result = _service.ValidateMetadata(fileName, "application/pdf", 1024);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateMetadata_FileTooLarge_ReturnsInvalid()
    {
        // Arrange
        var fileSize = 26L * 1024 * 1024; // 26MB

        // Act
        var result = _service.ValidateMetadata("document.pdf", "application/pdf", fileSize);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateMetadata_ZeroSize_ReturnsInvalid()
    {
        // Act
        var result = _service.ValidateMetadata("document.pdf", "application/pdf", 0);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateMetadata_NegativeSize_ReturnsInvalid()
    {
        // Act
        var result = _service.ValidateMetadata("document.pdf", "application/pdf", -100);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateMetadata_EmptyFileName_ReturnsInvalid()
    {
        // Act
        var result = _service.ValidateMetadata("", "application/pdf", 1024);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("../../../etc/passwd.pdf")]
    [InlineData("..\\..\\windows\\system.pdf")]
    [InlineData("file<>name.pdf")]
    [InlineData("file|name.pdf")]
    [InlineData("file:name.pdf")]
    [InlineData("file\"name.pdf")]
    [InlineData("file?name.pdf")]
    [InlineData("file*name.pdf")]
    public void SanitizeFileName_RemovesDangerousCharacters(string input)
    {
        // Act
        var result = _service.SanitizeFileName(input);

        // Assert
        result.Should().NotContain("..");
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().NotContain("|");
        result.Should().NotContain(":");
        result.Should().NotContain("\"");
        result.Should().NotContain("?");
        result.Should().NotContain("*");
        result.Should().EndWith(".pdf");
    }

    [Fact]
    public void SanitizeFileName_PreservesValidName()
    {
        // Arrange
        var validName = "valid-document_2024.pdf";

        // Act
        var result = _service.SanitizeFileName(validName);

        // Assert
        result.Should().Be(validName);
    }

    [Fact]
    public void SanitizeFileName_TruncatesLongNames()
    {
        // Arrange
        var longName = new string('a', 300) + ".pdf";

        // Act
        var result = _service.SanitizeFileName(longName);

        // Assert
        result.Length.Should().BeLessOrEqualTo(255);
        result.Should().EndWith(".pdf");
    }
}

#endregion

#region NoticeWorkflowService Tests

public class NoticeWorkflowServiceTests
{
    private readonly NoticeWorkflowService _service;

    public NoticeWorkflowServiceTests()
    {
        var logger = new Mock<ILogger<NoticeWorkflowService>>();
        _service = new NoticeWorkflowService(logger.Object);
    }

    [Theory]
    [InlineData(NoticeStatus.Uploaded, NoticeStatus.Processing, true)]
    [InlineData(NoticeStatus.Processing, NoticeStatus.Analyzed, true)]
    [InlineData(NoticeStatus.Processing, NoticeStatus.Failed, true)]
    [InlineData(NoticeStatus.Analyzed, NoticeStatus.InProgress, true)]
    [InlineData(NoticeStatus.InProgress, NoticeStatus.Responded, true)]
    [InlineData(NoticeStatus.Responded, NoticeStatus.Closed, true)]
    [InlineData(NoticeStatus.Closed, NoticeStatus.Archived, true)]
    [InlineData(NoticeStatus.Analyzed, NoticeStatus.Archived, true)]
    [InlineData(NoticeStatus.Failed, NoticeStatus.Archived, true)]
    [InlineData(NoticeStatus.Archived, NoticeStatus.Analyzed, true)]
    [InlineData(NoticeStatus.Archived, NoticeStatus.Closed, true)]
    [InlineData(NoticeStatus.Archived, NoticeStatus.Failed, true)]
    public void ValidateTransition_ValidTransitions_ReturnsAllowed(string from, string to, bool expectedAllowed)
    {
        // Act
        var result = _service.ValidateTransition(from, to);

        // Assert
        result.IsAllowed.Should().Be(expectedAllowed);
    }

    [Theory]
    [InlineData(NoticeStatus.Uploaded, NoticeStatus.Closed)]
    [InlineData(NoticeStatus.Analyzed, NoticeStatus.Uploaded)]
    [InlineData(NoticeStatus.Closed, NoticeStatus.Uploaded)]
    [InlineData(NoticeStatus.Archived, NoticeStatus.InProgress)]
    public void ValidateTransition_InvalidTransitions_ReturnsNotAllowed(string from, string to)
    {
        // Act
        var result = _service.ValidateTransition(from, to);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(NoticeStatus.Archived)]
    [InlineData(NoticeStatus.Analyzed)]
    public void ValidateTransition_SameStatus_ReturnsNotAllowed(string status)
    {
        // Archiving an archived notice must not report a silent success
        var result = _service.ValidateTransition(status, status);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be("NO_STATUS_CHANGE");
    }

    [Fact]
    public void GetAllowedTransitions_ForAnalyzed_ReturnsValidOptions()
    {
        // Act
        var result = _service.GetAllowedTransitions(NoticeStatus.Analyzed);

        // Assert
        result.Should().Contain(NoticeStatus.InProgress);
    }

    [Fact]
    public void GetAllowedTransitions_ForArchived_ReturnsAnalyzed()
    {
        // Archived notices can be restored to analyzed
        // Act
        var result = _service.GetAllowedTransitions(NoticeStatus.Archived);

        // Assert
        result.Should().Contain(NoticeStatus.Analyzed);
    }

    [Theory]
    [InlineData(NoticeStatus.Analyzed, NoticeStatus.Closed, true)] // Closing without response
    [InlineData(NoticeStatus.InProgress, NoticeStatus.Analyzed, true)] // Going back
    [InlineData(NoticeStatus.Responded, NoticeStatus.InProgress, true)] // Reopening
    [InlineData(NoticeStatus.Analyzed, NoticeStatus.Archived, false)] // Archiving
    [InlineData(NoticeStatus.Failed, NoticeStatus.Archived, false)] // Archiving
    [InlineData(NoticeStatus.Archived, NoticeStatus.Analyzed, false)] // Restoring
    [InlineData(NoticeStatus.Archived, NoticeStatus.Closed, false)] // Restoring
    public void RequiresReason_ForSpecificTransitions_ReturnsTrue(string from, string to, bool requiresReason)
    {
        // Act
        var result = _service.RequiresReason(from, to);

        // Assert
        result.Should().Be(requiresReason);
    }

    [Fact]
    public void GetStatusAfterProcessing_Success_ReturnsAnalyzed()
    {
        // Act
        var result = _service.GetStatusAfterProcessing(true);

        // Assert
        result.Should().Be(NoticeStatus.Analyzed);
    }

    [Fact]
    public void GetStatusAfterProcessing_Failure_ReturnsFailed()
    {
        // Act
        var result = _service.GetStatusAfterProcessing(false);

        // Assert
        result.Should().Be(NoticeStatus.Failed);
    }

    [Fact]
    public void GetInitialStatus_ReturnsUploaded()
    {
        // Act
        var result = _service.GetInitialStatus();

        // Assert
        result.Should().Be(NoticeStatus.Uploaded);
    }

    #region Priority Calculation Tests

    // Priority scoring system:
    // - Critical category (investigation, demand, recovery): +4
    // - High priority type (DRC-01, DRC-07, etc): +3
    // - Deadline overdue: +5
    // - Deadline <= 3 days: +4
    // - Deadline <= 7 days: +3
    // - Deadline <= 15 days: +2
    // - Deadline <= 30 days: +1
    // - Amount >= 10 lakh: +3
    // - Amount >= 5 lakh: +2
    // - Amount >= 1 lakh: +1
    //
    // Final priority: >=8 Critical, >=5 High, >=2 Medium, <2 Low

    [Fact]
    public void CalculatePriority_WithHighPriorityType_ReturnsMedium()
    {
        // Arrange - DRC-07 alone = 3 points → Medium
        var noticeType = "DRC-07";

        // Act
        var result = _service.CalculatePriority(noticeType, null, null, null);

        // Assert
        result.Should().Be(NoticePriority.Medium);
    }

    [Fact]
    public void CalculatePriority_WithCriticalCategory_ReturnsMedium()
    {
        // Arrange - demand category = 4 points → Medium
        var category = "demand";

        // Act
        var result = _service.CalculatePriority(null, category, null, null);

        // Assert
        result.Should().Be(NoticePriority.Medium);
    }

    [Fact]
    public void CalculatePriority_WithUrgentDeadline_ReturnsMedium()
    {
        // Arrange - deadline within 3 days = 4 points → Medium
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        // Act
        var result = _service.CalculatePriority(null, null, deadline, null);

        // Assert
        result.Should().Be(NoticePriority.Medium);
    }

    [Fact]
    public void CalculatePriority_WithNearDeadline_ReturnsMedium()
    {
        // Arrange - deadline within 7 days = 3 points → Medium
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

        // Act
        var result = _service.CalculatePriority(null, null, deadline, null);

        // Assert
        result.Should().Be(NoticePriority.Medium);
    }

    [Fact]
    public void CalculatePriority_WithHighAmount_ReturnsMedium()
    {
        // Arrange - total demand over 10 lakhs = 3 points → Medium
        var totalDemand = 1500000m; // 15 lakhs

        // Act
        var result = _service.CalculatePriority(null, null, null, totalDemand);

        // Assert
        result.Should().Be(NoticePriority.Medium);
    }

    [Fact]
    public void CalculatePriority_WithNoFactors_ReturnsLow()
    {
        // Arrange - no factors = 0 points → Low
        // Act
        var result = _service.CalculatePriority(null, null, null, null);

        // Assert
        result.Should().Be(NoticePriority.Low);
    }

    [Fact]
    public void CalculatePriority_WithDistantDeadline_ReturnsLow()
    {
        // Arrange - deadline in 30 days = 1 point → Low
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        // Act
        var result = _service.CalculatePriority(null, null, deadline, null);

        // Assert
        result.Should().Be(NoticePriority.Low);
    }

    [Fact]
    public void CalculatePriority_WithPastDeadline_ReturnsHigh()
    {
        // Arrange - deadline already passed = 5 points → High
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));

        // Act
        var result = _service.CalculatePriority(null, null, deadline, null);

        // Assert
        result.Should().Be(NoticePriority.High);
    }

    [Fact]
    public void CalculatePriority_WithCriticalTypeAndUrgentDeadline_ReturnsHigh()
    {
        // Arrange - DRC-07 (3) + urgent deadline (4) = 7 points → High
        var noticeType = "DRC-07";
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        // Act
        var result = _service.CalculatePriority(noticeType, null, deadline, null);

        // Assert
        result.Should().Be(NoticePriority.High);
    }

    [Fact]
    public void CalculatePriority_WithMultipleCriticalFactors_ReturnsCritical()
    {
        // Arrange - demand category (4) + past deadline (5) = 9 points → Critical
        var category = "demand";
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        // Act
        var result = _service.CalculatePriority(null, category, deadline, null);

        // Assert
        result.Should().Be(NoticePriority.Critical);
    }

    #endregion
}

#endregion

#region Notice Priority Tests

public class NoticePriorityTests
{
    [Fact]
    public void AllPriorities_AreDefinedCorrectly()
    {
        // Assert
        NoticePriority.Low.Should().Be("low");
        NoticePriority.Medium.Should().Be("medium");
        NoticePriority.High.Should().Be("high");
        NoticePriority.Critical.Should().Be("critical");
    }
}

#endregion

#region Notice Status Tests

public class NoticeStatusTests
{
    [Fact]
    public void AllStatuses_AreDefinedCorrectly()
    {
        // Assert
        NoticeStatus.Uploaded.Should().Be("uploaded");
        NoticeStatus.Processing.Should().Be("processing");
        NoticeStatus.Analyzed.Should().Be("analyzed");
        NoticeStatus.InProgress.Should().Be("in_progress");
        NoticeStatus.Responded.Should().Be("responded");
        NoticeStatus.Closed.Should().Be("closed");
        NoticeStatus.Archived.Should().Be("archived");
        NoticeStatus.Failed.Should().Be("failed");
    }

    [Fact]
    public void AllStatuses_ContainsAllValues()
    {
        // Assert
        NoticeStatus.All.Should().HaveCount(8);
        NoticeStatus.All.Should().Contain(NoticeStatus.Uploaded);
        NoticeStatus.All.Should().Contain(NoticeStatus.Processing);
        NoticeStatus.All.Should().Contain(NoticeStatus.Analyzed);
        NoticeStatus.All.Should().Contain(NoticeStatus.InProgress);
        NoticeStatus.All.Should().Contain(NoticeStatus.Responded);
        NoticeStatus.All.Should().Contain(NoticeStatus.Closed);
        NoticeStatus.All.Should().Contain(NoticeStatus.Archived);
        NoticeStatus.All.Should().Contain(NoticeStatus.Failed);
    }
}

public class NoticeStatusExtensionsTests
{
    private static Notice ArchivedNotice(object? preArchiveStatus) => new()
    {
        Status = NoticeStatus.Archived,
        Metadata = preArchiveStatus == null
            ? null
            : new Dictionary<string, object> { [NoticeStatusExtensions.PreArchiveStatusKey] = preArchiveStatus }
    };

    [Fact]
    public void GetRestoreStatus_UsesStatusSavedAtArchive()
    {
        ArchivedNotice(NoticeStatus.Closed).GetRestoreStatus().Should().Be(NoticeStatus.Closed);
    }

    [Fact]
    public void GetRestoreStatus_ReadsJsonValueLoadedFromDatabase()
    {
        // jsonb metadata comes back from the database as JsonElement, not string
        var stored = System.Text.Json.JsonDocument.Parse("\"failed\"").RootElement;

        ArchivedNotice(stored).GetRestoreStatus().Should().Be(NoticeStatus.Failed);
    }

    [Fact]
    public void GetRestoreStatus_WithoutSavedStatus_FallsBackToAnalyzed()
    {
        // Notices archived before the previous status was recorded
        ArchivedNotice(null).GetRestoreStatus().Should().Be(NoticeStatus.Analyzed);
    }

    [Fact]
    public void GetRestoreStatus_ForActiveNotice_ReturnsNull()
    {
        new Notice { Status = NoticeStatus.Analyzed }.GetRestoreStatus().Should().BeNull();
    }

    private static readonly IQueryable<Notice> Notices = new[]
    {
        new Notice { Status = NoticeStatus.Analyzed },
        new Notice { Status = NoticeStatus.Closed },
        new Notice { Status = NoticeStatus.Archived },
    }.AsQueryable();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WhereStatusFilter_AllStatus_ExcludesArchived(string? status)
    {
        Notices.WhereStatusFilter(status).Select(n => n.Status).Should()
            .BeEquivalentTo([NoticeStatus.Analyzed, NoticeStatus.Closed]);
    }

    [Fact]
    public void WhereStatusFilter_Archived_ReturnsOnlyArchived()
    {
        Notices.WhereStatusFilter(NoticeStatus.Archived).Select(n => n.Status).Should()
            .BeEquivalentTo([NoticeStatus.Archived]);
    }

    [Fact]
    public void WhereStatusFilter_MultipleStatuses_ReturnsThoseStatuses()
    {
        Notices.WhereStatusFilter("analyzed,closed").Select(n => n.Status).Should()
            .BeEquivalentTo([NoticeStatus.Analyzed, NoticeStatus.Closed]);
    }
}

#endregion
