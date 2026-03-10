using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Services;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists Windows services (read-only, capped).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemServiceListTool : SystemToolBase, ITool {
    private sealed record ServiceListRequest(
        string? ComputerName,
        string Target,
        ServiceEngine Engine,
        string? NameContains,
        ServiceListStatusFilter Status,
        int MaxServices);

    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, ServiceListStatusFilter> StatusFilterByName =
        new Dictionary<string, ServiceListStatusFilter>(StringComparer.OrdinalIgnoreCase) {
            ["running"] = ServiceListStatusFilter.Running,
            ["stopped"] = ServiceListStatusFilter.Stopped,
            ["paused"] = ServiceListStatusFilter.Paused
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_service_list",
        "List Windows services (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("engine", ToolSchema.String("Optional engine preference. Use 'auto' unless you need a specific backend for diagnostics or recovery.").Enum("auto", "native", "wmi", "cim")),
                ("name_contains", ToolSchema.String("Optional case-insensitive filter against service name and display name.")),
                ("status", ToolSchema.String("Optional status filter.").Enum("any", "running", "stopped", "paused")),
                ("max_services", ToolSchema.Integer("Optional maximum services to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemServiceListTool"/> class.
    /// </summary>
    public SystemServiceListTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync).ConfigureAwait(false);
    }

    private ToolRequestBindingResult<ServiceListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            if (!TryResolveServiceEngine(reader, "engine", out var engine, out var engineError)) {
                return ToolRequestBindingResult<ServiceListRequest>.Failure(engineError ?? "Invalid engine value.");
            }

            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("status"),
                    StatusFilterByName,
                    "status",
                    out ServiceListStatusFilter? parsedStatus,
                    out var statusError)) {
                return ToolRequestBindingResult<ServiceListRequest>.Failure(statusError ?? "Invalid status value.");
            }

            return ToolRequestBindingResult<ServiceListRequest>.Success(new ServiceListRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                Engine: engine,
                NameContains: reader.OptionalString("name_contains"),
                Status: parsedStatus ?? ServiceListStatusFilter.Any,
                MaxServices: ResolveBoundedOptionLimit(arguments, "max_services")));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<ServiceListRequest> context, CancellationToken cancellationToken) {
        var request = context.Request;
        var attempt = await ServiceListQueryExecutor.TryExecuteAsync(
            request: new ServiceListQueryRequest {
                ComputerName = request.ComputerName,
                Engine = request.Engine,
                NameContains = request.NameContains,
                StatusFilter = request.Status,
                MaxResults = request.MaxServices,
                SortBy = ServiceListQuerySort.ServiceNameAsc
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!attempt.Success) {
            return ErrorFromFailure(attempt.Failure, static x => x.Code, static x => x.Message, defaultMessage: "Service query failed.");
        }

        var result = attempt.Result ?? new ServiceListQueryResult();
        var effectiveComputerName = string.IsNullOrWhiteSpace(result.ComputerName) ? request.Target : result.ComputerName;
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Services,
            viewRowsPath: "services_view",
            title: "Services (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, effectiveComputerName);
                AddMaxResultsMeta(meta, request.MaxServices);
                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
                if (request.Status != ServiceListStatusFilter.Any) {
                    meta.Add("status", request.Status.ToString());
                }
                meta.Add("engine_preference", NormalizeServiceEngine(request.Engine));
                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_service_list",
                    targetComputer: effectiveComputerName,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: result.Scanned,
                    truncated: result.Truncated);
            });
        return response;
    }
}
