using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Reviewer;

/// <summary>
/// Entry point for running the IntelligenceX review workflow.
/// </summary>
public static class ReviewerApp {
    private const string ThreadReplyMarker = "<!-- intelligencex:thread-reply -->";
    private static int _integrationForbiddenHintLogged;
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tiff", ".tif", ".ico", ".webp",
        ".mp3", ".wav", ".flac", ".ogg", ".mp4", ".mov", ".mkv", ".avi", ".mpeg", ".mpg", ".webm",
        ".pdf", ".psd", ".ai", ".sketch",
        ".zip", ".rar", ".7z", ".gz", ".tgz", ".bz2", ".xz", ".tar",
        ".exe", ".dll", ".so", ".dylib", ".bin", ".class", ".jar", ".war",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".pdb", ".dmg", ".iso", ".apk", ".ipa", ".wasm"
    };
    private static readonly IReadOnlyList<string> GeneratedGlobs = new[] {
        "**/bin/**",
        "bin/**",
        "**/obj/**",
        "obj/**",
        "**/dist/**",
        "dist/**",
        "**/build/**",
        "build/**",
        "**/out/**",
        "out/**",
        "**/coverage/**",
        "coverage/**",
        "**/.next/**",
        ".next/**",
        "**/.nuxt/**",
        ".nuxt/**",
        "**/.turbo/**",
        ".turbo/**",
        "**/.cache/**",
        ".cache/**",
        "**/.parcel-cache/**",
        ".parcel-cache/**",
        "**/node_modules/**",
        "node_modules/**",
        "**/*.g.cs",
        "*.g.cs",
        "**/*.g.i.cs",
        "*.g.i.cs",
        "**/*.g.i.vb",
        "*.g.i.vb",
        "**/*.generated.*",
        "*.generated.*",
        "**/*.gen.*",
        "*.gen.*",
        "**/*.designer.*",
        "*.designer.*",
        "**/*.min.js",
        "*.min.js",
        "**/*.min.css",
        "*.min.css",
        "**/*.bundle.js",
        "*.bundle.js",
        "**/*.bundle.css",
        "*.bundle.css",
        "**/*.js.map",
        "*.js.map",
        "**/*.css.map",
        "*.css.map"
    };
    /// <summary>
    /// Executes the reviewer workflow with the provided arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code (0 for success).</returns>
    public static async Task<int> RunAsync(string[] args) {
        using var cts = new CancellationTokenSource();
        var runOptions = ParseRunOptions(args);
        if (runOptions.ShowHelp) {
            PrintRunHelp();
            return 0;
        }
        if (runOptions.Errors.Count > 0) {
            foreach (var error in runOptions.Errors) {
                Console.Error.WriteLine(error);
            }
            Console.Error.WriteLine("Use --help to see available options.");
            PrintRunHelp(Console.Error);
            return 1;
        }
        ApplyRunOptions(runOptions);
        ConsoleCancelEventHandler? cancelHandler = (_, evt) => {
            evt.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        // Hoist state so the exception handler can update the progress comment on failure.
        SecretsAuditSession? secretsAudit = null;
        ReviewSettings? settings = null;
        PullRequestContext? context = null;
        string? githubToken = null;
        bool allowWrites = false;
        bool inlineSupported = false;
        bool summaryPosted = false;
        long? commentId = null;
        var progress = new ReviewProgress { StatusLine = "Starting review." };
        try {
            var cancellationToken = cts.Token;
            if (!await TryWriteAuthFromEnvAsync().ConfigureAwait(false)) {
                return 1;
            }
            settings = ReviewSettings.Load();
            secretsAudit = SecretsAudit.TryStart(settings);
            var validation = ReviewConfigValidator.ValidateCurrent();
            if (validation is not null) {
                if (validation.Warnings.Count > 0) {
                    Console.Error.WriteLine($"Configuration warnings in {validation.ConfigPath}:");
                    foreach (var warning in validation.Warnings) {
                        Console.Error.WriteLine($"- {warning.Path}: {warning.Message}");
                    }
                    Console.Error.WriteLine($"Schema: {validation.SchemaHint}");
                }
                if (validation.Errors.Count > 0) {
                    Console.Error.WriteLine($"Configuration errors in {validation.ConfigPath}:");
                    foreach (var error in validation.Errors) {
                        Console.Error.WriteLine($"- {error.Path}: {error.Message}");
                    }
                    Console.Error.WriteLine($"Schema: {validation.SchemaHint}");
                    return 1;
                }
            }
            var providerContract = ReviewProviderContracts.Get(settings.Provider);
            if (settings.Diagnostics && settings.RetryCount > providerContract.MaxRecommendedRetryCount) {
                Console.Error.WriteLine(
                    $"Retry count ({settings.RetryCount}) exceeds recommended limit ({providerContract.MaxRecommendedRetryCount}) for {providerContract.DisplayName}.");
            }
            if (settings.CodeHost == ReviewCodeHost.AzureDevOps) {
                if (!await ValidateAuthAsync(settings).ConfigureAwait(false)) {
                    return 1;
                }
                return await AzureDevOpsReviewRunner.RunAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            var primaryToken = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN");
            var token = !string.IsNullOrWhiteSpace(primaryToken)
                ? primaryToken
                : Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            var tokenSource = !string.IsNullOrWhiteSpace(primaryToken)
                ? "INTELLIGENCEX_GITHUB_TOKEN"
                : "GITHUB_TOKEN";

            if (string.IsNullOrWhiteSpace(token)) {
                Console.Error.WriteLine("Missing GitHub token (INTELLIGENCEX_GITHUB_TOKEN or GITHUB_TOKEN).");
                return 1;
            }
            githubToken = token;
            SecretsAudit.Record($"GitHub token from {tokenSource}");

            var fallbackToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.Equals(token, fallbackToken, StringComparison.Ordinal)) {
                fallbackToken = null;
            }
            if (!string.IsNullOrWhiteSpace(fallbackToken)) {
                SecretsAudit.Record("GitHub fallback token from GITHUB_TOKEN");
            }

            using var github = new GitHubClient(token, maxConcurrency: settings.GitHubMaxConcurrency);
            IReviewCodeHostReader codeHostReader = new GitHubCodeHostReader(github);
            using var fallbackGithub = string.IsNullOrWhiteSpace(fallbackToken)
                ? null
                : new GitHubClient(fallbackToken, maxConcurrency: settings.GitHubMaxConcurrency);
            var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
            if (!string.IsNullOrWhiteSpace(eventPath) && File.Exists(eventPath)) {
                var json = await File.ReadAllTextAsync(eventPath).ConfigureAwait(false);
                var rootValue = JsonLite.Parse(json);
                var root = rootValue?.AsObject();
                if (root is null) {
                    Console.Error.WriteLine("Invalid GitHub event payload.");
                    return 1;
                }
                context = GitHubEventParser.TryParsePullRequest(root);
            }
            if (context is null) {
                var repoName = GetInput("repo") ?? GetInput("repository") ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
                var prNumber = GetInputInt("pr_number") ?? GetInputInt("pull_request") ?? GetInputInt("number");
                if (string.IsNullOrWhiteSpace(repoName) || !prNumber.HasValue) {
                    Console.Error.WriteLine("Missing pull_request data. Provide inputs: repo and pr_number.");
                    return 1;
                }
                context = await codeHostReader.GetPullRequestAsync(repoName!, prNumber.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            var isUntrusted = context.IsFromFork;
            if (isUntrusted) {
                Console.WriteLine("Untrusted pull request detected (fork).");
                if (!settings.UntrustedPrAllowSecrets) {
                    Console.WriteLine("Skipping review to avoid secret access. Set review.untrustedPrAllowSecrets=true to override.");
                    return 0;
                }
            }

            if (!await ValidateAuthAsync(settings).ConfigureAwait(false)) {
                return 1;
            }

            allowWrites = !isUntrusted || settings.UntrustedPrAllowWrites;
            if (!allowWrites && isUntrusted) {
                Console.WriteLine("Write actions disabled for untrusted pull requests.");
            }

            if (settings.SkipDraft && context.Draft) {
                Console.WriteLine("Skipping draft pull request.");
                return 0;
            }
            if (ShouldSkipByTitle(context.Title, settings.SkipTitles)) {
                Console.WriteLine("Skipping pull request due to title filter.");
                return 0;
            }
            if (ShouldSkipByLabels(context.Labels, settings.SkipLabels)) {
                Console.WriteLine("Skipping pull request due to label filter.");
                return 0;
            }

            var files = await codeHostReader.GetPullRequestFilesAsync(context, cancellationToken)
                .ConfigureAwait(false);

            if (files.Count == 0) {
                Console.WriteLine("No files to review.");
                return 0;
            }

            if (!settings.AllowWorkflowChanges && HasWorkflowChanges(files)) {
                Console.WriteLine("Workflow file changes detected; skipping review. Set allowWorkflowChanges or REVIEW_ALLOW_WORKFLOW_CHANGES=true to override.");
                return 0;
            }

            if (allowWrites) {
                context = await CleanupService.RunAsync(github, context, settings, cancellationToken)
                    .ConfigureAwait(false);
            } else {
                Console.WriteLine("Skipping cleanup for untrusted pull request.");
            }

            IssueComment? existingSummary = null;
            string? previousSummary = null;
            if (settings.SummaryStability && !string.IsNullOrWhiteSpace(context.HeadSha)) {
                existingSummary = await FindExistingSummaryAsync(codeHostReader, context, settings, cancellationToken)
                    .ConfigureAwait(false);
                if (existingSummary is not null && !IsSummaryOutdated(existingSummary, context.HeadSha)) {
                    previousSummary = ExtractSummaryBody(existingSummary.Body, settings.MaxCommentChars);
                }
            }

            progress = new ReviewProgress { StatusLine = "Starting review." };

            if (ShouldSkipByPaths(files, settings.SkipPaths)) {
                Console.WriteLine("Skipping pull request due to path filter.");
                return 0;
            }

            var allFiles = files;
            var (reviewFiles, diffNote) = await ResolveReviewFilesAsync(codeHostReader, context, settings, files, cancellationToken)
                .ConfigureAwait(false);
            reviewFiles = FilterFilesByPaths(reviewFiles, settings.IncludePaths, settings.ExcludePaths,
                settings.SkipBinaryFiles, settings.SkipGeneratedFiles, settings.GeneratedFileGlobs);
            if (reviewFiles.Count == 0) {
                progress.Context = ReviewProgressState.Complete;
                Console.WriteLine("No files matched include/exclude filters.");
                return 0;
            }
            files = reviewFiles;

            progress.Context = ReviewProgressState.Complete;
            progress.Files = ReviewProgressState.Complete;
            progress.StatusLine = "Analyzed changed files.";

            var extras = await BuildExtrasAsync(codeHostReader, github, fallbackGithub, context, settings, cancellationToken,
                    settings.TriageOnly)
                .ConfigureAwait(false);
            if (settings.TriageOnly) {
                if (!allowWrites) {
                    Console.WriteLine("Skipping thread triage for untrusted pull request.");
                    return 0;
                }
                var triageRunner = new ReviewRunner(settings);
                var (triageOnlyFiles, triageOnlyNote) = await ResolveThreadTriageFilesAsync(codeHostReader, context, settings, allFiles,
                        cancellationToken)
                    .ConfigureAwait(false);
                var triageOnlyResult = await MaybeAutoResolveAssessedThreadsAsync(github, fallbackGithub, triageRunner, context, triageOnlyFiles,
                        settings, extras, false, triageOnlyNote, cancellationToken, true, false)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(triageOnlyResult.EmbeddedBlock)) {
                    await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, triageOnlyResult.EmbeddedBlock, cancellationToken)
                        .ConfigureAwait(false);
                    Console.WriteLine("Posted thread triage comment.");
                } else {
                    Console.WriteLine("No threads eligible for triage.");
                }
                return 0;
            }
            inlineSupported = !string.Equals(settings.Mode, "summary", StringComparison.OrdinalIgnoreCase) &&
                                  settings.MaxInlineComments > 0 &&
                                  !string.IsNullOrWhiteSpace(context.HeadSha);
            var (limitedFiles, budgetNote) = PrepareFiles(files, settings.MaxFiles, settings.MaxPatchChars);
            if (!settings.ReviewBudgetSummary) {
                budgetNote = string.Empty;
            }
            var prompt = PromptBuilder.Build(context, limitedFiles, settings, diffNote, extras, inlineSupported, previousSummary);
            if (settings.RedactPii) {
                prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
            }

            if (allowWrites && settings.ProgressUpdates) {
                var progressBody = ReviewFormatter.BuildProgressComment(context, settings, progress, null, inlineSupported);
                commentId = await CreateOrUpdateProgressCommentAsync(codeHostReader, github, context, settings, progressBody, cancellationToken)
                    .ConfigureAwait(false);
            }

            var runner = new ReviewRunner(settings);
            progress.Review = ReviewProgressState.InProgress;
            progress.StatusLine = "Generating review findings.";

            Func<string, Task>? onPartial = null;
            if (allowWrites && settings.ProgressUpdates && commentId.HasValue) {
                onPartial = async partial => {
                    var body = ReviewFormatter.BuildProgressComment(context, settings, progress, partial, inlineSupported);
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, body, cancellationToken)
                        .ConfigureAwait(false);
                };
            }

            var reviewBody = await runner.RunAsync(prompt, onPartial, TimeSpan.FromSeconds(settings.ProgressUpdateSeconds),
                cancellationToken).ConfigureAwait(false);
            var effectiveProvider = runner.EffectiveProvider;
            if (runner.FallbackActivated && settings.Diagnostics) {
                Console.Error.WriteLine(
                    $"Provider fallback activated: {settings.Provider.ToString().ToLowerInvariant()} -> {effectiveProvider.ToString().ToLowerInvariant()}.");
            }

            var reviewFailed = ReviewDiagnostics.IsFailureBody(reviewBody);
            var inlineAllowed = inlineSupported && !reviewFailed && allowWrites;
            var inlineComments = Array.Empty<InlineReviewComment>();
            var summaryBody = reviewBody;
            if (inlineAllowed) {
                var inlineResult = ReviewInlineParser.Extract(reviewBody, settings.MaxInlineComments);
                inlineComments = inlineResult.Comments as InlineReviewComment[] ?? inlineResult.Comments.ToArray();
                if (inlineResult.HadInlineSection && !string.IsNullOrWhiteSpace(inlineResult.Body)) {
                    summaryBody = inlineResult.Body;
                }
            }

            var analysisSettings = settings.Analysis;
            var analysisResults = analysisSettings?.Results;
            if (analysisSettings?.Enabled == true && analysisResults is not null) {
                try {
                    var analysisFindings = AnalysisFindingsLoader.Load(settings, reviewFiles);
                    var analysisBlocks = new List<string>();
                    var analysisPolicy = AnalysisPolicyBuilder.BuildPolicy(settings);
                    if (!string.IsNullOrWhiteSpace(analysisPolicy)) {
                        analysisBlocks.Add(analysisPolicy);
                    }
                    var analysisSummary = AnalysisSummaryBuilder.BuildSummary(analysisFindings, analysisResults);
                    if (!string.IsNullOrWhiteSpace(analysisSummary)) {
                        analysisBlocks.Add(analysisSummary);
                    }
                    if (analysisBlocks.Count > 0) {
                        var analysisBlock = string.Join("\n\n", analysisBlocks);
                        summaryBody = ApplyEmbedPlacement(summaryBody, analysisBlock, analysisResults.SummaryPlacement);
                    }
                    if (inlineAllowed && analysisFindings.Count > 0) {
                        var analysisInline = AnalysisSummaryBuilder.BuildInlineComments(analysisFindings, analysisResults);
                        if (analysisInline.Count > 0) {
                            var merged = new List<InlineReviewComment>(inlineComments.Length + analysisInline.Count);
                            merged.AddRange(inlineComments);
                            merged.AddRange(analysisInline);
                            inlineComments = merged.ToArray();
                        }
                    }
                } catch {
                    Console.WriteLine("Static analysis load failed; skipping analysis findings.");
                }
            }

            HashSet<string>? inlineKeys = null;
            if (inlineAllowed) {
                inlineKeys = await PostInlineCommentsAsync(codeHostReader, github, context, files, settings, inlineComments,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (settings.ReviewThreadsAutoResolveMissingInline &&
                    !string.IsNullOrWhiteSpace(context.HeadSha) &&
                    inlineKeys is not null &&
                    inlineKeys.Count > 0) {
                    await AutoResolveMissingInlineThreadsAsync(codeHostReader, github, fallbackGithub, context, inlineKeys, settings,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var triageResult = ThreadTriageResult.Empty;
            if (allowWrites) {
                var (triageFiles, triageNote) = await ResolveThreadTriageFilesAsync(codeHostReader, context, settings, allFiles,
                        cancellationToken)
                    .ConfigureAwait(false);
                triageResult = await MaybeAutoResolveAssessedThreadsAsync(github, fallbackGithub, runner, context, triageFiles,
                        settings, extras, reviewFailed, triageNote, cancellationToken, false,
                        settings.ReviewThreadsAutoResolveAIPostComment)
                    .ConfigureAwait(false);
                if (settings.ReviewThreadsAutoResolveAIEmbed && !string.IsNullOrWhiteSpace(triageResult.EmbeddedBlock)) {
                    summaryBody = ApplyEmbedPlacement(summaryBody, triageResult.EmbeddedBlock,
                        settings.ReviewThreadsAutoResolveAIEmbedPlacement);
                }
            }
            var inlineSuppressed = inlineSupported && !inlineAllowed;
            var autoResolveSummary = allowWrites && settings.ReviewThreadsAutoResolveAISummary ? triageResult.SummaryLine : string.Empty;
            if (allowWrites && settings.ReviewThreadsAutoResolveSummaryAlways && string.IsNullOrWhiteSpace(autoResolveSummary)) {
                autoResolveSummary = triageResult.FallbackSummary;
            }
            var usageLine = await TryBuildUsageLineAsync(settings, effectiveProvider).ConfigureAwait(false);
            var findingsBlock = settings.StructuredFindings ? ReviewFindingsBuilder.Build(inlineComments) : string.Empty;
            var commentBody = ReviewFormatter.BuildComment(context, summaryBody, settings, inlineSupported, inlineSuppressed,
                autoResolveSummary, budgetNote, usageLine, findingsBlock);
            progress.Review = ReviewProgressState.Complete;
            progress.Finalize = ReviewProgressState.InProgress;
            progress.StatusLine = "Finalizing summary.";

            if (!allowWrites) {
                Console.WriteLine("Skipping GitHub writes for untrusted pull request.");
                Console.WriteLine(commentBody);
                return 0;
            }

            if (commentId.HasValue) {
                await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, commentBody, cancellationToken)
                    .ConfigureAwait(false);
                summaryPosted = true;
                Console.WriteLine("Updated review comment.");
            } else if (settings.CommentMode == ReviewCommentMode.Sticky) {
                var shouldSearch = settings.OverwriteSummary || settings.OverwriteSummaryOnNewCommit;
                IssueComment? existing = null;
                if (shouldSearch) {
                    existing = existingSummary ?? await FindExistingSummaryAsync(codeHostReader, context, settings, cancellationToken)
                        .ConfigureAwait(false);
                }
                var shouldOverwrite = settings.OverwriteSummary ||
                                      (settings.OverwriteSummaryOnNewCommit && IsSummaryOutdated(existing, context.HeadSha));
                if (existing is not null && shouldOverwrite) {
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, existing.Id, commentBody, cancellationToken)
                        .ConfigureAwait(false);
                    summaryPosted = true;
                    Console.WriteLine("Updated existing review comment.");
                    return 0;
                }
                await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, commentBody, cancellationToken)
                    .ConfigureAwait(false);
                summaryPosted = true;
                Console.WriteLine("Posted review comment.");
            } else {
                await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, commentBody, cancellationToken)
                    .ConfigureAwait(false);
                summaryPosted = true;
                Console.WriteLine("Posted review comment.");
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            if (!summaryPosted &&
                commentId.HasValue &&
                allowWrites &&
                context is not null &&
                settings is not null) {
                try {
                    var updated = await TryUpdateFailureSummaryAsync(githubToken, null, context, settings, commentId.Value, ex,
                            inlineSupported)
                        .ConfigureAwait(false);
                    if (updated) {
                        Console.WriteLine("Updated review comment with failure summary.");
                    }
                } catch (Exception updateEx) {
                    Console.Error.WriteLine($"Failed to update review comment after error: {updateEx.Message}");
                }
            }
            return 1;
        } finally {
            secretsAudit?.WriteSummary();
            secretsAudit?.Dispose();
            if (cancelHandler is not null) {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    internal static async Task<bool> TryUpdateFailureSummaryAsync(string? githubToken, string? apiBaseUrl,
        PullRequestContext context, ReviewSettings settings, long commentId, Exception ex, bool inlineSupported) {
        if (string.IsNullOrWhiteSpace(githubToken)) {
            return false;
        }
        var failureBody = ReviewDiagnostics.BuildFailureBody(ex, settings, null, null);
        var inlineSuppressed = inlineSupported;
        var commentBody = ReviewFormatter.BuildComment(context, failureBody, settings, inlineSupported, inlineSuppressed,
            string.Empty, string.Empty, string.Empty, string.Empty);
        using var failureClient = new GitHubClient(githubToken, apiBaseUrl, settings.GitHubMaxConcurrency);
        await failureClient.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId, commentBody,
                CancellationToken.None)
            .ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> TryWriteAuthFromEnvAsync() {
        var authJson = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_JSON");
        var authB64 = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_B64");
        if (string.IsNullOrWhiteSpace(authJson) && string.IsNullOrWhiteSpace(authB64)) {
            return true;
        }

        string content;
        if (!string.IsNullOrWhiteSpace(authJson)) {
            content = authJson!;
            SecretsAudit.Record("Auth store loaded from INTELLIGENCEX_AUTH_JSON");
        } else {
            try {
                var bytes = Convert.FromBase64String(authB64!);
                content = Encoding.UTF8.GetString(bytes);
                SecretsAudit.Record("Auth store loaded from INTELLIGENCEX_AUTH_B64");
            } catch {
                Console.Error.WriteLine("Failed to decode INTELLIGENCEX_AUTH_B64.");
                return false;
            }
        }

        if (IsEncryptedStore(content) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY"))) {
            Console.Error.WriteLine("Auth store is encrypted but INTELLIGENCEX_AUTH_KEY is not set.");
            return false;
        }

        if (HasAuthStoreBundles(content)) {
            WriteAuthStoreContent(content);
            return true;
        }

        var bundle = AuthBundleSerializer.Deserialize(content);
        if (bundle is not null) {
            try {
                var store = new FileAuthBundleStore();
                await store.SaveAsync(bundle).ConfigureAwait(false);
                return true;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to write auth bundle: {ex.Message}");
                return false;
            }
        }

        Console.Error.WriteLine("Auth bundle content is invalid.");
        return false;
    }

    private static void WriteAuthStoreContent(string content) {
        var path = AuthPaths.ResolveAuthPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
    }

    private static bool IsEncryptedStore(string content) {
        return content.TrimStart().StartsWith("{\"encrypted\":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAuthStoreBundles(string content) {
        var value = JsonLite.Parse(content);
        var obj = value?.AsObject();
        if (obj is null) {
            return false;
        }
        var bundles = obj.GetObject("bundles");
        if (bundles is null) {
            return false;
        }
        foreach (var entry in bundles) {
            if (entry.Value?.AsObject() is not null) {
                return true;
            }
        }
        return false;
    }

    private sealed class RunOptions {
        public string? Provider { get; set; }
        public string? ProviderFallback { get; set; }
        public string? CodeHost { get; set; }
        public string? AzureOrg { get; set; }
        public string? AzureProject { get; set; }
        public string? AzureRepo { get; set; }
        public string? AzureBaseUrl { get; set; }
        public string? AzureTokenEnv { get; set; }
        public bool ShowHelp { get; set; }
        public List<string> Errors { get; } = new();

        public bool HasAzureOverrides =>
            !string.IsNullOrWhiteSpace(AzureOrg) ||
            !string.IsNullOrWhiteSpace(AzureProject) ||
            !string.IsNullOrWhiteSpace(AzureRepo) ||
            !string.IsNullOrWhiteSpace(AzureBaseUrl) ||
            !string.IsNullOrWhiteSpace(AzureTokenEnv);
    }

    private sealed class RunOptionSpec {
        public RunOptionSpec(string name, string valueHint, string description, bool requiresValue, Action<RunOptions, string?> apply) {
            Name = name;
            ValueHint = valueHint;
            Description = description;
            RequiresValue = requiresValue;
            Apply = apply;
        }

        public string Name { get; }
        public string ValueHint { get; }
        public string Description { get; }
        public bool RequiresValue { get; }
        public Action<RunOptions, string?> Apply { get; }
    }

    private static readonly RunOptionSpec[] RunOptionSpecs = {
        new RunOptionSpec("--provider", "<openai|codex|copilot|azure>", "AI provider or Azure DevOps code host (aliases: azuredevops, azure-devops, ado)", true,
            (options, value) => options.Provider = value),
        new RunOptionSpec("--provider-fallback", "<openai|codex|copilot>", "Optional fallback AI provider when the primary provider fails", true,
            (options, value) => options.ProviderFallback = value),
        new RunOptionSpec("--code-host", "<github|azure>", "Override code host (azure/azuredevops supported)", true,
            (options, value) => options.CodeHost = value),
        new RunOptionSpec("--azure-org", "<org>", "Azure DevOps organization", true,
            (options, value) => options.AzureOrg = value),
        new RunOptionSpec("--azure-project", "<project>", "Azure DevOps project", true,
            (options, value) => options.AzureProject = value),
        new RunOptionSpec("--azure-repo", "<repo>", "Azure DevOps repository id or name", true,
            (options, value) => options.AzureRepo = value),
        new RunOptionSpec("--azure-base-url", "<url>", "Azure DevOps base URL", true,
            (options, value) => options.AzureBaseUrl = value),
        new RunOptionSpec("--azure-token-env", "<env>", "Env var holding Azure DevOps token", true,
            (options, value) => options.AzureTokenEnv = value)
    };

    private static readonly IReadOnlyDictionary<string, RunOptionSpec> RunOptionSpecMap =
        RunOptionSpecs.ToDictionary(spec => spec.Name, StringComparer.Ordinal);

    private static RunOptions ParseRunOptions(string[] args) {
        var options = new RunOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                default:
                    if (RunOptionSpecMap.TryGetValue(arg, out var spec)) {
                        var value = spec.RequiresValue ? ReadValue(args, ref i, spec.Name, options.Errors) : null;
                        if (value is not null || !spec.RequiresValue) {
                            spec.Apply(options, value);
                        }
                        break;
                    }
                    options.Errors.Add($"Unknown option: {arg}");
                    break;
            }
        }
        if (!string.IsNullOrWhiteSpace(options.Provider) && !IsValidProvider(options.Provider)) {
            options.Errors.Add($"Unsupported provider '{options.Provider}'. Use openai, codex, copilot, or azure/azuredevops.");
        }
        if (!string.IsNullOrWhiteSpace(options.ProviderFallback) && !IsValidAiProvider(options.ProviderFallback)) {
            options.Errors.Add(
                $"Unsupported provider fallback '{options.ProviderFallback}'. Use openai, codex, or copilot.");
        }
        if (!string.IsNullOrWhiteSpace(options.CodeHost) && !IsValidCodeHost(options.CodeHost)) {
            options.Errors.Add($"Unsupported code host '{options.CodeHost}'. Use github or azure/azuredevops.");
        }
        return options;
    }

    private static string? ReadValue(string[] args, ref int index, string name, List<string> errors) {
        if (index + 1 >= args.Length) {
            errors.Add($"Missing value for {name}.");
            return null;
        }
        index++;
        var value = args[index];
        if (string.IsNullOrWhiteSpace(value)) {
            errors.Add($"Empty value for {name}.");
            return null;
        }
        return value;
    }

    private static bool IsValidProvider(string provider) {
        return IsValidAiProvider(provider) ||
               IsAzureProvider(provider);
    }

    private static bool IsValidAiProvider(string provider) {
        return ReviewProviderContracts.TryParseProviderAlias(provider, out _);
    }

    private static bool IsAzureProvider(string provider) {
        return provider.Equals("azure", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("azuredevops", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("azure-devops", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("ado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidCodeHost(string codeHost) {
        return codeHost.Equals("github", StringComparison.OrdinalIgnoreCase) ||
               IsAzureProvider(codeHost);
    }

    private static void ApplyRunOptions(RunOptions options) {
        var provider = options.Provider?.Trim();
        var codeHost = options.CodeHost?.Trim();
        if (!string.IsNullOrWhiteSpace(provider)) {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", provider);
            if (IsAzureProvider(provider)) {
                if (string.IsNullOrWhiteSpace(codeHost)) {
                    codeHost = "azure";
                }
            }
        }
        var providerFallback = options.ProviderFallback?.Trim();
        if (!string.IsNullOrWhiteSpace(providerFallback)) {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", providerFallback);
        }
        if (!string.IsNullOrWhiteSpace(codeHost)) {
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", codeHost);
        } else if (options.HasAzureOverrides) {
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", "azure");
        }
        if (!string.IsNullOrWhiteSpace(options.AzureOrg)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_ORG", options.AzureOrg);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureProject)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_PROJECT", options.AzureProject);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureRepo)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_REPO", options.AzureRepo);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureBaseUrl)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_BASE_URL", options.AzureBaseUrl);
        }
        if (!string.IsNullOrWhiteSpace(options.AzureTokenEnv)) {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_TOKEN_ENV", options.AzureTokenEnv);
        }
    }

    private static void PrintRunHelp() {
        PrintRunHelp(Console.Out);
    }

    private static void PrintRunHelp(TextWriter writer) {
        writer.WriteLine("Reviewer run options:");
        var leftWidth = RunOptionSpecs
            .Select(spec => $"{spec.Name} {spec.ValueHint}".Length)
            .DefaultIfEmpty(0)
            .Max();
        foreach (var spec in RunOptionSpecs) {
            var left = $"{spec.Name} {spec.ValueHint}".PadRight(leftWidth);
            writer.WriteLine($"  {left}  {spec.Description}");
        }
    }

    private static async Task<bool> ValidateAuthAsync(ReviewSettings settings) {
        var provider = ReviewProviderContracts.Get(settings.Provider);
        if (!provider.RequiresOpenAiAuthStore) {
            return true;
        }

        var authPath = AuthPaths.ResolveAuthPath();
        if (!File.Exists(authPath)) {
            Console.Error.WriteLine("Missing OpenAI auth store.");
            Console.Error.WriteLine("Set INTELLIGENCEX_AUTH_B64 (store export) or run `intelligencex auth login`.");
            return false;
        }

        try {
            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync("openai-codex").ConfigureAwait(false)
                         ?? await store.GetAsync("openai").ConfigureAwait(false)
                         ?? await store.GetAsync("chatgpt").ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine($"No OpenAI auth bundle found in {authPath}.");
                Console.Error.WriteLine("Export a store bundle with `intelligencex auth export --format store-base64`.");
                return false;
            }
            SecretsAudit.Record($"OpenAI auth bundle '{bundle.Provider}' from {authPath}");
            return true;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load auth store: {ex.Message}");
            if (ex.Message.Contains("INTELLIGENCEX_AUTH_KEY", StringComparison.OrdinalIgnoreCase)) {
                Console.Error.WriteLine("Set INTELLIGENCEX_AUTH_KEY to decrypt the auth store.");
            }
            return false;
        }
    }

    private static bool ShouldSkipByTitle(string title, IReadOnlyList<string> skipTitles) {
        if (skipTitles.Count == 0) {
            return false;
        }
        foreach (var skip in skipTitles) {
            if (!string.IsNullOrWhiteSpace(skip) && title.Contains(skip, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static bool ShouldSkipByLabels(IReadOnlyList<string> labels, IReadOnlyList<string> skipLabels) {
        if (labels.Count == 0 || skipLabels.Count == 0) {
            return false;
        }
        foreach (var label in labels) {
            foreach (var skip in skipLabels) {
                if (!string.IsNullOrWhiteSpace(skip) && label.Equals(skip, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool ShouldSkipByPaths(IReadOnlyList<PullRequestFile> files, IReadOnlyList<string> skipPaths) {
        if (files.Count == 0 || skipPaths.Count == 0) {
            return false;
        }
        var allMatch = true;
        foreach (var file in files) {
            var matches = skipPaths.Any(pattern => GlobMatcher.IsMatch(pattern, file.Filename));
            if (!matches) {
                allMatch = false;
                break;
            }
        }
        return allMatch;
    }

    internal static bool HasWorkflowChanges(IReadOnlyList<PullRequestFile> files) {
        foreach (var file in files) {
            if (IsWorkflowPath(file.Filename)) {
                return true;
            }
        }
        return false;
    }

    private static bool IsWorkflowPath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return normalized.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) PrepareFiles(IReadOnlyList<PullRequestFile> files,
        int maxFiles, int maxPatchChars) {
        var list = new List<PullRequestFile>();
        var truncatedPatches = 0;
        var count = 0;
        foreach (var file in files) {
            if (count >= maxFiles) {
                break;
            }
            var patch = file.Patch;
            if (!string.IsNullOrWhiteSpace(patch)) {
                var trimmed = TrimPatch(patch, maxPatchChars);
                if (!string.Equals(trimmed, patch, StringComparison.Ordinal)) {
                    truncatedPatches++;
                }
                patch = trimmed;
            }
            list.Add(new PullRequestFile(file.Filename, file.Status, patch));
            count++;
        }
        var budgetNote = BuildBudgetNote(files.Count, list.Count, truncatedPatches, maxPatchChars);
        return (list, budgetNote);
    }

    internal static string BuildBudgetNote(int totalFiles, int includedFiles, int truncatedPatches, int maxPatchChars) {
        var parts = new List<string>();
        if (totalFiles > includedFiles) {
            parts.Add($"showing first {includedFiles} of {totalFiles} files");
        }
        if (truncatedPatches > 0) {
            var label = truncatedPatches == 1 ? "patch" : "patches";
            parts.Add($"{truncatedPatches} {label} trimmed to {maxPatchChars} chars");
        }
        if (parts.Count == 0) {
            return string.Empty;
        }
        return $"Review context truncated: {string.Join("; ", parts)}.";
    }

    private static string ApplyEmbedPlacement(string reviewBody, string embedBlock, string placement) {
        var embed = embedBlock.Trim();
        if (embed.Length == 0) {
            return reviewBody;
        }
        var body = string.IsNullOrWhiteSpace(reviewBody) ? string.Empty : reviewBody.Trim();
        var normalizedPlacement = ReviewSettings.NormalizeEmbedPlacement(placement, "bottom");
        if (normalizedPlacement == "top") {
            return string.IsNullOrWhiteSpace(body) ? embed : embed + "\n\n" + body;
        }
        return string.IsNullOrWhiteSpace(body) ? embed : body + "\n\n" + embed;
    }

    /// <summary>
    /// Filters pull request files using include/exclude glob patterns.
    /// Binary/generated filters are applied first, then include patterns, and exclude patterns are applied last.
    /// </summary>
    internal static IReadOnlyList<PullRequestFile> FilterFilesByPaths(IReadOnlyList<PullRequestFile> files,
        IReadOnlyList<string> includePaths, IReadOnlyList<string> excludePaths,
        bool skipBinaryFiles = false, bool skipGeneratedFiles = false,
        IReadOnlyList<string>? generatedFileGlobs = null) {
        if (files.Count == 0) {
            return files;
        }
        includePaths ??= Array.Empty<string>();
        excludePaths ??= Array.Empty<string>();
        var hasInclude = includePaths.Count > 0;
        var hasExclude = excludePaths.Count > 0;
        if (!hasInclude && !hasExclude && !skipBinaryFiles && !skipGeneratedFiles) {
            return files;
        }
        var effectiveGeneratedGlobs = skipGeneratedFiles
            ? ResolveGeneratedGlobs(generatedFileGlobs)
            : Array.Empty<string>();
        var filtered = new List<PullRequestFile>();
        foreach (var file in files) {
            var filename = file.Filename.Replace('\\', '/');
            if (skipBinaryFiles && IsBinaryFile(filename)) {
                continue;
            }
            if (skipGeneratedFiles && IsGeneratedFile(filename, effectiveGeneratedGlobs)) {
                continue;
            }
            if (hasInclude && !includePaths.Any(pattern => GlobMatcher.IsMatch(pattern, filename))) {
                continue;
            }
            if (hasExclude && excludePaths.Any(pattern => GlobMatcher.IsMatch(pattern, filename))) {
                continue;
            }
            filtered.Add(file);
        }
        return filtered;
    }

    private static bool IsBinaryFile(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension)) {
            return false;
        }
        return BinaryExtensions.Contains(extension);
    }

    private static IReadOnlyList<string> ResolveGeneratedGlobs(IReadOnlyList<string>? generatedFileGlobs) {
        if (generatedFileGlobs is null || generatedFileGlobs.Count == 0) {
            return GeneratedGlobs;
        }
        var combined = new List<string>(GeneratedGlobs.Count + generatedFileGlobs.Count);
        combined.AddRange(GeneratedGlobs);
        combined.AddRange(generatedFileGlobs);
        return combined;
    }

    private static bool IsGeneratedFile(string path, IReadOnlyList<string> generatedGlobs) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        return generatedGlobs.Any(pattern => GlobMatcher.IsMatch(pattern, path));
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveReviewFilesAsync(
        IReviewCodeHostReader codeHostReader, PullRequestContext context, ReviewSettings settings, IReadOnlyList<PullRequestFile> currentFiles,
        CancellationToken cancellationToken) {
        var range = ReviewSettings.NormalizeDiffRange(settings.ReviewDiffRange, "current");
        return await ResolveDiffRangeFilesAsync(codeHostReader, context, range, currentFiles, settings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ReviewContextExtras> BuildExtrasAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        GitHubClient? fallbackGithub,
        PullRequestContext context, ReviewSettings settings, CancellationToken cancellationToken, bool forceReviewThreads) {
        var extras = new ReviewContextExtras();
        if (settings.IncludeIssueComments) {
            var comments = await codeHostReader.ListIssueCommentsAsync(context, settings.MaxComments, cancellationToken)
                .ConfigureAwait(false);
            extras.IssueCommentsSection = BuildIssueCommentsSection(comments, settings);
        }
        var loadThreads = forceReviewThreads || settings.IncludeReviewThreads || settings.ReviewThreadsAutoResolveAI;
        if (loadThreads) {
            var threads = await codeHostReader.ListPullRequestReviewThreadsAsync(context, settings.ReviewThreadsMax,
                    settings.ReviewThreadsMaxComments, cancellationToken)
                .ConfigureAwait(false);
            extras.ReviewThreads = threads;
            if (settings.ReviewThreadsAutoResolveStale) {
                await AutoResolveStaleThreadsAsync(github, fallbackGithub, threads, settings, cancellationToken).ConfigureAwait(false);
            }
            if (settings.IncludeReviewThreads) {
                extras.ReviewThreadsSection = BuildReviewThreadsSection(threads, settings);
            }
        }
        if (settings.IncludeReviewComments && string.IsNullOrEmpty(extras.ReviewThreadsSection)) {
            var comments = await codeHostReader.ListPullRequestReviewCommentsAsync(context, settings.MaxComments, cancellationToken)
                .ConfigureAwait(false);
            extras.ReviewCommentsSection = BuildReviewCommentsSection(comments, settings);
        }
        if (settings.IncludeRelatedPrs) {
            var query = ResolveRelatedPrsQuery(context, settings);
            if (!string.IsNullOrWhiteSpace(query)) {
                var related = await github.SearchPullRequestsAsync(query, settings.MaxRelatedPrs, cancellationToken)
                    .ConfigureAwait(false);
                extras.RelatedPrsSection = BuildRelatedPrsSection(context, related);
            }
        }
        return extras;
    }

    private static string BuildIssueCommentsSection(IReadOnlyList<IssueComment> comments, ReviewSettings settings) {
        var filtered = new List<IssueComment>();
        foreach (var comment in comments) {
            if (string.IsNullOrWhiteSpace(comment.Body)) {
                continue;
            }
            if (comment.Body.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase) ||
                comment.Body.Contains(CleanupFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!ShouldIncludeComment(comment.Author, comment.Body, settings)) {
                continue;
            }
            filtered.Add(comment);
        }
        if (filtered.Count == 0) {
            return string.Empty;
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Issue comments (most recent first):");
        foreach (var comment in filtered) {
            var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author;
            var body = TrimComment(comment.Body, settings.MaxCommentChars);
            sb.AppendLine($"- {author}: {body}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildReviewCommentsSection(IReadOnlyList<PullRequestReviewComment> comments, ReviewSettings settings) {
        if (comments.Count == 0) {
            return string.Empty;
        }
        var filtered = new List<PullRequestReviewComment>();
        foreach (var comment in comments) {
            if (string.IsNullOrWhiteSpace(comment.Body)) {
                continue;
            }
            if (!ShouldIncludeComment(comment.Author, comment.Body, settings)) {
                continue;
            }
            filtered.Add(comment);
        }
        if (filtered.Count == 0) {
            return string.Empty;
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Review comments (most recent first):");
        foreach (var comment in filtered) {
            var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author;
            var body = TrimComment(comment.Body, settings.MaxCommentChars);
            var location = string.IsNullOrWhiteSpace(comment.Path)
                ? string.Empty
                : comment.Line.HasValue
                    ? $" ({comment.Path}:{comment.Line.Value})"
                    : $" ({comment.Path})";
            sb.AppendLine($"- {author}{location}: {body}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildReviewThreadsSection(IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings) {
        if (threads.Count == 0 || settings.ReviewThreadsMaxComments <= 0) {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var thread in threads) {
            if (!settings.ReviewThreadsIncludeResolved && thread.IsResolved) {
                continue;
            }
            if (!settings.ReviewThreadsIncludeOutdated && thread.IsOutdated) {
                continue;
            }

            var status = thread.IsResolved && thread.IsOutdated
                ? "resolved (stale)"
                : thread.IsResolved
                    ? "resolved"
                    : thread.IsOutdated
                        ? "stale"
                        : "active";
            var perThread = 0;
            foreach (var comment in thread.Comments) {
                if (perThread >= settings.ReviewThreadsMaxComments) {
                    break;
                }
                if (!ShouldIncludeThreadComment(comment.Author, comment.Body, settings)) {
                    continue;
                }
                var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author!;
                var body = TrimComment(comment.Body, settings.MaxCommentChars);
                var location = string.IsNullOrWhiteSpace(comment.Path)
                    ? string.Empty
                    : comment.Line.HasValue
                        ? $" ({comment.Path}:{comment.Line.Value})"
                        : $" ({comment.Path})";
                lines.Add($"- [{status}] {author}{location}: {body}");
                perThread++;
            }
        }

        if (lines.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Other reviewer threads (status: active/resolved/stale):");
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static async Task AutoResolveStaleThreadsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings, CancellationToken cancellationToken) {
        var resolved = 0;
        foreach (var thread in threads) {
            if (resolved >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (thread.IsResolved || !thread.IsOutdated) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolved++;
                continue;
            }
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {result.Error ?? "unknown error"}");
        }
    }

    private static async Task AutoResolveMissingInlineThreadsAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        GitHubClient? fallbackGithub, PullRequestContext context, HashSet<string>? expectedKeys, ReviewSettings settings,
        CancellationToken cancellationToken) {
        if (settings.ReviewThreadsAutoResolveMax <= 0) {
            return;
        }

        var keys = expectedKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxThreads = Math.Max(settings.ReviewThreadsAutoResolveMax, settings.ReviewThreadsMax);
        var maxComments = Math.Max(1, settings.ReviewThreadsMaxComments);
        var threads = await codeHostReader.ListPullRequestReviewThreadsAsync(context, maxThreads, maxComments, cancellationToken)
            .ConfigureAwait(false);

        var resolved = 0;
        foreach (var thread in threads) {
            if (resolved >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (thread.IsResolved) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                continue;
            }
            if (!TryGetInlineThreadKey(thread, settings, out var key)) {
                continue;
            }
            if (keys.Contains(key)) {
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolved++;
                continue;
            }
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {result.Error ?? "unknown error"}");
        }
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveThreadTriageFilesAsync(
        IReviewCodeHostReader codeHostReader, PullRequestContext context, ReviewSettings settings,
        IReadOnlyList<PullRequestFile> currentFiles, CancellationToken cancellationToken) {
        var range = ReviewSettings.NormalizeDiffRange(settings.ReviewThreadsAutoResolveDiffRange, "current");
        return await ResolveDiffRangeFilesAsync(codeHostReader, context, range, currentFiles, settings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveDiffRangeFilesAsync(
        IReviewCodeHostReader codeHostReader, PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles,
        ReviewSettings settings, CancellationToken cancellationToken) {
        if (range == "current") {
            return (currentFiles, "current PR files");
        }
        if (string.IsNullOrWhiteSpace(context.HeadSha)) {
            return (currentFiles, "current PR files (missing head SHA)");
        }

        async Task<(bool Success, IReadOnlyList<PullRequestFile> Files, string Note)> TryCompareAsync(string? baseSha, string label) {
            if (string.IsNullOrWhiteSpace(baseSha)) {
                return (false, Array.Empty<PullRequestFile>(), $"missing {label} commit");
            }
            try {
                var compareResult = await codeHostReader.GetCompareFilesAsync(context, baseSha, context.HeadSha!, cancellationToken)
                    .ConfigureAwait(false);
                var compareFiles = compareResult.Files;
                if (compareFiles.Count == 0) {
                    return (false, Array.Empty<PullRequestFile>(), $"{label} diff empty");
                }
                var filtered = FilterFilesByPaths(compareFiles, settings.IncludePaths, settings.ExcludePaths,
                    settings.SkipBinaryFiles, settings.SkipGeneratedFiles, settings.GeneratedFileGlobs);
                var note = $"{label} → head ({ShortSha(baseSha)}..{ShortSha(context.HeadSha)})";
                if (filtered.Count == 0) {
                    note += " (filtered empty)";
                }
                return (true, filtered, note);
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to load {label} diff: {ex.Message}");
                return (false, Array.Empty<PullRequestFile>(), $"failed to load {label} diff");
            }
        }

        if (range == "pr-base") {
            var result = await TryCompareAsync(context.BaseSha, "PR base").ConfigureAwait(false);
            return result.Success
                ? (result.Files, result.Note)
                : (currentFiles, $"current PR files ({result.Note})");
        }

        var firstReviewSha = await FindOldestSummaryCommitAsync(codeHostReader, context, settings, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(firstReviewSha)) {
            var prBaseResult = await TryCompareAsync(context.BaseSha, "PR base fallback").ConfigureAwait(false);
            if (prBaseResult.Success) {
                return (prBaseResult.Files, prBaseResult.Note);
            }
            return (currentFiles, $"current PR files (missing first review commit; {prBaseResult.Note})");
        }

        var firstReviewResult = await TryCompareAsync(firstReviewSha, "first review").ConfigureAwait(false);
        if (firstReviewResult.Success) {
            return (firstReviewResult.Files, firstReviewResult.Note);
        }

        var prBaseFallback = await TryCompareAsync(context.BaseSha, "PR base fallback").ConfigureAwait(false);
        if (prBaseFallback.Success) {
            return (prBaseFallback.Files, prBaseFallback.Note);
        }

        var note = firstReviewResult.Note;
        if (!string.IsNullOrWhiteSpace(prBaseFallback.Note)) {
            note = string.IsNullOrWhiteSpace(note) ? prBaseFallback.Note : $"{note}; {prBaseFallback.Note}";
        }
        return (currentFiles, $"current PR files ({note})");
    }

    private static async Task<ThreadTriageResult> MaybeAutoResolveAssessedThreadsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        ReviewRunner runner, PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        ReviewContextExtras extras, bool reviewFailed, string? diffNote, CancellationToken cancellationToken,
        bool force, bool allowCommentPost) {
        if ((!settings.ReviewThreadsAutoResolveAI && !force) || reviewFailed) {
            return ThreadTriageResult.Empty;
        }
        if (extras.ReviewThreads.Count == 0) {
            return ThreadTriageResult.Empty;
        }

        var candidates = SelectAssessmentCandidates(extras.ReviewThreads, settings);
        if (candidates.Count == 0) {
            return ThreadTriageResult.Empty;
        }

        var prompt = BuildThreadAssessmentPrompt(context, candidates, files, settings, diffNote);
        if (settings.RedactPii) {
            prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
        }

        var output = await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
        if (ReviewDiagnostics.IsFailureBody(output)) {
            return ThreadTriageResult.Empty;
        }

        var assessments = ParseThreadAssessments(output);
        if (assessments.Count == 0) {
            Console.Error.WriteLine("Thread assessment returned no usable results.");
            return ThreadTriageResult.Empty;
        }

        var byId = new Dictionary<string, ThreadAssessment>(StringComparer.OrdinalIgnoreCase);
        var missingIdCount = 0;
        var duplicateIdCount = 0;
        var duplicateIdExamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assessment in assessments) {
            var normalizedId = NormalizeThreadAssessmentId(assessment.Id);
            if (normalizedId.Length == 0) {
                missingIdCount++;
                continue;
            }
            if (!byId.TryAdd(normalizedId, assessment)) {
                duplicateIdCount++;
                if (duplicateIdExamples.Count < 3) {
                    duplicateIdExamples.Add(normalizedId);
                }
                // Last occurrence wins to keep deterministic behavior without throwing.
                byId[normalizedId] = assessment;
            }
        }
        if (missingIdCount > 0) {
            Console.Error.WriteLine($"Thread assessment skipped {missingIdCount} item(s) with missing ids.");
        }
        if (duplicateIdCount > 0) {
            var examples = duplicateIdExamples.Count > 0 ? $" (e.g., {string.Join(", ", duplicateIdExamples)})" : string.Empty;
            Console.Error.WriteLine($"Thread assessment contained {duplicateIdCount} duplicate id(s){examples}; using last occurrence.");
        }
        var replyMap = new Dictionary<string, ThreadAssessment>(StringComparer.OrdinalIgnoreCase);
        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, settings.MaxPatchChars);
        var resolved = new List<ThreadAssessment>();
        var kept = new List<ThreadAssessment>();
        var failed = new List<ThreadAssessment>();
        foreach (var assessment in assessments) {
            switch (assessment.Action) {
                case "comment":
                case "keep":
                    kept.Add(assessment);
                    var normalizedReplyId = NormalizeThreadAssessmentId(assessment.Id);
                    if (normalizedReplyId.Length > 0) {
                        replyMap[normalizedReplyId] = assessment;
                    }
                    break;
            }
        }

        var resolvedCount = 0;
        foreach (var thread in candidates) {
            if (resolvedCount >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            var normalizedThreadId = NormalizeThreadAssessmentId(thread.Id);
            if (!byId.TryGetValue(normalizedThreadId, out var assessment) || assessment.Action != "resolve") {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveRequireEvidence &&
                !HasValidResolveEvidence(assessment.Evidence, thread, patchIndex, patchLookup, settings.MaxPatchChars)) {
                var missingEvidence = new ThreadAssessment(assessment.Id, "keep",
                    $"{assessment.Reason} (missing diff evidence)", assessment.Evidence);
                kept.Add(missingEvidence);
                replyMap[normalizedThreadId] = missingEvidence;
                continue;
            }
            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolvedCount++;
                resolved.Add(assessment);
                continue;
            }
            var error = result.Error ?? "unknown error";
            var failedAssessment = new ThreadAssessment(assessment.Id, "keep", $"{assessment.Reason} (resolve failed: {error})",
                assessment.Evidence);
            failed.Add(failedAssessment);
            replyMap[normalizedThreadId] = failedAssessment;
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {error}");
        }

        if (failed.Count > 0) {
            kept.AddRange(failed);
        }

        var commentPosted = false;
        var triageBody = BuildThreadAssessmentComment(resolved, kept, context.HeadSha, diffNote);
        if (settings.ReviewThreadsAutoResolveAIReply && replyMap.Count > 0) {
            await ReplyToKeptThreadsAsync(github, context, candidates, replyMap, context.HeadSha, diffNote, settings, cancellationToken)
                .ConfigureAwait(false);
        }
        if (allowCommentPost && settings.ReviewThreadsAutoResolveAIPostComment &&
            !settings.ReviewThreadsAutoResolveAIEmbed && kept.Count > 0) {
            var body = triageBody;
            await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
                .ConfigureAwait(false);
            commentPosted = true;
        }
        if (settings.ReviewThreadsAutoResolveSummaryComment && !commentPosted && (resolved.Count > 0 || kept.Count > 0)) {
            var body = BuildThreadAutoResolveSummaryComment(resolved, kept, context.HeadSha, diffNote);
            await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
                .ConfigureAwait(false);
            commentPosted = true;
        }

        if (resolvedCount == 0 && kept.Count == 0) {
            return ThreadTriageResult.Empty;
        }
        var summary = $"Auto-resolve (AI): {resolvedCount} resolved, {kept.Count} kept.";
        if (commentPosted) {
            summary += " Triage comment posted.";
        }
        var fallbackSummary = BuildFallbackTriageSummary(resolved, kept);
        return new ThreadTriageResult(summary, triageBody, fallbackSummary);
    }

    private static async Task<string> TryBuildUsageLineAsync(ReviewSettings settings, ReviewProvider providerKind) {
        if (!settings.ReviewUsageSummary) {
            return string.Empty;
        }
        var provider = ReviewProviderContracts.Get(providerKind);
        if (!provider.SupportsUsageApi) {
            return string.Empty;
        }

        try {
            var snapshot = await TryGetUsageSnapshotAsync(settings).ConfigureAwait(false);
            if (snapshot is null) {
                return string.Empty;
            }
            var summary = FormatUsageSummary(snapshot);
            return string.IsNullOrWhiteSpace(summary) ? string.Empty : summary;
        } catch (Exception ex) {
            if (settings.Diagnostics) {
                Console.Error.WriteLine($"Usage summary failed: {ex.Message}");
            }
            return string.Empty;
        }
    }

    private static async Task<ChatGptUsageSnapshot?> TryGetUsageSnapshotAsync(ReviewSettings settings) {
        var cacheMinutes = Math.Max(0, settings.ReviewUsageSummaryCacheMinutes);
        if (cacheMinutes > 0 && ChatGptUsageCache.TryLoad(out var entry) && entry is not null) {
            var age = DateTimeOffset.UtcNow - entry.UpdatedAt;
            if (age <= TimeSpan.FromMinutes(cacheMinutes)) {
                return entry.Snapshot;
            }
        }

        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(1, settings.ReviewUsageSummaryTimeoutSeconds)));
        var options = new OpenAINativeOptions {
            AuthStore = new FileAuthBundleStore()
        };
        using var service = new ChatGptUsageService(options);
        var snapshot = await service.GetUsageSnapshotAsync(cts.Token).ConfigureAwait(false);
        try {
            ChatGptUsageCache.Save(snapshot);
        } catch {
            // Best-effort cache.
        }
        return snapshot;
    }

    private static string FormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        var lines = new List<string>();

        var primary = FormatRateLimitLine(snapshot.RateLimit?.PrimaryWindow, "rate limit");
        if (!string.IsNullOrWhiteSpace(primary)) {
            lines.Add(primary);
        }

        var secondary = FormatRateLimitLine(snapshot.RateLimit?.SecondaryWindow, "rate limit (secondary)");
        if (!string.IsNullOrWhiteSpace(secondary)) {
            lines.Add(secondary);
        }

        var codePrimary = FormatRateLimitLine(snapshot.CodeReviewRateLimit?.PrimaryWindow, "code review limit");
        if (!string.IsNullOrWhiteSpace(codePrimary)) {
            lines.Add(codePrimary);
        }

        var codeSecondary = FormatRateLimitLine(snapshot.CodeReviewRateLimit?.SecondaryWindow, "code review limit (secondary)");
        if (!string.IsNullOrWhiteSpace(codeSecondary)) {
            lines.Add(codeSecondary);
        }

        if (snapshot.Credits is not null) {
            if (snapshot.Credits.Unlimited) {
                lines.Add("credits: unlimited");
            } else if (snapshot.Credits.Balance.HasValue) {
                lines.Add($"credits: {snapshot.Credits.Balance.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            } else if (!snapshot.Credits.HasCredits) {
                lines.Add("credits: none");
            }
        }

        if (lines.Count == 0) {
            return string.Empty;
        }
        return "Usage: " + string.Join(" | ", lines);
    }

    private static string? FormatRateLimitLine(ChatGptRateLimitWindow? window, string fallbackLabel) {
        if (window is null) {
            return null;
        }
        var remaining = FormatRemainingPercent(window.UsedPercent);
        if (string.IsNullOrWhiteSpace(remaining)) {
            return null;
        }
        var label = FormatWindowLabel(window) ?? fallbackLabel;
        return $"{label}: {remaining}% remaining";
    }

    private static string? FormatWindowLabel(ChatGptRateLimitWindow window) {
        if (!window.LimitWindowSeconds.HasValue) {
            return null;
        }
        var seconds = Math.Max(0, window.LimitWindowSeconds.Value);
        if (IsWithin(seconds, 5 * 3600, 600)) {
            return "5h limit";
        }
        if (IsWithin(seconds, 7 * 24 * 3600, 3600)) {
            return "weekly limit";
        }
        if (IsWithin(seconds, 24 * 3600, 3600)) {
            return "daily limit";
        }
        if (IsWithin(seconds, 3600, 120)) {
            return "hourly limit";
        }
        return $"{FormatDuration(seconds)} limit";
    }

    private static bool IsWithin(long value, long target, long tolerance) {
        return Math.Abs(value - target) <= tolerance;
    }

    private static string FormatDuration(long seconds) {
        if (seconds <= 0) {
            return "0s";
        }
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1 && span.TotalDays % 1 == 0) {
            return $"{(int)span.TotalDays}d";
        }
        if (span.TotalHours >= 1 && span.TotalHours % 1 == 0) {
            return $"{(int)span.TotalHours}h";
        }
        if (span.TotalMinutes >= 1) {
            return $"{(int)Math.Round(span.TotalMinutes)}m";
        }
        return $"{(int)Math.Round(span.TotalSeconds)}s";
    }

    private static string? FormatRemainingPercent(double? usedPercent) {
        if (!usedPercent.HasValue) {
            return null;
        }
        var remaining = Math.Max(0, 100 - usedPercent.Value);
        return remaining.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static async Task<string?> FindOldestSummaryCommitAsync(IReviewCodeHostReader codeHostReader, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var limit = Math.Max(0, settings.CommentSearchLimit);
        var comments = await codeHostReader.ListIssueCommentsAsync(context, limit, cancellationToken)
            .ConfigureAwait(false);
        string? oldest = null;
        foreach (var comment in comments) {
            if (!comment.Body.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var commit = ExtractReviewedCommit(comment.Body);
            if (!string.IsNullOrWhiteSpace(commit)) {
                oldest = commit;
            }
        }
        return oldest;
    }

    private static string ShortSha(string? sha) {
        if (string.IsNullOrWhiteSpace(sha)) {
            return "?";
        }
        return sha.Length > 7 ? sha.Substring(0, 7) : sha;
    }

    private static List<PullRequestReviewThread> SelectAssessmentCandidates(IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings) {
        var candidates = new List<PullRequestReviewThread>();
        foreach (var thread in threads) {
            if (thread.IsResolved) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                continue;
            }
            candidates.Add(thread);
            if (candidates.Count >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
        }
        return candidates;
    }

    private static string BuildThreadAssessmentPrompt(PullRequestContext context, IReadOnlyList<PullRequestReviewThread> threads,
        IReadOnlyList<PullRequestFile> files, ReviewSettings settings, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("You are reviewing existing PR review threads to decide if they should be resolved.");
        sb.AppendLine("Only mark a thread as resolved if the current diff clearly addresses the comment.");
        sb.AppendLine("If a human reply in the thread confirms the issue is fixed/expected/accepted, resolve it.");
        sb.AppendLine("If unclear or still valid, keep it open.");
        sb.AppendLine("If diff context shows `<file patch>`, it is the file-level diff because line-level context was unavailable.");
        sb.AppendLine();
        sb.AppendLine("Return JSON only in this format:");
        sb.AppendLine("{\"threads\":[{\"id\":\"...\",\"action\":\"resolve|keep|comment\",\"reason\":\"...\",\"evidence\":\"...\"}]}");
        sb.AppendLine("When resolving, evidence must quote a line from the diff context (e.g., \"42: fixed null check\").");
        sb.AppendLine();
        sb.AppendLine($"PR: {context.Owner}/{context.Repo} #{context.Number}");
        if (!string.IsNullOrWhiteSpace(context.Title)) {
            sb.AppendLine($"Title: {context.Title}");
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"Diff range: {diffNote}");
        }
        sb.AppendLine();

        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, settings.MaxPatchChars);
        var index = 1;
        foreach (var thread in threads) {
            sb.AppendLine($"Thread {index}:");
            sb.AppendLine($"id: {thread.Id}");
            sb.AppendLine($"status: {(thread.IsOutdated ? "stale" : "active")}");
            var location = TryGetThreadLocation(thread, out var path, out var line)
                ? line > 0 ? $"{path}:{line}" : path
                : "<unknown>";
            sb.AppendLine($"location: {location}");
            sb.AppendLine("comments:");
            var commentCount = 0;
            foreach (var comment in thread.Comments) {
                if (commentCount >= settings.ReviewThreadsMaxComments) {
                    break;
                }
                var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author!;
                var body = TrimComment(comment.Body, settings.MaxCommentChars);
                sb.AppendLine($"- {author}: {body}");
                commentCount++;
            }
            sb.AppendLine("diff context:");
            sb.AppendLine("```");
            sb.AppendLine(BuildThreadDiffContext(patchIndex, patchLookup, path, line, settings.MaxPatchChars));
            sb.AppendLine("```");
            sb.AppendLine();
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    private static bool TryGetThreadLocation(PullRequestReviewThread thread, out string path, out int line) {
        path = string.Empty;
        line = 0;
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Path)) {
                continue;
            }
            path = comment.Path!;
            if (comment.Line.HasValue) {
                line = (int)comment.Line.Value;
            }
            return true;
        }
        return false;
    }

    private static string BuildThreadDiffContext(Dictionary<string, List<PatchLine>> patchIndex,
        IReadOnlyDictionary<string, string> patchLookup, string path, int lineNumber, int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(path)) {
            return "<unavailable>";
        }
        if (lineNumber <= 0) {
            var normalizedPath = NormalizePath(path);
            return TryBuildFallbackPatch(patchLookup, normalizedPath, maxPatchChars);
        }
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized) || !patchIndex.TryGetValue(normalized, out var lines) || lines.Count == 0) {
            return TryBuildFallbackPatch(patchLookup, normalized, maxPatchChars);
        }
        var context = 3;
        var lower = lineNumber - context;
        var upper = lineNumber + context;
        var snippet = lines.Where(l => l.LineNumber >= lower && l.LineNumber <= upper)
            .Select(l => $"{l.LineNumber}: {l.Text}")
            .ToList();
        if (snippet.Count > 0) {
            return string.Join("\n", snippet);
        }
        return TryBuildFallbackPatch(patchLookup, normalized, maxPatchChars);
    }

    private static Dictionary<string, string> BuildPatchLookup(IReadOnlyList<PullRequestFile> files, int maxPatchChars) {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalized = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalized)) {
                continue;
            }
            lookup[normalized] = TrimPatch(file.Patch!, maxPatchChars);
        }
        return lookup;
    }

    private static string TryBuildFallbackPatch(IReadOnlyDictionary<string, string> patchLookup, string normalizedPath,
        int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(normalizedPath)) {
            return "<unavailable>";
        }
        if (!patchLookup.TryGetValue(normalizedPath, out var patch) || string.IsNullOrWhiteSpace(patch)) {
            return "<unavailable>";
        }
        var limited = TrimPatch(patch, maxPatchChars);
        return $"<file patch>\n{limited}";
    }

    private static string TrimPatch(string patch, int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(patch)) {
            return string.Empty;
        }
        if (maxPatchChars <= 0 || patch.Length <= maxPatchChars) {
            return patch;
        }
        var newline = patch.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = patch.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var headerLines = new List<string>();
        var hunks = new List<List<string>>();
        List<string>? current = null;

        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                if (current is not null) {
                    hunks.Add(current);
                }
                current = new List<string> { line };
                continue;
            }
            if (current is null) {
                headerLines.Add(line);
            } else {
                current.Add(line);
            }
        }

        if (current is not null) {
            hunks.Add(current);
        }

        if (hunks.Count == 0) {
            return TrimHard(normalized, maxPatchChars, newline);
        }

        var header = string.Join(newline, headerLines);
        if (header.Length > maxPatchChars) {
            return TrimHard(header, maxPatchChars, newline);
        }

        var hunkTexts = hunks.Select(hunk => string.Join(newline, hunk)).ToList();
        if (hunkTexts.Count <= 1) {
            return AppendSequential(header, hunkTexts, maxPatchChars, newline);
        }

        var marker = "... (truncated) ...";
        var sb = new StringBuilder(maxPatchChars + 32);
        if (!string.IsNullOrEmpty(header)) {
            sb.Append(header);
        }

        if (!TryAppendSegment(sb, hunkTexts[0], maxPatchChars, newline)) {
            return TrimHard(header, maxPatchChars, newline);
        }

        if (hunkTexts.Count == 2) {
            if (TryAppendSegment(sb, hunkTexts[1], maxPatchChars, newline)) {
                return sb.ToString();
            }

            var fallback = new StringBuilder(maxPatchChars + 32);
            if (!string.IsNullOrEmpty(header)) {
                fallback.Append(header);
            }
            if (CanAppendTail(fallback, maxPatchChars, newline.Length, marker, hunkTexts[1], includeMarker: true)) {
                TryAppendSegment(fallback, marker, maxPatchChars, newline);
                TryAppendSegment(fallback, hunkTexts[1], maxPatchChars, newline);
                return fallback.ToString();
            }
            if (CanAppendTail(fallback, maxPatchChars, newline.Length, marker, hunkTexts[1], includeMarker: false)) {
                TryAppendSegment(fallback, hunkTexts[1], maxPatchChars, newline);
                return fallback.ToString();
            }

            TryAppendSegment(sb, marker, maxPatchChars, newline);
            return sb.ToString();
        }

        var lastHunk = hunkTexts[^1];
        var includedMiddle = 0;
        for (var i = 1; i < hunkTexts.Count - 1; i++) {
            var hunkText = hunkTexts[i];
            if (!CanAppendWithReserve(sb, hunkText, maxPatchChars, newline, marker, lastHunk)) {
                break;
            }
            if (!TryAppendSegment(sb, hunkText, maxPatchChars, newline)) {
                break;
            }
            includedMiddle++;
        }

        var needsTruncation = includedMiddle < hunkTexts.Count - 2;
        if (needsTruncation) {
            if (CanAppendTail(sb, maxPatchChars, newline.Length, marker, lastHunk, includeMarker: true)) {
                TryAppendSegment(sb, marker, maxPatchChars, newline);
                TryAppendSegment(sb, lastHunk, maxPatchChars, newline);
            } else if (CanAppendTail(sb, maxPatchChars, newline.Length, marker, lastHunk, includeMarker: false)) {
                TryAppendSegment(sb, lastHunk, maxPatchChars, newline);
            } else {
                TryAppendSegment(sb, marker, maxPatchChars, newline);
            }
        } else {
            TryAppendSegment(sb, lastHunk, maxPatchChars, newline);
        }

        return sb.ToString();
    }

    private static string AppendSequential(string header, IReadOnlyList<string> hunks, int maxPatchChars, string newline) {
        var sb = new StringBuilder(maxPatchChars + 32);
        if (!string.IsNullOrEmpty(header)) {
            sb.Append(header);
        }

        foreach (var hunk in hunks) {
            if (!TryAppendSegment(sb, hunk, maxPatchChars, newline)) {
                TryAppendSegment(sb, "... (truncated) ...", maxPatchChars, newline);
                break;
            }
        }
        return sb.ToString();
    }

    private static bool CanAppendWithReserve(StringBuilder sb, string segment, int maxChars, string newline,
        string lastText, string marker, bool includeMarker) {
        if (string.IsNullOrEmpty(segment)) {
            return true;
        }
        var extra = segment.Length + (sb.Length > 0 ? newline.Length : 0);
        var reserve = 0;
        if (includeMarker) {
            reserve += marker.Length + newline.Length;
        }
        if (!string.IsNullOrEmpty(lastText)) {
            reserve += lastText.Length + newline.Length;
        }
        return sb.Length + extra + reserve <= maxChars;
    }

    private static bool CanAppendTail(StringBuilder sb, int maxChars, string newline,
        string lastText, string marker, bool includeMarker) {
        var currentLength = sb.Length;
        if (includeMarker) {
            var markerExtra = marker.Length + (currentLength > 0 ? newline.Length : 0);
            if (currentLength + markerExtra > maxChars) {
                return false;
            }
            currentLength += markerExtra;
        }
        if (string.IsNullOrEmpty(lastText)) {
            return true;
        }
        var lastExtra = lastText.Length + (currentLength > 0 ? newline.Length : 0);
        return currentLength + lastExtra <= maxChars;
    }

    private static bool TryAppendSegment(StringBuilder sb, string segment, int maxChars, string newline) {
        if (string.IsNullOrEmpty(segment)) {
            return true;
        }
        var extra = segment.Length + (sb.Length > 0 ? newline.Length : 0);
        if (sb.Length + extra > maxChars) {
            return false;
        }
        if (sb.Length > 0) {
            sb.Append(newline);
        }
        sb.Append(segment);
        return true;
    }

    private static bool CanAppendWithReserve(StringBuilder sb, string segment, int maxChars, string newline,
        string marker, string lastSegment) {
        var length = AppendLength(sb.Length, segment.Length, newline.Length);
        if (!string.IsNullOrEmpty(marker)) {
            length = AppendLength(length, marker.Length, newline.Length);
        }
        if (!string.IsNullOrEmpty(lastSegment)) {
            length = AppendLength(length, lastSegment.Length, newline.Length);
        }
        return length <= maxChars;
    }

    private static bool CanAppendTail(StringBuilder sb, int maxChars, int newlineLength, string marker,
        string lastSegment, bool includeMarker) {
        var length = sb.Length;
        if (includeMarker && !string.IsNullOrEmpty(marker)) {
            length = AppendLength(length, marker.Length, newlineLength);
        }
        if (!string.IsNullOrEmpty(lastSegment)) {
            length = AppendLength(length, lastSegment.Length, newlineLength);
        }
        return length <= maxChars;
    }

    private static int AppendLength(int currentLength, int segmentLength, int newlineLength) {
        return currentLength + segmentLength + (currentLength > 0 ? newlineLength : 0);
    }

    private static string TrimHard(string text, int maxChars, string newline) {
        if (text.Length <= maxChars) {
            return text;
        }
        var suffix = $"{newline}... (truncated)";
        if (suffix.Length >= maxChars) {
            return text.Substring(0, maxChars);
        }
        var take = Math.Max(0, maxChars - suffix.Length);
        return text.Substring(0, take) + suffix;
    }

    private static string BuildThreadAssessmentComment(IReadOnlyList<ThreadAssessment> resolved,
        IReadOnlyList<ThreadAssessment> kept, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- intelligencex:thread-triage -->");
        sb.AppendLine("### IntelligenceX thread triage");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"_Diff range: {diffNote}_");
            sb.AppendLine();
        }
        if (resolved.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Resolved:");
            foreach (var item in resolved) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
        }
        if (kept.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Needs attention:");
            foreach (var item in kept) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    internal static string BuildFallbackTriageSummary(IReadOnlyList<ThreadAssessment> resolved,
        IReadOnlyList<ThreadAssessment> kept) {
        if (resolved.Count == 0 && kept.Count == 0) {
            return string.Empty;
        }
        if (resolved.Count > 0 && kept.Count == 0) {
            return $"Auto-resolve: resolved {resolved.Count} thread(s).";
        }
        if (resolved.Count == 0 && kept.Count > 0) {
            return $"Auto-resolve: kept {kept.Count} thread(s).";
        }
        return $"Auto-resolve: resolved {resolved.Count}, kept {kept.Count} thread(s).";
    }

    private static string BuildThreadAutoResolveSummaryComment(IReadOnlyList<ThreadAssessment> resolved,
        IReadOnlyList<ThreadAssessment> kept, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- intelligencex:thread-autoresolve-summary -->");
        sb.AppendLine("### IntelligenceX auto-resolve summary");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"_Diff range: {diffNote}_");
            sb.AppendLine();
        }
        if (resolved.Count > 0) {
            sb.AppendLine("Resolved:");
            foreach (var item in resolved) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
            sb.AppendLine();
        }
        if (kept.Count > 0) {
            sb.AppendLine("Kept:");
            foreach (var item in kept) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task ReplyToKeptThreadsAsync(GitHubClient github, PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> candidates, IReadOnlyDictionary<string, ThreadAssessment> assessments,
        string? headSha, string? diffNote, ReviewSettings settings, CancellationToken cancellationToken) {
        var replies = 0;
        foreach (var thread in candidates) {
            if (replies >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (!assessments.TryGetValue(thread.Id, out var assessment)) {
                continue;
            }
            if (assessment.Action == "resolve") {
                continue;
            }
            if (ThreadHasAutoReply(thread)) {
                continue;
            }
            var target = GetReplyTargetComment(thread);
            if (target?.DatabaseId is null) {
                continue;
            }
            var body = BuildThreadReply(assessment, headSha, diffNote);
            try {
                await github.CreatePullRequestReviewCommentReplyAsync(context.Owner, context.Repo, context.Number,
                        target.DatabaseId.Value, body, cancellationToken)
                    .ConfigureAwait(false);
                replies++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to reply to thread {thread.Id}: {ex.Message}");
            }
        }
    }

    private static bool ThreadHasAutoReply(PullRequestReviewThread thread) {
        foreach (var comment in thread.Comments) {
            if (!string.IsNullOrWhiteSpace(comment.Body) &&
                comment.Body.Contains(ThreadReplyMarker, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static PullRequestReviewThreadComment? GetReplyTargetComment(PullRequestReviewThread thread) {
        if (thread.Comments.Count == 0) {
            return null;
        }
        var withDates = thread.Comments.Where(c => c.CreatedAt.HasValue).ToList();
        if (withDates.Count > 0) {
            return withDates.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
        }
        return thread.Comments[^1];
    }

    private static string BuildThreadReply(ThreadAssessment assessment, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine(ThreadReplyMarker);
        sb.AppendLine($"IntelligenceX triage: {assessment.Reason}");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine();
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine();
            sb.AppendLine($"_Diff range: {diffNote}_");
        }
        return sb.ToString().TrimEnd();
    }

    private readonly record struct ThreadTriageResult(string SummaryLine, string EmbeddedBlock, string FallbackSummary) {
        public static ThreadTriageResult Empty => new(string.Empty, string.Empty, string.Empty);
    }

    private static List<ThreadAssessment> ParseThreadAssessments(string output) {
        var result = new List<ThreadAssessment>();
        var obj = TryParseJsonObject(output);
        var array = obj?.GetArray("threads");
        if (array is null) {
            return result;
        }
        foreach (var item in array) {
            var thread = item?.AsObject();
            if (thread is null) {
                continue;
            }
            var id = thread.GetString("id");
            var actionRaw = thread.GetString("action");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(actionRaw)) {
                continue;
            }
            var action = actionRaw.Trim().ToLowerInvariant();
            if (action is not "resolve" and not "keep" and not "comment") {
                action = "keep";
            }
            var reason = thread.GetString("reason") ?? string.Empty;
            var evidence = thread.GetString("evidence") ?? string.Empty;
            result.Add(new ThreadAssessment(id.Trim(), action, reason.Trim(), evidence.Trim()));
        }
        return result;
    }

    private static JsonObject? TryParseJsonObject(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return null;
        }
        var trimmed = output.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) {
            return null;
        }
        var json = trimmed.Substring(start, end - start + 1);
        var value = JsonLite.Parse(json);
        return value?.AsObject();
    }

    internal sealed record ThreadAssessment(string Id, string Action, string Reason, string Evidence);

    private static bool HasValidResolveEvidence(string? evidence, PullRequestReviewThread thread,
        Dictionary<string, List<PatchLine>> patchIndex, IReadOnlyDictionary<string, string> patchLookup,
        int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(evidence)) {
            return false;
        }
        if (!TryGetThreadLocation(thread, out var path, out var line)) {
            return false;
        }
        var context = BuildThreadDiffContext(patchIndex, patchLookup, path, line, maxPatchChars);
        if (string.IsNullOrWhiteSpace(context) || context == "<unavailable>") {
            return false;
        }
        var normalized = evidence.Trim().Trim('"');
        if (normalized.Length == 0) {
            return false;
        }
        return context.Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeThreadAssessmentId(string? id) {
        return id?.Trim() ?? string.Empty;
    }

    private static async Task<(bool Resolved, string? Error)> TryResolveThreadAsync(GitHubClient github,
        GitHubClient? fallbackGithub, string threadId, CancellationToken cancellationToken) {
        try {
            await github.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            return (true, null);
        } catch (Exception ex) {
            var isForbidden = IsIntegrationForbidden(ex);
            if (fallbackGithub is not null && isForbidden) {
                try {
                    await fallbackGithub.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
                    return (true, null);
                } catch (Exception fallbackEx) {
                    // Only log after the integration-forbidden path actually fails (avoid false alarms).
                    LogIntegrationForbiddenHint();
                    return (false, fallbackEx.Message);
                }
            }
            if (isForbidden) {
                LogIntegrationForbiddenHint();
            }
            return (false, ex.Message);
        }
    }

    private static bool IsIntegrationForbidden(Exception ex) {
        if (ex.Message.Contains("Resource not accessible by integration", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("\"FORBIDDEN\"", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return ex.InnerException is not null && IsIntegrationForbidden(ex.InnerException);
    }

    private static void LogIntegrationForbiddenHint() {
        if (Interlocked.Exchange(ref _integrationForbiddenHintLogged, 1) == 1) {
            return;
        }
        // This hint is emitted only from auto-resolve thread resolution flows.
        var lines = new[] {
            "Auto-resolve thread resolution: GitHub returned \"Resource not accessible by integration\".",
            "Hint: This usually means the GitHub App installation token cannot resolve review threads.",
            "Hint: Re-authorize or reinstall the GitHub App after permission changes.",
            "Hint: Confirm the app installation includes this repository.",
            "Hint: Ensure the app has Pull requests: Read & write (and Issues: write if needed).",
            "Hint: Verify INTELLIGENCEX_GITHUB_APP_ID/INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY point to the intended app.",
            "Hint: To bypass the app token, remove INTELLIGENCEX_GITHUB_APP_ID/INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY to use GITHUB_TOKEN.",
            "Hint: GITHUB_TOKEN is provided automatically in GitHub Actions; outside Actions set a PAT in GITHUB_TOKEN."
        };
        Console.Error.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private static bool ThreadHasOnlyBotComments(PullRequestReviewThread thread, ReviewSettings settings) {
        if (thread.Comments.Count == 0) {
            return false;
        }
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Author)) {
                return false;
            }
            if (!IsAutoResolveBot(comment.Author, settings)) {
                return false;
            }
        }
        return true;
    }

    internal static bool TryGetInlineThreadKey(PullRequestReviewThread thread, ReviewSettings settings, out string key) {
        key = string.Empty;
        if (thread.Comments.Count == 0) {
            return false;
        }
        if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
            return false;
        }

        var marker = thread.Comments.FirstOrDefault(comment =>
            comment.Body.Contains(ReviewFormatter.InlineMarker, StringComparison.OrdinalIgnoreCase));
        if (marker is null || string.IsNullOrWhiteSpace(marker.Path) || !marker.Line.HasValue) {
            return false;
        }

        key = BuildInlineKey(marker.Path!, marker.Line.Value);
        return true;
    }

    private static string BuildRelatedPrsSection(PullRequestContext context, IReadOnlyList<RelatedPullRequest> related) {
        if (related.Count == 0) {
            return string.Empty;
        }
        var lines = new List<string>();
        foreach (var pr in related) {
            if (pr.RepoFullName.Equals(context.RepoFullName, StringComparison.OrdinalIgnoreCase) &&
                pr.Number == context.Number) {
                continue;
            }
            lines.Add($"- {pr.RepoFullName}#{pr.Number}: {pr.Title} ({pr.Url})");
        }
        if (lines.Count == 0) {
            return string.Empty;
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Related pull requests (search results):");
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string ResolveRelatedPrsQuery(PullRequestContext context, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.RelatedPrsQuery)) {
            return string.Empty;
        }
        return settings.RelatedPrsQuery!
            .Replace("{repo}", context.RepoFullName, StringComparison.OrdinalIgnoreCase)
            .Replace("{owner}", context.Owner, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", context.Repo, StringComparison.OrdinalIgnoreCase)
            .Replace("{number}", context.Number.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimComment(string value, int maxChars) {
        var text = value.Replace("\r", "").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            return "<empty>";
        }
        if (text.Length <= maxChars) {
            return text;
        }
        return text.Substring(0, maxChars) + "...";
    }

    private static bool ShouldIncludeComment(string? author, string body, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        if (settings.ContextDenyEnabled && settings.ContextDenyPatterns.Count > 0) {
            if (ContextDenyMatcher.Matches(body, settings.ContextDenyPatterns)) {
                return false;
            }
        }
        if (!string.IsNullOrWhiteSpace(author)) {
            if (IsBotAuthor(author, settings)) {
                return false;
            }
        }
        if (body.Contains("<!-- intelligencex", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static bool ShouldIncludeThreadComment(string? author, string body, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        if (settings.ContextDenyEnabled && settings.ContextDenyPatterns.Count > 0) {
            if (ContextDenyMatcher.Matches(body, settings.ContextDenyPatterns)) {
                return false;
            }
        }
        if (!settings.ReviewThreadsIncludeBots && !string.IsNullOrWhiteSpace(author)) {
            if (IsBotAuthor(author, settings)) {
                return false;
            }
        }
        if (body.Contains("<!-- intelligencex", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static bool IsBotAuthor(string author, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(author)) {
            return false;
        }
        var trimmedAuthor = author.Trim();
        var normalizedAuthor = NormalizeBotLogin(trimmedAuthor);
        if (settings.ReviewThreadsAutoResolveBotLogins.Count > 0) {
            foreach (var login in settings.ReviewThreadsAutoResolveBotLogins) {
                if (string.IsNullOrWhiteSpace(login)) {
                    continue;
                }
                var normalizedLogin = NormalizeBotLogin(login);
                if (string.IsNullOrWhiteSpace(normalizedLogin)) {
                    continue;
                }
                if (string.Equals(normalizedAuthor, normalizedLogin, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }
        if (trimmedAuthor.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
            trimmedAuthor.EndsWith("bot", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return trimmedAuthor.Equals("github-actions", StringComparison.OrdinalIgnoreCase) ||
            trimmedAuthor.Equals("intelligencex-review", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAutoResolveBot(string author, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(author)) {
            return false;
        }
        var normalizedAuthor = NormalizeBotLogin(author.Trim());
        if (settings.ReviewThreadsAutoResolveBotLogins.Count > 0) {
            foreach (var login in settings.ReviewThreadsAutoResolveBotLogins) {
                if (string.IsNullOrWhiteSpace(login)) {
                    continue;
                }
                var normalizedLogin = NormalizeBotLogin(login);
                if (string.IsNullOrWhiteSpace(normalizedLogin)) {
                    continue;
                }
                if (string.Equals(normalizedAuthor, normalizedLogin, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }
        return IsBotAuthor(author, settings);
    }

    private static string NormalizeBotLogin(string login) {
        var trimmed = login.Trim();
        if (trimmed.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(0, trimmed.Length - "[bot]".Length).TrimEnd();
        }
        return trimmed;
    }

    private static async Task<HashSet<string>?> PostInlineCommentsAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        IReadOnlyList<InlineReviewComment> inlineComments, CancellationToken cancellationToken) {
        var expectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inlineComments.Count == 0 || string.IsNullOrWhiteSpace(context.HeadSha)) {
            return expectedKeys;
        }

        var lineMap = BuildInlineLineMap(files);
        var patchIndex = BuildInlinePatchIndex(files);
        if (lineMap.Count == 0) {
            return null;
        }

        var limit = Math.Max(0, settings.CommentSearchLimit);
        var existing = await codeHostReader.ListPullRequestReviewCommentsAsync(context, limit, cancellationToken)
            .ConfigureAwait(false);
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in existing) {
            if (string.IsNullOrWhiteSpace(comment.Path) || !comment.Line.HasValue) {
                continue;
            }
            if (!comment.Body.Contains(ReviewFormatter.InlineMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            existingKeys.Add(BuildInlineKey(comment.Path!, comment.Line.Value));
        }

        var posted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inline in inlineComments) {
            var allowPost = posted < settings.MaxInlineComments;
            var normalizedPath = NormalizePath(inline.Path);
            var lineNumber = inline.Line;
            if ((string.IsNullOrWhiteSpace(normalizedPath) || lineNumber <= 0) &&
                !string.IsNullOrWhiteSpace(inline.Snippet)) {
                if (!TryResolveSnippet(inline.Snippet!, patchIndex, normalizedPath, out normalizedPath, out lineNumber)) {
                    continue;
                }
            }
            if (string.IsNullOrWhiteSpace(normalizedPath) || lineNumber <= 0) {
                continue;
            }
            if (!lineMap.TryGetValue(normalizedPath, out var allowedLines) ||
                !allowedLines.Contains(lineNumber)) {
                continue;
            }
            var body = inline.Body.Trim();
            if (string.IsNullOrWhiteSpace(body)) {
                continue;
            }
            var key = BuildInlineKey(normalizedPath, lineNumber);
            expectedKeys.Add(key);
            if (!allowPost || existingKeys.Contains(key) || !seen.Add(key)) {
                continue;
            }
            body = $"{ReviewFormatter.InlineMarker}\n{body}";

            try {
                await github.CreatePullRequestReviewCommentAsync(context.Owner, context.Repo, context.Number, body,
                        context.HeadSha!, normalizedPath, lineNumber, cancellationToken)
                    .ConfigureAwait(false);
                posted++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Inline comment failed for {normalizedPath}:{lineNumber} - {ex.Message}");
            }
        }
        return expectedKeys;
    }

    private static Dictionary<string, HashSet<int>> BuildInlineLineMap(IReadOnlyList<PullRequestFile> files) {
        var map = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalizedPath = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }
            var allowed = ParsePatchLines(file.Patch!);
            if (allowed.Count > 0) {
                map[normalizedPath] = allowed;
            }
        }
        return map;
    }

    private sealed record PatchLine(int LineNumber, string Text, string NormalizedText);

    private static Dictionary<string, List<PatchLine>> BuildInlinePatchIndex(IReadOnlyList<PullRequestFile> files) {
        var index = new Dictionary<string, List<PatchLine>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalizedPath = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }
            var lines = ParsePatchContent(file.Patch!);
            if (lines.Count > 0) {
                index[normalizedPath] = lines;
            }
        }
        return index;
    }

    private static HashSet<int> ParsePatchLines(string patch) {
        var allowed = new HashSet<int>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = Regex.Match(line, @"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@");
                if (match.Success) {
                    oldLine = int.Parse(match.Groups[1].Value) - 1;
                    newLine = int.Parse(match.Groups[2].Value) - 1;
                }
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal)) {
                newLine++;
                allowed.Add(newLine);
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal)) {
                oldLine++;
                continue;
            }
            if (line.StartsWith(" ", StringComparison.Ordinal)) {
                oldLine++;
                newLine++;
                allowed.Add(newLine);
            }
        }
        return allowed;
    }

    private static List<PatchLine> ParsePatchContent(string patch) {
        var results = new List<PatchLine>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = Regex.Match(line, @"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@");
                if (match.Success) {
                    oldLine = int.Parse(match.Groups[1].Value) - 1;
                    newLine = int.Parse(match.Groups[2].Value) - 1;
                }
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal)) {
                newLine++;
                AddPatchLine(results, newLine, line.Substring(1));
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal)) {
                oldLine++;
                continue;
            }
            if (line.StartsWith(" ", StringComparison.Ordinal)) {
                oldLine++;
                newLine++;
                AddPatchLine(results, newLine, line.Substring(1));
            }
        }
        return results;
    }

    private static void AddPatchLine(List<PatchLine> results, int lineNumber, string text) {
        var normalized = NormalizeSnippetText(text);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return;
        }
        results.Add(new PatchLine(lineNumber, text, normalized));
    }

    private static bool TryResolveSnippet(string snippet, Dictionary<string, List<PatchLine>> patchIndex, string? preferredPath,
        out string path, out int lineNumber) {
        path = string.Empty;
        lineNumber = 0;
        var normalizedSnippet = NormalizeSnippetText(snippet);
        if (string.IsNullOrWhiteSpace(normalizedSnippet) || normalizedSnippet.Length < 3) {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(preferredPath)) {
            var normalizedPreferred = NormalizePath(preferredPath);
            if (patchIndex.TryGetValue(normalizedPreferred, out var lines) &&
                TryResolveSnippetInLines(normalizedSnippet, normalizedPreferred, lines, out path, out lineNumber)) {
                return true;
            }
            return false;
        }

        var candidates = new List<(string path, int line, string normalized)>();
        foreach (var (filePath, lines) in patchIndex) {
            foreach (var line in lines) {
                if (line.NormalizedText.Contains(normalizedSnippet, StringComparison.Ordinal)) {
                    candidates.Add((filePath, line.LineNumber, line.NormalizedText));
                }
            }
        }

        if (candidates.Count == 1) {
            path = candidates[0].path;
            lineNumber = candidates[0].line;
            return true;
        }

        if (candidates.Count > 1) {
            var exact = candidates.Where(candidate => candidate.normalized.Equals(normalizedSnippet, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) {
                path = exact[0].path;
                lineNumber = exact[0].line;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveSnippetInLines(string normalizedSnippet, string path,
        IReadOnlyList<PatchLine> lines, out string resolvedPath, out int resolvedLine) {
        resolvedPath = string.Empty;
        resolvedLine = 0;
        var candidates = new List<PatchLine>();
        foreach (var line in lines) {
            if (line.NormalizedText.Contains(normalizedSnippet, StringComparison.Ordinal)) {
                candidates.Add(line);
            }
        }

        if (candidates.Count == 1) {
            resolvedPath = path;
            resolvedLine = candidates[0].LineNumber;
            return true;
        }

        if (candidates.Count > 1) {
            var exact = candidates.Where(line => line.NormalizedText.Equals(normalizedSnippet, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) {
                resolvedPath = path;
                resolvedLine = exact[0].LineNumber;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSnippetText(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }
        var trimmed = text.Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }
        var buffer = new System.Text.StringBuilder(trimmed.Length);
        var inWhitespace = false;
        foreach (var ch in trimmed) {
            if (char.IsWhiteSpace(ch)) {
                if (!inWhitespace) {
                    buffer.Append(' ');
                    inWhitespace = true;
                }
            } else {
                buffer.Append(ch);
                inWhitespace = false;
            }
        }
        return buffer.ToString();
    }

    private static string NormalizePath(string path) {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized.Substring(2);
        }
        return normalized;
    }

    private static string BuildInlineKey(string path, int line) {
        return $"{NormalizePath(path)}:{line}";
    }

    private static async Task<IssueComment?> FindExistingSummaryAsync(IReviewCodeHostReader codeHostReader, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var limit = Math.Max(0, settings.CommentSearchLimit);
        var comments = await codeHostReader.ListIssueCommentsAsync(context, limit, cancellationToken)
            .ConfigureAwait(false);
        foreach (var comment in comments) {
            if (comment.Body.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase)) {
                return comment;
            }
        }
        return null;
    }

    private static bool IsSummaryOutdated(IssueComment? summary, string? headSha) {
        if (summary is null || string.IsNullOrWhiteSpace(headSha)) {
            return false;
        }
        var reviewedCommit = ExtractReviewedCommit(summary.Body);
        if (string.IsNullOrWhiteSpace(reviewedCommit)) {
            return true;
        }
        return !headSha.StartsWith(reviewedCommit, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractReviewedCommit(string body) {
        return ReviewSummaryParser.TryGetReviewedCommit(body, out var commit) ? commit : null;
    }

    private static string? ExtractSummaryBody(string? body, int maxChars) {
        if (string.IsNullOrWhiteSpace(body)) {
            return null;
        }

        var lines = body.Replace("\r", "").Split('\n');
        var endIndex = lines.Length;
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].IndexOf("_Model:", StringComparison.OrdinalIgnoreCase) >= 0) {
                endIndex = i;
                break;
            }
        }

        var startIndex = 0;
        for (var i = 0; i < endIndex; i++) {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0) {
                startIndex = i + 1;
                continue;
            }
            if (trimmed.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("## IntelligenceX Review", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Reviewing PR #", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(ReviewFormatter.ReviewedCommitMarker, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(">")) {
                startIndex = i + 1;
                continue;
            }
            break;
        }

        if (startIndex >= endIndex) {
            return null;
        }

        var block = string.Join("\n", lines, startIndex, endIndex - startIndex).Trim();
        if (string.IsNullOrWhiteSpace(block)) {
            return null;
        }
        if (maxChars > 0 && block.Length > maxChars) {
            return block.Substring(0, maxChars) + "...";
        }
        return block;
    }

    private static async Task<long?> CreateOrUpdateProgressCommentAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        PullRequestContext context, ReviewSettings settings, string body, CancellationToken cancellationToken) {
        IssueComment? existing = null;
        if (settings.CommentMode == ReviewCommentMode.Sticky) {
            existing = await FindExistingSummaryAsync(codeHostReader, context, settings, cancellationToken).ConfigureAwait(false);
        }

        if (existing is not null) {
            await github.UpdateIssueCommentAsync(context.Owner, context.Repo, existing.Id, body, cancellationToken)
                .ConfigureAwait(false);
            return existing.Id;
        }

        var created = await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
            .ConfigureAwait(false);
        return created.Id;
    }

    private static string? GetInput(string name) {
        var value = Environment.GetEnvironmentVariable($"INPUT_{name.ToUpperInvariant()}");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? GetInputInt(string name) {
        var value = GetInput(name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (int.TryParse(value, out var parsed) && parsed > 0) {
            return parsed;
        }
        return null;
    }

    private static (string owner, string repo) SplitRepo(string fullName) {
        var parts = fullName.Split('/');
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repo name '{fullName}'.");
        }
        return (parts[0], parts[1]);
    }
}

internal static class Program {
    private static Task<int> Main(string[] args) => ReviewerApp.RunAsync(args);
}
