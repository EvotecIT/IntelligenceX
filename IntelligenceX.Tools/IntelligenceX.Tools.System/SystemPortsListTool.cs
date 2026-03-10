using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Ports;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists TCP/UDP ports and owning processes (read-only, capped).
/// </summary>
public sealed class SystemPortsListTool : SystemToolBase, ITool {
    private sealed record PortsListRequest(
        string? ComputerName,
        string Target,
        PortInventoryProtocol Protocol,
        int? LocalPort,
        string? State,
        string? ProcessNameContains,
        int MaxEntries);

    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, PortInventoryProtocol> ProtocolByName =
        new Dictionary<string, PortInventoryProtocol>(StringComparer.OrdinalIgnoreCase) {
            ["any"] = PortInventoryProtocol.Any,
            ["tcp"] = PortInventoryProtocol.Tcp,
            ["udp"] = PortInventoryProtocol.Udp
        };

    private static readonly IReadOnlyDictionary<PortInventoryProtocol, string> ProtocolNames =
        new Dictionary<PortInventoryProtocol, string> {
            [PortInventoryProtocol.Any] = "any",
            [PortInventoryProtocol.Tcp] = "tcp",
            [PortInventoryProtocol.Udp] = "udp"
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_ports_list",
        "List TCP/UDP ports and owning processes (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("protocol", ToolSchema.String("Protocol filter. Default any.").Enum("any", "tcp", "udp")),
                ("local_port", ToolSchema.Integer("Optional exact local port filter (1-65535).")),
                ("state", ToolSchema.String("Optional TCP state filter (for example LISTEN, ESTAB).")),
                ("process_name_contains", ToolSchema.String("Optional case-insensitive process name substring filter.")),
                ("max_entries", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPortsListTool"/> class.
    /// </summary>
    public SystemPortsListTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<PortsListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("protocol"),
                    ProtocolByName,
                    "protocol",
                    out PortInventoryProtocol? parsedProtocol,
                    out var protocolError)) {
                return ToolRequestBindingResult<PortsListRequest>.Failure(protocolError ?? "Invalid protocol value.");
            }

            var localPortArg = reader.OptionalInt64("local_port");
            int? localPort = null;
            if (localPortArg.HasValue) {
                if (localPortArg.Value < 1 || localPortArg.Value > 65535) {
                    return ToolRequestBindingResult<PortsListRequest>.Failure("local_port must be between 1 and 65535.");
                }

                localPort = (int)localPortArg.Value;
            }

            return ToolRequestBindingResult<PortsListRequest>.Success(new PortsListRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                Protocol: parsedProtocol ?? PortInventoryProtocol.Any,
                LocalPort: localPort,
                State: reader.OptionalString("state"),
                ProcessNameContains: reader.OptionalString("process_name_contains"),
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<PortsListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (!PortInventoryQueryExecutor.TryExecute(
                request: new PortInventoryQueryRequest {
                    ComputerName = request.ComputerName,
                    Protocol = request.Protocol,
                    LocalPort = request.LocalPort,
                    ProcessNameContains = request.ProcessNameContains,
                    State = request.State,
                    MaxResults = request.MaxEntries
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Port inventory query failed."));
        }

        var result = queryResult!;
        var effectiveComputerName = string.IsNullOrWhiteSpace(result.ComputerName) ? request.Target : result.ComputerName;
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Rows,
            viewRowsPath: "rows_view",
            title: "Ports (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, effectiveComputerName);
                AddMaxResultsMeta(meta, request.MaxEntries);
                meta.Add("protocol", ProtocolToString(request.Protocol));
                if (request.LocalPort.HasValue) {
                    meta.Add("local_port", request.LocalPort.Value);
                }
                if (!string.IsNullOrWhiteSpace(request.State)) {
                    meta.Add("state", request.State);
                }
                if (!string.IsNullOrWhiteSpace(request.ProcessNameContains)) {
                    meta.Add("process_name_contains", request.ProcessNameContains);
                }
                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_ports_list",
                    targetComputer: effectiveComputerName,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: result.Scanned,
                    truncated: result.Truncated);
            });
        return Task.FromResult(response);
    }

    private static string ProtocolToString(PortInventoryProtocol protocol) {
        return ToolEnumBinders.ToName(protocol, ProtocolNames);
    }
}
