using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared alternate-engine selector vocabulary used by tool contracts and chat retry fallbacks.
/// </summary>
public static class ToolAlternateEngineSelectorNames {
    /// <summary>
    /// Canonical schema argument names that allow tools to select an alternate execution engine.
    /// </summary>
    public static IReadOnlyList<string> CanonicalSelectorArguments { get; } = new[] {
        "engine",
        "engine_id",
        "backend",
        "backend_id"
    };

    /// <summary>
    /// Resolves the first canonical alternate-engine selector name present in a JSON schema/property object.
    /// </summary>
    public static bool TryResolveSelectorArgumentName(JsonObject? arguments, out string argumentName) {
        argumentName = string.Empty;
        if (arguments is null || arguments.Count == 0) {
            return false;
        }

        var availableNames = new List<string>(arguments.Count);
        foreach (var pair in arguments) {
            availableNames.Add(pair.Key);
        }

        return TryResolveSelectorArgumentName(availableNames, out argumentName);
    }

    /// <summary>
    /// Resolves the first canonical alternate-engine selector name present in a tool schema or argument list.
    /// </summary>
    public static bool TryResolveSelectorArgumentName(IEnumerable<string>? argumentNames, out string argumentName) {
        argumentName = string.Empty;
        if (argumentNames is null) {
            return false;
        }

        var availableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in argumentNames) {
            var normalized = (candidate ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                continue;
            }

            availableNames.Add(normalized);
        }

        if (availableNames.Count == 0) {
            return false;
        }

        for (var i = 0; i < CanonicalSelectorArguments.Count; i++) {
            var canonical = CanonicalSelectorArguments[i];
            if (!availableNames.Contains(canonical)) {
                continue;
            }

            argumentName = canonical;
            return true;
        }

        return false;
    }
}
