using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal static class ReviewHistoryArtifacts {
    private const int MaxTextChars = 2000;
    private const string ArtifactDirectory = "artifacts/reviewer/history";
    private const string JsonFileName = "ix-review-history.json";
    private const string MarkdownFileName = "ix-review-history.md";

    public static async Task WriteAsync(PullRequestContext context, ReviewSettings settings,
        ReviewHistorySnapshot snapshot, string renderedPromptSection, CancellationToken cancellationToken) {
        if (!settings.History.Enabled || !settings.History.Artifacts || !snapshot.HasContent) {
            return;
        }

        Directory.CreateDirectory(ArtifactDirectory);
        var jsonPath = Path.Combine(ArtifactDirectory, JsonFileName);
        var markdownPath = Path.Combine(ArtifactDirectory, MarkdownFileName);
        await File.WriteAllTextAsync(jsonPath, BuildJson(context, snapshot, renderedPromptSection), cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(context, snapshot, renderedPromptSection),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string BuildJson(PullRequestContext context, ReviewHistorySnapshot snapshot,
        string renderedPromptSection) {
        var payload = new {
            schema = "intelligencex.review.history.v1",
            generatedAtUtc = DateTimeOffset.UtcNow,
            repository = context.RepoFullName,
            pullRequest = context.Number,
            headSha = context.HeadSha,
            currentHeadSha = snapshot.CurrentHeadSha,
            summary = new {
                rounds = snapshot.Rounds.Count,
                openFindings = snapshot.OpenFindings.Count,
                resolvedSinceLastRound = snapshot.ResolvedSinceLastRound.Count,
                externalSummaries = snapshot.ExternalSummaries.Count,
                activeThreads = snapshot.ThreadSnapshot?.ActiveCount ?? 0,
                staleThreads = snapshot.ThreadSnapshot?.StaleCount ?? 0,
                resolvedThreads = snapshot.ThreadSnapshot?.ResolvedCount ?? 0
            },
            rounds = BuildRoundItems(snapshot.Rounds),
            openFindings = BuildFindingItems(snapshot.OpenFindings),
            resolvedSinceLastRound = BuildFindingItems(snapshot.ResolvedSinceLastRound),
            externalSummaries = BuildExternalSummaryItems(snapshot.ExternalSummaries),
            threadSnapshot = BuildThreadSnapshotItem(snapshot.ThreadSnapshot),
            renderedPromptSection = TrimText(renderedPromptSection)
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions {
            WriteIndented = true
        });
    }

    internal static string BuildMarkdown(PullRequestContext context, ReviewHistorySnapshot snapshot,
        string renderedPromptSection) {
        var sb = new StringBuilder();
        sb.AppendLine("# IntelligenceX Review History Artifact");
        sb.AppendLine();
        sb.AppendLine($"- Repository: `{context.RepoFullName}`");
        sb.AppendLine($"- Pull request: `#{context.Number}`");
        if (!string.IsNullOrWhiteSpace(context.HeadSha)) {
            sb.AppendLine($"- Head SHA: `{context.HeadSha}`");
        }
        sb.AppendLine($"- History rounds: `{snapshot.Rounds.Count}`");
        sb.AppendLine($"- Open findings: `{snapshot.OpenFindings.Count}`");
        sb.AppendLine($"- Resolved since last round: `{snapshot.ResolvedSinceLastRound.Count}`");
        sb.AppendLine($"- External bot summaries: `{snapshot.ExternalSummaries.Count}`");
        if (snapshot.ThreadSnapshot is not null) {
            sb.AppendLine($"- Threads: active `{snapshot.ThreadSnapshot.ActiveCount}`, stale `{snapshot.ThreadSnapshot.StaleCount}`, resolved `{snapshot.ThreadSnapshot.ResolvedCount}`");
        }

        AppendFindings(sb, "Open Findings", snapshot.OpenFindings);
        AppendFindings(sb, "Resolved Since Last Round", snapshot.ResolvedSinceLastRound);
        AppendExternalSummaries(sb, snapshot.ExternalSummaries);
        AppendRounds(sb, snapshot.Rounds);
        AppendThreads(sb, snapshot.ThreadSnapshot);

        if (!string.IsNullOrWhiteSpace(renderedPromptSection)) {
            sb.AppendLine();
            sb.AppendLine("## Prompt Section");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(TrimText(renderedPromptSection));
            sb.AppendLine("```");
        }

        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<object> BuildRoundItems(IReadOnlyList<ReviewHistoryRound> rounds) {
        var items = new List<object>(rounds.Count);
        foreach (var round in rounds) {
            items.Add(new {
                sequence = round.Sequence,
                source = round.Source,
                summaryCommentId = round.SummaryCommentId,
                reviewedSha = round.ReviewedSha,
                sameHeadAsCurrent = round.SameHeadAsCurrent,
                hasMergeBlockers = round.HasMergeBlockers,
                mergeBlockerStatus = round.MergeBlockerStatus,
                findings = BuildFindingItems(round.Findings)
            });
        }
        return items;
    }

    private static IReadOnlyList<object> BuildFindingItems(IReadOnlyList<ReviewHistoryFinding> findings) {
        var items = new List<object>(findings.Count);
        foreach (var finding in findings) {
            items.Add(new {
                fingerprint = finding.Fingerprint,
                section = finding.Section,
                text = TrimText(finding.Text),
                status = finding.Status
            });
        }
        return items;
    }

    private static object? BuildThreadSnapshotItem(ReviewHistoryThreadSnapshot? snapshot) {
        if (snapshot is null) {
            return null;
        }

        var excerpts = new List<object>(snapshot.Excerpts.Count);
        foreach (var excerpt in snapshot.Excerpts) {
            excerpts.Add(new {
                threadId = excerpt.ThreadId,
                status = excerpt.Status,
                author = excerpt.Author,
                body = TrimText(excerpt.Body),
                path = excerpt.Path,
                line = excerpt.Line
            });
        }

        return new {
            activeCount = snapshot.ActiveCount,
            resolvedCount = snapshot.ResolvedCount,
            staleCount = snapshot.StaleCount,
            excerpts
        };
    }

    private static IReadOnlyList<object> BuildExternalSummaryItems(
        IReadOnlyList<ReviewHistoryExternalSummary> summaries) {
        var items = new List<object>(summaries.Count);
        foreach (var summary in summaries) {
            items.Add(new {
                commentId = summary.CommentId,
                author = summary.Author,
                source = summary.Source,
                excerpt = TrimText(summary.Excerpt)
            });
        }
        return items;
    }

    private static void AppendFindings(StringBuilder sb, string title, IReadOnlyList<ReviewHistoryFinding> findings) {
        sb.AppendLine();
        sb.Append("## ");
        sb.AppendLine(title);
        sb.AppendLine();
        if (findings.Count == 0) {
            sb.AppendLine("None.");
            return;
        }

        foreach (var finding in findings) {
            sb.Append("- `");
            sb.Append(EscapeInline(finding.Status));
            sb.Append("` [");
            sb.Append(EscapeInline(finding.Section));
            sb.Append("] ");
            sb.AppendLine(TrimText(finding.Text).Replace("\n", " ", StringComparison.Ordinal));
        }
    }

    private static void AppendRounds(StringBuilder sb, IReadOnlyList<ReviewHistoryRound> rounds) {
        sb.AppendLine();
        sb.AppendLine("## Rounds");
        sb.AppendLine();
        if (rounds.Count == 0) {
            sb.AppendLine("None.");
            return;
        }

        foreach (var round in rounds) {
            sb.Append("- Round ");
            sb.Append(round.Sequence);
            sb.Append(": `");
            sb.Append(EscapeInline(round.ReviewedSha));
            sb.Append("`, source `");
            sb.Append(EscapeInline(round.Source));
            sb.Append("`, blockers `");
            sb.Append(EscapeInline(round.HasMergeBlockers ? "yes" : "no"));
            sb.AppendLine("`");
        }
    }

    private static void AppendExternalSummaries(StringBuilder sb,
        IReadOnlyList<ReviewHistoryExternalSummary> summaries) {
        sb.AppendLine();
        sb.AppendLine("## External Bot Summaries");
        sb.AppendLine();
        if (summaries.Count == 0) {
            sb.AppendLine("None.");
            return;
        }

        sb.AppendLine("Supporting context only; these summaries are not treated as IX-owned blocker state.");
        foreach (var summary in summaries) {
            sb.Append("- `");
            sb.Append(EscapeInline(summary.Author));
            sb.Append("`: ");
            sb.AppendLine(TrimText(summary.Excerpt).Replace("\n", " ", StringComparison.Ordinal));
        }
    }

    private static void AppendThreads(StringBuilder sb, ReviewHistoryThreadSnapshot? snapshot) {
        sb.AppendLine();
        sb.AppendLine("## Thread Snapshot");
        sb.AppendLine();
        if (snapshot is null) {
            sb.AppendLine("None.");
            return;
        }

        sb.AppendLine($"Active `{snapshot.ActiveCount}`, stale `{snapshot.StaleCount}`, resolved `{snapshot.ResolvedCount}`.");
        foreach (var excerpt in snapshot.Excerpts) {
            var location = string.IsNullOrWhiteSpace(excerpt.Path)
                ? string.Empty
                : excerpt.Line.HasValue
                    ? $" ({excerpt.Path}:{excerpt.Line.Value})"
                    : $" ({excerpt.Path})";
            sb.Append("- `");
            sb.Append(EscapeInline(excerpt.Status));
            sb.Append("` ");
            sb.Append(EscapeInline(excerpt.Author));
            sb.Append(location);
            sb.Append(": ");
            sb.AppendLine(TrimText(excerpt.Body).Replace("\n", " ", StringComparison.Ordinal));
        }
    }

    private static string TrimText(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }
        var normalized = value.Replace("\r", " ").Trim();
        if (normalized.Length <= MaxTextChars) {
            return normalized;
        }
        const string suffix = "\n\n[truncated for review history artifact]";
        return normalized.Substring(0, Math.Max(0, MaxTextChars - suffix.Length)) + suffix;
    }

    private static string EscapeInline(string value) =>
        value.Replace("`", "\\`", StringComparison.Ordinal);
}
