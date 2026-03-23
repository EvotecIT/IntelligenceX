using System;
using System.Collections.Generic;
using System.Text.Json;

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
    private const string ChartVisualType = "chart";
    private const string NetworkVisualType = "network";
    private const string TableVisualType = "table";
    private const int MaxSupportedProactiveVisualBlocks = 3;
    private static readonly string[] NetworkJsonNodeAliases = new[] { "nodes", "vertices", "entities" };
    private static readonly string[] NetworkJsonEdgeAliases = new[] { "edges", "links", "relationships", "connections" };
    private static readonly string[] ChartJsonLabelAliases = new[] { "labels", "categories", "x", "x_axis" };
    private static readonly string[] ChartJsonSeriesAliases = new[] { "datasets", "series", "data", "values", "points", "metrics" };
    private static readonly string[] TableJsonRowAliases = new[] { "rows", "data", "items", "records", "entries" };
    private static readonly string[] TableJsonColumnAliases = new[] { "columns", "headers", "fields", "keys" };

    private static readonly VisualTypeCatalogEntry[] VisualTypeCatalog = {
        new(
            CanonicalType: MermaidVisualType,
            SupportsProactiveFenceGuidance: true,
            PreferredAliases: new[] { "diagram", "flowchart" },
            FenceLanguageSignals: new[] { MermaidVisualType },
            InlineTokenSignals: new[] { MermaidVisualType, "diagram" }),
        new(
            CanonicalType: ChartVisualType,
            SupportsProactiveFenceGuidance: true,
            PreferredAliases: new[] { "plot" },
            FenceLanguageSignals: new[] { ChartVisualType },
            InlineTokenSignals: new[] { ChartVisualType, "plot" }),
        new(
            CanonicalType: NetworkVisualType,
            SupportsProactiveFenceGuidance: true,
            PreferredAliases: new[] { "graph", "node-link" },
            FenceLanguageSignals: new[] { NetworkVisualType },
            InlineTokenSignals: new[] { NetworkVisualType, "graph" }),
        new(
            CanonicalType: TableVisualType,
            SupportsProactiveFenceGuidance: false,
            PreferredAliases: new[] { "markdown-table", "markdown_table", "data-table", "datatable", "grid" },
            FenceLanguageSignals: Array.Empty<string>(),
            InlineTokenSignals: new[] { TableVisualType, "markdown-table", "markdown_table" })
    };

    private static readonly IReadOnlyDictionary<string, string> PreferredVisualTypeByToken = BuildPreferredVisualTypeByToken();
    private static readonly string[] ProactiveVisualPromptFenceLanguages = BuildProactiveVisualPromptFenceLanguages();
    private static readonly string[] VisualContractFenceLanguages = BuildVisualContractFenceLanguages();
    private static readonly HashSet<string> VisualContractInlineTokenSignals = BuildVisualContractInlineTokenSignals();
    private static readonly HashSet<string> NaturalLanguageDiagramArtifactTokens = BuildNormalizedTokenSet(
        "diagram",
        "diagrama",
        "flowchart",
        "topology",
        "topologia",
        "topologii",
        "mermaid");
    private static readonly HashSet<string> NaturalLanguageChartArtifactTokens = BuildNormalizedTokenSet(
        "chart",
        "plot",
        "graf",
        "gráfico",
        "wykres",
        "wykresie");
    private static readonly HashSet<string> NaturalLanguageNetworkArtifactTokens = BuildNormalizedTokenSet(
        "network",
        "graph",
        "node-link",
        "nodelink",
        "relacje",
        "relations",
        "relationship",
        "relationships");
    private static readonly HashSet<string> NaturalLanguageTableArtifactTokens = BuildNormalizedTokenSet(
        "table",
        "markdown-table",
        "markdown_table",
        "datatable",
        "data-table",
        "grid",
        "tabela",
        "tabelka",
        "tabelki",
        "tabelke",
        "tabla");

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

    private static HashSet<string> BuildNormalizedTokenSet(params string[] values) {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (values is null || values.Length == 0) {
            return tokens;
        }

        for (var i = 0; i < values.Length; i++) {
            AddNormalizedInlineSignalToken(tokens, values[i]);
        }

        return tokens;
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

        if (TryResolvePreferredVisualTypeFromParsedJsonSignal(text, out preferredVisualType, out var parsedJsonCandidate)) {
            return true;
        }

        if (parsedJsonCandidate) {
            return false;
        }

        var value = text.AsSpan();
        if (!ContainsLikelyJsonEnvelope(value)) {
            return false;
        }

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

    private static bool TryResolvePreferredVisualTypeFromParsedJsonSignal(
        string text,
        out string preferredVisualType,
        out bool parsedJsonCandidate) {
        preferredVisualType = string.Empty;
        parsedJsonCandidate = false;
        var value = (text ?? string.Empty).Trim();
        if (value.Length < 2) {
            return false;
        }

        if (TryResolvePreferredVisualTypeFromJsonCandidate(value.AsSpan(), out preferredVisualType, out var parsedCandidate)) {
            parsedJsonCandidate = parsedCandidate;
            return true;
        }

        parsedJsonCandidate = parsedCandidate;

        var lineStart = 0;
        var content = value.AsSpan();
        while (lineStart < content.Length) {
            ReadOnlySpan<char> line;
            lineStart = ReadNextLine(content, lineStart, out line);
            if (TryResolvePreferredVisualTypeFromJsonCandidate(line.Trim(), out preferredVisualType, out parsedCandidate)) {
                parsedJsonCandidate = true;
                return true;
            }

            parsedJsonCandidate = parsedJsonCandidate || parsedCandidate;
        }

        return false;
    }

    private static bool TryResolvePreferredVisualTypeFromJsonCandidate(
        ReadOnlySpan<char> candidate,
        out string preferredVisualType,
        out bool parsedSuccessfully) {
        preferredVisualType = string.Empty;
        parsedSuccessfully = false;
        if (!LooksLikeJsonContainer(candidate)) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(candidate.ToString());
            parsedSuccessfully = true;
            return TryResolvePreferredVisualTypeFromJsonElement(document.RootElement, out preferredVisualType);
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryResolvePreferredVisualTypeFromJsonElement(
        JsonElement element,
        out string preferredVisualType) {
        preferredVisualType = string.Empty;
        if (element.ValueKind == JsonValueKind.Object) {
            if (LooksLikeNetworkJsonVisualContract(element)) {
                preferredVisualType = NetworkVisualType;
                return true;
            }

            if (LooksLikeChartJsonVisualContract(element)) {
                preferredVisualType = ChartVisualType;
                return true;
            }

            if (LooksLikeTableJsonVisualContract(element)) {
                preferredVisualType = TableVisualType;
                return true;
            }

            foreach (var property in element.EnumerateObject()) {
                if (TryResolvePreferredVisualTypeFromJsonElement(property.Value, out preferredVisualType)) {
                    return true;
                }
            }

            return false;
        }

        if (element.ValueKind != JsonValueKind.Array) {
            return false;
        }

        foreach (var item in element.EnumerateArray()) {
            if (TryResolvePreferredVisualTypeFromJsonElement(item, out preferredVisualType)) {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeNetworkJsonVisualContract(ReadOnlySpan<char> text) {
        return ContainsAnyJsonArrayProperty(text, NetworkJsonNodeAliases)
               && ContainsAnyJsonArrayProperty(text, NetworkJsonEdgeAliases);
    }

    private static bool LooksLikeNetworkJsonVisualContract(JsonElement element) {
        return HasAnyJsonArrayProperty(element, NetworkJsonNodeAliases)
               && HasAnyJsonArrayProperty(element, NetworkJsonEdgeAliases);
    }

    private static bool LooksLikeChartJsonVisualContract(ReadOnlySpan<char> text) {
        if (!ContainsAnyJsonArrayProperty(text, ChartJsonLabelAliases)) {
            return false;
        }

        return ContainsAnyJsonArrayProperty(text, ChartJsonSeriesAliases);
    }

    private static bool LooksLikeChartJsonVisualContract(JsonElement element) {
        if (!HasAnyJsonArrayProperty(element, ChartJsonLabelAliases)) {
            return false;
        }

        return HasAnyJsonArrayProperty(element, ChartJsonSeriesAliases);
    }

    private static bool LooksLikeTableJsonVisualContract(ReadOnlySpan<char> text) {
        if (!ContainsAnyJsonArrayProperty(text, TableJsonRowAliases)) {
            return false;
        }

        return ContainsAnyJsonArrayProperty(text, TableJsonColumnAliases);
    }

    private static bool LooksLikeTableJsonVisualContract(JsonElement element) {
        if (!HasAnyJsonArrayProperty(element, TableJsonRowAliases)) {
            return false;
        }

        return HasAnyJsonArrayProperty(element, TableJsonColumnAliases);
    }

    private static bool ContainsAnyJsonArrayProperty(ReadOnlySpan<char> text, string[] propertyNames) {
        if (propertyNames is null || propertyNames.Length == 0) {
            return false;
        }

        for (var i = 0; i < propertyNames.Length; i++) {
            var propertyName = propertyNames[i];
            if (ContainsJsonArrayProperty(text, propertyName)) {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyJsonArrayProperty(JsonElement element, string[] propertyNames) {
        if (propertyNames is null || propertyNames.Length == 0) {
            return false;
        }

        for (var i = 0; i < propertyNames.Length; i++) {
            var propertyName = propertyNames[i];
            if (HasJsonArrayProperty(element, propertyName)) {
                return true;
            }
        }

        return false;
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

    private static bool HasJsonArrayProperty(JsonElement element, string propertyName) {
        if (element.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyName)) {
            return false;
        }

        foreach (var property in element.EnumerateObject()) {
            if (!property.NameEquals(propertyName) && !string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.Array;
        }

        return false;
    }

}
