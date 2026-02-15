using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Todo;
using Sodium;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private sealed class GitHubDeviceFlow {
        public static async Task<string?> LoginAsync(string clientId, string authBaseUrl, string scopes) {
            using var http = new HttpClient();
            var deviceUri = new Uri(new Uri(authBaseUrl), "/login/device/code");
            using var request = new HttpRequestMessage(HttpMethod.Post, deviceUri) {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["client_id"] = clientId,
                    ["scope"] = scopes
                })
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request).ConfigureAwait(false);
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
                using var pollRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri) {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                        ["client_id"] = clientId,
                        ["device_code"] = deviceCode!,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    })
                };
                pollRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var pollResponse = await http.SendAsync(pollRequest).ConfigureAwait(false);
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
        public readonly record struct EnsureRepositoryLabelsResult(int CreatedCount, int TotalCount);

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
                    if (item.TryGetProperty("updated_at", out var updatedProperty)
                        && updatedProperty.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(updatedProperty.GetString(), out var parsed)) {
                        updatedAt = parsed;
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

        public async Task<int> CreateIssueAsync(string owner, string repo, string title, string body) {
            var payload = new {
                title,
                body
            };
            var result = await PostJsonAsync($"/repos/{owner}/{repo}/issues", payload, allowConflict: false)
                .ConfigureAwait(false);
            if (result is null ||
                !result.Value.TryGetProperty("number", out var numberProp) ||
                numberProp.ValueKind != JsonValueKind.Number ||
                !numberProp.TryGetInt32(out var number) ||
                number <= 0) {
                throw new InvalidOperationException("GitHub issue create response did not include a valid issue number.");
            }

            return number;
        }

        public async Task UpsertIssueCommentWithMarkerAsync(
            string owner,
            string repo,
            int issueNumber,
            string marker,
            string body) {
            if (issueNumber <= 0) {
                throw new ArgumentOutOfRangeException(nameof(issueNumber), issueNumber, "Issue number must be positive.");
            }
            if (string.IsNullOrWhiteSpace(marker)) {
                throw new ArgumentException("Marker is required.", nameof(marker));
            }

            var normalizedBody = string.IsNullOrWhiteSpace(body)
                ? marker
                : body.Contains(marker, StringComparison.Ordinal)
                    ? body
                    : $"{marker}{Environment.NewLine}{body}";

            var existing = await TryFindIssueCommentByMarkerAsync(owner, repo, issueNumber, marker).ConfigureAwait(false);
            if (existing.HasValue) {
                var existingBody = existing.Value.Body ?? string.Empty;
                if (string.Equals(existingBody.Trim(), normalizedBody.Trim(), StringComparison.Ordinal)) {
                    return;
                }

                await PatchJsonAsync(
                    $"/repos/{owner}/{repo}/issues/comments/{existing.Value.Id}",
                    new { body = normalizedBody }).ConfigureAwait(false);
                return;
            }

            await PostJsonAsync(
                $"/repos/{owner}/{repo}/issues/{issueNumber}/comments",
                new { body = normalizedBody },
                allowConflict: false).ConfigureAwait(false);
        }

        public async Task<string?> TryGetRepositoryVariableAsync(string owner, string repo, string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Variable name is required.", nameof(name));
            }

            var escapedName = Uri.EscapeDataString(name);
            using var response = await _http.GetAsync($"/repos/{owner}/{repo}/actions/variables/{escapedName}").ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                return null;
            }
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException(FormatGitHubFailure(response, content));
            }

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("value", out var valueProp) &&
                valueProp.ValueKind == JsonValueKind.String) {
                return valueProp.GetString();
            }

            throw new InvalidOperationException($"Repository variable '{name}' response did not include a value.");
        }

        public async Task UpsertRepositoryVariableAsync(string owner, string repo, string name, string value) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Variable name is required.", nameof(name));
            }

            var payload = new {
                name,
                value
            };
            var escapedName = Uri.EscapeDataString(name);
            var existing = await TryGetRepositoryVariableAsync(owner, repo, name).ConfigureAwait(false);
            if (existing is null) {
                await PostJsonAsync($"/repos/{owner}/{repo}/actions/variables", payload, allowConflict: false)
                    .ConfigureAwait(false);
                return;
            }

            await PatchJsonAsync($"/repos/{owner}/{repo}/actions/variables/{escapedName}", payload).ConfigureAwait(false);
        }

        public async Task<EnsureRepositoryLabelsResult> EnsureRepositoryLabelsAsync(
            string owner,
            string repo,
            IReadOnlyList<ProjectLabelDefinition> labels) {
            if (labels is null) {
                throw new ArgumentNullException(nameof(labels));
            }
            if (labels.Count == 0) {
                return new EnsureRepositoryLabelsResult(0, 0);
            }

            var existing = await GetRepositoryLabelNamesAsync(owner, repo).ConfigureAwait(false);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var createdCount = 0;
            var totalCount = 0;
            foreach (var label in labels) {
                if (string.IsNullOrWhiteSpace(label.Name) || !seen.Add(label.Name)) {
                    continue;
                }

                totalCount++;
                if (existing.Contains(label.Name)) {
                    continue;
                }

                var result = await PostJsonAsync(
                    $"/repos/{owner}/{repo}/labels",
                    new {
                        name = label.Name,
                        color = label.Color,
                        description = label.Description
                    },
                    allowConflict: true).ConfigureAwait(false);
                if (result is not null) {
                    createdCount++;
                    existing.Add(label.Name);
                }
            }

            return new EnsureRepositoryLabelsResult(createdCount, totalCount);
        }

        private async Task<(long Id, string Body)?> TryFindIssueCommentByMarkerAsync(
            string owner,
            string repo,
            int issueNumber,
            string marker) {
            for (var page = 1; page <= 10; page++) {
                var url = $"/repos/{owner}/{repo}/issues/{issueNumber}/comments?per_page=100&page={page}";
                var json = await GetJsonAsync(url).ConfigureAwait(false);
                if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0) {
                    return null;
                }

                var count = 0;
                foreach (var item in json.EnumerateArray()) {
                    count++;
                    if (!item.TryGetProperty("id", out var idProp) ||
                        idProp.ValueKind != JsonValueKind.Number ||
                        !idProp.TryGetInt64(out var commentId) ||
                        commentId <= 0) {
                        continue;
                    }

                    if (!item.TryGetProperty("body", out var bodyProp) || bodyProp.ValueKind != JsonValueKind.String) {
                        continue;
                    }

                    var body = bodyProp.GetString() ?? string.Empty;
                    if (body.Contains(marker, StringComparison.Ordinal)) {
                        return (commentId, body);
                    }
                }

                if (count < 100) {
                    return null;
                }
            }

            return null;
        }

        private async Task<HashSet<string>> GetRepositoryLabelNamesAsync(string owner, string repo) {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var page = 1; page <= 10; page++) {
                var json = await GetJsonAsync($"/repos/{owner}/{repo}/labels?per_page=100&page={page}").ConfigureAwait(false);
                if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0) {
                    return names;
                }

                var count = 0;
                foreach (var item in json.EnumerateArray()) {
                    count++;
                    if (!item.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) {
                        continue;
                    }

                    var name = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) {
                        names.Add(name.Trim());
                    }
                }

                if (count < 100) {
                    return names;
                }
            }

            return names;
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
                throw new InvalidOperationException(FormatGitHubFailure(response, content));
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
                throw new InvalidOperationException(FormatGitHubFailure(response, responseText));
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
                throw new InvalidOperationException(FormatGitHubFailure(response, responseText));
            }
        }

        private async Task PatchJsonAsync(string url, object payload) {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Patch, url) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException(FormatGitHubFailure(response, responseText));
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
                throw new InvalidOperationException(FormatGitHubFailure(response, responseText));
            }
        }

        private static string FormatGitHubFailure(HttpResponseMessage response, string body) {
            var msg = $"GitHub API request failed ({(int)response.StatusCode}): {body}";
            // Helpful when a GitHub App token is missing repository permissions for the endpoint.
            if (response.Headers.TryGetValues("X-Accepted-GitHub-Permissions", out var accepted)) {
                var joined = string.Join(", ", accepted);
                if (!string.IsNullOrWhiteSpace(joined)) {
                    msg += $"{Environment.NewLine}Accepted permissions: {joined}";
                }
            }
            // Helpful when a user OAuth/PAT token is missing scopes.
            if (response.Headers.TryGetValues("X-OAuth-Scopes", out var scopes)) {
                var joined = string.Join(", ", scopes);
                if (!string.IsNullOrWhiteSpace(joined)) {
                    msg += $"{Environment.NewLine}Token scopes: {joined}";
                }
            }
            return msg;
        }
    }
}
