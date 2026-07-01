using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the email and OTP verification flow for account linking.
/// </summary>
public class LinkCommandHandler : ICommandHandler
{
    private readonly IWhatsAppVerificationService _verificationService;
    private readonly IWhatsAppSessionService _sessionService;
    private readonly ILogger<LinkCommandHandler> _logger;

    public LinkCommandHandler(
        IWhatsAppVerificationService verificationService,
        IWhatsAppSessionService sessionService,
        ILogger<LinkCommandHandler> logger)
    {
        _verificationService = verificationService;
        _sessionService = sessionService;
        _logger = logger;
    }

    public string CommandName => "link";
    public bool RequiresAuth => false;

    public bool CanHandle(string input, WhatsAppSession session)
    {
        var normalized = CommandRouter.NormalizeInput(input);

        // Handle explicit link command
        if (normalized is "link" or "connect" or "verify")
            return true;

        // Handle email input when awaiting email
        if (session.CurrentState == WhatsAppSessionState.AwaitingEmail &&
            CommandRouter.LooksLikeEmail(input))
            return true;

        // Handle OTP input when awaiting OTP
        if (session.CurrentState == WhatsAppSessionState.AwaitingOtp &&
            CommandRouter.LooksLikeVerificationCode(input))
            return true;

        return false;
    }

    public async Task<CommandResult> HandleAsync(
        WhatsAppIncomingMessage message,
        WhatsAppSession session,
        CancellationToken ct = default)
    {
        var input = message.Text?.Trim() ?? message.ButtonReplyId ?? "";
        var normalized = CommandRouter.NormalizeInput(input);

        // If already linked, inform user
        if (session.CurrentState == WhatsAppSessionState.Linked)
        {
            return CommandResult.Text("Your account is already linked. Reply *help* to see available commands.");
        }

        // Handle explicit link command - ask for email
        if (normalized is "link" or "connect" or "verify")
        {
            return new CommandResult
            {
                TextResponse = "Please reply with your *registered email address* to link your account.",
                NewState = WhatsAppSessionState.AwaitingEmail
            };
        }

        // Handle email input
        if (session.CurrentState == WhatsAppSessionState.AwaitingEmail &&
            CommandRouter.LooksLikeEmail(input))
        {
            return await HandleEmailInputAsync(input, message.From, session, ct);
        }

        // Handle OTP input
        if (session.CurrentState == WhatsAppSessionState.AwaitingOtp &&
            CommandRouter.LooksLikeVerificationCode(input))
        {
            return await HandleOtpInputAsync(input, message.From, session, ct);
        }

        return CommandResult.Text("I didn't understand. Please reply with your email address.");
    }

    private async Task<CommandResult> HandleEmailInputAsync(
        string email,
        string phoneNumber,
        WhatsAppSession session,
        CancellationToken ct)
    {
        var (success, message, verificationId) = await _verificationService.InitiateVerificationFromBotAsync(
            email.Trim().ToLowerInvariant(),
            phoneNumber,
            ct);

        if (!success)
        {
            return CommandResult.Text(message ?? "Unable to verify email. Please try again.");
        }

        var responseText = """
            We've sent a 6-digit verification code to your EffortlessInsight app.

            Please enter the code here within 10 minutes.

            _Didn't receive it? Check your app notifications._
            """;

        return new CommandResult
        {
            TextResponse = responseText,
            NewState = WhatsAppSessionState.AwaitingOtp,
            PendingEmail = email,
            PendingVerificationId = verificationId
        };
    }

    private async Task<CommandResult> HandleOtpInputAsync(
        string code,
        string phoneNumber,
        WhatsAppSession session,
        CancellationToken ct)
    {
        var digits = new string(code.Where(char.IsDigit).ToArray());

        var (success, userId, message) = await _verificationService.VerifyCodeByPhoneAsync(
            phoneNumber,
            digits,
            ct);

        if (!success)
        {
            return CommandResult.Text(message ?? "Invalid code. Please try again.");
        }

        // Link session to user
        if (userId.HasValue)
        {
            await _sessionService.LinkSessionToUserAsync(session.Id, userId.Value, ct);
        }

        var successText = """
            *Account linked successfully!*

            You'll now receive GST compliance alerts on WhatsApp.

            *Quick Commands:*
            - *status* - Dashboard summary
            - *notices* - Recent notices
            - *deadlines* - Upcoming due dates
            - *tasks* - Your assigned tasks
            - *help* - All commands
            """;

        return new CommandResult
        {
            TextResponse = successText,
            NewState = WhatsAppSessionState.Linked
        };
    }
}
