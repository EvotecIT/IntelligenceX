using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using Sodium;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private sealed class SetupOptions {
        public string? RepoFullName { get; set; }
        public string? GitHubClientId { get; set; }
        public string? GitHubToken { get; set; }
        public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
        public string GitHubAuthBaseUrl { get; set; } = "https://github.com";
        public string GitHubScopes { get; set; } = "repo workflow read:org";
        public string? ActionsRepo { get; set; } = "evotecit/github-actions";
        public string? ActionsRef { get; set; } = "master";
        public string? RunsOn { get; set; } = "[\"self-hosted\",\"ubuntu\"]";
        public string? Provider { get; set; } = "openai";
        public string? OpenAIModel { get; set; } = "gpt-5.2-codex";
        public string? OpenAITransport { get; set; } = "native";
        public string? ReviewerSource { get; set; } = "release";
        public string? ReviewerReleaseRepo { get; set; } = "EvotecIT/github-actions";
        public string? ReviewerReleaseTag { get; set; } = "latest";
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public string? ReviewProfile { get; set; }
        public string? ReviewMode { get; set; }
        public string? ReviewCommentMode { get; set; }
        public string? ConfigPath { get; set; }
        public string? ConfigJson { get; set; }
        public string? AuthB64 { get; set; }
        public string? AuthB64Path { get; set; }
        public bool CleanupEnabled { get; set; }
        public string? CleanupMode { get; set; } = "comment";
        public string? CleanupScope { get; set; } = "pr";
        public string? CleanupRequireLabel { get; set; } = string.Empty;
        public double CleanupMinConfidence { get; set; } = 0.85;
        public string? CleanupAllowedEdits { get; set; } = "formatting,grammar,title,sections";
        public bool CleanupPostEditComment { get; set; } = true;
        public string? BranchName { get; set; }
        public bool WithConfig { get; set; }
        public bool Upgrade { get; set; }
        public bool Cleanup { get; set; }
        public bool UpdateSecret { get; set; }
        public bool SkipSecret { get; set; }
        public bool ManualSecret { get; set; }
        public bool KeepSecret { get; set; }
        public bool Force { get; set; }
        public bool DryRun { get; set; }
        public bool ShowHelp { get; set; }
        public bool ActionsRepoSet { get; set; }
        public bool ActionsRefSet { get; set; }
        public bool RunsOnSet { get; set; }
        public bool ProviderSet { get; set; }
        public bool OpenAIModelSet { get; set; }
        public bool OpenAITransportSet { get; set; }
        public bool ReviewerSourceSet { get; set; }
        public bool ReviewerReleaseRepoSet { get; set; }
        public bool ReviewerReleaseTagSet { get; set; }
        public bool ReviewerReleaseAssetSet { get; set; }
        public bool ReviewerReleaseUrlSet { get; set; }
        public bool IncludeIssueCommentsSet { get; set; }
        public bool IncludeReviewCommentsSet { get; set; }
        public bool IncludeRelatedPullRequestsSet { get; set; }
        public bool ProgressUpdatesSet { get; set; }
        public bool ReviewProfileSet { get; set; }
        public bool ReviewModeSet { get; set; }
        public bool ReviewCommentModeSet { get; set; }
        public bool CleanupEnabledSet { get; set; }
        public bool CleanupModeSet { get; set; }
        public bool CleanupScopeSet { get; set; }
        public bool CleanupRequireLabelSet { get; set; }
        public bool CleanupMinConfidenceSet { get; set; }
        public bool CleanupAllowedEditsSet { get; set; }
        public bool CleanupPostEditCommentSet { get; set; }
        public bool ExplicitSecrets { get; set; }

        public static SetupOptions Parse(string[] args) {
            var options = new SetupOptions {
                GitHubClientId = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_CLIENT_ID"),
                GitHubToken = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN"),
                GitHubApiBaseUrl = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_API_BASE_URL") ?? "https://api.github.com",
                GitHubAuthBaseUrl = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_AUTH_BASE_URL") ?? "https://github.com"
            };

            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                    continue;
                }
                var key = arg.Substring(2);
                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";

                switch (key) {
                    case "repo":
                        options.RepoFullName = value;
                        break;
                    case "github-client-id":
                        options.GitHubClientId = value;
                        break;
                    case "github-token":
                        options.GitHubToken = value;
                        break;
                    case "github-api-base-url":
                        options.GitHubApiBaseUrl = value;
                        break;
                    case "github-auth-base-url":
                        options.GitHubAuthBaseUrl = value;
                        break;
                    case "actions-repo":
                        options.ActionsRepo = value;
                        options.ActionsRepoSet = true;
                        break;
                    case "actions-ref":
                        options.ActionsRef = value;
                        options.ActionsRefSet = true;
                        break;
                    case "runs-on":
                        options.RunsOn = value;
                        options.RunsOnSet = true;
                        break;
                    case "provider":
                        options.Provider = value;
                        options.ProviderSet = true;
                        break;
                    case "openai-model":
                        options.OpenAIModel = value;
                        options.OpenAIModelSet = true;
                        break;
                    case "openai-transport":
                        options.OpenAITransport = value;
                        options.OpenAITransportSet = true;
                        break;
                    case "reviewer-source":
                        options.ReviewerSource = value;
                        options.ReviewerSourceSet = true;
                        break;
                    case "reviewer-release-repo":
                        options.ReviewerReleaseRepo = value;
                        options.ReviewerReleaseRepoSet = true;
                        break;
                    case "reviewer-release-tag":
                        options.ReviewerReleaseTag = value;
                        options.ReviewerReleaseTagSet = true;
                        break;
                    case "reviewer-release-asset":
                        options.ReviewerReleaseAsset = value;
                        options.ReviewerReleaseAssetSet = true;
                        break;
                    case "reviewer-release-url":
                        options.ReviewerReleaseUrl = value;
                        options.ReviewerReleaseUrlSet = true;
                        break;
                    case "include-issue-comments":
                        options.IncludeIssueComments = ParseBool(value, options.IncludeIssueComments);
                        options.IncludeIssueCommentsSet = true;
                        break;
                    case "include-review-comments":
                        options.IncludeReviewComments = ParseBool(value, options.IncludeReviewComments);
                        options.IncludeReviewCommentsSet = true;
                        break;
                    case "include-related-prs":
                        options.IncludeRelatedPullRequests = ParseBool(value, options.IncludeRelatedPullRequests);
                        options.IncludeRelatedPullRequestsSet = true;
                        break;
                    case "progress-updates":
                        options.ProgressUpdates = ParseBool(value, options.ProgressUpdates);
                        options.ProgressUpdatesSet = true;
                        break;
                    case "review-profile":
                        options.ReviewProfile = value;
                        options.ReviewProfileSet = true;
                        break;
                    case "review-mode":
                        options.ReviewMode = value;
                        options.ReviewModeSet = true;
                        break;
                    case "review-comment-mode":
                        options.ReviewCommentMode = value;
                        options.ReviewCommentModeSet = true;
                        break;
                    case "config-path":
                        options.ConfigPath = value;
                        options.WithConfig = true;
                        break;
                    case "config-json":
                        options.ConfigJson = value;
                        options.WithConfig = true;
                        break;
                    case "auth-b64":
                        options.AuthB64 = value;
                        break;
                    case "auth-b64-path":
                        options.AuthB64Path = value;
                        break;
                    case "cleanup-enabled":
                        options.CleanupEnabled = ParseBool(value, options.CleanupEnabled);
                        options.CleanupEnabledSet = true;
                        break;
                    case "cleanup-mode":
                        options.CleanupMode = value;
                        options.CleanupModeSet = true;
                        break;
                    case "cleanup-scope":
                        options.CleanupScope = value;
                        options.CleanupScopeSet = true;
                        break;
                    case "cleanup-require-label":
                        options.CleanupRequireLabel = value;
                        options.CleanupRequireLabelSet = true;
                        break;
                    case "cleanup-min-confidence":
                        options.CleanupMinConfidence = ParseDouble(value, options.CleanupMinConfidence);
                        options.CleanupMinConfidenceSet = true;
                        break;
                    case "cleanup-allowed-edits":
                        options.CleanupAllowedEdits = value;
                        options.CleanupAllowedEditsSet = true;
                        break;
                    case "cleanup-post-edit-comment":
                        options.CleanupPostEditComment = ParseBool(value, options.CleanupPostEditComment);
                        options.CleanupPostEditCommentSet = true;
                        break;
                    case "with-config":
                        options.WithConfig = ParseBool(value, true);
                        break;
                    case "upgrade":
                        options.Upgrade = ParseBool(value, true);
                        break;
                    case "cleanup":
                        options.Cleanup = ParseBool(value, true);
                        break;
                    case "update-secret":
                        options.UpdateSecret = ParseBool(value, true);
                        break;
                    case "skip-secret":
                        options.SkipSecret = ParseBool(value, true);
                        break;
                    case "manual-secret":
                        options.ManualSecret = ParseBool(value, true);
                        break;
                    case "keep-secret":
                        options.KeepSecret = ParseBool(value, true);
                        break;
                    case "branch":
                        options.BranchName = value;
                        break;
                    case "force":
                        options.Force = ParseBool(value, true);
                        break;
                    case "dry-run":
                        options.DryRun = ParseBool(value, true);
                        break;
                    case "explicit-secrets":
                        options.ExplicitSecrets = ParseBool(value, true);
                        break;
                    case "help":
                        options.ShowHelp = true;
                        break;
                }
            }

            return options;
        }
    }

    private sealed class FilePlan {
        public FilePlan(string path, string action, string? content, string? reason = null) {
            Path = path;
            Action = action;
            Content = content;
            Reason = reason;
        }

        public string Path { get; }
        public string Action { get; }
        public string? Content { get; }
        public string? Reason { get; }
        public bool IsWrite => Action is "create" or "update" or "overwrite";

        public static FilePlan Skip(string path, string? reason = null) => new(path, "skip", null, reason);
    }

    private sealed class RepoFile {
        public RepoFile(string sha, string content) {
            Sha = sha;
            Content = content;
        }

        public string Sha { get; }
        public string Content { get; }
    }

    private sealed class WorkflowSettings {
        public string ActionsRepo { get; set; } = "evotecit/github-actions";
        public string ActionsRef { get; set; } = "master";
        public string RunsOn { get; set; } = "[\"self-hosted\",\"ubuntu\"]";
        public string ReviewerSource { get; set; } = "release";
        public string ReviewerReleaseRepo { get; set; } = "EvotecIT/github-actions";
        public string ReviewerReleaseTag { get; set; } = "latest";
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public string Provider { get; set; } = "openai";
        public string Model { get; set; } = "gpt-5.2-codex";
        public string OpenAITransport { get; set; } = "native";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public bool ExplicitSecrets { get; set; }
        public bool CleanupEnabled { get; set; }
        public string CleanupMode { get; set; } = "comment";
        public string CleanupScope { get; set; } = "pr";
        public string? CleanupRequireLabel { get; set; } = string.Empty;
        public double CleanupMinConfidence { get; set; } = 0.85;
        public string CleanupAllowedEdits { get; set; } = "formatting,grammar,title,sections";
        public bool CleanupPostEditComment { get; set; } = true;

        public static WorkflowSettings FromOptions(SetupOptions options) {
            return new WorkflowSettings {
                ActionsRepo = NormalizeActionsRepo(options.ActionsRepo ?? "evotecit/github-actions"),
                ActionsRef = options.ActionsRef ?? "master",
                RunsOn = options.RunsOn ?? "[\"self-hosted\",\"ubuntu\"]",
                ReviewerSource = options.ReviewerSource ?? "release",
                ReviewerReleaseRepo = options.ReviewerReleaseRepo ?? "EvotecIT/github-actions",
                ReviewerReleaseTag = options.ReviewerReleaseTag ?? "latest",
                ReviewerReleaseAsset = options.ReviewerReleaseAsset,
                ReviewerReleaseUrl = options.ReviewerReleaseUrl,
                Provider = options.Provider ?? "openai",
                Model = options.OpenAIModel ?? "gpt-5.2-codex",
                OpenAITransport = options.OpenAITransport ?? "native",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates,
                ExplicitSecrets = options.ExplicitSecrets,
                CleanupEnabled = options.CleanupEnabled,
                CleanupMode = options.CleanupMode ?? "comment",
                CleanupScope = options.CleanupScope ?? "pr",
                CleanupRequireLabel = options.CleanupRequireLabel ?? string.Empty,
                CleanupMinConfidence = options.CleanupMinConfidence,
                CleanupAllowedEdits = options.CleanupAllowedEdits ?? "formatting,grammar,title,sections",
                CleanupPostEditComment = options.CleanupPostEditComment
            };
        }
    }

    private sealed class ConfigSettings {
        public string Provider { get; set; } = "openai";
        public string OpenAITransport { get; set; } = "native";
        public string OpenAIModel { get; set; } = "gpt-5.2-codex";
        public string Profile { get; set; } = "balanced";
        public string Mode { get; set; } = "hybrid";
        public string CommentMode { get; set; } = "sticky";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;

        public static ConfigSettings FromOptions(SetupOptions options) {
            return new ConfigSettings {
                Provider = options.Provider ?? "openai",
                OpenAITransport = options.OpenAITransport ?? "native",
                OpenAIModel = options.OpenAIModel ?? "gpt-5.2-codex",
                Profile = options.ReviewProfile ?? "balanced",
                Mode = options.ReviewMode ?? "hybrid",
                CommentMode = options.ReviewCommentMode ?? "sticky",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates
            };
        }
    }

    private sealed class WorkflowSnapshot {
        public string? ActionsRepo { get; set; }
        public string? ActionsRef { get; set; }
        public string? RunsOn { get; set; }
        public string? ReviewerSource { get; set; }
        public string? ReviewerReleaseRepo { get; set; }
        public string? ReviewerReleaseTag { get; set; }
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string? OpenAITransport { get; set; }
        public bool? IncludeIssueComments { get; set; }
        public bool? IncludeReviewComments { get; set; }
        public bool? IncludeRelatedPullRequests { get; set; }
        public bool? ProgressUpdates { get; set; }
        public bool? CleanupEnabled { get; set; }
        public string? CleanupMode { get; set; }
        public string? CleanupScope { get; set; }
        public string? CleanupRequireLabel { get; set; }
        public double? CleanupMinConfidence { get; set; }
        public string? CleanupAllowedEdits { get; set; }
        public bool? CleanupPostEditComment { get; set; }

        public bool HasAny =>
            ActionsRepo is not null ||
            ActionsRef is not null ||
            RunsOn is not null ||
            ReviewerSource is not null ||
            ReviewerReleaseRepo is not null ||
            ReviewerReleaseTag is not null ||
            ReviewerReleaseAsset is not null ||
            ReviewerReleaseUrl is not null ||
            Provider is not null ||
            Model is not null ||
            OpenAITransport is not null ||
            IncludeIssueComments.HasValue ||
            IncludeReviewComments.HasValue ||
            IncludeRelatedPullRequests.HasValue ||
            ProgressUpdates.HasValue ||
            CleanupEnabled.HasValue ||
            CleanupMode is not null ||
            CleanupScope is not null ||
            CleanupRequireLabel is not null ||
            CleanupMinConfidence.HasValue ||
            CleanupAllowedEdits is not null ||
            CleanupPostEditComment.HasValue;
    }

    private sealed class ConfigSnapshot {
        public string? Provider { get; set; }
        public string? OpenAITransport { get; set; }
        public string? OpenAIModel { get; set; }
        public string? Profile { get; set; }
        public string? Mode { get; set; }
        public string? CommentMode { get; set; }
        public bool? IncludeIssueComments { get; set; }
        public bool? IncludeReviewComments { get; set; }
        public bool? IncludeRelatedPullRequests { get; set; }
        public bool? ProgressUpdates { get; set; }

        public bool HasAny =>
            Provider is not null ||
            OpenAITransport is not null ||
            OpenAIModel is not null ||
            Profile is not null ||
            Mode is not null ||
            CommentMode is not null ||
            IncludeIssueComments.HasValue ||
            IncludeReviewComments.HasValue ||
            IncludeRelatedPullRequests.HasValue ||
            ProgressUpdates.HasValue;
    }

    private sealed class SetupState {
        public SetupState(SetupOptions options) {
            Options = options;
        }

        public SetupOptions Options { get; }
        public GitHubState GitHub { get; } = new();
        public OpenAiState OpenAI { get; } = new();
        public CopilotState Copilot { get; } = new();
        public Dictionary<string, object?> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GitHubState {
        public string? ClientId { get; set; }
        public string? Token { get; set; }
        public string? RepositoryFullName { get; set; }
        public string? Owner { get; set; }
        public string? Repo { get; set; }
        public List<RepositoryInfo> Repositories { get; set; } = new();
    }

    private sealed class OpenAiState {
        public AuthBundle? AuthBundle { get; set; }
        public string? AuthJson { get; set; }
        public string? AuthB64 { get; set; }
    }

    private sealed class CopilotState {
        public string? Status { get; set; }
    }

    private sealed class RepositoryInfo {
        public RepositoryInfo(string fullName, bool isPrivate, DateTimeOffset? updatedAt) {
            FullName = fullName;
            Private = isPrivate;
            UpdatedAt = updatedAt;
        }

        public string FullName { get; }
        public bool Private { get; }
        public DateTimeOffset? UpdatedAt { get; }
    }

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
