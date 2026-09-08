using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class GitHubAuth {
    public static async Task<string?> DeviceFlowAsync(string clientId, string authBaseUrl, string scopes) {
        using var flow = new IntelligenceX.Authentication.GitHub.GitHubDeviceFlowClient(clientId, authBaseUrl);
        var pending = await flow.RequestCodeAsync(scopes).ConfigureAwait(false);
        Console.WriteLine($"Open {pending.VerificationUri} and enter code: {pending.UserCode}");
        TryOpenUrl(pending.VerificationUri.AbsoluteUri);
        try { return (await flow.CompleteAsync(pending).ConfigureAwait(false)).AccessToken; }
        catch (TimeoutException) { return null; }
    }
    private static void TryOpenUrl(string url) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        } catch {
            // Best effort.
        }
    }
}
