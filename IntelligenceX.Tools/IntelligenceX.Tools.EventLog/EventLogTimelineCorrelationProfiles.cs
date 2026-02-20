using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogTimelineCorrelationProfiles {
    private sealed record CorrelationProfile(string Name, IReadOnlyList<string> Keys);

    private static readonly CorrelationProfile[] ProfilesValue = {
        new("identity", new[] { "who", "object_affected", "computer" }),
        new("actor_activity", new[] { "who", "action", "computer" }),
        new("object_activity", new[] { "object_affected", "action", "who" }),
        new("host_activity", new[] { "computer", "action", "who" }),
        new("rule_activity", new[] { "named_event", "who", "object_affected" })
    };

    private static readonly IReadOnlyDictionary<string, CorrelationProfile> ProfilesByName =
        ProfilesValue.ToDictionary(static profile => profile.Name, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> Names { get; } = ProfilesValue
        .Select(static profile => profile.Name)
        .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static bool TryResolve(
        string? rawProfile,
        out string? normalizedProfile,
        out IReadOnlyList<string>? keys,
        out string? error) {
        normalizedProfile = null;
        keys = null;
        error = null;

        var profile = EventLogNamedEventsQueryShared.ToSnakeCase(rawProfile ?? string.Empty);
        if (string.IsNullOrWhiteSpace(profile)) {
            return true;
        }

        if (!ProfilesByName.TryGetValue(profile, out var resolved)) {
            error = $"correlation_profile ('{rawProfile}') is not recognized. Allowed values: {string.Join(", ", Names)}.";
            return false;
        }

        normalizedProfile = resolved.Name;
        keys = resolved.Keys;
        return true;
    }
}
