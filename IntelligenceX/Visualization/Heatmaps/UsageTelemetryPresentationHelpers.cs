using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryPresentationHelpers {
    public static IReadOnlyDictionary<string, string> BuildSourceRootLabels(IEnumerable<SourceRootRecord> roots) {
        var rootList = (roots ?? Array.Empty<SourceRootRecord>())
            .Where(static root => root is not null && !string.IsNullOrWhiteSpace(root.Id))
            .ToArray();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in rootList.GroupBy(BuildSourceRootLabel, StringComparer.OrdinalIgnoreCase)) {
            var entries = group.ToArray();
            if (entries.Length == 1) {
                labels[entries[0].Id] = group.Key;
                continue;
            }

            var disambiguated = entries
                .Select(root => new KeyValuePair<SourceRootRecord, string>(root, BuildSourceRootLabel(root, includePathDisambiguator: true)))
                .ToArray();
            var distinctLabels = disambiguated
                .Select(static pair => pair.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (distinctLabels == disambiguated.Length) {
                foreach (var pair in disambiguated) {
                    labels[pair.Key.Id] = pair.Value;
                }
                continue;
            }

            foreach (var pair in disambiguated) {
                labels[pair.Key.Id] = pair.Value + " [" + pair.Key.Id + "]";
            }
        }

        return labels;
    }

    public static string BuildSourceRootLabel(SourceRootRecord root) {
        return BuildSourceRootLabel(root, includePathDisambiguator: false);
    }

    private static string BuildSourceRootLabel(SourceRootRecord root, bool includePathDisambiguator) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }

        var provider = BuildProviderTitle(root.ProviderId);
        var path = root.Path ?? string.Empty;
        if (IsInternalSourceRoot(root, path)) {
            var internalScope = ResolveInternalSourceRootScope(path);
            if (includePathDisambiguator && !string.IsNullOrWhiteSpace(internalScope)) {
                return provider + " · Internal (" + internalScope + ")";
            }

            return provider + " · Internal";
        }

        var isWsl = IsWslSourceRoot(root, path);
        var location = path.IndexOf("Windows.old", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Windows.old"
            : isWsl
                ? "WSL"
                : "Current";

        var segments = SplitSegments(path);
        var leaf = segments.Length > 0 ? segments[segments.Length - 1] : path;
        var parent = segments.Length > 1 ? segments[segments.Length - 2] : string.Empty;

        if (string.Equals(leaf, "projects", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(parent, ".claude", StringComparison.OrdinalIgnoreCase)) {
            leaf = ".claude/projects";
        } else if (string.Equals(leaf, "sessions", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(parent, ".codex", StringComparison.OrdinalIgnoreCase)) {
            leaf = ".codex/sessions";
        } else if (string.Equals(leaf, "conversations", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(parent, ".lmstudio", StringComparison.OrdinalIgnoreCase)) {
            leaf = ".lmstudio/conversations";
        } else if (string.IsNullOrWhiteSpace(leaf)) {
            leaf = path;
        }

        if (isWsl) {
            var wslScope = ResolveWslSourceRootScope(root, segments);
            if (!string.IsNullOrWhiteSpace(wslScope)) {
                leaf = wslScope + "/" + leaf;
            }
        } else if (includePathDisambiguator) {
            var disambiguator = ResolveSourceRootPathDisambiguator(segments, leaf, parent);
            if (!string.IsNullOrWhiteSpace(disambiguator)) {
                leaf = disambiguator + "/" + leaf;
            }
        }

        return provider + " · " + location + " (" + leaf + ")";
    }

    public static string BuildProviderTitle(string? providerId) {
        return UsageTelemetryProviderCatalog.ResolveDisplayTitle(providerId);
    }

    private static string[] SplitSegments(string path) {
        return (path ?? string.Empty)
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    private static string? ResolveSourceRootPathDisambiguator(string[] segments, string leaf, string parent) {
        if (segments.Length == 0) {
            return null;
        }

        if (string.Equals(leaf, ".claude/projects", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(leaf, ".codex/sessions", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(leaf, ".lmstudio/conversations", StringComparison.OrdinalIgnoreCase)) {
            return segments.Length > 2 ? segments[segments.Length - 3] : parent;
        }

        return string.IsNullOrWhiteSpace(parent) ? null : parent;
    }

    private static bool IsInternalSourceRoot(SourceRootRecord root, string path) {
        if (root.SourceKind == UsageSourceKind.InternalIx) {
            return true;
        }

        return path.IndexOf("://internal/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ResolveInternalSourceRootScope(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var markerIndex = path.IndexOf("://internal/", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return null;
        }

        var startIndex = markerIndex + "://internal/".Length;
        if (startIndex >= path.Length) {
            return null;
        }

        var scope = path.Substring(startIndex).Trim('/', '\\');
        return string.IsNullOrWhiteSpace(scope) ? null : scope;
    }

    private static bool IsWslSourceRoot(SourceRootRecord root, string path) {
        if (string.Equals(root.PlatformHint, "wsl", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveWslSourceRootScope(SourceRootRecord root, string[] segments) {
        if (!string.IsNullOrWhiteSpace(root.MachineLabel)) {
            return root.MachineLabel;
        }

        if (segments.Length > 1 &&
            (string.Equals(segments[0], "wsl$", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "wsl.localhost", StringComparison.OrdinalIgnoreCase))) {
            return segments[1];
        }

        return null;
    }
}
