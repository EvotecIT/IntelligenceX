using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Wizard;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed class WebApi {
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(System.Net.HttpListenerContext context) {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (path.StartsWith("/api/repos", StringComparison.OrdinalIgnoreCase)) {
            await HandleReposAsync(context).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("/api/repo-status", StringComparison.OrdinalIgnoreCase)) {
            await HandleRepoStatusAsync(context).ConfigureAwait(false);
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
        if (path.StartsWith("/api/echo", StringComparison.OrdinalIgnoreCase)) {
            await HandleEchoAsync(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = 404;
        await WriteJsonAsync(context, new { error = "Not found" }).ConfigureAwait(false);
    }

    private async Task HandleReposAsync(System.Net.HttpListenerContext context) {
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<RepoListRequest>(body, _jsonOptions) ?? new RepoListRequest();
        if (string.IsNullOrWhiteSpace(request.Token)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing token" }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubRepoClient(request.Token!, request.ApiBaseUrl ?? "https://api.github.com");
            var repos = await client.ListRepositoriesAsync().ConfigureAwait(false);
            await WriteJsonAsync(context, new {
                repos = repos.ConvertAll(r => new { name = r.FullName, updatedAt = r.UpdatedAt })
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleRepoStatusAsync(System.Net.HttpListenerContext context) {
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
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

        var results = new List<RepoStatusResponse>();
        try {
            using var client = new GitHubRepoClient(request.Token!, request.ApiBaseUrl ?? "https://api.github.com");
            foreach (var repo in request.Repos) {
                if (!TryParseRepo(repo, out var owner, out var name)) {
                    results.Add(new RepoStatusResponse { Repo = repo, Error = "Invalid repo name (expected owner/name)." });
                    continue;
                }
                try {
                    var defaultBranch = await client.GetDefaultBranchAsync(owner, name).ConfigureAwait(false);
                    var workflow = await client.TryGetFileAsync(owner, name, ".github/workflows/review-intelligencex.yml", defaultBranch)
                        .ConfigureAwait(false);
                    var config = await client.TryGetFileAsync(owner, name, ".intelligencex/config.json", defaultBranch)
                        .ConfigureAwait(false);

                    var managed = workflow?.Content?.Contains("INTELLIGENCEX:BEGIN", StringComparison.Ordinal) ?? false;
                    results.Add(new RepoStatusResponse {
                        Repo = repo,
                        DefaultBranch = defaultBranch,
                        WorkflowExists = workflow is not null,
                        WorkflowManaged = managed,
                        ConfigExists = config is not null
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

    private async Task HandleDeviceCodeAsync(System.Net.HttpListenerContext context) {
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<DeviceCodeRequest>(body, _jsonOptions) ?? new DeviceCodeRequest();
        if (string.IsNullOrWhiteSpace(request.ClientId)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing clientId" }).ConfigureAwait(false);
            return;
        }

        try {
            var result = await GitHubDeviceFlowClient.RequestCodeAsync(request.ClientId!, request.AuthBaseUrl, request.Scopes)
                .ConfigureAwait(false);
            await WriteJsonAsync(context, result).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleDevicePollAsync(System.Net.HttpListenerContext context) {
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<DevicePollRequest>(body, _jsonOptions) ?? new DevicePollRequest();
        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.DeviceCode)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing clientId or deviceCode" }).ConfigureAwait(false);
            return;
        }

        try {
            var token = await GitHubDeviceFlowClient.PollTokenAsync(request.ClientId!, request.DeviceCode!, request.AuthBaseUrl, request.IntervalSeconds, request.ExpiresIn)
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
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<AppManifestRequest>(body, _jsonOptions) ?? new AppManifestRequest();
        if (string.IsNullOrWhiteSpace(request.AppName)) {
            request.AppName = "IntelligenceX Reviewer";
        }

        try {
            var result = await GitHubAppManifestFlow.RunAsync(new GitHubAppManifestOptions {
                AppName = request.AppName!,
                Owner = request.Owner,
                AuthBaseUrl = request.AuthBaseUrl ?? "https://github.com",
                ApiBaseUrl = request.ApiBaseUrl ?? "https://api.github.com"
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
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<AppInstallationRequest>(body, _jsonOptions) ?? new AppInstallationRequest();
        if (request.AppId <= 0 || string.IsNullOrWhiteSpace(request.Pem)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing appId or pem" }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubAppClient(request.AppId, request.Pem!, request.ApiBaseUrl ?? "https://api.github.com");
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
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize<AppTokenRequest>(body, _jsonOptions) ?? new AppTokenRequest();
        if (request.AppId <= 0 || string.IsNullOrWhiteSpace(request.Pem) || request.InstallationId <= 0) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Missing appId, pem, or installationId" }).ConfigureAwait(false);
            return;
        }

        try {
            using var client = new GitHubAppClient(request.AppId, request.Pem!, request.ApiBaseUrl ?? "https://api.github.com");
            var token = await client.CreateInstallationTokenAsync(request.InstallationId).ConfigureAwait(false);
            await WriteJsonAsync(context, new { token }).ConfigureAwait(false);
        } catch (Exception ex) {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private async Task HandleSetupAsync(System.Net.HttpListenerContext context, bool dryRun) {
        if (context.Request.HttpMethod != "POST") {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context, new { error = "POST required" }).ConfigureAwait(false);
            return;
        }

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
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

        var hasAuthBundle = !string.IsNullOrWhiteSpace(request.AuthB64) || !string.IsNullOrWhiteSpace(request.AuthB64Path);
        if (!request.SkipSecret && !request.Cleanup && !request.UpdateSecret && !hasAuthBundle) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new {
                error = "Web UI cannot perform OpenAI login yet. Provide authB64/authB64Path or set skipSecret=true."
            }).ConfigureAwait(false);
            return;
        }

        if (request.UpdateSecret && !hasAuthBundle) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Update-secret requires authB64/authB64Path in the web UI." }).ConfigureAwait(false);
            return;
        }

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
        if (request.WithConfig) {
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
        if (request.SkipSecret) {
            args.Add("--skip-secret");
        }
        if (request.ManualSecret) {
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
        await ConsoleLock.WaitAsync().ConfigureAwait(false);
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

    private async Task HandleEchoAsync(System.Net.HttpListenerContext context) {
        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        await WriteJsonAsync(context, new { ok = true, body }).ConfigureAwait(false);
    }

    private async Task<string> ReadBodyAsync(System.Net.HttpListenerContext context) {
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private Task WriteJsonAsync(System.Net.HttpListenerContext context, object payload) {
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        return context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
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

    private sealed class DeviceCodeRequest {
        public string? ClientId { get; set; }
        public string? AuthBaseUrl { get; set; } = "https://github.com";
        public string? Scopes { get; set; } = "repo workflow read:org";
    }

    private sealed class DevicePollRequest {
        public string? ClientId { get; set; }
        public string? DeviceCode { get; set; }
        public string? AuthBaseUrl { get; set; } = "https://github.com";
        public int IntervalSeconds { get; set; } = 5;
        public int ExpiresIn { get; set; }
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
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(name);
    }
}
