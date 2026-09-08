using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Copilot.Native;

/// <summary>Configures direct Copilot HTTP access without a CLI or native runtime.</summary>
public sealed class CopilotNativeOptions {
    /// <summary>Copilot inference API root. Custom deployments must use HTTPS.</summary>
    public string BaseUrl { get; set; } = "https://api.githubcopilot.com/";
    /// <summary>GitHub credential authorized for Copilot requests. Never written to disk automatically.</summary>
    public string? GitHubToken { get; set; }
    /// <summary>Optional credential callback, invoked for each request so hosts can renew credentials.</summary>
    public Func<CancellationToken, Task<string>>? TokenProvider { get; set; }
    /// <summary>Optional shared authentication store containing the <c>copilot</c> provider bundle.</summary>
    public IAuthBundleStore? AuthStore { get; set; }
    /// <summary>Account to select from the authentication store.</summary>
    public string? AccountId { get; set; }
    /// <summary>Registered GitHub app client ID for interactive sign-in and OAuth renewal. Not needed for supplied credentials.</summary>
    public string? GitHubClientId { get; set; }
    /// <summary>Optional client secret required by some registered app types when refreshing tokens. Never persisted by the SDK.</summary>
    public string? GitHubClientSecret { get; set; }
    /// <summary>GitHub login root used only for explicitly requested device sign-in or token renewal.</summary>
    public string GitHubAuthBaseUrl { get; set; } = "https://github.com/";
    /// <summary>GitHub account API root used to verify the signed-in identity.</summary>
    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com/";
    /// <summary>Allows COPILOT_GITHUB_TOKEN, GH_TOKEN, or GITHUB_TOKEN when no explicit credential source is configured.</summary>
    public bool UseEnvironmentCredentials { get; set; } = true;
    /// <summary>Whether inference should stream text deltas.</summary>
    public bool Streaming { get; set; } = true;
    /// <summary>Maximum duration of each HTTP operation, including body reads.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>Optional HTTP handler for host networking and deterministic tests. Ownership transfers to the client.</summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }

    /// <summary>Checks endpoint and timeout configuration before any request is sent.</summary>
    public void Validate() {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Copilot BaseUrl must be an HTTPS API root without credentials, query or fragment.", nameof(BaseUrl));
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        if (!string.IsNullOrWhiteSpace(GitHubToken) && TokenProvider is not null)
            throw new ArgumentException("Specify either GitHubToken or TokenProvider, not both.");
        foreach (string endpoint in new[] { GitHubAuthBaseUrl, GitHubApiBaseUrl }) {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var auth) || auth.Scheme != Uri.UriSchemeHttps
                || auth.UserInfo.Length != 0 || auth.Query.Length != 0 || auth.Fragment.Length != 0)
                throw new ArgumentException("GitHub authentication endpoints must use HTTPS without credentials, query or fragment.");
        }
    }

    internal CopilotNativeOptions Snapshot() => (CopilotNativeOptions)MemberwiseClone();
}
