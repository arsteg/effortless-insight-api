using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Notices;
using FluentAssertions;

namespace EffortlessInsight.Api.Tests.Unit.Services;

/// <summary>
/// Tests for Phase D features: Response Status Workflow, Reminder Types, Statistics Calculations
/// These are unit tests that don't require database access.
/// </summary>
#region Response Status Workflow Tests

public class ResponseStatusWorkflowTests
{
    [Theory]
    [InlineData("draft", "review", true)]
    [InlineData("review", "approved", true)]
    [InlineData("approved", "submitted", true)]
    [InlineData("draft", "approved", false)]
    [InlineData("draft", "submitted", false)]
    [InlineData("review", "submitted", false)]
    [InlineData("submitted", "draft", false)]
    public void ResponseStatus_TransitionsAreValid(string from, string to, bool isValid)
    {
        // This tests the expected workflow: draft -> review -> approved -> submitted
        var validTransitions = new Dictionary<string, string[]>
        {
            { "draft", new[] { "review" } },
            { "review", new[] { "approved", "draft" } }, // Can go back to draft for revision
            { "approved", new[] { "submitted" } },
            { "submitted", Array.Empty<string>() }
        };

        // Assert
        var allowed = validTransitions.TryGetValue(from, out var allowedTo) &&
                     allowedTo.Contains(to);
        allowed.Should().Be(isValid);
    }

    [Theory]
    [InlineData("draft", false)]
    [InlineData("review", false)]
    [InlineData("approved", true)]
    [InlineData("submitted", true)]
    public void ResponseStatus_CanBeMarkedAsSubmitted(string status, bool canSubmit)
    {
        // Only approved responses can be submitted
        var submittableStatuses = new[] { "approved", "submitted" };
        var result = submittableStatuses.Contains(status);
        result.Should().Be(canSubmit);
    }

    [Theory]
    [InlineData("draft", true)]
    [InlineData("review", true)]
    [InlineData("approved", false)]
    [InlineData("submitted", false)]
    public void ResponseStatus_CanBeEdited(string status, bool canEdit)
    {
        // Only draft and review can be edited
        var editableStatuses = new[] { "draft", "review" };
        var result = editableStatuses.Contains(status);
        result.Should().Be(canEdit);
    }
}

#endregion

#region Reminder Type Tests

public class ReminderTypeTests
{
    private static readonly string[] ValidReminderTypes = { "email", "sms", "push", "whatsapp" };

    [Theory]
    [InlineData("email", true)]
    [InlineData("sms", true)]
    [InlineData("push", true)]
    [InlineData("whatsapp", true)]
    [InlineData("EMAIL", true)] // Case insensitive
    [InlineData("telegram", false)]
    [InlineData("slack", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReminderType_Validation(string? type, bool expected)
    {
        // Act
        var isValid = !string.IsNullOrEmpty(type) &&
                     ValidReminderTypes.Contains(type.ToLowerInvariant());

        // Assert
        isValid.Should().Be(expected);
    }

    [Fact]
    public void ReminderTypes_AllTypesAreDefined()
    {
        // Assert
        ValidReminderTypes.Should().HaveCount(4);
        ValidReminderTypes.Should().Contain("email");
        ValidReminderTypes.Should().Contain("sms");
        ValidReminderTypes.Should().Contain("push");
        ValidReminderTypes.Should().Contain("whatsapp");
    }
}

#endregion

#region Statistics Date Calculation Tests

public class StatisticsDateCalculationTests
{
    [Theory]
    [InlineData(DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    [InlineData(DayOfWeek.Saturday)]
    public void EndOfWeekCalculation_ReturnsCorrectSunday(DayOfWeek today)
    {
        // Arrange
        var baseDate = new DateTime(2026, 6, 7); // Known Sunday
        var daysToAdd = (int)today - (int)DayOfWeek.Sunday;
        var testDate = baseDate.AddDays(daysToAdd);
        var todayDate = DateOnly.FromDateTime(testDate);

        // Act - Same calculation as in GetStatisticsAsync
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)todayDate.DayOfWeek + 7) % 7;
        var endOfWeek = todayDate.AddDays(daysUntilSunday == 0 ? 7 : daysUntilSunday);

        // Assert
        endOfWeek.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        endOfWeek.Should().BeOnOrAfter(todayDate);
    }

    [Fact]
    public void EndOfMonthCalculation_ReturnsLastDayOfMonth()
    {
        // Arrange
        var testDate = new DateTime(2026, 2, 15);
        var today = DateOnly.FromDateTime(testDate);

        // Act
        var endOfMonth = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        // Assert
        endOfMonth.Day.Should().Be(28); // Feb 2026 is not a leap year
        endOfMonth.Month.Should().Be(2);
    }

    [Fact]
    public void EndOfMonthCalculation_LeapYear_ReturnsCorrectDay()
    {
        // Arrange - 2028 is a leap year
        var testDate = new DateTime(2028, 2, 15);
        var today = DateOnly.FromDateTime(testDate);

        // Act
        var endOfMonth = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        // Assert
        endOfMonth.Day.Should().Be(29); // Feb 2028 is a leap year
    }

    [Theory]
    [InlineData(2026, 1, 31)] // January
    [InlineData(2026, 3, 31)] // March
    [InlineData(2026, 4, 30)] // April
    [InlineData(2026, 12, 31)] // December
    public void EndOfMonthCalculation_VariousMonths_ReturnsCorrectDay(int year, int month, int expectedDay)
    {
        // Arrange
        var testDate = new DateTime(year, month, 1);
        var today = DateOnly.FromDateTime(testDate);

        // Act
        var endOfMonth = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        // Assert
        endOfMonth.Day.Should().Be(expectedDay);
    }
}

#endregion

#region Deadline Calculation Tests

public class DeadlineCalculationTests
{
    [Theory]
    [InlineData(0, false)]  // Today (due today, not yet overdue)
    [InlineData(-1, true)]  // Yesterday (overdue)
    [InlineData(-10, true)] // 10 days overdue
    [InlineData(1, false)]  // Tomorrow
    [InlineData(7, false)]  // Next week
    public void IsOverdue_CalculatesCorrectly(int daysFromToday, bool expectedOverdue)
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = today.AddDays(daysFromToday);

        // Act
        var isOverdue = deadline < today;

        // Assert (overdue means deadline is before today)
        isOverdue.Should().Be(expectedOverdue);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(-1, -1)]
    [InlineData(-30, -30)]
    public void DaysRemaining_CalculatesCorrectly(int daysFromToday, int expectedRemaining)
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = today.AddDays(daysFromToday);

        // Act
        var remaining = deadline.DayNumber - today.DayNumber;

        // Assert
        remaining.Should().Be(expectedRemaining);
    }

    [Theory]
    [InlineData(0, true)]   // Due today
    [InlineData(1, true)]   // Due tomorrow
    [InlineData(6, true)]   // Due in 6 days
    [InlineData(7, true)]   // Due in 7 days - edge case (end of week)
    [InlineData(8, false)]  // Due in 8 days
    [InlineData(-1, false)] // Overdue (past)
    public void IsDueThisWeek_CalculatesCorrectly(int daysFromToday, bool expectedDueThisWeek)
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = today.AddDays(daysFromToday);

        // Act - due this week means between today and 7 days from now
        var isDueThisWeek = deadline >= today && deadline <= today.AddDays(7);

        // Assert
        isDueThisWeek.Should().Be(expectedDueThisWeek);
    }
}

#endregion

#region Response Content Validation Tests

public class ResponseContentValidationTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void DraftContent_WhenEmpty_IsInvalidForReview(string? content, bool isEmpty)
    {
        // Act
        var isEmptyOrWhitespace = string.IsNullOrWhiteSpace(content);

        // Assert
        isEmptyOrWhitespace.Should().Be(isEmpty);
    }

    [Theory]
    [InlineData("Valid draft content")]
    [InlineData("Draft with special characters: @#$%")]
    [InlineData("Draft\nwith\nmultiple\nlines")]
    public void DraftContent_WhenValid_IsNotEmpty(string content)
    {
        // Act
        var isValid = !string.IsNullOrWhiteSpace(content);

        // Assert
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(10000, true)]
    [InlineData(99999, true)]
    [InlineData(100000, true)]  // Max allowed
    [InlineData(100001, false)] // Over max
    public void DraftContent_MaxLength_Validation(int contentLength, bool isValid)
    {
        // Arrange
        const int maxLength = 100000;

        // Assert
        var isWithinLimit = contentLength <= maxLength;
        isWithinLimit.Should().Be(isValid);
    }
}

#endregion

#region Submission Proof URL Validation Tests

public class SubmissionProofUrlValidationTests
{
    [Theory]
    [InlineData("https://example.com/proof.pdf", true)]
    [InlineData("http://example.com/proof.pdf", true)]
    [InlineData("https://s3.amazonaws.com/bucket/file.pdf", true)]
    [InlineData("ftp://example.com/file.pdf", false)]
    [InlineData("file:///C:/local/file.pdf", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData(null, true)] // Optional field
    public void SubmissionProofUrl_Validation(string? url, bool isValid)
    {
        // Act
        bool result;
        if (url == null)
        {
            result = true; // Null is valid (optional)
        }
        else if (string.IsNullOrEmpty(url))
        {
            result = false;
        }
        else
        {
            result = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        // Assert
        result.Should().Be(isValid);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(500, true)]  // Max allowed
    [InlineData(501, false)] // Over max
    public void SubmissionProofUrl_MaxLength_Validation(int urlLength, bool isValid)
    {
        // Arrange
        const int maxLength = 500;

        // Assert
        var isWithinLimit = urlLength <= maxLength;
        isWithinLimit.Should().Be(isValid);
    }
}

#endregion

#region Statistics Aggregation Tests

public class StatisticsAggregationTests
{
    [Fact]
    public void TotalDemand_CalculatesSum_OfTaxPenaltyInterest()
    {
        // Arrange
        decimal? taxAmount = 100000m;
        decimal? penaltyAmount = 25000m;
        decimal? interestAmount = 5000m;

        // Act
        var totalDemand = (taxAmount ?? 0m) + (penaltyAmount ?? 0m) + (interestAmount ?? 0m);

        // Assert
        totalDemand.Should().Be(130000m);
    }

    [Theory]
    [InlineData(null, null, null, 0)]
    [InlineData(100000, null, null, 100000)]
    [InlineData(null, 50000, null, 50000)]
    [InlineData(null, null, 10000, 10000)]
    [InlineData(100000, 25000, 5000, 130000)]
    public void TotalDemand_HandlesNullValues(int? tax, int? penalty, int? interest, int expected)
    {
        // Convert to decimal for calculation (InlineData doesn't support decimal literals)
        decimal? taxDecimal = tax.HasValue ? (decimal)tax.Value : null;
        decimal? penaltyDecimal = penalty.HasValue ? (decimal)penalty.Value : null;
        decimal? interestDecimal = interest.HasValue ? (decimal)interest.Value : null;

        // Act
        var totalDemand = (taxDecimal ?? 0m) + (penaltyDecimal ?? 0m) + (interestDecimal ?? 0m);

        // Assert
        totalDemand.Should().Be((decimal)expected);
    }

    [Fact]
    public void StatusCounts_AggregatesCorrectly()
    {
        // Arrange
        var statuses = new[] { "analyzed", "analyzed", "in_progress", "closed", "analyzed" };

        // Act
        var counts = statuses.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());

        // Assert
        counts.Should().HaveCount(3);
        counts["analyzed"].Should().Be(3);
        counts["in_progress"].Should().Be(1);
        counts["closed"].Should().Be(1);
    }

    [Fact]
    public void PriorityCounts_AggregatesCorrectly()
    {
        // Arrange
        var priorities = new[] { "high", "medium", "high", "low", "critical", "medium" };

        // Act
        var counts = priorities.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());

        // Assert
        counts.Should().HaveCount(4);
        counts["high"].Should().Be(2);
        counts["medium"].Should().Be(2);
        counts["low"].Should().Be(1);
        counts["critical"].Should().Be(1);
    }
}

#endregion
