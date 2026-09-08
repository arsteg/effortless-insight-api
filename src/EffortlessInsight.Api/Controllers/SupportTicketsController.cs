using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Services.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// In-app customer support tickets (org-scoped). Support is deliberately an
/// in-app channel — customers raise and follow tickets here instead of email.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/support/tickets")]
public class SupportTicketsController : ControllerBase
{
    private readonly ISupportTicketService _supportTicketService;
    private readonly ICurrentOrganizationService _currentOrganization;
    private readonly ILogger<SupportTicketsController> _logger;

    public SupportTicketsController(
        ISupportTicketService supportTicketService,
        ICurrentOrganizationService currentOrganization,
        ILogger<SupportTicketsController> logger)
    {
        _supportTicketService = supportTicketService;
        _currentOrganization = currentOrganization;
        _logger = logger;
    }

    /// <summary>
    /// List the organization's support tickets, most recently active first.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SupportTicketSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        var (orgId, _, error) = ResolveContext();
        if (error != null) return error;

        var tickets = await _supportTicketService.ListAsync(orgId);
        return Ok(new ApiResponse<IReadOnlyList<SupportTicketSummaryDto>>(true, tickets));
    }

    /// <summary>
    /// Get a ticket with its full message thread.
    /// </summary>
    [HttpGet("{ticketId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SupportTicketDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid ticketId)
    {
        var (orgId, _, error) = ResolveContext();
        if (error != null) return error;

        var ticket = await _supportTicketService.GetAsync(orgId, ticketId);
        if (ticket == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Ticket not found"));
        }

        return Ok(new ApiResponse<SupportTicketDetailDto>(true, ticket));
    }

    /// <summary>
    /// Create a new support ticket with an initial message.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SupportTicketDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSupportTicketRequest request)
    {
        var (orgId, userId, error) = ResolveContext();
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION", "Subject and message are required"));
        }
        if (request.Subject.Length > 200)
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION", "Subject must be 200 characters or fewer"));
        }

        var ticket = await _supportTicketService.CreateAsync(orgId, userId, request);
        _logger.LogInformation("Support ticket {TicketId} created by user {UserId}", ticket.Id, userId);
        return Ok(new ApiResponse<SupportTicketDetailDto>(true, ticket));
    }

    /// <summary>
    /// Add a customer reply to a ticket. Replying to a resolved ticket reopens it.
    /// </summary>
    [HttpPost("{ticketId:guid}/messages")]
    [ProducesResponseType(typeof(ApiResponse<SupportTicketMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reply(Guid ticketId, [FromBody] SupportTicketReplyRequest request)
    {
        var (orgId, userId, error) = ResolveContext();
        if (error != null) return error;

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ApiErrorResponse(false, "VALIDATION", "Message is required"));
        }

        var message = await _supportTicketService.ReplyAsync(orgId, userId, ticketId, request.Message);
        if (message == null)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Ticket not found or closed"));
        }

        return Ok(new ApiResponse<SupportTicketMessageDto>(true, message));
    }

    /// <summary>
    /// Close a ticket (customer-initiated).
    /// </summary>
    [HttpPost("{ticketId:guid}/close")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Close(Guid ticketId)
    {
        var (orgId, _, error) = ResolveContext();
        if (error != null) return error;

        var closed = await _supportTicketService.CloseAsync(orgId, ticketId);
        if (!closed)
        {
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Ticket not found or already closed"));
        }

        return Ok(new ApiResponse<object>(true, new { closed = true }));
    }

    private (Guid OrgId, Guid UserId, IActionResult? Error) ResolveContext()
    {
        var orgId = _currentOrganization.OrganizationId;
        var userId = _currentOrganization.UserId;
        if (orgId == null || userId == null)
        {
            return (Guid.Empty, Guid.Empty,
                NotFound(new ApiErrorResponse(false, "NO_ORG", "No organization selected")));
        }

        return (orgId.Value, userId.Value, null);
    }
}
