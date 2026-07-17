using System.Diagnostics;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.WhatsApp.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.WhatsApp;

/// <summary>
/// Main service for orchestrating WhatsApp bot interactions.
/// </summary>
public class WhatsAppBotService : IWhatsAppBotService
{
    private readonly IMetaWhatsAppClient _client;
    private readonly IWhatsAppSessionService _sessionService;
    private readonly IWhatsAppMessageLogService _messageLogService;
    private readonly CommandRouter _commandRouter;
    private readonly IEnumerable<ICommandHandler> _commandHandlers;
    private readonly ApplicationDbContext _db;
    private readonly MetaWhatsAppOptions _options;
    private readonly ILogger<WhatsAppBotService> _logger;

    public WhatsAppBotService(
        IMetaWhatsAppClient client,
        IWhatsAppSessionService sessionService,
        IWhatsAppMessageLogService messageLogService,
        CommandRouter commandRouter,
        IEnumerable<ICommandHandler> commandHandlers,
        ApplicationDbContext db,
        IOptions<MetaWhatsAppOptions> options,
        ILogger<WhatsAppBotService> logger)
    {
        _client = client;
        _sessionService = sessionService;
        _messageLogService = messageLogService;
        _commandRouter = commandRouter;
        _commandHandlers = commandHandlers;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessIncomingMessageAsync(
        WhatsAppIncomingMessage message,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Get or create session
            var session = await _sessionService.GetOrCreateSessionAsync(message.From, ct);

            // Verify linked user still exists (handle orphaned sessions)
            if (session.UserId.HasValue && session.CurrentState == WhatsAppSessionState.Linked)
            {
                var linkedUser = await _db.Users.FindAsync([session.UserId.Value], ct);
                if (linkedUser == null || linkedUser.DeletedAt != null)
                {
                    _logger.LogWarning(
                        "Linked user {UserId} no longer exists for session {SessionId}. Unlinking.",
                        session.UserId.Value,
                        session.Id);

                    // Unlink the orphaned session
                    await _sessionService.UnlinkSessionAsync(session.UserId.Value, ct);

                    // Refresh session
                    session = await _sessionService.GetOrCreateSessionAsync(message.From, ct);
                }
            }

            // Mark message as read
            await _client.MarkAsReadAsync(message.WamId, ct);

            // Get input text
            var inputText = GetInputText(message);

            // Log incoming message
            var command = _commandRouter.ExtractCommand(inputText);
            await _messageLogService.LogIncomingMessageAsync(
                message.WamId,
                message.From,
                message.Type,
                inputText,
                command,
                session.UserId,
                await GetOrganizationIdAsync(session.UserId, ct),
                ct);

            // Handle cancel action
            if (inputText == "cancel_stop")
            {
                await SendResponseAsync(session, new CommandResult
                {
                    TextResponse = "Action cancelled. Reply *help* to see available commands."
                }, ct);
                return;
            }

            // Find appropriate handler
            var handler = _commandRouter.FindHandler(inputText, session);

            // Check auth requirement
            if (handler?.RequiresAuth == true && session.CurrentState != WhatsAppSessionState.Linked)
            {
                await SendResponseAsync(session, new CommandResult
                {
                    TextResponse = "Please link your account first. Reply with your *registered email address*.",
                    NewState = WhatsAppSessionState.AwaitingEmail
                }, ct);
                return;
            }

            // Execute handler or show help
            CommandResult result;
            if (handler != null)
            {
                _logger.LogDebug("Executing handler: {Handler}", handler.CommandName);
                result = await handler.HandleAsync(message, session, ct);
            }
            else
            {
                result = GetUnknownCommandResponse(session);
            }

            // Update session state and context atomically
            if (result.NewState != null || result.ContextUpdate != null)
            {
                await _sessionService.UpdateStateAndContextAsync(
                    session.Id,
                    result.NewState,
                    result.PendingEmail,
                    result.PendingVerificationId,
                    result.ContextUpdate,
                    ct);
            }

            // Send response
            if (!result.SkipResponse)
            {
                await SendResponseAsync(session, result, ct);
            }

            sw.Stop();
            _logger.LogInformation(
                "Processed WhatsApp message in {Ms}ms. Command: {Command}",
                sw.ElapsedMilliseconds,
                command ?? "unknown");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error processing WhatsApp message from {From}", _client.MaskPhoneNumber(message.From));

            // Send error response
            try
            {
                await _client.SendTextMessageAsync(
                    message.From,
                    "Sorry, something went wrong. Please try again.",
                    ct: ct);
            }
            catch
            {
                // Ignore send errors
            }
        }
    }

    public async Task<WhatsAppSendResult> SendToUserAsync(
        Guid userId,
        string content,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user?.WhatsAppPhoneNumber == null || !user.WhatsAppVerified || !user.WhatsAppOptedIn)
        {
            return new WhatsAppSendResult(false, null, "NOT_LINKED", "User not linked to WhatsApp");
        }

        // Check rate limiting
        var rateLimitResult = await CheckRateLimitAsync(userId, ct);
        if (!rateLimitResult.Allowed)
        {
            _logger.LogWarning("Rate limit exceeded for user {UserId}: {Reason}", userId, rateLimitResult.Reason);
            return new WhatsAppSendResult(false, null, "RATE_LIMITED", rateLimitResult.Reason);
        }

        // Check 24h window
        var session = await _sessionService.GetSessionByUserIdAsync(userId, ct);
        if (session == null || session.SessionExpiresAt < DateTime.UtcNow)
        {
            // Need to use template outside conversation window
            return new WhatsAppSendResult(false, null, "WINDOW_EXPIRED",
                "24-hour conversation window expired. Use template message.");
        }

        var sw = Stopwatch.StartNew();
        var result = await _client.SendTextMessageAsync(user.WhatsAppPhoneNumber, content, ct: ct);
        sw.Stop();

        // Log outgoing message
        await _messageLogService.LogOutgoingMessageAsync(
            result.MessageId,
            user.WhatsAppPhoneNumber,
            "text",
            content,
            null,
            userId,
            user.OrganizationId,
            result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed,
            result.ErrorCode,
            result.ErrorMessage,
            (int)sw.ElapsedMilliseconds,
            ct);

        return result;
    }

    public async Task<WhatsAppSendResult> SendTemplateToUserAsync(
        Guid userId,
        string templateName,
        Dictionary<string, string> variables,
        string language = "en",
        string? referenceType = null,
        Guid? referenceId = null,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user?.WhatsAppPhoneNumber == null || !user.WhatsAppVerified || !user.WhatsAppOptedIn)
        {
            return new WhatsAppSendResult(false, null, "NOT_LINKED", "User not linked to WhatsApp");
        }

        // Check rate limiting
        var rateLimitResult = await CheckRateLimitAsync(userId, ct);
        if (!rateLimitResult.Allowed)
        {
            _logger.LogWarning("Rate limit exceeded for user {UserId}: {Reason}", userId, rateLimitResult.Reason);
            return new WhatsAppSendResult(false, null, "RATE_LIMITED", rateLimitResult.Reason);
        }

        var parameterValues = variables.Values.ToList();
        var parameters = parameterValues
            .Select(v => new TemplateParameter("text", v))
            .ToList();

        var sw = Stopwatch.StartNew();
        var result = await _client.SendTemplateMessageAsync(
            user.WhatsAppPhoneNumber,
            templateName,
            language,
            parameters,
            ct: ct);
        sw.Stop();

        // Log template message with parameters for retry capability
        await _messageLogService.LogTemplateMessageAsync(
            result.MessageId,
            user.WhatsAppPhoneNumber,
            templateName,
            language,
            parameterValues,
            userId,
            user.OrganizationId,
            result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed,
            result.ErrorCode,
            result.ErrorMessage,
            (int)sw.ElapsedMilliseconds,
            referenceType,
            referenceId,
            correlationId,
            ct);

        return result;
    }

    public async Task<WhatsAppSendResult> SendButtonsToUserAsync(
        Guid userId,
        string bodyText,
        List<WhatsAppButton> buttons,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user?.WhatsAppPhoneNumber == null || !user.WhatsAppVerified || !user.WhatsAppOptedIn)
        {
            return new WhatsAppSendResult(false, null, "NOT_LINKED", "User not linked to WhatsApp");
        }

        // Check 24h window
        var session = await _sessionService.GetSessionByUserIdAsync(userId, ct);
        if (session == null || session.SessionExpiresAt < DateTime.UtcNow)
        {
            return new WhatsAppSendResult(false, null, "WINDOW_EXPIRED",
                "24-hour conversation window expired.");
        }

        var sw = Stopwatch.StartNew();
        var result = await _client.SendInteractiveButtonsAsync(
            user.WhatsAppPhoneNumber,
            bodyText,
            buttons,
            ct: ct);
        sw.Stop();

        // Log outgoing message
        await _messageLogService.LogOutgoingMessageAsync(
            result.MessageId,
            user.WhatsAppPhoneNumber,
            "interactive",
            bodyText,
            null,
            userId,
            user.OrganizationId,
            result.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed,
            result.ErrorCode,
            result.ErrorMessage,
            (int)sw.ElapsedMilliseconds,
            ct);

        return result;
    }

    private async Task SendResponseAsync(WhatsAppSession session, CommandResult result, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        WhatsAppSendResult sendResult;

        if (result.TemplateName != null)
        {
            sendResult = await _client.SendTemplateMessageAsync(
                session.PhoneNumber,
                result.TemplateName,
                "en",
                result.TemplateParameters,
                ct: ct);
        }
        else if (result.ListSections != null && result.ListButtonText != null)
        {
            sendResult = await _client.SendInteractiveListAsync(
                session.PhoneNumber,
                result.TextResponse ?? "",
                result.ListButtonText,
                result.ListSections,
                ct: ct);
        }
        else if (result.Buttons != null)
        {
            sendResult = await _client.SendInteractiveButtonsAsync(
                session.PhoneNumber,
                result.TextResponse ?? "",
                result.Buttons,
                ct: ct);
        }
        else if (result.TextResponse != null)
        {
            sendResult = await _client.SendTextMessageAsync(
                session.PhoneNumber,
                result.TextResponse,
                ct: ct);
        }
        else
        {
            return;
        }

        sw.Stop();

        // Log outgoing message
        await _messageLogService.LogOutgoingMessageAsync(
            sendResult.MessageId,
            session.PhoneNumber,
            result.Buttons != null || result.ListSections != null ? "interactive" :
            result.TemplateName != null ? "template" : "text",
            result.TextResponse,
            result.TemplateName,
            session.UserId,
            await GetOrganizationIdAsync(session.UserId, ct),
            sendResult.Success ? WhatsAppMessageStatus.Sent : WhatsAppMessageStatus.Failed,
            sendResult.ErrorCode,
            sendResult.ErrorMessage,
            (int)sw.ElapsedMilliseconds,
            ct);
    }

    private static string GetInputText(WhatsAppIncomingMessage message)
    {
        // Priority: button reply > list reply > text
        if (!string.IsNullOrEmpty(message.ButtonReplyId))
            return message.ButtonReplyId;

        if (!string.IsNullOrEmpty(message.ListReplyId))
            return message.ListReplyId;

        return message.Text ?? "";
    }

    private CommandResult GetUnknownCommandResponse(WhatsAppSession session)
    {
        if (session.CurrentState == WhatsAppSessionState.Linked)
        {
            return CommandResult.Text(
                "I didn't understand that command.\n\n" +
                "Reply *help* to see available commands.");
        }

        // For unlinked users, guide them to link
        return new CommandResult
        {
            TextResponse = "To use this bot, please link your account first.\n\n" +
                          "Reply with your *registered email address* to get started.",
            NewState = WhatsAppSessionState.AwaitingEmail
        };
    }

    private async Task<Guid?> GetOrganizationIdAsync(Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue)
            return null;

        var user = await _db.Users.FindAsync([userId.Value], ct);
        return user?.OrganizationId;
    }

    /// <summary>
    /// Check if user has exceeded rate limits.
    /// </summary>
    private async Task<(bool Allowed, string? Reason)> CheckRateLimitAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Check per-minute rate limit
        var oneMinuteAgo = now.AddMinutes(-1);
        var recentMessageCount = await _db.WhatsAppMessageLogs
            .Where(l =>
                l.UserId == userId &&
                l.Direction == WhatsAppMessageDirection.Outbound &&
                l.CreatedAt >= oneMinuteAgo)
            .CountAsync(ct);

        if (recentMessageCount >= _options.RateLimitPerMinute)
        {
            return (false, $"Rate limit exceeded: {_options.RateLimitPerMinute} messages per minute");
        }

        // Check daily limit
        var startOfDay = now.Date;
        var dailyMessageCount = await _db.WhatsAppMessageLogs
            .Where(l =>
                l.UserId == userId &&
                l.Direction == WhatsAppMessageDirection.Outbound &&
                l.CreatedAt >= startOfDay)
            .CountAsync(ct);

        if (dailyMessageCount >= _options.MaxMessagesPerUserPerDay)
        {
            return (false, $"Daily limit exceeded: {_options.MaxMessagesPerUserPerDay} messages per day");
        }

        return (true, null);
    }
}
