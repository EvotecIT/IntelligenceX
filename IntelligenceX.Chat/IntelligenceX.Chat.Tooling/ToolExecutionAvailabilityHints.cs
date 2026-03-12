using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared execution-locality summaries and guidance derived from registered tool contracts.
/// </summary>
public sealed record ToolExecutionAvailabilitySummary {
    /// <summary>
    /// Total number of matched tools represented in this summary.
    /// </summary>
    public int ToolCount { get; init; }
    /// <summary>
    /// Number of tools whose execution scope is local-only.
    /// </summary>
    public int LocalOnlyTools { get; init; }
    /// <summary>
    /// Number of tools whose execution scope is remote-only.
    /// </summary>
    public int RemoteOnlyTools { get; init; }
    /// <summary>
    /// Number of tools whose execution scope supports both local and remote execution.
    /// </summary>
    public int LocalOrRemoteTools { get; init; }

    /// <summary>
    /// Indicates whether any matched tool can execute remotely.
    /// </summary>
    public bool HasRemoteReadyTools => RemoteOnlyTools > 0 || LocalOrRemoteTools > 0;
    /// <summary>
    /// Indicates whether both local-only and remote-ready tools are present.
    /// </summary>
    public bool HasMixedLocality => HasRemoteReadyTools && LocalOnlyTools > 0;
    /// <summary>
    /// Indicates whether every matched tool is local-only.
    /// </summary>
    public bool IsLocalOnly => ToolCount > 0 && LocalOnlyTools == ToolCount;
    /// <summary>
    /// Indicates whether every matched tool is remote-ready and none are local-only.
    /// </summary>
    public bool IsRemoteReadyOnly => ToolCount > 0 && LocalOnlyTools == 0 && HasRemoteReadyTools;
}

/// <summary>
/// Produces execution-locality guidance for recovery prompts and diagnostics.
/// </summary>
public static class ToolExecutionAvailabilityHints {
    /// <summary>
    /// Builds an execution-locality summary for the provided tool set and optional tool-name patterns.
    /// </summary>
    public static ToolExecutionAvailabilitySummary BuildSummary(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns = null) {
        var matchedEntries = CollectMatchedToolOrchestrationEntries(toolDefinitions, toolPatterns);
        if (matchedEntries.Count == 0) {
            return new ToolExecutionAvailabilitySummary();
        }

        var localOnlyTools = 0;
        var remoteOnlyTools = 0;
        var localOrRemoteTools = 0;
        for (var i = 0; i < matchedEntries.Count; i++) {
            var entry = matchedEntries[i];
            if (entry.SupportsRemoteExecution && !entry.SupportsLocalExecution) {
                remoteOnlyTools++;
                continue;
            }

            if (entry.SupportsRemoteExecution) {
                localOrRemoteTools++;
                continue;
            }

            localOnlyTools++;
        }

        return new ToolExecutionAvailabilitySummary {
            ToolCount = matchedEntries.Count,
            LocalOnlyTools = localOnlyTools,
            RemoteOnlyTools = remoteOnlyTools,
            LocalOrRemoteTools = localOrRemoteTools
        };
    }

    /// <summary>
    /// Builds prompt-friendly guidance lines that describe whether the current tool set is local-only,
    /// remote-ready, or mixed.
    /// </summary>
    public static IReadOnlyList<string> BuildPromptHintLines(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns = null,
        bool hasKnownHostTargets = false) {
        var summary = BuildSummary(toolDefinitions, toolPatterns);
        if (summary.ToolCount == 0) {
            return Array.Empty<string>();
        }

        if (summary.IsLocalOnly) {
            return new[] {
                hasKnownHostTargets
                    ? "- Current runtime tools for this request are local-only. Do not imply remote host/DC collection; known prior hosts/DCs may exist in thread context, but the available tools here are local-only and need a remote-capable path or one minimal missing input."
                    : "- Current runtime tools for this request are local-only. Do not imply remote host/DC collection; if remote evidence is required, say the available tools here are local-only and ask only for the minimal remote-capable path or missing input."
            };
        }

        if (summary.HasMixedLocality) {
            return new[] {
                "- This runtime has both local-only and remote-ready tools. Prefer remote-ready tools for host/DC-targeted work and keep local-only tools scoped to the current machine/session."
            };
        }

        if (summary.IsRemoteReadyOnly) {
            return new[] {
                "- Remote-ready tools are available in this runtime. Prefer them for host/DC-targeted requests before concluding the task is blocked."
            };
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Builds registration-friendly guidance lines that describe what kind of tool contracts are currently available.
    /// </summary>
    public static IReadOnlyList<string> BuildRegistrationHintLines(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns = null,
        bool hasKnownHostTargets = false) {
        var summary = BuildSummary(toolDefinitions, toolPatterns);
        if (summary.ToolCount == 0) {
            return Array.Empty<string>();
        }

        if (summary.IsLocalOnly) {
            return new[] {
                hasKnownHostTargets
                    ? "Registered tool contracts in this session are currently local-only. Known prior hosts/DCs may exist in thread context, but remote host/DC analysis still needs a registered remote-capable tool path."
                    : "Registered tool contracts in this session are currently local-only. If the request needs remote host/DC analysis, enable or register a remote-capable tool path instead of assuming remote execution."
            };
        }

        if (summary.HasMixedLocality) {
            return new[] {
                "This session has both local-only and remote-ready tool contracts. Prefer one of the registered remote-ready tools for host/DC-targeted work instead of inventing a new tool name."
            };
        }

        if (summary.IsRemoteReadyOnly) {
            return new[] {
                "Remote-ready tool contracts are already registered in this session. Retry with one of the registered host/DC-capable tools instead of inventing a new tool name."
            };
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Builds a compact warning sentence describing the current execution-locality surface.
    /// </summary>
    public static string BuildWarningText(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns = null,
        bool hasKnownHostTargets = false) {
        var summary = BuildSummary(toolDefinitions, toolPatterns);
        if (summary.ToolCount == 0) {
            return string.Empty;
        }

        if (summary.IsLocalOnly) {
            var hostScopeSuffix = hasKnownHostTargets
                ? " Known prior hosts/DCs exist in thread context, but the currently available tool contracts here are still local-only."
                : string.Empty;
            return "Tool locality: current enabled tools are local-only in this session, so direct remote host/DC analysis is not currently available from the registered contracts."
                   + hostScopeSuffix;
        }

        if (summary.HasMixedLocality) {
            return "Tool locality: this session has both local-only and remote-ready tools. Prefer remote-ready tools for host/DC-targeted analysis and keep local-only tools scoped to the current machine/session.";
        }

        if (summary.IsRemoteReadyOnly) {
            return "Tool locality: remote-ready tools are available in this session, so retrying can use registered host/DC-capable tools instead of stopping at narration.";
        }

        return string.Empty;
    }

    private static IReadOnlyList<ToolOrchestrationCatalogEntry> CollectMatchedToolOrchestrationEntries(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IReadOnlyList<string>? toolPatterns) {
        if (toolDefinitions is null || toolDefinitions.Count == 0) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
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

            if (string.Equals(entry.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!ToolPatternsMatch(entry.ToolName, toolPatterns)) {
                continue;
            }

            matchedEntries.Add(entry);
        }

        return matchedEntries;
    }

    private static bool ToolPatternsMatch(string toolName, IReadOnlyList<string>? toolPatterns) {
        if (toolPatterns is null || toolPatterns.Count == 0) {
            return true;
        }

        for (var i = 0; i < toolPatterns.Count; i++) {
            if (PatternMatchesToolName(toolPatterns[i], toolName)) {
                return true;
            }
        }

        return false;
    }

    private static bool PatternMatchesToolName(string pattern, string toolName) {
        var expected = (pattern ?? string.Empty).Trim();
        var actual = (toolName ?? string.Empty).Trim();
        if (expected.Length == 0 || actual.Length == 0) {
            return false;
        }

        if (string.Equals(expected, "*", StringComparison.Ordinal)) {
            return true;
        }

        var hasWildcard = expected.IndexOf('*') >= 0 || expected.IndexOf('?') >= 0;
        if (!hasWildcard) {
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        return WildcardMatchesOrdinalIgnoreCase(expected, actual);
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
