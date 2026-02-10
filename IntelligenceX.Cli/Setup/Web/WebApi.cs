using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Wizard;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed class WebApi {
    private static readonly Regex RepoSegmentRegex = new("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,98}[A-Za-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(System.Net.HttpListenerContext context) {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var normalizedPath = path.Length > 1 ? path.TrimEnd('/') : path;
        if (path.StartsWith("/api/repos", StringComparison.OrdinalIgnoreCase)) {
            await HandleReposAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/repo-status", StringComparison.OrdinalIgnoreCase)) {
            await HandleRepoStatusAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/repo-config", StringComparison.OrdinalIgnoreCase)) {
            await HandleRepoConfigAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/repo-workflow", StringComparison.OrdinalIgnoreCase)) {
            await HandleRepoWorkflowAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/device-code", StringComparison.OrdinalIgnoreCase)) {
            await HandleDeviceCodeAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/device-poll", StringComparison.OrdinalIgnoreCase)) {
            await HandleDevicePollAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/app-manifest", StringComparison.OrdinalIgnoreCase)) {
            await HandleAppManifestAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/app-installations", StringComparison.OrdinalIgnoreCase)) {
            await HandleAppInstallationsAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/app-token", StringComparison.OrdinalIgnoreCase)) {
            await HandleAppTokenAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/setup/plan", StringComparison.OrdinalIgnoreCase)) {
            await HandleSetupAsync(context, dryRun: true).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/setup/apply", StringComparison.OrdinalIgnoreCase)) {
            await HandleSetupAsync(context, dryRun: false).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/usage-cache", StringComparison.OrdinalIgnoreCase)) {
            await HandleUsageCacheAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/usage", StringComparison.OrdinalIgnoreCase)) {
            await HandleUsageAsync(context).ConfigureAwait(false);
            return;
        }
        if (normalizedPath.Equals("/api/openai-login", StringComparison.OrdinalIgnoreCase)) {
            await HandleOpenAILoginAsync(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = 404;
        await WriteJsonAsync(context, new { error = "Not found" }).ConfigureAwait(false);
    }

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

    private async Task HandleDeviceCodeAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<DeviceCodeRequest>(body, _jsonOptions) ?? new DeviceCodeRequest();
        var effectiveClientId = request.GetEffectiveClientId();
        if (string.IsNullOrWhiteSpace(effectiveClientId)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing clientId" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetAuthBaseUrl(request.AuthBaseUrl, out var authBaseUrl, out var authError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = authError }).ConfigureAwait(false);
            return;
        }

        try {
            var result = await GitHubDeviceFlowClient.RequestCodeAsync(effectiveClientId, authBaseUrl, request.Scopes)
                .ConfigureAwait(false);
            await WriteJsonAsync(context, result).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleDevicePollAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<DevicePollRequest>(body, _jsonOptions) ?? new DevicePollRequest();
        var effectiveClientId = request.GetEffectiveClientId();
        if (string.IsNullOrWhiteSpace(effectiveClientId) || string.IsNullOrWhiteSpace(request.DeviceCode)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing clientId or deviceCode" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetAuthBaseUrl(request.AuthBaseUrl, out var authBaseUrl, out var authError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = authError }).ConfigureAwait(false);
            return;
        }

        try {
            var token = await GitHubDeviceFlowClient.PollTokenAsync(effectiveClientId, request.DeviceCode!, authBaseUrl, request.IntervalSeconds, request.ExpiresIn)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token)) {
                context.Response.StatusCode = 408;
                await WriteJsonAsync(context, new { error = "Device flow expired. Please start again." }).ConfigureAwait(false);
                return;
            }
            await WriteJsonAsync(context, new { token }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleAppManifestAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<AppManifestRequest>(body, _jsonOptions) ?? new AppManifestRequest();
        if (string.IsNullOrWhiteSpace(request.AppName)) {
            request.AppName = "IntelligenceX Reviewer";
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            var result = await GitHubAppManifestFlow.RunAsync(new GitHubAppManifestOptions {
                AppName = request.AppName!,
                Owner = request.Owner,
                AuthBaseUrl = ResolveAuthBaseUrl(request.AuthBaseUrl),
                ApiBaseUrl = apiBaseUrl
            }, CancellationToken.None).ConfigureAwait(false);

            if (result is null) {
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context, new { error = "Manifest flow failed." }).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(context, new {
                appId = result.AppId,
                pem = result.Pem
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleAppInstallationsAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<AppInstallationRequest>(body, _jsonOptions) ?? new AppInstallationRequest();
        if (request.AppId <= 0 || string.IsNullOrWhiteSpace(request.Pem)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing appId or pem" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubAppClient(request.AppId, request.Pem!, apiBaseUrl);
            var installs = await client.ListInstallationsAsync().ConfigureAwait(false);
            await WriteJsonAsync(context, new {
                installations = installs.ConvertAll(i => new { id = i.Id, login = i.AccountLogin })
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleAppTokenAsync(System.Net.HttpListenerContext context) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<AppTokenRequest>(body, _jsonOptions) ?? new AppTokenRequest();
        if (request.AppId <= 0 || string.IsNullOrWhiteSpace(request.Pem) || request.InstallationId <= 0) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing appId, pem, or installationId" }).ConfigureAwait(false);
            return;
        }
        if (!TryGetApiBaseUrl(request.ApiBaseUrl, out var apiBaseUrl, out var apiError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = apiError }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubAppClient(request.AppId, request.Pem!, apiBaseUrl);
            var token = await client.CreateInstallationTokenAsync(request.InstallationId).ConfigureAwait(false);
            await WriteJsonAsync(context, new { token }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleSetupAsync(System.Net.HttpListenerContext context, bool dryRun) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        var request = JsonSerializer.Deserialize<SetupRequest>(body, _jsonOptions) ?? new SetupRequest();
        if ((request.Repos is null || request.Repos.Count == 0) && string.IsNullOrWhiteSpace(request.Repo)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing repo(s)" }).ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrWhiteSpace(request.GitHubToken) && string.IsNullOrWhiteSpace(request.GitHubClientId)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing repo or GitHub auth" }).ConfigureAwait(false);
            return;
        }
        if (!string.IsNullOrWhiteSpace(request.ConfigJson) && !string.IsNullOrWhiteSpace(request.ConfigPath)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Choose only one of configJson or configPath." }).ConfigureAwait(false);
            return;
        }
        if (!string.IsNullOrWhiteSpace(request.AuthB64) && !string.IsNullOrWhiteSpace(request.AuthB64Path)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Choose only one of authB64 or authB64Path." }).ConfigureAwait(false);
            return;
        }

        var hasAuthBundle = !string.IsNullOrWhiteSpace(request.AuthB64) || !string.IsNullOrWhiteSpace(request.AuthB64Path);
        if (!request.SkipSecret && !request.Cleanup && !request.UpdateSecret && !hasAuthBundle) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new {
                error = "Missing OpenAI auth bundle. Click 'Sign in with ChatGPT', provide authB64/authB64Path, or set skipSecret=true."
            }).ConfigureAwait(false);
            return;
        }

        if (request.UpdateSecret && !hasAuthBundle) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Update-secret requires authB64/authB64Path in the web UI." }).ConfigureAwait(false);
            return;
        }

        // Analysis flags are only honored when the setup flow is generating/merging reviewer.json.
        // If the user provides a full config override (configJson/configPath), these flags would be ignored by SetupRunner,
        // so reject them here to avoid misleading behavior and hard-to-debug no-op requests.
        var isSetup = !request.Cleanup && !request.UpdateSecret;
        var hasConfigOverride = !string.IsNullOrWhiteSpace(request.ConfigJson) || !string.IsNullOrWhiteSpace(request.ConfigPath);
        if (!WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: isSetup,
            withConfig: request.WithConfig,
            hasConfigOverride: hasConfigOverride,
            analysisEnabled: request.AnalysisEnabled,
            analysisGateEnabled: request.AnalysisGateEnabled,
            analysisPacks: request.AnalysisPacks,
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGateEnabled,
            normalizedPacks: out var normalizedPacks,
            error: out var analysisError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = analysisError }).ConfigureAwait(false);
            return;
        }
        request.AnalysisEnabled = normalizedEnabled;
        request.AnalysisGateEnabled = normalizedGateEnabled;
        request.AnalysisPacks = normalizedPacks;

        var repos = request.Repos is not null && request.Repos.Count > 0
            ? request.Repos
            : new List<string> { request.Repo! };

        var outputs = new List<SetupResponse>();
        foreach (var repo in repos) {
            var args = BuildSetupArgs(request, dryRun, repo);
            var result = await RunSetupAsync(args).ConfigureAwait(false);
            result.Repo = repo;
            outputs.Add(result);
        }

        await WriteJsonAsync(context, new {
            results = outputs
        }).ConfigureAwait(false);
    }

    private async Task HandleUsageAsync(System.Net.HttpListenerContext context) {
        if (!await RequirePostJsonAsync(context).ConfigureAwait(false)) {
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        UsageRequest request;
        try {
            request = JsonSerializer.Deserialize<UsageRequest>(body, _jsonOptions) ?? new UsageRequest();
        } catch (JsonException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid JSON payload." }).ConfigureAwait(false);
            return;
        }
        TempFile? tempFile = null;
        try {
            var authPath = request.AuthB64Path;
            if (string.IsNullOrWhiteSpace(authPath) && string.IsNullOrWhiteSpace(request.AuthB64)) {
                authPath = AuthPaths.ResolveAuthPath();
            }
            if (!string.IsNullOrWhiteSpace(request.AuthB64)) {
                var raw = Convert.FromBase64String(request.AuthB64);
                var content = Encoding.UTF8.GetString(raw);
                var tempPath = Path.Combine(Path.GetTempPath(), $"intelligencex-auth-{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(tempPath, content).ConfigureAwait(false);
                TryHardenTempFile(tempPath);
                tempFile = new TempFile(tempPath);
                authPath = tempPath;
            }
            if (string.IsNullOrWhiteSpace(authPath) || !File.Exists(authPath)) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "Auth bundle path not found." }).ConfigureAwait(false);
                return;
            }

            var options = new OpenAINativeOptions {
                AuthStore = new FileAuthBundleStore(authPath, request.AuthKey)
            };
            if (!string.IsNullOrWhiteSpace(request.ChatGptApiBaseUrl)) {
                if (!TryGetChatGptApiBaseUrl(request.ChatGptApiBaseUrl, out var chatGptBaseUrl, out var chatGptError)) {
                    context.Response.StatusCode = 400;
                    await WriteJsonAsync(context, new { error = chatGptError }).ConfigureAwait(false);
                    return;
                }
                options.ChatGptApiBaseUrl = chatGptBaseUrl;
            }

            using var service = new ChatGptUsageService(options);
            var report = await service.GetReportAsync(request.IncludeEvents, CancellationToken.None).ConfigureAwait(false);
            TrySaveCache(report.Snapshot);
            var response = BuildUsageResponse(report, DateTimeOffset.UtcNow);
            await WriteJsonAsync(context, response).ConfigureAwait(false);
        } catch (FormatException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid base64 auth bundle." }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        } finally {
            tempFile?.Dispose();
        }
    }

    private async Task HandleUsageCacheAsync(System.Net.HttpListenerContext context) {
        try {
            if (!ChatGptUsageCache.TryLoad(out var entry) || entry is null) {
                await WriteJsonAsync(context, new UsageResponse()).ConfigureAwait(false);
                return;
            }
            var response = new UsageResponse {
                Usage = UsageSnapshot.From(entry.Snapshot),
                Events = new List<UsageEvent>(),
                UpdatedAt = entry.UpdatedAt.ToUniversalTime().ToString("u")
            };
            await WriteJsonAsync(context, response).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleOpenAILoginAsync(System.Net.HttpListenerContext context) {
        if (!await RequirePostJsonAsync(context).ConfigureAwait(false)) {
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        OpenAILoginRequest request;
        try {
            request = string.IsNullOrWhiteSpace(body)
                ? new OpenAILoginRequest()
                : JsonSerializer.Deserialize<OpenAILoginRequest>(body, _jsonOptions) ?? new OpenAILoginRequest();
        } catch (JsonException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid JSON payload." }).ConfigureAwait(false);
            return;
        }

        try {
            var config = OAuthConfig.FromEnvironment();
            if (!string.IsNullOrWhiteSpace(request.ClientId)) {
                config.ClientId = request.ClientId!;
            }
            if (request.RedirectPort < 0 || request.RedirectPort > 65535) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "RedirectPort must be 0 (default) or between 1 and 65535." }).ConfigureAwait(false);
                return;
            }
            if (request.TimeoutSeconds < 0) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "TimeoutSeconds must be 0 (default) or a positive integer." }).ConfigureAwait(false);
                return;
            }
            if (request.RedirectPort > 0) {
                config.RedirectPort = request.RedirectPort;
            }

            var service = new OAuthLoginService();
            var options = new OAuthLoginOptions(config) {
                UseLocalListener = true,
                Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 180),
                OnAuthUrl = url => {
                    OpenBrowser(url);
                    return Task.CompletedTask;
                }
            };

            var result = await service.LoginAsync(options).ConfigureAwait(false);
            var json = AuthBundleSerializer.Serialize(result.Bundle);
            var authB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            await WriteJsonAsync(context, new {
                authB64,
                accountId = result.Bundle.AccountId,
                expiresAt = result.Bundle.ExpiresAt?.ToUniversalTime().ToString("u")
            }).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            context.Response.StatusCode = 408;
            await WriteJsonAsync(context, new { error = "Login timed out. Please try again." }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private static void OpenBrowser(string url) {
        if (!TryNormalizeHttpUrl(url, out var safeUrl)) {
            return;
        }
        try {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                Process.Start(new ProcessStartInfo(safeUrl) { UseShellExecute = true });
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                Process.Start("open", safeUrl);
            } else {
                Process.Start("xdg-open", safeUrl);
            }
        } catch (Exception ex) {
            Trace.TraceWarning($"Failed to open browser: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static UsageResponse BuildUsageResponse(ChatGptUsageReport report, DateTimeOffset updatedAt) {
        return new UsageResponse {
            Usage = UsageSnapshot.From(report.Snapshot),
            Events = UsageEvent.From(report.Events),
            UpdatedAt = updatedAt.ToUniversalTime().ToString("u")
        };
    }

    private static void TrySaveCache(ChatGptUsageSnapshot snapshot) {
        try {
            ChatGptUsageCache.Save(snapshot);
        } catch (Exception ex) {
            Trace.TraceWarning($"Failed to save usage cache: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string[] BuildSetupArgs(SetupRequest request, bool dryRun, string repo) {
        var args = new List<string> {
            "--repo", repo
        };
        if (!string.IsNullOrWhiteSpace(request.GitHubToken)) {
            args.Add("--github-token");
            args.Add(request.GitHubToken!);
        } else if (!string.IsNullOrWhiteSpace(request.GitHubClientId)) {
            args.Add("--github-client-id");
            args.Add(request.GitHubClientId!);
        }
        var withConfig = request.WithConfig ||
                         !string.IsNullOrWhiteSpace(request.ConfigJson) ||
                         !string.IsNullOrWhiteSpace(request.ConfigPath);
        var hasConfigOverride = !string.IsNullOrWhiteSpace(request.ConfigJson) || !string.IsNullOrWhiteSpace(request.ConfigPath);
        var isSetup = !request.Cleanup && !request.UpdateSecret;
        var analysisApplies = isSetup && request.WithConfig && !hasConfigOverride;
        if (withConfig) {
            args.Add("--with-config");
        }
        if (!string.IsNullOrWhiteSpace(request.AuthB64)) {
            args.Add("--auth-b64");
            args.Add(request.AuthB64!);
        }
        if (!string.IsNullOrWhiteSpace(request.AuthB64Path)) {
            args.Add("--auth-b64-path");
            args.Add(request.AuthB64Path!);
        }
        if (!string.IsNullOrWhiteSpace(request.ConfigJson)) {
            args.Add("--config-json");
            args.Add(request.ConfigJson!);
        }
        if (!string.IsNullOrWhiteSpace(request.ConfigPath)) {
            args.Add("--config-path");
            args.Add(request.ConfigPath!);
        }
        if (!string.IsNullOrWhiteSpace(request.Provider)) {
            args.Add("--provider");
            args.Add(request.Provider!);
        }
        if (!string.IsNullOrWhiteSpace(request.ReviewProfile)) {
            args.Add("--review-profile");
            args.Add(request.ReviewProfile!);
        }
        if (!string.IsNullOrWhiteSpace(request.ReviewMode)) {
            args.Add("--review-mode");
            args.Add(request.ReviewMode!);
        }
        if (!string.IsNullOrWhiteSpace(request.ReviewCommentMode)) {
            args.Add("--review-comment-mode");
            args.Add(request.ReviewCommentMode!);
        }
        if (analysisApplies && request.AnalysisEnabled.HasValue) {
            args.Add("--analysis-enabled");
            args.Add(request.AnalysisEnabled.Value ? "true" : "false");
        }
        if (analysisApplies && request.AnalysisEnabled == true && request.AnalysisGateEnabled.HasValue) {
            args.Add("--analysis-gate");
            args.Add(request.AnalysisGateEnabled.Value ? "true" : "false");
        }
        if (analysisApplies && request.AnalysisEnabled == true && !string.IsNullOrWhiteSpace(request.AnalysisPacks)) {
            args.Add("--analysis-packs");
            args.Add(request.AnalysisPacks!);
        }
        if (request.SkipSecret) {
            args.Add("--skip-secret");
        }
        if (request.ManualSecret && !request.UpdateSecret) {
            args.Add("--manual-secret");
        }
        if (request.ExplicitSecrets) {
            args.Add("--explicit-secrets");
        }
        if (request.Upgrade) {
            args.Add("--upgrade");
        }
        if (request.Force) {
            args.Add("--force");
        }
        if (request.UpdateSecret) {
            args.Add("--update-secret");
        }
        if (request.Cleanup) {
            args.Add("--cleanup");
        }
        if (request.KeepSecret) {
            args.Add("--keep-secret");
        }
        if (dryRun || request.DryRun) {
            args.Add("--dry-run");
        }
        if (!string.IsNullOrWhiteSpace(request.BranchName)) {
            args.Add("--branch");
            args.Add(request.BranchName!);
        }
        return args.ToArray();
    }

    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    private async Task<SetupResponse> RunSetupAsync(string[] args) {
        if (!await ConsoleLock.WaitAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false)) {
            return new SetupResponse {
                ExitCode = 1,
                Error = "Setup is busy. Please retry in a moment."
            };
        }
        try {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            try {
                Console.SetOut(output);
                Console.SetError(error);
                var code = await SetupRunner.RunAsync(args).ConfigureAwait(false);
                return new SetupResponse {
                    ExitCode = code,
                    Output = output.ToString(),
                    Error = error.ToString()
                };
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        } finally {
            ConsoleLock.Release();
        }
    }

    private async Task<string> ReadBodyAsync(System.Net.HttpListenerContext context) {
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task WriteJsonAsync(System.Net.HttpListenerContext context, object payload) {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        context.Response.Close();
    }

    private async Task<string?> ReadJsonBodyAsync(System.Net.HttpListenerContext context) {
        if (!await RequirePostJsonAsync(context).ConfigureAwait(false)) {
            return null;
        }
        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Request body required." }).ConfigureAwait(false);
            return null;
        }
        return body;
    }

    private async Task<bool> RequirePostJsonAsync(System.Net.HttpListenerContext context) {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return false;
        }
        if (!IsJsonContentType(context.Request.ContentType)) {
            context.Response.StatusCode = 415;
            await WriteJsonAsync(context, new { error = "Content-Type must be application/json." }).ConfigureAwait(false);
            return false;
        }
        if (!context.Request.HasEntityBody) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Request body required." }).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    private static bool IsJsonContentType(string? contentType) {
        if (string.IsNullOrWhiteSpace(contentType)) {
            return false;
        }
        var type = contentType.Split(';', 2)[0].Trim();
        if (type.Equals("application/json", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return type.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetApiBaseUrl(string? requested, out string apiBaseUrl, out string error) {
        apiBaseUrl = "https://api.github.com";
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(requested)) {
            return true;
        }
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var uri)) {
            error = "ApiBaseUrl must be a valid absolute URL.";
            return false;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        error = "ApiBaseUrl must use https (http allowed only for localhost).";
        return false;
    }

    private static bool TryGetAuthBaseUrl(string? requested, out string authBaseUrl, out string error) {
        authBaseUrl = "https://github.com";
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(requested)) {
            return true;
        }
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var uri)) {
            error = "AuthBaseUrl must be a valid absolute URL.";
            return false;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            authBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback) {
            authBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        error = "AuthBaseUrl must use https (http allowed only for localhost).";
        return false;
    }

    private static string ResolveAuthBaseUrl(string? requested) {
        return TryGetAuthBaseUrl(requested, out var resolved, out _)
            ? resolved
            : "https://github.com";
    }

    private static bool TryGetChatGptApiBaseUrl(string? requested, out string apiBaseUrl, out string error) {
        apiBaseUrl = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(requested)) {
            return true;
        }
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var uri)) {
            error = "ChatGptApiBaseUrl must be a valid absolute URL.";
            return false;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        error = "ChatGptApiBaseUrl must use https (http allowed only for localhost).";
        return false;
    }

    private static bool TryNormalizeHttpUrl(string url, out string normalized) {
        normalized = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            return false;
        }
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        normalized = uri.ToString();
        return true;
    }

    private sealed class RepoListRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class RepoStatusRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
        public List<string>? Repos { get; set; }
    }

    private sealed class RepoConfigRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
        public string? Repo { get; set; }
    }

    private sealed class RepoWorkflowRequest {
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
        public string? Repo { get; set; }
    }

    private sealed class DeviceCodeRequest {
        public string? ClientId { get; set; }
        public string? AuthBaseUrl { get; set; } = "https://github.com";
        public string? Scopes { get; set; } = IntelligenceXDefaults.GitHubScopes;

        public string GetEffectiveClientId() {
            if (!string.IsNullOrWhiteSpace(ClientId)) {
                return ClientId!;
            }
            return IntelligenceXDefaults.GetEffectiveGitHubClientId();
        }
    }

    private sealed class DevicePollRequest {
        public string? ClientId { get; set; }
        public string? DeviceCode { get; set; }
        public string? AuthBaseUrl { get; set; } = "https://github.com";
        public int IntervalSeconds { get; set; } = 5;
        public int ExpiresIn { get; set; }

        public string GetEffectiveClientId() {
            if (!string.IsNullOrWhiteSpace(ClientId)) {
                return ClientId!;
            }
            return IntelligenceXDefaults.GetEffectiveGitHubClientId();
        }
    }

    private sealed class AppManifestRequest {
        public string? AppName { get; set; }
        public string? Owner { get; set; }
        public string? AuthBaseUrl { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class AppInstallationRequest {
        public long AppId { get; set; }
        public string? Pem { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class AppTokenRequest {
        public long AppId { get; set; }
        public long InstallationId { get; set; }
        public string? Pem { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class SetupRequest {
        public string? Repo { get; set; }
        public List<string>? Repos { get; set; }
        public string? GitHubToken { get; set; }
        public string? GitHubClientId { get; set; }
        public bool WithConfig { get; set; }
        public string? AuthB64 { get; set; }
        public string? AuthB64Path { get; set; }
        public string? Provider { get; set; }
        public string? ConfigJson { get; set; }
        public string? ConfigPath { get; set; }
        public string? ReviewProfile { get; set; }
        public string? ReviewMode { get; set; }
        public string? ReviewCommentMode { get; set; }
        public bool? AnalysisEnabled { get; set; }
        public bool? AnalysisGateEnabled { get; set; }
        public string? AnalysisPacks { get; set; }
        public bool SkipSecret { get; set; }
        public bool ManualSecret { get; set; }
        public bool ExplicitSecrets { get; set; }
        public bool Upgrade { get; set; }
        public bool Force { get; set; }
        public bool Cleanup { get; set; }
        public bool KeepSecret { get; set; }
        public bool UpdateSecret { get; set; }
        public bool DryRun { get; set; }
        public string? BranchName { get; set; }
    }

    private sealed class UsageRequest {
        public string? AuthB64 { get; set; }
        public string? AuthB64Path { get; set; }
        public bool IncludeEvents { get; set; }
        public string? AuthKey { get; set; }
        public string? ChatGptApiBaseUrl { get; set; }
    }

    private sealed class OpenAILoginRequest {
        public string? ClientId { get; set; }
        public int RedirectPort { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    private sealed class SetupResponse {
        public string Repo { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    private sealed class RepoStatusResponse {
        public string Repo { get; set; } = string.Empty;
        public string? DefaultBranch { get; set; }
        public bool WorkflowExists { get; set; }
        public bool WorkflowManaged { get; set; }
        public bool ConfigExists { get; set; }
        public string? Error { get; set; }
    }

    private sealed class TempFile : IDisposable {
        private readonly string _path;

        public TempFile(string path) {
            _path = path;
        }

        public void Dispose() {
            if (string.IsNullOrWhiteSpace(_path)) {
                return;
            }
            try {
                if (File.Exists(_path)) {
                    File.Delete(_path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Temp file cleanup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static void TryHardenTempFile(string path) {
        try {
            if (!File.Exists(path)) {
                return;
            }
            var attrs = File.GetAttributes(path);
            File.SetAttributes(path, attrs | FileAttributes.Temporary | FileAttributes.Hidden);
        } catch (Exception ex) {
            Trace.TraceWarning($"Temp file harden failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class UsageResponse {
        public UsageSnapshot? Usage { get; set; }
        public List<UsageEvent>? Events { get; set; }
        public string? UpdatedAt { get; set; }
    }

    private sealed class UsageSnapshot {
        public string? PlanType { get; set; }
        public string? Email { get; set; }
        public string? AccountId { get; set; }
        public UsageRateLimit? RateLimit { get; set; }
        public UsageRateLimit? CodeReviewRateLimit { get; set; }
        public UsageCredits? Credits { get; set; }

        public static UsageSnapshot From(ChatGptUsageSnapshot snapshot) {
            return new UsageSnapshot {
                PlanType = snapshot.PlanType,
                Email = snapshot.Email,
                AccountId = snapshot.AccountId,
                RateLimit = UsageRateLimit.From(snapshot.RateLimit),
                CodeReviewRateLimit = UsageRateLimit.From(snapshot.CodeReviewRateLimit),
                Credits = UsageCredits.From(snapshot.Credits)
            };
        }
    }

    private sealed class UsageRateLimit {
        public bool Allowed { get; set; }
        public bool LimitReached { get; set; }
        public UsageRateLimitWindow? Primary { get; set; }
        public UsageRateLimitWindow? Secondary { get; set; }

        public static UsageRateLimit? From(ChatGptRateLimitStatus? status) {
            if (status is null) {
                return null;
            }
            return new UsageRateLimit {
                Allowed = status.Allowed,
                LimitReached = status.LimitReached,
                Primary = UsageRateLimitWindow.From(status.PrimaryWindow),
                Secondary = UsageRateLimitWindow.From(status.SecondaryWindow)
            };
        }
    }

    private sealed class UsageRateLimitWindow {
        public double? UsedPercent { get; set; }
        public long? LimitWindowSeconds { get; set; }
        public long? ResetAfterSeconds { get; set; }
        public long? ResetAt { get; set; }

        public static UsageRateLimitWindow? From(ChatGptRateLimitWindow? window) {
            if (window is null) {
                return null;
            }
            return new UsageRateLimitWindow {
                UsedPercent = window.UsedPercent,
                LimitWindowSeconds = window.LimitWindowSeconds,
                ResetAfterSeconds = window.ResetAfterSeconds,
                ResetAt = window.ResetAtUnixSeconds
            };
        }
    }

    private sealed class UsageCredits {
        public bool HasCredits { get; set; }
        public bool Unlimited { get; set; }
        public double? Balance { get; set; }
        public int[]? ApproxLocalMessages { get; set; }
        public int[]? ApproxCloudMessages { get; set; }

        public static UsageCredits? From(ChatGptCreditsSnapshot? credits) {
            if (credits is null) {
                return null;
            }
            return new UsageCredits {
                HasCredits = credits.HasCredits,
                Unlimited = credits.Unlimited,
                Balance = credits.Balance,
                ApproxLocalMessages = credits.ApproxLocalMessages,
                ApproxCloudMessages = credits.ApproxCloudMessages
            };
        }
    }

    private sealed class UsageEvent {
        public string? Date { get; set; }
        public string? ProductSurface { get; set; }
        public double? CreditAmount { get; set; }
        public string? UsageId { get; set; }

        public static List<UsageEvent> From(IReadOnlyList<ChatGptCreditUsageEvent> events) {
            var result = new List<UsageEvent>();
            foreach (var evt in events) {
                result.Add(new UsageEvent {
                    Date = evt.Date,
                    ProductSurface = evt.ProductSurface,
                    CreditAmount = evt.CreditAmount,
                    UsageId = evt.UsageId
                });
            }
            return result;
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
