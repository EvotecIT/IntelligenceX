namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static ReviewConfigValidationResult? RunConfigValidation(string json) {
        var previousPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var tempDir = Path.Combine(Path.GetTempPath(), $"ix-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "reviewer.json");
        File.WriteAllText(configPath, json);
        Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
        try {
            return ReviewConfigValidator.ValidateCurrent();
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousPath);
            try {
                DeleteDirectoryIfExistsWithRetries(tempDir);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static string CallTrimPatch(string patch, int maxChars) {
        var method = typeof(ReviewerApp).GetMethod("TrimPatch", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("TrimPatch method not found.");
        }
        var result = method.Invoke(null, new object?[] { patch, maxChars }) as string;
        return result ?? string.Empty;
    }

    private static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) CallPrepareFiles(IReadOnlyList<PullRequestFile> files,
        int maxFiles, int maxPatchChars) {
        var method = typeof(ReviewerApp).GetMethod("PrepareFiles", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("PrepareFiles method not found.");
        }
        var result = method.Invoke(null, new object?[] { files, maxFiles, maxPatchChars });
        if (result is ValueTuple<IReadOnlyList<PullRequestFile>, string> tuple) {
            return tuple;
        }
        throw new InvalidOperationException("PrepareFiles method returned unexpected result.");
    }

    private static string CallFormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        var method = typeof(ReviewerApp).GetMethod("FormatUsageSummary", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("FormatUsageSummary method not found.");
        }
        var result = method.Invoke(null, new object?[] { snapshot }) as string;
        return result ?? string.Empty;
    }

    private static string? CallEvaluateUsageBudgetGuardFailure(ReviewSettings settings, ChatGptUsageSnapshot snapshot) {
        var method = typeof(ReviewerApp).GetMethod("EvaluateUsageBudgetGuardFailure",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("EvaluateUsageBudgetGuardFailure method not found.");
        }
        return method.Invoke(null, new object?[] { settings, snapshot }) as string;
    }

    private static string? CallTryBuildUsageBudgetGuardFailure(ReviewSettings settings, ReviewProvider provider) {
        var method = typeof(ReviewerApp).GetMethod("TryBuildUsageBudgetGuardFailureAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("TryBuildUsageBudgetGuardFailureAsync method not found.");
        }
        var task = method.Invoke(null, new object?[] { settings, provider });
        if (task is Task<string?> typedTask) {
            return typedTask.GetAwaiter().GetResult();
        }
        throw new InvalidOperationException("TryBuildUsageBudgetGuardFailureAsync returned unexpected result.");
    }

    private static IReadOnlyList<string> CallOrderOpenAiAccounts(
        IReadOnlyList<string> accountIds,
        string rotation,
        string? stickyAccountId,
        long rotationSeed) {
        var method = typeof(ReviewerApp).GetMethod("OrderOpenAiAccounts",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("OrderOpenAiAccounts method not found.");
        }
        var result = method.Invoke(null, new object?[] { accountIds, rotation, stickyAccountId, rotationSeed });
        if (result is IReadOnlyList<string> ordered) {
            return ordered;
        }
        throw new InvalidOperationException("OrderOpenAiAccounts returned unexpected result.");
    }

    private static (bool Success, string? Error, bool BudgetGuardEvaluated) CallTryResolveOpenAiAccount(ReviewSettings settings) {
        var method = typeof(ReviewerApp).GetMethod("TryResolveOpenAiAccountAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("TryResolveOpenAiAccountAsync method not found.");
        }
        var task = method.Invoke(null, new object?[] { settings });
        if (task is Task<ValueTuple<bool, string?, bool>> typedTask) {
            return typedTask.GetAwaiter().GetResult();
        }
        throw new InvalidOperationException("TryResolveOpenAiAccountAsync returned unexpected result.");
    }

    private static void CallPreflightNativeConnectivity(OpenAINativeOptions options, TimeSpan timeout) {
        var runner = new ReviewRunner(new ReviewSettings());
        var method = typeof(ReviewRunner).GetMethod("PreflightNativeConnectivityAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null) {
            throw new InvalidOperationException("PreflightNativeConnectivityAsync method not found.");
        }
        var task = method.Invoke(runner, new object?[] { options, timeout, CancellationToken.None }) as Task;
        if (task is null) {
            throw new InvalidOperationException("PreflightNativeConnectivityAsync did not return a task.");
        }
        task.GetAwaiter().GetResult();
    }

    private static Exception? CallMapPreflightConnectivityException(HttpRequestException ex, string host, TimeSpan timeout,
        bool cancellationRequested) {
        var method = typeof(ReviewRunner).GetMethod("MapPreflightConnectivityException",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("MapPreflightConnectivityException method not found.");
        }
        var result = method.Invoke(null, new object?[] { ex, host, timeout, cancellationRequested });
        return result as Exception;
    }

    private static bool CallShouldAutoResolveMissingInlineThreads(ReviewSettings settings, PullRequestContext context,
        HashSet<string>? inlineKeys, int inlineCommentsCount) {
        var method = typeof(ReviewerApp).GetMethod("ShouldAutoResolveMissingInlineThreads",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("ShouldAutoResolveMissingInlineThreads method not found.");
        }
        var result = method.Invoke(null, new object?[] { settings, context, inlineKeys, inlineCommentsCount });
        if (result is bool value) {
            return value;
        }
        throw new InvalidOperationException("ShouldAutoResolveMissingInlineThreads returned unexpected result.");
    }

    private static ReviewContextExtras CallBuildExtrasAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, bool forceReviewThreads) {
        var method = typeof(ReviewerApp).GetMethod("BuildExtrasAsync", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("BuildExtrasAsync method not found.");
        }
        var codeHostReader = new GitHubCodeHostReader(github);
        var task = method.Invoke(null, new object?[] {
            codeHostReader,
            github,
            null,
            context,
            settings,
            CancellationToken.None,
            forceReviewThreads
        }) as Task<ReviewContextExtras>;
        if (task is null) {
            throw new InvalidOperationException("BuildExtrasAsync did not return a task.");
        }
        return task.GetAwaiter().GetResult();
    }

    private static void CallAutoResolveMissingInlineThreads(GitHubClient github, PullRequestContext context,
        HashSet<string>? expectedKeys, ReviewSettings settings) {
        var method = typeof(ReviewerApp).GetMethod("AutoResolveMissingInlineThreadsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("AutoResolveMissingInlineThreadsAsync method not found.");
        }
        var codeHostReader = new GitHubCodeHostReader(github);
        var task = method.Invoke(null, new object?[] {
            codeHostReader,
            github,
            null,
            context,
            expectedKeys,
            settings,
            CancellationToken.None
        }) as Task;
        if (task is null) {
            throw new InvalidOperationException("AutoResolveMissingInlineThreadsAsync did not return a task.");
        }
        task.GetAwaiter().GetResult();
    }

    private static void CallAutoResolveStaleThreads(GitHubClient github, IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings) {
        var method = typeof(ReviewerApp).GetMethod("AutoResolveStaleThreadsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("AutoResolveStaleThreadsAsync method not found.");
        }
        var task = method.Invoke(null, new object?[] {
            github,
            null,
            threads,
            settings,
            CancellationToken.None
        }) as Task;
        if (task is null) {
            throw new InvalidOperationException("AutoResolveStaleThreadsAsync did not return a task.");
        }
        task.GetAwaiter().GetResult();
    }

    private static string CallBuildThreadAssessmentPrompt(PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> threads, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        string? diffNote) {
        var method = typeof(ReviewerApp).GetMethod("BuildThreadAssessmentPrompt",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("BuildThreadAssessmentPrompt method not found.");
        }
        var result = method.Invoke(null, new object?[] { context, threads, files, settings, diffNote }) as string;
        return result ?? string.Empty;
    }

    private static (IReadOnlyList<PullRequestFile> Files, string Note) CallResolveDiffRangeFiles(GitHubClient github,
        PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles, ReviewSettings settings) {
        var method = typeof(ReviewerApp).GetMethod("ResolveDiffRangeFilesAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("ResolveDiffRangeFilesAsync method not found.");
        }
        var codeHostReader = new GitHubCodeHostReader(github);
        var task = method.Invoke(null, new object?[] {
            codeHostReader,
            context,
            range,
            currentFiles,
            settings,
            CancellationToken.None
        }) as Task;
        if (task is null) {
            throw new InvalidOperationException("ResolveDiffRangeFilesAsync did not return a task.");
        }
        task.GetAwaiter().GetResult();
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty is null) {
            throw new InvalidOperationException("ResolveDiffRangeFilesAsync Result not found.");
        }
        var result = resultProperty.GetValue(task);
        if (result is ValueTuple<IReadOnlyList<PullRequestFile>, string> tuple) {
            return tuple;
        }
        throw new InvalidOperationException("ResolveDiffRangeFilesAsync returned unexpected result.");
    }
}
#endif
