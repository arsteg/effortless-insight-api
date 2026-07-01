using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the stop/unsubscribe command.
/// </summary>
public class StopCommandHandler : ICommandHandler
{
    private static readonly string[] Triggers = ["stop", "unsubscribe", "optout", "opt-out", "quit", "bye"];
    private readonly IWhatsAppSessionService _sessionService;

    public StopCommandHandler(IWhatsAppSessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public string CommandName => "stop";
    public bool RequiresAuth => true;

    public bool CanHandle(string input, WhatsAppSession session)
    {
        if (session.CurrentState != WhatsAppSessionState.Linked)
            return false;

        var normalized = CommandRouter.NormalizeInput(input);
        return Triggers.Contains(normalized);
    }

    public async Task<CommandResult> HandleAsync(
        WhatsAppIncomingMessage message,
        WhatsAppSession session,
        CancellationToken ct = default)
    {
        if (!session.UserId.HasValue)
        {
            return CommandResult.Text("You're not currently linked. Nothing to unsubscribe from.");
        }

        // Check for confirmation
        var input = message.Text?.Trim().ToLower() ?? message.ButtonReplyId ?? "";

        // Check if this is a confirmation - either via button or pending context
        var isPendingConfirmation = session.Context.TryGetValue("pendingStop", out var pending) && pending is true;

        if (input == "confirm_stop" || (isPendingConfirmation && input != "cancel_stop"))
        {
            // Actually unlink
            await _sessionService.UnlinkSessionAsync(session.UserId.Value, ct);

            var goodbyeText = """
                *You've been unsubscribed*

                You will no longer receive WhatsApp notifications.

                You can re-link anytime by sending *hi* to this number.

                Thank you for using EffortlessInsight!
                """;

            return new CommandResult
            {
                TextResponse = goodbyeText,
                NewState = WhatsAppSessionState.Start,
                // Clear the pending context
                ContextUpdate = new Dictionary<string, object>
                {
                    ["pendingStop"] = false,
                    ["lastCommand"] = ""
                }
            };
        }

        // Ask for confirmation
        var confirmText = """
            Are you sure you want to unsubscribe from WhatsApp notifications?

            You'll stop receiving:
            - Deadline reminders
            - High risk alerts
            - Task assignments
            - Daily digest

            You can always re-link later.
            """;

        var buttons = new List<WhatsAppButton>
        {
            new("confirm_stop", "Yes, Unsubscribe"),
            new("cancel_stop", "Cancel")
        };

        return new CommandResult
        {
            TextResponse = confirmText,
            Buttons = buttons,
            ContextUpdate = new Dictionary<string, object>
            {
                ["pendingStop"] = true
            }
        };
    }
}
