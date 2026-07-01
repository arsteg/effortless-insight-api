using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the help command to show available commands.
/// </summary>
public class HelpCommandHandler : ICommandHandler
{
    private static readonly string[] Triggers = ["help", "?", "commands", "menu", "options"];

    public string CommandName => "help";
    public bool RequiresAuth => false;

    public bool CanHandle(string input, WhatsAppSession session)
    {
        var normalized = CommandRouter.NormalizeInput(input);
        return Triggers.Contains(normalized);
    }

    public Task<CommandResult> HandleAsync(
        WhatsAppIncomingMessage message,
        WhatsAppSession session,
        CancellationToken ct = default)
    {
        if (session.CurrentState != WhatsAppSessionState.Linked)
        {
            var unlinkedHelp = """
                *EffortlessInsight Help*

                To use this bot, you need to link your account first.

                Reply with your *registered email address* to get started.

                Or visit effortlessinsight.com to create an account.
                """;

            return Task.FromResult(new CommandResult
            {
                TextResponse = unlinkedHelp,
                NewState = WhatsAppSessionState.AwaitingEmail
            });
        }

        var helpText = """
            *EffortlessInsight Commands*

            *Dashboard:*
            - *status* - Quick summary of pending items

            *Notices:*
            - *notices* - View recent notices
            - *deadlines* - Upcoming due dates

            *Tasks:*
            - *tasks* - Your assigned tasks

            *Account:*
            - *stop* - Unsubscribe from notifications
            - *help* - Show this menu

            _Reply with any command to get started!_
            """;

        return Task.FromResult(CommandResult.Text(helpText));
    }
}
