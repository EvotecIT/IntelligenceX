using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string CapabilitySnapshotMarker = "ix:capability-snapshot:v1";
    private const string SkillsSnapshotMarker = "ix:skills:v1";
    private const int MaxCapabilitySnapshotPackIds = 8;
    private const int MaxCapabilitySnapshotPluginIds = 8;
    private const int MaxCapabilitySnapshotFamilies = 6;
    private const int MaxCapabilitySnapshotSkills = 8;
    private const int MaxCapabilitySnapshotHealthyTools = 12;

    private SessionCapabilitySnapshotDto BuildRuntimeCapabilitySnapshot() {
        return BuildCapabilitySnapshot(
            _options,
            _packAvailability,
            _pluginAvailability,
            _routingCatalogDiagnostics,
            connectedRuntimeSkills: _connectedRuntimeSkillInventory,
            healthyToolNames: ResolveWorkingMemoryCapabilityHealthyToolNames(
                Array.Empty<string>(),
                Array.Empty<string>()),
            remoteReachabilityMode: ResolveHelloRemoteReachabilityMode());
    }

    internal static SessionCapabilitySnapshotDto BuildCapabilitySnapshot(
        ServiceOptions options,
        IEnumerable<ToolPackAvailabilityInfo> packAvailability,
        IEnumerable<ToolPluginAvailabilityInfo>? pluginAvailability,
        ToolRoutingCatalogDiagnostics? routingCatalog,
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

        var familyActions = MapCapabilityFamilyActions(routingCatalog);
        var routingFamilies = NormalizeCapabilitySnapshotRoutingFamilies(
            familyActions.Select(static summary => summary.Family));
        var skills = ResolveCapabilitySnapshotSkills(pluginAvailability, routingCatalog, connectedRuntimeSkills);
        var healthyTools = NormalizeCapabilitySnapshotHealthyToolNames(healthyToolNames ?? Array.Empty<string>());
        var registeredTools = Math.Max(0, routingCatalog?.TotalTools ?? 0);
        var allowedRootCount = Math.Max(0, options.AllowedRoots.Count);

        return new SessionCapabilitySnapshotDto {
            RegisteredTools = registeredTools,
            EnabledPackCount = enabledPackIds.Length,
            PluginCount = allPluginIds.Length,
            EnabledPluginCount = enabledPluginIds.Length,
            ToolingAvailable = enabledPackIds.Length > 0 || registeredTools > 0,
            AllowedRootCount = allowedRootCount,
            EnabledPackIds = enabledPackIds,
            EnabledPluginIds = enabledPluginIds,
            RoutingFamilies = routingFamilies,
            FamilyActions = familyActions,
            Skills = skills,
            HealthyTools = healthyTools,
            RemoteReachabilityMode = NormalizeCapabilitySnapshotRemoteReachabilityMode(remoteReachabilityMode)
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

    private static void AppendCapabilitySnapshotPromptBlock(StringBuilder runtimeIdentity, SessionCapabilitySnapshotDto snapshot) {
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

        if (snapshot.RoutingFamilies.Length > 0) {
            runtimeIdentity.AppendLine("routing_families: " + string.Join(", ", snapshot.RoutingFamilies));
        }

        if (snapshot.HealthyTools.Length > 0) {
            runtimeIdentity.AppendLine("healthy_tools: " + string.Join(", ", snapshot.HealthyTools));
        }

        runtimeIdentity.AppendLine("Treat this capability snapshot as the authoritative runtime tool context for this turn.");

        runtimeIdentity.AppendLine();
        runtimeIdentity.AppendLine("[Skills snapshot]");
        runtimeIdentity.AppendLine(SkillsSnapshotMarker);
        runtimeIdentity.AppendLine("skill_count: " + snapshot.Skills.Length);
        if (snapshot.Skills.Length > 0) {
            runtimeIdentity.AppendLine("skills: " + string.Join(", ", snapshot.Skills));
        }

        runtimeIdentity.AppendLine("Treat this skills snapshot as the authoritative reusable skill inventory for this turn.");
    }
}
