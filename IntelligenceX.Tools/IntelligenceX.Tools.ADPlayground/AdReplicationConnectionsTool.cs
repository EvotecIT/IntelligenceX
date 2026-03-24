using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists AD replication connection objects with filters and optional grouping summary (read-only).
/// </summary>
public sealed class AdReplicationConnectionsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_replication_connections",
        "List inbound AD replication connections (nTDSConnection) with filters and optional grouping summary (read-only).",
        ToolSchema.Object(
                ("server", ToolSchema.Array(ToolSchema.String(), "Exact destination server names to include.")),
                ("server_match", ToolSchema.Array(ToolSchema.String(), "Wildcard patterns for destination server names.")),
                ("site", ToolSchema.Array(ToolSchema.String(), "Exact site names to include.")),
                ("site_match", ToolSchema.Array(ToolSchema.String(), "Wildcard patterns for site names.")),
                ("source_server", ToolSchema.Array(ToolSchema.String(), "Exact source server names to include.")),
                ("source_server_match", ToolSchema.Array(ToolSchema.String(), "Wildcard patterns for source server names.")),
                ("transport", ToolSchema.String("Transport filter.").Enum("any", "rpc", "smtp")),
                ("state", ToolSchema.String("Enabled state filter.").Enum("any", "enabled", "disabled")),
                ("origin", ToolSchema.String("Connection origin filter.").Enum("any", "kcc", "user_defined")),
                ("summary", ToolSchema.Boolean("When true, emits grouped summary rows instead of raw connection rows.")),
                ("summary_by", ToolSchema.String("Summary grouping key used when summary=true.").Enum("site", "server")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdReplicationConnectionsRequest(
        IReadOnlyList<string>? Server,
        IReadOnlyList<string>? ServerMatch,
        IReadOnlyList<string>? Site,
        IReadOnlyList<string>? SiteMatch,
        IReadOnlyList<string>? SourceServer,
        IReadOnlyList<string>? SourceServerMatch,
        string Transport,
        string State,
        string Origin,
        bool Summary,
        string SummaryBy);

    private sealed record AdReplicationConnectionsResult(
        string Mode,
        int Scanned,
        bool Truncated,
        int TotalFiltered,
        string Transport,
        string State,
        string Origin,
        string SummaryBy,
        IReadOnlyList<SiteConnectionSerializableRow> Connections,
        IReadOnlyList<ConnectionSummary> SummaryRows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdReplicationConnectionsTool"/> class.
    /// </summary>
    public AdReplicationConnectionsTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<AdReplicationConnectionsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var transport = NormalizeValue(reader.OptionalString("transport"), "any");
            var state = NormalizeValue(reader.OptionalString("state"), "any");
            var origin = NormalizeValue(reader.OptionalString("origin"), "any");
            var summaryBy = NormalizeValue(reader.OptionalString("summary_by"), "site");

            if (!IsOneOf(transport, "any", "rpc", "smtp")) {
                return ToolRequestBindingResult<AdReplicationConnectionsRequest>.Failure("transport must be one of: any, rpc, smtp.");
            }
            if (!IsOneOf(state, "any", "enabled", "disabled")) {
                return ToolRequestBindingResult<AdReplicationConnectionsRequest>.Failure("state must be one of: any, enabled, disabled.");
            }
            if (!IsOneOf(origin, "any", "kcc", "user_defined")) {
                return ToolRequestBindingResult<AdReplicationConnectionsRequest>.Failure("origin must be one of: any, kcc, user_defined.");
            }
            if (!IsOneOf(summaryBy, "site", "server")) {
                return ToolRequestBindingResult<AdReplicationConnectionsRequest>.Failure("summary_by must be one of: site, server.");
            }

            return ToolRequestBindingResult<AdReplicationConnectionsRequest>.Success(new AdReplicationConnectionsRequest(
                Server: ToNullableList(reader.DistinctStringArray("server")),
                ServerMatch: ToNullableList(reader.DistinctStringArray("server_match")),
                Site: ToNullableList(reader.DistinctStringArray("site")),
                SiteMatch: ToNullableList(reader.DistinctStringArray("site_match")),
                SourceServer: ToNullableList(reader.DistinctStringArray("source_server")),
                SourceServerMatch: ToNullableList(reader.DistinctStringArray("source_server_match")),
                Transport: transport,
                State: state,
                Origin: origin,
                Summary: reader.Boolean("summary", defaultValue: false),
                SummaryBy: summaryBy));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<AdReplicationConnectionsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var maxResults = ResolveMaxResults(context.Arguments);

        if (!TryExecute(
                action: () => ConnectionsExplorer.Get(new ConnectionsQuery {
                Server = request.Server,
                ServerMatch = request.ServerMatch,
                Site = request.Site,
                SiteMatch = request.SiteMatch,
                SourceServer = request.SourceServer,
                SourceServerMatch = request.SourceServerMatch,
                Transport = ToTransportFilter(request.Transport),
                State = ToStateFilter(request.State),
                Origin = ToOriginFilter(request.Origin)
            }),
                result: out IReadOnlyList<SiteConnectionInfo> filtered,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Replication connections query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        if (request.Summary) {
            var allSummary = ConnectionsExplorer.GetSummaryBy(filtered, request.SummaryBy);
            var rows = CapRows(allSummary, maxResults, out var scanned, out var truncated);

            var summaryResult = new AdReplicationConnectionsResult(
                Mode: "summary",
                Scanned: scanned,
                Truncated: truncated,
                TotalFiltered: filtered.Count,
                Transport: request.Transport,
                State: request.State,
                Origin: request.Origin,
                SummaryBy: request.SummaryBy,
                Connections: Array.Empty<SiteConnectionSerializableRow>(),
                SummaryRows: rows);

            return Task.FromResult(ToolResultV2.OkAutoTableResponse(
                arguments: context.Arguments,
                model: summaryResult,
                sourceRows: rows,
                viewRowsPath: "summary_view",
                title: "Active Directory: Replication Connections Summary (preview)",
                maxTop: MaxViewTop,
                baseTruncated: truncated,
                scanned: scanned,
                metaMutate: meta => {
                    meta.Add("mode", "summary");
                    meta.Add("summary_by", request.SummaryBy);
                    meta.Add("total_filtered", filtered.Count);
                    AddMaxResultsMeta(meta, maxResults);
                }));
        }

        var cappedConnections = CapRows(filtered, maxResults, out var scannedConnections, out var truncatedConnections);
        var connectionRows = ConnectionsExplorer.ProjectSerializableRows(cappedConnections);

        var rawResult = new AdReplicationConnectionsResult(
            Mode: "raw",
            Scanned: scannedConnections,
            Truncated: truncatedConnections,
            TotalFiltered: scannedConnections,
            Transport: request.Transport,
            State: request.State,
            Origin: request.Origin,
            SummaryBy: request.SummaryBy,
            Connections: connectionRows,
            SummaryRows: Array.Empty<ConnectionSummary>());

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: rawResult,
            sourceRows: connectionRows,
            viewRowsPath: "connections_view",
            title: "Active Directory: Replication Connections (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncatedConnections,
            scanned: scannedConnections,
            metaMutate: meta => {
                meta.Add("mode", "raw");
                AddMaxResultsMeta(meta, maxResults);
            }));
    }

    private static IReadOnlyList<string>? ToNullableList(IReadOnlyList<string> values) {
        return values.Count == 0 ? null : values;
    }

    private static bool IsOneOf(string value, params string[] allowed) {
        for (var i = 0; i < allowed.Length; i++) {
            if (string.Equals(value, allowed[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeValue(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }

        return value.Trim().ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
    }

    private static string ToTransportFilter(string normalizedTransport) {
        return normalizedTransport switch {
            "rpc" => "Rpc",
            "smtp" => "Smtp",
            _ => "Any"
        };
    }

    private static string ToStateFilter(string normalizedState) {
        return normalizedState switch {
            "enabled" => "Enabled",
            "disabled" => "Disabled",
            _ => "Any"
        };
    }

    private static string ToOriginFilter(string normalizedOrigin) {
        return normalizedOrigin switch {
            "kcc" => "Kcc",
            "user_defined" => "UserDefined",
            _ => "Any"
        };
    }
}
