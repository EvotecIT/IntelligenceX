using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private readonly record struct VisualTypeCatalogEntry(
        string CanonicalType,
        bool SupportsProactiveFenceGuidance,
        string[] PreferredAliases,
        string[] FenceLanguageSignals,
        string[] InlineTokenSignals);

    private const string ProactiveVisualizationMarker = "ix:proactive-visualization:v1";
    private const string AutoVisualType = "auto";
    private const string MermaidVisualType = "mermaid";
    private const string ChartVisualType = "ix-chart";
    private const string NetworkVisualType = "ix-network";
    private const string TableVisualType = "table";
    private const string LegacyNetworkVisualType = "visnetwork";
    private const int MaxSupportedProactiveVisualBlocks = 3;

    private static readonly VisualTypeCatalogEntry[] VisualTypeCatalog = {
        new(
            CanonicalType: MermaidVisualType,
            SupportsProactiveFenceGuidance: true,
            PreferredAliases: new[] { "diagram" },
            FenceLanguageSignals: new[] { MermaidVisualType },
            InlineTokenSignals: new[] { MermaidVisualType, "diagram" }),
        new(
            CanonicalType: ChartVisualType,
            SupportsProactiveFenceGuidance: true,
            PreferredAliases: new[] { "chart" },
            FenceLanguageSignals: new[] { ChartVisualType },
            InlineTokenSignals: new[] { ChartVisualType, "chart" }),
        new(
            CanonicalType: NetworkVisualType,
            SupportsProactiveFenceGuidance: true,
            PreferredAliases: new[] { "network", LegacyNetworkVisualType },
            FenceLanguageSignals: new[] { NetworkVisualType, LegacyNetworkVisualType },
            InlineTokenSignals: new[] { NetworkVisualType, "network", LegacyNetworkVisualType }),
        new(
            CanonicalType: TableVisualType,
            SupportsProactiveFenceGuidance: false,
            PreferredAliases: new[] { "markdown-table", "markdown_table" },
            FenceLanguageSignals: Array.Empty<string>(),
            InlineTokenSignals: new[] { TableVisualType, "markdown-table", "markdown_table" })
    };

    private static readonly IReadOnlyDictionary<string, string> PreferredVisualTypeByToken = BuildPreferredVisualTypeByToken();
    private static readonly string[] ProactiveVisualPromptFenceLanguages = BuildProactiveVisualPromptFenceLanguages();
    private static readonly string[] VisualContractFenceLanguages = BuildVisualContractFenceLanguages();
    private static readonly HashSet<string> VisualContractInlineTokenSignals = BuildVisualContractInlineTokenSignals();

    private static IReadOnlyDictionary<string, string> BuildPreferredVisualTypeByToken() {
        var map = new Dictionary<string, string>(StringComparer.Ordinal) {
            [AutoVisualType] = AutoVisualType
        };

        for (var i = 0; i < VisualTypeCatalog.Length; i++) {
            var entry = VisualTypeCatalog[i];
            AddPreferredVisualTypeToken(map, entry.CanonicalType, entry.CanonicalType);

            for (var aliasIndex = 0; aliasIndex < entry.PreferredAliases.Length; aliasIndex++) {
                AddPreferredVisualTypeToken(map, entry.PreferredAliases[aliasIndex], entry.CanonicalType);
            }
        }

        return map;
    }

    private static string[] BuildProactiveVisualPromptFenceLanguages() {
        var values = new List<string>(VisualTypeCatalog.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < VisualTypeCatalog.Length; i++) {
            var entry = VisualTypeCatalog[i];
            if (!entry.SupportsProactiveFenceGuidance || !seen.Add(entry.CanonicalType)) {
                continue;
            }

            values.Add(entry.CanonicalType);
        }

        return values.Count == 0 ? Array.Empty<string>() : values.ToArray();
    }

    private static string[] BuildVisualContractFenceLanguages() {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < VisualTypeCatalog.Length; i++) {
            var entry = VisualTypeCatalog[i];
            for (var signalIndex = 0; signalIndex < entry.FenceLanguageSignals.Length; signalIndex++) {
                var signal = entry.FenceLanguageSignals[signalIndex];
                if (string.IsNullOrWhiteSpace(signal) || !seen.Add(signal)) {
                    continue;
                }

                values.Add(signal);
            }
        }

        return values.Count == 0 ? Array.Empty<string>() : values.ToArray();
    }

    private static HashSet<string> BuildVisualContractInlineTokenSignals() {
        var signals = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < VisualTypeCatalog.Length; i++) {
            var entry = VisualTypeCatalog[i];
            AddNormalizedInlineSignalToken(signals, entry.CanonicalType);

            for (var aliasIndex = 0; aliasIndex < entry.PreferredAliases.Length; aliasIndex++) {
                AddNormalizedInlineSignalToken(signals, entry.PreferredAliases[aliasIndex]);
            }

            for (var signalIndex = 0; signalIndex < entry.FenceLanguageSignals.Length; signalIndex++) {
                AddNormalizedInlineSignalToken(signals, entry.FenceLanguageSignals[signalIndex]);
            }

            for (var signalIndex = 0; signalIndex < entry.InlineTokenSignals.Length; signalIndex++) {
                AddNormalizedInlineSignalToken(signals, entry.InlineTokenSignals[signalIndex]);
            }
        }

        return signals;
    }

    private static void AddPreferredVisualTypeToken(
        IDictionary<string, string> map,
        string token,
        string canonicalType) {
        var normalizedToken = NormalizeCompactToken(token.AsSpan());
        if (normalizedToken.Length == 0 || string.IsNullOrWhiteSpace(canonicalType) || map.ContainsKey(normalizedToken)) {
            return;
        }

        map[normalizedToken] = canonicalType;
    }

    private static void AddNormalizedInlineSignalToken(ISet<string> signals, string token) {
        var normalizedToken = NormalizeCompactToken(token.AsSpan());
        if (normalizedToken.Length == 0) {
            return;
        }

        signals.Add(normalizedToken);
    }

    private static bool ContainsVisualContractSignal(string? text) {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            return false;
        }

        for (var i = 0; i < VisualContractFenceLanguages.Length; i++) {
            if (ContainsFenceLanguage(value, VisualContractFenceLanguages[i])) {
                return true;
            }
        }

        return ContainsVisualInlineTokenSignal(value);
    }

    private static string GetSupportedProactiveVisualBlockListText() {
        return string.Join("/", ProactiveVisualPromptFenceLanguages);
    }

    private static bool TryResolvePreferredVisualTypeToken(ReadOnlySpan<char> value, out string preferredVisualType) {
        preferredVisualType = string.Empty;
        var normalized = NormalizeCompactToken(value);
        if (normalized.Length == 0) {
            return false;
        }

        if (!PreferredVisualTypeByToken.TryGetValue(normalized, out var resolved) || string.IsNullOrWhiteSpace(resolved)) {
            return false;
        }

        preferredVisualType = resolved;
        return true;
    }
}
