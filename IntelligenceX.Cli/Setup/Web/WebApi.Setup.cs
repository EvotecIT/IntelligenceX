using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private async Task HandleSetupAsync(System.Net.HttpListenerContext context, bool dryRun) {
        var body = await ReadJsonBodyAsync(context).ConfigureAwait(false);
        if (body is null) {
            return;
        }
        SetupRequest request;
        try {
            request = JsonSerializer.Deserialize<SetupRequest>(body, _jsonOptions) ?? new SetupRequest();
        } catch (JsonException) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Invalid JSON payload." }).ConfigureAwait(false);
            return;
        }
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
}
