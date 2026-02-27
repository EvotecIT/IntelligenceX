using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Formats structured tool-run envelopes into transcript markdown.
/// </summary>
internal static class ToolRunMarkdownFormatter {
    private const string DataViewPayloadFenceLanguage = "ix-dataview";
    private const string DataViewPayloadKind = "ix_tool_dataview_v1";
    private const string ChartFenceLanguage = "ix-chart";
    private const string NetworkFenceLanguage = "ix-network";

    /// <summary>
    /// Builds markdown for tool calls and outputs.
    /// </summary>
    /// <param name="tools">Tool run payload.</param>
    /// <param name="resolveToolDisplayName">Display-name resolver callback.</param>
    /// <returns>Markdown summary for transcript.</returns>
    public static string Format(ToolRunDto tools, Func<string?, string> resolveToolDisplayName) {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resolveToolDisplayName);

        var markdown = new MarkdownComposer()
            .Paragraph("**Tool outputs:**")
            .BlankLine();

        var namesByCallId = BuildToolNamesByCallId(tools, resolveToolDisplayName);

        foreach (var output in tools.Outputs) {
            var toolLabel = ResolveToolLabel(namesByCallId, output.CallId);
            var hasError = !string.IsNullOrWhiteSpace(output.Error) || !string.IsNullOrWhiteSpace(output.ErrorCode) || output.Ok == false;

            markdown.Heading(toolLabel, 4);
            if (hasError) {
                AppendFailureDescriptor(markdown, output);
            }

            var renderHintFences = BuildRenderHintFences(output);
            AppendCodeFences(markdown, renderHintFences);

            var summary = NormalizeSummaryMarkdown(output.SummaryMarkdown, toolLabel);
            if (ShouldIncludeSummary(summary, hasError)) {
                markdown.Raw(summary);
            } else if (!hasError && renderHintFences.Count == 0) {
                markdown.Paragraph("completed");
            }

            markdown.BlankLine();
        }

        return markdown.Build();
    }

    /// <summary>
    /// Builds markdown containing first-party visual fences only.
    /// </summary>
    /// <param name="tools">Tool run payload.</param>
    /// <param name="resolveToolDisplayName">Display-name resolver callback.</param>
    /// <returns>Markdown containing visual fences or empty when none are available.</returns>
    public static string FormatVisualsOnly(ToolRunDto tools, Func<string?, string> resolveToolDisplayName) {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resolveToolDisplayName);

        var markdown = new MarkdownComposer()
            .Paragraph("**Tool visuals:**")
            .BlankLine();

        var namesByCallId = BuildToolNamesByCallId(tools, resolveToolDisplayName);
        var visualGroups = BuildVisualFenceGroupsByCallId(tools.Outputs);
        if (visualGroups.Count == 0) {
            return string.Empty;
        }

        foreach (var visualGroup in visualGroups) {
            var toolLabel = ResolveToolLabel(namesByCallId, visualGroup.CallId);
            markdown.Heading(toolLabel, 4);
            AppendCodeFences(markdown, visualGroup.Fences);
            markdown.BlankLine();
        }

        return markdown.Build();
    }

    private static Dictionary<string, string> BuildToolNamesByCallId(
        ToolRunDto tools,
        Func<string?, string> resolveToolDisplayName) {
        var namesByCallId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var call in tools.Calls) {
            namesByCallId[call.CallId] = resolveToolDisplayName(call.Name);
        }

        return namesByCallId;
    }

    private static string ResolveToolLabel(IReadOnlyDictionary<string, string> namesByCallId, string? callId) {
        if (callId is not null
            && namesByCallId.TryGetValue(callId, out var name)) {
            return name;
        }

        return "Call " + callId;
    }

    private static void AppendCodeFences(
        MarkdownComposer markdown,
        IReadOnlyList<(string Language, string Content)> fences) {
        for (var i = 0; i < fences.Count; i++) {
            var fence = fences[i];
            markdown.CodeFence(fence.Language, fence.Content);
        }
    }

    private static List<VisualFenceGroup> BuildVisualFenceGroupsByCallId(IReadOnlyList<ToolOutputDto> outputs) {
        var groups = new List<VisualFenceGroup>();
        var groupsByCallId = new Dictionary<string, VisualFenceGroup>(StringComparer.Ordinal);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var visualFences = ExtractFirstPartyVisualFences(BuildRenderHintFences(output));
            if (visualFences.Count == 0) {
                continue;
            }

            var groupKey = NormalizeCallId(output.CallId);
            if (!groupsByCallId.TryGetValue(groupKey, out var group)) {
                group = new VisualFenceGroup(groupKey);
                groupsByCallId[groupKey] = group;
                groups.Add(group);
            }

            for (var j = 0; j < visualFences.Count; j++) {
                var fence = visualFences[j];
                var dedupeKey = BuildRenderHintDeduplicationKey(fence.Language, fence.Content);
                if (group.SeenFences.Add(dedupeKey)) {
                    group.Fences.Add(fence);
                }
            }
        }

        return groups;
    }

    private static string NormalizeCallId(string? callId) {
        return callId ?? string.Empty;
    }

    private static bool ShouldIncludeSummary(string summary, bool hasError) {
        if (string.IsNullOrWhiteSpace(summary)) {
            return false;
        }

        if (!hasError) {
            return true;
        }

        // In error cases, only keep summaries that add real diagnostic value.
        var usefulLines = 0;
        var lines = summary.Split('\n', StringSplitOptions.None);
        foreach (var rawLine in lines) {
            var line = rawLine.Trim();
            if (line.Length == 0 || IsPipeOnlyLine(line)) {
                continue;
            }

            if (line.StartsWith('#') || line.Equals("count", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (int.TryParse(line, out _)) {
                continue;
            }

            usefulLines++;
            if (usefulLines >= 2) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSummaryMarkdown(string? summaryMarkdown, string toolLabel) {
        if (string.IsNullOrWhiteSpace(summaryMarkdown)) {
            return string.Empty;
        }

        var lines = summaryMarkdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        var sb = new StringBuilder();
        var previousBlank = false;
        var inFence = false;
        var fenceMarker = '\0';
        var fenceRunLength = 0;
        foreach (var rawLine in lines) {
            var line = rawLine.TrimEnd();
            if (TryReadFenceRun(line, out var runMarker, out var runLength, out var runSuffix)) {
                if (!inFence) {
                    inFence = true;
                    fenceMarker = runMarker;
                    fenceRunLength = runLength;
                } else if (runMarker == fenceMarker
                           && runLength >= fenceRunLength
                           && string.IsNullOrWhiteSpace(runSuffix)) {
                    inFence = false;
                    fenceMarker = '\0';
                    fenceRunLength = 0;
                }

                sb.AppendLine(line);
                previousBlank = false;
                continue;
            }

            if (inFence) {
                sb.AppendLine(line);
                previousBlank = false;
                continue;
            }

            if (line.Length == 0) {
                if (!previousBlank && sb.Length > 0) {
                    sb.AppendLine();
                }
                previousBlank = true;
                continue;
            }

            if (IsPipeOnlyLine(line)) {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal)) {
                var heading = line[4..].Trim();
                if (heading.Equals(toolLabel, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
            }

            sb.AppendLine(line);
            previousBlank = false;
        }

        return sb.ToString().TrimEnd();
    }

    private static bool TryReadFenceRun(string line, out char marker, out int runLength, out string suffix) {
        marker = '\0';
        runLength = 0;
        suffix = string.Empty;
        if (line is null) {
            return false;
        }

        var trimmed = line.TrimStart();
        if (trimmed.Length < 3) {
            return false;
        }

        var first = trimmed[0];
        if (first != '`' && first != '~') {
            return false;
        }

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == first) {
            i++;
        }

        if (i < 3) {
            return false;
        }

        marker = first;
        runLength = i;
        suffix = trimmed.Substring(i);
        return true;
    }

    private static bool IsPipeOnlyLine(string line) {
        var hasPipe = false;
        for (var i = 0; i < line.Length; i++) {
            var ch = line[i];
            if (ch == '|') {
                hasPipe = true;
                continue;
            }

            if (!char.IsWhiteSpace(ch)) {
                return false;
            }
        }

        return hasPipe;
    }

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
            "network" => NetworkFenceLanguage,
            "visnetwork" => NetworkFenceLanguage,
            _ => normalized
        };
    }

    private static string NormalizeRenderFenceLanguage(string language, string normalizedKind) {
        var normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedLanguage.Length > 0) {
            if (string.Equals(normalizedLanguage, "chart", StringComparison.Ordinal)) {
                return ChartFenceLanguage;
            }

            if (string.Equals(normalizedLanguage, "network", StringComparison.Ordinal)
                || string.Equals(normalizedLanguage, "visnetwork", StringComparison.Ordinal)) {
                return NetworkFenceLanguage;
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
            && !string.Equals(normalizedKind, NetworkFenceLanguage, StringComparison.Ordinal)) {
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
        var payload = Encoding.UTF8.GetBytes(value);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        return normalizedLanguage + ":" + hash + ":" + value.Length;
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
            switch (rowNode.ValueKind) {
                case JsonValueKind.Object:
                    for (var i = 0; i < columns.Count; i++) {
                        row[i] = TryGetPropertyValueCaseInsensitive(rowNode, columns[i].Key, out var valueNode)
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
        value = default;
        if (obj.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyName)) {
            return false;
        }

        if (obj.TryGetProperty(propertyName, out value)) {
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

    private static void AppendFailureDescriptor(MarkdownComposer markdown, ToolOutputDto output) {
        var detailParts = new List<string>();
        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        var errorMessage = (output.Error ?? string.Empty).Trim();

        if (errorCode.Length > 0) {
            detailParts.Add("code: `" + errorCode + "`");
        }
        if (output.IsTransient.HasValue) {
            detailParts.Add("retryable: " + (output.IsTransient.Value ? "yes" : "no"));
        }
        if (detailParts.Count > 0) {
            markdown.Quote("failure descriptor: " + string.Join(" | ", detailParts));
        }

        if (errorMessage.Length > 0) {
            markdown.Quote("error: " + errorMessage);
        } else if (errorCode.Length > 0) {
            markdown.Quote("error: Tool failed with code `" + errorCode + "`.");
        }

        if (output.Hints is { Length: > 0 }) {
            markdown.Paragraph("hints:");
            for (var i = 0; i < output.Hints.Length; i++) {
                var hint = (output.Hints[i] ?? string.Empty).Trim();
                if (hint.Length > 0) {
                    markdown.Bullet(hint);
                }
            }
        }
    }

    private sealed class VisualFenceGroup {
        public VisualFenceGroup(string callId) {
            CallId = callId;
        }

        public string CallId { get; }
        public List<(string Language, string Content)> Fences { get; } = new();
        public HashSet<string> SeenFences { get; } = new(StringComparer.Ordinal);
    }
}
