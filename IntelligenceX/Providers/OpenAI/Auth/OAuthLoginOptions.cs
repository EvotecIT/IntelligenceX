using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Options for the OAuth login flow.
/// </summary>
public sealed class OAuthLoginOptions {
    /// <summary>
    /// Initializes login options with the provided configuration.
    /// </summary>
    /// <param name="config">OAuth configuration.</param>
    public OAuthLoginOptions(OAuthConfig config) {
        Config = config;
    }

    /// <summary>
    /// Gets the OAuth configuration.
    /// </summary>
    public OAuthConfig Config { get; }
    /// <summary>
    /// Callback invoked with the authorization URL.
    /// </summary>
    public Func<string, Task>? OnAuthUrl { get; set; }
    /// <summary>
    /// Callback used to prompt for user input.
    /// </summary>
    public Func<string, Task<string>>? OnPrompt { get; set; }
    /// <summary>
    /// Overall timeout for the login flow.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>
    /// Whether to use a local listener for the redirect.
    /// </summary>
    public bool UseLocalListener { get; set; } = true;
    /// <summary>
    /// Cancellation token for the login flow.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
