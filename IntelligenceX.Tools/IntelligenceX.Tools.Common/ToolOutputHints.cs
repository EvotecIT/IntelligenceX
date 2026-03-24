using System;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Helpers for consistent tool output metadata and UI render hints.
/// </summary>
public static class ToolOutputHints {
    /// <summary>
    /// Current schema version for <c>meta</c>/<c>render</c> helper outputs.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Content type marker for JSON tool output envelopes.
    /// </summary>
    public const string ContentType = "application/vnd.intelligencex.tooloutput+json";

    /// <summary>
    /// Builds a standard <c>meta</c> object for list-like tool results.
    /// </summary>
    /// <param name="count">Number of items returned.</param>
    /// <param name="truncated">Whether results were truncated due to caps.</param>
    /// <param name="scanned">Optional number of items scanned/considered.</param>
    /// <param name="previewCount">Optional number of preview items included in a markdown summary.</param>
    public static JsonObject Meta(int count, bool truncated, int? scanned = null, int? previewCount = null) {
        var meta = new JsonObject()
            .Add("schema_version", SchemaVersion)
            .Add("content_type", ContentType)
            .Add("count", count)
            .Add("truncated", truncated);

        if (scanned.HasValue && scanned.Value >= 0) {
            meta.Add("scanned", scanned.Value);
        }
        if (previewCount.HasValue && previewCount.Value >= 0) {
            meta.Add("preview_count", previewCount.Value);
        }

        return meta;
    }

    /// <summary>
    /// Builds a simple table render hint.
    /// </summary>
    /// <param name="rowsPath">JSON path (relative to the tool output root) pointing to the rows array.</param>
    /// <param name="columns">Column definitions (key should match row object properties).</param>
    public static JsonObject RenderTable(string rowsPath, params ToolColumn[] columns) {
        rowsPath = NormalizePath(rowsPath);

        var arr = new JsonArray();
        if (columns is not null) {
            foreach (var c in columns) {
                if (string.IsNullOrWhiteSpace(c.Key)) {
                    continue;
                }
                arr.Add(new JsonObject()
                    .Add("key", c.Key)
                    .Add("label", string.IsNullOrWhiteSpace(c.Label) ? c.Key : c.Label)
                    .Add("type", string.IsNullOrWhiteSpace(c.Type) ? "string" : c.Type));
            }
        }

        return new JsonObject()
            .Add("kind", "table")
            .Add("rows_path", rowsPath)
            .Add("columns", arr);
    }

    /// <summary>
    /// Builds a simple code render hint.
    /// </summary>
    /// <param name="language">Language tag (for example: text, json, xml).</param>
    /// <param name="contentPath">JSON path (relative to the tool output root) pointing to the content string.</param>
    public static JsonObject RenderCode(string language, string contentPath) {
        contentPath = NormalizePath(contentPath);

        return new JsonObject()
            .Add("kind", "code")
            .Add("language", string.IsNullOrWhiteSpace(language) ? "text" : language)
            .Add("content_path", contentPath);
    }

    /// <summary>
    /// Builds a Mermaid diagram render hint.
    /// </summary>
    /// <param name="contentPath">JSON path (relative to the tool output root) pointing to the Mermaid source string.</param>
    public static JsonObject RenderMermaid(string contentPath) {
        return RenderCode(language: "mermaid", contentPath: contentPath);
    }

    /// <summary>
    /// Builds a generic chart render hint.
    /// </summary>
    /// <param name="contentPath">JSON path (relative to the tool output root) pointing to the chart JSON payload.</param>
    public static JsonObject RenderChart(string contentPath) {
        return RenderCode(language: "chart", contentPath: contentPath);
    }

    /// <summary>
    /// Builds a generic network render hint.
    /// </summary>
    /// <param name="contentPath">JSON path (relative to the tool output root) pointing to the network JSON payload.</param>
    public static JsonObject RenderNetwork(string contentPath) {
        return RenderCode(language: "network", contentPath: contentPath);
    }

    /// <summary>
    /// Builds a generic dataview render hint.
    /// </summary>
    /// <param name="contentPath">JSON path (relative to the tool output root) pointing to the dataview JSON payload.</param>
    public static JsonObject RenderDataView(string contentPath) {
        return RenderCode(language: "dataview", contentPath: contentPath);
    }

    /// <summary>
    /// Builds an IntelligenceX chart render hint compatibility alias.
    /// Prefer <see cref="RenderChart"/> for new tool output.
    /// </summary>
    public static JsonObject RenderIxChart(string contentPath) {
        return RenderCode(language: "ix-chart", contentPath: contentPath);
    }

    /// <summary>
    /// Builds an IntelligenceX network render hint compatibility alias.
    /// Prefer <see cref="RenderNetwork"/> for new tool output.
    /// </summary>
    public static JsonObject RenderIxNetwork(string contentPath) {
        return RenderCode(language: "ix-network", contentPath: contentPath);
    }

    /// <summary>
    /// Builds an IntelligenceX dataview render hint compatibility alias.
    /// Prefer <see cref="RenderDataView"/> for new tool output.
    /// </summary>
    public static JsonObject RenderIxDataView(string contentPath) {
        return RenderCode(language: "ix-dataview", contentPath: contentPath);
    }

    private static string NormalizePath(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }
        var p = path.Trim();
        while (p.StartsWith("/", StringComparison.Ordinal)) {
            p = p.Substring(1);
        }
        return p;
    }
}

/// <summary>
/// Column definition for table render hints.
/// </summary>
public readonly record struct ToolColumn {
    /// <summary>
    /// Creates a new <see cref="ToolColumn"/>.
    /// </summary>
    public ToolColumn(string key, string label, string type) {
        Key = key;
        Label = label;
        Type = type;
    }

    /// <summary>
    /// Property key name in each row object.
    /// </summary>
    public string Key { get; }
    /// <summary>
    /// Display label.
    /// </summary>
    public string Label { get; }
    /// <summary>
    /// Display type hint (for example: string, int, bytes, datetime).
    /// </summary>
    public string Type { get; }
}
