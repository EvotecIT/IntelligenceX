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

    private static void TestPrWatchNightlyConsolidationWorkflowUsesDirectCliInvocation() {
        var workflowPath = ResolveRepoFilePath(".github", "workflows", "ix-pr-babysit-nightly-consolidation.yml");
        var content = File.ReadAllText(workflowPath);

        AssertContainsText(content, "todo pr-watch-consolidate", "nightly workflow should invoke consolidation CLI command");
        AssertContainsText(content, "--retry-failure-policy", "nightly workflow should pass retry failure policy through to the CLI");
        AssertContainsText(content, "--apply-governance-signal-label", "nightly workflow should pass governance label option through to the CLI");
        AssertEqual(false, content.Contains("set -euo pipefail", StringComparison.Ordinal),
            "nightly workflow should avoid shell wrapper logic");
        AssertEqual(false, content.Contains("if [ -z \"${MAX_PRS}\" ]", StringComparison.Ordinal),
            "nightly workflow should keep defaulting logic in CLI, not YAML shell conditionals");
    }

    private static void TestPrWatchWeeklyGovernanceWorkflowExposesOptionalOverrides() {
        var workflowPath = ResolveRepoFilePath(".github", "workflows", "ix-pr-babysit-weekly-governance.yml");
        var content = File.ReadAllText(workflowPath);

        AssertContainsText(content, "workflow_dispatch:", "weekly governance workflow should support manual dispatch");
        AssertContainsText(content, "retry_failure_policy:", "weekly governance workflow should expose retry policy override");
        AssertContainsText(content, "publish_tracking_issue:", "weekly governance workflow should expose tracker publishing override");
        AssertContainsText(content, "apply_governance_signal_label:", "weekly governance workflow should expose governance label override");
        AssertContainsText(content, "tracker_issue_labels:", "weekly governance workflow should expose tracker label override");
        AssertContainsText(content, "uses: ./.github/workflows/ix-pr-babysit-nightly-consolidation.yml",
            "weekly governance workflow should remain a wrapper over nightly consolidation");
    }

    private static void TestPrWatchAssistRetryWorkflowUsesDirectCliInvocation() {
        var workflowPath = ResolveRepoFilePath(".github", "workflows", "ix-pr-babysit-assist-retry.yml");
        var content = File.ReadAllText(workflowPath);

        AssertContainsText(content, "todo pr-watch-assist-retry", "assist workflow should invoke assist CLI command");
        AssertContainsText(content, "--retry-failure-policy", "assist workflow should pass retry failure policy through to the CLI");
        AssertEqual(false, content.Contains("set -euo pipefail", StringComparison.Ordinal),
            "assist workflow should avoid shell wrapper logic");
    }

    private static void TestPrWatchWorkflowParserSupportsScalarTypesValue() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-prwatch-workflow-scalar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var workflowPath = Path.Combine(tempDir, "workflow.yml");
            var content = string.Join('\n', new[] {
                "on:",
                "  pull_request_review:",
                "    types: submitted",
                "jobs:",
                "  test:",
                "    runs-on: ubuntu-latest"
            }) + "\n";
            File.WriteAllText(workflowPath, content);

            var eventTypes = ParseWorkflowOnEventTypes(workflowPath);
            AssertEqual(true, eventTypes.TryGetValue("pull_request_review", out var reviewTypes),
                "scalar types parser event key");
            AssertEqual(1, reviewTypes!.Count, "scalar types parser type count");
            AssertContains(reviewTypes, "submitted", "scalar types parser submitted");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TestPrWatchWorkflowParserSupportsFlowSequenceTypesValue() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-prwatch-workflow-flow-seq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var workflowPath = Path.Combine(tempDir, "workflow.yml");
            var content = string.Join('\n', new[] {
                "on:",
                "  pull_request_review:",
                "    types: ['submitted', \"edited\"]",
                "jobs:",
                "  test:",
                "    runs-on: ubuntu-latest"
            }) + "\n";
            File.WriteAllText(workflowPath, content);

            var eventTypes = ParseWorkflowOnEventTypes(workflowPath);
            AssertEqual(true, eventTypes.TryGetValue("pull_request_review", out var reviewTypes),
                "flow sequence parser event key");
            AssertEqual(2, reviewTypes!.Count, "flow sequence parser type count");
            AssertContains(reviewTypes!, "submitted", "flow sequence parser submitted");
            AssertContains(reviewTypes!, "edited", "flow sequence parser edited");
        } finally {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TestPrWatchWorkflowParserMissingFileHasClearError() {
        var workflowPath = Path.Combine(Path.GetTempPath(), "ix-prwatch-workflow-missing-" + Guid.NewGuid().ToString("N") + ".yml");
        if (File.Exists(workflowPath)) {
            File.Delete(workflowPath);
        }

        try {
            _ = ParseWorkflowOnEventTypes(workflowPath);
            throw new InvalidOperationException("Expected parser to fail when workflow file is missing.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "Workflow file not found:", "missing file error prefix");
            AssertContainsText(ex.Message, workflowPath, "missing file error path");
        }
    }

    private static Dictionary<string, List<string>> ParseWorkflowOnEventTypes(string workflowPath) {
        if (string.IsNullOrWhiteSpace(workflowPath)) {
            throw new InvalidOperationException("Workflow path cannot be empty.");
        }
        if (!File.Exists(workflowPath)) {
            throw new InvalidOperationException($"Workflow file not found: {workflowPath}");
        }

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
            var trimmed = StripInlineComment(trimmedStart).TrimEnd();
            if (string.IsNullOrWhiteSpace(trimmed)) {
                continue;
            }

            if (!inOnBlock) {
                if (TryParseYamlKeyValueLine(trimmed, out var rootKey, out var hasRootValue, out _) &&
                    string.Equals(rootKey, "on", StringComparison.OrdinalIgnoreCase) &&
                    !hasRootValue) {
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
                    AddTypeValue(types, trimmed.Substring(2));
                }
                continue;
            }

            if (!TryParseYamlKeyValueLine(trimmed, out var key, out var hasValue, out var value)) {
                continue;
            }

            if (string.Equals(key, "types", StringComparison.OrdinalIgnoreCase) && currentEvent is not null) {
                if (!events.ContainsKey(currentEvent)) {
                    events[currentEvent] = new List<string>();
                }

                if (hasValue) {
                    ParseInlineTypesValue(value, events[currentEvent]);
                    inTypesBlock = false;
                } else {
                    inTypesBlock = true;
                    typesIndent = indent;
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
        if (relativeSegments is null || relativeSegments.Length == 0) {
            throw new InvalidOperationException("Repository file path segments cannot be empty.");
        }

        var rootFromCurrent = TryResolveRepositoryRoot(Environment.CurrentDirectory);
        var rootFromBase = TryResolveRepositoryRoot(AppContext.BaseDirectory);

        if (!string.IsNullOrWhiteSpace(rootFromCurrent) &&
            !string.IsNullOrWhiteSpace(rootFromBase) &&
            !PathsEqual(rootFromCurrent!, rootFromBase!)) {
            throw new InvalidOperationException(
                $"Resolved different repository roots from current and base directories: current='{rootFromCurrent}', base='{rootFromBase}'.");
        }

        var repoRoot = !string.IsNullOrWhiteSpace(rootFromCurrent) ? rootFromCurrent : rootFromBase;
        if (string.IsNullOrWhiteSpace(repoRoot)) {
            throw new InvalidOperationException("Unable to locate repository root from current directory or application base directory.");
        }

        var candidate = repoRoot!;
        for (var i = 0; i < relativeSegments.Length; i++) {
            if (string.IsNullOrWhiteSpace(relativeSegments[i])) {
                throw new InvalidOperationException("Repository file path segment cannot be empty.");
            }
            candidate = Path.Combine(candidate, relativeSegments[i]);
        }

        if (!File.Exists(candidate)) {
            throw new InvalidOperationException($"Repository file not found: {candidate}");
        }

        return candidate;
    }

    private static string? TryResolveRepositoryRoot(string? startPath) {
        if (string.IsNullOrWhiteSpace(startPath)) {
            return null;
        }

        var current = new DirectoryInfo(startPath);
        while (current is not null) {
            var gitDirectory = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory)) {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void ParseInlineTypesValue(string value, List<string> types) {
        var trimmed = NormalizeYamlToken(value);
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
            var inner = trimmed.Substring(1, trimmed.Length - 2);
            var parts = inner.Split(',');
            for (var i = 0; i < parts.Length; i++) {
                AddTypeValue(types, parts[i]);
            }
            return;
        }

        AddTypeValue(types, trimmed);
    }

    private static void AddTypeValue(List<string> types, string rawValue) {
        var normalized = NormalizeYamlToken(rawValue);
        if (!string.IsNullOrWhiteSpace(normalized)) {
            types.Add(normalized);
        }
    }

    private static bool TryParseYamlKeyValueLine(string line, out string key, out bool hasValue, out string value) {
        key = string.Empty;
        value = string.Empty;
        hasValue = false;

        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0) {
            return false;
        }

        key = NormalizeYamlToken(line.Substring(0, separatorIndex));
        if (string.IsNullOrWhiteSpace(key)) {
            return false;
        }

        value = line.Substring(separatorIndex + 1).Trim();
        hasValue = value.Length > 0;
        return true;
    }

    private static string NormalizeYamlToken(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length >= 2) {
            var first = trimmed[0];
            var last = trimmed[trimmed.Length - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) {
                return trimmed.Substring(1, trimmed.Length - 2).Trim();
            }
        }

        return trimmed;
    }

    private static string StripInlineComment(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return string.Empty;
        }

        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < line.Length; i++) {
            var ch = line[i];
            if (ch == '\'' && !inDoubleQuote) {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (ch == '"' && !inSingleQuote) {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (ch == '#' && !inSingleQuote && !inDoubleQuote) {
                if (i == 0 || char.IsWhiteSpace(line[i - 1])) {
                    return line.Substring(0, i).TrimEnd();
                }
            }
        }

        return line;
    }

    private static bool PathsEqual(string left, string right) {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedLeft, normalizedRight, comparison);
    }

    private static void TryDeleteDirectory(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // best effort cleanup for temp test directories
        }
    }
#endif
}
