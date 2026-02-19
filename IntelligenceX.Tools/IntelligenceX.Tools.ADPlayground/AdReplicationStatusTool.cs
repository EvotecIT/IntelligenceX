using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns replication status rows for selected domain controllers (read-only).
/// </summary>
public sealed class AdReplicationStatusTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_replication_status",
        "Get replication status for selected domain controllers with optional health-only filtering (read-only).",
        ToolSchema.Object(
                ("computer_names", ToolSchema.Array(ToolSchema.String(), "Optional domain controller names. When omitted, all discovered domain controllers are queried.")),
                ("health_only", ToolSchema.Boolean("When true, returns only non-healthy replication rows.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdReplicationStatusResult(
        bool HealthOnly,
        IReadOnlyList<string> RequestedComputerNames,
        int Scanned,
        bool Truncated,
        IReadOnlyList<ReplicationStatusInfo> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdReplicationStatusTool"/> class.
    /// </summary>
    public AdReplicationStatusTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var requestedComputerNames = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("computer_names"));
        var healthOnly = ToolArgs.GetBoolean(arguments, "health_only", defaultValue: false);
        var maxResults = ResolveBoundedMaxResults(arguments);

        IReadOnlyList<string> targetServers = requestedComputerNames.Count == 0
            ? DomainHelper.EnumerateDomainControllers().Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : requestedComputerNames;

        if (!TryExecute(
                action: () => StatusExplorer.GetStatusInfos(targetServers, healthOnly),
                result: out IReadOnlyList<ReplicationStatusInfo> allRows,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Replication status query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var rows = CapRows(allRows, maxResults, out var scanned, out var truncated);

        var result = new AdReplicationStatusResult(
            HealthOnly: healthOnly,
            RequestedComputerNames: requestedComputerNames,
            Scanned: scanned,
            Truncated: truncated,
            Rows: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: Replication Status (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("health_only", healthOnly);
                meta.Add("target_server_count", targetServers.Count);
            }));
    }
}

