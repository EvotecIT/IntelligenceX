using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Wizard;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
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
        var analysisApplies = isSetup && withConfig && !hasConfigOverride;
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
}
