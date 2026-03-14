using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared helpers for collecting declared representative tool examples and composing generic fallback examples.
/// </summary>
public static class ToolRepresentativeExamples {
    /// <summary>
    /// Generic fallback example for directory-scoped discovery flows.
    /// </summary>
    public const string DirectoryScopeFallbackExample =
        "discover directory scope, search directory objects, and target a specific domain controller or base DN";

    /// <summary>
    /// Generic fallback example for event-evidence flows.
    /// </summary>
    public const string EventEvidenceFallbackExample =
        "inspect event logs and summarize recurring failures on this machine or a reachable host";

    /// <summary>
    /// Generic fallback example for host diagnostics flows.
    /// </summary>
    public const string HostDiagnosticsFallbackExample =
        "collect system inventory plus CPU, memory, and disk health locally or on reachable machines";

    /// <summary>
    /// Generic fallback example for setup-aware flows.
    /// </summary>
    public const string SetupAwareFallbackExample =
        "use built-in setup or preflight helpers before deeper checks when a workflow needs environment context";

    /// <summary>
    /// Generic fallback example for pack-overview flows.
    /// </summary>
    public const string PackInfoFallbackExample =
        "summarize the currently loaded tool areas before choosing the next check";

    /// <summary>
    /// Returns <see langword="true"/> when the provided traits look like a directory-scope discovery flow.
    /// </summary>
    public static bool IsDirectoryScopeFallbackCandidate(
        bool isEnvironmentDiscoverTool,
        string? scope,
        bool supportsTargetScoping,
        IReadOnlyList<string>? targetScopeArguments) {
        return isEnvironmentDiscoverTool
               || (string.Equals((scope ?? string.Empty).Trim(), "domain", StringComparison.OrdinalIgnoreCase)
                   && (supportsTargetScoping
                       || ContainsArgument(targetScopeArguments, "domain_controller")
                       || ContainsArgument(targetScopeArguments, "search_base_dn")));
    }

    /// <summary>
    /// Returns <see langword="true"/> when the provided traits look like an event-evidence workflow.
    /// </summary>
    public static bool IsEventEvidenceFallbackCandidate(
        string? entity,
        bool supportsRemoteHostTargeting,
        bool supportsRemoteExecution,
        string? executionScope) {
        return string.Equals((entity ?? string.Empty).Trim(), "event", StringComparison.OrdinalIgnoreCase)
               && (supportsRemoteHostTargeting || supportsRemoteExecution || ToolExecutionScopes.IsRemoteCapable(executionScope));
    }

    /// <summary>
    /// Returns <see langword="true"/> when the provided traits look like a host-diagnostics workflow.
    /// </summary>
    public static bool IsHostDiagnosticsFallbackCandidate(
        string? scope,
        string? entity,
        bool supportsRemoteHostTargeting,
        bool supportsRemoteExecution,
        string? executionScope) {
        return string.Equals((scope ?? string.Empty).Trim(), "host", StringComparison.OrdinalIgnoreCase)
               && string.Equals((entity ?? string.Empty).Trim(), "host", StringComparison.OrdinalIgnoreCase)
               && (supportsRemoteHostTargeting || supportsRemoteExecution || ToolExecutionScopes.IsRemoteCapable(executionScope));
    }

    /// <summary>
    /// Collects declared representative examples from items while trimming, de-duplicating, and honoring the cap.
    /// </summary>
    public static List<string> CollectDeclaredExamples<T>(
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<string>?> examplesSelector,
        int maxExamples = 4) {
        if (items is null) {
            throw new ArgumentNullException(nameof(items));
        }

        if (examplesSelector is null) {
            throw new ArgumentNullException(nameof(examplesSelector));
        }

        var examples = new List<string>();
        if (maxExamples <= 0 || items.Count == 0) {
            return examples;
        }

        for (var i = 0; i < items.Count && examples.Count < maxExamples; i++) {
            var declaredExamples = examplesSelector(items[i]);
            if (declaredExamples is not { Count: > 0 }) {
                continue;
            }

            for (var j = 0; j < declaredExamples.Count && examples.Count < maxExamples; j++) {
                TryAddExample(examples, declaredExamples[j], maxExamples);
            }
        }

        return examples;
    }

    /// <summary>
    /// Appends ordered fallback examples for the first matching predicates that apply.
    /// </summary>
    public static void AppendFallbackExamples<T>(
        List<string> examples,
        IReadOnlyList<T> items,
        params (Func<T, bool> Predicate, string Example)[] rules) {
        AppendFallbackExamples(examples, items, maxExamples: 4, rules);
    }

    /// <summary>
    /// Appends ordered fallback examples for the first matching predicates that apply.
    /// </summary>
    public static void AppendFallbackExamples<T>(
        List<string> examples,
        IReadOnlyList<T> items,
        int maxExamples,
        params (Func<T, bool> Predicate, string Example)[] rules) {
        if (examples is null) {
            throw new ArgumentNullException(nameof(examples));
        }

        if (items is null) {
            throw new ArgumentNullException(nameof(items));
        }

        if (rules is null) {
            throw new ArgumentNullException(nameof(rules));
        }

        if (maxExamples <= 0 || examples.Count >= maxExamples || items.Count == 0 || rules.Length == 0) {
            return;
        }

        for (var i = 0; i < rules.Length && examples.Count < maxExamples; i++) {
            var rule = rules[i];
            if (rule.Predicate is null || string.IsNullOrWhiteSpace(rule.Example)) {
                continue;
            }

            if (!HasMatchingItem(items, rule.Predicate)) {
                continue;
            }

            TryAddExample(examples, rule.Example, maxExamples);
        }
    }

    /// <summary>
    /// Adds an example when it is non-empty, unique, and the target has not reached the requested cap.
    /// </summary>
    public static bool TryAddExample(List<string> examples, string? example, int maxExamples = 4) {
        if (examples is null) {
            throw new ArgumentNullException(nameof(examples));
        }

        if (maxExamples <= 0 || examples.Count >= maxExamples) {
            return false;
        }

        var normalized = (example ?? string.Empty).Trim();
        if (normalized.Length == 0 || ContainsIgnoreCase(examples, normalized)) {
            return false;
        }

        examples.Add(normalized);
        return true;
    }

    /// <summary>
    /// Collects distinct normalized display names from item target ids and returns them sorted.
    /// </summary>
    public static List<string> CollectTargetDisplayNames<T>(
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<string>?> targetIdsSelector,
        Func<string?, string> normalizeTargetId,
        Func<string, string> resolveDisplayName) {
        if (items is null) {
            throw new ArgumentNullException(nameof(items));
        }

        if (targetIdsSelector is null) {
            throw new ArgumentNullException(nameof(targetIdsSelector));
        }

        if (normalizeTargetId is null) {
            throw new ArgumentNullException(nameof(normalizeTargetId));
        }

        if (resolveDisplayName is null) {
            throw new ArgumentNullException(nameof(resolveDisplayName));
        }

        var names = new List<string>();
        for (var i = 0; i < items.Count; i++) {
            var targetIds = targetIdsSelector(items[i]);
            if (targetIds is not { Count: > 0 }) {
                continue;
            }

            for (var j = 0; j < targetIds.Count; j++) {
                var normalizedTargetId = normalizeTargetId(targetIds[j]);
                if (normalizedTargetId.Length == 0) {
                    continue;
                }

                var displayName = (resolveDisplayName(normalizedTargetId) ?? string.Empty).Trim();
                if (displayName.Length == 0 || ContainsIgnoreCase(names, displayName)) {
                    continue;
                }

                names.Add(displayName);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>
    /// Formats a representative cross-pack follow-up example for capability prompts.
    /// </summary>
    public static string BuildCrossPackPivotExample(IReadOnlyList<string> displayNames) {
        if (displayNames is null) {
            throw new ArgumentNullException(nameof(displayNames));
        }

        return "pivot findings into " + string.Join(", ", displayNames) + " for follow-up checks when the workflow calls for it";
    }

    /// <summary>
    /// Formats a cross-pack availability line for capability guidance.
    /// </summary>
    public static string BuildCrossPackAvailabilityLine(IReadOnlyList<string> displayNames, string availabilityQualifier) {
        if (displayNames is null) {
            throw new ArgumentNullException(nameof(displayNames));
        }

        var normalizedQualifier = (availabilityQualifier ?? string.Empty).Trim();
        if (normalizedQualifier.Length == 0) {
            throw new ArgumentException("Availability qualifier is required.", nameof(availabilityQualifier));
        }

        return "Cross-pack follow-up pivots are " + normalizedQualifier + " into " + string.Join(", ", displayNames) + " when the workflow calls for it.";
    }

    /// <summary>
    /// Formats a compact cross-pack summary for planner hints.
    /// </summary>
    public static string BuildCrossPackSummary(IReadOnlyList<string> displayNames) {
        if (displayNames is null) {
            throw new ArgumentNullException(nameof(displayNames));
        }

        return "Cross-pack follow-up pivots: " + string.Join(", ", displayNames);
    }

    private static bool HasMatchingItem<T>(IReadOnlyList<T> items, Func<T, bool> predicate) {
        for (var i = 0; i < items.Count; i++) {
            if (predicate(items[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIgnoreCase(IReadOnlyList<string> values, string expected) {
        for (var i = 0; i < values.Count; i++) {
            if (string.Equals(values[i], expected, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsArgument(IReadOnlyList<string>? values, string expected) {
        if (values is not { Count: > 0 } || string.IsNullOrWhiteSpace(expected)) {
            return false;
        }

        for (var i = 0; i < values.Count; i++) {
            if (string.Equals((values[i] ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
