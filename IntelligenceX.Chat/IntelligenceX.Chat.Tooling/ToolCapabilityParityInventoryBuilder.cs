using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Builds a runtime parity inventory from live tool registration and pack-owned engine contracts.
/// </summary>
public static class ToolCapabilityParityInventoryBuilder {
    /// <summary>
    /// Status used when the current runtime surface matches the phase-1 parity slice.
    /// </summary>
    public const string HealthyStatus = ToolCapabilityParityStatuses.Healthy;

    /// <summary>
    /// Status used when upstream read-only capabilities are still missing from the live IX surface.
    /// </summary>
    public const string GapStatus = ToolCapabilityParityStatuses.Gap;

    /// <summary>
    /// Status used when a capability family is intentionally kept outside autonomous phase-1 execution.
    /// </summary>
    public const string GovernedBacklogStatus = ToolCapabilityParityStatuses.GovernedBacklog;

    /// <summary>
    /// Status used when upstream source metadata was not available for inspection in this runtime.
    /// </summary>
    public const string SourceUnavailableStatus = ToolCapabilityParityStatuses.SourceUnavailable;

    /// <summary>
    /// Status used when the associated pack is not currently surfaced in the active runtime.
    /// </summary>
    public const string PackUnavailableStatus = ToolCapabilityParityStatuses.PackUnavailable;

    private const int MaxMissingCapabilities = 12;

    /// <summary>
    /// Builds the phase-1 parity inventory. Returns an empty array when no live tool definitions are available.
    /// </summary>
    public static SessionCapabilityParityEntryDto[] Build(
        IReadOnlyList<ToolDefinition>? definitions,
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability = null) {
        if (definitions is not { Count: > 0 }) {
            return Array.Empty<SessionCapabilityParityEntryDto>();
        }

        var definitionsByPackId = BuildDefinitionsByPackId(definitions);
        var availability = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()).ToArray();
        var entries = new List<SessionCapabilityParityEntryDto>();
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var packIndex = 0; packIndex < availability.Length; packIndex++) {
            var pack = availability[packIndex];
            if (pack is null || pack.CapabilityParity is not { Count: > 0 }) {
                continue;
            }

            var defaultPackId = ToolPackBootstrap.NormalizePackId(pack.Id);
            for (var sliceIndex = 0; sliceIndex < pack.CapabilityParity.Count; sliceIndex++) {
                var slice = pack.CapabilityParity[sliceIndex];
                if (slice is null) {
                    continue;
                }

                var slicePackId = ToolPackBootstrap.NormalizePackId(slice.PackId ?? defaultPackId);
                var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, slicePackId);
                if (registeredToolCount == 0 && !pack.Enabled) {
                    continue;
                }

                var evaluation = slice.Evaluate(definitions);
                if (evaluation is null) {
                    continue;
                }

                var engineId = (slice.EngineId ?? string.Empty).Trim();
                if (engineId.Length == 0) {
                    continue;
                }

                var entryKey = engineId + "::" + slicePackId;
                if (!seenEntries.Add(entryKey)) {
                    continue;
                }

                entries.Add(CreateEntry(engineId, slicePackId, registeredToolCount, evaluation));
            }
        }

        return entries
            .OrderBy(static entry => entry.EngineId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Formats a one-line parity summary for host diagnostics.
    /// </summary>
    public static string FormatSummary(IReadOnlyList<SessionCapabilityParityEntryDto>? entries) {
        if (entries is not { Count: > 0 }) {
            return "engines=0, healthy=0, gaps=0, governed_backlog=0, missing_readonly=0";
        }

        var healthy = 0;
        var gaps = 0;
        var governedBacklog = 0;
        var missingReadonly = 0;
        for (var i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            if (entry is null) {
                continue;
            }

            missingReadonly += Math.Max(0, entry.MissingCapabilityCount);
            if (string.Equals(entry.Status, HealthyStatus, StringComparison.OrdinalIgnoreCase)) {
                healthy++;
            } else if (string.Equals(entry.Status, GapStatus, StringComparison.OrdinalIgnoreCase)) {
                gaps++;
            } else if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                governedBacklog++;
            }
        }

        return
            $"engines={entries.Count}, " +
            $"healthy={healthy}, " +
            $"gaps={gaps}, " +
            $"governed_backlog={governedBacklog}, " +
            $"missing_readonly={missingReadonly}";
    }

    /// <summary>
    /// Builds compact attention summaries for non-healthy parity entries.
    /// </summary>
    public static IReadOnlyList<string> BuildAttentionSummaries(IReadOnlyList<SessionCapabilityParityEntryDto>? entries, int maxItems = 6) {
        if (entries is not { Count: > 0 } || maxItems <= 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(Math.Min(maxItems, entries.Count));
        for (var i = 0; i < entries.Count && lines.Count < maxItems; i++) {
            var entry = entries[i];
            if (entry is null
                || string.Equals(entry.Status, HealthyStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, PackUnavailableStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{entry.EngineId}: governed backlog ({entry.Note ?? "intentionally not autonomous in phase 1."})");
                continue;
            }

            var suffix = entry.MissingCapabilityCount > 0
                ? $"missing {entry.MissingCapabilityCount} ({string.Join(", ", entry.MissingCapabilities)})"
                : entry.Note ?? entry.Status;
            lines.Add($"{entry.EngineId}: {suffix}");
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines.ToArray();
    }

    /// <summary>
    /// Builds operator-facing per-engine parity detail summaries.
    /// </summary>
    public static IReadOnlyList<string> BuildDetailSummaries(IReadOnlyList<SessionCapabilityParityEntryDto>? entries, int maxItems = 8) {
        if (entries is not { Count: > 0 } || maxItems <= 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(Math.Min(maxItems, entries.Count));
        for (var i = 0; i < entries.Count && lines.Count < maxItems; i++) {
            var entry = entries[i];
            if (entry is null) {
                continue;
            }

            var prefix = $"{entry.EngineId} [{entry.Status}]";
            if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: registered_tools={entry.RegisteredToolCount}; {entry.Note ?? "governed backlog."}");
                continue;
            }

            if (string.Equals(entry.Status, SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: source metadata unavailable; registered_tools={entry.RegisteredToolCount}.");
                continue;
            }

            if (string.Equals(entry.Status, PackUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: pack unavailable.");
                continue;
            }

            var detail = $"{prefix}: surfaced={entry.SurfacedCapabilityCount}/{entry.ExpectedCapabilityCount}, registered_tools={entry.RegisteredToolCount}";
            if (entry.MissingCapabilityCount > 0) {
                detail += $", missing={entry.MissingCapabilityCount}";
                if (entry.MissingCapabilities.Length > 0) {
                    detail += $" ({FormatCapabilityList(entry.MissingCapabilities, entry.MissingCapabilityCount)})";
                }
            }

            lines.Add(detail);
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines.ToArray();
    }

    private static Dictionary<string, List<ToolDefinition>> BuildDefinitionsByPackId(IReadOnlyList<ToolDefinition> definitions) {
        var result = new Dictionary<string, List<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || !ToolHealthDiagnostics.TryResolvePackId(definition, out var packId) || packId.Length == 0) {
                continue;
            }

            if (!result.TryGetValue(packId, out var list)) {
                list = new List<ToolDefinition>();
                result[packId] = list;
            }

            list.Add(definition);
        }

        return result;
    }

    private static int GetRegisteredToolCount(Dictionary<string, List<ToolDefinition>> definitionsByPackId, string packId) {
        return definitionsByPackId.TryGetValue(packId, out var definitions)
            ? definitions.Count
            : 0;
    }

    private static SessionCapabilityParityEntryDto CreateEntry(
        string engineId,
        string packId,
        int registeredToolCount,
        ToolCapabilityParitySliceEvaluation evaluation) {
        var expected = NormalizeDistinctValues(evaluation.ExpectedCapabilities, maxItems: 0);
        var surfaced = NormalizeDistinctValues(evaluation.SurfacedCapabilities, maxItems: 0);
        var missing = expected
            .Except(surfaced, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SessionCapabilityParityEntryDto {
            EngineId = engineId,
            PackId = packId,
            Status = NormalizeStatus(evaluation.Status, missing.Length),
            SourceAvailable = evaluation.SourceAvailable,
            RegisteredToolCount = Math.Max(0, registeredToolCount),
            ExpectedCapabilityCount = expected.Length,
            SurfacedCapabilityCount = surfaced.Length,
            MissingCapabilityCount = missing.Length,
            MissingCapabilities = NormalizeDistinctValues(missing, MaxMissingCapabilities),
            Note = evaluation.Note
        };
    }

    private static string NormalizeStatus(string? status, int missingCount) {
        var normalized = (status ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return missingCount == 0 ? HealthyStatus : GapStatus;
        }

        if (string.Equals(normalized, HealthyStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, GapStatus, StringComparison.OrdinalIgnoreCase)) {
            return missingCount == 0 ? HealthyStatus : GapStatus;
        }

        return normalized;
    }

    private static string FormatCapabilityList(IReadOnlyList<string> capabilities, int totalCount) {
        var shown = capabilities?.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? Array.Empty<string>();
        if (shown.Length == 0) {
            return string.Empty;
        }

        var suffix = totalCount > shown.Length ? $", +{totalCount - shown.Length} more" : string.Empty;
        return string.Join(", ", shown) + suffix;
    }

    private static string[] NormalizeDistinctValues(IEnumerable<string> values, int maxItems) {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? Array.Empty<string>()) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            result.Add(normalized);
            if (maxItems > 0 && result.Count >= maxItems) {
                break;
            }
        }

        return result.Count == 0 ? Array.Empty<string>() : result.ToArray();
    }
}
