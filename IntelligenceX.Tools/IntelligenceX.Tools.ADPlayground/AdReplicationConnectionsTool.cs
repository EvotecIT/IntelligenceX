using System;
using System.Collections.Generic;
using System.Text;
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
    private const int MermaidRenderPriority = 500;
    private const int NetworkRenderPriority = 450;

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
            .NoAdditionalProperties(),
        category: "active_directory",
        tags: new[] {
            "pack:active_directory",
            "intent:replication",
            "intent:replication_topology",
            "intent:replikacja",
            "intent:topologia_replikacji",
            "artifact:diagram",
            "artifact:topology"
        },
        aliases: new[] {
            new ToolAliasDefinition("ad_replication_topology", "List AD replication topology edges and connection objects."),
            new ToolAliasDefinition("ad_replikacja_topologia", "Pokaz topologie replikacji Active Directory."),
            new ToolAliasDefinition("ad_replikacja_wykres", "Przygotuj dane do wykresu topologii replikacji.")
        });

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

    internal sealed record TopologyArtifacts(JsonObject Graph, string MermaidSource);

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
        var topologyArtifacts = BuildTopologyArtifacts(cappedConnections);

        return Task.FromResult(BuildRawTopologyResponse(
            arguments: context.Arguments,
            request: request,
            maxResults: maxResults,
            scannedConnections: scannedConnections,
            truncatedConnections: truncatedConnections,
            connectionRows: connectionRows,
            topologyArtifacts: topologyArtifacts));
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

    private static string BuildRawTopologyResponse(
        JsonObject? arguments,
        AdReplicationConnectionsRequest request,
        int maxResults,
        int scannedConnections,
        bool truncatedConnections,
        IReadOnlyList<SiteConnectionSerializableRow> connectionRows,
        TopologyArtifacts topologyArtifacts) {
        var columnKeys = ToolAutoTableColumns.GetColumnKeys<SiteConnectionSerializableRow>();
        if (!ToolTableView.TryParse(arguments, columnKeys, maxTop: MaxViewTop, out var view, out var viewError)) {
            var error = string.IsNullOrWhiteSpace(viewError) ? "Invalid tabular view arguments." : viewError!;
            var hints = new List<string> {
                "Use only listed columns for projection.",
                "Use sort_direction as 'asc' or 'desc'.",
                "If projection keeps failing, retry without columns/sort_by/sort_direction/top."
            };
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: error,
                hints: hints,
                isTransient: false);
        }

        var viewResult = ToolTableView.Apply(
            sourceRows: connectionRows,
            request: view,
            columnSpecs: ToolAutoTableColumns.GetColumnSpecs<SiteConnectionSerializableRow>(),
            previewMaxRows: 20);

        var root = ToolJson.ToJsonObjectSnakeCase(new {
            mode = "raw",
            scanned = scannedConnections,
            truncated = truncatedConnections,
            total_filtered = scannedConnections,
            transport = request.Transport,
            state = request.State,
            origin = request.Origin,
            summary_by = request.SummaryBy,
            connections = connectionRows,
            summary_rows = Array.Empty<ConnectionSummary>(),
            topology_graph = topologyArtifacts.Graph,
            mermaid_source = topologyArtifacts.MermaidSource
        });
        root.Add("connections_view", viewResult.Rows);

        var meta = ToolOutputHints.Meta(
            count: viewResult.Count,
            truncated: truncatedConnections || viewResult.TruncatedByView,
            scanned: scannedConnections,
            previewCount: viewResult.PreviewRows.Count);
        meta.Add("available_columns", new JsonArray().AddRange(columnKeys));
        meta.Add("mode", "raw");
        AddMaxResultsMeta(meta, maxResults);

        var summaryMarkdown = ToolMarkdownContract.Create()
            .AddTable(
                title: "Active Directory: Replication Connections (preview)",
                headers: BuildColumnLabels(viewResult.Columns),
                rows: viewResult.PreviewRows,
                totalCount: viewResult.Count,
                truncated: truncatedConnections || viewResult.TruncatedByView)
            .AddMermaid(topologyArtifacts.MermaidSource, title: "Replication Topology")
            .Build();

        var render = new JsonArray()
            .Add(ToolOutputHints.RenderMermaid("mermaid_source").Add("priority", MermaidRenderPriority))
            .Add(ToolOutputHints.RenderNetwork("topology_graph").Add("priority", NetworkRenderPriority))
            .Add(ToolOutputHints.RenderTable("connections_view", viewResult.Columns.ToArray()));

        return ToolResultV2.OkFlatWithRenderValue(
            root: root,
            meta: meta,
            summaryMarkdown: summaryMarkdown,
            render: JsonValue.From(render));
    }

    private static IReadOnlyList<string> BuildColumnLabels(IReadOnlyList<ToolColumn> columns) {
        if (columns is null || columns.Count == 0) {
            return Array.Empty<string>();
        }

        var labels = new string[columns.Count];
        for (var i = 0; i < columns.Count; i++) {
            labels[i] = string.IsNullOrWhiteSpace(columns[i].Label) ? columns[i].Key : columns[i].Label;
        }

        return labels;
    }

    internal static TopologyArtifacts BuildTopologyArtifacts(IReadOnlyList<SiteConnectionInfo> connections) {
        var nodes = new JsonArray();
        var edges = new JsonArray();
        var nodeIdsByServer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nodeLabelsByServer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var edgeCountsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var mermaidNodeLines = new List<string>();
        var mermaidEdgeLines = new List<string>();

        if (connections is not null) {
            for (var i = 0; i < connections.Count; i++) {
                var connection = connections[i];
                var sourceServer = NormalizeTopologyLabel(connection.SourceServer);
                var destinationServer = NormalizeTopologyLabel(connection.DestinationServer);
                if (sourceServer.Length == 0 || destinationServer.Length == 0) {
                    continue;
                }

                var sourceNodeId = EnsureTopologyNode(
                    serverName: sourceServer,
                    siteName: NormalizeTopologyLabel(connection.SourceSite),
                    nodes: nodes,
                    nodeIdsByServer: nodeIdsByServer,
                    nodeLabelsByServer: nodeLabelsByServer,
                    mermaidNodeLines: mermaidNodeLines);
                var destinationNodeId = EnsureTopologyNode(
                    serverName: destinationServer,
                    siteName: NormalizeTopologyLabel(connection.Site),
                    nodes: nodes,
                    nodeIdsByServer: nodeIdsByServer,
                    nodeLabelsByServer: nodeLabelsByServer,
                    mermaidNodeLines: mermaidNodeLines);

                var transport = NormalizeTopologyLabel(connection.Transport.ToString());
                var edgeKey = sourceServer + "|" + destinationServer + "|" + transport;
                edgeCountsByKey.TryGetValue(edgeKey, out var edgeCount);
                edgeCount++;
                edgeCountsByKey[edgeKey] = edgeCount;

                var edgeLabel = BuildEdgeLabel(
                    transport: transport,
                    edgeCount: edgeCount,
                    enabled: connection.Enabled,
                    generatedByKcc: connection.GeneratedByKcc);
                edges.Add(new JsonObject(StringComparer.Ordinal)
                    .Add("id", BuildTopologyEdgeId(sourceNodeId, destinationNodeId, transport, edgeCount))
                    .Add("source", sourceNodeId)
                    .Add("target", destinationNodeId)
                    .Add("source_server", sourceServer)
                    .Add("destination_server", destinationServer)
                    .Add("source_site", NormalizeTopologyLabel(connection.SourceSite))
                    .Add("site", NormalizeTopologyLabel(connection.Site))
                    .Add("transport", transport)
                    .Add("enabled", connection.Enabled)
                    .Add("generated_by_kcc", connection.GeneratedByKcc)
                    .Add("replication_span", NormalizeTopologyLabel(connection.ReplicationSpan.ToString()))
                    .Add("label", edgeLabel)
                    .Add("count", edgeCount));
                mermaidEdgeLines.Add($"    {sourceNodeId} -->|{EscapeMermaidLabel(edgeLabel)}| {destinationNodeId}");
            }
        }

        if (nodes.Count == 0) {
            nodes.Add(new JsonObject(StringComparer.Ordinal)
                .Add("id", "replication_topology_empty")
                .Add("label", "No replication connections matched the current filters")
                .Add("site", string.Empty));
            mermaidNodeLines.Add("    replication_topology_empty[\"No replication connections matched the current filters\"]");
        }

        var graph = new JsonObject(StringComparer.Ordinal)
            .Add("nodes", nodes)
            .Add("edges", edges)
            .Add("node_count", nodes.Count)
            .Add("edge_count", edges.Count);

        return new TopologyArtifacts(
            Graph: graph,
            MermaidSource: BuildMermaidDocument(mermaidNodeLines, mermaidEdgeLines));
    }

    private static string EnsureTopologyNode(
        string serverName,
        string siteName,
        JsonArray nodes,
        IDictionary<string, string> nodeIdsByServer,
        IDictionary<string, string> nodeLabelsByServer,
        ICollection<string> mermaidNodeLines) {
        if (nodeIdsByServer.TryGetValue(serverName, out var existingId)) {
            return existingId;
        }

        var nodeId = BuildMermaidNodeId(serverName, nodeIdsByServer.Count);
        var label = BuildTopologyNodeLabel(serverName, siteName);
        nodeIdsByServer[serverName] = nodeId;
        nodeLabelsByServer[serverName] = label;
        nodes.Add(new JsonObject(StringComparer.Ordinal)
            .Add("id", nodeId)
            .Add("label", label)
            .Add("site", siteName));
        mermaidNodeLines.Add($"    {nodeId}[\"{EscapeMermaidLabel(label)}\"]");
        return nodeId;
    }

    private static string BuildTopologyNodeLabel(string serverName, string siteName) {
        if (siteName.Length == 0) {
            return serverName;
        }

        return $"{serverName} ({siteName})";
    }

    private static string BuildTopologyEdgeId(string sourceNodeId, string destinationNodeId, string transport, int edgeCount) {
        var normalizedTransport = transport.Length == 0 ? "connection" : transport;
        var builder = new StringBuilder(sourceNodeId.Length + destinationNodeId.Length + normalizedTransport.Length + 16);
        builder.Append(sourceNodeId)
            .Append('_')
            .Append(destinationNodeId)
            .Append('_');
        for (var i = 0; i < normalizedTransport.Length; i++) {
            var character = normalizedTransport[i];
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        builder.Append('_').Append(edgeCount);
        return builder.ToString();
    }

    private static string BuildEdgeLabel(string transport, int edgeCount, bool enabled, bool generatedByKcc) {
        var builder = new StringBuilder();
        builder.Append(transport.Length == 0 ? "connection" : transport);
        if (edgeCount > 1) {
            builder.Append(" x").Append(edgeCount);
        }

        builder.Append(enabled ? " enabled" : " disabled");
        builder.Append(generatedByKcc ? " kcc" : " manual");
        return builder.ToString();
    }

    private static string BuildMermaidDocument(
        IReadOnlyCollection<string> nodeLines,
        IReadOnlyCollection<string> edgeLines) {
        var builder = new StringBuilder();
        builder.AppendLine("flowchart LR");
        foreach (var line in nodeLines) {
            builder.AppendLine(line);
        }

        foreach (var line in edgeLines) {
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildMermaidNodeId(string serverName, int index) {
        var builder = new StringBuilder(serverName.Length + 8);
        for (var i = 0; i < serverName.Length; i++) {
            var character = serverName[i];
            if (char.IsLetterOrDigit(character)) {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            builder.Append('_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0])) {
            builder.Insert(0, "node_");
        }

        builder.Append('_').Append(index);
        return builder.ToString();
    }

    private static string NormalizeTopologyLabel(string? value) {
        return (value ?? string.Empty).Trim();
    }

    private static string EscapeMermaidLabel(string value) {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}
