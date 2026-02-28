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
        return TryResolvePreferredVisualTypeFromVisualContractSignal(text, out _);
    }

    private static bool TryResolvePreferredVisualTypeFromVisualContractSignal(
        string? text,
        out string preferredVisualType) {
        preferredVisualType = string.Empty;
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            return false;
        }

        var content = value.AsSpan();
        var lineStart = 0;
        while (lineStart < content.Length) {
            var lineEnd = content.Slice(lineStart).IndexOf('\n');
            if (lineEnd < 0) {
                lineEnd = content.Length - lineStart;
            }

            var line = content.Slice(lineStart, lineEnd).TrimStart();
            if (TryGetFenceLanguage(line, out var fenceLanguage)) {
                if (TryResolvePreferredVisualTypeToken(fenceLanguage, out preferredVisualType)) {
                    return true;
                }

                if (ContainsVisualContractFenceLanguage(fenceLanguage)) {
                    return true;
                }
            }

            lineStart += lineEnd + 1;
        }

        if (TryResolvePreferredVisualTypeFromInlineTokenSignal(value, out preferredVisualType)) {
            return true;
        }

        if (TryResolvePreferredVisualTypeFromMarkdownTableSignal(value, out preferredVisualType)) {
            return true;
        }

        if (TryResolvePreferredVisualTypeFromStructuredJsonSignal(value, out preferredVisualType)) {
            return true;
        }

        return false;
    }

    private static bool TryResolvePreferredVisualTypeFromMarkdownTableSignal(
        string text,
        out string preferredVisualType) {
        preferredVisualType = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        if (!ContainsMarkdownTableContractSignal(text.AsSpan())) {
            return false;
        }

        preferredVisualType = TableVisualType;
        return true;
    }

    private static bool TryResolvePreferredVisualTypeFromStructuredJsonSignal(
        string text,
        out string preferredVisualType) {
        preferredVisualType = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var value = text.AsSpan();
        if (LooksLikeNetworkJsonVisualContract(value)) {
            preferredVisualType = NetworkVisualType;
            return true;
        }

        if (LooksLikeChartJsonVisualContract(value)) {
            preferredVisualType = ChartVisualType;
            return true;
        }

        if (LooksLikeTableJsonVisualContract(value)) {
            preferredVisualType = TableVisualType;
            return true;
        }

        return false;
    }

    private static bool LooksLikeNetworkJsonVisualContract(ReadOnlySpan<char> text) {
        return ContainsJsonArrayProperty(text, "nodes") && ContainsJsonArrayProperty(text, "edges");
    }

    private static bool LooksLikeChartJsonVisualContract(ReadOnlySpan<char> text) {
        if (!ContainsJsonArrayProperty(text, "labels")) {
            return false;
        }

        return ContainsJsonArrayProperty(text, "datasets") || ContainsJsonArrayProperty(text, "series");
    }

    private static bool LooksLikeTableJsonVisualContract(ReadOnlySpan<char> text) {
        if (!ContainsJsonArrayProperty(text, "rows")) {
            return false;
        }

        return ContainsJsonArrayProperty(text, "columns")
               || ContainsJsonArrayProperty(text, "headers")
               || ContainsJsonArrayProperty(text, "fields");
    }

    private static bool ContainsJsonArrayProperty(ReadOnlySpan<char> text, string propertyName) {
        if (text.IsEmpty || string.IsNullOrWhiteSpace(propertyName)) {
            return false;
        }

        var token = $"\"{propertyName}\"".AsSpan();
        var searchStart = 0;
        while (searchStart < text.Length) {
            var tokenIndex = text.Slice(searchStart).IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (tokenIndex < 0) {
                return false;
            }

            var propertyIndex = searchStart + tokenIndex;
            var valueIndex = propertyIndex + token.Length;
            while (valueIndex < text.Length && char.IsWhiteSpace(text[valueIndex])) {
                valueIndex++;
            }

            if (valueIndex >= text.Length || text[valueIndex] != ':') {
                searchStart = propertyIndex + token.Length;
                continue;
            }

            valueIndex++;
            while (valueIndex < text.Length && char.IsWhiteSpace(text[valueIndex])) {
                valueIndex++;
            }

            if (valueIndex < text.Length && text[valueIndex] == '[') {
                return true;
            }

            searchStart = propertyIndex + token.Length;
        }

        return false;
    }

    private static bool ContainsMarkdownTableContractSignal(ReadOnlySpan<char> text) {
        var lineStart = 0;
        while (lineStart < text.Length) {
            ReadOnlySpan<char> headerLine;
            lineStart = ReadNextLine(text, lineStart, out headerLine);
            if (!LooksLikeMarkdownTableHeaderRow(headerLine)) {
                continue;
            }

            if (lineStart >= text.Length) {
                return false;
            }

            ReadOnlySpan<char> separatorLine;
            _ = ReadNextLine(text, lineStart, out separatorLine);
            if (LooksLikeMarkdownTableSeparatorRow(separatorLine)) {
                return true;
            }
        }

        return false;
    }

    private static int ReadNextLine(ReadOnlySpan<char> text, int startIndex, out ReadOnlySpan<char> line) {
        if (startIndex >= text.Length) {
            line = ReadOnlySpan<char>.Empty;
            return text.Length;
        }

        var remaining = text.Slice(startIndex);
        var lineBreakIndex = remaining.IndexOfAny('\r', '\n');
        if (lineBreakIndex < 0) {
            line = remaining;
            return text.Length;
        }

        line = remaining.Slice(0, lineBreakIndex);
        var nextIndex = startIndex + lineBreakIndex + 1;
        if (nextIndex < text.Length && text[startIndex + lineBreakIndex] == '\r' && text[nextIndex] == '\n') {
            nextIndex++;
        }

        return nextIndex;
    }

    private static bool LooksLikeMarkdownTableHeaderRow(ReadOnlySpan<char> line) {
        var trimmed = line.Trim();
        if (trimmed.IsEmpty || trimmed.IndexOf('|') < 0) {
            return false;
        }

        var cellCount = 0;
        var hasTextualCell = false;
        var parts = trimmed.ToString().Split('|', StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++) {
            var cell = parts[i].Trim();
            if (cell.Length == 0) {
                continue;
            }

            cellCount++;
            if (ContainsLetterOrDigit(cell.AsSpan())) {
                hasTextualCell = true;
            }
        }

        return cellCount >= 2 && hasTextualCell;
    }

    private static bool LooksLikeMarkdownTableSeparatorRow(ReadOnlySpan<char> line) {
        var trimmed = line.Trim();
        if (trimmed.IsEmpty || trimmed.IndexOf('|') < 0) {
            return false;
        }

        var separatorCellCount = 0;
        var parts = trimmed.ToString().Split('|', StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++) {
            var cell = parts[i].Trim();
            if (cell.Length == 0) {
                continue;
            }

            var dashCount = 0;
            for (var j = 0; j < cell.Length; j++) {
                var ch = cell[j];
                if (ch == '-') {
                    dashCount++;
                    continue;
                }

                if (ch == ':') {
                    continue;
                }

                return false;
            }

            if (dashCount < 3) {
                return false;
            }

            separatorCellCount++;
        }

        return separatorCellCount >= 2;
    }

    private static bool ContainsLetterOrDigit(ReadOnlySpan<char> value) {
        for (var i = 0; i < value.Length; i++) {
            if (char.IsLetterOrDigit(value[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsVisualContractFenceLanguage(ReadOnlySpan<char> language) {
        if (language.IsEmpty) {
            return false;
        }

        for (var i = 0; i < VisualContractFenceLanguages.Length; i++) {
            if (language.Equals(VisualContractFenceLanguages[i].AsSpan(), StringComparison.OrdinalIgnoreCase)) {
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
