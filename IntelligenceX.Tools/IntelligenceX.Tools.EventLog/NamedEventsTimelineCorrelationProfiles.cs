using System;
using System.Collections.Generic;
using System.Linq;
using EventViewerX.Reports.Correlation;

namespace IntelligenceX.Tools.EventLog;

internal static class NamedEventsTimelineCorrelationProfiles {
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Profiles =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["identity"] = new[] { "who", "object_affected", "computer" },
            ["actor_activity"] = new[] { "who", "action", "computer" },
            ["object_activity"] = new[] { "object_affected", "action", "computer" },
            ["host_activity"] = new[] { "computer", "action", "named_event" },
            ["rule_activity"] = new[] { "named_event", "computer", "who" }
        };

    internal static IReadOnlyList<string> Names { get; } = Profiles.Keys
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static bool TryResolve(
        string? rawProfile,
        out string? normalizedProfile,
        out IReadOnlyList<string>? correlationKeys,
        out string? error) {
        normalizedProfile = null;
        correlationKeys = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawProfile)) {
            return true;
        }

        var normalized = EventLogNamedEventsQueryShared.ToSnakeCase(rawProfile);

        if (!Profiles.TryGetValue(normalized, out var keys)) {
            error = $"correlation_profile ('{rawProfile}') is not recognized. Allowed values: {string.Join(", ", Names)}.";
            return false;
        }

        var allowed = NamedEventsTimelineQueryExecutor.AllowedCorrelationKeys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedKeys = keys
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(EventLogNamedEventsQueryShared.ToSnakeCase)
            .Where(allowed.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedKeys.Length == 0) {
            correlationKeys = NamedEventsTimelineQueryExecutor.DefaultCorrelationKeys.ToArray();
        } else {
            correlationKeys = normalizedKeys;
        }

        normalizedProfile = normalized;
        return true;
    }
}
