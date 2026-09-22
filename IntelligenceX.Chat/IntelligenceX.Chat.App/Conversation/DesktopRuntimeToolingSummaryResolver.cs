using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Summarizes the live service-owned tooling inventory for desktop runtime prompts.
/// </summary>
internal sealed record DesktopRuntimeToolingSummary(
    string DetailedAvailability,
    string CompactAvailability,
    int EnabledPacks,
    int DisabledPacks,
    bool HasMetadata);

/// <summary>
/// Keeps native and legacy desktop shells on one service-policy tooling interpretation.
/// </summary>
internal static class DesktopRuntimeToolingSummaryResolver {
    public static DesktopRuntimeToolingSummary Resolve(
        SessionPolicyDto? sessionPolicy,
        IReadOnlyList<ToolPackInfoDto>? toolCatalogPacks = null,
        IReadOnlyList<PluginInfoDto>? toolCatalogPlugins = null,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot = null) {
        var tooling = RuntimeToolingMetadataResolver.Resolve(
            sessionPolicy,
            toolCatalogPacks,
            toolCatalogPlugins,
            toolCatalogCapabilitySnapshot);
        var enabledPacks = tooling.Packs.Count(static pack => pack.Enabled);
        var disabledPacks = tooling.Packs.Length - enabledPacks;
        var enabledPlugins = tooling.Plugins.Count(static plugin => plugin.Enabled);
        var registeredTools = tooling.CapabilitySnapshot?.RegisteredTools ?? 0;
        var hasMetadata = tooling.Packs.Length > 0
                          || tooling.Plugins.Length > 0
                          || tooling.CapabilitySnapshot is not null;
        if (!hasMetadata) {
            return new DesktopRuntimeToolingSummary(
                "unknown (the live session policy has not reported its tool inventory yet).",
                "unknown:session_policy_loading.",
                enabledPacks,
                disabledPacks,
                HasMetadata: false);
        }

        var toolingAvailable = tooling.CapabilitySnapshot?.ToolingAvailable
                               ?? (enabledPacks > 0 || enabledPlugins > 0);
        var availability = toolingAvailable ? "available" : "unavailable";
        return new DesktopRuntimeToolingSummary(
            availability
            + " (registered tools: " + registeredTools.ToString(CultureInfo.InvariantCulture)
            + ", enabled packs: " + enabledPacks.ToString(CultureInfo.InvariantCulture)
            + ", disabled packs: " + disabledPacks.ToString(CultureInfo.InvariantCulture)
            + ", enabled plugins: " + enabledPlugins.ToString(CultureInfo.InvariantCulture) + ").",
            availability
            + ":registered_tools=" + registeredTools.ToString(CultureInfo.InvariantCulture)
            + ";enabled_packs=" + enabledPacks.ToString(CultureInfo.InvariantCulture)
            + ";enabled_plugins=" + enabledPlugins.ToString(CultureInfo.InvariantCulture) + ".",
            enabledPacks,
            disabledPacks,
            HasMetadata: true);
    }
}
