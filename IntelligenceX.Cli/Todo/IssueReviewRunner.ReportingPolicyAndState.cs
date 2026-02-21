using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class IssueReviewRunner {
    private static object BuildReport(
        Options options,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<IssueReviewAssessment> assessments,
        IReadOnlyList<int> closedIssueNumbers) {
        var infra = assessments.Where(value => value.IsInfraBlocker).ToList();
        return new {
            schema = "intelligencex.issue-review.v1",
            generatedAtUtc = generatedAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            repo = options.Repo,
            settings = new {
                maxIssues = options.MaxIssues,
                staleDays = options.StaleDays,
                minConsecutiveCandidatesForClose = options.MinConsecutiveCandidatesForClose,
                minAutoCloseConfidence = options.MinAutoCloseConfidence,
                statePath = options.StatePath,
                autoCloseAllowLabels = options.AutoCloseAllowLabels,
                autoCloseDenyLabels = options.AutoCloseDenyLabels,
                proposalOnly = options.ProposalOnly,
                applyClose = options.ApplyClose,
                closeReason = options.CloseReason
            },
            summary = new {
                openIssuesScanned = assessments.Count,
                infraBlockers = infra.Count,
                noLongerApplicable = infra.Count(value => value.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase)),
                autoCloseEligible = infra.Count(value =>
                    value.EligibleForAutoClose &&
                    value.ProposedAction.Equals("close", StringComparison.OrdinalIgnoreCase) &&
                    value.ActionConfidence >= options.MinAutoCloseConfidence),
                proposedClose = assessments.Count(value => value.ProposedAction.Equals("close", StringComparison.OrdinalIgnoreCase)),
                proposedKeepOpen = assessments.Count(value => value.ProposedAction.Equals("keep-open", StringComparison.OrdinalIgnoreCase)),
                proposedNeedsHumanReview = assessments.Count(value => value.ProposedAction.Equals("needs-human-review", StringComparison.OrdinalIgnoreCase)),
                proposedIgnore = assessments.Count(value => value.ProposedAction.Equals("ignore", StringComparison.OrdinalIgnoreCase)),
                needsReview = infra.Count(value => value.Classification.Equals("needs-review", StringComparison.OrdinalIgnoreCase)),
                active = infra.Count(value => value.Classification.Equals("active", StringComparison.OrdinalIgnoreCase)),
                closedByAutomation = closedIssueNumbers.Count
            },
            closedIssueNumbers = closedIssueNumbers,
            items = assessments.Select(value => new {
                number = value.Number,
                title = value.Title,
                url = value.Url,
                isInfraBlocker = value.IsInfraBlocker,
                classification = value.Classification,
                eligibleForAutoClose = value.EligibleForAutoClose,
                candidateStreak = value.CandidateStreak,
                proposedAction = value.ProposedAction,
                actionConfidence = value.ActionConfidence,
                actionConfidenceLevel = ConfidenceLevel(value.ActionConfidence),
                confidenceSignals = value.ConfidenceSignals ?? Array.Empty<string>(),
                reopenedCount = value.ReopenedCount,
                ageDays = value.AgeDays,
                linkedPullRequests = value.LinkedPullRequests,
                linkedPullRequestStates = value.LinkedPullRequestStates,
                labels = value.Labels,
                reason = value.Reason
            }).ToList()
        };
    }

    private static string BuildMarkdownSummary(
        Options options,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<IssueReviewAssessment> assessments,
        IReadOnlyList<int> closedIssueNumbers) {
        var infra = assessments.Where(value => value.IsInfraBlocker).ToList();
        var noLongerApplicable = infra
            .Where(value => value.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var needsReview = infra
            .Where(value => value.Classification.Equals("needs-review", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var active = infra
            .Where(value => value.Classification.Equals("active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# Issue Review");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {generatedAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"- Repo: `{options.Repo}`");
        builder.AppendLine($"- Open issues scanned: {assessments.Count}");
        builder.AppendLine($"- Infra blockers detected: {infra.Count}");
        builder.AppendLine($"- No-longer-applicable: {noLongerApplicable.Count}");
        builder.AppendLine($"- Auto-close eligible: {noLongerApplicable.Count(value => value.EligibleForAutoClose && value.ProposedAction.Equals("close", StringComparison.OrdinalIgnoreCase) && value.ActionConfidence >= options.MinAutoCloseConfidence)}");
        builder.AppendLine($"- Needs review: {needsReview.Count}");
        builder.AppendLine($"- Active infra blockers: {active.Count}");
        builder.AppendLine($"- Min consecutive candidates for close: {options.MinConsecutiveCandidatesForClose}");
        builder.AppendLine($"- Min auto-close confidence: {options.MinAutoCloseConfidence}");
        builder.AppendLine($"- Proposed action `close`: {assessments.Count(value => value.ProposedAction.Equals("close", StringComparison.OrdinalIgnoreCase))}");
        builder.AppendLine($"- Proposed action `keep-open`: {assessments.Count(value => value.ProposedAction.Equals("keep-open", StringComparison.OrdinalIgnoreCase))}");
        builder.AppendLine($"- Proposed action `needs-human-review`: {assessments.Count(value => value.ProposedAction.Equals("needs-human-review", StringComparison.OrdinalIgnoreCase))}");
        builder.AppendLine(options.StatePath is null
            ? "- State path: disabled"
            : $"- State path: `{options.StatePath}`");
        if (options.AutoCloseAllowLabels.Count > 0) {
            builder.AppendLine($"- Auto-close allow labels: `{string.Join("`, `", options.AutoCloseAllowLabels)}`");
        }
        if (options.AutoCloseDenyLabels.Count > 0) {
            builder.AppendLine($"- Auto-close deny labels: `{string.Join("`, `", options.AutoCloseDenyLabels)}`");
        }
        builder.AppendLine(options.ProposalOnly
            ? "- Proposal-only mode: close operations disabled."
            : options.ApplyClose
            ? $"- Closed by automation: {closedIssueNumbers.Count}"
            : "- Dry-run mode: no issues were closed.");
        builder.AppendLine();
        AppendSection(builder, "No-Longer-Applicable Candidates", noLongerApplicable);
        AppendSection(builder, "Needs Review", needsReview);
        AppendSection(builder, "Active Infra Blockers", active);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<IssueReviewAssessment> items) {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (items.Count == 0) {
            builder.AppendLine("None.");
            builder.AppendLine();
            return;
        }

        foreach (var item in items) {
            var linked = item.LinkedPullRequestStates.Count == 0
                ? "none"
                : string.Join(", ", item.LinkedPullRequestStates);
            var streak = item.CandidateStreak > 0
                ? $" | streak {item.CandidateStreak}"
                : string.Empty;
            var eligibility = item.EligibleForAutoClose
                ? " | eligible auto-close"
                : string.Empty;
            var action = $" | action {item.ProposedAction} ({item.ActionConfidence}/100,{ConfidenceLevel(item.ActionConfidence)})";
            builder.AppendLine(
                $"- #{item.Number} [{item.Title}]({item.Url}) | age {item.AgeDays.ToString("0.0", CultureInfo.InvariantCulture)}d{streak}{eligibility}{action} | linked PRs: {linked} | {item.Reason}");
        }
        builder.AppendLine();
    }

    private static int ClassificationRank(string classification) {
        return classification.ToLowerInvariant() switch {
            "no-longer-applicable" => 0,
            "needs-review" => 1,
            "active" => 2,
            _ => 3
        };
    }

    private static int ProposedActionRank(string proposedAction) {
        return proposedAction.ToLowerInvariant() switch {
            "close" => 0,
            "needs-human-review" => 1,
            "keep-open" => 2,
            "ignore" => 3,
            _ => 4
        };
    }

    private static string ConfidenceLevel(int confidence) {
        return confidence switch {
            >= 80 => "high",
            >= 60 => "medium",
            _ => "low"
        };
    }

    internal static IssueReviewPolicy BuildPolicy(
        IReadOnlyCollection<string> autoCloseAllowLabels,
        IReadOnlyCollection<string> autoCloseDenyLabels) {
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in autoCloseAllowLabels) {
            if (!string.IsNullOrWhiteSpace(value)) {
                allow.Add(value.Trim());
            }
        }

        var deny = new HashSet<string>(ProtectedLabels, StringComparer.OrdinalIgnoreCase);
        foreach (var value in autoCloseDenyLabels) {
            if (!string.IsNullOrWhiteSpace(value)) {
                deny.Add(value.Trim());
            }
        }

        return new IssueReviewPolicy(allow, deny);
    }

    private static IssueReviewState LoadState(string? path, string repo) {
        if (string.IsNullOrWhiteSpace(path)) {
            return new IssueReviewState { Repo = repo };
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) {
            return new IssueReviewState { Repo = repo };
        }

        try {
            var content = File.ReadAllText(fullPath);
            var state = JsonSerializer.Deserialize<IssueReviewState>(content);
            if (state is null) {
                return new IssueReviewState { Repo = repo };
            }

            if (!string.IsNullOrWhiteSpace(state.Repo) &&
                !state.Repo.Equals(repo, StringComparison.OrdinalIgnoreCase)) {
                return new IssueReviewState { Repo = repo };
            }

            state.Repo = repo;
            state.CandidateStreaks ??= new Dictionary<int, int>();
            return state;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to load issue-review state from '{path}': {ex.Message}");
            return new IssueReviewState { Repo = repo };
        }
    }

    private static IssueReviewState BuildUpdatedState(
        string repo,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<IssueReviewAssessment> assessments) {
        var state = new IssueReviewState {
            Repo = repo,
            UpdatedAtUtc = updatedAtUtc
        };

        foreach (var assessment in assessments) {
            if (!assessment.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase) ||
                assessment.CandidateStreak <= 0) {
                continue;
            }
            state.CandidateStreaks[assessment.Number] = assessment.CandidateStreak;
        }

        return state;
    }

    private static void SaveState(string? path, IssueReviewState state) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        try {
            WriteText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to write issue-review state to '{path}': {ex.Message}");
        }
    }

    private static bool IsInfraBlocker(IssueReviewCandidateIssue issue) {
        foreach (var label in issue.Labels) {
            if (label.Equals("infra", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("infra-blocker", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("infrastructure", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("blocker", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("ci", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        var text = $"{issue.Title}\n{issue.Body}";
        foreach (var keyword in InfraKeywords) {
            if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePullRequestState(PullRequestReference reference) {
        if (reference.MergedAtUtc.HasValue) {
            return "merged";
        }

        return reference.State.Trim().ToUpperInvariant() switch {
            "OPEN" => "open",
            "CLOSED" => "closed",
            "MERGED" => "merged",
            _ => "unknown"
        };
    }

    private static bool TryExtractNumber(Match match, string groupName, out int value) {
        value = 0;
        var raw = match.Groups[groupName].Value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static (string Owner, string Name) SplitRepo(string repo) {
        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repo value: '{repo}'. Expected owner/name.");
        }
        return (parts[0], parts[1]);
    }

    private static int ReadInt(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value)) {
            return 0;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) {
            return number;
        }
        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string ReadString(JsonElement element, string name) {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset ReadDate(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) {
            return DateTimeOffset.UtcNow;
        }
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? ReadNullableDate(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String) {
            return null;
        }
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement issue) {
        if (!issue.TryGetProperty("labels", out var labelsElement) || labelsElement.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var labels = new List<string>();
        foreach (var label in labelsElement.EnumerateArray()) {
            if (label.ValueKind == JsonValueKind.Object &&
                label.TryGetProperty("name", out var nameProp) &&
                nameProp.ValueKind == JsonValueKind.String) {
                var value = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    labels.Add(value.Trim());
                }
                continue;
            }

            if (label.ValueKind == JsonValueKind.String) {
                var value = label.GetString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    labels.Add(value.Trim());
                }
            }
        }

        return labels;
    }

    private static void WriteText(string path, string content) {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }
}
