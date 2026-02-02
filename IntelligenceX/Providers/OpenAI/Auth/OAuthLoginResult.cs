using System.Collections.Generic;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Represents the result of an OAuth login flow.
/// </summary>
public sealed class OAuthLoginResult {
    /// <summary>
    /// Initializes a new OAuth login result.
    /// </summary>
    /// <param name="bundle">Authentication bundle.</param>
    /// <param name="raw">Raw response key/value data.</param>
    public OAuthLoginResult(AuthBundle bundle, Dictionary<string, string> raw) {
        Bundle = bundle;
        Raw = raw;
    }

    /// <summary>
    /// Gets the authentication bundle.
    /// </summary>
    public AuthBundle Bundle { get; }
    /// <summary>
    /// Gets the raw response data.
    /// </summary>
    public Dictionary<string, string> Raw { get; }
}
