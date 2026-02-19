using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Processes;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists currently running processes (capped).
/// </summary>
public sealed class SystemProcessListTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_process_list",
        "List running processes (read-only, capped).",
        ToolSchema.Object(
                ("name_contains", ToolSchema.String("Optional case-insensitive name filter.")),
                ("max_processes", ToolSchema.Integer("Optional maximum processes to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemProcessListTool"/> class.
    /// </summary>
    public SystemProcessListTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var nameContains = arguments?.GetString("name_contains");
        var max = ResolveBoundedOptionLimit(arguments, "max_processes");

        if (!ProcessListQueryExecutor.TryExecute(
                request: new ProcessListQueryRequest {
                    NameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim(),
                    MaxResults = max,
                    SortBy = ProcessListQuerySort.PidAsc
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Process query failed."));
        }

        var result = queryResult ?? new ProcessListQueryResult();
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Processes,
            viewRowsPath: "processes_view",
            title: "Processes (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned);
        return Task.FromResult(response);
    }
}

