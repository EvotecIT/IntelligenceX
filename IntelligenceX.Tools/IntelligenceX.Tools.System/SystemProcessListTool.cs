using System;
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
    private sealed record ProcessListRequest(
        string? ComputerName,
        string Target,
        string? NameContains,
        int MaxProcesses);

    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_process_list",
        "List running processes (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<ProcessListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<ProcessListRequest>.Success(new ProcessListRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                NameContains: reader.OptionalString("name_contains"),
                MaxProcesses: ResolveBoundedOptionLimit(arguments, "max_processes")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ProcessListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (!ProcessListQueryExecutor.TryExecute(
                request: new ProcessListQueryRequest {
                    ComputerName = request.ComputerName,
                    NameContains = request.NameContains,
                    MaxResults = request.MaxProcesses,
                    SortBy = ProcessListQuerySort.PidAsc
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Process query failed."));
        }

        var result = queryResult ?? new ProcessListQueryResult();
        var effectiveComputerName = string.IsNullOrWhiteSpace(result.ComputerName) ? request.Target : result.ComputerName;
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Processes,
            viewRowsPath: "processes_view",
            title: "Processes (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, effectiveComputerName);
                AddMaxResultsMeta(meta, request.MaxProcesses);
                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_process_list",
                    targetComputer: effectiveComputerName,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: result.Scanned,
                    truncated: result.Truncated);
            });
        return Task.FromResult(response);
    }
}
