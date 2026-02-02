using System.Collections.Generic;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Result from an OAuth login or refresh call.
/// </summary>
/// <example>
/// <code>
/// var result = await service.LoginAsync(options);
/// Console.WriteLine(result.Bundle.AccountId);
/// </code>
/// </example>
public sealed class OAuthLoginResult {
    public OAuthLoginResult(AuthBundle bundle, Dictionary<string, string> raw) {
        Bundle = bundle;
        Raw = raw;
    }

    /// <summary>Auth bundle with tokens.</summary>
    public AuthBundle Bundle { get; }
    /// <summary>Raw token response fields.</summary>
    public Dictionary<string, string> Raw { get; }
}
