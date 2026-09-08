using EffortlessInsight.Api.Services.Support;
using Microsoft.AspNetCore.Mvc;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin endpoints for answering in-app support tickets across all
/// organizations. Customer-facing replies are attributed to a shared
/// "EffortlessInsight Support" identity, not the individual admin.
/// </summary>
[Route("api/v1/admin/support-tickets")]
public class AdminSupportTicketsController : AdminControllerBase
{
    private readonly ISupportTicketService _supportTicketService;

    public AdminSupportTicketsController(
        ISupportTicketService supportTicketService,
        ILogger<AdminSupportTicketsController> logger)
        : base(logger)
    {
        _supportTicketService = supportTicketService;
    }

    /// <summary>
    /// List support tickets across all organizations, most recently active first.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var tickets = await _supportTicketService.AdminListAsync(status, page, pageSize);
        return Success(tickets);
    }

    /// <summary>
    /// Get a ticket with its full message thread.
    /// </summary>
    [HttpGet("{ticketId:guid}")]
    public async Task<IActionResult> Get(Guid ticketId)
    {
        var ticket = await _supportTicketService.AdminGetAsync(ticketId);
        if (ticket == null)
        {
            return NotFoundResponse("Ticket not found");
        }

        return Success(ticket);
    }

    /// <summary>
    /// Reply to a ticket as support.
    /// </summary>
    [HttpPost("{ticketId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid ticketId, [FromBody] SupportTicketReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Error("Message is required", "VALIDATION");
        }

        var message = await _supportTicketService.AdminReplyAsync(ticketId, CurrentAdminName, request.Message);
        if (message == null)
        {
            return NotFoundResponse("Ticket not found or closed");
        }

        _logger.LogInformation(
            "Admin {AdminId} replied to support ticket {TicketId}", CurrentAdminId, ticketId);
        return Success(message);
    }

    /// <summary>
    /// Change a ticket's status (open / in_progress / resolved / closed).
    /// </summary>
    [HttpPost("{ticketId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid ticketId, [FromBody] SupportTicketStatusRequest request)
    {
        var updated = await _supportTicketService.AdminSetStatusAsync(ticketId, request.Status);
        if (!updated)
        {
            return Error("Invalid status or ticket not found", "VALIDATION");
        }

        _logger.LogInformation(
            "Admin {AdminId} set support ticket {TicketId} status to {Status}",
            CurrentAdminId, ticketId, request.Status);
        return Success(new { updated = true });
    }
}
