using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Live;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Aggregates basic statistics from a local Windows Event Log channel (read-only).
/// </summary>
public sealed class EventLogLiveStatsTool : EventLogToolBase, ITool {
    private const int DefaultTop = 10;
    private const int MaxTop = 50;
    private const int MaxScanCap = 5000;
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_live_stats",
        "Aggregate event statistics from a Windows Event Log (local or remote machine).",
        ToolSchema.Object(
                ("log_name", ToolSchema.String("Windows Event Log name (for example: System, Security, Application).")),
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for local machine.")),
                ("xpath", ToolSchema.String("Optional XPath query (default: '*').")),
                ("max_events_scanned", ToolSchema.Integer("Maximum number of events to scan (capped).")),
                ("oldest_first", ToolSchema.Boolean("If true, read from oldest to newest (default false).")),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("top_event_ids", ToolSchema.Integer("How many top Event IDs to return (capped).")),
                ("top_providers", ToolSchema.Integer("How many top providers to return (capped).")),
                ("top_levels", ToolSchema.Integer("How many top levels to return (capped).")),
                ("top_computers", ToolSchema.Integer("How many top computers to return (capped).")),
                ("session_timeout_ms", ToolSchema.Integer("Optional remote session timeout in milliseconds (capped).")))
            .WithTableViewOptions()
            .Required("log_name")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogLiveStatsTool"/> class.
    /// </summary>
    public EventLogLiveStatsTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var logName = arguments?.GetString("log_name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(logName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "log_name is required."));
        }

        var xpath = ResolveXPathOrDefault(arguments);

        var oldestFirst = arguments?.GetBoolean("oldest_first") ?? false;
        var machineName = ToolArgs.GetOptionalTrimmed(arguments, "machine_name");
        var sessionTimeoutMs = ResolveSessionTimeoutMs(arguments, minInclusive: MinSessionTimeoutMs, maxInclusive: MaxSessionTimeoutMs);

        if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", timeErr ?? "Invalid time range."));
        }

        // Preserve existing behavior: default scales with Options.MaxResults; caller value is capped.
        var maxScanDefault = Math.Min(MaxScanCap, Options.MaxResults * 10);
        var maxScan = ToolArgs.GetCappedInt32(arguments, "max_events_scanned", maxScanDefault, 1, MaxScanCap);

        var topEventIds = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_event_ids", DefaultTop, MaxTop);
        var topProviders = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_providers", DefaultTop, MaxTop);
        var topLevels = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_levels", DefaultTop, MaxTop);
        var topComputers = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_computers", DefaultTop, MaxTop);

        if (!LiveStatsQueryExecutor.TryBuild(
                request: new LiveStatsQueryRequest {
                    LogName = logName,
                    MachineName = machineName,
                    XPath = xpath,
                    MaxEventsScanned = maxScan,
                    OldestFirst = oldestFirst,
                    StartTimeUtc = startUtc,
                    EndTimeUtc = endUtc,
                    TopEventIds = topEventIds,
                    TopProviders = topProviders,
                    TopLevels = topLevels,
                    TopComputers = topComputers,
                    SessionTimeoutMs = sessionTimeoutMs
                },
                result: out var result,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromLiveStatsFailure(
                failure: failure,
                machineName: machineName,
                logName: logName));
        }

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.TopEventIds,
            viewRowsPath: "top_event_ids_view",
            title: "Live stats: top Event IDs (preview)",
            baseTruncated: result.Truncated,
            scanned: result.ScannedEvents,
            maxTop: MaxViewTop,
            metaMutate: meta => meta
                .Add("matched_events", result.MatchedEvents)
                .Add("max_events_scanned", result.MaxEventsScanned));
        return Task.FromResult(response);
    }
}
