using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared aggregation helpers for unavailable-pack startup warnings.
/// </summary>
public static class StartupUnavailablePackWarningFormatter {
    /// <summary>
    /// Builds canonical unavailable-pack warning entries from pack metadata.
    /// </summary>
    public static StartupUnavailablePackWarningEntry[] BuildEntries<TPack>(
        IEnumerable<TPack>? packs,
        Func<TPack, string?> idSelector,
        Func<TPack, string?> nameSelector,
        Func<TPack, bool> enabledSelector,
        Func<TPack, string?> disabledReasonSelector) {
        ArgumentNullException.ThrowIfNull(idSelector);
        ArgumentNullException.ThrowIfNull(nameSelector);
        ArgumentNullException.ThrowIfNull(enabledSelector);
        ArgumentNullException.ThrowIfNull(disabledReasonSelector);

        if (packs is null) {
            return Array.Empty<StartupUnavailablePackWarningEntry>();
        }

        var entries = new List<StartupUnavailablePackWarningEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs) {
            if (enabledSelector(pack)) {
                continue;
            }

            var rawId = idSelector(pack);
            var rawName = nameSelector(pack);
            var reason = (disabledReasonSelector(pack) ?? string.Empty).Trim();
            if (reason.Length == 0) {
                continue;
            }

            var normalizedId = StartupToolHealthWarningFormatter.NormalizePackId(rawId);
            if (normalizedId.Length == 0) {
                normalizedId = (rawName ?? string.Empty).Trim();
            }

            if (normalizedId.Length == 0) {
                continue;
            }

            var label = StartupToolHealthWarningFormatter.ResolvePackDisplayLabel(rawId, rawName);
            var signature = normalizedId + "|" + reason;
            if (!seen.Add(signature)) {
                continue;
            }

            entries.Add(new StartupUnavailablePackWarningEntry(normalizedId, label, reason));
        }

        return entries.ToArray();
    }
}

/// <summary>
/// Canonical unavailable-pack warning entry for operator-facing startup surfaces.
/// </summary>
public readonly record struct StartupUnavailablePackWarningEntry(string Id, string Label, string Reason);
