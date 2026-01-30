using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Release;

internal sealed class GitHubReleasePublisher : IDisposable {
    private readonly HttpClient _api;

    public GitHubReleasePublisher(string token, string apiBaseUrl = "https://api.github.com") {
        _api = new HttpClient {
            BaseAddress = new Uri(apiBaseUrl)
        };
        _api.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        _api.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _api.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public void Dispose() => _api.Dispose();

    public async Task<ReleaseInfo> GetOrCreateReleaseAsync(string owner, string repo, string tag, string title, string notes) {
        var existing = await TryGetReleaseByTagAsync(owner, repo, tag).ConfigureAwait(false);
        if (existing.HasValue) {
            return existing.Value;
        }

        var payload = new {
            tag_name = tag,
            name = title,
            body = notes,
            draft = false,
            prerelease = false
        };
        using var response = await PostJsonAsync($"/repos/{owner}/{repo}/releases", payload).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        using var doc = JsonDocument.Parse(content);
        return ParseRelease(doc.RootElement);
    }

    public async Task UploadAssetAsync(string owner, string repo, ReleaseInfo release, string assetPath) {
        if (!File.Exists(assetPath)) {
            throw new InvalidOperationException($"Asset not found: {assetPath}");
        }

        await DeleteAssetIfExistsAsync(owner, repo, release.Id, Path.GetFileName(assetPath))
            .ConfigureAwait(false);

        using var content = new StreamContent(File.OpenRead(assetPath));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        var uploadUrl = BuildUploadUrl(release, owner, repo, Path.GetFileName(assetPath));
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl) {
            Content = content
        };
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = _api.DefaultRequestHeaders.Authorization;
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _api.SendAsync(request).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub upload failed ({(int)response.StatusCode}): {responseText}");
        }
    }

    private async Task<ReleaseInfo?> TryGetReleaseByTagAsync(string owner, string repo, string tag) {
        using var response = await _api.GetAsync($"/repos/{owner}/{repo}/releases/tags/{tag}").ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
            return null;
        }
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        using var doc = JsonDocument.Parse(content);
        return ParseRelease(doc.RootElement);
    }

    private async Task DeleteAssetIfExistsAsync(string owner, string repo, long releaseId, string assetName) {
        using var response = await _api.GetAsync($"/repos/{owner}/{repo}/releases/{releaseId}/assets?per_page=100").ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return;
        }
        foreach (var asset in doc.RootElement.EnumerateArray()) {
            var name = asset.GetProperty("name").GetString();
            if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var id = asset.GetProperty("id").GetInt64();
            using var deleteResponse = await _api.DeleteAsync($"/repos/{owner}/{repo}/releases/assets/{id}").ConfigureAwait(false);
            if (!deleteResponse.IsSuccessStatusCode) {
                var deleteContent = await deleteResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"GitHub asset delete failed ({(int)deleteResponse.StatusCode}): {deleteContent}");
            }
            return;
        }
    }

    private static ReleaseInfo ParseRelease(JsonElement root) {
        var id = root.GetProperty("id").GetInt64();
        var uploadUrl = root.TryGetProperty("upload_url", out var upload) ? upload.GetString() : null;
        return new ReleaseInfo(id, SanitizeUploadUrl(uploadUrl));
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload) {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _api.PostAsync(url, content).ConfigureAwait(false);
    }

    private static string BuildUploadUrl(ReleaseInfo release, string owner, string repo, string assetName) {
        var baseUrl = string.IsNullOrWhiteSpace(release.UploadUrl)
            ? $"https://uploads.github.com/repos/{owner}/{repo}/releases/{release.Id}/assets"
            : release.UploadUrl;
        var separator = baseUrl.Contains("?", StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}name={Uri.EscapeDataString(assetName)}";
    }

    private static string SanitizeUploadUrl(string? uploadUrl) {
        if (string.IsNullOrWhiteSpace(uploadUrl)) {
            return string.Empty;
        }
        var idx = uploadUrl.IndexOf('{');
        return idx >= 0 ? uploadUrl.Substring(0, idx) : uploadUrl;
    }
}

internal readonly record struct ReleaseInfo(long Id, string UploadUrl);
