using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class ReviewHistoryBuilder {
    public static ReviewHistorySnapshot BuildSnapshot(IReadOnlyList<IssueComment>? issueComments, string? currentHeadSha,
        IReadOnlyList<PullRequestReviewThread>? reviewThreads, ReviewSettings settings) {
        var rounds = new List<ReviewHistoryRound>();
        var openFindings = new List<ReviewHistoryFinding>();
        var resolvedSinceLastRound = new List<ReviewHistoryFinding>();
        var externalSummaries = new List<ReviewHistoryExternalSummary>();
        var snapshot = new ReviewHistorySnapshot {
            CurrentHeadSha = currentHeadSha?.Trim() ?? string.Empty
        };
        if (!settings.History.Enabled) {
            return snapshot;
        }

        if (settings.History.IncludeIxSummaryHistory && issueComments is not null && issueComments.Count > 0) {
            AppendSummaryRounds(rounds, openFindings, resolvedSinceLastRound, issueComments, snapshot.CurrentHeadSha, settings);
        }

        if (settings.History.IncludeExternalBotSummaries && issueComments is not null && issueComments.Count > 0) {
            AppendExternalSummaries(externalSummaries, issueComments, settings);
        }

        var threadSnapshot = settings.History.IncludeReviewThreads && reviewThreads is not null && reviewThreads.Count > 0
            ? BuildThreadSnapshot(reviewThreads, settings)
            : null;

        return new ReviewHistorySnapshot {
            CurrentHeadSha = snapshot.CurrentHeadSha,
            Rounds = rounds,
            OpenFindings = openFindings,
            ResolvedSinceLastRound = resolvedSinceLastRound,
            ExternalSummaries = externalSummaries,
            ThreadSnapshot = threadSnapshot
        };
    }

    public static ReviewHistorySnapshot BuildSnapshot(IssueComment? existingSummary, string? currentHeadSha,
        IReadOnlyList<PullRequestReviewThread>? reviewThreads, ReviewSettings settings) {
        return existingSummary is null
            ? BuildSnapshot(Array.Empty<IssueComment>(), currentHeadSha, reviewThreads, settings)
            : BuildSnapshot(new[] { existingSummary }, currentHeadSha, reviewThreads, settings);
    }

    public static string Build(IssueComment? existingSummary, string? currentHeadSha,
        IReadOnlyList<PullRequestReviewThread>? reviewThreads, ReviewSettings settings) {
        return Render(BuildSnapshot(existingSummary, currentHeadSha, reviewThreads, settings));
    }

    public static string Render(ReviewHistorySnapshot snapshot) {
        if (!snapshot.HasContent) {
            return string.Empty;
        }

        var lines = new List<string>();
        AppendDerivedStateLines(lines, snapshot);
        foreach (var round in snapshot.Rounds) {
            AppendStickySummaryLines(lines, round, snapshot.CurrentHeadSha);
        }
        if (snapshot.ExternalSummaries.Count > 0) {
            AppendExternalSummaryLines(lines, snapshot.ExternalSummaries);
        }
        if (snapshot.ThreadSnapshot is not null) {
            AppendThreadLines(lines, snapshot.ThreadSnapshot);
        }

        if (lines.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Review history snapshot:");
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        sb.AppendLine("- Use review history as supporting state only. Re-open prior findings only when the current diff or thread state supports it.");
        sb.AppendLine();
        return sb.ToString();
    }

    public static string BuildCommentBlock(ReviewHistorySnapshot snapshot) {
        if (snapshot.Rounds.Count == 0) {
            return string.Empty;
        }

        var lines = new List<string>();
        var hasVisibleProgress = false;
        var latestSameHeadRound = FindLatestSameHeadRound(snapshot.Rounds);
        if (latestSameHeadRound is not null) {
            hasVisibleProgress |= AppendPostureTable(lines, latestSameHeadRound);
        }
        if (snapshot.OpenFindings.Count > 0) {
            lines.Add($"Open on current head: {FormatCount(snapshot.OpenFindings.Count, "finding")}.");
            hasVisibleProgress = true;
        } else if (latestSameHeadRound is not null &&
                   (latestSameHeadRound.FindingsParseIncomplete ||
                    (latestSameHeadRound.HasMergeBlockers && latestSameHeadRound.Findings.Count == 0))) {
            lines.Add("Open on current head: unknown; merge-blocker lines could not be fully normalized.");
            hasVisibleProgress = true;
        }

        if (snapshot.ResolvedSinceLastRound.Count > 0) {
            lines.Add($"Resolved since last round: {FormatCount(snapshot.ResolvedSinceLastRound.Count, "finding")}.");
            hasVisibleProgress = true;
        }

        if (snapshot.OpenFindings.Count > 0 || snapshot.ResolvedSinceLastRound.Count > 0) {
            lines.Add("Finding lifecycle:");
            AppendLifecycleTable(lines, snapshot);
            hasVisibleProgress = true;
        }

        if (!hasVisibleProgress) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("## History Progress 🔁");
        sb.AppendLine();
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendSummaryRounds(List<ReviewHistoryRound> rounds, List<ReviewHistoryFinding> openFindings,
        List<ReviewHistoryFinding> resolvedSinceLastRound, IReadOnlyList<IssueComment> issueComments, string currentHeadSha,
        ReviewSettings settings) {
        if (settings.History.MaxRounds <= 0) {
            return;
        }

        var ownedSummaries = new List<IssueComment>();
        foreach (var comment in issueComments) {
            if (!ReviewerApp.IsOwnedSummaryComment(comment) || string.IsNullOrWhiteSpace(comment.Body)) {
                continue;
            }

            if (IsProgressSummaryComment(comment.Body)) {
                continue;
            }

            ownedSummaries.Add(comment);
            if (ownedSummaries.Count >= settings.History.MaxRounds) {
                break;
            }
        }

        if (ownedSummaries.Count == 0) {
            return;
        }

        foreach (var summary in ownedSummaries) {
            if (!ReviewHistoryMarker.TryReadRounds(summary.Body, currentHeadSha, settings, out var markerRounds)) {
                continue;
            }

            rounds.AddRange(markerRounds);
            break;
        }

        if (rounds.Count == 0) {
            ownedSummaries.Reverse();
            for (var index = 0; index < ownedSummaries.Count; index++) {
                var round = BuildStickySummaryRound(ownedSummaries[index], currentHeadSha, settings, index + 1);
                if (round is null) {
                    continue;
                }

                rounds.Add(round);
            }
        }

        var latestSameHeadRound = FindLatestSameHeadRound(rounds);
        if (latestSameHeadRound is not null) {
            openFindings.AddRange(CollectLatestRoundOpenFindings(latestSameHeadRound.Findings));
        }

        AppendResolvedSinceLastRound(resolvedSinceLastRound, rounds);
    }

    private static bool IsProgressSummaryComment(string body) {
        return body.Contains("## IntelligenceX Review (in progress)", StringComparison.OrdinalIgnoreCase) ||
               body.Contains("### Review Checklist", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendExternalSummaries(List<ReviewHistoryExternalSummary> externalSummaries,
        IReadOnlyList<IssueComment> issueComments, ReviewSettings settings) {
        foreach (var comment in issueComments) {
            if (externalSummaries.Count >= settings.History.MaxItems) {
                break;
            }
            if (ReviewerApp.IsOwnedSummaryComment(comment) || string.IsNullOrWhiteSpace(comment.Body)) {
                continue;
            }
            var author = comment.Author ?? string.Empty;
            if (!IsExternalBotAuthor(author, settings)) {
                continue;
            }

            externalSummaries.Add(new ReviewHistoryExternalSummary {
                CommentId = comment.Id,
                Author = string.IsNullOrWhiteSpace(author) ? "unknown" : author.Trim(),
                Source = "external-bot",
                Excerpt = TrimText(comment.Body, settings.MaxCommentChars)
            });
        }
    }

    private static void AppendDerivedStateLines(List<string> lines, ReviewHistorySnapshot snapshot) {
        if (snapshot.OpenFindings.Count > 0) {
            lines.Add("- Open findings confirmed on the current head:");
            foreach (var finding in snapshot.OpenFindings) {
                lines.Add($"  - [{NormalizeSectionLabel(finding.Section)}] {finding.Text}");
            }
        } else if (snapshot.Rounds.Count > 0) {
            lines.Add("- Open findings confirmed on the current head: none. Prior-round findings below are candidates only; do not treat them as current blockers unless current diff or active thread evidence reconfirms them.");
        }

        if (snapshot.ResolvedSinceLastRound.Count == 0) {
            return;
        }

        lines.Add("- Resolved since the latest prior round:");
        foreach (var finding in snapshot.ResolvedSinceLastRound) {
            lines.Add($"  - [{NormalizeSectionLabel(finding.Section)}] {finding.Text}");
        }
    }

    private static void AppendResolvedSinceLastRound(List<ReviewHistoryFinding> resolvedSinceLastRound,
        IReadOnlyList<ReviewHistoryRound> rounds) {
        if (rounds.Count < 2) {
            return;
        }

        var latestRound = rounds[rounds.Count - 1];
        var previousRound = rounds[rounds.Count - 2];
        var latestFindings = ToFindingMap(latestRound.Findings);
        foreach (var finding in ToFindingMap(previousRound.Findings).Values) {
            if (!string.Equals(finding.Status, "open", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!latestFindings.TryGetValue(finding.Fingerprint, out var latestFinding)) {
                if (RoundsShareReviewedHead(previousRound, latestRound) &&
                    !latestRound.FindingsHitLimit &&
                    !LatestRoundHasUnparseableMergeBlockers(latestRound)) {
                    resolvedSinceLastRound.Add(new ReviewHistoryFinding {
                        Fingerprint = finding.Fingerprint,
                        Section = finding.Section,
                        Text = finding.Text,
                        Status = "resolved"
                    });
                }
                continue;
            }

            if (!string.Equals(latestFinding.Status, "resolved", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            resolvedSinceLastRound.Add(latestFinding);
        }
    }

    private static ReviewHistoryRound? FindLatestSameHeadRound(IReadOnlyList<ReviewHistoryRound> rounds) {
        for (var index = rounds.Count - 1; index >= 0; index--) {
            if (rounds[index].SameHeadAsCurrent) {
                return rounds[index];
            }
        }

        return null;
    }

    private static bool LatestRoundHasUnparseableMergeBlockers(ReviewHistoryRound latestRound) {
        return latestRound.FindingsParseIncomplete ||
               (latestRound.HasMergeBlockers && latestRound.Findings.Count == 0);
    }

    private static bool RoundsShareReviewedHead(ReviewHistoryRound previousRound, ReviewHistoryRound latestRound) {
        return !string.IsNullOrWhiteSpace(previousRound.ReviewedSha) &&
               !string.IsNullOrWhiteSpace(latestRound.ReviewedSha) &&
               string.Equals(previousRound.ReviewedSha, latestRound.ReviewedSha, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ReviewHistoryFinding> ToFindingMap(IReadOnlyList<ReviewHistoryFinding> findings) {
        var map = new Dictionary<string, ReviewHistoryFinding>(StringComparer.Ordinal);
        foreach (var finding in findings) {
            map[finding.Fingerprint] = finding;
        }
        return map;
    }

    private static IReadOnlyList<ReviewHistoryFinding> CollectLatestRoundOpenFindings(IReadOnlyList<ReviewHistoryFinding> findings) {
        if (findings.Count == 0) {
            return Array.Empty<ReviewHistoryFinding>();
        }

        var latestIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < findings.Count; index++) {
            var finding = findings[index];
            if (string.IsNullOrWhiteSpace(finding.Fingerprint)) {
                continue;
            }

            latestIndices[finding.Fingerprint] = index;
        }

        var open = new List<ReviewHistoryFinding>(findings.Count);
        for (var index = 0; index < findings.Count; index++) {
            var finding = findings[index];
            if (!string.IsNullOrWhiteSpace(finding.Fingerprint) &&
                latestIndices.TryGetValue(finding.Fingerprint, out var latestIndex) &&
                latestIndex != index) {
                continue;
            }

            if (!string.Equals(finding.Status, "open", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            open.Add(finding);
        }
        return open;
    }

    private static ReviewHistoryRound? BuildStickySummaryRound(IssueComment existingSummary, string? currentHeadSha,
        ReviewSettings settings, int sequence = 1) {
        return BuildSummaryRound(existingSummary.Body, existingSummary.Id, currentHeadSha, settings, sequence);
    }

    internal static ReviewHistoryRound? BuildSummaryRound(string? body, long? summaryCommentId, string? currentHeadSha,
        ReviewSettings settings, int sequence = 1) {
        if (string.IsNullOrWhiteSpace(body)) {
            return null;
        }

        body = ReviewHistoryMarker.Remove(body);
        ReviewSummaryParser.TryGetReviewedCommit(body, out var reviewedCommit);
        var findings = ReviewSummaryParser.ExtractMergeBlockerFindings(body, settings, settings.History.MaxItems,
            out var findingsHitLimit, out var findingsParseIncomplete);
        var hasMergeBlockers = ReviewSummaryParser.HasMergeBlockers(body, settings);
        if (hasMergeBlockers &&
            findings.Count == 0 &&
            !findingsParseIncomplete &&
            ReviewSummaryParser.HasAnyMergeBlockerSection(body, settings)) {
            hasMergeBlockers = false;
        }
        var recommendation = ResolveRecommendation(hasMergeBlockers, findingsParseIncomplete);
        var mergeBlockerStatus = findings.Count == 0
            ? findingsParseIncomplete
                ? "unknown; merge-blocker lines were present but could not be normalized."
                : hasMergeBlockers
                ? "present, but markdown items could not be normalized."
                : "none."
            : findingsParseIncomplete
                ? $"{findings.Count} normalized item(s), but additional merge-blocker lines could not be normalized."
            : $"{findings.Count} normalized item(s).";
        return new ReviewHistoryRound {
            Sequence = sequence,
            Source = "intelligencex",
            SummaryCommentId = summaryCommentId,
            ReviewedSha = reviewedCommit?.Trim() ?? string.Empty,
            SameHeadAsCurrent = !string.IsNullOrWhiteSpace(reviewedCommit) &&
                                !string.IsNullOrWhiteSpace(currentHeadSha) &&
                                currentHeadSha.StartsWith(reviewedCommit!, StringComparison.OrdinalIgnoreCase),
            HasMergeBlockers = hasMergeBlockers,
            MergeBlockerStatus = mergeBlockerStatus,
            Recommendation = recommendation,
            PositiveHighlights = ExtractSectionItems(body, new[] { "Excellent Aspects", "Code Quality Assessment" }, 3),
            RiskNotes = ExtractSectionItems(body, new[] { "Other Issues", "Security & Performance", "Backward Compatibility" }, 3),
            FollowUps = ExtractSectionItems(body, new[] { "Recommendations", "Next Steps", "Test Quality", "Documentation" }, 3),
            FindingsHitLimit = findingsHitLimit,
            FindingsParseIncomplete = findingsParseIncomplete,
            Findings = ConvertFindings(findings)
        };
    }

    private static bool AppendPostureTable(List<string> lines, ReviewHistoryRound round) {
        if (string.IsNullOrWhiteSpace(round.Recommendation) &&
            round.PositiveHighlights.Count == 0 &&
            round.RiskNotes.Count == 0 &&
            round.FollowUps.Count == 0) {
            return false;
        }

        if (round.PositiveHighlights.Count == 0 &&
            round.RiskNotes.Count == 0 &&
            round.FollowUps.Count == 0 &&
            string.Equals(round.Recommendation, "approve", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        lines.Add("Latest review posture:");
        lines.Add("| Recommendation | Good | Risks / Bad | Follow-up |");
        lines.Add("| --- | --- | --- | --- |");
        lines.Add($"| {EscapeTableCell(NormalizeRecommendationLabel(round.Recommendation))} | {EscapeTableCell(JoinSummaryItems(round.PositiveHighlights))} | {EscapeTableCell(JoinSummaryItems(round.RiskNotes))} | {EscapeTableCell(JoinSummaryItems(round.FollowUps))} |");
        return true;
    }

    private static void AppendLifecycleTable(List<string> lines, ReviewHistorySnapshot snapshot) {
        var priorOpenFingerprints = CollectPriorOpenFingerprints(snapshot.Rounds);
        lines.Add("| State | Section | Finding |");
        lines.Add("| --- | --- | --- |");
        foreach (var finding in snapshot.OpenFindings) {
            var state = priorOpenFingerprints.Contains(finding.Fingerprint) ? "Still open" : "New";
            lines.Add($"| {EscapeTableCell(state)} | {EscapeTableCell(NormalizeSectionLabel(finding.Section))} | {EscapeTableCell(finding.Text)} |");
        }

        foreach (var finding in snapshot.ResolvedSinceLastRound) {
            lines.Add($"| Resolved | {EscapeTableCell(NormalizeSectionLabel(finding.Section))} | {EscapeTableCell(finding.Text)} |");
        }
    }

    private static string FormatCount(int count, string singular) {
        if (count == 1) {
            return $"1 {singular}";
        }

        return $"{count} {singular}s";
    }

    private static HashSet<string> CollectPriorOpenFingerprints(IReadOnlyList<ReviewHistoryRound> rounds) {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        if (rounds.Count <= 1) {
            return fingerprints;
        }

        var latestSameHeadRound = FindLatestSameHeadRound(rounds);
        foreach (var round in rounds) {
            if (ReferenceEquals(round, latestSameHeadRound)) {
                continue;
            }

            foreach (var finding in round.Findings) {
                if (string.IsNullOrWhiteSpace(finding.Fingerprint) ||
                    !string.Equals(finding.Status, "open", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                fingerprints.Add(finding.Fingerprint);
            }
        }

        return fingerprints;
    }

    private static string EscapeTableCell(string value) {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string JoinSummaryItems(IReadOnlyList<string> items) {
        if (items.Count == 0) {
            return "None noted.";
        }

        return string.Join("; ", items);
    }

    private static IReadOnlyList<ReviewHistoryFinding> ConvertFindings(IReadOnlyList<ReviewSummaryFinding> findings) {
        if (findings.Count == 0) {
            return Array.Empty<ReviewHistoryFinding>();
        }

        var converted = new List<ReviewHistoryFinding>(findings.Count);
        foreach (var finding in findings) {
            converted.Add(new ReviewHistoryFinding {
                Fingerprint = finding.Fingerprint,
                Section = finding.Section,
                Text = finding.Text,
                Status = finding.Status
            });
        }
        return converted;
    }

    private static string ResolveRecommendation(bool hasMergeBlockers, bool findingsParseIncomplete) {
        if (findingsParseIncomplete) {
            return "manual-review";
        }

        return hasMergeBlockers ? "needs-work" : "approve";
    }

    private static string NormalizeRecommendationLabel(string recommendation) {
        return recommendation.Trim().ToLowerInvariant() switch {
            "approve" => "Approve",
            "needs-work" => "Needs work",
            "manual-review" => "Manual review",
            "skipped" => "Skipped",
            _ => string.IsNullOrWhiteSpace(recommendation) ? "Unknown" : recommendation.Trim()
        };
    }

    private static IReadOnlyList<string> ExtractSectionItems(string body, IReadOnlyList<string> sectionNames, int maxItems) {
        if (string.IsNullOrWhiteSpace(body) || maxItems <= 0 || sectionNames.Count == 0) {
            return Array.Empty<string>();
        }

        var targetSections = new HashSet<string>(sectionNames.Select(NormalizeHeader), StringComparer.OrdinalIgnoreCase);
        var items = new List<string>();
        var inTargetSection = false;
        using var reader = new StringReader(ReviewFormatter.NormalizeSectionLayout(body));
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)) {
                inTargetSection = targetSections.Contains(NormalizeHeader(trimmed));
                continue;
            }

            if (!inTargetSection || items.Count >= maxItems || !TryExtractSummaryItem(trimmed, out var item)) {
                continue;
            }

            items.Add(item);
        }

        return items;
    }

    private static bool TryExtractSummaryItem(string line, out string item) {
        item = string.Empty;
        if (string.IsNullOrWhiteSpace(line) ||
            line.StartsWith("<!--", StringComparison.Ordinal) ||
            line.StartsWith("|", StringComparison.Ordinal) ||
            line.StartsWith("```", StringComparison.Ordinal) ||
            IsNoneLine(line)) {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal) ||
            trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
            item = trimmed.Substring(5).Trim();
        } else if (trimmed.StartsWith("-", StringComparison.Ordinal) ||
                   trimmed.StartsWith("*", StringComparison.Ordinal)) {
            item = trimmed.Substring(1).Trim();
        } else {
            return false;
        }

        return !string.IsNullOrWhiteSpace(item) && !IsNoneLine(item);
    }

    private static bool IsNoneLine(string value) {
        var normalized = value.Trim().TrimEnd('.').Trim();
        return normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("none noted", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHeader(string header) {
        var trimmed = header.Trim();
        while (trimmed.StartsWith("#", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(1).TrimStart();
        }

        var sb = new StringBuilder(trimmed.Length);
        var pendingSpace = false;
        foreach (var ch in trimmed.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) {
                if (pendingSpace && sb.Length > 0) {
                    sb.Append(' ');
                }
                sb.Append(ch);
                pendingSpace = false;
            } else {
                pendingSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static void AppendStickySummaryLines(List<string> lines, ReviewHistoryRound round, string currentHeadSha) {
        var hasReviewedCommit = !string.IsNullOrWhiteSpace(round.ReviewedSha);
        var reviewedCommitLabel = hasReviewedCommit ? $"`{ShortSha(round.ReviewedSha)}`" : "an unknown commit";
        var currentHeadLabel = string.IsNullOrWhiteSpace(currentHeadSha) ? "unknown head" : $"`{ShortSha(currentHeadSha)}`";
        var roundLabel = round.SameHeadAsCurrent
            ? $"same SHA as current head ({currentHeadLabel})"
            : hasReviewedCommit
                ? $"prior round on {reviewedCommitLabel} before current head {currentHeadLabel}"
                : $"prior round before current head {currentHeadLabel}";
        lines.Add($"- Round {round.Sequence}: IX sticky summary reviewed {reviewedCommitLabel} ({roundLabel}).");
        if (!string.IsNullOrWhiteSpace(round.Recommendation)) {
            lines.Add($"- IX recommendation: {NormalizeRecommendationLabel(round.Recommendation)}.");
        }
        AppendRoundHighlights(lines, "Good", round.PositiveHighlights);
        AppendRoundHighlights(lines, "Risks / bad", round.RiskNotes);
        AppendRoundHighlights(lines, "Follow-up", round.FollowUps);

        if (round.Findings.Count == 0) {
            lines.Add($"- IX merge blockers in sticky summary: {round.MergeBlockerStatus}");
            return;
        }

        lines.Add(round.SameHeadAsCurrent
            ? "- Current-head IX merge blockers from sticky summary:"
            : "- Prior-head IX merge blockers from sticky summary (candidates only; revalidate against current diff or active threads before treating as blockers):");
        foreach (var item in round.Findings) {
            lines.Add($"  - [{NormalizeSectionLabel(item.Section)}] {item.Text}");
        }
        if (round.FindingsParseIncomplete) {
            lines.Add("  - Additional merge-blocker lines were present but could not be normalized; do not infer missing same-head findings as resolved from this round alone.");
        }
    }

    private static void AppendRoundHighlights(List<string> lines, string label, IReadOnlyList<string> items) {
        if (items.Count == 0) {
            return;
        }

        lines.Add($"- IX {label}: {string.Join("; ", items)}");
    }

    private static ReviewHistoryThreadSnapshot? BuildThreadSnapshot(IReadOnlyList<PullRequestReviewThread> reviewThreads,
        ReviewSettings settings) {
        var active = 0;
        var resolved = 0;
        var stale = 0;
        var excerpts = new List<ReviewHistoryThreadExcerpt>();
        foreach (var thread in reviewThreads) {
            if (thread.IsResolved) {
                resolved++;
            } else if (thread.IsOutdated) {
                stale++;
            } else {
                active++;
            }

            if (excerpts.Count >= settings.History.MaxItems) {
                continue;
            }
            if (thread.IsResolved) {
                continue;
            }

            foreach (var comment in thread.Comments) {
                if (excerpts.Count >= settings.History.MaxItems) {
                    break;
                }
                if (!ShouldIncludeThreadComment(comment, settings)) {
                    continue;
                }

                excerpts.Add(new ReviewHistoryThreadExcerpt {
                    ThreadId = thread.Id,
                    Status = thread.IsOutdated ? "stale" : "active",
                    Author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author!,
                    Body = TrimText(comment.Body, settings.MaxCommentChars),
                    Path = comment.Path,
                    Line = comment.Line
                });
            }
        }

        if (active == 0 && resolved == 0 && stale == 0) {
            return null;
        }

        return new ReviewHistoryThreadSnapshot {
            ActiveCount = active,
            ResolvedCount = resolved,
            StaleCount = stale,
            Excerpts = excerpts
        };
    }

    private static void AppendThreadLines(List<string> lines, ReviewHistoryThreadSnapshot snapshot) {
        lines.Add($"- Review threads snapshot: active {snapshot.ActiveCount}, resolved {snapshot.ResolvedCount}, stale {snapshot.StaleCount}.");
        if (snapshot.Excerpts.Count == 0) {
            return;
        }

        lines.Add("- Unresolved thread excerpts:");
        foreach (var excerpt in snapshot.Excerpts) {
            var location = string.IsNullOrWhiteSpace(excerpt.Path)
                ? string.Empty
                : excerpt.Line.HasValue
                    ? $" ({excerpt.Path}:{excerpt.Line.Value})"
                    : $" ({excerpt.Path})";
            lines.Add($"  - [{excerpt.Status}] {excerpt.Author}{location}: {excerpt.Body}");
        }
    }

    private static void AppendExternalSummaryLines(List<string> lines,
        IReadOnlyList<ReviewHistoryExternalSummary> summaries) {
        lines.Add("- External bot summaries included as supporting context only:");
        foreach (var summary in summaries) {
            lines.Add($"  - [{summary.Author}] {summary.Excerpt}");
        }
    }

    private static bool ShouldIncludeThreadComment(PullRequestReviewThreadComment comment, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(comment.Body)) {
            return false;
        }

        var author = comment.Author ?? string.Empty;
        if (settings.ReviewThreadsIncludeBots) {
            return true;
        }

        if (author.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        foreach (var botLogin in settings.ReviewThreadsAutoResolveBotLogins) {
            if (string.Equals(author, botLogin, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    private static bool IsExternalBotAuthor(string author, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(author)) {
            return false;
        }

        foreach (var login in settings.History.ExternalBotLogins) {
            if (string.Equals(author.Trim(), login, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeSectionLabel(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "unknown";
        }

        return value.Trim().ToLowerInvariant() switch {
            "todo list" => "todo",
            "critical issues" => "critical",
            _ => value.Trim()
        };
    }

    private static string ShortSha(string value) {
        var trimmed = value.Trim();
        return trimmed.Length <= 7 ? trimmed : trimmed.Substring(0, 7);
    }

    private static string TrimText(string value, int maxChars) {
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (maxChars <= 0 || normalized.Length <= maxChars) {
            return normalized;
        }
        return normalized.Substring(0, maxChars) + "...";
    }
}
