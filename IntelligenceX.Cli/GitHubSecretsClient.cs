using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Sodium;

namespace IntelligenceX.Cli;

internal sealed class GitHubSecretsClient : IDisposable {
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public GitHubSecretsClient(string token, string apiBaseUrl = "https://api.github.com") {
        _http = new HttpClient {
            BaseAddress = new Uri(apiBaseUrl)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public void Dispose() => _http.Dispose();

    public async Task SetRepoSecretAsync(string owner, string repo, string name, string value) {
        ValidateRepoInputs(owner, repo, name);
        var key = await GetPublicKeyAsync($"/repos/{owner}/{repo}/actions/secrets/public-key").ConfigureAwait(false);
        var payload = new Dictionary<string, object?> {
            ["encrypted_value"] = Encrypt(value, key.PublicKey),
            ["key_id"] = key.KeyId
        };
        await PutJsonAsync($"/repos/{owner}/{repo}/actions/secrets/{name}", payload).ConfigureAwait(false);
    }

    public async Task SetOrgSecretAsync(string org, string name, string value, string visibility = "all", IReadOnlyList<long>? selectedRepositoryIds = null) {
        ValidateOrgInputs(org, name);
        var key = await GetPublicKeyAsync($"/orgs/{org}/actions/secrets/public-key").ConfigureAwait(false);
        var normalizedVisibility = NormalizeVisibility(visibility);
        if (normalizedVisibility == "selected" && (selectedRepositoryIds is null || selectedRepositoryIds.Count == 0)) {
            throw new InvalidOperationException("Selected visibility requires repository IDs. Use all/private/selected or provide selected repository IDs.");
        }
        var payload = new Dictionary<string, object?> {
            ["encrypted_value"] = Encrypt(value, key.PublicKey),
            ["key_id"] = key.KeyId,
            ["visibility"] = normalizedVisibility
        };
        if (normalizedVisibility == "selected") {
            payload["selected_repository_ids"] = selectedRepositoryIds;
        }
        await PutJsonAsync($"/orgs/{org}/actions/secrets/{name}", payload).ConfigureAwait(false);
    }

    private static string NormalizeVisibility(string visibility) {
        if (string.IsNullOrWhiteSpace(visibility)) {
            return "all";
        }
        var normalized = visibility.Trim().ToLowerInvariant();
        return normalized switch {
            "private" => "private",
            "selected" => "selected",
            "all" => "all",
            _ => throw new InvalidOperationException("Invalid visibility. Use all, private, or selected.")
        };
    }

    private async Task<PublicKeyInfo> GetPublicKeyAsync(string url) {
        using var response = await _http.GetAsync(url).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var key = root.GetProperty("key").GetString();
        var keyId = root.GetProperty("key_id").GetString();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(keyId)) {
            throw new InvalidOperationException("Failed to read GitHub public key.");
        }
        return new PublicKeyInfo(key!, keyId!);
    }

    private async Task PutJsonAsync(string url, object payload) {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        using var response = await _http.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"))
            .ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
    }

    private static string Encrypt(string value, string publicKey) {
        var keyBytes = Convert.FromBase64String(publicKey);
        var encrypted = SealedPublicKeyBox.Create(Encoding.UTF8.GetBytes(value), keyBytes);
        return Convert.ToBase64String(encrypted);
    }

    private static void ValidateRepoInputs(string owner, string repo, string name) {
        ValidateIdentifier(owner, "owner");
        ValidateIdentifier(repo, "repo");
        ValidateSecretName(name);
    }

    private static void ValidateOrgInputs(string org, string name) {
        ValidateIdentifier(org, "org");
        ValidateSecretName(name);
    }

    private static void ValidateIdentifier(string value, string label) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException($"{label} is required.");
        }
        var trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal)) {
            throw new InvalidOperationException($"{label} must not contain leading or trailing whitespace.");
        }
        for (var i = 0; i < trimmed.Length; i++) {
            var ch = trimmed[i];
            if (char.IsWhiteSpace(ch) || ch == '/' || ch == '\\') {
                throw new InvalidOperationException($"{label} must not contain spaces or slashes.");
            }
        }
    }

    private static void ValidateSecretName(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new InvalidOperationException("Secret name is required.");
        }
        var trimmed = name.Trim();
        if (!string.Equals(trimmed, name, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Secret name must not contain leading or trailing whitespace.");
        }
        if (trimmed.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Secret name must not start with GITHUB_.");
        }
        for (var i = 0; i < trimmed.Length; i++) {
            var ch = trimmed[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_')) {
                throw new InvalidOperationException("Secret name must contain only letters, numbers, or underscores.");
            }
        }
    }

    private readonly record struct PublicKeyInfo(string PublicKey, string KeyId);
}

