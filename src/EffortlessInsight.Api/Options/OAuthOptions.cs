namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for OAuth providers.
/// </summary>
public class OAuthOptions
{
    public const string SectionName = "OAuth";

    /// <summary>
    /// Google OAuth configuration.
    /// </summary>
    public OAuthProviderOptions Google { get; set; } = new();

    /// <summary>
    /// Microsoft OAuth configuration.
    /// </summary>
    public OAuthProviderOptions Microsoft { get; set; } = new();

    /// <summary>
    /// Base URL for OAuth callbacks (frontend URL).
    /// </summary>
    public string CallbackBaseUrl { get; set; } = "http://localhost:3000";
}

/// <summary>
/// Configuration for a single OAuth provider.
/// </summary>
public class OAuthProviderOptions
{
    /// <summary>
    /// OAuth Client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth Client Secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Whether this provider is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Checks if the provider is properly configured.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
