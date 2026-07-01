using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Routes incoming messages to the appropriate command handler.
/// </summary>
public class CommandRouter
{
    private readonly IEnumerable<ICommandHandler> _handlers;
    private readonly ILogger<CommandRouter> _logger;

    // Command aliases
    private static readonly Dictionary<string, string[]> CommandAliases = new()
    {
        ["start"] = ["hi", "hello", "hey", "hola"],
        ["link"] = ["connect", "verify", "login"],
        ["status"] = ["dashboard", "summary", "home"],
        ["notices"] = ["list", "gst", "notice"],
        ["deadlines"] = ["due", "urgent", "pending", "upcoming"],
        ["tasks"] = ["mytasks", "assigned", "todo"],
        ["help"] = ["?", "commands", "menu", "options"],
        ["stop"] = ["unsubscribe", "optout", "opt-out", "quit", "bye"]
    };

    public CommandRouter(
        IEnumerable<ICommandHandler> handlers,
        ILogger<CommandRouter> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    /// <summary>
    /// Find the appropriate handler for the given input.
    /// </summary>
    public ICommandHandler? FindHandler(string input, WhatsAppSession session)
    {
        var normalizedInput = NormalizeInput(input);

        // First, try exact match handlers
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(normalizedInput, session))
            {
                _logger.LogDebug("Routing to handler: {Handler}", handler.CommandName);
                return handler;
            }
        }

        return null;
    }

    /// <summary>
    /// Extract the command name from input.
    /// </summary>
    public string? ExtractCommand(string input)
    {
        var normalized = NormalizeInput(input);

        foreach (var (command, aliases) in CommandAliases)
        {
            if (command == normalized || aliases.Contains(normalized))
            {
                return command;
            }
        }

        // Check if input matches any command directly
        if (CommandAliases.ContainsKey(normalized))
        {
            return normalized;
        }

        return null;
    }

    /// <summary>
    /// Normalize input for command matching.
    /// </summary>
    public static string NormalizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return input.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Check if input is a button/list reply ID.
    /// </summary>
    public static bool IsButtonReply(WhatsAppIncomingMessage message)
    {
        return !string.IsNullOrEmpty(message.ButtonReplyId) ||
               !string.IsNullOrEmpty(message.ListReplyId);
    }

    /// <summary>
    /// Get the reply ID from a button/list reply.
    /// </summary>
    public static string GetReplyId(WhatsAppIncomingMessage message)
    {
        return message.ButtonReplyId ?? message.ListReplyId ?? string.Empty;
    }

    /// <summary>
    /// Check if input looks like an email.
    /// </summary>
    public static bool LooksLikeEmail(string input)
    {
        return input.Contains('@') && input.Contains('.');
    }

    /// <summary>
    /// Check if input looks like a verification code.
    /// </summary>
    public static bool LooksLikeVerificationCode(string input)
    {
        var digits = new string(input.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 && digits.Length <= 8;
    }
}
