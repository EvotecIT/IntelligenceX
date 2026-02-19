using System;
using System.Collections.Generic;
using System.Linq;
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

    private sealed record AdReplicationConnectionsResult(
        string Mode,
        int Scanned,
        bool Truncated,
        int TotalFiltered,
        string Transport,
        string State,
        string Origin,
        string SummaryBy,
        IReadOnlyList<SiteConnectionInfo> Connections,
        IReadOnlyList<ConnectionSummary> SummaryRows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdReplicationConnectionsTool"/> class.
    /// </summary>
    public AdReplicationConnectionsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var transport = NormalizeValue(ToolArgs.GetOptionalTrimmed(arguments, "transport"), "any");
        var state = NormalizeValue(ToolArgs.GetOptionalTrimmed(arguments, "state"), "any");
        var origin = NormalizeValue(ToolArgs.GetOptionalTrimmed(arguments, "origin"), "any");
        var summary = ToolArgs.GetBoolean(arguments, "summary", defaultValue: false);
        var summaryBy = NormalizeValue(ToolArgs.GetOptionalTrimmed(arguments, "summary_by"), "site");
        var maxResults = ResolveMaxResultsClampToOne(arguments);

        if (!IsOneOf(transport, "any", "rpc", "smtp")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "transport must be one of: any, rpc, smtp."));
        }
        if (!IsOneOf(state, "any", "enabled", "disabled")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "state must be one of: any, enabled, disabled."));
        }
        if (!IsOneOf(origin, "any", "kcc", "user_defined")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "origin must be one of: any, kcc, user_defined."));
        }
        if (!IsOneOf(summaryBy, "site", "server")) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "summary_by must be one of: site, server."));
        }

        if (!TryExecute(
                action: () => ConnectionsExplorer.Get(new ConnectionsQuery {
                Server = ReadStringArrayOrNull(arguments?.GetArray("server")),
                ServerMatch = ReadStringArrayOrNull(arguments?.GetArray("server_match")),
                Site = ReadStringArrayOrNull(arguments?.GetArray("site")),
                SiteMatch = ReadStringArrayOrNull(arguments?.GetArray("site_match")),
                SourceServer = ReadStringArrayOrNull(arguments?.GetArray("source_server")),
                SourceServerMatch = ReadStringArrayOrNull(arguments?.GetArray("source_server_match")),
                Transport = ToTransportFilter(transport),
                State = ToStateFilter(state),
                Origin = ToOriginFilter(origin)
            }),
                result: out IReadOnlyList<SiteConnectionInfo> filtered,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Replication connections query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        if (summary) {
            var allSummary = ConnectionsExplorer.GetSummaryBy(filtered, summaryBy);
            var rows = CapRows(allSummary, maxResults, out var scanned, out var truncated);

            var summaryResult = new AdReplicationConnectionsResult(
                Mode: "summary",
                Scanned: scanned,
                Truncated: truncated,
                TotalFiltered: filtered.Count,
                Transport: transport,
                State: state,
                Origin: origin,
                SummaryBy: summaryBy,
                Connections: Array.Empty<SiteConnectionInfo>(),
                SummaryRows: rows);

            return Task.FromResult(BuildAutoTableResponse(
                arguments: arguments,
                model: summaryResult,
                sourceRows: rows,
                viewRowsPath: "summary_view",
                title: "Active Directory: Replication Connections Summary (preview)",
                maxTop: MaxViewTop,
                baseTruncated: truncated,
                scanned: scanned,
                metaMutate: meta => {
                    meta.Add("mode", "summary");
                    meta.Add("summary_by", summaryBy);
                    meta.Add("total_filtered", filtered.Count);
                    AddMaxResultsMeta(meta, maxResults);
                }));
        }

        var scannedConnections = filtered.Count;
        var connectionRows = scannedConnections > maxResults ? filtered.Take(maxResults).ToArray() : filtered;
        var truncatedConnections = scannedConnections > connectionRows.Count;

        var rawResult = new AdReplicationConnectionsResult(
            Mode: "raw",
            Scanned: scannedConnections,
            Truncated: truncatedConnections,
            TotalFiltered: scannedConnections,
            Transport: transport,
            State: state,
            Origin: origin,
            SummaryBy: summaryBy,
            Connections: connectionRows,
            SummaryRows: Array.Empty<ConnectionSummary>());

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
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

    private static IReadOnlyList<string>? ReadStringArrayOrNull(JsonArray? array) {
        var values = ToolArgs.ReadDistinctStringArray(array);
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
