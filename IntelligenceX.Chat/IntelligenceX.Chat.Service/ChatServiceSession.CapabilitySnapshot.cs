using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string CapabilitySnapshotMarker = "ix:capability-snapshot:v1";
    private const string SkillsSnapshotMarker = "ix:skills:v1";
    private const int MaxCapabilitySnapshotPackIds = 8;
    private const int MaxCapabilitySnapshotPluginIds = 8;
    private const int MaxCapabilitySnapshotEngineIds = 8;
    private const int MaxCapabilitySnapshotCapabilityTags = 12;
    private const int MaxCapabilitySnapshotFamilies = 6;
    private const int MaxCapabilitySnapshotSkills = 8;
    private const int MaxCapabilitySnapshotHealthyTools = 12;
    private const int MaxCapabilitySnapshotParityAttention = 4;
    private const int MaxCapabilitySnapshotParityDetail = 4;

    private SessionCapabilitySnapshotDto BuildRuntimeCapabilitySnapshot() {
        return BuildCapabilitySnapshot(
            _options,
            _registry.GetDefinitions(),
            _packAvailability,
            _pluginAvailability,
            _routingCatalogDiagnostics,
            _toolOrchestrationCatalog,
            connectedRuntimeSkills: _connectedRuntimeSkillInventory,
            healthyToolNames: ResolveWorkingMemoryCapabilityHealthyToolNames(
                Array.Empty<string>(),
                Array.Empty<string>()),
            remoteReachabilityMode: ResolveHelloRemoteReachabilityMode());
    }

    internal static SessionCapabilitySnapshotDto BuildCapabilitySnapshot(
        ServiceOptions options,
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IEnumerable<ToolPackAvailabilityInfo> packAvailability,
        IEnumerable<ToolPluginAvailabilityInfo>? pluginAvailability,
        ToolRoutingCatalogDiagnostics? routingCatalog,
        ToolOrchestrationCatalog? orchestrationCatalog = null,
        IEnumerable<string>? connectedRuntimeSkills = null,
        IEnumerable<string>? healthyToolNames = null,
        string? remoteReachabilityMode = null) {
        ArgumentNullException.ThrowIfNull(options);

        var enabledPackIds = NormalizeCapabilitySnapshotEnabledPackIds(
            (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack.Enabled)
            .Select(static pack => pack.Id));
        var allPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
            (pluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>())
            .Select(static plugin => plugin.Id),
            maxItems: 0);
        if (allPluginIds.Length == 0) {
            allPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
                (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
                .Select(static pack => pack.Id),
                maxItems: 0);
        }

        var enabledPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
            (pluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>())
            .Where(static plugin => plugin.Enabled)
            .Select(static plugin => plugin.Id),
            MaxCapabilitySnapshotPluginIds);
        if (enabledPluginIds.Length == 0) {
            enabledPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
                (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
                .Where(static pack => pack.Enabled)
                .Select(static pack => pack.Id),
                MaxCapabilitySnapshotPluginIds);
        }
        var enabledPackEngineIds = NormalizeCapabilitySnapshotDescriptorTokens(
            (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack.Enabled)
            .Select(static pack => pack.EngineId),
            MaxCapabilitySnapshotEngineIds);
        var enabledCapabilityTags = NormalizeCapabilitySnapshotDescriptorTokens(
            (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack.Enabled)
            .SelectMany(static pack => pack.CapabilityTags ?? Array.Empty<string>()),
            MaxCapabilitySnapshotCapabilityTags);

        var familyActions = MapCapabilityFamilyActions(routingCatalog);
        var routingFamilies = NormalizeCapabilitySnapshotRoutingFamilies(
            familyActions.Select(static summary => summary.Family));
        var skills = ResolveCapabilitySnapshotSkills(pluginAvailability, routingCatalog, connectedRuntimeSkills);
        var healthyTools = NormalizeCapabilitySnapshotHealthyToolNames(healthyToolNames ?? Array.Empty<string>());
        var registeredTools = Math.Max(0, routingCatalog?.TotalTools ?? 0);
        var allowedRootCount = Math.Max(0, options.AllowedRoots.Count);
        var autonomy = ToolAutonomySummaryBuilder.BuildCapabilityAutonomySummary(
            packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            orchestrationCatalog);
        var parityEntries = ToolCapabilityParityInventoryBuilder.Build(toolDefinitions, packAvailability);
        var parityAttentionCount = parityEntries.Count(static entry =>
            !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.HealthyStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.PackUnavailableStatus, StringComparison.OrdinalIgnoreCase));
        var parityMissingCapabilityCount = parityEntries.Sum(static entry => Math.Max(0, entry.MissingCapabilityCount));

        return new SessionCapabilitySnapshotDto {
            RegisteredTools = registeredTools,
            EnabledPackCount = enabledPackIds.Length,
            PluginCount = allPluginIds.Length,
            EnabledPluginCount = enabledPluginIds.Length,
            ToolingAvailable = enabledPackIds.Length > 0 || registeredTools > 0,
            AllowedRootCount = allowedRootCount,
            EnabledPackIds = enabledPackIds,
            EnabledPluginIds = enabledPluginIds,
            EnabledPackEngineIds = enabledPackEngineIds,
            EnabledCapabilityTags = enabledCapabilityTags,
            RoutingFamilies = routingFamilies,
            FamilyActions = familyActions,
            Skills = skills,
            HealthyTools = healthyTools,
            RemoteReachabilityMode = NormalizeCapabilitySnapshotRemoteReachabilityMode(remoteReachabilityMode),
            Autonomy = autonomy,
            ParityEntries = parityEntries,
            ParityAttentionCount = Math.Max(0, parityAttentionCount),
            ParityMissingCapabilityCount = Math.Max(0, parityMissingCapabilityCount)
        };
    }

    private static SessionRoutingFamilyActionSummaryDto[] MapCapabilityFamilyActions(ToolRoutingCatalogDiagnostics? diagnostics) {
        var familyActions = diagnostics?.FamilyActions;
        if (familyActions is null || familyActions.Count == 0) {
            return Array.Empty<SessionRoutingFamilyActionSummaryDto>();
        }

        return familyActions
            .Where(static summary =>
                !string.IsNullOrWhiteSpace(summary.Family)
                && !string.IsNullOrWhiteSpace(summary.ActionId))
            .OrderByDescending(static summary => Math.Max(0, summary.ToolCount))
            .ThenBy(static summary => summary.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static summary => summary.ActionId, StringComparer.OrdinalIgnoreCase)
            .Select(static summary => new SessionRoutingFamilyActionSummaryDto {
                Family = summary.Family.Trim(),
                ActionId = summary.ActionId.Trim(),
                ToolCount = Math.Max(0, summary.ToolCount)
            })
            .ToArray();
    }

    private static string[] NormalizeCapabilitySnapshotEnabledPackIds(IEnumerable<string> packIds) {
        return NormalizeDistinctStrings(
            (packIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0),
            MaxCapabilitySnapshotPackIds);
    }

    private static string[] NormalizeCapabilitySnapshotEnabledPluginIds(IEnumerable<string> pluginIds, int maxItems) {
        return NormalizeDistinctStrings(
            (pluginIds ?? Array.Empty<string>())
            .Select(static pluginId => NormalizePackId(pluginId))
            .Where(static pluginId => pluginId.Length > 0),
            maxItems);
    }

    private static string[] NormalizeCapabilitySnapshotDescriptorTokens(IEnumerable<string?> values, int maxItems) {
        return NormalizeDistinctStrings(
            (values ?? Array.Empty<string>())
            .Select(static value => ToolPackMetadataNormalizer.NormalizeDescriptorToken(value))
            .Where(static value => value.Length > 0),
            maxItems);
    }

    private static string[] NormalizeCapabilitySnapshotRoutingFamilies(IEnumerable<string> routingFamilies) {
        if (routingFamilies is null) {
            return Array.Empty<string>();
        }

        var normalizedFamilies = new List<string>();
        foreach (var family in routingFamilies) {
            if (!TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                continue;
            }

            normalizedFamilies.Add(normalizedFamily);
        }

        return NormalizeDistinctStrings(normalizedFamilies, MaxCapabilitySnapshotFamilies);
    }

    private static string[] NormalizeCapabilitySnapshotSkills(IEnumerable<string> skills) {
        return NormalizeSkillInventoryValues(skills, MaxCapabilitySnapshotSkills);
    }

    private static string[] NormalizeSkillInventoryValues(IEnumerable<string> skills, int maxItems) {
        return NormalizeDistinctStrings(
            (skills ?? Array.Empty<string>())
            .Select(static skill => NormalizeSkillSnapshotValue(skill))
            .Where(static skill => skill.Length > 0),
            maxItems);
    }

    private static string[] NormalizeCapabilitySnapshotHealthyToolNames(IEnumerable<string> healthyToolNames) {
        return NormalizeDistinctStrings(
            (healthyToolNames ?? Array.Empty<string>())
            .Select(static toolName => (toolName ?? string.Empty).Trim())
            .Where(static toolName => toolName.Length > 0),
            MaxCapabilitySnapshotHealthyTools);
    }

    private static string? NormalizeCapabilitySnapshotRemoteReachabilityMode(string? remoteReachabilityMode) {
        var normalized = (remoteReachabilityMode ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static void AppendCapabilitySnapshotPromptBlock(
        StringBuilder runtimeIdentity,
        SessionCapabilitySnapshotDto snapshot,
        ToolRoutingCatalogDiagnostics? routingCatalog = null) {
        ArgumentNullException.ThrowIfNull(runtimeIdentity);
        ArgumentNullException.ThrowIfNull(snapshot);

        runtimeIdentity.AppendLine();
        runtimeIdentity.AppendLine("[Capability snapshot]");
        runtimeIdentity.AppendLine(CapabilitySnapshotMarker);
        runtimeIdentity.AppendLine("registered_tools: " + snapshot.RegisteredTools);
        runtimeIdentity.AppendLine("enabled_pack_count: " + snapshot.EnabledPackCount);
        runtimeIdentity.AppendLine("plugin_count: " + snapshot.PluginCount);
        runtimeIdentity.AppendLine("enabled_plugin_count: " + snapshot.EnabledPluginCount);
        if (snapshot.EnabledPackIds.Length > 0) {
            runtimeIdentity.AppendLine("enabled_packs: " + string.Join(", ", snapshot.EnabledPackIds));
        }

        if (snapshot.EnabledPluginIds.Length > 0) {
            runtimeIdentity.AppendLine("enabled_plugins: " + string.Join(", ", snapshot.EnabledPluginIds));
        }

        if (snapshot.EnabledPackEngineIds.Length > 0) {
            runtimeIdentity.AppendLine("enabled_pack_engines: " + string.Join(", ", snapshot.EnabledPackEngineIds));
        }

        if (snapshot.EnabledCapabilityTags.Length > 0) {
            runtimeIdentity.AppendLine("enabled_capability_tags: " + string.Join(", ", snapshot.EnabledCapabilityTags));
        }

        if (snapshot.RoutingFamilies.Length > 0) {
            runtimeIdentity.AppendLine("routing_families: " + string.Join(", ", snapshot.RoutingFamilies));
        }

        if (snapshot.HealthyTools.Length > 0) {
            runtimeIdentity.AppendLine("healthy_tools: " + string.Join(", ", snapshot.HealthyTools));
        }
        var routingAutonomyReadiness = routingCatalog is null
            ? Array.Empty<string>()
            : ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(routingCatalog, maxItems: 4);
        if (routingAutonomyReadiness.Count > 0) {
            runtimeIdentity.AppendLine("routing_autonomy_readiness: " + string.Join(" | ", routingAutonomyReadiness));
        }
        if (snapshot.Autonomy is not null) {
            runtimeIdentity.AppendLine("autonomy_remote_capable_tools: " + snapshot.Autonomy.RemoteCapableToolCount);
            runtimeIdentity.AppendLine("autonomy_setup_aware_tools: " + snapshot.Autonomy.SetupAwareToolCount);
            runtimeIdentity.AppendLine("autonomy_handoff_aware_tools: " + snapshot.Autonomy.HandoffAwareToolCount);
            runtimeIdentity.AppendLine("autonomy_recovery_aware_tools: " + snapshot.Autonomy.RecoveryAwareToolCount);
            runtimeIdentity.AppendLine("autonomy_cross_pack_handoff_tools: " + snapshot.Autonomy.CrossPackHandoffToolCount);
            if (snapshot.Autonomy.RemoteCapablePackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_remote_capable_packs: " + string.Join(", ", snapshot.Autonomy.RemoteCapablePackIds));
            }

            if (snapshot.Autonomy.CrossPackReadyPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_cross_pack_ready_packs: " + string.Join(", ", snapshot.Autonomy.CrossPackReadyPackIds));
            }

            if (snapshot.Autonomy.CrossPackTargetPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_cross_pack_targets: " + string.Join(", ", snapshot.Autonomy.CrossPackTargetPackIds));
            }
        }
        if (snapshot.ParityEntries.Length > 0) {
            runtimeIdentity.AppendLine("parity_engine_count: " + snapshot.ParityEntries.Length);
            runtimeIdentity.AppendLine("parity_attention_count: " + snapshot.ParityAttentionCount);
            runtimeIdentity.AppendLine("parity_missing_readonly_capabilities: " + snapshot.ParityMissingCapabilityCount);

            var attentionSummaries = ToolCapabilityParityInventoryBuilder.BuildAttentionSummaries(
                snapshot.ParityEntries,
                MaxCapabilitySnapshotParityAttention);
            if (attentionSummaries.Count > 0) {
                runtimeIdentity.AppendLine("parity_attention: " + string.Join(" | ", attentionSummaries));
            }

            var detailSummaries = ToolCapabilityParityInventoryBuilder.BuildDetailSummaries(
                snapshot.ParityEntries,
                MaxCapabilitySnapshotParityDetail);
            if (detailSummaries.Count > 0) {
                runtimeIdentity.AppendLine("parity_detail: " + string.Join(" | ", detailSummaries));
            }
        }

        runtimeIdentity.AppendLine("Use this snapshot only for routing and tool-availability decisions.");
        runtimeIdentity.AppendLine("Parity fields describe phase-1 read-only engine coverage; use them to avoid promising governed or missing surfaces as live tools.");
        runtimeIdentity.AppendLine("Do not narrate this snapshot to the user unless they explicitly ask about runtime, tooling, or bootstrap state.");

        runtimeIdentity.AppendLine();
        runtimeIdentity.AppendLine("[Skills snapshot]");
        runtimeIdentity.AppendLine(SkillsSnapshotMarker);
        runtimeIdentity.AppendLine("skill_count: " + snapshot.Skills.Length);
        if (snapshot.Skills.Length > 0) {
            runtimeIdentity.AppendLine("skills: " + string.Join(", ", snapshot.Skills));
        }

        runtimeIdentity.AppendLine("Use this skills snapshot only to decide reusable skill availability for this turn.");
        runtimeIdentity.AppendLine("Do not narrate this skills inventory unless the user explicitly asks about it.");
    }
}
