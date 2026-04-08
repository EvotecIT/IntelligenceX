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

    internal readonly record struct ReplicationStatusBindingContract(
        IReadOnlyList<string> RequestedComputerNames,
        bool HealthOnly);

    private sealed record ReplicationStatusRequest(
        IReadOnlyList<string> RequestedComputerNames,
        bool HealthOnly);

    private static readonly string[] SupportedProjectionColumns = {
        "server",
        "source_dsa",
        "destination_dsa",
        "transport_type",
        "last_successful_sync",
        "last_failure_time",
        "status",
        "failure_message"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_replication_status",
        "Get replication status for selected domain controllers with optional health-only filtering (read-only).",
        ToolSchema.Object(
                ("computer_names", ToolSchema.Array(ToolSchema.String(), "Optional domain controller names. When omitted, all discovered domain controllers are queried.")),
                ("health_only", ToolSchema.Boolean("When true, returns only non-healthy replication rows.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "active_directory",
        tags: new[] {
            "pack:active_directory",
            "intent:replication",
            "intent:replication_status",
            "intent:replikacja",
            "intent:status_replikacji",
            "scope:domain_controller",
            "scope:forest"
        },
        aliases: new[] {
            new ToolAliasDefinition("ad_replication_health", "Inspect AD replication health rows for selected domain controllers."),
            new ToolAliasDefinition("ad_replikacja_status", "Pokaz status replikacji dla kontrolerow domeny."),
            new ToolAliasDefinition("ad_replikacja_kontrolery", "Pokaz status replikacji dla wykrytych kontrolerow domeny.")
        });

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<ReplicationStatusRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader =>
            ToolRequestBindingResult<ReplicationStatusRequest>.Success(new ReplicationStatusRequest(
                RequestedComputerNames: reader.DistinctStringArray("computer_names"),
                HealthOnly: reader.Boolean("health_only"))));
    }

    internal static ToolRequestBindingResult<ReplicationStatusBindingContract> BindRequestContract(JsonObject? arguments) {
        var binding = BindRequest(arguments);
        if (!binding.IsValid || binding.Request is null) {
            return ToolRequestBindingResult<ReplicationStatusBindingContract>.Failure(
                binding.Error,
                binding.ErrorCode,
                binding.Hints,
                binding.IsTransient);
        }

        var request = binding.Request;
        return ToolRequestBindingResult<ReplicationStatusBindingContract>.Success(new ReplicationStatusBindingContract(
            RequestedComputerNames: request.RequestedComputerNames,
            HealthOnly: request.HealthOnly));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ReplicationStatusRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var requestedComputerNames = request.RequestedComputerNames;
        var maxResults = ResolveMaxResults(context.Arguments);

        IReadOnlyList<string> targetServers = requestedComputerNames.Count == 0
            ? DomainHelper.EnumerateDomainControllers().Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : requestedComputerNames;

        if (!TryExecute(
                action: () => StatusExplorer.GetStatusInfos(targetServers, request.HealthOnly),
                result: out IReadOnlyList<ReplicationStatusInfo> allRows,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Replication status query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var rows = CapRows(allRows, maxResults, out var scanned, out var truncated);

        var result = new AdReplicationStatusResult(
            HealthOnly: request.HealthOnly,
            RequestedComputerNames: requestedComputerNames,
            Scanned: scanned,
            Truncated: truncated,
            Rows: rows);

        var shapedArguments = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            context.Arguments,
            SupportedProjectionColumns);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: shapedArguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: Replication Status (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("health_only", request.HealthOnly);
                meta.Add("target_server_count", targetServers.Count);
            }));
    }
}
