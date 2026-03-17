using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools;

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
        var targetScopedToolNames = entries
            .Where(static entry => entry.SupportsTargetScoping || entry.TargetScopeArguments.Count > 0)
            .Select(static entry => entry.ToolName);
        var remoteHostTargetingToolNames = entries
            .Where(static entry => entry.SupportsRemoteHostTargeting || entry.RemoteHostArguments.Count > 0)
            .Select(static entry => entry.ToolName);
        var setupAwareToolNames = entries
            .Where(static entry => entry.IsSetupAware)
            .Select(static entry => entry.ToolName);
        var environmentDiscoverToolNames = entries
            .Where(static entry => entry.IsEnvironmentDiscoverTool)
            .Select(static entry => entry.ToolName);
        var handoffAwareToolNames = entries
            .Where(static entry => entry.IsHandoffAware)
            .Select(static entry => entry.ToolName);
        var recoveryAwareToolNames = entries
            .Where(static entry => entry.IsRecoveryAware)
            .Select(static entry => entry.ToolName);
        var writeCapableToolNames = entries
            .Where(static entry => entry.IsWriteCapable)
            .Select(static entry => entry.ToolName);
        var authenticationRequiredToolNames = entries
            .Where(static entry => entry.RequiresAuthentication)
            .Select(static entry => entry.ToolName);
        var probeCapableToolNames = entries
            .Where(static entry => entry.SupportsConnectivityProbe || !string.IsNullOrWhiteSpace(entry.ProbeToolName))
            .Select(static entry => entry.ToolName);
        var crossPackHandoffToolNames = entries
            .Where(entry => IsCrossPackHandoff(normalizedPackId, entry))
            .Select(static entry => entry.ToolName);
        var crossPackTargetPacks = entries
            .Where(entry => IsCrossPackHandoff(normalizedPackId, entry))
            .SelectMany(static entry => entry.HandoffEdges)
            .Select(edge => NormalizePackId(edge.TargetPackId))
            .Where(targetPackId => targetPackId.Length > 0 && !string.Equals(targetPackId, normalizedPackId, StringComparison.OrdinalIgnoreCase));

        var remoteCapableToolCount = CountDistinct(remoteCapableToolNames);
        var targetScopedToolCount = CountDistinct(targetScopedToolNames);
        var remoteHostTargetingToolCount = CountDistinct(remoteHostTargetingToolNames);
        var setupAwareToolCount = CountDistinct(setupAwareToolNames);
        var environmentDiscoverToolCount = CountDistinct(environmentDiscoverToolNames);
        var handoffAwareToolCount = CountDistinct(handoffAwareToolNames);
        var recoveryAwareToolCount = CountDistinct(recoveryAwareToolNames);
        var writeCapableToolCount = CountDistinct(writeCapableToolNames);
        var authenticationRequiredToolCount = CountDistinct(authenticationRequiredToolNames);
        var probeCapableToolCount = CountDistinct(probeCapableToolNames);
        var crossPackHandoffToolCount = CountDistinct(crossPackHandoffToolNames);
        var normalizedRemoteCapableToolNames = NormalizeDistinctStrings(remoteCapableToolNames, maxItems);
        var normalizedTargetScopedToolNames = NormalizeDistinctStrings(targetScopedToolNames, maxItems);
        var normalizedRemoteHostTargetingToolNames = NormalizeDistinctStrings(remoteHostTargetingToolNames, maxItems);
        var normalizedSetupAwareToolNames = NormalizeDistinctStrings(setupAwareToolNames, maxItems);
        var normalizedEnvironmentDiscoverToolNames = NormalizeDistinctStrings(environmentDiscoverToolNames, maxItems);
        var normalizedHandoffAwareToolNames = NormalizeDistinctStrings(handoffAwareToolNames, maxItems);
        var normalizedRecoveryAwareToolNames = NormalizeDistinctStrings(recoveryAwareToolNames, maxItems);
        var normalizedWriteCapableToolNames = NormalizeDistinctStrings(writeCapableToolNames, maxItems);
        var normalizedAuthenticationRequiredToolNames = NormalizeDistinctStrings(authenticationRequiredToolNames, maxItems);
        var normalizedProbeCapableToolNames = NormalizeDistinctStrings(probeCapableToolNames, maxItems);
        var normalizedCrossPackHandoffToolNames = NormalizeDistinctStrings(crossPackHandoffToolNames, maxItems);
        var normalizedCrossPackTargetPacks = NormalizeDistinctStrings(crossPackTargetPacks, maxItems);

        return new ToolPackAutonomySummaryDto {
            TotalTools = Math.Max(0, entries.Count),
            RemoteCapableTools = remoteCapableToolCount,
            RemoteCapableToolNames = normalizedRemoteCapableToolNames,
            TargetScopedTools = targetScopedToolCount,
            TargetScopedToolNames = normalizedTargetScopedToolNames,
            RemoteHostTargetingTools = remoteHostTargetingToolCount,
            RemoteHostTargetingToolNames = normalizedRemoteHostTargetingToolNames,
            SetupAwareTools = setupAwareToolCount,
            EnvironmentDiscoverTools = environmentDiscoverToolCount,
            SetupAwareToolNames = normalizedSetupAwareToolNames,
            HandoffAwareTools = handoffAwareToolCount,
            EnvironmentDiscoverToolNames = normalizedEnvironmentDiscoverToolNames,
            HandoffAwareToolNames = normalizedHandoffAwareToolNames,
            RecoveryAwareTools = recoveryAwareToolCount,
            RecoveryAwareToolNames = normalizedRecoveryAwareToolNames,
            WriteCapableTools = writeCapableToolCount,
            WriteCapableToolNames = normalizedWriteCapableToolNames,
            AuthenticationRequiredTools = authenticationRequiredToolCount,
            AuthenticationRequiredToolNames = normalizedAuthenticationRequiredToolNames,
            ProbeCapableTools = probeCapableToolCount,
            ProbeCapableToolNames = normalizedProbeCapableToolNames,
            CrossPackHandoffTools = crossPackHandoffToolCount,
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
        var targetScopedToolCount = 0;
        var remoteHostTargetingToolCount = 0;
        var setupAwareToolCount = 0;
        var environmentDiscoverToolCount = 0;
        var handoffAwareToolCount = 0;
        var recoveryAwareToolCount = 0;
        var writeCapableToolCount = 0;
        var authenticationRequiredToolCount = 0;
        var probeCapableToolCount = 0;
        var crossPackHandoffToolCount = 0;
        var remoteCapablePackIds = new List<string>();
        var targetScopedPackIds = new List<string>();
        var remoteHostTargetingPackIds = new List<string>();
        var environmentDiscoverPackIds = new List<string>();
        var writeCapablePackIds = new List<string>();
        var authenticationRequiredPackIds = new List<string>();
        var probeCapablePackIds = new List<string>();
        var crossPackReadyPackIds = new List<string>();
        var crossPackTargetPackIds = new List<string>();

        for (var i = 0; i < enabledPackIds.Length; i++) {
            var summary = BuildPackAutonomySummary(enabledPackIds[i], orchestrationCatalog, maxItems: 16);
            if (summary is null) {
                continue;
            }

            remoteCapableToolCount += Math.Max(0, summary.RemoteCapableTools);
            targetScopedToolCount += Math.Max(0, summary.TargetScopedTools);
            remoteHostTargetingToolCount += Math.Max(0, summary.RemoteHostTargetingTools);
            setupAwareToolCount += Math.Max(0, summary.SetupAwareTools);
            environmentDiscoverToolCount += Math.Max(0, summary.EnvironmentDiscoverTools);
            handoffAwareToolCount += Math.Max(0, summary.HandoffAwareTools);
            recoveryAwareToolCount += Math.Max(0, summary.RecoveryAwareTools);
            writeCapableToolCount += Math.Max(0, summary.WriteCapableTools);
            authenticationRequiredToolCount += Math.Max(0, summary.AuthenticationRequiredTools);
            probeCapableToolCount += Math.Max(0, summary.ProbeCapableTools);
            crossPackHandoffToolCount += Math.Max(0, summary.CrossPackHandoffTools);

            if (summary.RemoteCapableTools > 0) {
                remoteCapablePackIds.Add(enabledPackIds[i]);
            }
            if (summary.TargetScopedTools > 0) {
                targetScopedPackIds.Add(enabledPackIds[i]);
            }
            if (summary.RemoteHostTargetingTools > 0) {
                remoteHostTargetingPackIds.Add(enabledPackIds[i]);
            }
            if (summary.EnvironmentDiscoverTools > 0) {
                environmentDiscoverPackIds.Add(enabledPackIds[i]);
            }
            if (summary.WriteCapableTools > 0) {
                writeCapablePackIds.Add(enabledPackIds[i]);
            }
            if (summary.AuthenticationRequiredTools > 0) {
                authenticationRequiredPackIds.Add(enabledPackIds[i]);
            }
            if (summary.ProbeCapableTools > 0) {
                probeCapablePackIds.Add(enabledPackIds[i]);
            }

            if (summary.CrossPackHandoffTools > 0) {
                crossPackReadyPackIds.Add(enabledPackIds[i]);
                crossPackTargetPackIds.AddRange(summary.CrossPackTargetPacks ?? Array.Empty<string>());
            }
        }

        var normalizedRemoteCapablePackIds = NormalizeDistinctStrings(remoteCapablePackIds, maxPackIds);
        var normalizedTargetScopedPackIds = NormalizeDistinctStrings(targetScopedPackIds, maxPackIds);
        var normalizedRemoteHostTargetingPackIds = NormalizeDistinctStrings(remoteHostTargetingPackIds, maxPackIds);
        var normalizedEnvironmentDiscoverPackIds = NormalizeDistinctStrings(environmentDiscoverPackIds, maxPackIds);
        var normalizedWriteCapablePackIds = NormalizeDistinctStrings(writeCapablePackIds, maxPackIds);
        var normalizedAuthenticationRequiredPackIds = NormalizeDistinctStrings(authenticationRequiredPackIds, maxPackIds);
        var normalizedProbeCapablePackIds = NormalizeDistinctStrings(probeCapablePackIds, maxPackIds);
        var normalizedCrossPackReadyPackIds = NormalizeDistinctStrings(crossPackReadyPackIds, maxPackIds);
        var normalizedCrossPackTargetPackIds = NormalizeDistinctStrings(crossPackTargetPackIds, maxPackIds);

        if (remoteCapableToolCount <= 0
            && targetScopedToolCount <= 0
            && remoteHostTargetingToolCount <= 0
            && setupAwareToolCount <= 0
            && environmentDiscoverToolCount <= 0
            && handoffAwareToolCount <= 0
            && recoveryAwareToolCount <= 0
            && writeCapableToolCount <= 0
            && authenticationRequiredToolCount <= 0
            && probeCapableToolCount <= 0
            && crossPackHandoffToolCount <= 0
            && normalizedRemoteCapablePackIds.Length == 0
            && normalizedTargetScopedPackIds.Length == 0
            && normalizedRemoteHostTargetingPackIds.Length == 0
            && normalizedEnvironmentDiscoverPackIds.Length == 0
            && normalizedWriteCapablePackIds.Length == 0
            && normalizedAuthenticationRequiredPackIds.Length == 0
            && normalizedProbeCapablePackIds.Length == 0
            && normalizedCrossPackReadyPackIds.Length == 0
            && normalizedCrossPackTargetPackIds.Length == 0) {
            return null;
        }

        return new SessionCapabilityAutonomySummaryDto {
            RemoteCapableToolCount = Math.Max(0, remoteCapableToolCount),
            TargetScopedToolCount = Math.Max(0, targetScopedToolCount),
            RemoteHostTargetingToolCount = Math.Max(0, remoteHostTargetingToolCount),
            SetupAwareToolCount = Math.Max(0, setupAwareToolCount),
            EnvironmentDiscoverToolCount = Math.Max(0, environmentDiscoverToolCount),
            HandoffAwareToolCount = Math.Max(0, handoffAwareToolCount),
            RecoveryAwareToolCount = Math.Max(0, recoveryAwareToolCount),
            WriteCapableToolCount = Math.Max(0, writeCapableToolCount),
            AuthenticationRequiredToolCount = Math.Max(0, authenticationRequiredToolCount),
            ProbeCapableToolCount = Math.Max(0, probeCapableToolCount),
            CrossPackHandoffToolCount = Math.Max(0, crossPackHandoffToolCount),
            RemoteCapablePackIds = normalizedRemoteCapablePackIds,
            TargetScopedPackIds = normalizedTargetScopedPackIds,
            RemoteHostTargetingPackIds = normalizedRemoteHostTargetingPackIds,
            EnvironmentDiscoverPackIds = normalizedEnvironmentDiscoverPackIds,
            WriteCapablePackIds = normalizedWriteCapablePackIds,
            AuthenticationRequiredPackIds = normalizedAuthenticationRequiredPackIds,
            ProbeCapablePackIds = normalizedProbeCapablePackIds,
            CrossPackReadyPackIds = normalizedCrossPackReadyPackIds,
            CrossPackTargetPackIds = normalizedCrossPackTargetPackIds
        };
    }

    private static bool IsRemoteCapable(ToolOrchestrationCatalogEntry entry) {
        return ToolExecutionScopes.IsRemoteCapable(entry.ExecutionScope);
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

    private static int CountDistinct(IEnumerable<string>? values) {
        if (values is null) {
            return 0;
        }

        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > 0) {
                dedupe.Add(normalized);
            }
        }

        return dedupe.Count;
    }
}
