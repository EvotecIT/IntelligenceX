using System;
using System.Threading.Tasks;
using IntelligenceX.Authentication.GitHub;
using NativeDeviceFlow = IntelligenceX.Authentication.GitHub.GitHubDeviceFlowClient;

namespace IntelligenceX.Cli.Setup.Web;

internal static class GitHubDeviceFlowClient {
    public static async Task<DeviceCodeResponse> RequestCodeAsync(string clientId, string? authBaseUrl, string? scopes) {
        using var flow = new NativeDeviceFlow(clientId, ResolveBase(authBaseUrl));
        var pending = await flow.RequestCodeAsync(scopes ?? "repo workflow read:org").ConfigureAwait(false);
        return new DeviceCodeResponse {
            DeviceCode = pending.DeviceCode, UserCode = pending.UserCode,
            VerificationUri = pending.VerificationUri.AbsoluteUri, IntervalSeconds = pending.IntervalSeconds,
            ExpiresIn = Math.Max(1, (int)(pending.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)
        };
    }

    public static async Task<string?> PollTokenAsync(string clientId, string deviceCode, string? authBaseUrl, int intervalSeconds, int expiresInSeconds) {
        string root = ResolveBase(authBaseUrl);
        using var flow = new NativeDeviceFlow(clientId, root);
        var pending = new GitHubDeviceAuthorization(deviceCode, string.Empty, new Uri(root),
            Math.Clamp(intervalSeconds, 1, 300), DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(expiresInSeconds > 0 ? expiresInSeconds : 600, 1, 3600)));
        try { return (await flow.CompleteAsync(pending).ConfigureAwait(false)).AccessToken; }
        catch (TimeoutException) { return null; }
    }

    private static string ResolveBase(string? value) => string.IsNullOrWhiteSpace(value) ? "https://github.com/" : value;
}

internal sealed class DeviceCodeResponse {
    public string DeviceCode { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; }
    public int ExpiresIn { get; set; }
}