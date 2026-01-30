using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal static class GitHubDeviceFlowClient {
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient() {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        return http;
    }

    public static async Task<DeviceCodeResponse> RequestCodeAsync(string clientId, string? authBaseUrl, string? scopes) {
        var baseUrl = string.IsNullOrWhiteSpace(authBaseUrl) ? "https://github.com" : authBaseUrl;
        var deviceUri = new Uri(new Uri(baseUrl), "/login/device/code");
        using var request = new HttpRequestMessage(HttpMethod.Post, deviceUri) {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["client_id"] = clientId,
                ["scope"] = scopes ?? "repo workflow read:org"
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await Http.SendAsync(request).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new DeviceCodeResponse {
            DeviceCode = root.GetProperty("device_code").GetString() ?? string.Empty,
            UserCode = root.GetProperty("user_code").GetString() ?? string.Empty,
            VerificationUri = root.GetProperty("verification_uri").GetString() ?? string.Empty,
            IntervalSeconds = root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5,
            ExpiresIn = root.TryGetProperty("expires_in", out var expiresIn) ? expiresIn.GetInt32() : 0
        };
    }

    public static async Task<string?> PollTokenAsync(string clientId, string deviceCode, string? authBaseUrl, int intervalSeconds, int expiresInSeconds) {
        var baseUrl = string.IsNullOrWhiteSpace(authBaseUrl) ? "https://github.com" : authBaseUrl;
        var tokenUri = new Uri(new Uri(baseUrl), "/login/oauth/access_token");
        var interval = Math.Max(1, intervalSeconds);
        var timeoutSeconds = expiresInSeconds > 0 ? expiresInSeconds : 600;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline) {
            await Task.Delay(TimeSpan.FromSeconds(interval)).ConfigureAwait(false);
            using var pollRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri) {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["client_id"] = clientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                })
            };
            pollRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var pollResponse = await Http.SendAsync(pollRequest).ConfigureAwait(false);
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
}

internal sealed class DeviceCodeResponse {
    public string DeviceCode { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; }
    public int ExpiresIn { get; set; }
}
