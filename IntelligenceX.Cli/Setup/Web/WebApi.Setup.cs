using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup;
using IntelligenceX.Cli.Setup.Wizard;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private static string[] BuildSetupArgsForRepo(SetupRequest request, bool routeDryRun, string repo) {
        var effectiveDryRun = routeDryRun || request.DryRun;
        return BuildSetupArgs(request, effectiveDryRun, repo);
    }


    private static bool ResolveWithConfigFromArgs(IReadOnlyList<string> args) {
        return ContainsArg(args, "--with-config") ||
               ContainsArg(args, "--config-json") ||
               ContainsArg(args, "--config-path");
    }

    private static (bool ExpectOrgSecret, string? SecretOrg) ResolveOrgSecretVerificationContext(
        SetupApplyOperation operation,
        string provider,
        string? secretTarget,
        string? secretOrg) {
        var expectOrgSecret = (operation == SetupApplyOperation.Setup || operation == SetupApplyOperation.UpdateSecret) &&
                              IsOpenAiProvider(provider) &&
                              string.Equals(secretTarget, "org", StringComparison.OrdinalIgnoreCase);
        return (expectOrgSecret, expectOrgSecret ? secretOrg : null);
    }

    private static bool IsOpenAiProvider(string provider) {
        return string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSecretOrgForRepo(string repo, string? secretOrg) {
        if (!string.IsNullOrWhiteSpace(secretOrg)) {
            return secretOrg;
        }

        if (string.IsNullOrWhiteSpace(repo)) {
            return null;
        }

        var slashIndex = repo.IndexOf('/');
        if (slashIndex <= 0) {
            return null;
        }

        return repo[..slashIndex];
    }

    private static bool ContainsArg(IReadOnlyList<string> args, string name) {
        for (var i = 0; i < args.Count; i++) {
            if (string.Equals(args[i], name, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    private static bool TryValidateAndNormalizeOpenAiAccountRouting(
        SetupRequest request,
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        out string? error) {
        error = null;

        var hasRoutingInput = !string.IsNullOrWhiteSpace(request.OpenAIAccountId) ||
                              !string.IsNullOrWhiteSpace(request.OpenAIAccountIds) ||
                              !string.IsNullOrWhiteSpace(request.OpenAIAccountRotation) ||
                              request.OpenAIAccountFailover.HasValue;
        if (!hasRoutingInput) {
            return true;
        }

        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "openai" : request.Provider!;
        if (!IsOpenAiProvider(provider)) {
            error = "OpenAI account routing options are supported only when provider=openai/chatgpt/codex.";
            return false;
        }

        if (!isSetup) {
            error = "OpenAI account routing options are only supported for setup operation.";
            return false;
        }
        if (hasConfigOverride) {
            error = "OpenAI account routing options are not supported when configJson/configPath override is used.";
            return false;
        }
        if (!withConfig) {
            error = "OpenAI account routing options require withConfig=true.";
            return false;
        }

        request.OpenAIAccountId = string.IsNullOrWhiteSpace(request.OpenAIAccountId)
            ? null
            : request.OpenAIAccountId.Trim();
        var normalizedIds = (request.OpenAIAccountIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(request.OpenAIAccountId) &&
            !normalizedIds.Any(id => string.Equals(id, request.OpenAIAccountId, StringComparison.OrdinalIgnoreCase))) {
            normalizedIds.Insert(0, request.OpenAIAccountId!);
        }

        request.OpenAIAccountIds = normalizedIds.Count == 0
            ? null
            : string.Join(",", normalizedIds);
        var hasConfiguredAccounts = !string.IsNullOrWhiteSpace(request.OpenAIAccountId) || normalizedIds.Count > 0;
        if (!hasConfiguredAccounts) {
            request.OpenAIAccountRotation = null;
            request.OpenAIAccountFailover = null;
            return true;
        }

        var rotation = string.IsNullOrWhiteSpace(request.OpenAIAccountRotation)
            ? "first-available"
            : request.OpenAIAccountRotation.Trim().ToLowerInvariant();
        rotation = rotation switch {
            "first" or "first-available" or "first_available" or "ordered" => "first-available",
            "round-robin" or "round_robin" or "rr" or "rotate" => "round-robin",
            "sticky" or "pin" or "pinned" => "sticky",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(rotation)) {
            error = "OpenAI account rotation must be one of: first-available, round-robin, sticky.";
            return false;
        }
        request.OpenAIAccountRotation = rotation;
        request.OpenAIAccountFailover ??= true;
        return true;
    }

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
        var withConfig = request.WithConfig || hasConfigOverride;
        if (!WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: isSetup,
            withConfig: withConfig,
            hasConfigOverride: hasConfigOverride,
            analysisEnabled: request.AnalysisEnabled,
            analysisGateEnabled: request.AnalysisGateEnabled,
            analysisRunStrict: request.AnalysisRunStrict,
            analysisPacks: request.AnalysisPacks,
            analysisExportPath: request.AnalysisExportPath,
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGateEnabled,
            normalizedRunStrict: out var normalizedRunStrict,
            normalizedPacks: out var normalizedPacks,
            normalizedExportPath: out var normalizedExportPath,
            error: out var analysisError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = analysisError }).ConfigureAwait(false);
            return;
        }
        request.AnalysisEnabled = normalizedEnabled;
        request.AnalysisGateEnabled = normalizedGateEnabled;
        request.AnalysisRunStrict = normalizedRunStrict;
        request.AnalysisPacks = normalizedPacks;
        request.AnalysisExportPath = normalizedExportPath;
        if (!WebSetupReviewConfigValidator.TryValidateAndNormalize(
            isSetup: isSetup,
            withConfig: withConfig,
            hasConfigOverride: hasConfigOverride,
            reviewIntent: request.ReviewIntent,
            reviewStrictness: request.ReviewStrictness,
            reviewLoopPolicy: request.ReviewLoopPolicy,
            reviewVisionPath: request.ReviewVisionPath,
            mergeBlockerSections: request.MergeBlockerSections,
            mergeBlockerRequireAllSections: request.MergeBlockerRequireAllSections,
            mergeBlockerRequireSectionMatch: request.MergeBlockerRequireSectionMatch,
            normalizedReviewIntent: out var normalizedReviewIntent,
            normalizedReviewStrictness: out var normalizedReviewStrictness,
            normalizedReviewLoopPolicy: out var normalizedReviewLoopPolicy,
            normalizedReviewVisionPath: out var normalizedReviewVisionPath,
            normalizedMergeBlockerSections: out var normalizedMergeBlockerSections,
            normalizedMergeBlockerRequireAllSections: out var normalizedMergeBlockerRequireAllSections,
            normalizedMergeBlockerRequireSectionMatch: out var normalizedMergeBlockerRequireSectionMatch,
            error: out var reviewConfigError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = reviewConfigError }).ConfigureAwait(false);
            return;
        }
        request.ReviewIntent = normalizedReviewIntent;
        request.ReviewStrictness = normalizedReviewStrictness;
        request.ReviewLoopPolicy = normalizedReviewLoopPolicy;
        request.ReviewVisionPath = normalizedReviewVisionPath;
        request.MergeBlockerSections = normalizedMergeBlockerSections;
        request.MergeBlockerRequireAllSections = normalizedMergeBlockerRequireAllSections;
        request.MergeBlockerRequireSectionMatch = normalizedMergeBlockerRequireSectionMatch;
        if (!TryValidateAndNormalizeOpenAiAccountRouting(request, isSetup, withConfig, hasConfigOverride, out var routingError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = routingError }).ConfigureAwait(false);
            return;
        }

        var repos = request.Repos is not null && request.Repos.Count > 0
            ? request.Repos
            : new List<string> { request.Repo! };

        var outputs = new List<SetupResponse>();
        var operation = request.Cleanup
            ? SetupApplyOperation.Cleanup
            : request.UpdateSecret
                ? SetupApplyOperation.UpdateSecret
                : SetupApplyOperation.Setup;
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "openai" : request.Provider!;
        var requestDryRun = dryRun || request.DryRun;

        GitHubRepoClient? verifyClient = null;
        try {
            if (!requestDryRun && !string.IsNullOrWhiteSpace(request.GitHubToken)) {
                verifyClient = new GitHubRepoClient(request.GitHubToken!, "https://api.github.com");
            }

            foreach (var repo in repos) {
                var args = BuildSetupArgsForRepo(request, dryRun, repo);
                var repoWithConfig = ResolveWithConfigFromArgs(args);
                var secretOrgForRepo = ResolveSecretOrgForRepo(repo, request.SecretOrg);
                var orgSecretContext = ResolveOrgSecretVerificationContext(operation, provider, request.SecretTarget, secretOrgForRepo);
                var result = await RunSetupAsync(args).ConfigureAwait(false);
                result.Repo = repo;
                result.PullRequestUrl = SetupPostApplyVerifier.ExtractPullRequestUrl(result.Output);
                var verifyContext = new SetupPostApplyContext {
                    Repo = repo,
                    Operation = operation,
                    WithConfig = repoWithConfig,
                    SkipSecret = request.SkipSecret,
                    ManualSecret = request.ManualSecret,
                    KeepSecret = request.KeepSecret,
                    DryRun = requestDryRun,
                    ExitSuccess = result.ExitCode == 0,
                    ExpectOrgSecret = orgSecretContext.ExpectOrgSecret,
                    SecretOrg = orgSecretContext.SecretOrg,
                    Provider = provider,
                    Output = result.Output,
                    PullRequestUrl = result.PullRequestUrl
                };
                result.Verify = await ResolvePostApplyVerificationAsync(
                    verifyContext,
                    () => SetupPostApplyVerifier.VerifyAsync(verifyClient, verifyContext)).ConfigureAwait(false);
                outputs.Add(result);
            }
        } finally {
            verifyClient?.Dispose();
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
            analysisRunStrict: request.AnalysisRunStrict,
            analysisPacks: request.AnalysisPacks,
            analysisExportPath: request.AnalysisExportPath,
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGateEnabled,
            normalizedRunStrict: out var normalizedRunStrict,
            normalizedPacks: out var normalizedPacks,
            normalizedExportPath: out var normalizedExportPath,
            error: out var analysisError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = analysisError }).ConfigureAwait(false);
            return;
        }
        request.AnalysisEnabled = normalizedEnabled;
        request.AnalysisGateEnabled = normalizedGateEnabled;
        request.AnalysisRunStrict = normalizedRunStrict;
        request.AnalysisPacks = normalizedPacks;
        request.AnalysisExportPath = normalizedExportPath;
        if (!WebSetupReviewConfigValidator.TryValidateAndNormalize(
            isSetup: isSetup,
            withConfig: withConfig,
            hasConfigOverride: hasConfigOverride,
            reviewIntent: request.ReviewIntent,
            reviewStrictness: request.ReviewStrictness,
            reviewLoopPolicy: request.ReviewLoopPolicy,
            reviewVisionPath: request.ReviewVisionPath,
            mergeBlockerSections: request.MergeBlockerSections,
            mergeBlockerRequireAllSections: request.MergeBlockerRequireAllSections,
            mergeBlockerRequireSectionMatch: request.MergeBlockerRequireSectionMatch,
            normalizedReviewIntent: out var normalizedReviewIntent,
            normalizedReviewStrictness: out var normalizedReviewStrictness,
            normalizedReviewLoopPolicy: out var normalizedReviewLoopPolicy,
            normalizedReviewVisionPath: out var normalizedReviewVisionPath,
            normalizedMergeBlockerSections: out var normalizedMergeBlockerSections,
            normalizedMergeBlockerRequireAllSections: out var normalizedMergeBlockerRequireAllSections,
            normalizedMergeBlockerRequireSectionMatch: out var normalizedMergeBlockerRequireSectionMatch,
            error: out var reviewConfigError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = reviewConfigError }).ConfigureAwait(false);
            return;
        }
        request.ReviewIntent = normalizedReviewIntent;
        request.ReviewStrictness = normalizedReviewStrictness;
        request.ReviewLoopPolicy = normalizedReviewLoopPolicy;
        request.ReviewVisionPath = normalizedReviewVisionPath;
        request.MergeBlockerSections = normalizedMergeBlockerSections;
        request.MergeBlockerRequireAllSections = normalizedMergeBlockerRequireAllSections;
        request.MergeBlockerRequireSectionMatch = normalizedMergeBlockerRequireSectionMatch;
        if (!TryValidateAndNormalizeOpenAiAccountRouting(request, isSetup, withConfig, hasConfigOverride, out var routingError)) {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context, new { error = routingError }).ConfigureAwait(false);
            return;
        }

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
                note = "Using config path override.",
                config = (string?)null
            }).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ConfigJson)) {
            string normalized;
            try {
                var parsed = JsonNode.Parse(request.ConfigJson!);
                normalized = parsed?.ToJsonString(CliJson.Indented) ?? request.ConfigJson!;
            } catch (JsonException) {
                context.Response.StatusCode = 400;
                await WriteJsonAsync(context, new { error = "Invalid configJson payload." }).ConfigureAwait(false);
                return;
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

        try {
            var args = BuildSetupArgs(request, dryRun: true, previewRepo!);
            var config = SetupRunner.BuildReviewerConfigJson(args);
            await WriteJsonOkAsync(context, new {
                source = "generated",
                note = "Generated from current setup selections.",
                config
            }).ConfigureAwait(false);
        } catch (Exception ex) {
            Trace.TraceWarning($"Effective config preview generation failed: {ex.GetType().Name}: {ex.Message}");
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new {
                error = "Effective config preview is unavailable."
            }).ConfigureAwait(false);
        }
    }
}
