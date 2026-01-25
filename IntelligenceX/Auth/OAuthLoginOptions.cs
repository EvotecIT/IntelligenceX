using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Auth;

public sealed class OAuthLoginOptions {
    public OAuthLoginOptions(OAuthConfig config) {
        Config = config;
    }

    public OAuthConfig Config { get; }
    public Func<string, Task>? OnAuthUrl { get; set; }
    public Func<string, Task<string>>? OnPrompt { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(3);
    public bool UseLocalListener { get; set; } = true;
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
