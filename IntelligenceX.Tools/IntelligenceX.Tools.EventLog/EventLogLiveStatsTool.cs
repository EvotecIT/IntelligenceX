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
    private sealed record LiveStatsRequest(
        string LogName,
        string? MachineName,
        string XPath,
        bool OldestFirst,
        DateTime? StartUtc,
        DateTime? EndUtc,
        int MaxEventsScanned,
        int TopEventIds,
        int TopProviders,
        int TopLevels,
        int TopComputers,
        int? SessionTimeoutMs);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<LiveStatsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("log_name", out var logName, out var logNameError)) {
                return ToolRequestBindingResult<LiveStatsRequest>.Failure(logNameError);
            }

            if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
                return ToolRequestBindingResult<LiveStatsRequest>.Failure(timeErr ?? "Invalid time range.");
            }

            // Preserve existing behavior: default scales with Options.MaxResults; caller value is capped.
            var maxScanDefault = Math.Min(MaxScanCap, Options.MaxResults * 10);
            var request = new LiveStatsRequest(
                LogName: logName,
                MachineName: reader.OptionalString("machine_name"),
                XPath: ResolveXPathOrDefault(reader.OptionalString("xpath")),
                OldestFirst: reader.Boolean("oldest_first", defaultValue: false),
                StartUtc: startUtc,
                EndUtc: endUtc,
                MaxEventsScanned: reader.CappedInt32("max_events_scanned", maxScanDefault, 1, MaxScanCap),
                TopEventIds: ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_event_ids", DefaultTop, MaxTop),
                TopProviders: ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_providers", DefaultTop, MaxTop),
                TopLevels: ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_levels", DefaultTop, MaxTop),
                TopComputers: ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_computers", DefaultTop, MaxTop),
                SessionTimeoutMs: ResolveSessionTimeoutMs(
                    TryReadOptionalInt64(arguments, "session_timeout_ms"),
                    minInclusive: MinSessionTimeoutMs,
                    maxInclusive: MaxSessionTimeoutMs));
            return ToolRequestBindingResult<LiveStatsRequest>.Success(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<LiveStatsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        if (!LiveStatsQueryExecutor.TryBuild(
                request: new LiveStatsQueryRequest {
                    LogName = request.LogName,
                    MachineName = request.MachineName,
                    XPath = request.XPath,
                    MaxEventsScanned = request.MaxEventsScanned,
                    OldestFirst = request.OldestFirst,
                    StartTimeUtc = request.StartUtc,
                    EndTimeUtc = request.EndUtc,
                    TopEventIds = request.TopEventIds,
                    TopProviders = request.TopProviders,
                    TopLevels = request.TopLevels,
                    TopComputers = request.TopComputers,
                    SessionTimeoutMs = request.SessionTimeoutMs
                },
                result: out var result,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromLiveStatsFailure(
                failure: failure,
                machineName: request.MachineName,
                logName: request.LogName));
        }

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.TopEventIds,
            viewRowsPath: "top_event_ids_view",
            title: "Live stats: top Event IDs (preview)",
            baseTruncated: result.Truncated,
            scanned: result.ScannedEvents,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                meta
                    .Add("matched_events", result.MatchedEvents)
                    .Add("max_events_scanned", result.MaxEventsScanned);
                AddReadOnlyTriageChainingMeta(
                    meta: meta,
                    currentTool: "eventlog_live_stats",
                    logName: request.LogName,
                    machineName: request.MachineName,
                    suggestedMaxEvents: request.MaxEventsScanned,
                    scanned: result.ScannedEvents,
                    truncated: result.Truncated,
                    queryMode: "stats");
            });
        return Task.FromResult(response);
    }

    private static long? TryReadOptionalInt64(JsonObject? arguments, string key) {
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        foreach (var kv in arguments) {
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return kv.Value.AsInt64();
        }

        return null;
    }

    private static string ResolveXPathOrDefault(string? xpath) {
        return string.IsNullOrWhiteSpace(xpath) ? "*" : xpath;
    }
}
