using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class GitHubAuth {
    public static async Task<string?> DeviceFlowAsync(string clientId, string authBaseUrl, string scopes) {
        using var http = new HttpClient();
        var deviceUri = new Uri(new Uri(authBaseUrl), "/login/device/code");
        var request = new HttpRequestMessage(HttpMethod.Post, deviceUri) {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["client_id"] = clientId,
                ["scope"] = scopes
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await http.SendAsync(request).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var deviceCode = root.GetProperty("device_code").GetString();
        var userCode = root.GetProperty("user_code").GetString();
        var verificationUri = root.GetProperty("verification_uri").GetString();
        var interval = root.GetProperty("interval").GetInt32();
        var expiresIn = root.GetProperty("expires_in").GetInt32();

        if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(userCode) || string.IsNullOrWhiteSpace(verificationUri)) {
            throw new InvalidOperationException("Invalid device flow response.");
        }

        Console.WriteLine($"Open {verificationUri} and enter code: {userCode}");
        TryOpenUrl(verificationUri);

        var tokenUri = new Uri(new Uri(authBaseUrl), "/login/oauth/access_token");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        while (DateTimeOffset.UtcNow < deadline) {
            await Task.Delay(TimeSpan.FromSeconds(interval)).ConfigureAwait(false);
            var pollRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri) {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["client_id"] = clientId,
                    ["device_code"] = deviceCode!,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                })
            };
            pollRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var pollResponse = await http.SendAsync(pollRequest).ConfigureAwait(false);
            var pollJson = await pollResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            pollResponse.EnsureSuccessStatusCode();
            using var pollDoc = JsonDocument.Parse(pollJson);
            var pollRoot = pollDoc.RootElement;
            if (pollRoot.TryGetProperty("access_token", out var accessToken)) {
                return accessToken.GetString();
            }
            if (pollRoot.TryGetProperty("error", out var error)) {
                var code = error.GetString();
                if (string.Equals(code, "authorization_pending", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if (string.Equals(code, "slow_down", StringComparison.OrdinalIgnoreCase)) {
                    interval += 5;
                    continue;
                }
                throw new InvalidOperationException($"GitHub device flow error: {code}");
            }
        }

        return null;
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
