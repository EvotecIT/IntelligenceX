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
/// Lists local TCP/UDP ports and owning processes (read-only, capped).
/// </summary>
public sealed class SystemPortsListTool : SystemToolBase, ITool {
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
        "List local TCP/UDP ports and owning processes (read-only, capped).",
        ToolSchema.Object(
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
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolEnumBinders.TryParseOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "protocol"),
                ProtocolByName,
                "protocol",
                out PortInventoryProtocol? parsedProtocol,
                out var protocolError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", protocolError ?? "Invalid protocol value."));
        }
        var protocol = parsedProtocol ?? PortInventoryProtocol.Any;

        var localPortArg = arguments?.GetInt64("local_port");
        int? localPort = null;
        if (localPortArg.HasValue) {
            if (localPortArg.Value < 1 || localPortArg.Value > 65535) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", "local_port must be between 1 and 65535."));
            }
            localPort = (int)localPortArg.Value;
        }

        var processNameContains = ToolArgs.GetOptionalTrimmed(arguments, "process_name_contains");
        var state = ToolArgs.GetOptionalTrimmed(arguments, "state");
        var max = ResolveBoundedOptionLimit(arguments, "max_entries");

        if (!PortInventoryQueryExecutor.TryExecute(
                request: new PortInventoryQueryRequest {
                    Protocol = protocol,
                    LocalPort = localPort,
                    ProcessNameContains = processNameContains,
                    State = state,
                    MaxResults = max
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Port inventory query failed."));
        }

        var result = queryResult!;
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Rows,
            viewRowsPath: "rows_view",
            title: "Ports (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                meta.Add("protocol", ProtocolToString(protocol));
                if (localPort.HasValue) {
                    meta.Add("local_port", localPort.Value);
                }
                if (!string.IsNullOrWhiteSpace(state)) {
                    meta.Add("state", state);
                }
                if (!string.IsNullOrWhiteSpace(processNameContains)) {
                    meta.Add("process_name_contains", processNameContains);
                }
            });
        return Task.FromResult(response);
    }

    private static string ProtocolToString(PortInventoryProtocol protocol) {
        return ToolEnumBinders.ToName(protocol, ProtocolNames);
    }
}

