using System;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.ScheduledTasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists Windows scheduled tasks via schtasks.exe (read-only, capped).
/// </summary>
public sealed class SystemScheduledTasksListTool : SystemToolBase, ITool {
    private sealed record ScheduledTasksListRequest(
        string? ComputerName,
        string Target,
        string? NameContains,
        int MaxTasks,
        bool Suspicious,
        bool OnlySuspicious);

    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_scheduled_tasks_list",
        "List Windows scheduled tasks (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive substring match against task path.")),
                ("max_tasks", ToolSchema.Integer("Optional maximum tasks to return (capped).")),
                ("suspicious", ToolSchema.Boolean("Whether to include suspicion enrichment fields (ComputerX heuristics).")),
                ("only_suspicious", ToolSchema.Boolean("When suspicious=true, return only suspicious tasks.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemScheduledTasksListTool"/> class.
    /// </summary>
    public SystemScheduledTasksListTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<ScheduledTasksListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<ScheduledTasksListRequest>.Success(new ScheduledTasksListRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                NameContains: reader.OptionalString("name_contains"),
                MaxTasks: ResolveBoundedOptionLimit(arguments, "max_tasks"),
                Suspicious: reader.Boolean("suspicious", defaultValue: false),
                OnlySuspicious: reader.Boolean("only_suspicious", defaultValue: false)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ScheduledTasksListRequest> context, CancellationToken cancellationToken) {
        // schtasks.exe queries can be slow; keep the tool runner responsive by not blocking the caller thread.
        return Task.Run(() => {
            cancellationToken.ThrowIfCancellationRequested();
            var request = context.Request;

            var unsupported = ValidateWindowsSupport("The scheduled tasks tool");
            if (!string.IsNullOrWhiteSpace(unsupported)) {
                return unsupported;
            }

            if (!TaskSchedulerListQueryExecutor.TryExecute(
                    request: new TaskSchedulerListQueryRequest {
                        ComputerName = request.ComputerName,
                        PathContains = request.NameContains,
                        Suspicious = request.Suspicious,
                        OnlySuspicious = request.OnlySuspicious,
                        MaxResults = request.MaxTasks
                    },
                    result: out var queryResult,
                    failure: out var failure,
                    cancellationToken: cancellationToken)) {
                return ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Scheduled task query failed.");
            }

            var result = queryResult ?? new TaskSchedulerListQueryResult();
            var effectiveComputerName = string.IsNullOrWhiteSpace(result.ComputerName) ? request.Target : result.ComputerName;
            var response = ToolResultV2.OkAutoTableResponse(
                arguments: context.Arguments,
                model: result,
                sourceRows: result.Tasks,
                viewRowsPath: "tasks_view",
                title: "Scheduled tasks (preview)",
                maxTop: MaxViewTop,
                baseTruncated: result.Truncated,
                scanned: result.Scanned,
                metaMutate: meta => {
                    AddComputerNameMeta(meta, effectiveComputerName);
                    AddMaxResultsMeta(meta, request.MaxTasks);
                    meta.Add("suspicious", request.Suspicious);
                    meta.Add("only_suspicious", request.OnlySuspicious);
                    if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                        meta.Add("name_contains", request.NameContains);
                    }
                    AddReadOnlyPostureChainingMeta(
                        meta: meta,
                        currentTool: "system_scheduled_tasks_list",
                        targetComputer: effectiveComputerName,
                        isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                        scanned: result.Scanned,
                        truncated: result.Truncated);
                });
            return response;
        }, cancellationToken);
    }
}
