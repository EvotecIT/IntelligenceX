using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Network;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists local network adapters (read-only, capped).
/// </summary>
public sealed class SystemNetworkAdaptersTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private sealed record NetworkAdaptersRequest(string? NameContains, int MaxAdapters, int TimeoutMs);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_network_adapters",
        "List network adapters with IP/DNS details (read-only, capped).",
        ToolSchema.Object(
                ("name_contains", ToolSchema.String("Optional case-insensitive filter against adapter name/description.")),
                ("max_adapters", ToolSchema.Integer("Optional maximum adapters to return (capped).")),
                ("timeout_ms", ToolSchema.Integer("Optional query timeout in milliseconds (capped). Default 10000.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemNetworkAdaptersTool"/> class.
    /// </summary>
    public SystemNetworkAdaptersTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<NetworkAdaptersRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => ToolRequestBindingResult<NetworkAdaptersRequest>.Success(new NetworkAdaptersRequest(
            NameContains: reader.OptionalString("name_contains"),
            MaxAdapters: ResolveBoundedOptionLimit(arguments, "max_adapters"),
            TimeoutMs: ResolveTimeoutMs(arguments))));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<NetworkAdaptersRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        if (!NetworkAdapterInventoryQueryExecutor.TryExecute(
                request: new NetworkAdapterInventoryQueryRequest {
                    NameContains = request.NameContains,
                    MaxResults = request.MaxAdapters,
                    Timeout = TimeSpan.FromMilliseconds(request.TimeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Network adapter query failed."));
        }

        var result = queryResult ?? new NetworkAdapterInventoryQueryResult();
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Adapters,
            viewRowsPath: "adapters_view",
            title: "Network adapters (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                meta.Add("timeout_ms", request.TimeoutMs);
            });
        return Task.FromResult(response);
    }
}
