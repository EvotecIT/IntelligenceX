using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helper for table-view parsing/projection and response-envelope shaping.
/// </summary>
public static class ToolTableViewEnvelope {
    /// <summary>
    /// Builds a standard raw+view table response using auto-derived columns from <typeparamref name="TRow"/>.
    /// </summary>
    public static bool TryBuildModelResponseAutoColumns<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IEnumerable<TRow> sourceRows,
        string viewRowsPath,
        string title,
        int maxTop,
        bool baseTruncated,
        out string response,
        int? scanned = null,
        Action<JsonObject>? metaMutate = null) {
        return TryBuildModelResponse(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            columnSpecs: ToolAutoTableColumns.GetColumnSpecs<TRow>(),
            columnKeys: ToolAutoTableColumns.GetColumnKeys<TRow>(),
            maxTop: maxTop,
            baseTruncated: baseTruncated,
            response: out response,
            scanned: scanned,
            metaMutate: metaMutate);
    }

    /// <summary>
    /// Builds a standard raw+view table response from a typed model and typed source rows.
    /// </summary>
    /// <typeparam name="TModel">Root model type.</typeparam>
    /// <typeparam name="TRow">Row type used for projection.</typeparam>
    /// <param name="arguments">Tool arguments containing optional view parameters.</param>
    /// <param name="model">Root model containing raw engine payload.</param>
    /// <param name="sourceRows">Rows to project into view rows.</param>
    /// <param name="viewRowsPath">Payload path for projected rows (for example: <c>results_view</c>).</param>
    /// <param name="title">Preview table title.</param>
    /// <param name="columnSpecs">Available projection columns.</param>
    /// <param name="columnKeys">Allowed projection keys.</param>
    /// <param name="maxTop">Maximum accepted <c>top</c> value.</param>
    /// <param name="baseTruncated">Whether the raw engine result was already truncated.</param>
    /// <param name="response">Serialized envelope (success or invalid-argument error).</param>
    /// <param name="scanned">Optional scanned count for metadata.</param>
    /// <param name="metaMutate">Optional metadata mutator.</param>
    /// <returns><c>true</c> when response is success; <c>false</c> when view args are invalid.</returns>
    public static bool TryBuildModelResponse<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IEnumerable<TRow> sourceRows,
        string viewRowsPath,
        string title,
        IReadOnlyList<ToolTableColumnSpec<TRow>> columnSpecs,
        IReadOnlyList<string> columnKeys,
        int maxTop,
        bool baseTruncated,
        out string response,
        int? scanned = null,
        Action<JsonObject>? metaMutate = null) {
        if (!ToolTableView.TryParse(arguments, columnKeys, maxTop: maxTop, out var view, out var viewError)) {
            var error = string.IsNullOrWhiteSpace(viewError) ? "Invalid tabular view arguments." : viewError!;
            var hints = new List<string> {
                "Use only listed columns for projection.",
                "Use sort_direction as 'asc' or 'desc'.",
                "If projection keeps failing, retry without columns/sort_by/sort_direction/top."
            };
            var meta = new JsonObject()
                .Add("available_columns", new JsonArray().AddRange(columnKeys))
                .Add("projection_arguments", new JsonArray().Add("columns").Add("sort_by").Add("sort_direction").Add("top"));
            response = ToolOutputEnvelope.Error(
                errorCode: "invalid_argument",
                error: error,
                hints: hints,
                isTransient: false,
                meta: meta);
            return false;
        }

        var viewResult = ToolTableView.Apply(sourceRows, view, columnSpecs, previewMaxRows: 20);
        var output = model is JsonObject obj
            ? obj
            : ToolJson.ToJsonObjectSnakeCase(model);
        output.Add(viewRowsPath, viewResult.Rows);

        response = ToolResponse.OkTablePreviewModel(
            model: output,
            title: title,
            rowsPath: viewRowsPath,
            headers: viewResult.Columns.Select(static c => c.Label).ToArray(),
            previewRows: viewResult.PreviewRows,
            count: viewResult.Count,
            truncated: baseTruncated || viewResult.TruncatedByView,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("available_columns", new JsonArray().AddRange(columnKeys));
                metaMutate?.Invoke(meta);
            },
            columns: viewResult.Columns.ToArray());
        return true;
    }
}
