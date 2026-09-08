using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Support;

/// <summary>
/// In-app customer support tickets. Support is deliberately an in-app channel
/// (no support@ mailbox) — customers raise tickets here and admins reply via
/// the admin endpoints. The team is notified by email (best-effort) so new
/// tickets never sit unseen.
/// </summary>
public interface ISupportTicketService
{
    // Tenant-facing
    Task<IReadOnlyList<SupportTicketSummaryDto>> ListAsync(Guid organizationId);
    Task<SupportTicketDetailDto?> GetAsync(Guid organizationId, Guid ticketId);
    Task<SupportTicketDetailDto> CreateAsync(Guid organizationId, Guid userId, CreateSupportTicketRequest request);
    Task<SupportTicketMessageDto?> ReplyAsync(Guid organizationId, Guid userId, Guid ticketId, string message);
    Task<bool> CloseAsync(Guid organizationId, Guid ticketId);

    // Admin-facing (cross-tenant)
    Task<IReadOnlyList<AdminSupportTicketSummaryDto>> AdminListAsync(string? status, int page, int pageSize);
    Task<SupportTicketDetailDto?> AdminGetAsync(Guid ticketId);
    Task<SupportTicketMessageDto?> AdminReplyAsync(Guid ticketId, string senderName, string message);
    Task<bool> AdminSetStatusAsync(Guid ticketId, string status);
}

public class SupportTicketService : ISupportTicketService
{
    private const string SupportSenderName = "EffortlessInsight Support";

    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SupportTicketService> _logger;

    public SupportTicketService(
        ApplicationDbContext db,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<SupportTicketService> logger)
    {
        _db = db;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Tenant-facing
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<SupportTicketSummaryDto>> ListAsync(Guid organizationId)
    {
        return await _db.SupportTickets
            .Where(t => t.OrganizationId == organizationId)
            .OrderByDescending(t => t.LastMessageAt)
            .Select(t => new SupportTicketSummaryDto(
                t.Id,
                t.Subject,
                t.Category,
                t.Status,
                t.CreatedAt,
                t.LastMessageAt,
                t.Messages.Count(),
                // "New reply": the most recent message in the thread came from support
                t.Messages.OrderByDescending(m => m.CreatedAt).First().IsFromSupport))
            .ToListAsync();
    }

    public async Task<SupportTicketDetailDto?> GetAsync(Guid organizationId, Guid ticketId)
    {
        var ticket = await _db.SupportTickets
            .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.OrganizationId == organizationId);

        return ticket == null ? null : ToDetailDto(ticket);
    }

    public async Task<SupportTicketDetailDto> CreateAsync(Guid organizationId, Guid userId, CreateSupportTicketRequest request)
    {
        var category = SupportTicketCategory.All.Contains(request.Category)
            ? request.Category
            : SupportTicketCategory.Other;

        var senderName = await GetUserNameAsync(userId);
        var now = DateTime.UtcNow;

        var ticket = new SupportTicket
        {
            OrganizationId = organizationId,
            CreatedByUserId = userId,
            Subject = request.Subject.Trim(),
            Category = category,
            Status = SupportTicketStatus.Open,
            LastMessageAt = now,
        };
        ticket.Messages.Add(new SupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderUserId = userId,
            IsFromSupport = false,
            SenderName = senderName,
            Body = request.Message.Trim(),
            CreatedAt = now,
        });

        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync();

        await NotifyTeamAsync(
            subject: $"[Support] New ticket: {ticket.Subject}",
            ticket: ticket,
            senderName: senderName,
            body: request.Message);

        return ToDetailDto(ticket);
    }

    public async Task<SupportTicketMessageDto?> ReplyAsync(Guid organizationId, Guid userId, Guid ticketId, string message)
    {
        var ticket = await _db.SupportTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.OrganizationId == organizationId);
        if (ticket == null || ticket.Status == SupportTicketStatus.Closed)
        {
            return null;
        }

        var senderName = await GetUserNameAsync(userId);
        var reply = new SupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderUserId = userId,
            IsFromSupport = false,
            SenderName = senderName,
            Body = message.Trim(),
        };
        _db.SupportTicketMessages.Add(reply);

        // A customer reply re-opens a resolved ticket
        if (ticket.Status == SupportTicketStatus.Resolved)
        {
            ticket.Status = SupportTicketStatus.Open;
        }
        ticket.LastMessageAt = reply.CreatedAt;
        ticket.UpdatedAt = reply.CreatedAt;
        await _db.SaveChangesAsync();

        await NotifyTeamAsync(
            subject: $"[Support] Customer reply: {ticket.Subject}",
            ticket: ticket,
            senderName: senderName,
            body: message);

        return ToMessageDto(reply);
    }

    public async Task<bool> CloseAsync(Guid organizationId, Guid ticketId)
    {
        var ticket = await _db.SupportTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.OrganizationId == organizationId);
        if (ticket == null || ticket.Status == SupportTicketStatus.Closed)
        {
            return false;
        }

        ticket.Status = SupportTicketStatus.Closed;
        ticket.ClosedAt = DateTime.UtcNow;
        ticket.UpdatedAt = ticket.ClosedAt;
        await _db.SaveChangesAsync();
        return true;
    }

    // ------------------------------------------------------------------
    // Admin-facing (cross-tenant: bypasses the tenant query filter)
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<AdminSupportTicketSummaryDto>> AdminListAsync(string? status, int page, int pageSize)
    {
        var query = _db.SupportTickets
            .IgnoreQueryFilters()
            .Where(t => t.DeletedAt == null);

        if (!string.IsNullOrEmpty(status) && SupportTicketStatus.All.Contains(status))
        {
            query = query.Where(t => t.Status == status);
        }

        return await query
            .OrderByDescending(t => t.LastMessageAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new AdminSupportTicketSummaryDto(
                t.Id,
                t.OrganizationId,
                t.Organization.Name,
                t.CreatedBy.Name,
                t.CreatedBy.Email ?? string.Empty,
                t.Subject,
                t.Category,
                t.Status,
                t.CreatedAt,
                t.LastMessageAt,
                t.Messages.Count(m => m.DeletedAt == null)))
            .ToListAsync();
    }

    public async Task<SupportTicketDetailDto?> AdminGetAsync(Guid ticketId)
    {
        var ticket = await _db.SupportTickets
            .IgnoreQueryFilters()
            .Include(t => t.Messages.Where(m => m.DeletedAt == null).OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null);

        return ticket == null ? null : ToDetailDto(ticket);
    }

    public async Task<SupportTicketMessageDto?> AdminReplyAsync(Guid ticketId, string senderName, string message)
    {
        var ticket = await _db.SupportTickets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null);
        if (ticket == null || ticket.Status == SupportTicketStatus.Closed)
        {
            return null;
        }

        var reply = new SupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderUserId = null,
            IsFromSupport = true,
            // Internal admin name is not exposed to the customer; a shared
            // support identity keeps the channel consistent
            SenderName = SupportSenderName,
            Body = message.Trim(),
        };
        _db.SupportTicketMessages.Add(reply);

        if (ticket.Status == SupportTicketStatus.Open)
        {
            ticket.Status = SupportTicketStatus.InProgress;
        }
        ticket.LastMessageAt = reply.CreatedAt;
        ticket.UpdatedAt = reply.CreatedAt;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Support reply added to ticket {TicketId} by admin {AdminName}", ticketId, senderName);

        return ToMessageDto(reply);
    }

    public async Task<bool> AdminSetStatusAsync(Guid ticketId, string status)
    {
        if (!SupportTicketStatus.All.Contains(status))
        {
            return false;
        }

        var ticket = await _db.SupportTickets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null);
        if (ticket == null)
        {
            return false;
        }

        ticket.Status = status;
        ticket.ClosedAt = status == SupportTicketStatus.Closed ? DateTime.UtcNow : null;
        ticket.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<string> GetUserNameAsync(Guid userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user?.FullName ?? "Customer";
    }

    /// <summary>
    /// Best-effort email to the team inbox so tickets never sit unseen.
    /// Failures are logged and swallowed — the ticket itself is already saved.
    /// </summary>
    private async Task NotifyTeamAsync(string subject, SupportTicket ticket, string senderName, string body)
    {
        var to = _configuration["Support:NotificationEmail"];
        if (string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        try
        {
            var html =
                $"<p><strong>{System.Net.WebUtility.HtmlEncode(senderName)}</strong> wrote on ticket " +
                $"<strong>{System.Net.WebUtility.HtmlEncode(ticket.Subject)}</strong> " +
                $"(category: {ticket.Category}, status: {ticket.Status}):</p>" +
                $"<blockquote>{System.Net.WebUtility.HtmlEncode(body)}</blockquote>" +
                $"<p>Reply from the admin panel — ticket ID <code>{ticket.Id}</code>.</p>";

            await _emailService.SendAsync(to, subject, html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send support notification email for ticket {TicketId}", ticket.Id);
        }
    }

    private static SupportTicketDetailDto ToDetailDto(SupportTicket ticket) =>
        new(
            ticket.Id,
            ticket.Subject,
            ticket.Category,
            ticket.Status,
            ticket.CreatedAt,
            ticket.LastMessageAt,
            ticket.Messages.OrderBy(m => m.CreatedAt).Select(ToMessageDto).ToList());

    private static SupportTicketMessageDto ToMessageDto(SupportTicketMessage message) =>
        new(
            message.Id,
            message.IsFromSupport,
            message.SenderName ?? (message.IsFromSupport ? SupportSenderName : "Customer"),
            message.Body,
            message.CreatedAt);
}
