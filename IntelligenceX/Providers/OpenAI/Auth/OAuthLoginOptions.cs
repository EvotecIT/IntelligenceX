using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Options for an OAuth login flow.
/// </summary>
public sealed class OAuthLoginOptions {
    /// <summary>
    /// Creates options for the given OAuth configuration.
    /// </summary>
    public OAuthLoginOptions(OAuthConfig config) {
        Config = config;
    }

    /// <summary>
    /// OAuth configuration used for the flow.
    /// </summary>
    public OAuthConfig Config { get; }
    /// <summary>
    /// Callback invoked with the authorization URL.
    /// </summary>
    public Func<string, Task>? OnAuthUrl { get; set; }
    /// <summary>
    /// Prompt handler for manual code entry.
    /// </summary>
    public Func<string, Task<string>>? OnPrompt { get; set; }
    /// <summary>
    /// Maximum time to wait for login.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>
    /// Whether to spin up a local listener for redirects.
    /// </summary>
    public bool UseLocalListener { get; set; } = true;
    /// <summary>
    /// Cancellation token for the flow.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
