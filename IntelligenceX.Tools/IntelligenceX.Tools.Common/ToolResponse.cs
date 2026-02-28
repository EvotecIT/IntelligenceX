using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Thin facade for consistent tool success/error envelopes.
/// </summary>
/// <remarks>
/// Keep tool-specific payload fields at the root level to match <c>render.rows_path</c>/<c>render.content_path</c>
/// semantics used by the UI contract.
/// </remarks>
public static class ToolResponse {
    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) with optional root payload fields.
    /// </summary>
    public static string Ok(JsonObject? root = null, JsonObject? meta = null, string? summaryMarkdown = null, JsonObject? render = null) {
        // Delegate to the canonical helper so envelope fields stay consistent across repos.
        return ToolOutputEnvelope.OkFlat(root, meta, summaryMarkdown, render);
    }

    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) from a typed model, mapped to snake_case JSON keys.
    /// </summary>
    /// <remarks>
    /// This is a convenience API to keep tools thin: build a typed report/DTO and let Tools.Common handle JSON shaping.
    /// The resulting object is used as the tool output root payload.
    /// </remarks>
    public static string OkModel<T>(T model, JsonObject? meta = null, string? summaryMarkdown = null, JsonObject? render = null) {
        if (model is null) {
            return Ok(root: new JsonObject(StringComparer.Ordinal), meta: meta, summaryMarkdown: summaryMarkdown, render: render);
        }

        if (model is JsonObject obj) {
            return Ok(root: obj, meta: meta, summaryMarkdown: summaryMarkdown, render: render);
        }

        var root = ToolJson.ToJsonObjectSnakeCase(model);
        return Ok(root: root, meta: meta, summaryMarkdown: summaryMarkdown, render: render);
    }

    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) from a typed model and emits a Mermaid render hint.
    /// </summary>
    /// <typeparam name="T">Model type.</typeparam>
    /// <param name="model">Typed model mapped to snake_case root payload fields.</param>
    /// <param name="mermaidPath">JSON path (relative to tool output root) containing Mermaid source.</param>
    /// <param name="meta">Optional metadata object.</param>
    /// <param name="summaryMarkdown">Optional markdown summary.</param>
    public static string OkMermaidModel<T>(T model, string mermaidPath, JsonObject? meta = null, string? summaryMarkdown = null) {
        return OkModel(
            model: model,
            meta: meta,
            summaryMarkdown: summaryMarkdown,
            render: ToolOutputHints.RenderMermaid(mermaidPath));
    }

    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) for list/table-like results with a markdown table preview,
    /// using a typed root model mapped to snake_case.
    /// </summary>
    /// <remarks>
    /// This keeps tools thin: build a typed DTO for the root payload and let Tools.Common handle JSON shaping.
    /// </remarks>
    public static string OkTablePreviewModel<T>(
        T model,
        string title,
        string rowsPath,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> previewRows,
        int count,
        bool truncated,
        int? scanned = null,
        Action<JsonObject>? metaMutate = null,
        params ToolColumn[] columns) {

        var root = ToolJson.ToJsonObjectSnakeCase(model);
        return OkTablePreview(
            root: root,
            title: title,
            rowsPath: rowsPath,
            headers: headers,
            previewRows: previewRows,
            count: count,
            truncated: truncated,
            scanned: scanned,
            metaMutate: metaMutate,
            columns: columns);
    }

    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) for list/table-like results with a markdown table preview.
    /// </summary>
    /// <remarks>
    /// Intended for tools that return an array at <paramref name="rowsPath"/> and want a stable
    /// <c>meta</c>/<c>render</c>/<c>summary_markdown</c> pattern without repeating boilerplate.
    /// </remarks>
    /// <param name="root">Root payload fields (kept at the tool output root).</param>
    /// <param name="title">Markdown title used by the preview table.</param>
    /// <param name="rowsPath">JSON path (relative to the tool output root) pointing to the rows array.</param>
    /// <param name="headers">Preview table headers.</param>
    /// <param name="previewRows">Preview table rows.</param>
    /// <param name="count">Returned item count.</param>
    /// <param name="truncated">Whether results were truncated by caps.</param>
    /// <param name="scanned">Optional scanned/considered count.</param>
    /// <param name="metaMutate">Optional meta mutator for adding extra fields.</param>
    /// <param name="columns">Render columns (keys must match row objects).</param>
    public static string OkTablePreview(
        JsonObject root,
        string title,
        string rowsPath,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> previewRows,
        int count,
        bool truncated,
        int? scanned = null,
        Action<JsonObject>? metaMutate = null,
        params ToolColumn[] columns) {

        var meta = ToolOutputHints.Meta(count: count, truncated: truncated, scanned: scanned, previewCount: previewRows?.Count ?? 0);
        metaMutate?.Invoke(meta);

        var render = ToolOutputHints.RenderTable(rowsPath ?? string.Empty, columns);
        var summaryMarkdown = ToolMarkdownContract.Create()
            .AddTable(
                title: title ?? string.Empty,
                headers: headers ?? Array.Empty<string>(),
                rows: previewRows ?? Array.Empty<IReadOnlyList<string>>(),
                totalCount: count,
                truncated: truncated)
            .Build();

        return Ok(root: root, meta: meta, summaryMarkdown: summaryMarkdown, render: render);
    }

    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) from a typed model and a key/value facts summary table.
    /// </summary>
    /// <typeparam name="T">Model type.</typeparam>
    /// <param name="model">Typed model mapped to snake_case root payload fields.</param>
    /// <param name="title">Summary table title.</param>
    /// <param name="facts">Ordered fact rows as <c>(key,value)</c> tuples.</param>
    /// <param name="meta">Optional metadata. When omitted, a default meta object is produced.</param>
    /// <param name="keyHeader">Optional first-column header label.</param>
    /// <param name="valueHeader">Optional second-column header label.</param>
    /// <param name="truncated">Optional summary truncation marker.</param>
    /// <param name="render">Optional render hint override.</param>
    public static string OkFactsModel<T>(
        T model,
        string title,
        IReadOnlyList<(string Key, string Value)> facts,
        JsonObject? meta = null,
        string keyHeader = "Field",
        string valueHeader = "Value",
        bool truncated = false,
        JsonObject? render = null) {
        var items = facts ?? Array.Empty<(string Key, string Value)>();

        var rows = new List<IReadOnlyList<string>>(items.Count);
        for (var i = 0; i < items.Count; i++) {
            var item = items[i];
            rows.Add(new[] { item.Key ?? string.Empty, item.Value ?? string.Empty });
        }

        var summaryMarkdown = ToolMarkdownContract.Create()
            .AddTable(
                title: title,
                headers: new[] { keyHeader, valueHeader },
                rows: rows,
                totalCount: rows.Count,
                truncated: truncated)
            .Build();

        var resolvedMeta = meta ?? ToolOutputHints.Meta(
            count: rows.Count,
            truncated: truncated,
            scanned: null,
            previewCount: rows.Count);

        return OkModel(model, meta: resolvedMeta, summaryMarkdown: summaryMarkdown, render: render);
    }

    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) from a typed model and a key/value facts summary table,
    /// allowing <c>render</c> to be either an object or an array.
    /// </summary>
    /// <typeparam name="T">Model type.</typeparam>
    /// <param name="model">Typed model mapped to snake_case root payload fields.</param>
    /// <param name="title">Summary table title.</param>
    /// <param name="facts">Ordered fact rows as <c>(key,value)</c> tuples.</param>
    /// <param name="meta">Optional metadata. When omitted, a default meta object is produced.</param>
    /// <param name="keyHeader">Optional first-column header label.</param>
    /// <param name="valueHeader">Optional second-column header label.</param>
    /// <param name="truncated">Optional summary truncation marker.</param>
    /// <param name="render">Optional render hint override as object or array.</param>
    public static string OkFactsModelWithRenderValue<T>(
        T model,
        string title,
        IReadOnlyList<(string Key, string Value)> facts,
        JsonObject? meta = null,
        string keyHeader = "Field",
        string valueHeader = "Value",
        bool truncated = false,
        JsonValue? render = null) {
        var items = facts ?? Array.Empty<(string Key, string Value)>();

        var rows = new List<IReadOnlyList<string>>(items.Count);
        for (var i = 0; i < items.Count; i++) {
            var item = items[i];
            rows.Add(new[] { item.Key ?? string.Empty, item.Value ?? string.Empty });
        }

        var summaryMarkdown = ToolMarkdownContract.Create()
            .AddTable(
                title: title,
                headers: new[] { keyHeader, valueHeader },
                rows: rows,
                totalCount: rows.Count,
                truncated: truncated)
            .Build();

        var resolvedMeta = meta ?? ToolOutputHints.Meta(
            count: rows.Count,
            truncated: truncated,
            scanned: null,
            previewCount: rows.Count);

        JsonObject root;
        if (model is null) {
            root = new JsonObject(StringComparer.Ordinal);
        } else if (model is JsonObject obj) {
            root = obj;
        } else {
            root = ToolJson.ToJsonObjectSnakeCase(model);
        }

        return ToolOutputEnvelope.OkFlatWithRenderValue(
            root: root,
            meta: resolvedMeta,
            summaryMarkdown: summaryMarkdown,
            render: render);
    }

    /// <summary>
    /// Serializes a success envelope for mutating tools with a standardized dry-run/apply summary and metadata.
    /// </summary>
    /// <typeparam name="T">Model type.</typeparam>
    /// <param name="model">Typed model mapped to snake_case root payload fields.</param>
    /// <param name="action">Human-readable action name.</param>
    /// <param name="writeApplied">True when the write action actually executed; false for dry-run mode.</param>
    /// <param name="facts">Optional additional summary facts.</param>
    /// <param name="meta">Optional metadata object. When omitted, a default meta object is produced.</param>
    /// <param name="render">Optional render hint override.</param>
    /// <param name="summaryTitle">Optional summary title override.</param>
    public static string OkWriteActionModel<T>(
        T model,
        string action,
        bool writeApplied,
        IReadOnlyList<(string Key, string Value)>? facts = null,
        JsonObject? meta = null,
        JsonObject? render = null,
        string? summaryTitle = null) {
        if (string.IsNullOrWhiteSpace(action)) {
            throw new ArgumentException("Action name is required.", nameof(action));
        }

        string normalizedAction = action.Trim();
        string mode = writeApplied ? "apply" : "dry-run";

        var summaryFacts = new List<(string Key, string Value)> {
            ("Mode", mode),
            ("Action", normalizedAction)
        };

        foreach (var fact in facts ?? Array.Empty<(string Key, string Value)>()) {
            if (string.IsNullOrWhiteSpace(fact.Key)) {
                continue;
            }

            summaryFacts.Add((fact.Key.Trim(), fact.Value ?? string.Empty));
        }

        var resolvedMeta = meta ?? ToolOutputHints.Meta(count: 1, truncated: false);
        resolvedMeta
            .Add("mode", mode)
            .Add("write_applied", writeApplied)
            .Add("action", normalizedAction);

        string resolvedTitle = string.IsNullOrWhiteSpace(summaryTitle)
            ? normalizedAction
            : summaryTitle.Trim();
        var summaryMarkdown = ToolMarkdown.SummaryFacts(
            title: resolvedTitle,
            facts: summaryFacts);

        return OkModel(model, meta: resolvedMeta, summaryMarkdown: summaryMarkdown, render: render);
    }

    /// <summary>
    /// Serializes an error envelope (<c>ok=false</c>) as JSON.
    /// </summary>
    public static string Error(string errorCode, string error, IEnumerable<string>? hints = null, bool isTransient = false)
        => ToolOutputEnvelope.Error(errorCode, error, hints, isTransient);
}
