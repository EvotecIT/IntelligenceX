using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryPresentationHelpers {
    public static IReadOnlyDictionary<string, string> BuildSourceRootLabels(IEnumerable<SourceRootRecord> roots) {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots ?? Array.Empty<SourceRootRecord>()) {
            if (root is null || string.IsNullOrWhiteSpace(root.Id)) {
                continue;
            }

            labels[root.Id] = BuildSourceRootLabel(root);
        }

        return labels;
    }

    public static string BuildSourceRootLabel(SourceRootRecord root) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }

        var provider = BuildProviderTitle(root.ProviderId);
        var path = root.Path ?? string.Empty;
        if (path.StartsWith("ix://", StringComparison.OrdinalIgnoreCase)) {
            return provider + " · Internal IX";
        }

        var location = path.IndexOf("Windows.old", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Windows.old"
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
        } else if (string.IsNullOrWhiteSpace(leaf)) {
            leaf = path;
        }

        return provider + " · " + location + " (" + leaf + ")";
    }

    public static string BuildProviderTitle(string? providerId) {
        return HeatmapText.NormalizeOptionalText(providerId)?.ToLowerInvariant() switch {
            "codex" => "Codex",
            "claude" => "Claude",
            "ix" => "IntelligenceX",
            "chatgpt" => "ChatGPT",
            "github" => "GitHub",
            "lmstudio" => "LM Studio",
            "ollama" => "Ollama",
            _ => string.IsNullOrWhiteSpace(providerId) ? "Unknown" : providerId!.Trim()
        };
    }

    private static string[] SplitSegments(string path) {
        return (path ?? string.Empty)
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }
}
