using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Analysis;
using IntelligenceX.Cli;
using IntelligenceX.Cli.GitHub;
using JsonLite = IntelligenceX.Json.JsonLite;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args) {
        try {
            var options = SetupOptions.Parse(args);
            if (options.ShowHelp) {
                WriteHelp();
                return 0;
            }

            if (options.Cleanup && options.UpdateSecret) {
                Console.Error.WriteLine("Choose only one of --cleanup or --update-secret.");
                return 1;
            }
            if (options.ManualSecret && options.SkipSecret) {
                Console.Error.WriteLine("Choose only one of --manual-secret or --skip-secret.");
                return 1;
            }
            if (options.ManualSecret && options.UpdateSecret) {
                Console.Error.WriteLine("Choose only one of --manual-secret or --update-secret.");
                return 1;
            }
            if (options.ManualSecretStdout && !options.ManualSecret) {
                Console.Error.WriteLine("--manual-secret-stdout requires --manual-secret.");
                return 1;
            }
            if (options.TriageBootstrap && (options.Cleanup || options.UpdateSecret)) {
                Console.Error.WriteLine("--triage-bootstrap is only supported for setup operation.");
                return 1;
            }
            if (!string.IsNullOrWhiteSpace(options.ConfigJson) && !string.IsNullOrWhiteSpace(options.ConfigPath)) {
                Console.Error.WriteLine("Choose only one of --config-json or --config-path.");
                return 1;
            }
            if (!string.IsNullOrWhiteSpace(options.AuthB64) && !string.IsNullOrWhiteSpace(options.AuthB64Path)) {
                Console.Error.WriteLine("Choose only one of --auth-b64 or --auth-b64-path.");
                return 1;
            }

            if (!TryValidateAnalysisOptionContextForCurrentOperation(options, out var withConfig, out var analysisOptionError)) {
                Console.Error.WriteLine(analysisOptionError);
                return 1;
            }
            if (!TryValidateReviewOptionContextForCurrentOperation(options, withConfig, out var reviewOptionError)) {
                Console.Error.WriteLine(reviewOptionError);
                return 1;
            }

            if (options.AnalysisExportPathSet) {
                if (options.Cleanup || options.UpdateSecret) {
                    Console.Error.WriteLine("--analysis-export-path is only supported for setup operation.");
                    return 1;
                }
                if (!withConfig) {
                    Console.Error.WriteLine("--analysis-export-path requires --with-config (or config-json/config-path).");
                    return 1;
                }
                if (!SetupAnalysisExportPath.TryNormalize(options.AnalysisExportPath, out var normalizedExportPath, out var exportError)) {
                    Console.Error.WriteLine(exportError ?? "Invalid --analysis-export-path value.");
                    return 1;
                }
                options.AnalysisExportPath = normalizedExportPath;
                if (!TryValidateLocalAnalysisCatalog(Environment.CurrentDirectory, out var catalogError)) {
                    Console.Error.WriteLine(catalogError);
                    return 1;
                }
            }

            var state = new SetupState(options);
            await ResolveGitHubAuthAsync(state).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(state.GitHub.Token)) {
                Console.Error.WriteLine("GitHub login failed.");
                return 1;
            }

            using var github = new GitHubApi(state.GitHub.Token!, options.GitHubApiBaseUrl);
            state.GitHub.Repositories = await github.ListRepositoriesAsync().ConfigureAwait(false);
            state.GitHub.RepositoryFullName = await ResolveRepositoryAsync(state).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(state.GitHub.RepositoryFullName) || !state.GitHub.RepositoryFullName.Contains('/')) {
                Console.Error.WriteLine("Repository must be in owner/name format.");
                return 1;
            }

            var (owner, repo) = SplitRepo(state.GitHub.RepositoryFullName!);
            state.GitHub.Owner = owner;
            state.GitHub.Repo = repo;

            if (options.Cleanup) {
                return await RunCleanupAsync(github, state).ConfigureAwait(false);
            }

            if (options.UpdateSecret) {
                return await UpdateSecretAsync(github, state).ConfigureAwait(false);
            }

            var defaultBranch = await github.GetDefaultBranchAsync(owner, repo).ConfigureAwait(false);
            var existingWorkflow = await github.TryGetFileAsync(owner, repo, ".github/workflows/review-intelligencex.yml", defaultBranch)
                .ConfigureAwait(false);
            RepoFile? existingConfig = null;
            RepoFile? legacyConfig = null;
            var legacyConfigLooksLikeReviewer = false;
            string? seedConfigContent = null;
            if (options.WithConfig) {
                existingConfig = await github.TryGetFileAsync(owner, repo, ".intelligencex/reviewer.json", defaultBranch)
                    .ConfigureAwait(false);
                if (existingConfig is not null) {
                    seedConfigContent = existingConfig.Content;
                } else {
                    // Backward compatibility: older setup flows wrote reviewer settings into `.intelligencex/config.json`.
                    legacyConfig = await github.TryGetFileAsync(owner, repo, ".intelligencex/config.json", defaultBranch)
                        .ConfigureAwait(false);
                    if (legacyConfig is not null) {
                        legacyConfigLooksLikeReviewer = LooksLikeReviewerConfig(legacyConfig.Content);
                        if (legacyConfigLooksLikeReviewer) {
                            seedConfigContent = legacyConfig.Content;
                        }
                    }
                }
            }

            var workflowPlan = PlanWorkflowChange(options, existingWorkflow?.Content);
            var configPlan = options.WithConfig
                ? PlanConfigChange(options, existingConfig?.Content, seedConfigContent)
                : FilePlan.Skip(".intelligencex/reviewer.json", "Not requested (--with-config not set)");
            var exportPlans = await PlanAnalysisExportFilesAsync(
                github, options, owner, repo, defaultBranch, configPlan, existingConfig?.Content ?? seedConfigContent)
                .ConfigureAwait(false);
            var triagePlans = options.TriageBootstrap
                ? await PlanTriageBootstrapFilesAsync(github, options, owner, repo, defaultBranch, state.GitHub.Token).ConfigureAwait(false)
                : new List<FilePlan>();

            if (options.DryRun) {
                PrintDryRun(state, workflowPlan, configPlan, exportPlans, triagePlans);
                return 0;
            }

            if (!options.SkipSecret && SetupProviderCatalog.RequiresManagedSecret(options.Provider)) {
                var secretValue = await ResolveManagedSecretValueAsync(state).ConfigureAwait(false);
                var secretName = GetRequiredManagedSecretName(options.Provider);
                if (options.ManualSecret) {
                    PrintManualSecret(options.Provider ?? IntelligenceXDefaults.DefaultProvider, secretValue, options.ManualSecretStdout);
                } else {
                    await github.SetSecretAsync(owner, repo, secretName, secretValue).ConfigureAwait(false);
                }
            }

            var hasFileChanges = workflowPlan.IsWrite || configPlan.IsWrite || exportPlans.Any(plan => plan.IsWrite) ||
                                 triagePlans.Any(plan => plan.IsWrite);
            if (!hasFileChanges) {
                Console.WriteLine("No files changed. Skipping PR creation.");
                return 0;
            }

            var baseSha = await github.GetBranchShaAsync(owner, repo, defaultBranch).ConfigureAwait(false);
            var branchName = options.BranchName ?? $"intelligencex-setup/{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            await github.EnsureBranchAsync(owner, repo, branchName, baseSha).ConfigureAwait(false);

            var changed = false;
            if (workflowPlan.IsWrite && workflowPlan.Content is not null) {
                changed |= await github.CreateOrUpdateFileAsync(owner, repo, workflowPlan.Path, workflowPlan.Content,
                    "Configure IntelligenceX review workflow", branchName, overwrite: true).ConfigureAwait(false);
            }
            if (configPlan.IsWrite && configPlan.Content is not null) {
                changed |= await github.CreateOrUpdateFileAsync(owner, repo, configPlan.Path, configPlan.Content,
                    "Configure IntelligenceX reviewer", branchName, overwrite: true).ConfigureAwait(false);
            }
            foreach (var exportPlan in exportPlans) {
                if (!exportPlan.IsWrite || exportPlan.Content is null) {
                    continue;
                }
                changed |= await github.CreateOrUpdateFileAsync(owner, repo, exportPlan.Path, exportPlan.Content,
                    "Export analyzer configs for IDE support", branchName, overwrite: true).ConfigureAwait(false);
            }
            foreach (var triagePlan in triagePlans) {
                if (!triagePlan.IsWrite || triagePlan.Content is null) {
                    continue;
                }
                changed |= await github.CreateOrUpdateFileAsync(owner, repo, triagePlan.Path, triagePlan.Content,
                    DescribeTriageBootstrapCommitMessage(triagePlan.Path), branchName, overwrite: true).ConfigureAwait(false);
            }
            if (legacyConfigLooksLikeReviewer && legacyConfig is not null && configPlan.IsWrite) {
                // Migrate legacy reviewer config location to the correct file name.
                changed |= await github.DeleteFileAsync(owner, repo, ".intelligencex/config.json",
                    "Migrate IntelligenceX reviewer config to reviewer.json", branchName).ConfigureAwait(false);
            }

            if (!changed) {
                Console.WriteLine("No files changed. Skipping PR creation.");
                return 0;
            }

            var prTitle = "Add IntelligenceX review automation";
            var prBody = BuildSetupPrBody(
                includeConfig: options.WithConfig,
                includeAnalysisExport: exportPlans.Any(plan => plan.IsWrite),
                includeTriageBootstrap: triagePlans.Any(plan => plan.IsWrite));
            var prUrl = await github.CreatePullRequestAsync(owner, repo, prTitle, branchName, defaultBranch, prBody)
                .ConfigureAwait(false);

            Console.WriteLine(prUrl is null
                ? "Setup complete. Files updated on branch, but PR was not created."
                : $"Setup complete. PR created: {prUrl}");

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

}
