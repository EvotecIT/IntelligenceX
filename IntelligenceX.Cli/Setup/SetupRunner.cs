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
            if (!string.IsNullOrWhiteSpace(options.ConfigJson) && !string.IsNullOrWhiteSpace(options.ConfigPath)) {
                Console.Error.WriteLine("Choose only one of --config-json or --config-path.");
                return 1;
            }
            if (!string.IsNullOrWhiteSpace(options.AuthB64) && !string.IsNullOrWhiteSpace(options.AuthB64Path)) {
                Console.Error.WriteLine("Choose only one of --auth-b64 or --auth-b64-path.");
                return 1;
            }

            var hasConfigOverride = !string.IsNullOrWhiteSpace(options.ConfigJson) ||
                                    !string.IsNullOrWhiteSpace(options.ConfigPath);
            var withConfig = options.WithConfig || hasConfigOverride;
            var isSetupOperation = !options.Cleanup && !options.UpdateSecret;
            if (!TryValidateAnalysisOptionContext(
                    options,
                    isSetup: isSetupOperation,
                    withConfig: withConfig,
                    hasConfigOverride: hasConfigOverride,
                    out var analysisOptionError)) {
                Console.Error.WriteLine(analysisOptionError);
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

            if (options.DryRun) {
                PrintDryRun(state, workflowPlan, configPlan, exportPlans);
                return 0;
            }

            if (!options.SkipSecret) {
                var authB64 = ResolveAuthB64(options);
                if (string.IsNullOrWhiteSpace(authB64)) {
                    state.OpenAI.AuthBundle = await LoginOpenAiAsync(options).ConfigureAwait(false);
                    state.OpenAI.AuthJson = AuthBundleSerializer.Serialize(state.OpenAI.AuthBundle);
                    state.OpenAI.AuthB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(state.OpenAI.AuthJson));
                } else {
                    state.OpenAI.AuthB64 = authB64;
                }

                if (options.ManualSecret) {
                    PrintManualSecret(state.OpenAI.AuthB64);
                } else {
                    await github.SetSecretAsync(owner, repo, "INTELLIGENCEX_AUTH_B64", state.OpenAI.AuthB64)
                        .ConfigureAwait(false);
                }
            }

            var hasFileChanges = workflowPlan.IsWrite || configPlan.IsWrite || exportPlans.Any(plan => plan.IsWrite);
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
            var prBody = options.WithConfig
                ? BuildSetupPrBody(includeAnalysisExport: exportPlans.Any(plan => plan.IsWrite))
                : "This adds the IntelligenceX review workflow.";
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

    private static bool TryValidateAnalysisOptionContext(
        SetupOptions options,
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        out string? error) {
        error = null;
        var hasAnyAnalysisOption = options.AnalysisEnabledSet ||
                                   options.AnalysisGateEnabledSet ||
                                   options.AnalysisRunStrictSet ||
                                   options.AnalysisPacksSet ||
                                   options.AnalysisExportPathSet;
        if (!hasAnyAnalysisOption) {
            return true;
        }

        if (!isSetup) {
            error = "Analysis options are only supported for setup operation.";
            return false;
        }

        if (!withConfig) {
            error = "Analysis options require --with-config (or --config-json/--config-path).";
            return false;
        }

        if (hasConfigOverride) {
            error = "Analysis options are not supported when --config-json/--config-path override is used.";
            return false;
        }

        var requiresAnalysisEnabled = options.AnalysisGateEnabledSet ||
                                      options.AnalysisRunStrictSet ||
                                      options.AnalysisPacksSet ||
                                      options.AnalysisExportPathSet;
        if (requiresAnalysisEnabled && (!options.AnalysisEnabledSet || !options.AnalysisEnabled)) {
            error = "--analysis-gate/--analysis-run-strict/--analysis-packs/--analysis-export-path require --analysis-enabled true.";
            return false;
        }

        if (options.AnalysisPacksSet) {
            if (!SetupAnalysisPacks.TryNormalizeCsv(options.AnalysisPacks, out var normalizedPacks, out error)) {
                return false;
            }
            options.AnalysisPacks = normalizedPacks;
        }

        return true;
    }

    private static async Task ResolveGitHubAuthAsync(SetupState state) {
        var options = state.Options;

        var token = options.GitHubToken;
        if (string.IsNullOrWhiteSpace(token)) {
            token = await TryReadGhTokenAsync(options.GitHubAuthBaseUrl).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(token)) {
            // Fall back to device flow using the built-in IntelligenceX OAuth app client id.
            if (string.IsNullOrWhiteSpace(options.GitHubClientId)) {
                options.GitHubClientId = IntelligenceXDefaults.GetEffectiveGitHubClientId();
            }
            token = await GitHubDeviceFlow.LoginAsync(options.GitHubClientId!, options.GitHubAuthBaseUrl, options.GitHubScopes)
                .ConfigureAwait(false);
        }

        state.GitHub.Token = token;
        state.GitHub.ClientId = options.GitHubClientId;
    }

    private static async Task<string?> TryReadGhTokenAsync(string gitHubAuthBaseUrl) {
        // Prefer reusing existing `gh auth login` state to keep setup to ~1-3 steps.
        var host = "github.com";
        try {
            if (!string.IsNullOrWhiteSpace(gitHubAuthBaseUrl)) {
                host = new Uri(gitHubAuthBaseUrl).Host;
            }
        } catch {
            // ignore
        }
        var (code, stdout, _) = await GhCli.RunAsync(TimeSpan.FromSeconds(15),
            "auth", "token", "--hostname", host).ConfigureAwait(false);
        if (code != 0) {
            return null;
        }
        var token = (stdout ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static async Task<string?> ResolveRepositoryAsync(SetupState state) {
        var options = state.Options;
        if (!string.IsNullOrWhiteSpace(options.RepoFullName)) {
            return options.RepoFullName;
        }

        var autoRepo = GitHubRepoDetector.TryDetectRepo(Environment.CurrentDirectory);
        if (!string.IsNullOrWhiteSpace(autoRepo)) {
            Console.WriteLine($"Auto-detected repository: {autoRepo}");
            return autoRepo;
        }

        if (state.GitHub.Repositories.Count == 0) {
            return Prompt("Repository (owner/name)");
        }

        var filter = Prompt("Filter repositories (optional)");
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? state.GitHub.Repositories
            : state.GitHub.Repositories
                .Where(r => r.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (filtered.Count == 0) {
            Console.WriteLine("No repositories matched. Enter full name manually.");
            return Prompt("Repository (owner/name)");
        }

        var ordered = filtered
            .OrderByDescending(r => r.UpdatedAt ?? DateTimeOffset.MinValue)
            .Take(50)
            .ToList();

        Console.WriteLine("Select a repository:");
        for (var i = 0; i < ordered.Count; i++) {
            var repo = ordered[i];
            var privacy = repo.Private ? "private" : "public";
            var updated = repo.UpdatedAt?.ToString("yyyy-MM-dd") ?? "unknown";
            Console.WriteLine($"  {i + 1,2}. {repo.FullName} ({privacy}, updated {updated})");
        }

        var choice = Prompt("Repository number");
        if (int.TryParse(choice, out var index) && index > 0 && index <= ordered.Count) {
            return ordered[index - 1].FullName;
        }

        Console.WriteLine("Invalid selection. Enter full name manually.");
        return Prompt("Repository (owner/name)");
    }

    private static async Task<int> RunCleanupAsync(GitHubApi github, SetupState state) {
        var options = state.Options;
        var owner = state.GitHub.Owner ?? "<owner>";
        var repo = state.GitHub.Repo ?? "<repo>";
        var defaultBranch = await github.GetDefaultBranchAsync(owner, repo).ConfigureAwait(false);
        var workflow = await github.TryGetFileAsync(owner, repo, ".github/workflows/review-intelligencex.yml", defaultBranch)
            .ConfigureAwait(false);
        var reviewerConfig = await github.TryGetFileAsync(owner, repo, ".intelligencex/reviewer.json", defaultBranch)
            .ConfigureAwait(false);
        var legacyConfig = await github.TryGetFileAsync(owner, repo, ".intelligencex/config.json", defaultBranch)
            .ConfigureAwait(false);
        var legacyLooksLikeReviewer = legacyConfig is not null && LooksLikeReviewerConfig(legacyConfig.Content);

        if (options.DryRun) {
            Console.WriteLine("Cleanup dry run:");
            Console.WriteLine($"- Repo: {state.GitHub.RepositoryFullName}");
            Console.WriteLine($"- Secret: INTELLIGENCEX_AUTH_B64 ({(options.KeepSecret ? "keep" : "delete")})");
            Console.WriteLine($"- File: .github/workflows/review-intelligencex.yml ({(workflow is null ? "skip (missing)" : "delete")})");
            Console.WriteLine($"- File: .intelligencex/reviewer.json ({(reviewerConfig is null ? "skip (missing)" : "delete")})");
            Console.WriteLine($"- File: .intelligencex/config.json ({(legacyLooksLikeReviewer ? "delete (legacy reviewer config)" : "skip (reserved for library config)")})");
            Console.WriteLine("- PR: would be created on a new branch for file removals");
            return 0;
        }

        if (!options.KeepSecret) {
            await github.DeleteSecretAsync(owner, repo, "INTELLIGENCEX_AUTH_B64").ConfigureAwait(false);
        }

        if (workflow is null && reviewerConfig is null && !legacyLooksLikeReviewer) {
            Console.WriteLine("No files found to remove. Skipping PR creation.");
            return 0;
        }

        var baseSha = await github.GetBranchShaAsync(owner, repo, defaultBranch).ConfigureAwait(false);
        var branchName = options.BranchName ?? $"intelligencex-cleanup/{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        await github.EnsureBranchAsync(owner, repo, branchName, baseSha).ConfigureAwait(false);

        var changed = false;
        if (workflow is not null) {
            changed |= await github.DeleteFileAsync(owner, repo, ".github/workflows/review-intelligencex.yml",
                "Remove IntelligenceX review workflow", branchName).ConfigureAwait(false);
        }
        if (reviewerConfig is not null) {
            changed |= await github.DeleteFileAsync(owner, repo, ".intelligencex/reviewer.json",
                "Remove IntelligenceX reviewer config", branchName).ConfigureAwait(false);
        }
        if (legacyLooksLikeReviewer) {
            changed |= await github.DeleteFileAsync(owner, repo, ".intelligencex/config.json",
                "Remove legacy IntelligenceX reviewer config", branchName).ConfigureAwait(false);
        }

        if (!changed) {
            Console.WriteLine("No files removed. Skipping PR creation.");
            return 0;
        }

        var prTitle = "Remove IntelligenceX review automation";
        var prBody = "This removes the IntelligenceX review workflow and reviewer config.";
        var prUrl = await github.CreatePullRequestAsync(owner, repo, prTitle, branchName, defaultBranch, prBody)
            .ConfigureAwait(false);

        Console.WriteLine(prUrl is null
            ? "Cleanup complete. Files removed on branch, but PR was not created."
            : $"Cleanup complete. PR created: {prUrl}");

        return 0;
    }

    private static async Task<int> UpdateSecretAsync(GitHubApi github, SetupState state) {
        var options = state.Options;
        var owner = state.GitHub.Owner ?? "<owner>";
        var repo = state.GitHub.Repo ?? "<repo>";

        if (options.DryRun) {
            Console.WriteLine("Secret update dry run:");
            Console.WriteLine($"- Repo: {state.GitHub.RepositoryFullName}");
            Console.WriteLine("- Secret: INTELLIGENCEX_AUTH_B64 (would be updated)");
            return 0;
        }

        var authB64 = ResolveAuthB64(options);
        if (string.IsNullOrWhiteSpace(authB64)) {
            state.OpenAI.AuthBundle = await LoginOpenAiAsync(options).ConfigureAwait(false);
            state.OpenAI.AuthJson = AuthBundleSerializer.Serialize(state.OpenAI.AuthBundle);
            state.OpenAI.AuthB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(state.OpenAI.AuthJson));
        } else {
            state.OpenAI.AuthB64 = authB64;
        }

        await github.SetSecretAsync(owner, repo, "INTELLIGENCEX_AUTH_B64", state.OpenAI.AuthB64)
            .ConfigureAwait(false);

        Console.WriteLine("Secret updated: INTELLIGENCEX_AUTH_B64");
        return 0;
    }

    internal static bool ValidateLocalAnalysisCatalogForTests(string workspace, out string error) {
        return TryValidateLocalAnalysisCatalog(workspace, out error);
    }

    private static bool TryValidateLocalAnalysisCatalog(string workspace, out string error) {
        error = string.Empty;
        var resolvedWorkspace = string.IsNullOrWhiteSpace(workspace) ? Environment.CurrentDirectory : workspace;
        var rulesRoot = Path.Combine(resolvedWorkspace, "Analysis", "Catalog", "rules");
        var packsRoot = Path.Combine(resolvedWorkspace, "Analysis", "Packs");

        if (!Directory.Exists(rulesRoot) || !Directory.Exists(packsRoot)) {
            error =
                "--analysis-export-path requires a local analysis catalog. Expected directories: Analysis/Catalog/rules and Analysis/Packs.";
            return false;
        }

        var hasRuleFiles = Directory.EnumerateFiles(rulesRoot, "*.json", SearchOption.AllDirectories).Any();
        var hasPackFiles = Directory.EnumerateFiles(packsRoot, "*.json", SearchOption.TopDirectoryOnly).Any();
        if (!hasRuleFiles || !hasPackFiles) {
            error =
                "--analysis-export-path requires local catalog JSON files under Analysis/Catalog/rules and Analysis/Packs.";
            return false;
        }

        return true;
    }

    private static async Task<List<FilePlan>> PlanAnalysisExportFilesAsync(
        GitHubApi github,
        SetupOptions options,
        string owner,
        string repo,
        string defaultBranch,
        FilePlan configPlan,
        string? existingConfigContent) {
        var plans = new List<FilePlan>();
        if (!options.AnalysisExportPathSet) {
            return plans;
        }

        if (!SetupAnalysisExportPath.TryNormalize(options.AnalysisExportPath, out var normalizedExportPath, out var exportPathError)) {
            throw new InvalidOperationException(exportPathError ?? "Invalid --analysis-export-path value.");
        }
        if (string.IsNullOrWhiteSpace(normalizedExportPath)) {
            throw new InvalidOperationException("Invalid --analysis-export-path value.");
        }

        var effectiveConfigContent = ResolveEffectiveConfigContentForAnalysisExport(options, configPlan, existingConfigContent);
        if (string.IsNullOrWhiteSpace(effectiveConfigContent)) {
            throw new InvalidOperationException("--analysis-export-path requires effective reviewer config content.");
        }

        IntelligenceX.Json.JsonObject? root;
        try {
            root = JsonLite.Parse(effectiveConfigContent)?.AsObject();
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to parse effective reviewer config for analysis export: {ex.Message}");
        }
        if (root is null) {
            throw new InvalidOperationException("Effective reviewer config must be a JSON object for analysis export.");
        }

        var settings = new AnalysisSettings();
        var reviewObj = root.GetObject("review") ?? root;
        AnalysisConfigReader.Apply(root, reviewObj, settings);
        if (!settings.Enabled) {
            throw new InvalidOperationException("--analysis-export-path requires analysis.enabled=true.");
        }

        var catalog = AnalysisCatalogLoader.LoadFromWorkspace(Environment.CurrentDirectory);
        var tempOutputDirectory = Path.Combine(Path.GetTempPath(), "ix-setup-analysis-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempOutputDirectory);
        try {
            var export = AnalysisConfigExporter.Export(settings, catalog, tempOutputDirectory);
            foreach (var warning in export.Warnings) {
                Console.WriteLine($"Warning: {warning}");
            }
            if (export.Files.Count == 0) {
                throw new InvalidOperationException(
                    "No analyzer config files were generated. Check analysis packs and local Analysis catalog availability.");
            }

            var targetEntries = new List<(string Path, string Content)>();
            foreach (var generatedPath in export.Files) {
                var fileName = Path.GetFileName(generatedPath);
                if (string.IsNullOrWhiteSpace(fileName)) {
                    continue;
                }
                var targetPath = SetupAnalysisExportPath.Combine(normalizedExportPath, fileName);
                var content = File.ReadAllText(generatedPath);
                targetEntries.Add((targetPath, content));
            }

            var duplicateTargetPath = SetupAnalysisExportPath.FindFirstDuplicatePath(targetEntries.Select(entry => entry.Path));
            if (!string.IsNullOrWhiteSpace(duplicateTargetPath)) {
                throw new InvalidOperationException($"Duplicate analyzer export target path detected: {duplicateTargetPath}");
            }

            foreach (var entry in targetEntries) {
                var existing = await github.TryGetFileAsync(owner, repo, entry.Path, defaultBranch).ConfigureAwait(false);
                plans.Add(PlanWrite(entry.Path, existing?.Content, entry.Content, options.Force));
            }
        } finally {
            TryDeleteDirectory(tempOutputDirectory);
        }

        return plans;
    }

    private static string? ResolveEffectiveConfigContentForAnalysisExport(SetupOptions options, FilePlan configPlan, string? existingConfigContent) {
        if (!options.WithConfig) {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(configPlan.Content)) {
            return configPlan.Content;
        }
        if (!string.IsNullOrWhiteSpace(existingConfigContent)) {
            return existingConfigContent;
        }
        return ReadConfigOverride(options);
    }

    private static void TryDeleteDirectory(string path) {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
            return;
        }
        try {
            Directory.Delete(path, recursive: true);
        } catch {
            // Best-effort cleanup.
        }
    }

    private static void PrintDryRun(SetupState state, FilePlan workflowPlan, FilePlan configPlan, IReadOnlyList<FilePlan> exportPlans) {
        Console.WriteLine("Dry run summary:");
        Console.WriteLine($"- Repo: {state.GitHub.RepositoryFullName}");
        Console.WriteLine($"- Secret: INTELLIGENCEX_AUTH_B64 ({DescribeSecretAction(state.Options)})");
        Console.WriteLine($"- File: {workflowPlan.Path} ({DescribePlan(workflowPlan)})");
        Console.WriteLine($"- File: {configPlan.Path} ({DescribePlan(configPlan)})");
        foreach (var exportPlan in exportPlans) {
            Console.WriteLine($"- File: {exportPlan.Path} ({DescribePlan(exportPlan)})");
        }
        Console.WriteLine("- PR: would be created on a new branch for file changes");
        Console.WriteLine();

        if (configPlan.Content is not null) {
            Console.WriteLine($"--- {configPlan.Path} ---");
            Console.WriteLine(configPlan.Content);
        }
        if (workflowPlan.Content is not null) {
            Console.WriteLine($"--- {workflowPlan.Path} ---");
            Console.WriteLine(workflowPlan.Content);
        }
        foreach (var exportPlan in exportPlans) {
            if (exportPlan.Content is null) {
                continue;
            }
            Console.WriteLine($"--- {exportPlan.Path} ---");
            Console.WriteLine(exportPlan.Content);
        }
    }

    private static string BuildSetupPrBody(bool includeAnalysisExport) {
        if (!includeAnalysisExport) {
            return "This adds the IntelligenceX review workflow and config.";
        }
        return "This adds the IntelligenceX review workflow and config, and exports analyzer configs for IDE support.";
    }

    private static string DescribePlan(FilePlan plan) {
        if (plan.Action == "skip" && !string.IsNullOrWhiteSpace(plan.Reason)) {
            return $"skip ({plan.Reason})";
        }
        return plan.Action;
    }

    private static string DescribeSecretAction(SetupOptions options) {
        if (options.UpdateSecret) {
            return options.DryRun ? "would update" : "update";
        }
        if (options.Cleanup) {
            return options.KeepSecret ? "keep" : "delete";
        }
        if (options.ManualSecret) {
            return "manual";
        }
        return options.SkipSecret ? "skip" : "create/update";
    }

    private static void PrintManualSecret(string secret) {
        Console.WriteLine("Manual secret mode enabled.");
        Console.WriteLine("Set INTELLIGENCEX_AUTH_B64 in your repo/org secrets with the following value:");
        Console.WriteLine(secret);
        Console.WriteLine("Warning: this value is sensitive. Avoid sharing logs.");
    }

    private static string? ResolveAuthB64(SetupOptions options) {
        if (!string.IsNullOrWhiteSpace(options.AuthB64)) {
            return options.AuthB64;
        }
        if (!string.IsNullOrWhiteSpace(options.AuthB64Path)) {
            try {
                return File.ReadAllText(options.AuthB64Path);
            } catch {
                return null;
            }
        }
        return null;
    }

    private static async Task<AuthBundle> LoginOpenAiAsync(SetupOptions options) {
        var config = OAuthConfig.FromEnvironment();
        config.Validate();
        var service = new OAuthLoginService();
        var loginOptions = new OAuthLoginOptions(config) {
            UseLocalListener = true,
            OnAuthUrl = url => {
                Console.WriteLine($"Open: {url}");
                TryOpenUrl(url);
                return Task.CompletedTask;
            },
            OnPrompt = async prompt => {
                Console.Write(prompt + " ");
                var input = Console.ReadLine();
                return await Task.FromResult(input ?? string.Empty);
            }
        };

        var result = await service.LoginAsync(loginOptions).ConfigureAwait(false);
        return result.Bundle;
    }

    private static void TryOpenUrl(string url) {
        try {
            var psi = new ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        } catch {
            // Ignore failures.
        }
    }

    private static (string owner, string repo) SplitRepo(string fullName) {
        var parts = fullName.Split('/');
        return (parts[0], parts[1]);
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

            // Current schema uses `{ "review": { ... } }` (or occasionally root-as-review).
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

    private static string Prompt(string label) {
        Console.Write(label + ": ");
        return Console.ReadLine() ?? string.Empty;
    }

    // Repo autodetection is implemented in GitHubRepoDetector to keep behavior consistent across commands.
}
