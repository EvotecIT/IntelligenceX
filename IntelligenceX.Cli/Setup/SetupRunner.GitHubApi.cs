using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Sodium;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private sealed class GitHubDeviceFlow {
        public static async Task<string?> LoginAsync(string clientId, string authBaseUrl, string scopes) {
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
    }

    private sealed class GitHubApi : IDisposable {
        private readonly HttpClient _http;

        public GitHubApi(string token, string apiBaseUrl) {
            _http = new HttpClient {
                BaseAddress = new Uri(apiBaseUrl)
            };
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public void Dispose() => _http.Dispose();

        public async Task<List<RepositoryInfo>> ListRepositoriesAsync() {
            var repos = new List<RepositoryInfo>();
            var page = 1;
            while (true) {
                var url = $"/user/repos?per_page=100&page={page}&affiliation=owner,collaborator,organization_member";
                var json = await GetJsonAsync(url).ConfigureAwait(false);
                if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0) {
                    break;
                }
                foreach (var item in json.EnumerateArray()) {
                    var fullName = item.GetProperty("full_name").GetString() ?? string.Empty;
                    var isPrivate = item.GetProperty("private").GetBoolean();
                    DateTimeOffset? updatedAt = null;
                    if (item.TryGetProperty("updated_at", out var updatedProperty) && updatedProperty.ValueKind == JsonValueKind.String) {
                        if (DateTimeOffset.TryParse(updatedProperty.GetString(), out var parsed)) {
                            updatedAt = parsed;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(fullName)) {
                        repos.Add(new RepositoryInfo(fullName, isPrivate, updatedAt));
                    }
                }
                if (json.GetArrayLength() < 100) {
                    break;
                }
                page++;
            }
            return repos;
        }

        public async Task<string> GetDefaultBranchAsync(string owner, string repo) {
            var json = await GetJsonAsync($"/repos/{owner}/{repo}").ConfigureAwait(false);
            return json.GetProperty("default_branch").GetString() ?? "main";
        }

        public async Task<string> GetBranchShaAsync(string owner, string repo, string branch) {
            var json = await GetJsonAsync($"/repos/{owner}/{repo}/git/ref/heads/{branch}").ConfigureAwait(false);
            return json.GetProperty("object").GetProperty("sha").GetString() ?? string.Empty;
        }

        public async Task EnsureBranchAsync(string owner, string repo, string branch, string sha) {
            var payload = new {
                @ref = $"refs/heads/{branch}",
                sha
            };
            await PostJsonAsync($"/repos/{owner}/{repo}/git/refs", payload, allowConflict: true)
                .ConfigureAwait(false);
        }

        public async Task<bool> CreateOrUpdateFileAsync(string owner, string repo, string path, string content,
            string message, string branch, bool overwrite) {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            var sha = await TryGetFileShaAsync(owner, repo, path, branch).ConfigureAwait(false);
            if (!overwrite && !string.IsNullOrWhiteSpace(sha)) {
                Console.WriteLine($"Skipped {path} (already exists). Use --force to overwrite.");
                return false;
            }

            var payload = new Dictionary<string, object?> {
                ["message"] = message,
                ["content"] = encoded,
                ["branch"] = branch
            };
            if (!string.IsNullOrWhiteSpace(sha)) {
                payload["sha"] = sha;
            }

            await PutJsonAsync($"/repos/{owner}/{repo}/contents/{path}", payload).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> DeleteFileAsync(string owner, string repo, string path, string message, string branch) {
            var sha = await TryGetFileShaAsync(owner, repo, path, branch).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sha)) {
                return false;
            }

            var payload = new Dictionary<string, object?> {
                ["message"] = message,
                ["sha"] = sha,
                ["branch"] = branch
            };

            await DeleteJsonAsync($"/repos/{owner}/{repo}/contents/{path}", payload).ConfigureAwait(false);
            return true;
        }

        public async Task<string?> CreatePullRequestAsync(string owner, string repo, string title, string head, string @base, string body) {
            var payload = new {
                title,
                head,
                @base,
                body
            };
            var result = await PostJsonAsync($"/repos/{owner}/{repo}/pulls", payload, allowConflict: true)
                .ConfigureAwait(false);
            if (result is null) {
                return null;
            }
            return result.Value.GetProperty("html_url").GetString();
        }

        public async Task SetSecretAsync(string owner, string repo, string name, string value) {
            var publicKeyJson = await GetJsonAsync($"/repos/{owner}/{repo}/actions/secrets/public-key").ConfigureAwait(false);
            var key = publicKeyJson.GetProperty("key").GetString();
            var keyId = publicKeyJson.GetProperty("key_id").GetString();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(keyId)) {
                throw new InvalidOperationException("Failed to read GitHub public key.");
            }

            var keyBytes = Convert.FromBase64String(key);
            var encrypted = SealedPublicKeyBox.Create(Encoding.UTF8.GetBytes(value), keyBytes);
            var encryptedB64 = Convert.ToBase64String(encrypted);

            var payload = new {
                encrypted_value = encryptedB64,
                key_id = keyId
            };

            await PutJsonAsync($"/repos/{owner}/{repo}/actions/secrets/{name}", payload).ConfigureAwait(false);
        }

        public async Task DeleteSecretAsync(string owner, string repo, string name) {
            using var response = await _http.DeleteAsync($"/repos/{owner}/{repo}/actions/secrets/{name}").ConfigureAwait(false);
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                return;
            }
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
        }

        public async Task<RepoFile?> TryGetFileAsync(string owner, string repo, string path, string branch) {
            try {
                var json = await GetJsonAsync($"/repos/{owner}/{repo}/contents/{path}?ref={branch}")
                    .ConfigureAwait(false);
                if (!json.TryGetProperty("content", out var contentProperty)) {
                    return null;
                }
                var content = contentProperty.GetString();
                var sha = json.GetProperty("sha").GetString();
                if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(sha)) {
                    return null;
                }

                var normalized = content.Replace("\n", string.Empty).Replace("\r", string.Empty);
                var bytes = Convert.FromBase64String(normalized);
                var text = Encoding.UTF8.GetString(bytes);
                return new RepoFile(sha, text);
            } catch {
                return null;
            }
        }

        public async Task<string?> TryGetFileShaAsync(string owner, string repo, string path, string branch) {
            try {
                var json = await GetJsonAsync($"/repos/{owner}/{repo}/contents/{path}?ref={branch}")
                    .ConfigureAwait(false);
                return json.GetProperty("sha").GetString();
            } catch {
                return null;
            }
        }

        private async Task<JsonElement> GetJsonAsync(string url) {
            using var response = await _http.GetAsync(url).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
            }
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.Clone();
        }

        private async Task<JsonElement?> PostJsonAsync(string url, object payload, bool allowConflict) {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                if (allowConflict && response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity) {
                    return null;
                }
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
            }
            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement.Clone();
        }

        private async Task PutJsonAsync(string url, object payload) {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PutAsync(url, content).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
            }
        }

        private async Task DeleteJsonAsync(string url, object payload) {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Delete, url) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
            }
        }
    }
}
