using System.Security.Claims;
using EffortlessInsight.Api.Controllers;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Notices;
using EffortlessInsight.Api.Services.Organizations;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for NoticesController Phase D features: Responses, Reminders, Statistics
/// </summary>
public class NoticesControllerPhaseDTests
{
    private readonly Mock<INoticeServiceExtended> _noticeService;
    private readonly Mock<ICurrentOrganizationService> _currentOrg;
    private readonly Mock<ILogger<NoticesController>> _logger;
    private readonly NoticesController _controller;

    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _noticeId = Guid.NewGuid();

    public NoticesControllerPhaseDTests()
    {
        _noticeService = new Mock<INoticeServiceExtended>();
        _currentOrg = new Mock<ICurrentOrganizationService>();
        _logger = new Mock<ILogger<NoticesController>>();

        // Setup current org accessor
        _currentOrg.Setup(x => x.OrganizationId).Returns(_orgId);
        _currentOrg.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);

        _controller = new NoticesController(
            _noticeService.Object,
            _currentOrg.Object,
            _logger.Object);

        // Setup user claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    #region GetResponses Tests

    [Fact]
    public async Task GetResponses_WithPermission_ReturnsOk()
    {
        // Arrange
        var responses = new List<NoticeResponse>
        {
            new()
            {
                Id = Guid.NewGuid(),
                NoticeId = _noticeId,
                CreatedById = _userId,
                Status = "draft",
                Version = 1,
                CreatedAt = DateTime.UtcNow
            }
        };
        _noticeService.Setup(x => x.GetResponsesAsync(_noticeId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(responses);

        // Act
        var result = await _controller.GetResponses(_noticeId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<List<NoticeResponseDto>>>().Subject;
        response.Success.Should().BeTrue();
        response.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetResponses_WithoutPermission_ReturnsForbid()
    {
        // Arrange
        _currentOrg.Setup(x => x.HasPermission("notices.view")).Returns(false);

        // Act
        var result = await _controller.GetResponses(_noticeId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetResponses_NoticeNotFound_ReturnsNotFound()
    {
        // Arrange
        _noticeService.Setup(x => x.GetResponsesAsync(_noticeId, _orgId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Notice not found"));

        // Act
        var result = await _controller.GetResponses(_noticeId, CancellationToken.None);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var response = notFoundResult.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        response.Code.Should().Be("NOT_FOUND");
    }

    #endregion

    #region GetLatestResponse Tests

    [Fact]
    public async Task GetLatestResponse_ResponseExists_ReturnsOk()
    {
        // Arrange
        var response = new NoticeResponse
        {
            Id = Guid.NewGuid(),
            NoticeId = _noticeId,
            CreatedById = _userId,
            Status = "review",
            Version = 2,
            CreatedAt = DateTime.UtcNow
        };
        _noticeService.Setup(x => x.GetLatestResponseAsync(_noticeId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetLatestResponse(_noticeId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<NoticeResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data!.Version.Should().Be(2);
    }

    [Fact]
    public async Task GetLatestResponse_NoResponses_ReturnsNotFound()
    {
        // Arrange
        _noticeService.Setup(x => x.GetLatestResponseAsync(_noticeId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoticeResponse?)null);

        // Act
        var result = await _controller.GetLatestResponse(_noticeId, CancellationToken.None);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var response = notFoundResult.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        response.Message.Should().Contain("No response found");
    }

    #endregion

    #region SaveResponseDraft Tests

    [Fact]
    public async Task SaveResponseDraft_ValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new SaveDraftRequest("Draft content here");
        var savedResponse = new NoticeResponse
        {
            Id = Guid.NewGuid(),
            NoticeId = _noticeId,
            CreatedById = _userId,
            DraftContent = "Draft content here",
            Status = "draft",
            Version = 1,
            CreatedAt = DateTime.UtcNow
        };
        _noticeService.Setup(x => x.SaveResponseDraftAsync(_noticeId, _orgId, _userId, request.DraftContent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedResponse);

        // Act
        var result = await _controller.SaveResponseDraft(_noticeId, request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<NoticeResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SaveResponseDraft_WithoutPermission_ReturnsForbid()
    {
        // Arrange
        _currentOrg.Setup(x => x.HasPermission("notices.edit")).Returns(false);
        var request = new SaveDraftRequest("Draft content");

        // Act
        var result = await _controller.SaveResponseDraft(_noticeId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    #endregion

    #region SubmitForReview Tests

    [Fact]
    public async Task SubmitForReview_ValidDraft_ReturnsOk()
    {
        // Arrange
        var responseId = Guid.NewGuid();
        var response = new NoticeResponse
        {
            Id = responseId,
            NoticeId = _noticeId,
            CreatedById = _userId,
            Status = "review",
            Version = 1,
            CreatedAt = DateTime.UtcNow
        };
        _noticeService.Setup(x => x.SubmitForReviewAsync(responseId, _noticeId, _orgId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.SubmitForReview(_noticeId, responseId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<NoticeResponseDto>>().Subject;
        apiResponse.Data!.Status.Should().Be("review");
    }

    [Fact]
    public async Task SubmitForReview_InvalidStatus_ReturnsBadRequest()
    {
        // Arrange
        var responseId = Guid.NewGuid();
        _noticeService.Setup(x => x.SubmitForReviewAsync(responseId, _noticeId, _orgId, _userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cannot submit response in status 'review' for review"));

        // Act
        var result = await _controller.SubmitForReview(_noticeId, responseId, CancellationToken.None);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        response.Code.Should().Be("INVALID_OPERATION");
    }

    #endregion

    #region ApproveResponse Tests

    [Fact]
    public async Task ApproveResponse_ValidReview_ReturnsOk()
    {
        // Arrange
        var responseId = Guid.NewGuid();
        _currentOrg.Setup(x => x.HasPermission("notices.approve")).Returns(true);

        var response = new NoticeResponse
        {
            Id = responseId,
            NoticeId = _noticeId,
            CreatedById = _userId,
            ApprovedById = _userId,
            Status = "approved",
            Version = 1,
            CreatedAt = DateTime.UtcNow
        };
        _noticeService.Setup(x => x.ApproveResponseAsync(responseId, _noticeId, _orgId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.ApproveResponse(_noticeId, responseId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<NoticeResponseDto>>().Subject;
        apiResponse.Data!.Status.Should().Be("approved");
    }

    [Fact]
    public async Task ApproveResponse_WithoutPermission_ReturnsForbid()
    {
        // Arrange
        var responseId = Guid.NewGuid();
        _currentOrg.Setup(x => x.HasPermission("notices.approve")).Returns(false);

        // Act
        var result = await _controller.ApproveResponse(_noticeId, responseId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    #endregion

    #region MarkAsSubmitted Tests

    [Fact]
    public async Task MarkAsSubmitted_ValidApproved_ReturnsOk()
    {
        // Arrange
        var responseId = Guid.NewGuid();
        var request = new MarkSubmittedRequest("REF-001", "https://example.com/proof.pdf");
        var response = new NoticeResponse
        {
            Id = responseId,
            NoticeId = _noticeId,
            CreatedById = _userId,
            Status = "submitted",
            SubmissionReference = "REF-001",
            SubmittedAt = DateTime.UtcNow,
            Version = 1,
            CreatedAt = DateTime.UtcNow
        };
        _noticeService.Setup(x => x.MarkAsSubmittedAsync(
                responseId, _noticeId, _orgId, _userId, request.SubmissionReference, request.SubmissionProofUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.MarkAsSubmitted(_noticeId, responseId, request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<NoticeResponseDto>>().Subject;
        apiResponse.Data!.Status.Should().Be("submitted");
    }

    #endregion

    #region GetReminders Tests

    [Fact]
    public async Task GetReminders_WithPermission_ReturnsOk()
    {
        // Arrange
        var reminders = new List<DeadlineReminder>
        {
            new()
            {
                Id = Guid.NewGuid(),
                NoticeId = _noticeId,
                UserId = _userId,
                ReminderType = "email",
                RemindAt = DateTime.UtcNow.AddDays(1),
                IsSent = false,
                CreatedAt = DateTime.UtcNow
            }
        };
        _noticeService.Setup(x => x.GetRemindersAsync(_noticeId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reminders);

        // Act
        var result = await _controller.GetReminders(_noticeId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<List<ReminderDto>>>().Subject;
        response.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetReminders_WithoutPermission_ReturnsForbid()
    {
        // Arrange
        _currentOrg.Setup(x => x.HasPermission("notices.view")).Returns(false);

        // Act
        var result = await _controller.GetReminders(_noticeId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    #endregion

    #region CreateReminder Tests

    [Fact]
    public async Task CreateReminder_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddDays(1), 1);
        var reminder = new DeadlineReminder
        {
            Id = Guid.NewGuid(),
            NoticeId = _noticeId,
            UserId = _userId,
            ReminderType = "email",
            RemindAt = request.RemindAt,
            DaysBefore = 1,
            IsSent = false,
            CreatedAt = DateTime.UtcNow
        };
        _noticeService.Setup(x => x.CreateReminderAsync(_noticeId, _orgId, _userId, It.IsAny<CreateReminderDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reminder);

        // Act
        var result = await _controller.CreateReminder(_noticeId, request, CancellationToken.None);

        // Assert
        var createdResult = result.Should().BeOfType<ObjectResult>().Subject;
        createdResult.StatusCode.Should().Be(StatusCodes.Status201Created);
    }

    [Fact]
    public async Task CreateReminder_InvalidType_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateReminderRequest("invalid", DateTime.UtcNow.AddDays(1), null);
        _noticeService.Setup(x => x.CreateReminderAsync(_noticeId, _orgId, _userId, It.IsAny<CreateReminderDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid reminder type"));

        // Act
        var result = await _controller.CreateReminder(_noticeId, request, CancellationToken.None);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        response.Code.Should().Be("INVALID_OPERATION");
    }

    [Fact]
    public async Task CreateReminder_WithoutPermission_ReturnsForbid()
    {
        // Arrange
        _currentOrg.Setup(x => x.HasPermission("notices.edit")).Returns(false);
        var request = new CreateReminderRequest("email", DateTime.UtcNow.AddDays(1), null);

        // Act
        var result = await _controller.CreateReminder(_noticeId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    #endregion

    #region DeleteReminder Tests

    [Fact]
    public async Task DeleteReminder_Exists_ReturnsNoContent()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        _noticeService.Setup(x => x.DeleteReminderAsync(reminderId, _noticeId, _orgId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.DeleteReminder(_noticeId, reminderId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteReminder_NotFound_ReturnsNotFound()
    {
        // Arrange
        var reminderId = Guid.NewGuid();
        _noticeService.Setup(x => x.DeleteReminderAsync(reminderId, _noticeId, _orgId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Reminder not found"));

        // Act
        var result = await _controller.DeleteReminder(_noticeId, reminderId, CancellationToken.None);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var response = notFoundResult.Value.Should().BeOfType<ApiErrorResponse>().Subject;
        response.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task DeleteReminder_WithoutPermission_ReturnsForbid()
    {
        // Arrange
        _currentOrg.Setup(x => x.HasPermission("notices.edit")).Returns(false);
        var reminderId = Guid.NewGuid();

        // Act
        var result = await _controller.DeleteReminder(_noticeId, reminderId, CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    #endregion

    #region GetStatistics Tests

    [Fact]
    public async Task GetStatistics_WithPermission_ReturnsOk()
    {
        // Arrange
        var stats = new NoticeStatistics(
            ByStatus: new Dictionary<string, int> { { "analyzed", 5 }, { "in_progress", 3 } },
            ByPriority: new Dictionary<string, int> { { "high", 2 }, { "medium", 6 } },
            OverdueCount: 1,
            DueThisWeek: 2,
            DueThisMonth: 4,
            TotalDemandAmount: 500000m,
            TotalCount: 8);
        _noticeService.Setup(x => x.GetStatisticsAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        // Act
        var result = await _controller.GetStatistics(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<NoticeStatisticsDto>>().Subject;
        response.Data!.TotalCount.Should().Be(8);
        response.Data.OverdueCount.Should().Be(1);
        response.Data.TotalDemandAmount.Should().Be(500000m);
    }

    [Fact]
    public async Task GetStatistics_WithoutPermission_ReturnsForbid()
    {
        // Arrange
        _currentOrg.Setup(x => x.HasPermission("notices.view")).Returns(false);

        // Act
        var result = await _controller.GetStatistics(CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetStatistics_ServiceError_Returns500()
    {
        // Arrange
        _noticeService.Setup(x => x.GetStatisticsAsync(_orgId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _controller.GetStatistics(CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    #endregion

    #region GetAll with Aggregations Tests

    [Fact]
    public async Task GetAll_WithAggregations_ReturnsAggregationsInResponse()
    {
        // Arrange
        var notices = new PagedResult<Notice>(new List<Notice>(), 0, 1, 20, 0);
        var stats = new NoticeStatistics(
            new Dictionary<string, int> { { "analyzed", 5 } },
            new Dictionary<string, int> { { "high", 2 } },
            1, 2, 4, 500000m, 5);

        _noticeService.Setup(x => x.GetListAsync(_orgId, It.IsAny<NoticeFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notices);
        _noticeService.Setup(x => x.GetStatisticsAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        // Act
        var filter = new NoticeFilterDto(null, null, null, null, null, null, null);
        var result = await _controller.GetAll(filter, includeAggregations: true);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<NoticeListResponse>>().Subject;
        response.Data!.Aggregations.Should().NotBeNull();
        response.Data.Aggregations!.OverdueCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAll_WithoutAggregations_ReturnsNullAggregations()
    {
        // Arrange
        var notices = new PagedResult<Notice>(new List<Notice>(), 0, 1, 20, 0);

        _noticeService.Setup(x => x.GetListAsync(_orgId, It.IsAny<NoticeFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notices);

        // Act
        var filter = new NoticeFilterDto(null, null, null, null, null, null, null);
        var result = await _controller.GetAll(filter, includeAggregations: false);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<NoticeListResponse>>().Subject;
        response.Data!.Aggregations.Should().BeNull();
    }

    #endregion
}
