namespace IntelligenceX.Reviewer;

/// <summary>
/// Entry point for running the IntelligenceX review workflow.
/// </summary>
public static partial class ReviewerApp {
    private const string ThreadReplyMarker = "<!-- intelligencex:thread-reply -->";
    private const string UsageSummaryPrefix = "Usage: ";
    private const string UsageSummarySeparator = " | ";
    private const string SecondaryWindowSuffix = " (secondary)";
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
        bool? requiresConversationResolution = null;
        var progress = new ReviewProgress { StatusLine = "Starting review." };
        try {
            var cancellationToken = cts.Token;
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
                if (!await TryWriteAuthFromEnvAsync().ConfigureAwait(false)) {
                    return 1;
                }
                if (!await ValidateAuthAsync(settings).ConfigureAwait(false)) {
                    return 1;
                }
                return await AzureDevOpsReviewRunner.RunAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            var primaryToken = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN");
            var standardToken = ResolveFirstNonEmptyGitHubToken("GITHUB_TOKEN", "GH_TOKEN");
            var token = !string.IsNullOrWhiteSpace(primaryToken)
                ? primaryToken
                : standardToken;
            var tokenSource = !string.IsNullOrWhiteSpace(primaryToken)
                ? "INTELLIGENCEX_GITHUB_TOKEN"
                : ResolveFirstNonEmptyGitHubTokenSource("GITHUB_TOKEN", "GH_TOKEN");

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(tokenSource)) {
                Console.Error.WriteLine("Missing GitHub token (INTELLIGENCEX_GITHUB_TOKEN, GITHUB_TOKEN, or GH_TOKEN).");
                return 1;
            }
            githubToken = token;
            SecretsAudit.Record($"GitHub token from {tokenSource}");

            var fallbackToken = standardToken;
            var fallbackTokenSource = ResolveFirstNonEmptyGitHubTokenSource("GITHUB_TOKEN", "GH_TOKEN");
            if (string.Equals(token, fallbackToken, StringComparison.Ordinal)) {
                fallbackToken = null;
                fallbackTokenSource = null;
            }
            if (!string.IsNullOrWhiteSpace(fallbackToken) && !string.IsNullOrWhiteSpace(fallbackTokenSource)) {
                SecretsAudit.Record($"GitHub fallback token from {fallbackTokenSource}");
            }

            using var github = new GitHubClient(token, maxConcurrency: settings.GitHubMaxConcurrency, credentialLabel: tokenSource);
            using var fallbackGithub = string.IsNullOrWhiteSpace(fallbackToken)
                ? null
                : new GitHubClient(fallbackToken, maxConcurrency: settings.GitHubMaxConcurrency,
                    credentialLabel: fallbackTokenSource);

            // Prefer the standard GITHUB_TOKEN for read operations when available; some GitHub App tokens are
            // intentionally scoped for writes/comments and may not have full PR read access in all environments.
            var readGithub = fallbackGithub ?? github;
            // Lightweight adapter over github; it does not own additional resources.
            IReviewCodeHostReader codeHostReader = new GitHubCodeHostReader(readGithub);
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
                var repoParts = repoName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (repoParts.Length != 2) {
                    Console.Error.WriteLine($"Invalid repo name '{repoName}'. Expected owner/repo.");
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

            if (!await TryWriteAuthFromEnvAsync().ConfigureAwait(false)) {
                return 1;
            }
            if (!await ValidateAuthAsync(settings).ConfigureAwait(false)) {
                return 1;
            }
            var accountSelection = await TryResolveOpenAiAccountAsync(settings).ConfigureAwait(false);
            if (!accountSelection.Success) {
                Console.Error.WriteLine(accountSelection.Error);
                return 1;
            }
            if (!accountSelection.BudgetGuardEvaluated) {
                var usageBudgetFailure = await TryBuildUsageBudgetGuardFailureAsync(settings, settings.Provider)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(usageBudgetFailure)) {
                    Console.Error.WriteLine(usageBudgetFailure);
                    return 1;
                }
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
            var allFiles = files;

            if (files.Count == 0) {
                Console.WriteLine("No files to review.");
                return 0;
            }

            var workflowGuardNote = string.Empty;
            if (!settings.AllowWorkflowChanges && HasWorkflowChanges(files)) {
                var workflowFileCount = CountWorkflowFiles(files);
                var reviewableFiles = ExcludeWorkflowFiles(files);
                if (reviewableFiles.Count == 0) {
                    var skipNote = BuildWorkflowGuardNote(context.HeadSha, workflowFileCount, 0, skipped: true);
                    Console.WriteLine(skipNote);
                    if (allowWrites) {
                        await PostWorkflowGuardSummaryAsync(codeHostReader, github, context, settings, skipNote, cancellationToken)
                            .ConfigureAwait(false);
                        Console.WriteLine("Posted workflow policy summary.");
                    }
                    return 0;
                }

                workflowGuardNote = BuildWorkflowGuardNote(context.HeadSha, workflowFileCount, reviewableFiles.Count, skipped: false);
                Console.WriteLine(
                    $"Workflow file changes detected; excluding {workflowFileCount} workflow file(s) from review. Reviewing {reviewableFiles.Count} non-workflow file(s).");
                files = reviewableFiles;
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

            requiresConversationResolution = await TryGetRequiredConversationResolutionAsync(readGithub, context, settings,
                    cancellationToken)
                .ConfigureAwait(false);

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
                        settings, extras, false, triageOnlyNote, cancellationToken, true, false, noMergeBlockers: false)
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
            budgetNote = CombineNotes(workflowGuardNote, budgetNote);
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
            // Merge-blocker detection depends on the review markdown contract (Todo/Critical sections)
            // produced by ReviewFormatter and prompts. Keep parser + formatter in sync.
            var hasMergeBlockers = ReviewSummaryParser.HasMergeBlockers(reviewBody, settings);
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
                    var analysisLoad = AnalysisFindingsLoader.LoadWithReport(settings, reviewFiles);
                    var analysisFindings = analysisLoad.Findings;
                    var analysisBlocks = new List<string>();
                    var analysisPolicy = AnalysisPolicyBuilder.BuildPolicy(settings, analysisLoad);
                    if (!string.IsNullOrWhiteSpace(analysisPolicy)) {
                        analysisBlocks.Add(analysisPolicy);
                    }
                    var hotspotsBlock = AnalysisHotspots.BuildBlock(settings, analysisFindings);
                    if (!string.IsNullOrWhiteSpace(hotspotsBlock)) {
                        analysisBlocks.Add(hotspotsBlock);
                    }
                    var analysisSummary = AnalysisSummaryBuilder.BuildSummary(analysisFindings, analysisResults, analysisLoad.Report);
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
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    Console.WriteLine($"Static analysis load failed; rendering unavailable summary. ({ex.GetType().Name})");
                    summaryBody = ApplyAnalysisLoadFailure(summaryBody, settings, ex);
                }
            }

            HashSet<string>? inlineKeys = null;
            if (inlineAllowed) {
                inlineKeys = await PostInlineCommentsAsync(codeHostReader, github, context, files, settings, inlineComments,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (ShouldAutoResolveMissingInlineThreads(settings, context, inlineKeys, inlineComments.Length)) {
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
                        settings.ReviewThreadsAutoResolveAIPostComment, noMergeBlockers: !hasMergeBlockers)
                    .ConfigureAwait(false);
                if (settings.ReviewThreadsAutoResolveAIEmbed && !string.IsNullOrWhiteSpace(triageResult.EmbeddedBlock)) {
                    summaryBody = ApplyEmbedPlacement(summaryBody, triageResult.EmbeddedBlock,
                        settings.ReviewThreadsAutoResolveAIEmbedPlacement);
                }
            }
            var combinedPermissionDiagnostics = extras.StaleThreadAutoResolvePermissions.Merge(triageResult.PermissionDiagnostics);
            var permissionNote = allowWrites
                ? BuildAutoResolvePermissionNote(combinedPermissionDiagnostics.DeniedThreadCount,
                    combinedPermissionDiagnostics.DeniedCredentialLabels)
                : string.Empty;
            summaryBody = AppendConversationResolutionPermissionBlocker(summaryBody, combinedPermissionDiagnostics,
                requiresConversationResolution);
            var inlineSuppressed = inlineSupported && !inlineAllowed;
            var autoResolveSummary = allowWrites && settings.ReviewThreadsAutoResolveAISummary ? triageResult.SummaryLine : string.Empty;
            autoResolveSummary = CombineNotes(autoResolveSummary, permissionNote);
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
                try {
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, commentBody, cancellationToken)
                        .ConfigureAwait(false);
                    summaryPosted = true;
                    Console.WriteLine("Updated review comment.");
                } catch (Exception ex) {
                    Console.Error.WriteLine($"Failed to update review comment {commentId.Value}: {ex.Message}");
                    await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, commentBody, cancellationToken)
                        .ConfigureAwait(false);
                    summaryPosted = true;
                    Console.WriteLine("Posted replacement review comment.");
                }
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
                    try {
                        await github.UpdateIssueCommentAsync(context.Owner, context.Repo, existing.Id, commentBody, cancellationToken)
                            .ConfigureAwait(false);
                        summaryPosted = true;
                        Console.WriteLine("Updated existing review comment.");
                        return 0;
                    } catch (Exception ex) {
                        Console.Error.WriteLine($"Failed to update existing review comment {existing.Id}: {ex.Message}");
                    }
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
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("Operation canceled.");
            return 130;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            var failOpen = settings is not null && ReviewRunner.ShouldFailOpen(settings, ex);
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
            return failOpen ? 0 : 1;
        } finally {
            secretsAudit?.WriteSummary();
            secretsAudit?.Dispose();
            if (cancelHandler is not null) {
                Console.CancelKeyPress -= cancelHandler;
            }
            CleanupTempAuthPathFromEnv();
        }
    }

    internal static string? ResolveFirstNonEmptyGitHubToken(params string[] variableNames) {
        foreach (var variableName in variableNames) {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }
        return null;
    }

    internal static string? ResolveFirstNonEmptyGitHubTokenSource(params string[] variableNames) {
        foreach (var variableName in variableNames) {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(value)) {
                return variableName;
            }
        }
        return null;
    }

    private static string ApplyAnalysisLoadFailure(string summaryBody, ReviewSettings settings, Exception ex) {
        var analysisResults = settings.Analysis?.Results;
        if (analysisResults is null) {
            return summaryBody;
        }
        var reason = BuildAnalysisLoadFailureReason(ex);
        var unavailablePolicy = AnalysisPolicyBuilder.BuildUnavailablePolicy(settings, reason);
        var analysisBlocks = new List<string>();
        if (!string.IsNullOrWhiteSpace(unavailablePolicy)) {
            analysisBlocks.Add(unavailablePolicy);
        }
        if (analysisResults.Summary) {
            var unavailableSummary = AnalysisSummaryBuilder.BuildUnavailableSummary(reason);
            if (!string.IsNullOrWhiteSpace(unavailableSummary)) {
                analysisBlocks.Add(unavailableSummary);
            }
        }
        if (analysisBlocks.Count == 0) {
            return summaryBody;
        }
        return ApplyEmbedPlacement(summaryBody, string.Join("\n\n", analysisBlocks), analysisResults.SummaryPlacement);
    }

    private static bool ShouldAutoResolveMissingInlineThreads(ReviewSettings settings, PullRequestContext context,
        HashSet<string>? inlineKeys, int inlineCommentsCount) {
        return settings.ReviewThreadsAutoResolveMissingInline &&
               !string.IsNullOrWhiteSpace(context.HeadSha) &&
               inlineKeys is not null &&
               (inlineCommentsCount == 0 || inlineKeys.Count > 0);
    }

    private static async Task<bool?> TryGetRequiredConversationResolutionAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(context.BaseRefName)) {
            return null;
        }

        try {
            return await github.GetRequiredConversationResolutionAsync(context.Owner, context.Repo, context.BaseRefName!,
                    cancellationToken)
                .ConfigureAwait(false);
        } catch (Exception ex) {
            if (settings.Diagnostics) {
                Console.Error.WriteLine($"Branch protection lookup failed for '{context.BaseRefName}': {ex.Message}");
            }
            return null;
        }
    }

    private static string BuildAnalysisLoadFailureReason(Exception ex) {
        const string defaultReason = "internal error while loading analysis results";
        return ex switch {
            UnauthorizedAccessException => "permission denied while loading analysis results",
            IOException => "analysis result input unavailable",
            FormatException => "invalid analysis result format",
            global::System.Text.Json.JsonException => "invalid analysis result format",
            _ => defaultReason
        };
    }

    internal static async Task<bool> TryUpdateFailureSummaryAsync(string? githubToken, string? apiBaseUrl,
        PullRequestContext context, ReviewSettings settings, long commentId, Exception ex, bool inlineSupported) {
        if (string.IsNullOrWhiteSpace(githubToken)) {
            return false;
        }
        var failureBody = ReviewDiagnostics.BuildFailureBody(ex, settings, null, null, $"{context.Owner}/{context.Repo}");
        var inlineSuppressed = inlineSupported;
        var commentBody = ReviewFormatter.BuildComment(context, failureBody, settings, inlineSupported, inlineSuppressed,
            string.Empty, string.Empty, string.Empty, string.Empty);
        using var failureClient = new GitHubClient(githubToken, apiBaseUrl, settings.GitHubMaxConcurrency);
        await failureClient.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId, commentBody,
                CancellationToken.None)
            .ConfigureAwait(false);
        return true;
    }

}
