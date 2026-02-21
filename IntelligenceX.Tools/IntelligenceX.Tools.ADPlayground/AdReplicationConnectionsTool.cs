using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
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

    internal sealed record AdReplicationConnectionSchedule(
        int Days,
        int HoursPerDay,
        int SlotsPerHour,
        int AllowedSlots,
        int TotalSlots,
        IReadOnlyList<int> AllowedSlotsByDay,
        IReadOnlyList<IReadOnlyList<bool>> AllowedHoursGrid);

    internal sealed record AdReplicationConnectionRow(
        string Name,
        string Site,
        string? SourceServer,
        string DestinationServer,
        ActiveDirectoryTransportType Transport,
        bool Enabled,
        bool GeneratedByKcc,
        bool ReciprocalReplicationEnabled,
        NotificationStatus ChangeNotificationStatus,
        bool DataCompressionEnabled,
        bool ReplicationScheduleOwnedByUser,
        ReplicationSpan ReplicationSpan,
        AdReplicationConnectionSchedule? ReplicationSchedule);

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
        IReadOnlyList<AdReplicationConnectionRow> Connections,
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
        var maxResults = ResolveMaxResults(arguments);

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
                Connections: Array.Empty<AdReplicationConnectionRow>(),
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

        var connectionRows = CapRows(filtered, maxResults, out var scannedConnections, out var truncatedConnections)
            .Select(MapConnectionForResponse)
            .ToArray();

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

    internal static AdReplicationConnectionRow MapConnectionForResponse(SiteConnectionInfo connection) {
        if (connection is null) {
            throw new ArgumentNullException(nameof(connection));
        }

        return new AdReplicationConnectionRow(
            Name: connection.Name,
            Site: connection.Site,
            SourceServer: connection.SourceServer,
            DestinationServer: connection.DestinationServer,
            Transport: connection.Transport,
            Enabled: connection.Enabled,
            GeneratedByKcc: connection.GeneratedByKcc,
            ReciprocalReplicationEnabled: connection.ReciprocalReplicationEnabled,
            ChangeNotificationStatus: connection.ChangeNotificationStatus,
            DataCompressionEnabled: connection.DataCompressionEnabled,
            ReplicationScheduleOwnedByUser: connection.ReplicationScheduleOwnedByUser,
            ReplicationSpan: connection.ReplicationSpan,
            ReplicationSchedule: MapReplicationScheduleForResponse(connection.ReplicationSchedule));
    }

    internal static AdReplicationConnectionSchedule? MapReplicationScheduleForResponse(ActiveDirectorySchedule? schedule) {
        if (schedule is null) {
            return null;
        }

        bool[,,]? rawSchedule;
        try {
            rawSchedule = schedule.RawSchedule;
        } catch {
            // DirectoryServices can throw when schedule materialization fails on partial objects.
            return null;
        }

        if (rawSchedule is null) {
            return null;
        }

        var days = rawSchedule.GetLength(0);
        var hoursPerDay = rawSchedule.GetLength(1);
        var slotsPerHour = rawSchedule.GetLength(2);
        if (days <= 0 || hoursPerDay <= 0 || slotsPerHour <= 0) {
            return null;
        }

        var allowedSlotsByDay = new int[days];
        var allowedHoursGrid = new List<IReadOnlyList<bool>>(days);
        var allowedSlots = 0;

        for (var day = 0; day < days; day++) {
            var hourRow = new bool[hoursPerDay];
            var allowedDaySlots = 0;

            for (var hour = 0; hour < hoursPerDay; hour++) {
                var hourAllowed = false;
                for (var slot = 0; slot < slotsPerHour; slot++) {
                    if (!rawSchedule[day, hour, slot]) {
                        continue;
                    }

                    hourAllowed = true;
                    allowedDaySlots++;
                    allowedSlots++;
                }

                hourRow[hour] = hourAllowed;
            }

            allowedSlotsByDay[day] = allowedDaySlots;
            allowedHoursGrid.Add(hourRow);
        }

        return new AdReplicationConnectionSchedule(
            Days: days,
            HoursPerDay: hoursPerDay,
            SlotsPerHour: slotsPerHour,
            AllowedSlots: allowedSlots,
            TotalSlots: days * hoursPerDay * slotsPerHour,
            AllowedSlotsByDay: allowedSlotsByDay,
            AllowedHoursGrid: allowedHoursGrid);
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
