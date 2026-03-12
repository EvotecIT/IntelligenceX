using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared projections for pack-level and aggregate autonomy readiness.
/// </summary>
public static class ToolAutonomySummaryBuilder {
    /// <summary>
    /// Builds the autonomy summary for a single pack from the orchestration catalog.
    /// </summary>
    public static ToolPackAutonomySummaryDto? BuildPackAutonomySummary(
        string? packId,
        ToolOrchestrationCatalog? orchestrationCatalog,
        int maxItems = 16) {
        if (orchestrationCatalog is null) {
            return null;
        }

        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return null;
        }

        var entries = orchestrationCatalog.GetByPackId(normalizedPackId);
        if (entries.Count == 0) {
            return null;
        }

        var remoteCapableToolNames = entries
            .Where(static entry => IsRemoteCapable(entry))
            .Select(static entry => entry.ToolName);
        var setupAwareToolNames = entries
            .Where(static entry => entry.IsSetupAware)
            .Select(static entry => entry.ToolName);
        var handoffAwareToolNames = entries
            .Where(static entry => entry.IsHandoffAware)
            .Select(static entry => entry.ToolName);
        var recoveryAwareToolNames = entries
            .Where(static entry => entry.IsRecoveryAware)
            .Select(static entry => entry.ToolName);
        var crossPackHandoffToolNames = entries
            .Where(entry => IsCrossPackHandoff(normalizedPackId, entry))
            .Select(static entry => entry.ToolName);
        var crossPackTargetPacks = entries
            .Where(entry => IsCrossPackHandoff(normalizedPackId, entry))
            .SelectMany(static entry => entry.HandoffEdges)
            .Select(edge => NormalizePackId(edge.TargetPackId))
            .Where(targetPackId => targetPackId.Length > 0 && !string.Equals(targetPackId, normalizedPackId, StringComparison.OrdinalIgnoreCase));

        var normalizedRemoteCapableToolNames = NormalizeDistinctStrings(remoteCapableToolNames, maxItems);
        var normalizedSetupAwareToolNames = NormalizeDistinctStrings(setupAwareToolNames, maxItems);
        var normalizedHandoffAwareToolNames = NormalizeDistinctStrings(handoffAwareToolNames, maxItems);
        var normalizedRecoveryAwareToolNames = NormalizeDistinctStrings(recoveryAwareToolNames, maxItems);
        var normalizedCrossPackHandoffToolNames = NormalizeDistinctStrings(crossPackHandoffToolNames, maxItems);
        var normalizedCrossPackTargetPacks = NormalizeDistinctStrings(crossPackTargetPacks, maxItems);

        return new ToolPackAutonomySummaryDto {
            TotalTools = Math.Max(0, entries.Count),
            RemoteCapableTools = normalizedRemoteCapableToolNames.Length,
            RemoteCapableToolNames = normalizedRemoteCapableToolNames,
            SetupAwareTools = normalizedSetupAwareToolNames.Length,
            SetupAwareToolNames = normalizedSetupAwareToolNames,
            HandoffAwareTools = normalizedHandoffAwareToolNames.Length,
            HandoffAwareToolNames = normalizedHandoffAwareToolNames,
            RecoveryAwareTools = normalizedRecoveryAwareToolNames.Length,
            RecoveryAwareToolNames = normalizedRecoveryAwareToolNames,
            CrossPackHandoffTools = normalizedCrossPackHandoffToolNames.Length,
            CrossPackHandoffToolNames = normalizedCrossPackHandoffToolNames,
            CrossPackTargetPacks = normalizedCrossPackTargetPacks
        };
    }

    /// <summary>
    /// Builds aggregate autonomy readiness for the enabled pack set.
    /// </summary>
    public static SessionCapabilityAutonomySummaryDto? BuildCapabilityAutonomySummary(
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog,
        int maxPackIds = 8) {
        if (orchestrationCatalog is null) {
            return null;
        }

        var enabledPackIds = NormalizeDistinctStrings(
            (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
                .Where(static pack => pack.Enabled)
                .Select(static pack => NormalizePackId(pack.Id)),
            maxPackIds: 0);
        if (enabledPackIds.Length == 0) {
            return null;
        }

        var remoteCapableToolCount = 0;
        var setupAwareToolCount = 0;
        var handoffAwareToolCount = 0;
        var recoveryAwareToolCount = 0;
        var crossPackHandoffToolCount = 0;
        var remoteCapablePackIds = new List<string>();
        var crossPackReadyPackIds = new List<string>();
        var crossPackTargetPackIds = new List<string>();

        for (var i = 0; i < enabledPackIds.Length; i++) {
            var summary = BuildPackAutonomySummary(enabledPackIds[i], orchestrationCatalog, maxItems: 16);
            if (summary is null) {
                continue;
            }

            remoteCapableToolCount += Math.Max(0, summary.RemoteCapableTools);
            setupAwareToolCount += Math.Max(0, summary.SetupAwareTools);
            handoffAwareToolCount += Math.Max(0, summary.HandoffAwareTools);
            recoveryAwareToolCount += Math.Max(0, summary.RecoveryAwareTools);
            crossPackHandoffToolCount += Math.Max(0, summary.CrossPackHandoffTools);

            if (summary.RemoteCapableTools > 0) {
                remoteCapablePackIds.Add(enabledPackIds[i]);
            }

            if (summary.CrossPackHandoffTools > 0) {
                crossPackReadyPackIds.Add(enabledPackIds[i]);
                crossPackTargetPackIds.AddRange(summary.CrossPackTargetPacks ?? Array.Empty<string>());
            }
        }

        var normalizedRemoteCapablePackIds = NormalizeDistinctStrings(remoteCapablePackIds, maxPackIds);
        var normalizedCrossPackReadyPackIds = NormalizeDistinctStrings(crossPackReadyPackIds, maxPackIds);
        var normalizedCrossPackTargetPackIds = NormalizeDistinctStrings(crossPackTargetPackIds, maxPackIds);

        if (remoteCapableToolCount <= 0
            && setupAwareToolCount <= 0
            && handoffAwareToolCount <= 0
            && recoveryAwareToolCount <= 0
            && crossPackHandoffToolCount <= 0
            && normalizedRemoteCapablePackIds.Length == 0
            && normalizedCrossPackReadyPackIds.Length == 0
            && normalizedCrossPackTargetPackIds.Length == 0) {
            return null;
        }

        return new SessionCapabilityAutonomySummaryDto {
            RemoteCapableToolCount = Math.Max(0, remoteCapableToolCount),
            SetupAwareToolCount = Math.Max(0, setupAwareToolCount),
            HandoffAwareToolCount = Math.Max(0, handoffAwareToolCount),
            RecoveryAwareToolCount = Math.Max(0, recoveryAwareToolCount),
            CrossPackHandoffToolCount = Math.Max(0, crossPackHandoffToolCount),
            RemoteCapablePackIds = normalizedRemoteCapablePackIds,
            CrossPackReadyPackIds = normalizedCrossPackReadyPackIds,
            CrossPackTargetPackIds = normalizedCrossPackTargetPackIds
        };
    }

    private static bool IsRemoteCapable(ToolOrchestrationCatalogEntry entry) {
        return string.Equals(entry.ExecutionScope, "local_or_remote", StringComparison.OrdinalIgnoreCase)
            || entry.SupportsRemoteHostTargeting
            || entry.RemoteHostArguments.Count > 0;
    }

    private static bool IsCrossPackHandoff(string normalizedPackId, ToolOrchestrationCatalogEntry entry) {
        if (!entry.IsHandoffAware || entry.HandoffEdges.Count == 0) {
            return false;
        }

        for (var i = 0; i < entry.HandoffEdges.Count; i++) {
            var targetPackId = NormalizePackId(entry.HandoffEdges[i].TargetPackId);
            if (targetPackId.Length > 0 && !string.Equals(targetPackId, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePackId(string? value) {
        return ToolPackBootstrap.NormalizePackId(value);
    }

    private static string[] NormalizeDistinctStrings(IEnumerable<string>? values, int maxPackIds) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var value in values) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !dedupe.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
            if (maxPackIds > 0 && list.Count >= maxPackIds) {
                break;
            }
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }
}
