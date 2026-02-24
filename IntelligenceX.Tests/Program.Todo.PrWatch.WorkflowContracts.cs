namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestPrWatchMonitorWorkflowReviewTriggersIncludeSubmittedAndEdited() {
        var workflowPath = ResolveRepoFilePath(".github", "workflows", "ix-pr-babysit-monitor.yml");
        var eventTypes = ParseWorkflowOnEventTypes(workflowPath);

        AssertEqual(true, eventTypes.TryGetValue("pull_request_review", out var reviewTypes),
            "monitor workflow defines pull_request_review trigger");
        AssertContains(reviewTypes!, "submitted", "monitor workflow includes submitted review trigger");
        AssertContains(reviewTypes!, "edited", "monitor workflow includes edited review trigger");
    }

    private static void TestPrWatchMonitorWorkflowExcludesReviewCommentTrigger() {
        var workflowPath = ResolveRepoFilePath(".github", "workflows", "ix-pr-babysit-monitor.yml");
        var eventTypes = ParseWorkflowOnEventTypes(workflowPath);

        AssertEqual(false, eventTypes.ContainsKey("pull_request_review_comment"),
            "monitor workflow should not define pull_request_review_comment trigger");
    }

    private static Dictionary<string, List<string>> ParseWorkflowOnEventTypes(string workflowPath) {
        var lines = File.ReadAllLines(workflowPath);
        var events = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inOnBlock = false;
        var onIndent = -1;
        string? currentEvent = null;
        var inTypesBlock = false;
        var typesIndent = -1;

        foreach (var rawLine in lines) {
            if (string.IsNullOrWhiteSpace(rawLine)) {
                continue;
            }

            var trimmedStart = rawLine.TrimStart();
            if (trimmedStart.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }

            var indent = rawLine.Length - trimmedStart.Length;
            var trimmed = trimmedStart.TrimEnd();

            if (!inOnBlock) {
                if (string.Equals(trimmed, "on:", StringComparison.Ordinal)) {
                    inOnBlock = true;
                    onIndent = indent;
                }
                continue;
            }

            if (indent <= onIndent && !trimmed.StartsWith("-", StringComparison.Ordinal)) {
                break;
            }

            if (inTypesBlock && indent <= typesIndent && !trimmed.StartsWith("-", StringComparison.Ordinal)) {
                inTypesBlock = false;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal)) {
                if (inTypesBlock && currentEvent is not null && events.TryGetValue(currentEvent, out var types)) {
                    var value = trimmed.Substring(2).Trim();
                    if (!string.IsNullOrWhiteSpace(value)) {
                        types.Add(value);
                    }
                }
                continue;
            }

            if (!trimmed.EndsWith(":", StringComparison.Ordinal)) {
                continue;
            }

            var key = trimmed.Substring(0, trimmed.Length - 1).Trim();
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            if (string.Equals(key, "types", StringComparison.OrdinalIgnoreCase) && currentEvent is not null) {
                inTypesBlock = true;
                typesIndent = indent;
                if (!events.ContainsKey(currentEvent)) {
                    events[currentEvent] = new List<string>();
                }
                continue;
            }

            if (indent == onIndent + 2) {
                currentEvent = key;
                inTypesBlock = false;
                if (!events.ContainsKey(currentEvent)) {
                    events[currentEvent] = new List<string>();
                }
            }
        }

        return events;
    }

    private static string ResolveRepoFilePath(params string[] relativeSegments) {
        var fromCurrent = TryResolveRepoFilePath(Environment.CurrentDirectory, relativeSegments);
        if (!string.IsNullOrWhiteSpace(fromCurrent)) {
            return fromCurrent!;
        }

        var fromBase = TryResolveRepoFilePath(AppContext.BaseDirectory, relativeSegments);
        if (!string.IsNullOrWhiteSpace(fromBase)) {
            return fromBase!;
        }

        throw new InvalidOperationException($"Unable to locate repository file: {Path.Combine(relativeSegments)}");
    }

    private static string? TryResolveRepoFilePath(string startPath, IReadOnlyList<string> relativeSegments) {
        if (string.IsNullOrWhiteSpace(startPath) || relativeSegments is null || relativeSegments.Count == 0) {
            return null;
        }

        var current = new DirectoryInfo(startPath);
        while (current is not null) {
            var candidate = current.FullName;
            for (var i = 0; i < relativeSegments.Count; i++) {
                candidate = Path.Combine(candidate, relativeSegments[i]);
            }

            if (File.Exists(candidate)) {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
#endif
}
