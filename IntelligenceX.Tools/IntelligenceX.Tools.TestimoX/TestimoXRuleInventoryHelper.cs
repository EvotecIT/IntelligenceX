using System;
using System.Collections.Generic;
using System.Linq;
using TestimoX.Execution;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXRuleInventoryHelper {
    internal static readonly string[] MigrationStateNames = {
        "authoritative_csharp",
        "demo_legacy_powershell",
        "superseded_or_hidden"
    };

    internal static bool TryParseMigrationStates(
        IReadOnlyList<string> requestedStates,
        out HashSet<string>? parsedStates,
        out string? error) {
        parsedStates = null;
        error = null;

        if (requestedStates.Count == 0) {
            return true;
        }

        var parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in requestedStates) {
            var normalized = (state ?? string.Empty).Trim();
            if (!MigrationStateNames.Contains(normalized, StringComparer.OrdinalIgnoreCase)) {
                error = $"migration_states contains unsupported value '{state}'. Supported values: {string.Join(", ", MigrationStateNames)}.";
                return false;
            }

            parsed.Add(normalized);
        }

        parsedStates = parsed;
        return true;
    }

    internal static string ToMigrationStateName(RuleMigrationState state) {
        return state switch {
            RuleMigrationState.AuthoritativeCSharp => "authoritative_csharp",
            RuleMigrationState.DemoLegacyPowerShell => "demo_legacy_powershell",
            _ => "superseded_or_hidden"
        };
    }
}
