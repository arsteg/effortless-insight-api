using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.WhatsApp.Commands;

/// <summary>
/// Interface for WhatsApp bot command handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Check if this handler can handle the given input.
    /// </summary>
    bool CanHandle(string input, WhatsAppSession session);

    /// <summary>
    /// Get the command name for logging.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Whether this command requires a linked user.
    /// </summary>
    bool RequiresAuth { get; }

    /// <summary>
    /// Handle the command and return a response.
    /// </summary>
    Task<CommandResult> HandleAsync(
        WhatsAppIncomingMessage message,
        WhatsAppSession session,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a command execution.
/// </summary>
public class CommandResult
{
    /// <summary>
    /// Text response to send.
    /// </summary>
    public string? TextResponse { get; set; }

    /// <summary>
    /// Interactive buttons to send.
    /// </summary>
    public List<WhatsAppButton>? Buttons { get; set; }

    /// <summary>
    /// Interactive list sections to send.
    /// </summary>
    public List<WhatsAppListSection>? ListSections { get; set; }

    /// <summary>
    /// List button text (if ListSections is set).
    /// </summary>
    public string? ListButtonText { get; set; }

    /// <summary>
    /// Template to send (name).
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>
    /// Template parameters.
    /// </summary>
    public List<TemplateParameter>? TemplateParameters { get; set; }

    /// <summary>
    /// New session state to set.
    /// </summary>
    public string? NewState { get; set; }

    /// <summary>
    /// Pending email for verification flow.
    /// </summary>
    public string? PendingEmail { get; set; }

    /// <summary>
    /// Pending verification ID.
    /// </summary>
    public Guid? PendingVerificationId { get; set; }

    /// <summary>
    /// Context data to update.
    /// </summary>
    public Dictionary<string, object>? ContextUpdate { get; set; }

    /// <summary>
    /// Whether to skip sending a response (handled internally).
    /// </summary>
    public bool SkipResponse { get; set; }

    public static CommandResult Text(string text) => new() { TextResponse = text };

    public static CommandResult WithButtons(string text, List<WhatsAppButton> buttons) =>
        new() { TextResponse = text, Buttons = buttons };

    public static CommandResult WithList(string text, string buttonText, List<WhatsAppListSection> sections) =>
        new() { TextResponse = text, ListButtonText = buttonText, ListSections = sections };

    public static CommandResult Template(string name, List<TemplateParameter>? parameters = null) =>
        new() { TemplateName = name, TemplateParameters = parameters };

    public static CommandResult Skip() => new() { SkipResponse = true };
}
