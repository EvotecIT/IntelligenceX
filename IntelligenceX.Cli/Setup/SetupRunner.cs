using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using Sodium;

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
            if (options.WithConfig) {
                existingConfig = await github.TryGetFileAsync(owner, repo, ".intelligencex/config.json", defaultBranch)
                    .ConfigureAwait(false);
            }

            var workflowPlan = PlanWorkflowChange(options, existingWorkflow?.Content);
            var configPlan = options.WithConfig
                ? PlanConfigChange(options, existingConfig?.Content)
                : FilePlan.Skip(".intelligencex/config.json", "Not requested (--with-config not set)");

            if (options.DryRun) {
                PrintDryRun(state, workflowPlan, configPlan);
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

            var hasFileChanges = workflowPlan.IsWrite || configPlan.IsWrite;
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

            if (!changed) {
                Console.WriteLine("No files changed. Skipping PR creation.");
                return 0;
            }

            var prTitle = "Add IntelligenceX review automation";
            var prBody = options.WithConfig
                ? "This adds the IntelligenceX review workflow and config."
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

    private static async Task ResolveGitHubAuthAsync(SetupState state) {
        var options = state.Options;

        if (string.IsNullOrWhiteSpace(options.GitHubToken) && string.IsNullOrWhiteSpace(options.GitHubClientId)) {
            options.GitHubClientId = Prompt("GitHub OAuth client id (or press Enter to use env INTELLIGENCEX_GITHUB_CLIENT_ID)");
            if (string.IsNullOrWhiteSpace(options.GitHubClientId)) {
                options.GitHubClientId = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_CLIENT_ID");
            }
        }

        if (string.IsNullOrWhiteSpace(options.GitHubToken) && string.IsNullOrWhiteSpace(options.GitHubClientId)) {
            Console.Error.WriteLine("GitHub auth required. Provide --github-token or --github-client-id.");
            return;
        }

        var token = options.GitHubToken;
        if (string.IsNullOrWhiteSpace(token)) {
            token = await GitHubDeviceFlow.LoginAsync(options.GitHubClientId!, options.GitHubAuthBaseUrl, options.GitHubScopes)
                .ConfigureAwait(false);
        }

        state.GitHub.Token = token;
        state.GitHub.ClientId = options.GitHubClientId;
    }

    private static async Task<string?> ResolveRepositoryAsync(SetupState state) {
        var options = state.Options;
        if (!string.IsNullOrWhiteSpace(options.RepoFullName)) {
            return options.RepoFullName;
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
        var config = await github.TryGetFileAsync(owner, repo, ".intelligencex/config.json", defaultBranch)
            .ConfigureAwait(false);

        if (options.DryRun) {
            Console.WriteLine("Cleanup dry run:");
            Console.WriteLine($"- Repo: {state.GitHub.RepositoryFullName}");
            Console.WriteLine($"- Secret: INTELLIGENCEX_AUTH_B64 ({(options.KeepSecret ? "keep" : "delete")})");
            Console.WriteLine($"- File: .github/workflows/review-intelligencex.yml ({(workflow is null ? "skip (missing)" : "delete")})");
            Console.WriteLine($"- File: .intelligencex/config.json ({(config is null ? "skip (missing)" : "delete")})");
            Console.WriteLine("- PR: would be created on a new branch for file removals");
            return 0;
        }

        if (!options.KeepSecret) {
            await github.DeleteSecretAsync(owner, repo, "INTELLIGENCEX_AUTH_B64").ConfigureAwait(false);
        }

        if (workflow is null && config is null) {
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
        if (config is not null) {
            changed |= await github.DeleteFileAsync(owner, repo, ".intelligencex/config.json",
                "Remove IntelligenceX reviewer config", branchName).ConfigureAwait(false);
        }

        if (!changed) {
            Console.WriteLine("No files removed. Skipping PR creation.");
            return 0;
        }

        var prTitle = "Remove IntelligenceX review automation";
        var prBody = "This removes the IntelligenceX review workflow and config.";
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

    private static void PrintDryRun(SetupState state, FilePlan workflowPlan, FilePlan configPlan) {
        Console.WriteLine("Dry run summary:");
        Console.WriteLine($"- Repo: {state.GitHub.RepositoryFullName}");
        Console.WriteLine($"- Secret: INTELLIGENCEX_AUTH_B64 ({DescribeSecretAction(state.Options)})");
        Console.WriteLine($"- File: {workflowPlan.Path} ({DescribePlan(workflowPlan)})");
        Console.WriteLine($"- File: {configPlan.Path} ({DescribePlan(configPlan)})");
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

    private static FilePlan PlanWorkflowChange(SetupOptions options, string? existingContent) {
        var path = ".github/workflows/review-intelligencex.yml";
        if (string.IsNullOrWhiteSpace(existingContent)) {
            var freshSettings = ResolveWorkflowSettings(options, null);
            var content = BuildWorkflowYaml(freshSettings);
            return PlanWrite(path, existingContent, content, options.Force);
        }

        var allowUpgrade = options.Upgrade || !options.Force;
        if (!allowUpgrade) {
            return FilePlan.Skip(path, "Already exists (use --upgrade or --force)");
        }

        var managedBlock = TryExtractManagedBlock(existingContent);
        if (managedBlock is null && !IsIntelligenceXWorkflow(existingContent) && !options.Force) {
            return FilePlan.Skip(path, "Existing workflow not recognized (use --force to overwrite)");
        }

        var snapshotSource = managedBlock ?? existingContent;
        var settings = ResolveWorkflowSettings(options, snapshotSource);
        var updatedBlock = BuildManagedWorkflowBlock(settings, indent: 2);
        var upgraded = managedBlock is null
            ? BuildWorkflowYaml(settings)
            : ReplaceManagedBlock(existingContent, updatedBlock) ?? BuildWorkflowYaml(settings);
        return PlanWrite(path, existingContent, upgraded, options.Force);
    }

    private static FilePlan PlanConfigChange(SetupOptions options, string? existingContent) {
        var path = ".intelligencex/config.json";
        var overrideContent = ReadConfigOverride(options);
        if (!string.IsNullOrWhiteSpace(overrideContent)) {
            return PlanWrite(path, existingContent, overrideContent, options.Force);
        }
        var settings = ResolveConfigSettings(options, existingContent, out var parsed);
        if (!parsed && !options.Force && !string.IsNullOrWhiteSpace(existingContent)) {
            return FilePlan.Skip(path, "Config exists but could not be parsed (use --force to overwrite)");
        }

        var content = !string.IsNullOrWhiteSpace(existingContent) && parsed
            ? MergeConfigJson(existingContent, settings)
            : BuildConfigJson(settings);
        return PlanWrite(path, existingContent, content, options.Force);
    }

    private static string? ReadConfigOverride(SetupOptions options) {
        if (!string.IsNullOrWhiteSpace(options.ConfigJson)) {
            return options.ConfigJson;
        }
        if (!string.IsNullOrWhiteSpace(options.ConfigPath)) {
            try {
                return File.ReadAllText(options.ConfigPath);
            } catch {
                return null;
            }
        }
        return null;
    }

    private static FilePlan PlanWrite(string path, string? existingContent, string content, bool force) {
        if (string.IsNullOrWhiteSpace(existingContent)) {
            return new FilePlan(path, "create", content);
        }
        if (NormalizeLineEndings(existingContent) == NormalizeLineEndings(content)) {
            return FilePlan.Skip(path, "no changes");
        }
        return new FilePlan(path, force ? "overwrite" : "update", content);
    }

    private static WorkflowSettings ResolveWorkflowSettings(SetupOptions options, string? existingManagedBlock) {
        var settings = WorkflowSettings.FromOptions(options);
        if (string.IsNullOrWhiteSpace(existingManagedBlock)) {
            return settings;
        }

        if (TryReadWorkflowSnapshot(existingManagedBlock, out var snapshot)) {
            if (!options.ActionsRepoSet && !string.IsNullOrWhiteSpace(snapshot.ActionsRepo)) {
                settings.ActionsRepo = NormalizeActionsRepo(snapshot.ActionsRepo!);
            }
            if (!options.ActionsRefSet && !string.IsNullOrWhiteSpace(snapshot.ActionsRef)) {
                settings.ActionsRef = snapshot.ActionsRef!;
            }
            if (!options.RunsOnSet && !string.IsNullOrWhiteSpace(snapshot.RunsOn)) {
                settings.RunsOn = snapshot.RunsOn!;
            }
            if (!options.ReviewerSourceSet && !string.IsNullOrWhiteSpace(snapshot.ReviewerSource)) {
                settings.ReviewerSource = snapshot.ReviewerSource!;
            }
            if (!options.ReviewerReleaseRepoSet && !string.IsNullOrWhiteSpace(snapshot.ReviewerReleaseRepo)) {
                settings.ReviewerReleaseRepo = snapshot.ReviewerReleaseRepo!;
            }
            if (!options.ReviewerReleaseTagSet && !string.IsNullOrWhiteSpace(snapshot.ReviewerReleaseTag)) {
                settings.ReviewerReleaseTag = snapshot.ReviewerReleaseTag!;
            }
            if (!options.ReviewerReleaseAssetSet && snapshot.ReviewerReleaseAsset is not null) {
                settings.ReviewerReleaseAsset = snapshot.ReviewerReleaseAsset;
            }
            if (!options.ReviewerReleaseUrlSet && snapshot.ReviewerReleaseUrl is not null) {
                settings.ReviewerReleaseUrl = snapshot.ReviewerReleaseUrl;
            }
            if (!options.ProviderSet && !string.IsNullOrWhiteSpace(snapshot.Provider)) {
                settings.Provider = snapshot.Provider!;
            }
            if (!options.OpenAIModelSet && !string.IsNullOrWhiteSpace(snapshot.Model)) {
                settings.Model = snapshot.Model!;
            }
            if (!options.OpenAITransportSet && !string.IsNullOrWhiteSpace(snapshot.OpenAITransport)) {
                settings.OpenAITransport = snapshot.OpenAITransport!;
            }
            if (!options.IncludeIssueCommentsSet && snapshot.IncludeIssueComments.HasValue) {
                settings.IncludeIssueComments = snapshot.IncludeIssueComments.Value;
            }
            if (!options.IncludeReviewCommentsSet && snapshot.IncludeReviewComments.HasValue) {
                settings.IncludeReviewComments = snapshot.IncludeReviewComments.Value;
            }
            if (!options.IncludeRelatedPullRequestsSet && snapshot.IncludeRelatedPullRequests.HasValue) {
                settings.IncludeRelatedPullRequests = snapshot.IncludeRelatedPullRequests.Value;
            }
            if (!options.ProgressUpdatesSet && snapshot.ProgressUpdates.HasValue) {
                settings.ProgressUpdates = snapshot.ProgressUpdates.Value;
            }
            if (!options.DiagnosticsSet && snapshot.Diagnostics.HasValue) {
                settings.Diagnostics = snapshot.Diagnostics.Value;
            }
            if (!options.PreflightSet && snapshot.Preflight.HasValue) {
                settings.Preflight = snapshot.Preflight.Value;
            }
            if (!options.PreflightTimeoutSecondsSet && snapshot.PreflightTimeoutSeconds.HasValue) {
                settings.PreflightTimeoutSeconds = snapshot.PreflightTimeoutSeconds.Value;
            }
            if (!options.CleanupEnabledSet && snapshot.CleanupEnabled.HasValue) {
                settings.CleanupEnabled = snapshot.CleanupEnabled.Value;
            }
            if (!options.CleanupModeSet && !string.IsNullOrWhiteSpace(snapshot.CleanupMode)) {
                settings.CleanupMode = snapshot.CleanupMode!;
            }
            if (!options.CleanupScopeSet && !string.IsNullOrWhiteSpace(snapshot.CleanupScope)) {
                settings.CleanupScope = snapshot.CleanupScope!;
            }
            if (!options.CleanupRequireLabelSet && snapshot.CleanupRequireLabel is not null) {
                settings.CleanupRequireLabel = snapshot.CleanupRequireLabel;
            }
            if (!options.CleanupMinConfidenceSet && snapshot.CleanupMinConfidence.HasValue) {
                settings.CleanupMinConfidence = snapshot.CleanupMinConfidence.Value;
            }
            if (!options.CleanupAllowedEditsSet && snapshot.CleanupAllowedEdits is not null) {
                settings.CleanupAllowedEdits = snapshot.CleanupAllowedEdits;
            }
            if (!options.CleanupPostEditCommentSet && snapshot.CleanupPostEditComment.HasValue) {
                settings.CleanupPostEditComment = snapshot.CleanupPostEditComment.Value;
            }
        }

        return settings;
    }

    private static ConfigSettings ResolveConfigSettings(SetupOptions options, string? existingContent, out bool parsed) {
        parsed = false;
        var settings = ConfigSettings.FromOptions(options);
        if (string.IsNullOrWhiteSpace(existingContent)) {
            return settings;
        }

        if (!TryReadConfigSnapshot(existingContent, out var snapshot)) {
            return settings;
        }

        parsed = true;
        if (!options.ProviderSet && !string.IsNullOrWhiteSpace(snapshot.Provider)) {
            settings.Provider = snapshot.Provider!;
        }
        if (!options.OpenAITransportSet && !string.IsNullOrWhiteSpace(snapshot.OpenAITransport)) {
            settings.OpenAITransport = snapshot.OpenAITransport!;
        }
        if (!options.OpenAIModelSet && !string.IsNullOrWhiteSpace(snapshot.OpenAIModel)) {
            settings.OpenAIModel = snapshot.OpenAIModel!;
        }
        if (!options.ReviewProfileSet && !string.IsNullOrWhiteSpace(snapshot.Profile)) {
            settings.Profile = snapshot.Profile!;
        }
        if (!options.ReviewModeSet && !string.IsNullOrWhiteSpace(snapshot.Mode)) {
            settings.Mode = snapshot.Mode!;
        }
        if (!options.ReviewCommentModeSet && !string.IsNullOrWhiteSpace(snapshot.CommentMode)) {
            settings.CommentMode = snapshot.CommentMode!;
        }
        if (!options.IncludeIssueCommentsSet && snapshot.IncludeIssueComments.HasValue) {
            settings.IncludeIssueComments = snapshot.IncludeIssueComments.Value;
        }
        if (!options.IncludeReviewCommentsSet && snapshot.IncludeReviewComments.HasValue) {
            settings.IncludeReviewComments = snapshot.IncludeReviewComments.Value;
        }
        if (!options.IncludeRelatedPullRequestsSet && snapshot.IncludeRelatedPullRequests.HasValue) {
            settings.IncludeRelatedPullRequests = snapshot.IncludeRelatedPullRequests.Value;
        }
        if (!options.ProgressUpdatesSet && snapshot.ProgressUpdates.HasValue) {
            settings.ProgressUpdates = snapshot.ProgressUpdates.Value;
        }
        if (!options.DiagnosticsSet && snapshot.Diagnostics.HasValue) {
            settings.Diagnostics = snapshot.Diagnostics.Value;
        }
        if (!options.PreflightSet && snapshot.Preflight.HasValue) {
            settings.Preflight = snapshot.Preflight.Value;
        }
        if (!options.PreflightTimeoutSecondsSet && snapshot.PreflightTimeoutSeconds.HasValue) {
            settings.PreflightTimeoutSeconds = snapshot.PreflightTimeoutSeconds.Value;
        }

        return settings;
    }

    private static string BuildConfigJson(ConfigSettings settings) {
        var root = new JsonObject {
            ["review"] = new JsonObject {
                ["provider"] = settings.Provider,
                ["openaiTransport"] = settings.OpenAITransport,
                ["openaiModel"] = settings.OpenAIModel,
                ["profile"] = settings.Profile,
                ["mode"] = settings.Mode,
                ["commentMode"] = settings.CommentMode,
                ["includeIssueComments"] = settings.IncludeIssueComments,
                ["includeReviewComments"] = settings.IncludeReviewComments,
                ["includeRelatedPullRequests"] = settings.IncludeRelatedPullRequests,
                ["progressUpdates"] = settings.ProgressUpdates,
                ["diagnostics"] = settings.Diagnostics,
                ["preflight"] = settings.Preflight,
                ["preflightTimeoutSeconds"] = settings.PreflightTimeoutSeconds
            }
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string MergeConfigJson(string existingContent, ConfigSettings settings) {
        var node = JsonNode.Parse(existingContent) as JsonObject ?? new JsonObject();
        var review = node["review"] as JsonObject ?? new JsonObject();
        review["provider"] = settings.Provider;
        review["openaiTransport"] = settings.OpenAITransport;
        review["openaiModel"] = settings.OpenAIModel;
        review["profile"] = settings.Profile;
        review["mode"] = settings.Mode;
        review["commentMode"] = settings.CommentMode;
        review["includeIssueComments"] = settings.IncludeIssueComments;
        review["includeReviewComments"] = settings.IncludeReviewComments;
        review["includeRelatedPullRequests"] = settings.IncludeRelatedPullRequests;
        review["progressUpdates"] = settings.ProgressUpdates;
        review["diagnostics"] = settings.Diagnostics;
        review["preflight"] = settings.Preflight;
        review["preflightTimeoutSeconds"] = settings.PreflightTimeoutSeconds;
        node["review"] = review;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildWorkflowYaml(WorkflowSettings settings) {
        var template = ReadEmbeddedResource("review-intelligencex.yml");
        var managed = BuildManagedWorkflowBlock(settings, indent: 2).TrimEnd();
        return template.Replace("{{ManagedBlock}}", managed, StringComparison.Ordinal);
    }

    private static string BuildManagedWorkflowBlock(WorkflowSettings settings, int indent) {
        var template = settings.ExplicitSecrets
            ? ReadEmbeddedResource("review-intelligencex.managed.explicit.yml")
            : ReadEmbeddedResource("review-intelligencex.managed.yml");
        var tokens = new Dictionary<string, string> {
            ["ActionsRepo"] = settings.ActionsRepo,
            ["ActionsRef"] = settings.ActionsRef,
            ["RunsOn"] = NormalizeRunsOn(settings.RunsOn),
            ["ReviewerSource"] = settings.ReviewerSource,
            ["ReviewerReleaseRepo"] = settings.ReviewerReleaseRepo,
            ["ReviewerReleaseTag"] = settings.ReviewerReleaseTag,
            ["ReviewerReleaseAssetLine"] = string.IsNullOrWhiteSpace(settings.ReviewerReleaseAsset)
                ? string.Empty
                : $"reviewer_release_asset: {YamlQuote(settings.ReviewerReleaseAsset)}",
            ["ReviewerReleaseUrlLine"] = string.IsNullOrWhiteSpace(settings.ReviewerReleaseUrl)
                ? string.Empty
                : $"reviewer_release_url: {YamlQuote(settings.ReviewerReleaseUrl)}",
            ["Provider"] = settings.Provider,
            ["Model"] = settings.Model,
            ["OpenAITransport"] = settings.OpenAITransport,
            ["IncludeIssueComments"] = ToYamlBool(settings.IncludeIssueComments),
            ["IncludeReviewComments"] = ToYamlBool(settings.IncludeReviewComments),
            ["IncludeRelatedPullRequests"] = ToYamlBool(settings.IncludeRelatedPullRequests),
            ["ProgressUpdates"] = ToYamlBool(settings.ProgressUpdates),
            ["Diagnostics"] = ToYamlBool(settings.Diagnostics),
            ["Preflight"] = ToYamlBool(settings.Preflight),
            ["PreflightTimeoutSeconds"] = settings.PreflightTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["CleanupEnabled"] = ToYamlBool(settings.CleanupEnabled),
            ["CleanupMode"] = settings.CleanupMode,
            ["CleanupScope"] = settings.CleanupScope,
            ["CleanupRequireLabel"] = YamlQuote(settings.CleanupRequireLabel),
            ["CleanupMinConfidence"] = settings.CleanupMinConfidence.ToString(CultureInfo.InvariantCulture),
            ["CleanupAllowedEdits"] = YamlQuote(settings.CleanupAllowedEdits),
            ["CleanupPostEditComment"] = ToYamlBool(settings.CleanupPostEditComment)
        };

        var block = ReplaceTokens(template, tokens);
        return IndentBlock(block, indent);
    }

    private static string ReplaceTokens(string template, IReadOnlyDictionary<string, string> tokens) {
        var result = template;
        foreach (var pair in tokens) {
            result = result.Replace("{{" + pair.Key + "}}", pair.Value ?? string.Empty, StringComparison.Ordinal);
        }
        return result;
    }

    private static string IndentBlock(string content, int indent) {
        if (indent <= 0) {
            return content;
        }
        var normalized = NormalizeLineEndings(content);
        var lines = normalized.Split('\n');
        var pad = new string(' ', indent);
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].Length > 0) {
                lines[i] = pad + lines[i];
            }
        }
        return string.Join("\n", lines);
    }

    private static string ToYamlBool(bool value) => value ? "true" : "false";

    private static string YamlQuote(string? value) {
        var raw = value ?? string.Empty;
        var escaped = raw.Replace("'", "''", StringComparison.Ordinal);
        return $"'{escaped}'";
    }

    private static string ReadEmbeddedResource(string name) {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) {
            throw new InvalidOperationException($"Embedded template not found: {name}");
        }
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string NormalizeRunsOn(string? value) {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "[\"self-hosted\",\"ubuntu\"]" : value.Trim();
        if (trimmed.StartsWith("'") && trimmed.EndsWith("'")) {
            return trimmed;
        }
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) {
            return trimmed;
        }
        return $"'{trimmed}'";
    }

    private static string NormalizeActionsRepo(string value) {
        var trimmed = value.Trim().TrimEnd('/');
        var marker = "/.github/workflows/";
        var index = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index > 0) {
            trimmed = trimmed.Substring(0, index);
        }
        return trimmed.TrimEnd('/');
    }

    private static string? TryExtractManagedBlock(string content) {
        var pattern = @"^[ \t]*# INTELLIGENCEX:BEGIN[\s\S]*?^[ \t]*# INTELLIGENCEX:END[ \t]*\r?$";
        var match = Regex.Match(content, pattern, RegexOptions.Multiline);
        return match.Success ? match.Value : null;
    }

    private static string? ReplaceManagedBlock(string content, string newBlock) {
        var pattern = @"^[ \t]*# INTELLIGENCEX:BEGIN[\s\S]*?^[ \t]*# INTELLIGENCEX:END[ \t]*\r?$";
        if (!Regex.IsMatch(content, pattern, RegexOptions.Multiline)) {
            return null;
        }
        return Regex.Replace(content, pattern, newBlock.TrimEnd(), RegexOptions.Multiline);
    }

    private static bool TryReadWorkflowSnapshot(string content, out WorkflowSnapshot snapshot) {
        snapshot = new WorkflowSnapshot();
        var match = Regex.Match(content, @"^\s*uses:\s*([^\s@]+)@([^\s]+)\s*$", RegexOptions.Multiline);
        if (match.Success) {
            snapshot.ActionsRepo = NormalizeActionsRepo(match.Groups[1].Value.Trim());
            snapshot.ActionsRef = match.Groups[2].Value.Trim();
        }

        snapshot.RunsOn = ReadYamlScalar(content, "runs_on");
        snapshot.ReviewerSource = ReadYamlScalar(content, "reviewer_source");
        snapshot.ReviewerReleaseRepo = ReadYamlScalar(content, "reviewer_release_repo");
        snapshot.ReviewerReleaseTag = ReadYamlScalar(content, "reviewer_release_tag");
        snapshot.ReviewerReleaseAsset = ReadYamlScalar(content, "reviewer_release_asset");
        snapshot.ReviewerReleaseUrl = ReadYamlScalar(content, "reviewer_release_url");
        snapshot.Provider = ReadYamlScalar(content, "provider");
        snapshot.Model = ReadYamlScalar(content, "model");
        snapshot.OpenAITransport = ReadYamlScalar(content, "openai_transport");
        snapshot.IncludeIssueComments = ReadYamlBool(content, "include_issue_comments");
        snapshot.IncludeReviewComments = ReadYamlBool(content, "include_review_comments");
        snapshot.IncludeRelatedPullRequests = ReadYamlBool(content, "include_related_prs");
        snapshot.ProgressUpdates = ReadYamlBool(content, "progress_updates");
        snapshot.Diagnostics = ReadYamlBool(content, "diagnostics");
        snapshot.Preflight = ReadYamlBool(content, "preflight");
        snapshot.PreflightTimeoutSeconds = ReadYamlInt(content, "preflight_timeout_seconds");
        snapshot.CleanupEnabled = ReadYamlBool(content, "cleanup_enabled");
        snapshot.CleanupMode = ReadYamlScalar(content, "cleanup_mode");
        snapshot.CleanupScope = ReadYamlScalar(content, "cleanup_scope");
        snapshot.CleanupRequireLabel = ReadYamlScalar(content, "cleanup_require_label");
        snapshot.CleanupMinConfidence = ReadYamlDouble(content, "cleanup_min_confidence");
        snapshot.CleanupAllowedEdits = ReadYamlScalar(content, "cleanup_allowed_edits");
        snapshot.CleanupPostEditComment = ReadYamlBool(content, "cleanup_post_edit_comment");

        return snapshot.HasAny;
    }

    private static string? ReadYamlScalar(string content, string key) {
        var pattern = @"^\s*" + Regex.Escape(key) + @"\s*:\s*(.+?)\s*$";
        var match = Regex.Match(content, pattern, RegexOptions.Multiline);
        if (!match.Success) {
            return null;
        }
        var value = match.Groups[1].Value.Trim();
        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
        value = commentIndex >= 0 ? value.Substring(0, commentIndex).Trim() : value;
        if (value.Length >= 2 &&
            ((value.StartsWith('\'') && value.EndsWith('\'')) || (value.StartsWith('"') && value.EndsWith('"')))) {
            value = value.Substring(1, value.Length - 2);
            value = value.Replace("''", "'", StringComparison.Ordinal);
        }
        return value;
    }

    private static bool? ReadYamlBool(string content, string key) {
        var value = ReadYamlScalar(content, key);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return value.Trim().ToLowerInvariant() switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => null
        };
    }

    private static double? ReadYamlDouble(string content, string key) {
        var value = ReadYamlScalar(content, key);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) {
            return result;
        }
        return null;
    }

    private static int? ReadYamlInt(string content, string key) {
        var value = ReadYamlScalar(content, key);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) {
            return result;
        }
        return null;
    }

    private static bool TryReadConfigSnapshot(string content, out ConfigSnapshot snapshot) {
        snapshot = new ConfigSnapshot();
        try {
            var root = JsonNode.Parse(content) as JsonObject;
            if (root is null) {
                return false;
            }
            var review = root["review"] as JsonObject;
                if (review is not null) {
                    snapshot.Provider = ReadJsonString(review, "provider");
                    snapshot.OpenAITransport = ReadJsonString(review, "openaiTransport");
                    snapshot.OpenAIModel = ReadJsonString(review, "openaiModel");
                    snapshot.Profile = ReadJsonString(review, "profile");
                    snapshot.Mode = ReadJsonString(review, "mode");
                    snapshot.CommentMode = ReadJsonString(review, "commentMode");
                    snapshot.IncludeIssueComments = ReadJsonBool(review, "includeIssueComments");
                    snapshot.IncludeReviewComments = ReadJsonBool(review, "includeReviewComments");
                    snapshot.IncludeRelatedPullRequests = ReadJsonBool(review, "includeRelatedPullRequests");
                    snapshot.ProgressUpdates = ReadJsonBool(review, "progressUpdates");
                    snapshot.Diagnostics = ReadJsonBool(review, "diagnostics");
                    snapshot.Preflight = ReadJsonBool(review, "preflight");
                    snapshot.PreflightTimeoutSeconds = ReadJsonInt(review, "preflightTimeoutSeconds");
                }
            return true;
        } catch {
            return false;
        }
    }

    private static string? ReadJsonString(JsonObject obj, string key) {
        return obj.TryGetPropertyValue(key, out var value) ? value?.GetValue<string>() : null;
    }

    private static bool? ReadJsonBool(JsonObject obj, string key) {
        if (!obj.TryGetPropertyValue(key, out var value) || value is null) {
            return null;
        }
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var result)) {
            return result;
        }
        return null;
    }

    private static int? ReadJsonInt(JsonObject obj, string key) {
        if (!obj.TryGetPropertyValue(key, out var value) || value is null) {
            return null;
        }
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var result)) {
            return result;
        }
        return null;
    }

    private static string NormalizeLineEndings(string value) {
        return value.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static bool IsIntelligenceXWorkflow(string content) {
        if (string.IsNullOrWhiteSpace(content)) {
            return false;
        }
        return content.IndexOf("review-intelligencex.yml", StringComparison.OrdinalIgnoreCase) >= 0 ||
               content.IndexOf("IntelligenceX Review", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Prompt(string label) {
        Console.Write(label + ": ");
        return Console.ReadLine() ?? string.Empty;
    }

    private static void WriteHelp() {
        Console.WriteLine("IntelligenceX setup");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>");
        Console.WriteLine("  --github-client-id <id>");
        Console.WriteLine("  --github-token <token>");
        Console.WriteLine("  --github-api-base-url <url> (default https://api.github.com)");
        Console.WriteLine("  --github-auth-base-url <url> (default https://github.com)");
        Console.WriteLine("  --actions-repo <owner/repo> (default evotecit/github-actions)");
        Console.WriteLine("  --actions-ref <ref> (default master)");
        Console.WriteLine("  --runs-on <json-array> (default [\"self-hosted\",\"ubuntu\"])");
        Console.WriteLine("  --reviewer-source <source|release> (default release)");
        Console.WriteLine("  --reviewer-release-repo <owner/repo> (default EvotecIT/github-actions)");
        Console.WriteLine("  --reviewer-release-tag <tag> (default latest)");
        Console.WriteLine("  --reviewer-release-asset <name>");
        Console.WriteLine("  --reviewer-release-url <url>");
        Console.WriteLine("  --provider <openai|copilot> (default openai)");
        Console.WriteLine("  --openai-model <model>");
        Console.WriteLine("  --openai-transport <native|appserver>");
        Console.WriteLine("  --include-issue-comments <true|false>");
        Console.WriteLine("  --include-review-comments <true|false>");
        Console.WriteLine("  --include-related-prs <true|false>");
        Console.WriteLine("  --progress-updates <true|false>");
        Console.WriteLine("  --review-profile <balanced|picky|highlevel|security|performance|tests|minimal>");
        Console.WriteLine("  --review-mode <hybrid|summary|inline>");
        Console.WriteLine("  --review-comment-mode <sticky|fresh>");
        Console.WriteLine("  --config-path <path> (use custom config.json content)");
        Console.WriteLine("  --config-json <json> (use inline config.json content)");
        Console.WriteLine("  --auth-b64 <value> (use pre-exported auth bundle)");
        Console.WriteLine("  --auth-b64-path <path> (read pre-exported auth bundle)");
        Console.WriteLine("  --diagnostics <true|false>");
        Console.WriteLine("  --preflight <true|false>");
        Console.WriteLine("  --preflight-timeout-seconds <number>");
        Console.WriteLine("  --cleanup-enabled <true|false>");
        Console.WriteLine("  --cleanup-mode <comment|edit|hybrid>");
        Console.WriteLine("  --cleanup-scope <pr|issue|both>");
        Console.WriteLine("  --cleanup-require-label <label>");
        Console.WriteLine("  --cleanup-min-confidence <0-1>");
        Console.WriteLine("  --cleanup-allowed-edits <comma-list>");
        Console.WriteLine("  --cleanup-post-edit-comment <true|false>");
        Console.WriteLine("  --with-config (also write .intelligencex/config.json)");
        Console.WriteLine("  --upgrade (update managed sections instead of skipping)");
        Console.WriteLine("  --update-secret (refresh INTELLIGENCEX_AUTH_B64 only)");
        Console.WriteLine("  --skip-secret (skip secret update during setup)");
        Console.WriteLine("  --manual-secret (print secret instead of uploading)");
        Console.WriteLine("  --cleanup (remove workflow/config and optionally secret)");
        Console.WriteLine("  --keep-secret (do not delete secret during cleanup)");
        Console.WriteLine("  --branch <name>");
        Console.WriteLine("  --force (overwrite existing files)");
        Console.WriteLine("  --dry-run (show changes only)");
        Console.WriteLine("  --explicit-secrets (use explicit secrets block in workflow)");
        Console.WriteLine("  --help");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_CLIENT_ID");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_TOKEN");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_API_BASE_URL");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_AUTH_BASE_URL");
    }

    private static bool ParseBool(string value, bool fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        return value.Trim().ToLowerInvariant() switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => fallback
        };
    }

    private static double ParseDouble(string value, double fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }
        return fallback;
    }

    private static int ParseInt(string value, int fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }
        return fallback;
    }
}

