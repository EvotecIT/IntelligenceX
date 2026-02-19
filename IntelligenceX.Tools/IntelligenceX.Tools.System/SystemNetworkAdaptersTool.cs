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
        cancellationToken.ThrowIfCancellationRequested();

        var nameContains = ToolArgs.GetOptionalTrimmed(arguments, "name_contains");
        var max = ResolveBoundedOptionLimit(arguments, "max_adapters");
        var timeoutMs = ResolveTimeoutMs(arguments);

        if (!NetworkAdapterInventoryQueryExecutor.TryExecute(
                request: new NetworkAdapterInventoryQueryRequest {
                    NameContains = nameContains,
                    MaxResults = max,
                    Timeout = TimeSpan.FromMilliseconds(timeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Network adapter query failed."));
        }

        var result = queryResult ?? new NetworkAdapterInventoryQueryResult();
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Adapters,
            viewRowsPath: "adapters_view",
            title: "Network adapters (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                meta.Add("timeout_ms", timeoutMs);
            });
        return Task.FromResult(response);
    }
}
