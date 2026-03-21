using System;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App;

internal static class RuntimeToolingMetadataResolver {
    internal static ToolPackInfoDto[] ResolveEffectivePacks(
        SessionPolicyDto? sessionPolicy,
        ToolPackInfoDto[]? toolCatalogPacks,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot) {
        if (sessionPolicy?.CapabilitySnapshot?.ToolingSnapshot?.Packs is { Length: > 0 } sessionSnapshotPacks) {
            return sessionSnapshotPacks;
        }

        if (sessionPolicy?.Packs is { Length: > 0 } sessionPacks) {
            return sessionPacks;
        }

        if (toolCatalogCapabilitySnapshot?.ToolingSnapshot?.Packs is { Length: > 0 } previewSnapshotPacks) {
            return previewSnapshotPacks;
        }

        return toolCatalogPacks is { Length: > 0 }
            ? toolCatalogPacks
            : Array.Empty<ToolPackInfoDto>();
    }

    internal static PluginInfoDto[] ResolveEffectivePlugins(
        SessionPolicyDto? sessionPolicy,
        PluginInfoDto[]? toolCatalogPlugins,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot) {
        if (sessionPolicy?.CapabilitySnapshot?.ToolingSnapshot?.Plugins is { Length: > 0 } sessionSnapshotPlugins) {
            return sessionSnapshotPlugins;
        }

        if (sessionPolicy?.Plugins is { Length: > 0 } sessionPlugins) {
            return sessionPlugins;
        }

        if (toolCatalogCapabilitySnapshot?.ToolingSnapshot?.Plugins is { Length: > 0 } previewSnapshotPlugins) {
            return previewSnapshotPlugins;
        }

        return toolCatalogPlugins is { Length: > 0 }
            ? toolCatalogPlugins
            : Array.Empty<PluginInfoDto>();
    }
}
