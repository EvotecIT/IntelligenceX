using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Common base class for tool implementations to avoid repeating invocation boilerplate.
/// </summary>
/// <remarks>
/// Derived tools implement <see cref="InvokeCoreAsync"/> and return an already-shaped tool output envelope
/// (typically via <see cref="ToolResponse.Ok"/>/<see cref="ToolResponse.Error"/>). Unhandled exceptions are
/// converted to a standard <c>error_code=exception</c> envelope by <see cref="ToolInvoker"/>.
/// </remarks>
public abstract class ToolBase : ITool {
    /// <inheritdoc />
    public abstract ToolDefinition Definition { get; }

    /// <inheritdoc />
    public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ToolInvoker.RunAsync(cancellationToken, () => InvokeCoreAsync(arguments, cancellationToken));
    }

    /// <summary>
    /// Runs a typed bind -> middleware -> execute pipeline.
    /// </summary>
    protected Task<string> RunPipelineAsync<TRequest>(
        JsonObject? arguments,
        CancellationToken cancellationToken,
        Func<JsonObject?, ToolRequestBindingResult<TRequest>> binder,
        ToolPipelineNext<TRequest> execute,
        ToolPipelineReliabilityOptions? reliability = null,
        IReadOnlyList<ToolPipelineMiddleware<TRequest>>? middleware = null)
        where TRequest : notnull {
        return ToolPipeline.RunAsync(
            definition: Definition,
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: binder,
            terminal: execute,
            reliability: reliability,
            middleware: middleware);
    }

    /// <summary>
    /// Adds max_results metadata consistently across tool responses.
    /// </summary>
    protected static void AddMaxResultsMeta(JsonObject meta, int maxResults) {
        meta.Add("max_results", maxResults);
    }

    /// <summary>
    /// Builds the standard auto-column table envelope used by read-only list/query tools.
    /// </summary>
    protected static string BuildAutoTableResponse<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<TRow> sourceRows,
        string viewRowsPath,
        string title,
        bool baseTruncated,
        int scanned,
        int maxTop,
        Action<JsonObject>? metaMutate = null) {
        return ToolQueryHelpers.BuildAutoTableResponse(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            maxTop: maxTop,
            baseTruncated: baseTruncated,
            scanned: scanned,
            metaMutate: metaMutate);
    }

    /// <summary>
    /// Builds a standard auto-column table envelope using source row count as scanned.
    /// </summary>
    protected static string BuildAutoTableResponse<TModel, TRow>(
        JsonObject? arguments,
        TModel model,
        IReadOnlyList<TRow> sourceRows,
        string viewRowsPath,
        string title,
        bool baseTruncated,
        int maxTop,
        Action<JsonObject>? metaMutate = null) {
        return BuildAutoTableResponse(
            arguments: arguments,
            model: model,
            sourceRows: sourceRows,
            viewRowsPath: viewRowsPath,
            title: title,
            baseTruncated: baseTruncated,
            scanned: sourceRows.Count,
            maxTop: maxTop,
            metaMutate: metaMutate);
    }

    /// <summary>
    /// Tool-specific implementation.
    /// </summary>
    /// <remarks>
    /// Do not wrap this in a generic try/catch. Let the base class normalize unhandled exceptions.
    /// Catch only when mapping to a specific <c>error_code</c> is required.
    /// </remarks>
    protected abstract Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken);
}
