using System;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class ProjectSyncRunner {
    internal static PrWatchGovernanceContext? SelectPrWatchGovernanceContextFromIssueList(JsonElement issuesRoot) {
        if (issuesRoot.ValueKind != JsonValueKind.Array) {
            return null;
        }

        foreach (var preferredSource in new[] { "weekly-governance", "schedule" }) {
            foreach (var issue in issuesRoot.EnumerateArray()) {
                var context = TryParsePrWatchGovernanceContext(issue, preferredSource);
                if (context is not null) {
                    return context;
                }
            }
        }

        return null;
    }

    private static async Task<PrWatchGovernanceContext?> TryLoadPrWatchGovernanceContextAsync(string repo) {
        var (code, stdOut, stdErr) = await GhCli.RunAsync(
            "api",
            $"repos/{repo}/issues?state=open&per_page=100").ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to load pr-watch governance tracker context: {(string.IsNullOrWhiteSpace(stdErr) ? "unknown error" : stdErr.Trim())}");
            return null;
        }

        try {
            using var doc = JsonDocument.Parse(stdOut);
            return SelectPrWatchGovernanceContextFromIssueList(doc.RootElement);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to parse pr-watch governance tracker context: {ex.Message}");
            return null;
        }
    }

    private static PrWatchGovernanceContext? TryParsePrWatchGovernanceContext(JsonElement issue, string source) {
        if (issue.ValueKind != JsonValueKind.Object) {
            return null;
        }

        if (TryGetProperty(issue, "pull_request", out var pullRequestProperty) &&
            pullRequestProperty.ValueKind != JsonValueKind.Null &&
            pullRequestProperty.ValueKind != JsonValueKind.Undefined) {
            return null;
        }

        var body = ReadNullableStringCaseInsensitive(issue, "body");
        if (string.IsNullOrWhiteSpace(body) ||
            body.IndexOf($"<!-- intelligencex:pr-watch-rollup-tracker:{source} -->", StringComparison.OrdinalIgnoreCase) < 0) {
            return null;
        }

        var summaryLine = string.Empty;
        foreach (var rawLine in body.Split('\n')) {
            var line = rawLine.Trim();
            if (!line.StartsWith("- Governance: ", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            summaryLine = line["- Governance: ".Length..].Trim();
            break;
        }

        return new PrWatchGovernanceContext(
            Source: source,
            RetryPolicyReviewSuggested: summaryLine.IndexOf("retry-policy-review-suggested=yes", StringComparison.OrdinalIgnoreCase) >= 0,
            SummaryLine: summaryLine,
            TrackerIssueUrl: ReadNullableStringCaseInsensitive(issue, "html_url") ?? string.Empty);
    }
}
