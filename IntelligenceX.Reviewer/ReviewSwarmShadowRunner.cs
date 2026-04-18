using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewSwarmShadowReviewerResult {
    public ReviewSwarmShadowReviewerPlan Reviewer { get; init; } = new();
    public bool Succeeded { get; init; }
    public string Output { get; init; } = string.Empty;
    public string? Error { get; init; }
    public long DurationMs { get; init; }
}

internal sealed class ReviewSwarmShadowRunResult {
    public IReadOnlyList<ReviewSwarmShadowReviewerResult> Results { get; init; } =
        Array.Empty<ReviewSwarmShadowReviewerResult>();
    public ReviewSwarmShadowAggregatorResult? Aggregator { get; init; }

    public bool HasFailures {
        get {
            foreach (var result in Results) {
                if (!result.Succeeded) {
                    return true;
                }
            }
            return Aggregator is not null && !Aggregator.Succeeded;
        }
    }
}

internal sealed class ReviewSwarmShadowAggregatorResult {
    public bool Succeeded { get; init; }
    public string Output { get; init; } = string.Empty;
    public string? Error { get; init; }
    public long DurationMs { get; init; }
}

internal static class ReviewSwarmShadowRunner {
    private const int MaxAggregatorContextChars = 24000;

    public static Task<ReviewSwarmShadowRunResult> RunAsync(ReviewSettings settings, ReviewSwarmShadowPlan plan,
        string basePrompt, CancellationToken cancellationToken) =>
        RunAsync(settings, plan, basePrompt,
            (reviewer, prompt, token) => ExecuteReviewerAsync(settings, reviewer, prompt, token),
            (aggregator, prompt, token) => ExecuteAggregatorAsync(settings, aggregator, prompt, token),
            cancellationToken);

    internal static async Task<ReviewSwarmShadowRunResult> RunAsync(ReviewSettings settings,
        ReviewSwarmShadowPlan plan, string basePrompt,
        Func<ReviewSwarmShadowReviewerPlan, string, CancellationToken, Task<string>> executeReviewerAsync,
        Func<ReviewSwarmShadowAggregatorPlan, string, CancellationToken, Task<string>> executeAggregatorAsync,
        CancellationToken cancellationToken) {
        if (!plan.Enabled || !plan.ShadowMode || plan.Reviewers.Count == 0) {
            return new ReviewSwarmShadowRunResult();
        }

        var results = await RunReviewerLanesAsync(settings, plan, basePrompt, executeReviewerAsync,
                cancellationToken)
            .ConfigureAwait(false);

        var aggregatorResult = await RunAggregatorAsync(settings, plan, basePrompt, results, executeAggregatorAsync,
                cancellationToken)
            .ConfigureAwait(false);
        return new ReviewSwarmShadowRunResult {
            Results = results,
            Aggregator = aggregatorResult
        };
    }

    public static string RenderResultSummary(ReviewSwarmShadowRunResult result) {
        if (result.Results.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Swarm shadow results (not posted publicly):");
        foreach (var resultItem in result.Results) {
            sb.Append("- ");
            sb.Append(resultItem.Reviewer.Id);
            sb.Append(": ");
            if (resultItem.Succeeded) {
                sb.Append("completed");
                sb.Append(" (");
                sb.Append(Math.Max(0, resultItem.Output?.Length ?? 0));
                sb.Append(" chars");
                AppendDuration(sb, resultItem.DurationMs);
                sb.Append(')');
            } else {
                sb.Append("failed");
                if (!string.IsNullOrWhiteSpace(resultItem.Error)) {
                    sb.Append(" - ");
                    sb.Append(resultItem.Error);
                }
                AppendDuration(sb, resultItem.DurationMs);
            }
            sb.AppendLine();
        }
        if (result.Aggregator is not null) {
            sb.Append("- aggregator: ");
            if (result.Aggregator.Succeeded) {
                sb.Append("completed (");
                sb.Append(Math.Max(0, result.Aggregator.Output?.Length ?? 0));
                sb.Append(" chars");
                AppendDuration(sb, result.Aggregator.DurationMs);
                sb.Append(')');
            } else {
                sb.Append("failed");
                if (!string.IsNullOrWhiteSpace(result.Aggregator.Error)) {
                    sb.Append(" - ");
                    sb.Append(result.Aggregator.Error);
                }
                AppendDuration(sb, result.Aggregator.DurationMs);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    internal static string BuildReviewerPrompt(string basePrompt, ReviewSwarmShadowReviewerPlan reviewer) {
        var focus = ResolveReviewerFocus(reviewer.Id);
        return string.Concat(basePrompt.TrimEnd(), "\n\n",
            "## Swarm Shadow Reviewer\n",
            "You are running as an independent shadow reviewer. Your output is diagnostic-only and will not be posted directly.\n",
            "- Reviewer id: `", reviewer.Id, "`\n",
            "- Primary focus: ", focus, "\n",
            "- Do not assume other reviewers will see your reasoning.\n",
            "- Return concise findings using the existing review markdown contract.\n");
    }

    internal static string BuildAggregatorPrompt(string basePrompt, ReviewSwarmShadowPlan plan,
        IReadOnlyList<ReviewSwarmShadowReviewerResult> results) {
        var sb = new StringBuilder();
        sb.Append(basePrompt.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Swarm Shadow Aggregator");
        sb.AppendLine("You are aggregating diagnostic-only sub-review outputs. This result will not be posted publicly yet.");
        sb.AppendLine("- Merge overlapping findings into one item.");
        sb.AppendLine("- Prefer actionable correctness, security, reliability, and test coverage blockers.");
        sb.AppendLine("- Preserve the existing review markdown contract.");
        sb.AppendLine("- Note reviewer disagreement only when it materially changes confidence.");
        sb.Append("- Aggregator model target: `");
        sb.Append(plan.Aggregator.Model);
        sb.AppendLine("`.");
        sb.AppendLine();
        sb.AppendLine("### Sub-review outputs");
        foreach (var result in results) {
            sb.Append("#### ");
            sb.Append(result.Reviewer.Id);
            sb.Append(result.Succeeded ? " (succeeded)" : " (failed)");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(result.Error)) {
                sb.Append("Error: ");
                sb.AppendLine(result.Error);
            }
            if (!string.IsNullOrWhiteSpace(result.Output)) {
                sb.AppendLine("```markdown");
                sb.AppendLine(TrimForAggregator(result.Output));
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }

        return TrimForAggregator(sb.ToString());
    }

    private static async Task<IReadOnlyList<ReviewSwarmShadowReviewerResult>> RunReviewerLanesAsync(
        ReviewSettings settings, ReviewSwarmShadowPlan plan, string basePrompt,
        Func<ReviewSwarmShadowReviewerPlan, string, CancellationToken, Task<string>> executeReviewerAsync,
        CancellationToken cancellationToken) {
        var results = new ReviewSwarmShadowReviewerResult[plan.Reviewers.Count];
        var maxParallel = Math.Max(1, Math.Min(plan.MaxParallel, plan.Reviewers.Count));
        using var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
        var tasks = new List<Task>(plan.Reviewers.Count);
        for (var index = 0; index < plan.Reviewers.Count; index++) {
            var reviewerIndex = index;
            tasks.Add(Task.Run(async () => {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try {
                    results[reviewerIndex] = await RunReviewerLaneAsync(settings, plan.Reviewers[reviewerIndex],
                            basePrompt, executeReviewerAsync, cancellationToken)
                        .ConfigureAwait(false);
                } finally {
                    semaphore.Release();
                }
            }, CancellationToken.None));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static async Task<ReviewSwarmShadowReviewerResult> RunReviewerLaneAsync(ReviewSettings settings,
        ReviewSwarmShadowReviewerPlan reviewer, string basePrompt,
        Func<ReviewSwarmShadowReviewerPlan, string, CancellationToken, Task<string>> executeReviewerAsync,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = BuildReviewerPrompt(basePrompt, reviewer);
        var stopwatch = Stopwatch.StartNew();
        try {
            var output = await executeReviewerAsync(reviewer, prompt, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (ReviewDiagnostics.IsFailureBody(output)) {
                var failure = new ReviewSwarmShadowReviewerResult {
                    Reviewer = reviewer,
                    Succeeded = false,
                    Output = output,
                    Error = "Provider returned a fail-open review body.",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
                if (!settings.Swarm.FailOpenOnPartial) {
                    throw new InvalidOperationException(
                        $"Swarm shadow reviewer '{reviewer.Id}' returned a fail-open review body.");
                }
                return failure;
            }

            return new ReviewSwarmShadowReviewerResult {
                Reviewer = reviewer,
                Succeeded = true,
                Output = output ?? string.Empty,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            stopwatch.Stop();
            var failure = new ReviewSwarmShadowReviewerResult {
                Reviewer = reviewer,
                Succeeded = false,
                Error = $"{ex.GetType().Name}: {ex.Message}",
                DurationMs = stopwatch.ElapsedMilliseconds
            };
            if (!settings.Swarm.FailOpenOnPartial) {
                throw;
            }
            return failure;
        }
    }

    private static async Task<string> ExecuteReviewerAsync(ReviewSettings settings,
        ReviewSwarmShadowReviewerPlan reviewer, string prompt, CancellationToken cancellationToken) {
        var reviewerSettings = settings.CloneWithProviderOverride(reviewer.Provider, reviewer.Model,
            reviewer.ReasoningEffort);
        var runner = new ReviewRunner(reviewerSettings);
        return await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ExecuteAggregatorAsync(ReviewSettings settings,
        ReviewSwarmShadowAggregatorPlan aggregator, string prompt, CancellationToken cancellationToken) {
        var aggregatorSettings = settings.CloneWithProviderOverride(aggregator.Provider, aggregator.Model,
            aggregator.ReasoningEffort);
        var runner = new ReviewRunner(aggregatorSettings);
        return await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ReviewSwarmShadowAggregatorResult?> RunAggregatorAsync(ReviewSettings settings,
        ReviewSwarmShadowPlan plan, string basePrompt, IReadOnlyList<ReviewSwarmShadowReviewerResult> results,
        Func<ReviewSwarmShadowAggregatorPlan, string, CancellationToken, Task<string>> executeAggregatorAsync,
        CancellationToken cancellationToken) {
        if (results.Count == 0) {
            return null;
        }

        var prompt = BuildAggregatorPrompt(basePrompt, plan, results);
        var stopwatch = Stopwatch.StartNew();
        try {
            var output = await executeAggregatorAsync(plan.Aggregator, prompt, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            if (ReviewDiagnostics.IsFailureBody(output)) {
                if (!settings.Swarm.FailOpenOnPartial) {
                    throw new InvalidOperationException("Swarm shadow aggregator returned a fail-open review body.");
                }
                return new ReviewSwarmShadowAggregatorResult {
                    Succeeded = false,
                    Output = output,
                    Error = "Provider returned a fail-open review body.",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            return new ReviewSwarmShadowAggregatorResult {
                Succeeded = true,
                Output = output ?? string.Empty,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            stopwatch.Stop();
            if (!settings.Swarm.FailOpenOnPartial) {
                throw;
            }
            return new ReviewSwarmShadowAggregatorResult {
                Succeeded = false,
                Error = $"{ex.GetType().Name}: {ex.Message}",
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private static void AppendDuration(StringBuilder sb, long durationMs) {
        if (durationMs <= 0) {
            return;
        }
        sb.Append(", ");
        sb.Append(durationMs);
        sb.Append(" ms");
    }

    private static string ResolveReviewerFocus(string reviewerId) {
        var normalized = reviewerId.Trim().ToLowerInvariant();
        if (normalized.Contains("security", StringComparison.Ordinal)) {
            return "security, auth boundaries, secret handling, injection risk, and unsafe side effects.";
        }
        if (normalized.Contains("test", StringComparison.Ordinal) || normalized.Contains("coverage", StringComparison.Ordinal)) {
            return "missing regression coverage, brittle tests, and verification gaps.";
        }
        if (normalized.Contains("perf", StringComparison.Ordinal) || normalized.Contains("performance", StringComparison.Ordinal)) {
            return "performance, allocation pressure, network cost, and avoidable repeated work.";
        }
        if (normalized.Contains("docs", StringComparison.Ordinal) || normalized.Contains("documentation", StringComparison.Ordinal)) {
            return "public/internal documentation drift, setup clarity, and migration guidance.";
        }
        if (normalized.Contains("compat", StringComparison.Ordinal) || normalized.Contains("interop", StringComparison.Ordinal)) {
            return "provider interoperability, configuration compatibility, and fallback behavior.";
        }
        return "correctness, reliability, merge blockers, and user-visible regressions.";
    }

    private static string TrimForAggregator(string text) {
        if (text.Length <= MaxAggregatorContextChars) {
            return text;
        }
        const string suffix = "\n\n[truncated for swarm shadow aggregation]";
        var trimmed = text.Substring(0, Math.Max(0, MaxAggregatorContextChars - suffix.Length - 8));
        var closingFence = ResolveOpenMarkdownFence(trimmed);
        if (!string.IsNullOrEmpty(closingFence)) {
            trimmed = string.Concat(trimmed.TrimEnd(), "\n", closingFence);
        }
        return trimmed + suffix;
    }

    private static string ResolveOpenMarkdownFence(string markdown) {
        var openFenceChar = '\0';
        var openFenceLength = 0;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var line in lines) {
            var trimmed = line.TrimStart();
            if (line.Length - trimmed.Length > 3 || trimmed.Length < 3) {
                continue;
            }
            var fenceChar = trimmed[0];
            if (fenceChar != '`' && fenceChar != '~') {
                continue;
            }
            var fenceLength = 0;
            while (fenceLength < trimmed.Length && trimmed[fenceLength] == fenceChar) {
                fenceLength++;
            }
            if (fenceLength < 3) {
                continue;
            }

            if (openFenceChar == '\0') {
                openFenceChar = fenceChar;
                openFenceLength = fenceLength;
                continue;
            }
            if (fenceChar == openFenceChar && fenceLength >= openFenceLength) {
                openFenceChar = '\0';
                openFenceLength = 0;
            }
        }

        return openFenceChar == '\0' ? string.Empty : new string(openFenceChar, openFenceLength);
    }
}
