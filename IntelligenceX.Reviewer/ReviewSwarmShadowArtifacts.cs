using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal static class ReviewSwarmShadowArtifacts {
    private const int MaxOutputChars = 12000;
    private const string ArtifactDirectory = "artifacts/reviewer/swarm-shadow";
    private const string JsonFileName = "ix-review-swarm-shadow.json";
    private const string MarkdownFileName = "ix-review-swarm-shadow.md";
    private const string MetricsFileName = "ix-review-swarm-shadow-metrics.jsonl";

    public static async Task WriteAsync(PullRequestContext context, ReviewSettings settings,
        ReviewSwarmShadowPlan plan, ReviewSwarmShadowRunResult result, CancellationToken cancellationToken) {
        if (!settings.Swarm.Metrics || result.Results.Count == 0) {
            return;
        }

        Directory.CreateDirectory(ArtifactDirectory);
        var jsonPath = Path.Combine(ArtifactDirectory, JsonFileName);
        var markdownPath = Path.Combine(ArtifactDirectory, MarkdownFileName);
        var metricsPath = Path.Combine(ArtifactDirectory, MetricsFileName);
        await File.WriteAllTextAsync(jsonPath, BuildJson(context, plan, result), cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(context, plan, result), cancellationToken)
            .ConfigureAwait(false);
        await File.AppendAllTextAsync(metricsPath, BuildMetricsJsonLine(context, result) + Environment.NewLine,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string BuildJson(PullRequestContext context, ReviewSwarmShadowPlan plan,
        ReviewSwarmShadowRunResult result) {
        var payload = new {
            schema = "intelligencex.review.swarmShadow.v1",
            generatedAtUtc = DateTimeOffset.UtcNow,
            repository = context.RepoFullName,
            pullRequest = context.Number,
            headSha = context.HeadSha,
            plan = new {
                enabled = plan.Enabled,
                shadowMode = plan.ShadowMode,
                maxParallel = plan.MaxParallel,
                reviewers = BuildReviewerPlanItems(plan),
                aggregator = new {
                    provider = FormatProvider(plan.Aggregator.Provider),
                    model = plan.Aggregator.Model,
                    reasoningEffort = plan.Aggregator.ReasoningEffort?.ToString().ToLowerInvariant()
                }
            },
            result = new {
                hasFailures = result.HasFailures,
                reviewers = BuildReviewerResultItems(result),
                aggregator = BuildAggregatorResultItem(result.Aggregator)
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions {
            WriteIndented = true
        });
    }

    internal static string BuildMarkdown(PullRequestContext context, ReviewSwarmShadowPlan plan,
        ReviewSwarmShadowRunResult result) {
        var sb = new StringBuilder();
        sb.AppendLine("# IntelligenceX Swarm Shadow Artifact");
        sb.AppendLine();
        sb.AppendLine($"- Repository: `{context.RepoFullName}`");
        sb.AppendLine($"- Pull request: `#{context.Number}`");
        if (!string.IsNullOrWhiteSpace(context.HeadSha)) {
            sb.AppendLine($"- Head SHA: `{context.HeadSha}`");
        }
        sb.AppendLine($"- Reviewers: `{result.Results.Count}`");
        sb.AppendLine($"- Failures: `{(result.HasFailures ? "yes" : "no")}`");
        sb.AppendLine($"- Total shadow duration: `{CalculateTotalDurationMs(result)} ms`");
        sb.AppendLine();
        sb.AppendLine("## Plan");
        sb.AppendLine();
        sb.AppendLine($"- Parallel cap: `{plan.MaxParallel}`");
        foreach (var reviewer in plan.Reviewers) {
            sb.Append("- `");
            sb.Append(reviewer.Id);
            sb.Append("`: ");
            sb.Append(FormatProvider(reviewer.Provider));
            sb.Append(" / `");
            sb.Append(reviewer.Model);
            sb.Append('`');
            if (reviewer.ReasoningEffort.HasValue) {
                sb.Append(" / reasoning `");
                sb.Append(reviewer.ReasoningEffort.Value.ToString().ToLowerInvariant());
                sb.Append('`');
            }
            sb.AppendLine();
        }
        sb.Append("- aggregator: ");
        sb.Append(FormatProvider(plan.Aggregator.Provider));
        sb.Append(" / `");
        sb.Append(plan.Aggregator.Model);
        sb.AppendLine("`");
        sb.AppendLine();
        sb.AppendLine("## Results");
        sb.AppendLine();
        foreach (var item in result.Results) {
            sb.Append("### ");
            sb.AppendLine(item.Reviewer.Id);
            sb.AppendLine();
            sb.AppendLine($"- Status: `{(item.Succeeded ? "succeeded" : "failed")}`");
            sb.AppendLine($"- Duration: `{Math.Max(0, item.DurationMs)} ms`");
            if (!string.IsNullOrWhiteSpace(item.Error)) {
                sb.AppendLine($"- Error: `{EscapeInline(item.Error)}`");
            }
            if (!string.IsNullOrWhiteSpace(item.Output)) {
                sb.AppendLine();
                sb.AppendLine("```markdown");
                sb.AppendLine(TrimOutput(item.Output));
                sb.AppendLine("```");
            }
            sb.AppendLine();
        }

        if (result.Aggregator is not null) {
            sb.AppendLine("### Aggregator");
            sb.AppendLine();
            sb.AppendLine($"- Status: `{(result.Aggregator.Succeeded ? "succeeded" : "failed")}`");
            sb.AppendLine($"- Duration: `{Math.Max(0, result.Aggregator.DurationMs)} ms`");
            if (!string.IsNullOrWhiteSpace(result.Aggregator.Error)) {
                sb.AppendLine($"- Error: `{EscapeInline(result.Aggregator.Error)}`");
            }
            if (!string.IsNullOrWhiteSpace(result.Aggregator.Output)) {
                sb.AppendLine();
                sb.AppendLine("```markdown");
                sb.AppendLine(TrimOutput(result.Aggregator.Output));
                sb.AppendLine("```");
            }
        }

        return sb.ToString().TrimEnd();
    }

    internal static string BuildMetricsJsonLine(PullRequestContext context, ReviewSwarmShadowRunResult result) {
        var reviewerSucceeded = 0;
        var reviewerFailed = 0;
        var reviewerDurationMs = 0L;
        var reviewerOutputChars = 0;
        foreach (var reviewer in result.Results) {
            if (reviewer.Succeeded) {
                reviewerSucceeded++;
            } else {
                reviewerFailed++;
            }
            reviewerDurationMs += Math.Max(0, reviewer.DurationMs);
            reviewerOutputChars += reviewer.Output?.Length ?? 0;
        }

        var aggregatorDurationMs = Math.Max(0, result.Aggregator?.DurationMs ?? 0);
        var aggregatorOutputChars = result.Aggregator?.Output?.Length ?? 0;
        var payload = new {
            schema = "intelligencex.review.swarmShadowMetrics.v1",
            generatedAtUtc = DateTimeOffset.UtcNow,
            repository = context.RepoFullName,
            pullRequest = context.Number,
            headSha = context.HeadSha,
            reviewerCount = result.Results.Count,
            reviewerSucceeded,
            reviewerFailed,
            aggregatorSucceeded = result.Aggregator?.Succeeded,
            hasFailures = result.HasFailures,
            reviewerDurationMs,
            aggregatorDurationMs,
            totalDurationMs = reviewerDurationMs + aggregatorDurationMs,
            reviewerOutputChars,
            aggregatorOutputChars
        };

        return JsonSerializer.Serialize(payload);
    }

    private static IReadOnlyList<object> BuildReviewerPlanItems(ReviewSwarmShadowPlan plan) {
        var items = new List<object>(plan.Reviewers.Count);
        foreach (var reviewer in plan.Reviewers) {
            items.Add(new {
                id = reviewer.Id,
                provider = FormatProvider(reviewer.Provider),
                model = reviewer.Model,
                reasoningEffort = reviewer.ReasoningEffort?.ToString().ToLowerInvariant()
            });
        }
        return items;
    }

    private static IReadOnlyList<object> BuildReviewerResultItems(ReviewSwarmShadowRunResult result) {
        var items = new List<object>(result.Results.Count);
        foreach (var reviewerResult in result.Results) {
            items.Add(new {
                id = reviewerResult.Reviewer.Id,
                provider = FormatProvider(reviewerResult.Reviewer.Provider),
                model = reviewerResult.Reviewer.Model,
                succeeded = reviewerResult.Succeeded,
                error = reviewerResult.Error,
                durationMs = Math.Max(0, reviewerResult.DurationMs),
                outputChars = reviewerResult.Output?.Length ?? 0,
                output = TrimOutput(reviewerResult.Output ?? string.Empty)
            });
        }
        return items;
    }

    private static object? BuildAggregatorResultItem(ReviewSwarmShadowAggregatorResult? aggregator) {
        if (aggregator is null) {
            return null;
        }
        return new {
            succeeded = aggregator.Succeeded,
            error = aggregator.Error,
            durationMs = Math.Max(0, aggregator.DurationMs),
            outputChars = aggregator.Output?.Length ?? 0,
            output = TrimOutput(aggregator.Output ?? string.Empty)
        };
    }

    private static long CalculateTotalDurationMs(ReviewSwarmShadowRunResult result) {
        var total = 0L;
        foreach (var item in result.Results) {
            total += Math.Max(0, item.DurationMs);
        }
        return total + Math.Max(0, result.Aggregator?.DurationMs ?? 0);
    }

    private static string TrimOutput(string text) {
        if (text.Length <= MaxOutputChars) {
            return text;
        }
        const string suffix = "\n\n[truncated for swarm shadow artifact]";
        return text.Substring(0, Math.Max(0, MaxOutputChars - suffix.Length)) + suffix;
    }

    private static string FormatProvider(ReviewProvider provider) =>
        provider.ToString().ToLowerInvariant();

    private static string EscapeInline(string value) =>
        value.Replace("`", "\\`", StringComparison.Ordinal);
}
