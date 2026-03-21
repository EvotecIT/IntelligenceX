using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.App;

internal static class RuntimeToolingSupportSnapshotBuilder {
    public static RuntimeToolingSupportSnapshot? Build(
        SessionPolicyDto? sessionPolicy,
        IReadOnlyList<ToolPackInfoDto>? toolCatalogPacks,
        IReadOnlyList<PluginInfoDto>? toolCatalogPlugins,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot = null) {
        var packs = RuntimeToolingMetadataResolver.ResolveEffectivePacks(
            sessionPolicy,
            toolCatalogPacks as ToolPackInfoDto[] ?? toolCatalogPacks?.ToArray(),
            toolCatalogCapabilitySnapshot);
        var plugins = RuntimeToolingMetadataResolver.ResolveEffectivePlugins(
            sessionPolicy,
            toolCatalogPlugins as PluginInfoDto[] ?? toolCatalogPlugins?.ToArray(),
            toolCatalogCapabilitySnapshot);
        if (packs.Length == 0 && plugins.Length == 0) {
            return null;
        }

        var source = sessionPolicy is not null
            ? "session_policy"
            : "tool_catalog_preview";
        var packSnapshots = new List<RuntimeToolingPackSnapshot>(packs.Length);
        for (var i = 0; i < packs.Length; i++) {
            var pack = packs[i];
            var normalizedPackId = NormalizeRuntimeToken(pack.Id);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            packSnapshots.Add(new RuntimeToolingPackSnapshot {
                Id = normalizedPackId,
                Name = ToolPackMetadataNormalizer.ResolveDisplayName(normalizedPackId, pack.Name),
                Enabled = pack.Enabled,
                DisabledReason = NormalizeOptionalValue(pack.DisabledReason),
                IsDangerous = pack.IsDangerous,
                SourceKind = NormalizeSourceKind(pack.SourceKind),
                Category = NormalizeOptionalValue(pack.Category),
                EngineId = NormalizeOptionalValue(pack.EngineId),
                CapabilityTags = NormalizeStringArray(pack.CapabilityTags)
            });
        }

        packSnapshots.Sort(static (a, b) => {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) {
                return byName;
            }

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        var pluginSnapshots = new List<RuntimeToolingPluginSnapshot>(plugins.Length);
        for (var i = 0; i < plugins.Length; i++) {
            var plugin = plugins[i];
            var normalizedPluginId = NormalizeRuntimeToken(plugin.Id);
            if (normalizedPluginId.Length == 0) {
                continue;
            }

            pluginSnapshots.Add(new RuntimeToolingPluginSnapshot {
                Id = normalizedPluginId,
                Name = ResolvePluginDisplayName(plugin, normalizedPluginId),
                Enabled = plugin.Enabled,
                DefaultEnabled = plugin.DefaultEnabled,
                DisabledReason = NormalizeOptionalValue(plugin.DisabledReason),
                IsDangerous = plugin.IsDangerous,
                Origin = NormalizeOptionalValue(plugin.Origin),
                SourceKind = NormalizeSourceKind(plugin.SourceKind),
                Version = NormalizeOptionalValue(plugin.Version),
                RootPath = NormalizeOptionalValue(plugin.RootPath),
                PackIds = NormalizeStringArray(plugin.PackIds),
                SkillIds = NormalizeStringArray(plugin.SkillIds)
            });
        }

        pluginSnapshots.Sort(static (a, b) => {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) {
                return byName;
            }

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        return new RuntimeToolingSupportSnapshot {
            Source = source,
            PackCount = packSnapshots.Count,
            PluginCount = pluginSnapshots.Count,
            Packs = packSnapshots,
            Plugins = pluginSnapshots
        };
    }

    public static string BuildClipboardText(string startupLogText, RuntimeToolingSupportSnapshot? snapshot) {
        var normalizedStartupLogText = startupLogText ?? string.Empty;
        if (snapshot is null) {
            return normalizedStartupLogText;
        }

        var builder = new StringBuilder(normalizedStartupLogText.Length + 1024);
        builder.Append(normalizedStartupLogText.TrimEnd());
        if (builder.Length > 0) {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.AppendLine("==== Runtime Tooling Snapshot ====");
        builder.AppendLine("source: " + snapshot.Source);
        builder.AppendLine("packs: " + snapshot.PackCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.AppendLine("plugins: " + snapshot.PluginCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (snapshot.Packs.Count > 0) {
            builder.AppendLine("pack details:");
            for (var i = 0; i < snapshot.Packs.Count; i++) {
                var pack = snapshot.Packs[i];
                var parts = new List<string>(5) {
                    pack.Enabled ? "enabled" : "disabled",
                    "source=" + pack.SourceKind
                };
                if (!string.IsNullOrWhiteSpace(pack.EngineId)) {
                    parts.Add("engine=" + pack.EngineId);
                }
                if (pack.IsDangerous) {
                    parts.Add("dangerous=yes");
                }
                if (pack.CapabilityTags.Count > 0) {
                    parts.Add("capabilities=" + string.Join("/", pack.CapabilityTags));
                }
                if (!string.IsNullOrWhiteSpace(pack.DisabledReason)) {
                    parts.Add("reason=" + pack.DisabledReason);
                }

                builder.AppendLine("- " + FormatLabel(pack.Name, pack.Id) + ": " + string.Join(", ", parts));
            }
        }

        if (snapshot.Plugins.Count > 0) {
            builder.AppendLine("plugin details:");
            for (var i = 0; i < snapshot.Plugins.Count; i++) {
                var plugin = snapshot.Plugins[i];
                var parts = new List<string>(7) {
                    plugin.Enabled ? "enabled" : "disabled",
                    "default=" + (plugin.DefaultEnabled ? "enabled" : "disabled"),
                    "origin=" + (plugin.Origin ?? "unknown"),
                    "source=" + plugin.SourceKind
                };
                if (!string.IsNullOrWhiteSpace(plugin.Version)) {
                    parts.Add("version=" + plugin.Version);
                }
                if (!string.IsNullOrWhiteSpace(plugin.RootPath)) {
                    parts.Add("root=" + plugin.RootPath);
                }
                if (plugin.IsDangerous) {
                    parts.Add("dangerous=yes");
                }
                if (plugin.PackIds.Count > 0) {
                    parts.Add("packs=" + string.Join("/", plugin.PackIds));
                }
                if (plugin.SkillIds.Count > 0) {
                    parts.Add("skills=" + string.Join("/", plugin.SkillIds));
                }
                if (!string.IsNullOrWhiteSpace(plugin.DisabledReason)) {
                    parts.Add("reason=" + plugin.DisabledReason);
                }

                builder.AppendLine("- " + FormatLabel(plugin.Name, plugin.Id) + ": " + string.Join(", ", parts));
            }
        }

        return builder.ToString();
    }
    private static string ResolvePluginDisplayName(PluginInfoDto plugin, string normalizedPluginId) {
        var displayName = (plugin.Name ?? string.Empty).Trim();
        if (displayName.Length > 0) {
            return displayName;
        }

        return ToolPackMetadataNormalizer.ResolveDisplayName(normalizedPluginId, fallbackName: plugin.Id);
    }

    private static string NormalizeRuntimeToken(string? value) {
        return (value ?? string.Empty).Trim();
    }

    private static string? NormalizeOptionalValue(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeSourceKind(ToolPackSourceKind sourceKind) {
        return sourceKind switch {
            ToolPackSourceKind.Builtin => "builtin",
            ToolPackSourceKind.ClosedSource => "closed_source",
            _ => "open_source"
        };
    }

    private static List<string> NormalizeStringArray(string[]? values) {
        if (values is not { Length: > 0 }) {
            return new List<string>();
        }

        var list = new List<string>(values.Length);
        for (var i = 0; i < values.Length; i++) {
            var normalized = NormalizeRuntimeToken(values[i]);
            if (normalized.Length == 0 || ContainsIgnoreCase(list, normalized)) {
                continue;
            }

            list.Add(normalized);
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static bool ContainsIgnoreCase(List<string> values, string candidate) {
        for (var i = 0; i < values.Count; i++) {
            if (string.Equals(values[i], candidate, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string FormatLabel(string? displayName, string? id) {
        var normalizedName = NormalizeRuntimeToken(displayName);
        var normalizedId = NormalizeRuntimeToken(id);
        if (normalizedName.Length == 0) {
            return normalizedId;
        }

        return normalizedId.Length > 0 && !normalizedName.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)
            ? normalizedName + " [" + normalizedId + "]"
            : normalizedName;
    }
}

internal sealed class RuntimeToolingSupportSnapshot {
    public string Source { get; set; } = string.Empty;
    public int PackCount { get; set; }
    public int PluginCount { get; set; }
    public List<RuntimeToolingPackSnapshot> Packs { get; set; } = new();
    public List<RuntimeToolingPluginSnapshot> Plugins { get; set; } = new();
}

internal sealed class RuntimeToolingPackSnapshot {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? DisabledReason { get; set; }
    public bool IsDangerous { get; set; }
    public string SourceKind { get; set; } = "open_source";
    public string? Category { get; set; }
    public string? EngineId { get; set; }
    public List<string> CapabilityTags { get; set; } = new();
}

internal sealed class RuntimeToolingPluginSnapshot {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool DefaultEnabled { get; set; }
    public string? DisabledReason { get; set; }
    public bool IsDangerous { get; set; }
    public string? Origin { get; set; }
    public string SourceKind { get; set; } = "open_source";
    public string? Version { get; set; }
    public string? RootPath { get; set; }
    public List<string> PackIds { get; set; } = new();
    public List<string> SkillIds { get; set; } = new();
}
