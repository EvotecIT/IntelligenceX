using System;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App;

internal static class RuntimeToolingMetadataResolver {
    internal static string ResolveEffectiveSource(
        SessionPolicyDto? sessionPolicy,
        ToolPackInfoDto[]? toolCatalogPacks,
        PluginInfoDto[]? toolCatalogPlugins,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot) {
        if (HasToolingSnapshotData(sessionPolicy?.CapabilitySnapshot?.ToolingSnapshot)) {
            return NormalizeSource(
                sessionPolicy!.CapabilitySnapshot!.ToolingSnapshot!.Source,
                "session_policy");
        }

        if (sessionPolicy?.Packs is { Length: > 0 } || sessionPolicy?.Plugins is { Length: > 0 }) {
            return "session_policy";
        }

        if (HasToolingSnapshotData(toolCatalogCapabilitySnapshot?.ToolingSnapshot)) {
            return NormalizeSource(
                toolCatalogCapabilitySnapshot!.ToolingSnapshot!.Source,
                "tool_catalog_preview");
        }

        if (toolCatalogPacks is { Length: > 0 } || toolCatalogPlugins is { Length: > 0 }) {
            return "tool_catalog_preview";
        }

        return "unknown";
    }

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

    private static bool HasToolingSnapshotData(SessionCapabilityToolingSnapshotDto? toolingSnapshot) {
        return toolingSnapshot?.Packs is { Length: > 0 }
               || toolingSnapshot?.Plugins is { Length: > 0 };
    }

    private static string NormalizeSource(string? source, string fallback) {
        var normalized = (source ?? string.Empty).Trim();
        return normalized.Length == 0 ? fallback : normalized;
    }
}
