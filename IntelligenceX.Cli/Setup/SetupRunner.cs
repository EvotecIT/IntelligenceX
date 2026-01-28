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

internal static class SetupRunner {
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
                state.OpenAI.AuthBundle = await LoginOpenAiAsync(options).ConfigureAwait(false);
                state.OpenAI.AuthJson = AuthBundleSerializer.Serialize(state.OpenAI.AuthBundle);
                state.OpenAI.AuthB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(state.OpenAI.AuthJson));

                await github.SetSecretAsync(owner, repo, "INTELLIGENCEX_AUTH_B64", state.OpenAI.AuthB64)
                    .ConfigureAwait(false);
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

        state.OpenAI.AuthBundle = await LoginOpenAiAsync(options).ConfigureAwait(false);
        state.OpenAI.AuthJson = AuthBundleSerializer.Serialize(state.OpenAI.AuthBundle);
        state.OpenAI.AuthB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(state.OpenAI.AuthJson));

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
        return options.SkipSecret ? "skip" : "create/update";
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
        var settings = ResolveConfigSettings(options, existingContent, out var parsed);
        if (!parsed && !options.Force && !string.IsNullOrWhiteSpace(existingContent)) {
            return FilePlan.Skip(path, "Config exists but could not be parsed (use --force to overwrite)");
        }

        var content = !string.IsNullOrWhiteSpace(existingContent) && parsed
            ? MergeConfigJson(existingContent, settings)
            : BuildConfigJson(settings);
        return PlanWrite(path, existingContent, content, options.Force);
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
        if (!string.IsNullOrWhiteSpace(snapshot.Profile)) {
            settings.Profile = snapshot.Profile!;
        }
        if (!string.IsNullOrWhiteSpace(snapshot.Mode)) {
            settings.Mode = snapshot.Mode!;
        }
        if (!string.IsNullOrWhiteSpace(snapshot.CommentMode)) {
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
                ["progressUpdates"] = settings.ProgressUpdates
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
        node["review"] = review;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildWorkflowYaml(WorkflowSettings settings) {
        var template = ReadEmbeddedResource("review-intelligencex.yml");
        var managed = BuildManagedWorkflowBlock(settings, indent: 2).TrimEnd();
        return template.Replace("{{ManagedBlock}}", managed, StringComparison.Ordinal);
    }

    private static string BuildManagedWorkflowBlock(WorkflowSettings settings, int indent) {
        var template = ReadEmbeddedResource("review-intelligencex.managed.yml");
        var tokens = new Dictionary<string, string> {
            ["ActionsRepo"] = settings.ActionsRepo,
            ["ActionsRef"] = settings.ActionsRef,
            ["RunsOn"] = NormalizeRunsOn(settings.RunsOn),
            ["ReviewerSource"] = settings.ReviewerSource,
            ["ReviewerReleaseRepo"] = settings.ReviewerReleaseRepo,
            ["ReviewerReleaseTag"] = settings.ReviewerReleaseTag,
            ["ReviewerReleaseAsset"] = YamlQuote(settings.ReviewerReleaseAsset),
            ["ReviewerReleaseUrl"] = YamlQuote(settings.ReviewerReleaseUrl),
            ["Provider"] = settings.Provider,
            ["Model"] = settings.Model,
            ["OpenAITransport"] = settings.OpenAITransport,
            ["IncludeIssueComments"] = ToYamlBool(settings.IncludeIssueComments),
            ["IncludeReviewComments"] = ToYamlBool(settings.IncludeReviewComments),
            ["IncludeRelatedPullRequests"] = ToYamlBool(settings.IncludeRelatedPullRequests),
            ["ProgressUpdates"] = ToYamlBool(settings.ProgressUpdates),
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
        Console.WriteLine("  --cleanup (remove workflow/config and optionally secret)");
        Console.WriteLine("  --keep-secret (do not delete secret during cleanup)");
        Console.WriteLine("  --branch <name>");
        Console.WriteLine("  --force (overwrite existing files)");
        Console.WriteLine("  --dry-run (show changes only)");
        Console.WriteLine("  --help");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_CLIENT_ID");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_TOKEN");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_API_BASE_URL");
        Console.WriteLine("  INTELLIGENCEX_GITHUB_AUTH_BASE_URL");
    }

    private sealed class SetupOptions {
        public string? RepoFullName { get; set; }
        public string? GitHubClientId { get; set; }
        public string? GitHubToken { get; set; }
        public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
        public string GitHubAuthBaseUrl { get; set; } = "https://github.com";
        public string GitHubScopes { get; set; } = "repo workflow read:org";
        public string? ActionsRepo { get; set; } = "evotecit/github-actions";
        public string? ActionsRef { get; set; } = "master";
        public string? RunsOn { get; set; } = "[\"self-hosted\",\"ubuntu\"]";
        public string? Provider { get; set; } = "openai";
        public string? OpenAIModel { get; set; } = "gpt-5.2-codex";
        public string? OpenAITransport { get; set; } = "native";
        public string? ReviewerSource { get; set; } = "release";
        public string? ReviewerReleaseRepo { get; set; } = "EvotecIT/github-actions";
        public string? ReviewerReleaseTag { get; set; } = "latest";
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public bool CleanupEnabled { get; set; }
        public string? CleanupMode { get; set; } = "comment";
        public string? CleanupScope { get; set; } = "pr";
        public string? CleanupRequireLabel { get; set; } = string.Empty;
        public double CleanupMinConfidence { get; set; } = 0.85;
        public string? CleanupAllowedEdits { get; set; } = "formatting,grammar,title,sections";
        public bool CleanupPostEditComment { get; set; } = true;
        public string? BranchName { get; set; }
        public bool WithConfig { get; set; }
        public bool Upgrade { get; set; }
        public bool Cleanup { get; set; }
        public bool UpdateSecret { get; set; }
        public bool SkipSecret { get; set; }
        public bool KeepSecret { get; set; }
        public bool Force { get; set; }
        public bool DryRun { get; set; }
        public bool ShowHelp { get; set; }
        public bool ActionsRepoSet { get; set; }
        public bool ActionsRefSet { get; set; }
        public bool RunsOnSet { get; set; }
        public bool ProviderSet { get; set; }
        public bool OpenAIModelSet { get; set; }
        public bool OpenAITransportSet { get; set; }
        public bool ReviewerSourceSet { get; set; }
        public bool ReviewerReleaseRepoSet { get; set; }
        public bool ReviewerReleaseTagSet { get; set; }
        public bool ReviewerReleaseAssetSet { get; set; }
        public bool ReviewerReleaseUrlSet { get; set; }
        public bool IncludeIssueCommentsSet { get; set; }
        public bool IncludeReviewCommentsSet { get; set; }
        public bool IncludeRelatedPullRequestsSet { get; set; }
        public bool ProgressUpdatesSet { get; set; }
        public bool CleanupEnabledSet { get; set; }
        public bool CleanupModeSet { get; set; }
        public bool CleanupScopeSet { get; set; }
        public bool CleanupRequireLabelSet { get; set; }
        public bool CleanupMinConfidenceSet { get; set; }
        public bool CleanupAllowedEditsSet { get; set; }
        public bool CleanupPostEditCommentSet { get; set; }

        public static SetupOptions Parse(string[] args) {
            var options = new SetupOptions {
                GitHubClientId = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_CLIENT_ID"),
                GitHubToken = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN"),
                GitHubApiBaseUrl = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_API_BASE_URL") ?? "https://api.github.com",
                GitHubAuthBaseUrl = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_AUTH_BASE_URL") ?? "https://github.com"
            };

            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                    continue;
                }
                var key = arg.Substring(2);
                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";

                switch (key) {
                    case "repo":
                        options.RepoFullName = value;
                        break;
                    case "github-client-id":
                        options.GitHubClientId = value;
                        break;
                    case "github-token":
                        options.GitHubToken = value;
                        break;
                    case "github-api-base-url":
                        options.GitHubApiBaseUrl = value;
                        break;
                    case "github-auth-base-url":
                        options.GitHubAuthBaseUrl = value;
                        break;
                    case "actions-repo":
                        options.ActionsRepo = value;
                        options.ActionsRepoSet = true;
                        break;
                    case "actions-ref":
                        options.ActionsRef = value;
                        options.ActionsRefSet = true;
                        break;
                    case "runs-on":
                        options.RunsOn = value;
                        options.RunsOnSet = true;
                        break;
                    case "provider":
                        options.Provider = value;
                        options.ProviderSet = true;
                        break;
                    case "openai-model":
                        options.OpenAIModel = value;
                        options.OpenAIModelSet = true;
                        break;
                    case "openai-transport":
                        options.OpenAITransport = value;
                        options.OpenAITransportSet = true;
                        break;
                    case "reviewer-source":
                        options.ReviewerSource = value;
                        options.ReviewerSourceSet = true;
                        break;
                    case "reviewer-release-repo":
                        options.ReviewerReleaseRepo = value;
                        options.ReviewerReleaseRepoSet = true;
                        break;
                    case "reviewer-release-tag":
                        options.ReviewerReleaseTag = value;
                        options.ReviewerReleaseTagSet = true;
                        break;
                    case "reviewer-release-asset":
                        options.ReviewerReleaseAsset = value;
                        options.ReviewerReleaseAssetSet = true;
                        break;
                    case "reviewer-release-url":
                        options.ReviewerReleaseUrl = value;
                        options.ReviewerReleaseUrlSet = true;
                        break;
                    case "include-issue-comments":
                        options.IncludeIssueComments = ParseBool(value, options.IncludeIssueComments);
                        options.IncludeIssueCommentsSet = true;
                        break;
                    case "include-review-comments":
                        options.IncludeReviewComments = ParseBool(value, options.IncludeReviewComments);
                        options.IncludeReviewCommentsSet = true;
                        break;
                    case "include-related-prs":
                        options.IncludeRelatedPullRequests = ParseBool(value, options.IncludeRelatedPullRequests);
                        options.IncludeRelatedPullRequestsSet = true;
                        break;
                    case "progress-updates":
                        options.ProgressUpdates = ParseBool(value, options.ProgressUpdates);
                        options.ProgressUpdatesSet = true;
                        break;
                    case "cleanup-enabled":
                        options.CleanupEnabled = ParseBool(value, options.CleanupEnabled);
                        options.CleanupEnabledSet = true;
                        break;
                    case "cleanup-mode":
                        options.CleanupMode = value;
                        options.CleanupModeSet = true;
                        break;
                    case "cleanup-scope":
                        options.CleanupScope = value;
                        options.CleanupScopeSet = true;
                        break;
                    case "cleanup-require-label":
                        options.CleanupRequireLabel = value;
                        options.CleanupRequireLabelSet = true;
                        break;
                    case "cleanup-min-confidence":
                        options.CleanupMinConfidence = ParseDouble(value, options.CleanupMinConfidence);
                        options.CleanupMinConfidenceSet = true;
                        break;
                    case "cleanup-allowed-edits":
                        options.CleanupAllowedEdits = value;
                        options.CleanupAllowedEditsSet = true;
                        break;
                    case "cleanup-post-edit-comment":
                        options.CleanupPostEditComment = ParseBool(value, options.CleanupPostEditComment);
                        options.CleanupPostEditCommentSet = true;
                        break;
                    case "with-config":
                        options.WithConfig = ParseBool(value, true);
                        break;
                    case "upgrade":
                        options.Upgrade = ParseBool(value, true);
                        break;
                    case "cleanup":
                        options.Cleanup = ParseBool(value, true);
                        break;
                    case "update-secret":
                        options.UpdateSecret = ParseBool(value, true);
                        break;
                    case "skip-secret":
                        options.SkipSecret = ParseBool(value, true);
                        break;
                    case "keep-secret":
                        options.KeepSecret = ParseBool(value, true);
                        break;
                    case "branch":
                        options.BranchName = value;
                        break;
                    case "force":
                        options.Force = ParseBool(value, true);
                        break;
                    case "dry-run":
                        options.DryRun = ParseBool(value, true);
                        break;
                    case "help":
                        options.ShowHelp = true;
                        break;
                }
            }

            return options;
        }
    }

    private sealed class FilePlan {
        public FilePlan(string path, string action, string? content, string? reason = null) {
            Path = path;
            Action = action;
            Content = content;
            Reason = reason;
        }

        public string Path { get; }
        public string Action { get; }
        public string? Content { get; }
        public string? Reason { get; }
        public bool IsWrite => Action is "create" or "update" or "overwrite";

        public static FilePlan Skip(string path, string? reason = null) => new(path, "skip", null, reason);
    }

    private sealed class RepoFile {
        public RepoFile(string sha, string content) {
            Sha = sha;
            Content = content;
        }

        public string Sha { get; }
        public string Content { get; }
    }

    private sealed class WorkflowSettings {
        public string ActionsRepo { get; set; } = "evotecit/github-actions";
        public string ActionsRef { get; set; } = "master";
        public string RunsOn { get; set; } = "[\"self-hosted\",\"ubuntu\"]";
        public string ReviewerSource { get; set; } = "release";
        public string ReviewerReleaseRepo { get; set; } = "EvotecIT/github-actions";
        public string ReviewerReleaseTag { get; set; } = "latest";
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public string Provider { get; set; } = "openai";
        public string Model { get; set; } = "gpt-5.2-codex";
        public string OpenAITransport { get; set; } = "native";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public bool CleanupEnabled { get; set; }
        public string CleanupMode { get; set; } = "comment";
        public string CleanupScope { get; set; } = "pr";
        public string? CleanupRequireLabel { get; set; } = string.Empty;
        public double CleanupMinConfidence { get; set; } = 0.85;
        public string CleanupAllowedEdits { get; set; } = "formatting,grammar,title,sections";
        public bool CleanupPostEditComment { get; set; } = true;

        public static WorkflowSettings FromOptions(SetupOptions options) {
            return new WorkflowSettings {
                ActionsRepo = NormalizeActionsRepo(options.ActionsRepo ?? "evotecit/github-actions"),
                ActionsRef = options.ActionsRef ?? "master",
                RunsOn = options.RunsOn ?? "[\"self-hosted\",\"ubuntu\"]",
                ReviewerSource = options.ReviewerSource ?? "release",
                ReviewerReleaseRepo = options.ReviewerReleaseRepo ?? "EvotecIT/github-actions",
                ReviewerReleaseTag = options.ReviewerReleaseTag ?? "latest",
                ReviewerReleaseAsset = options.ReviewerReleaseAsset,
                ReviewerReleaseUrl = options.ReviewerReleaseUrl,
                Provider = options.Provider ?? "openai",
                Model = options.OpenAIModel ?? "gpt-5.2-codex",
                OpenAITransport = options.OpenAITransport ?? "native",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates,
                CleanupEnabled = options.CleanupEnabled,
                CleanupMode = options.CleanupMode ?? "comment",
                CleanupScope = options.CleanupScope ?? "pr",
                CleanupRequireLabel = options.CleanupRequireLabel ?? string.Empty,
                CleanupMinConfidence = options.CleanupMinConfidence,
                CleanupAllowedEdits = options.CleanupAllowedEdits ?? "formatting,grammar,title,sections",
                CleanupPostEditComment = options.CleanupPostEditComment
            };
        }
    }

    private sealed class ConfigSettings {
        public string Provider { get; set; } = "openai";
        public string OpenAITransport { get; set; } = "native";
        public string OpenAIModel { get; set; } = "gpt-5.2-codex";
        public string Profile { get; set; } = "balanced";
        public string Mode { get; set; } = "hybrid";
        public string CommentMode { get; set; } = "sticky";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;

        public static ConfigSettings FromOptions(SetupOptions options) {
            return new ConfigSettings {
                Provider = options.Provider ?? "openai",
                OpenAITransport = options.OpenAITransport ?? "native",
                OpenAIModel = options.OpenAIModel ?? "gpt-5.2-codex",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates
            };
        }
    }

    private sealed class WorkflowSnapshot {
        public string? ActionsRepo { get; set; }
        public string? ActionsRef { get; set; }
        public string? RunsOn { get; set; }
        public string? ReviewerSource { get; set; }
        public string? ReviewerReleaseRepo { get; set; }
        public string? ReviewerReleaseTag { get; set; }
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string? OpenAITransport { get; set; }
        public bool? IncludeIssueComments { get; set; }
        public bool? IncludeReviewComments { get; set; }
        public bool? IncludeRelatedPullRequests { get; set; }
        public bool? ProgressUpdates { get; set; }
        public bool? CleanupEnabled { get; set; }
        public string? CleanupMode { get; set; }
        public string? CleanupScope { get; set; }
        public string? CleanupRequireLabel { get; set; }
        public double? CleanupMinConfidence { get; set; }
        public string? CleanupAllowedEdits { get; set; }
        public bool? CleanupPostEditComment { get; set; }

        public bool HasAny =>
            ActionsRepo is not null ||
            ActionsRef is not null ||
            RunsOn is not null ||
            ReviewerSource is not null ||
            ReviewerReleaseRepo is not null ||
            ReviewerReleaseTag is not null ||
            ReviewerReleaseAsset is not null ||
            ReviewerReleaseUrl is not null ||
            Provider is not null ||
            Model is not null ||
            OpenAITransport is not null ||
            IncludeIssueComments.HasValue ||
            IncludeReviewComments.HasValue ||
            IncludeRelatedPullRequests.HasValue ||
            ProgressUpdates.HasValue ||
            CleanupEnabled.HasValue ||
            CleanupMode is not null ||
            CleanupScope is not null ||
            CleanupRequireLabel is not null ||
            CleanupMinConfidence.HasValue ||
            CleanupAllowedEdits is not null ||
            CleanupPostEditComment.HasValue;
    }

    private sealed class ConfigSnapshot {
        public string? Provider { get; set; }
        public string? OpenAITransport { get; set; }
        public string? OpenAIModel { get; set; }
        public string? Profile { get; set; }
        public string? Mode { get; set; }
        public string? CommentMode { get; set; }
        public bool? IncludeIssueComments { get; set; }
        public bool? IncludeReviewComments { get; set; }
        public bool? IncludeRelatedPullRequests { get; set; }
        public bool? ProgressUpdates { get; set; }

        public bool HasAny =>
            Provider is not null ||
            OpenAITransport is not null ||
            OpenAIModel is not null ||
            Profile is not null ||
            Mode is not null ||
            CommentMode is not null ||
            IncludeIssueComments.HasValue ||
            IncludeReviewComments.HasValue ||
            IncludeRelatedPullRequests.HasValue ||
            ProgressUpdates.HasValue;
    }

    private sealed class SetupState {
        public SetupState(SetupOptions options) {
            Options = options;
        }

        public SetupOptions Options { get; }
        public GitHubState GitHub { get; } = new();
        public OpenAiState OpenAI { get; } = new();
        public CopilotState Copilot { get; } = new();
        public Dictionary<string, object?> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GitHubState {
        public string? ClientId { get; set; }
        public string? Token { get; set; }
        public string? RepositoryFullName { get; set; }
        public string? Owner { get; set; }
        public string? Repo { get; set; }
        public List<RepositoryInfo> Repositories { get; set; } = new();
    }

    private sealed class OpenAiState {
        public AuthBundle? AuthBundle { get; set; }
        public string? AuthJson { get; set; }
        public string? AuthB64 { get; set; }
    }

    private sealed class CopilotState {
        public string? Status { get; set; }
    }

    private sealed class RepositoryInfo {
        public RepositoryInfo(string fullName, bool isPrivate, DateTimeOffset? updatedAt) {
            FullName = fullName;
            Private = isPrivate;
            UpdatedAt = updatedAt;
        }

        public string FullName { get; }
        public bool Private { get; }
        public DateTimeOffset? UpdatedAt { get; }
    }

    private sealed class GitHubDeviceFlow {
        public static async Task<string?> LoginAsync(string clientId, string authBaseUrl, string scopes) {
            using var http = new HttpClient();
            var deviceUri = new Uri(new Uri(authBaseUrl), "/login/device/code");
            var request = new HttpRequestMessage(HttpMethod.Post, deviceUri) {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["client_id"] = clientId,
                    ["scope"] = scopes
                })
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await http.SendAsync(request).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var deviceCode = root.GetProperty("device_code").GetString();
            var userCode = root.GetProperty("user_code").GetString();
            var verificationUri = root.GetProperty("verification_uri").GetString();
            var interval = root.GetProperty("interval").GetInt32();
            var expiresIn = root.GetProperty("expires_in").GetInt32();

            if (string.IsNullOrWhiteSpace(deviceCode) || string.IsNullOrWhiteSpace(userCode) || string.IsNullOrWhiteSpace(verificationUri)) {
                throw new InvalidOperationException("Invalid device flow response.");
            }

            Console.WriteLine($"Open {verificationUri} and enter code: {userCode}");
            TryOpenUrl(verificationUri);

            var tokenUri = new Uri(new Uri(authBaseUrl), "/login/oauth/access_token");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            while (DateTimeOffset.UtcNow < deadline) {
                await Task.Delay(TimeSpan.FromSeconds(interval)).ConfigureAwait(false);
                var pollRequest = new HttpRequestMessage(HttpMethod.Post, tokenUri) {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> {
                        ["client_id"] = clientId,
                        ["device_code"] = deviceCode!,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    })
                };
                pollRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var pollResponse = await http.SendAsync(pollRequest).ConfigureAwait(false);
                var pollJson = await pollResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                pollResponse.EnsureSuccessStatusCode();
                using var pollDoc = JsonDocument.Parse(pollJson);
                var pollRoot = pollDoc.RootElement;
                if (pollRoot.TryGetProperty("access_token", out var accessToken)) {
                    return accessToken.GetString();
                }
                if (pollRoot.TryGetProperty("error", out var error)) {
                    var code = error.GetString();
                    if (string.Equals(code, "authorization_pending", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    if (string.Equals(code, "slow_down", StringComparison.OrdinalIgnoreCase)) {
                        interval += 5;
                        continue;
                    }
                    throw new InvalidOperationException($"GitHub device flow error: {code}");
                }
            }

            return null;
        }
    }

    private sealed class GitHubApi : IDisposable {
        private readonly HttpClient _http;

        public GitHubApi(string token, string apiBaseUrl) {
            _http = new HttpClient {
                BaseAddress = new Uri(apiBaseUrl)
            };
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public void Dispose() => _http.Dispose();

        public async Task<List<RepositoryInfo>> ListRepositoriesAsync() {
            var repos = new List<RepositoryInfo>();
            var page = 1;
            while (true) {
                var url = $"/user/repos?per_page=100&page={page}&affiliation=owner,collaborator,organization_member";
                var json = await GetJsonAsync(url).ConfigureAwait(false);
                if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0) {
                    break;
                }
                foreach (var item in json.EnumerateArray()) {
                    var fullName = item.GetProperty("full_name").GetString() ?? string.Empty;
                    var isPrivate = item.GetProperty("private").GetBoolean();
                    DateTimeOffset? updatedAt = null;
                    if (item.TryGetProperty("updated_at", out var updatedProperty) && updatedProperty.ValueKind == JsonValueKind.String) {
                        if (DateTimeOffset.TryParse(updatedProperty.GetString(), out var parsed)) {
                            updatedAt = parsed;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(fullName)) {
                        repos.Add(new RepositoryInfo(fullName, isPrivate, updatedAt));
                    }
                }
                if (json.GetArrayLength() < 100) {
                    break;
                }
                page++;
            }
            return repos;
        }

        public async Task<string> GetDefaultBranchAsync(string owner, string repo) {
            var json = await GetJsonAsync($"/repos/{owner}/{repo}").ConfigureAwait(false);
            return json.GetProperty("default_branch").GetString() ?? "main";
        }

        public async Task<string> GetBranchShaAsync(string owner, string repo, string branch) {
            var json = await GetJsonAsync($"/repos/{owner}/{repo}/git/ref/heads/{branch}").ConfigureAwait(false);
            return json.GetProperty("object").GetProperty("sha").GetString() ?? string.Empty;
        }

        public async Task EnsureBranchAsync(string owner, string repo, string branch, string sha) {
            var payload = new {
                @ref = $"refs/heads/{branch}",
                sha
            };
            await PostJsonAsync($"/repos/{owner}/{repo}/git/refs", payload, allowConflict: true)
                .ConfigureAwait(false);
        }

        public async Task<bool> CreateOrUpdateFileAsync(string owner, string repo, string path, string content,
            string message, string branch, bool overwrite) {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            var sha = await TryGetFileShaAsync(owner, repo, path, branch).ConfigureAwait(false);
            if (!overwrite && !string.IsNullOrWhiteSpace(sha)) {
                Console.WriteLine($"Skipped {path} (already exists). Use --force to overwrite.");
                return false;
            }

            var payload = new Dictionary<string, object?> {
                ["message"] = message,
                ["content"] = encoded,
                ["branch"] = branch
            };
            if (!string.IsNullOrWhiteSpace(sha)) {
                payload["sha"] = sha;
            }

            await PutJsonAsync($"/repos/{owner}/{repo}/contents/{path}", payload).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> DeleteFileAsync(string owner, string repo, string path, string message, string branch) {
            var sha = await TryGetFileShaAsync(owner, repo, path, branch).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sha)) {
                return false;
            }

            var payload = new Dictionary<string, object?> {
                ["message"] = message,
                ["sha"] = sha,
                ["branch"] = branch
            };

            await DeleteJsonAsync($"/repos/{owner}/{repo}/contents/{path}", payload).ConfigureAwait(false);
            return true;
        }

        public async Task<string?> CreatePullRequestAsync(string owner, string repo, string title, string head, string @base, string body) {
            var payload = new {
                title,
                head,
                @base,
                body
            };
            var result = await PostJsonAsync($"/repos/{owner}/{repo}/pulls", payload, allowConflict: true)
                .ConfigureAwait(false);
            if (result is null) {
                return null;
            }
            return result.Value.GetProperty("html_url").GetString();
        }

        public async Task SetSecretAsync(string owner, string repo, string name, string value) {
            var publicKeyJson = await GetJsonAsync($"/repos/{owner}/{repo}/actions/secrets/public-key").ConfigureAwait(false);
            var key = publicKeyJson.GetProperty("key").GetString();
            var keyId = publicKeyJson.GetProperty("key_id").GetString();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(keyId)) {
                throw new InvalidOperationException("Failed to read GitHub public key.");
            }

            var keyBytes = Convert.FromBase64String(key);
            var encrypted = SealedPublicKeyBox.Create(Encoding.UTF8.GetBytes(value), keyBytes);
            var encryptedB64 = Convert.ToBase64String(encrypted);

            var payload = new {
                encrypted_value = encryptedB64,
                key_id = keyId
            };

            await PutJsonAsync($"/repos/{owner}/{repo}/actions/secrets/{name}", payload).ConfigureAwait(false);
        }

        public async Task DeleteSecretAsync(string owner, string repo, string name) {
            using var response = await _http.DeleteAsync($"/repos/{owner}/{repo}/actions/secrets/{name}").ConfigureAwait(false);
            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                return;
            }
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
        }

        public async Task<RepoFile?> TryGetFileAsync(string owner, string repo, string path, string branch) {
            try {
                var json = await GetJsonAsync($"/repos/{owner}/{repo}/contents/{path}?ref={branch}")
                    .ConfigureAwait(false);
                if (!json.TryGetProperty("content", out var contentProperty)) {
                    return null;
                }
                var content = contentProperty.GetString();
                var sha = json.GetProperty("sha").GetString();
                if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(sha)) {
                    return null;
                }

                var normalized = content.Replace("\n", string.Empty).Replace("\r", string.Empty);
                var bytes = Convert.FromBase64String(normalized);
                var text = Encoding.UTF8.GetString(bytes);
                return new RepoFile(sha, text);
            } catch {
                return null;
            }
        }

        public async Task<string?> TryGetFileShaAsync(string owner, string repo, string path, string branch) {
            try {
                var json = await GetJsonAsync($"/repos/{owner}/{repo}/contents/{path}?ref={branch}")
                    .ConfigureAwait(false);
                return json.GetProperty("sha").GetString();
            } catch {
                return null;
            }
        }

        private async Task<JsonElement> GetJsonAsync(string url) {
            using var response = await _http.GetAsync(url).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
            }
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.Clone();
        }

        private async Task<JsonElement?> PostJsonAsync(string url, object payload, bool allowConflict) {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                if (allowConflict && response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity) {
                    return null;
                }
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
            }
            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement.Clone();
        }

        private async Task PutJsonAsync(string url, object payload) {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PutAsync(url, content).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
            }
        }

        private async Task DeleteJsonAsync(string url, object payload) {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Delete, url) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
            }
        }
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
}
