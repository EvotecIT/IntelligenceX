using System;
using System.Collections.Generic;
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
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_scheduled_tasks_list",
        "List Windows scheduled tasks (read-only, capped).",
        ToolSchema.Object(
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
        // schtasks.exe queries can be slow; keep the tool runner responsive by not blocking the caller thread.
        return Task.Run(() => {
            cancellationToken.ThrowIfCancellationRequested();

            var unsupported = ValidateWindowsSupport("The scheduled tasks tool");
            if (!string.IsNullOrWhiteSpace(unsupported)) {
                return unsupported;
            }

            var nameContains = arguments?.GetString("name_contains");
            var max = ResolveBoundedOptionLimit(arguments, "max_tasks");

            var suspicious = arguments?.GetBoolean("suspicious") ?? false;
            var onlySuspicious = arguments?.GetBoolean("only_suspicious") ?? false;

            if (!TaskSchedulerListQueryExecutor.TryExecute(
                    request: new TaskSchedulerListQueryRequest {
                        PathContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim(),
                        Suspicious = suspicious,
                        OnlySuspicious = onlySuspicious,
                        MaxResults = max
                    },
                    result: out var queryResult,
                    failure: out var failure,
                    cancellationToken: cancellationToken)) {
                return ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Scheduled task query failed.");
            }

            var result = queryResult ?? new TaskSchedulerListQueryResult();
            var response = BuildAutoTableResponse(
                arguments: arguments,
                model: result,
                sourceRows: result.Tasks,
                viewRowsPath: "tasks_view",
                title: "Scheduled tasks (preview)",
                maxTop: MaxViewTop,
                baseTruncated: result.Truncated,
                scanned: result.Scanned,
                metaMutate: meta => {
                    meta.Add("suspicious", suspicious);
                    meta.Add("only_suspicious", onlySuspicious);
                });
            return response;
        }, cancellationToken);
    }
}

