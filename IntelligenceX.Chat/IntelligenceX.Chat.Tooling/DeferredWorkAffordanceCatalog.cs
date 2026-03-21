using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Builds runtime deferred-work capability affordances from enabled pack metadata and scheduler support.
/// </summary>
public static class DeferredWorkAffordanceCatalog {
    /// <summary>
    /// Stable capability identifier used for runtime-managed background follow-up work.
    /// </summary>
    public const string BackgroundFollowUpCapabilityId = "background_followup";
    private const string AvailabilityModePackDeclared = "pack_declared";
    private const string AvailabilityModeRuntimeScheduler = "runtime_scheduler";

    private sealed class AffordanceAggregate {
        public required string CapabilityId { get; init; }
        public HashSet<string> PackIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RoutingFamilies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RepresentativeExamples { get; } = new();
        public string AvailabilityMode { get; set; } = AvailabilityModePackDeclared;
        public bool SupportsBackgroundExecution { get; set; }
    }

    /// <summary>
    /// Builds deferred-work affordances from enabled pack metadata and optional runtime scheduler support.
    /// </summary>
    public static SessionCapabilityDeferredWorkAffordanceDto[] Build(
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog,
        SessionCapabilityBackgroundSchedulerDto? backgroundScheduler,
        int maxItems) {
        if (maxItems <= 0) {
            return Array.Empty<SessionCapabilityDeferredWorkAffordanceDto>();
        }

        var enabledPacks = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack.Enabled)
            .ToArray();
        var packAvailabilityById = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => !string.IsNullOrWhiteSpace(pack.Id))
            .GroupBy(static pack => ToolPackBootstrap.NormalizePackId(pack.Id), StringComparer.OrdinalIgnoreCase)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var aggregates = new Dictionary<string, AffordanceAggregate>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < enabledPacks.Length; i++) {
            var pack = enabledPacks[i];
            var normalizedPackId = ToolPackBootstrap.NormalizePackId(pack.Id);
            if (normalizedPackId.Length == 0 || pack.CapabilityTags is not { Count: > 0 }) {
                continue;
            }

            for (var tagIndex = 0; tagIndex < pack.CapabilityTags.Count; tagIndex++) {
                if (!ToolPackCapabilityTags.TryGetDeferredCapabilityId(pack.CapabilityTags[tagIndex], out var capabilityId)) {
                    continue;
                }

                var aggregate = GetOrCreateAggregate(aggregates, capabilityId, AvailabilityModePackDeclared);
                aggregate.PackIds.Add(normalizedPackId);
            }
        }

        if (orchestrationCatalog is not null) {
            var knownPackIds = orchestrationCatalog.GetKnownPackIds();
            for (var i = 0; i < knownPackIds.Count; i++) {
                var normalizedPackId = ToolPackBootstrap.NormalizePackId(knownPackIds[i]);
                if (normalizedPackId.Length == 0
                    || !IsPackEnabledOrUnknown(normalizedPackId, packAvailabilityById)
                    || !orchestrationCatalog.TryGetPackCapabilityTags(normalizedPackId, out var capabilityTags)
                    || capabilityTags.Count == 0) {
                    continue;
                }

                for (var tagIndex = 0; tagIndex < capabilityTags.Count; tagIndex++) {
                    if (!ToolPackCapabilityTags.TryGetDeferredCapabilityId(capabilityTags[tagIndex], out var capabilityId)) {
                        continue;
                    }

                    var aggregate = GetOrCreateAggregate(aggregates, capabilityId, AvailabilityModePackDeclared);
                    aggregate.PackIds.Add(normalizedPackId);
                }
            }
        }

        if (backgroundScheduler is not null
            && (enabledPacks.Length > 0 || aggregates.Count > 0 || backgroundScheduler.TrackedThreadCount > 0)
            && (backgroundScheduler.SupportsPersistentQueue
                || backgroundScheduler.SupportsReadOnlyAutoReplay
                || backgroundScheduler.SupportsCrossThreadScheduling
                || backgroundScheduler.TrackedThreadCount > 0
                || backgroundScheduler.DaemonEnabled)) {
            var aggregate = GetOrCreateAggregate(aggregates, BackgroundFollowUpCapabilityId, AvailabilityModeRuntimeScheduler);
            aggregate.SupportsBackgroundExecution = backgroundScheduler.SupportsReadOnlyAutoReplay
                                                   || backgroundScheduler.SupportsCrossThreadScheduling
                                                   || backgroundScheduler.DaemonEnabled
                                                   || backgroundScheduler.TrackedThreadCount > 0;
            aggregate.AvailabilityMode = AvailabilityModeRuntimeScheduler;
        }

        if (aggregates.Count == 0) {
            return Array.Empty<SessionCapabilityDeferredWorkAffordanceDto>();
        }

        if (orchestrationCatalog?.EntriesByToolName is { Count: > 0 } entriesByToolName) {
            foreach (var entry in entriesByToolName.Values) {
                if (entry is null || string.IsNullOrWhiteSpace(entry.PackId)) {
                    continue;
                }

                foreach (var aggregate in aggregates.Values) {
                    if (!aggregate.PackIds.Contains(entry.PackId)) {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.DomainIntentFamily)) {
                        aggregate.RoutingFamilies.Add(entry.DomainIntentFamily);
                    }

                    AddRepresentativeExamples(aggregate.RepresentativeExamples, entry.RepresentativeExamples);
                }
            }
        }

        return aggregates.Values
            .OrderBy(static aggregate => string.Equals(aggregate.CapabilityId, BackgroundFollowUpCapabilityId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(static aggregate => aggregate.CapabilityId, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .Select(static aggregate => ToDto(aggregate))
            .ToArray();
    }

    /// <summary>
    /// Formats a compact prompt-friendly summary for a deferred-work affordance.
    /// </summary>
    public static string FormatSummary(SessionCapabilityDeferredWorkAffordanceDto affordance) {
        ArgumentNullException.ThrowIfNull(affordance);

        var mode = (affordance.AvailabilityMode ?? string.Empty).Trim();
        if (string.Equals(mode, AvailabilityModeRuntimeScheduler, StringComparison.OrdinalIgnoreCase)) {
            return affordance.SupportsBackgroundExecution
                ? affordance.CapabilityId + "[runtime_scheduler]"
                : affordance.CapabilityId + "[runtime]";
        }

        if (affordance.PackIds.Length > 0) {
            return affordance.CapabilityId + "[pack_declared:" + string.Join("/", affordance.PackIds) + "]";
        }

        return affordance.CapabilityId + "[pack_declared]";
    }

    /// <summary>
    /// Builds prompt-friendly guidance lines that summarize deferred follow-up affordances.
    /// </summary>
    public static IReadOnlyList<string> BuildPromptHintLines(
        IReadOnlyList<SessionCapabilityDeferredWorkAffordanceDto>? affordances,
        int maxExamples = 3) {
        if (affordances is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var labels = new List<string>(affordances.Count);
        var detailSummaries = new List<string>(affordances.Count);
        var examples = new List<string>(Math.Max(0, maxExamples));
        var hasBackgroundCapableAffordance = false;

        for (var i = 0; i < affordances.Count; i++) {
            var affordance = affordances[i];
            if (affordance is null || string.IsNullOrWhiteSpace(affordance.CapabilityId)) {
                continue;
            }

            var displayName = ResolveDisplayName(affordance);
            var capabilityId = affordance.CapabilityId.Trim();
            var label = displayName.Length == 0
                ? capabilityId
                : displayName + " [" + capabilityId + "]";
            if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase)) {
                labels.Add(label);
            }

            var detail = FormatSummary(affordance);
            if (!detailSummaries.Contains(detail, StringComparer.OrdinalIgnoreCase)) {
                detailSummaries.Add(detail);
            }

            hasBackgroundCapableAffordance |= affordance.SupportsBackgroundExecution;
            AddRepresentativeExamples(examples, affordance.RepresentativeExamples, maxExamples);
        }

        if (labels.Count == 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(capacity: 4) {
            "Registered deferred follow-up capabilities: " + string.Join(", ", labels) + "."
        };
        if (examples.Count > 0) {
            lines.Add("Deferred follow-up examples: " + string.Join("; ", examples) + ".");
        }

        if (detailSummaries.Count > 0) {
            lines.Add("Deferred capability details: " + string.Join(" | ", detailSummaries) + ".");
        }

        if (hasBackgroundCapableAffordance) {
            lines.Add("Background-capable deferred follow-up is available for long-running read-only or otherwise explicitly permitted work.");
        }

        return lines;
    }

    private static bool IsPackEnabledOrUnknown(
        string normalizedPackId,
        IReadOnlyDictionary<string, ToolPackAvailabilityInfo[]> packAvailabilityById) {
        if (normalizedPackId.Length == 0) {
            return false;
        }

        if (!packAvailabilityById.TryGetValue(normalizedPackId, out var entries) || entries.Length == 0) {
            return true;
        }

        for (var i = 0; i < entries.Length; i++) {
            if (entries[i].Enabled) {
                return true;
            }
        }

        return false;
    }

    private static void AddRepresentativeExamples(List<string> destination, IReadOnlyList<string>? examples, int maxItems = int.MaxValue) {
        if (examples is not { Count: > 0 } || maxItems <= 0 || destination.Count >= maxItems) {
            return;
        }

        for (var i = 0; i < examples.Count; i++) {
            var example = (examples[i] ?? string.Empty).Trim();
            if (example.Length == 0 || destination.Contains(example, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }

            destination.Add(example);
            if (destination.Count >= maxItems) {
                break;
            }
        }
    }

    private static AffordanceAggregate GetOrCreateAggregate(
        IDictionary<string, AffordanceAggregate> aggregates,
        string capabilityId,
        string availabilityMode) {
        if (aggregates.TryGetValue(capabilityId, out var existing)) {
            return existing;
        }

        var aggregate = new AffordanceAggregate {
            CapabilityId = capabilityId,
            AvailabilityMode = availabilityMode
        };
        aggregates[capabilityId] = aggregate;
        return aggregate;
    }

    private static SessionCapabilityDeferredWorkAffordanceDto ToDto(AffordanceAggregate aggregate) {
        var displayName = HumanizeCapabilityId(aggregate.CapabilityId);
        var summary = BuildSummary(aggregate, displayName);

        return new SessionCapabilityDeferredWorkAffordanceDto {
            CapabilityId = aggregate.CapabilityId,
            DisplayName = displayName,
            Summary = summary,
            AvailabilityMode = aggregate.AvailabilityMode,
            SupportsBackgroundExecution = aggregate.SupportsBackgroundExecution,
            PackIds = aggregate.PackIds
                .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RoutingFamilies = aggregate.RoutingFamilies
                .OrderBy(static family => family, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RepresentativeExamples = aggregate.RepresentativeExamples.Take(3).ToArray()
        };
    }

    private static string BuildSummary(AffordanceAggregate aggregate, string displayName) {
        if (string.Equals(aggregate.AvailabilityMode, AvailabilityModeRuntimeScheduler, StringComparison.OrdinalIgnoreCase)) {
            return aggregate.SupportsBackgroundExecution
                ? displayName + " can be tracked and advanced by the runtime background scheduler."
                : displayName + " is exposed by the runtime scheduler surface.";
        }

        if (aggregate.PackIds.Count > 0) {
            return displayName + " is advertised by enabled pack metadata: " + string.Join(", ", aggregate.PackIds.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)) + ".";
        }

        return displayName + " is advertised by the current runtime.";
    }

    private static string ResolveDisplayName(SessionCapabilityDeferredWorkAffordanceDto affordance) {
        var displayName = (affordance.DisplayName ?? string.Empty).Trim();
        return displayName.Length == 0
            ? HumanizeCapabilityId(affordance.CapabilityId)
            : displayName;
    }

    private static string HumanizeCapabilityId(string capabilityId) {
        var normalized = (capabilityId ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var parts = normalized
            .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => char.ToUpperInvariant(part[0]) + part[1..])
            .ToArray();
        return parts.Length == 0 ? normalized : string.Join(" ", parts);
    }
}
