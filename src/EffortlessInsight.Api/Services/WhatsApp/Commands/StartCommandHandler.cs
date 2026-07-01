using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Handles the start/welcome command for unlinked users.
/// </summary>
public class StartCommandHandler : ICommandHandler
{
    private static readonly string[] Greetings = ["hi", "hello", "hey", "hola", "start"];

    public string CommandName => "start";
    public bool RequiresAuth => false;

    public bool CanHandle(string input, WhatsAppSession session)
    {
        // Handle start command for unlinked users
        if (session.CurrentState == WhatsAppSessionState.Linked)
            return false;

        var normalized = CommandRouter.NormalizeInput(input);
        return Greetings.Contains(normalized) ||
               session.CurrentState == WhatsAppSessionState.Start;
    }

    public Task<CommandResult> HandleAsync(
        WhatsAppIncomingMessage message,
        WhatsAppSession session,
        CancellationToken ct = default)
    {
        var welcomeText = """
            Welcome to *EffortlessInsight*!

            I can help you stay on top of your GST compliance with:
            - Deadline reminders
            - Quick notice summaries
            - Task updates

            To get started, please link your account.
            Reply with your *registered email address*.
            """;

        return Task.FromResult(new CommandResult
        {
            TextResponse = welcomeText,
            NewState = WhatsAppSessionState.AwaitingEmail
        });
    }
}
