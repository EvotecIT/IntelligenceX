using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Markdown;

internal static partial class ToolRunMarkdownFormatter {
    // Keep stack allocation bounded (~4 KB) while reducing encoder/hash call overhead.
    private const int DedupHashChunkChars = 1024;
    private const int DedupHashChunkMaxBytes = DedupHashChunkChars * 4;
    private const int CaseInsensitiveLookupMapColumnThreshold = 2;

    private static List<(string Language, string Content)> BuildRenderHintFences(ToolOutputDto output) {
        var fences = new List<(string Language, string Content)>();
        if (string.IsNullOrWhiteSpace(output.RenderJson)) {
            return fences;
        }

        JsonDocument? outputDoc = null;
        JsonElement outputRoot = default;
        try {
            if (!string.IsNullOrWhiteSpace(output.Output)) {
                try {
                    outputDoc = JsonDocument.Parse(output.Output);
                    if (outputDoc.RootElement.ValueKind == JsonValueKind.Object) {
                        outputRoot = outputDoc.RootElement;
                    }
                } catch (Exception ex) {
                    TraceRenderHintWarning(output.CallId, "Failed to parse tool output JSON while building render fences.", ex);
                }
            }

            JsonDocument renderDoc;
            try {
                renderDoc = JsonDocument.Parse(output.RenderJson);
            } catch (Exception ex) {
                TraceRenderHintWarning(output.CallId, "Failed to parse render hints JSON.", ex);
                return fences;
            }

            using (renderDoc) {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var renderHint in EnumerateRenderHints(renderDoc.RootElement)) {
                    try {
                        var normalizedKind = NormalizeRenderKind(ReadStringProperty(renderHint, "kind"));
                        if (normalizedKind.Length == 0) {
                            continue;
                        }

                        if (string.Equals(normalizedKind, "table", StringComparison.Ordinal)) {
                            if (!TryBuildDataViewPayload(
                                    renderHint,
                                    outputRoot,
                                    output.CallId ?? string.Empty,
                                    out var dataViewPayloadJson)) {
                                continue;
                            }

                            var dedupeKey = BuildRenderHintDeduplicationKey(DataViewPayloadFenceLanguage, dataViewPayloadJson);
                            if (seen.Add(dedupeKey)) {
                                fences.Add((DataViewPayloadFenceLanguage, dataViewPayloadJson));
                            }
                            continue;
                        }

                        if (!TryBuildCodeRenderFence(
                                renderHint,
                                normalizedKind,
                                outputRoot,
                                out var language,
                                out var content)) {
                            continue;
                        }

                        var key = BuildRenderHintDeduplicationKey(language, content);
                        if (seen.Add(key)) {
                            fences.Add((language, content));
                        }
                    } catch (Exception ex) {
                        TraceRenderHintWarning(output.CallId, "Failed to process a render hint entry.", ex);
                    }
                }
            }

            return fences;
        } finally {
            outputDoc?.Dispose();
        }
    }

    private static List<(string Language, string Content)> ExtractFirstPartyVisualFences(
        IReadOnlyList<(string Language, string Content)> fences) {
        var selected = new List<(string Language, string Content)>();
        for (var i = 0; i < fences.Count; i++) {
            var fence = fences[i];
            var language = (fence.Language ?? string.Empty).Trim().ToLowerInvariant();
            if (language.Length == 0) {
                continue;
            }

            if (string.Equals(language, "mermaid", StringComparison.Ordinal)
                || string.Equals(language, ChartFenceLanguage, StringComparison.Ordinal)
                || string.Equals(language, NetworkFenceLanguage, StringComparison.Ordinal)
                || string.Equals(language, DataViewPayloadFenceLanguage, StringComparison.Ordinal)) {
                selected.Add(fence);
            }
        }

        return selected;
    }

    private static IEnumerable<JsonElement> EnumerateRenderHints(JsonElement renderRoot) {
        if (renderRoot.ValueKind == JsonValueKind.Object) {
            yield return renderRoot;
            yield break;
        }

        if (renderRoot.ValueKind != JsonValueKind.Array) {
            yield break;
        }

        foreach (var item in renderRoot.EnumerateArray()) {
            if (item.ValueKind == JsonValueKind.Object) {
                yield return item;
            }
        }
    }

    private static string NormalizeRenderKind(string kind) {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "chart" => ChartFenceLanguage,
            "ix-chart" => ChartFenceLanguage,
            "network" => NetworkFenceLanguage,
            "ix-network" => NetworkFenceLanguage,
            "visnetwork" => NetworkFenceLanguage,
            "dataview" => DataViewPayloadFenceLanguage,
            "ix-dataview" => DataViewPayloadFenceLanguage,
            _ => normalized
        };
    }

    private static string NormalizeRenderFenceLanguage(string language, string normalizedKind) {
        var normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedLanguage.Length > 0) {
            if (string.Equals(normalizedLanguage, "chart", StringComparison.Ordinal)
                || string.Equals(normalizedLanguage, "ix-chart", StringComparison.Ordinal)) {
                return ChartFenceLanguage;
            }

            if (string.Equals(normalizedLanguage, "network", StringComparison.Ordinal)
                || string.Equals(normalizedLanguage, "ix-network", StringComparison.Ordinal)
                || string.Equals(normalizedLanguage, "visnetwork", StringComparison.Ordinal)) {
                return NetworkFenceLanguage;
            }

            if (string.Equals(normalizedLanguage, "dataview", StringComparison.Ordinal)
                || string.Equals(normalizedLanguage, "ix-dataview", StringComparison.Ordinal)) {
                return DataViewPayloadFenceLanguage;
            }

            return normalizedLanguage;
        }

        if (string.Equals(normalizedKind, ChartFenceLanguage, StringComparison.Ordinal)
            || string.Equals(normalizedKind, NetworkFenceLanguage, StringComparison.Ordinal)
            || string.Equals(normalizedKind, "mermaid", StringComparison.Ordinal)) {
            return normalizedKind;
        }

        return "text";
    }

    private static bool TryBuildCodeRenderFence(
        JsonElement render,
        string normalizedKind,
        JsonElement outputRoot,
        out string language,
        out string content) {
        language = string.Empty;
        content = string.Empty;

        if (!string.Equals(normalizedKind, "code", StringComparison.Ordinal)
            && !string.Equals(normalizedKind, "mermaid", StringComparison.Ordinal)
            && !string.Equals(normalizedKind, ChartFenceLanguage, StringComparison.Ordinal)
            && !string.Equals(normalizedKind, NetworkFenceLanguage, StringComparison.Ordinal)
            && !string.Equals(normalizedKind, DataViewPayloadFenceLanguage, StringComparison.Ordinal)) {
            return false;
        }

        language = NormalizeRenderFenceLanguage(ReadStringProperty(render, "language"), normalizedKind);
        if (language.Length == 0) {
            return false;
        }

        if (TryGetPropertyValueCaseInsensitive(render, "content", out var inlineContentNode)
            && inlineContentNode.ValueKind != JsonValueKind.Null
            && inlineContentNode.ValueKind != JsonValueKind.Undefined) {
            content = inlineContentNode.ValueKind == JsonValueKind.String
                ? (inlineContentNode.GetString() ?? string.Empty)
                : inlineContentNode.GetRawText();
            return !string.IsNullOrWhiteSpace(content);
        }

        var contentPath = ReadStringProperty(render, "content_path");
        if (string.IsNullOrWhiteSpace(contentPath)) {
            return false;
        }

        if (outputRoot.ValueKind != JsonValueKind.Object
            || !TryResolvePath(outputRoot, contentPath, out var contentNode)) {
            return false;
        }

        content = contentNode.ValueKind == JsonValueKind.String
            ? (contentNode.GetString() ?? string.Empty)
            : contentNode.GetRawText();
        return !string.IsNullOrWhiteSpace(content);
    }

    private static string BuildRenderHintDeduplicationKey(string language, string content) {
        var normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        var value = content ?? string.Empty;
        var hash = ComputeUtf8Sha256Hex(value);
        return normalizedLanguage + ":" + hash + ":" + value.Length;
    }

    internal static string ComputeUtf8Sha256Hex(string value) {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var encoder = Encoding.UTF8.GetEncoder();
        Span<byte> buffer = stackalloc byte[DedupHashChunkMaxBytes];
        var offset = 0;
        while (offset < value.Length) {
            var chunkChars = Math.Min(DedupHashChunkChars, value.Length - offset);
            var span = value.AsSpan(offset, chunkChars);
            var flush = offset + chunkChars >= value.Length;
            encoder.Convert(span, buffer, flush, out var charsUsed, out var bytesUsed, out _);
            if (bytesUsed > 0) {
                incremental.AppendData(buffer[..bytesUsed]);
            }
            offset += charsUsed;
        }

        return Convert.ToHexString(incremental.GetHashAndReset());
    }

    private static void TraceRenderHintWarning(string? callId, string message, Exception ex) {
        var id = string.IsNullOrWhiteSpace(callId) ? "<unknown>" : callId.Trim();
        Trace.TraceWarning(
            $"Tool render hint warning (call_id={id}): {message} ({ex.GetType().Name}: {ex.Message})");
    }

    private static bool TryBuildDataViewPayload(
        JsonElement render,
        JsonElement outputRoot,
        string callId,
        out string payloadJson) {
        payloadJson = string.Empty;
        if (outputRoot.ValueKind != JsonValueKind.Object) {
            return false;
        }

        var rowsPath = ReadStringProperty(render, "rows_path");
        if (string.IsNullOrWhiteSpace(rowsPath)) {
            return false;
        }

        if (!TryResolvePath(outputRoot, rowsPath, out var rowsNode) || rowsNode.ValueKind != JsonValueKind.Array) {
            return false;
        }

        var columns = ReadRenderColumns(render);
        if (columns.Count == 0) {
            columns = InferColumnsFromRows(rowsNode);
        }
        if (columns.Count == 0) {
            return false;
        }

        var matrix = BuildRowsMatrix(rowsNode, columns);
        if (matrix.Length == 0) {
            return false;
        }

        var payload = new Dictionary<string, object?> {
            ["kind"] = DataViewPayloadKind,
            ["call_id"] = callId,
            ["rows"] = matrix
        };
        payloadJson = JsonSerializer.Serialize(payload);
        return true;
    }

    private static string[][] BuildRowsMatrix(JsonElement rowsNode, IReadOnlyList<(string Key, string Label)> columns) {
        var header = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++) {
            var label = columns[i].Label;
            header[i] = string.IsNullOrWhiteSpace(label) ? columns[i].Key : label.Trim();
        }

        var result = new List<string[]> {
            header
        };

        foreach (var rowNode in rowsNode.EnumerateArray()) {
            var row = new string[columns.Count];
            Array.Fill(row, string.Empty);
            switch (rowNode.ValueKind) {
                case JsonValueKind.Object:
                    var propertyMap = columns.Count >= CaseInsensitiveLookupMapColumnThreshold
                        ? BuildCaseInsensitivePropertyMap(rowNode)
                        : null;
                    for (var i = 0; i < columns.Count; i++) {
                        row[i] = TryGetPropertyValueCaseInsensitive(rowNode, columns[i].Key, out var valueNode, propertyMap)
                            ? FormatJsonElement(valueNode)
                            : string.Empty;
                    }
                    break;
                case JsonValueKind.Array: {
                        var i = 0;
                        foreach (var cell in rowNode.EnumerateArray()) {
                            if (i >= row.Length) {
                                break;
                            }

                            row[i] = FormatJsonElement(cell);
                            i++;
                        }
                        break;
                    }
                default:
                    if (row.Length > 0) {
                        row[0] = FormatJsonElement(rowNode);
                    }
                    break;
            }

            result.Add(row);
        }

        return result.ToArray();
    }

    private static List<(string Key, string Label)> ReadRenderColumns(JsonElement render) {
        var columns = new List<(string Key, string Label)>();
        if (!TryGetPropertyValueCaseInsensitive(render, "columns", out var columnsNode) || columnsNode.ValueKind != JsonValueKind.Array) {
            return columns;
        }

        foreach (var item in columnsNode.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) {
                continue;
            }

            var key = ReadStringProperty(item, "key");
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            var label = ReadStringProperty(item, "label");
            columns.Add((key.Trim(), string.IsNullOrWhiteSpace(label) ? key.Trim() : label.Trim()));
        }

        return columns;
    }

    private static List<(string Key, string Label)> InferColumnsFromRows(JsonElement rowsNode) {
        var columns = new List<(string Key, string Label)>();
        foreach (var rowNode in rowsNode.EnumerateArray()) {
            if (rowNode.ValueKind != JsonValueKind.Object) {
                continue;
            }

            foreach (var prop in rowNode.EnumerateObject()) {
                var key = (prop.Name ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                columns.Add((key, key));
            }
            break;
        }

        return columns;
    }

    private static bool TryResolvePath(JsonElement root, string path, out JsonElement node) {
        node = root;
        var normalized = (path ?? string.Empty).Trim().Trim('/');
        if (normalized.Length == 0) {
            return false;
        }

        var segments = normalized.Split(new[] { '/', '.' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) {
            return false;
        }

        var current = root;
        for (var i = 0; i < segments.Length; i++) {
            if (current.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!TryGetPropertyValueCaseInsensitive(current, segments[i], out var next)) {
                return false;
            }

            current = next;
        }

        node = current;
        return true;
    }

    private static bool TryGetPropertyValueCaseInsensitive(JsonElement obj, string propertyName, out JsonElement value) {
        return TryGetPropertyValueCaseInsensitive(obj, propertyName, out value, propertyMap: null);
    }

    private static bool TryGetPropertyValueCaseInsensitive(
        JsonElement obj,
        string propertyName,
        out JsonElement value,
        IReadOnlyDictionary<string, JsonElement>? propertyMap = null) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyName)) {
            return false;
        }

        if (obj.TryGetProperty(propertyName, out value)) {
            return true;
        }

        if (propertyMap is not null && propertyMap.TryGetValue(propertyName, out value)) {
            return true;
        }

        foreach (var prop in obj.EnumerateObject()) {
            if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            value = prop.Value;
            return true;
        }

        return false;
    }

    private static Dictionary<string, JsonElement> BuildCaseInsensitivePropertyMap(JsonElement obj) {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (obj.ValueKind != JsonValueKind.Object) {
            return map;
        }

        foreach (var prop in obj.EnumerateObject()) {
            map.TryAdd(prop.Name, prop.Value);
        }

        return map;
    }

    private static string ReadStringProperty(JsonElement obj, string propertyName) {
        if (!TryGetPropertyValueCaseInsensitive(obj, propertyName, out var valueNode)) {
            return string.Empty;
        }

        return valueNode.ValueKind == JsonValueKind.String
            ? (valueNode.GetString() ?? string.Empty)
            : valueNode.GetRawText();
    }

    private static string FormatJsonElement(JsonElement valueNode) {
        return valueNode.ValueKind switch {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String => valueNode.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => valueNode.GetRawText(),
            _ => valueNode.GetRawText()
        };
    }
}
