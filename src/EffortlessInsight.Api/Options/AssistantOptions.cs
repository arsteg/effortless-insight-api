namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration for the app-wide end-user assistant gateway.
/// The assistant engine itself runs in the Python AI service; this API
/// authenticates users, persists conversations, and proxies chat turns.
/// </summary>
public class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>Kill switch for the whole assistant surface.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many recent messages are sent to the AI service per turn.</summary>
    public int HistoryWindowMessages { get; set; } = 20;

    /// <summary>Timeout for one chat turn against the AI service (seconds).</summary>
    public int AiServiceTimeoutSeconds { get; set; } = 120;

    /// <summary>Maximum conversations kept per user per organization (oldest pruned from listing).</summary>
    public int MaxConversationsPerUser { get; set; } = 50;
}
