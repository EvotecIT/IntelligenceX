using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var toolName = (toolDefinitions[i].Name ?? string.Empty).Trim();
            if (toolName.Length == 0 || !orchestrationCatalog.TryGetEntry(toolName, out var entry)) {
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

        if (matchedEntries.Count == 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(2);
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

        if (includeRemoteHostFallbackHint
            && matchedEntries.Any(static entry => entry.SupportsRemoteHostTargeting)) {
            lines.Add("- If a remote-capable tool is missing host or machine input, default to the first discovered/source host/DC from prior turns when thread context provides one.");
        }

        return lines;
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

        var regexPattern = "^"
                           + Regex.Escape(expected)
                               .Replace("\\*", ".*", StringComparison.Ordinal)
                               .Replace("\\?", ".", StringComparison.Ordinal)
                           + "$";
        return Regex.IsMatch(actual, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
