using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup;

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
            await WriteJsonAsync(context, new { error = "Missing GitHub auth" }).ConfigureAwait(false);
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

        await WriteJsonOkAsync(context, new {
            results = outputs
        }).ConfigureAwait(false);
    }

    private async Task HandleSetupEffectiveConfigAsync(System.Net.HttpListenerContext context) {
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

        if (!string.IsNullOrWhiteSpace(request.ConfigJson) && !string.IsNullOrWhiteSpace(request.ConfigPath)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = "Choose only one of configJson or configPath." }).ConfigureAwait(false);
            return;
        }

        var isSetup = !request.Cleanup && !request.UpdateSecret;
        if (!isSetup) {
            await WriteJsonOkAsync(context, new {
                source = "none",
                note = "Effective reviewer config preview is only available for setup operation.",
                config = (string?)null
            }).ConfigureAwait(false);
            return;
        }

        var withConfig = request.WithConfig ||
                         !string.IsNullOrWhiteSpace(request.ConfigJson) ||
                         !string.IsNullOrWhiteSpace(request.ConfigPath);
        var hasConfigOverride = !string.IsNullOrWhiteSpace(request.ConfigJson) || !string.IsNullOrWhiteSpace(request.ConfigPath);

        if (!WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: isSetup,
            withConfig: withConfig,
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

        if (!withConfig) {
            await WriteJsonOkAsync(context, new {
                source = "disabled",
                note = "Reviewer config will not be written (withConfig disabled).",
                config = (string?)null
            }).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ConfigPath)) {
            await WriteJsonOkAsync(context, new {
                source = "path",
                note = $"Using config path override: {request.ConfigPath}",
                config = (string?)null
            }).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ConfigJson)) {
            var normalized = request.ConfigJson!;
            try {
                var parsed = JsonNode.Parse(request.ConfigJson!);
                normalized = parsed?.ToJsonString(CliJson.Indented) ?? request.ConfigJson!;
            } catch {
                // Keep raw input if parsing fails.
            }

            await WriteJsonOkAsync(context, new {
                source = "inline",
                note = "Using inline config override.",
                config = normalized
            }).ConfigureAwait(false);
            return;
        }

        // Generate preview using the same config builder path as setup.
        var previewRepo = request.Repo;
        if (string.IsNullOrWhiteSpace(previewRepo) && request.Repos is { Count: > 0 }) {
            previewRepo = request.Repos[0];
        }
        if (string.IsNullOrWhiteSpace(previewRepo)) {
            previewRepo = "owner/repo";
        }

        var args = BuildSetupArgs(request, dryRun: true, previewRepo!);
        var config = SetupRunner.BuildReviewerConfigJsonForTests(args);
        await WriteJsonOkAsync(context, new {
            source = "generated",
            note = "Generated from current setup selections.",
            config
        }).ConfigureAwait(false);
    }
}
