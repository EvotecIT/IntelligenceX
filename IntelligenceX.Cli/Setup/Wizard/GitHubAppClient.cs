using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Wizard;

internal sealed class GitHubAppClient : IDisposable {
    private readonly HttpClient _http;
    private readonly long _appId;
    private readonly string _appKeyPem;

    public GitHubAppClient(long appId, string appKeyPem, string apiBaseUrl) {
        _appId = appId;
        _appKeyPem = appKeyPem;
        _http = new HttpClient {
            BaseAddress = new Uri(apiBaseUrl)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public void Dispose() => _http.Dispose();

    public async Task<List<GitHubInstallationInfo>> ListInstallationsAsync() {
        var json = await GetJsonAsync("/app/installations").ConfigureAwait(false);
        if (json.ValueKind != JsonValueKind.Array) {
            return new List<GitHubInstallationInfo>();
        }

        var installs = new List<GitHubInstallationInfo>();
        foreach (var item in json.EnumerateArray()) {
            var id = item.GetProperty("id").GetInt64();
            var account = item.GetProperty("account");
            var login = account.GetProperty("login").GetString() ?? "unknown";
            installs.Add(new GitHubInstallationInfo(id, login));
        }
        return installs;
    }

    public async Task<string?> CreateInstallationTokenAsync(long installationId) {
        var json = await PostJsonAsync($"/app/installations/{installationId}/access_tokens").ConfigureAwait(false);
        if (json is null || json.Value.ValueKind != JsonValueKind.Object) {
            return null;
        }
        if (!json.Value.TryGetProperty("token", out var token)) {
            return null;
        }
        return token.GetString();
    }

    private async Task<JsonElement> GetJsonAsync(string url) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BuildJwt());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement?> PostJsonAsync(string url) {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BuildJwt());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private string BuildJwt() => GitHubAppJwt.Create(_appId, _appKeyPem);
}
