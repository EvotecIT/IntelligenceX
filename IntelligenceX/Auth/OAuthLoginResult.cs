using System.Collections.Generic;

namespace IntelligenceX.Auth;

public sealed class OAuthLoginResult {
    public OAuthLoginResult(AuthBundle bundle, Dictionary<string, string> raw) {
        Bundle = bundle;
        Raw = raw;
    }

    public AuthBundle Bundle { get; }
    public Dictionary<string, string> Raw { get; }
}
