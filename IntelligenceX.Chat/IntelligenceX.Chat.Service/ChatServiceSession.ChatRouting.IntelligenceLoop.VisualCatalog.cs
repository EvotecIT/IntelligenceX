using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string ProactiveVisualizationMarker = "ix:proactive-visualization:v1";
    private const string MermaidFenceLanguage = "mermaid";
    private const string ChartFenceLanguage = "ix-chart";
    private const string NetworkFenceLanguage = "ix-network";
    private const string LegacyNetworkFenceLanguage = "visnetwork";
    private const int MaxSupportedProactiveVisualBlocks = 3;

    private static readonly string[] ProactiveVisualPromptFenceLanguages = {
        MermaidFenceLanguage,
        ChartFenceLanguage,
        NetworkFenceLanguage
    };

    private static readonly string[] VisualContractFenceLanguages = {
        MermaidFenceLanguage,
        ChartFenceLanguage,
        NetworkFenceLanguage,
        LegacyNetworkFenceLanguage
    };

    private static readonly string[] VisualContractTokenSignals = {
        MermaidFenceLanguage,
        ChartFenceLanguage,
        NetworkFenceLanguage,
        LegacyNetworkFenceLanguage,
        "table"
    };

    private static readonly IReadOnlyDictionary<string, string> PreferredVisualTypeByToken = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["auto"] = "auto",
        ["ixnetwork"] = "ix-network",
        ["network"] = "ix-network",
        ["visnetwork"] = "ix-network",
        ["ixchart"] = "ix-chart",
        ["chart"] = "ix-chart",
        ["mermaid"] = "mermaid",
        ["diagram"] = "mermaid",
        ["table"] = "table",
        ["markdowntable"] = "table"
    };

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

        for (var i = 0; i < VisualContractTokenSignals.Length; i++) {
            if (ContainsToken(value, VisualContractTokenSignals[i])) {
                return true;
            }
        }

        return false;
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
