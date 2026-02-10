using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Wizard;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private async Task HandleReposAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<RepoListRequest>(body, _jsonOptions) ?? new RepoListRequest();
        if (string.IsNullOrWhiteSpace(request.Token)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing token" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubRepoClient(request.Token!, apiBaseUrl);
            Exception? userError = null;
            Exception? installError = null;
            List<GitHubRepoClient.RepositoryInfo>? repos = null;
            var source = "user";

            try {
                repos = await client.ListRepositoriesAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                userError = ex;
                try {
                    repos = await client.ListInstallationRepositoriesAsync().ConfigureAwait(false);
                    source = "installation";
                } catch (Exception installEx) {
                    installError = installEx;
                }
            }

            if (repos is null) {
                var message = installError is null
                    ? userError?.Message ?? "Failed to list repositories."
                    : $"User repo list failed: {userError?.Message}. Installation repo list failed: {installError.Message}";
                throw new InvalidOperationException(message);
            }

            await WriteJsonAsync(context, new {
                repos = repos.ConvertAll(r => new {
                    name = r.FullName,
                    updatedAt = r.UpdatedAt,
                    canPush = r.CanPush,
                    canAdmin = r.CanAdmin
                }),
                source
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleRepoStatusAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<RepoStatusRequest>(body, _jsonOptions) ?? new RepoStatusRequest();
        if (string.IsNullOrWhiteSpace(request.Token)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing token" }).ConfigureAwait(false);
            return;
        }
        if (request.Repos is null || request.Repos.Count == 0) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing repos" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        var results = new List<RepoStatusResponse>();
        try {
            using var client = new GitHubRepoClient(request.Token!, apiBaseUrl);
            foreach (var repo in request.Repos) {
                if (!TryParseRepo(repo, out var owner, out var name)) {
                    results.Add(new RepoStatusResponse { Repo = repo, Error = "Invalid repo name (expected owner/name)." });
                    continue;
                }
                try {
                    var defaultBranch = await client.GetDefaultBranchAsync(owner, name).ConfigureAwait(false);
                    var workflow = await client.TryGetFileAsync(owner, name, ".github/workflows/review-intelligencex.yml", defaultBranch)
                        .ConfigureAwait(false);
                    var config = await client.TryGetFileAsync(owner, name, ".intelligencex/reviewer.json", defaultBranch)
                        .ConfigureAwait(false);
                    GitHubRepoClient.RepoFile? legacyConfig = null;
                    if (config is null) {
                        legacyConfig = await client.TryGetFileAsync(owner, name, ".intelligencex/config.json", defaultBranch)
                            .ConfigureAwait(false);
                    }
                    var hasReviewerConfig = config is not null ||
                                           (legacyConfig is not null && LooksLikeReviewerConfig(legacyConfig.Content));

                    var managed = workflow?.Content?.Contains("INTELLIGENCEX:BEGIN", StringComparison.Ordinal) ?? false;
                    results.Add(new RepoStatusResponse {
                        Repo = repo,
                        DefaultBranch = defaultBranch,
                        WorkflowExists = workflow is not null,
                        WorkflowManaged = managed,
                        ConfigExists = hasReviewerConfig
                    });
                } catch (Exception ex) {
                    results.Add(new RepoStatusResponse { Repo = repo, Error = ex.Message });
                }
            }
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context, new { status = results }).ConfigureAwait(false);
    }

    private async Task HandleRepoConfigAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<RepoConfigRequest>(body, _jsonOptions) ?? new RepoConfigRequest();
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Repo)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing token or repo" }).ConfigureAwait(false);
            return;
        }
        if (!TryParseRepo(request.Repo, out var owner, out var name)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid repo name (expected owner/name)." }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubRepoClient(request.Token!, apiBaseUrl);
            var defaultBranch = await client.GetDefaultBranchAsync(owner, name).ConfigureAwait(false);
            var config = await client.TryGetFileAsync(owner, name, ".intelligencex/reviewer.json", defaultBranch)
                .ConfigureAwait(false);
            if (config is null) {
                // Backward compatibility: older setup flows wrote reviewer settings into `.intelligencex/config.json`.
                var legacyConfig = await client.TryGetFileAsync(owner, name, ".intelligencex/config.json", defaultBranch)
                    .ConfigureAwait(false);
                if (legacyConfig is null || !LooksLikeReviewerConfig(legacyConfig.Content)) {
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context, new { error = "Config not found in default branch." }).ConfigureAwait(false);
                    return;
                }
                config = legacyConfig;
            }

            await WriteJsonAsync(context, new {
                config = config.Content,
                branch = defaultBranch
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private static bool LooksLikeReviewerConfig(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            return false;
        }
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }
            if (root.TryGetProperty("review", out var review) && review.ValueKind == JsonValueKind.Object) {
                return true;
            }
            if (root.TryGetProperty("provider", out _) ||
                root.TryGetProperty("model", out _) ||
                root.TryGetProperty("openaiModel", out _)) {
                return true;
            }
            return false;
        } catch {
            return false;
        }
    }

    private async Task HandleRepoWorkflowAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<RepoWorkflowRequest>(body, _jsonOptions) ?? new RepoWorkflowRequest();
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Repo)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing token or repo" }).ConfigureAwait(false);
            return;
        }
        if (!TryParseRepo(request.Repo, out var owner, out var name)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid repo name (expected owner/name)." }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubRepoClient(request.Token!, apiBaseUrl);
            var defaultBranch = await client.GetDefaultBranchAsync(owner, name).ConfigureAwait(false);
            var workflow = await client.TryGetFileAsync(owner, name, ".github/workflows/review-intelligencex.yml", defaultBranch)
                .ConfigureAwait(false);
            if (workflow is null) {
                context.Response.StatusCode = 404;
                await WriteJsonAsync(context, new { error = "Workflow not found in default branch." }).ConfigureAwait(false);
                return;
            }
            var managed = workflow.Content.Contains("INTELLIGENCEX:BEGIN", StringComparison.Ordinal);
            await WriteJsonAsync(context, new {
                workflow = workflow.Content,
                branch = defaultBranch,
                managed
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private static bool TryParseRepo(string repo, out string owner, out string name) {
        owner = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(repo)) {
            return false;
        }
        var parts = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            return false;
        }
        owner = parts[0];
        name = parts[1];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name)) {
            return false;
        }
        if (!RepoSegmentRegex.IsMatch(owner) || !RepoSegmentRegex.IsMatch(name)) {
            return false;
        }
        return true;
    }
}
