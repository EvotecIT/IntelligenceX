using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private static IReadOnlyList<string> BuildToolContractPromptHintLines(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns,
        bool includeRemoteHostFallbackHint) {
        if (toolDefinitions is null || toolDefinitions.Count == 0) {
            return Array.Empty<string>();
        }

        var orchestrationCatalog = ToolOrchestrationCatalog.Build(toolDefinitions);
        var matchedEntries = new List<ToolOrchestrationCatalogEntry>(toolDefinitions.Count);
        var seenToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var toolName = (toolDefinitions[i].Name ?? string.Empty).Trim();
            if (toolName.Length == 0 || !orchestrationCatalog.TryGetEntry(toolName, out var entry)) {
                continue;
            }

            if (!seenToolNames.Add(entry.ToolName)) {
                continue;
            }

            if (entry.IsPackInfoTool) {
                continue;
            }

            if (!PatternMatchesAnyToolName(toolPatterns, entry.ToolName)) {
                continue;
            }

            matchedEntries.Add(entry);
        }

        if (matchedEntries.Count == 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(4);
        var representativeExamples = ToolContractPromptExamples.BuildRepresentativeExamples(matchedEntries);
        if (representativeExamples.Count > 0) {
            lines.Add("- Representative live tool examples for this flow: " + string.Join("; ", representativeExamples) + ".");
        }
        var setupExamples = matchedEntries
            .Where(static entry => entry.SetupToolName.Length > 0
                                   && entry.SetupToolName.IndexOf("_catalog", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(static entry => entry.SetupToolName + " -> " + entry.ToolName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (setupExamples.Length > 0) {
            lines.Add("- For tools with declared setup helpers, use them to discover valid names/values when uncertain (for example "
                      + string.Join("; ", setupExamples)
                      + ").");
        }

        var crossPackTargets = ToolContractPromptExamples.BuildCrossPackTargetPackDisplayNames(matchedEntries);
        if (crossPackTargets.Count > 0) {
            lines.Add("- Cross-pack follow-up pivots are available into " + string.Join(", ", crossPackTargets) + " when the workflow calls for it.");
        }

        if (matchedEntries.Any(static entry => entry.SupportsRemoteHostTargeting && entry.SupportsRemoteExecution)) {
            lines.Add(includeRemoteHostFallbackHint
                ? "- If a remote-capable tool is missing host or machine input, default to the first discovered/source host/DC from prior turns when thread context provides one."
                : "- If a remote-capable tool is missing host or machine input, infer it from prior thread context when available.");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildToolExecutionAvailabilityHintLines(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns,
        IReadOnlyList<string>? knownHostTargets = null) {
        return ToolExecutionAvailabilityHints.BuildPromptHintLines(
            toolDefinitions,
            toolPatterns,
            hasKnownHostTargets: knownHostTargets is { Count: > 0 });
    }

    private static string BuildToolExecutionAvailabilityWarningText(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns,
        IReadOnlyList<string>? knownHostTargets) {
        return ToolExecutionAvailabilityHints.BuildWarningText(
            toolDefinitions,
            toolPatterns,
            hasKnownHostTargets: knownHostTargets is { Count: > 0 });
    }

    private static bool PatternMatchesAnyToolName(IReadOnlyList<string>? toolPatterns, string toolName) {
        return ToolPatternsMatch(toolName, toolPatterns);
    }

    private static bool ToolPatternsMatch(string toolName, IReadOnlyList<string>? toolPatterns) {
        if (toolPatterns is null || toolPatterns.Count == 0) {
            return true;
        }

        for (var i = 0; i < toolPatterns.Count; i++) {
            var pattern = (toolPatterns[i] ?? string.Empty).Trim();
            if (pattern.Length == 0) {
                continue;
            }

            if (string.Equals(pattern, "*", StringComparison.Ordinal)) {
                return true;
            }

            var hasWildcard = pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;
            if (!hasWildcard && string.Equals(pattern, toolName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            if (!hasWildcard) {
                continue;
            }

            if (WildcardMatchesOrdinalIgnoreCase(pattern, toolName)) {
                return true;
            }
        }

        return false;
    }

    private static bool WildcardMatchesOrdinalIgnoreCase(string pattern, string candidate) {
        var patternIndex = 0;
        var candidateIndex = 0;
        var starIndex = -1;
        var candidateCheckpoint = 0;

        while (candidateIndex < candidate.Length) {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?'
                    || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(candidate[candidateIndex]))) {
                patternIndex++;
                candidateIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*') {
                starIndex = patternIndex++;
                candidateCheckpoint = candidateIndex;
                continue;
            }

            if (starIndex >= 0) {
                patternIndex = starIndex + 1;
                candidateIndex = ++candidateCheckpoint;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
