using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Stats;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Aggregates basic statistics from an EVTX file (restricted to allowed roots).
/// </summary>
public sealed class EventLogEvtxStatsTool : EventLogToolBase, ITool {
    private const int DefaultTop = 10;
    private const int MaxTop = 50;
    private const int MaxScanCap = 100000;
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_evtx_stats",
        "Aggregate event statistics from a local .evtx file with basic filters (restricted to allowed roots).",
        ToolSchema.Object(
                ("path", ToolSchema.String("Path to the .evtx file (absolute or relative).")),
                ("event_ids", ToolSchema.Array(ToolSchema.Integer(), "Optional event IDs to include.")),
                ("provider_name", ToolSchema.String("Optional provider name filter.")),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("max_events_scanned", ToolSchema.Integer("Maximum number of events to scan (capped).")),
                ("oldest_first", ToolSchema.Boolean("If true, scan from oldest to newest (default false).")),
                ("top_event_ids", ToolSchema.Integer("How many top Event IDs to return (capped).")),
                ("top_providers", ToolSchema.Integer("How many top providers to return (capped).")),
                ("top_levels", ToolSchema.Integer("How many top levels to return (capped).")),
                ("top_computers", ToolSchema.Integer("How many top computers to return (capped).")))
            .WithTableViewOptions()
            .Required("path")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogEvtxStatsTool"/> class.
    /// </summary>
    public EventLogEvtxStatsTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var inputPath = arguments?.GetString("path") ?? string.Empty;
        if (!TryResolveEvtxPath(inputPath, out var fullPath, out var errCode, out var err, out var hints)) {
            return Task.FromResult(ToolResponse.Error(errCode, err, hints: hints, isTransient: false));
        }

        var providerName = arguments?.GetString("provider_name");
        var oldestFirst = arguments?.GetBoolean("oldest_first") ?? false;

        if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", timeErr ?? "Invalid time range."));
        }

        // Preserve existing behavior: default scales with Options.MaxResults; caller value is capped.
        var maxScanCap = Math.Min(MaxScanCap, Options.MaxResults * 500);
        var maxScanDefault = Math.Min(MaxScanCap, Options.MaxResults * 50);
        var maxScan = ToolArgs.GetCappedInt32(arguments, "max_events_scanned", maxScanDefault, 1, maxScanCap);

        var eventIds = ToolArgs.TryReadPositiveInt32Array(arguments?.GetArray("event_ids"), "event_ids", out var eventIdsError);
        if (!string.IsNullOrWhiteSpace(eventIdsError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", eventIdsError));
        }

        var topEventIds = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_event_ids", DefaultTop, MaxTop);
        var topProviders = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_providers", DefaultTop, MaxTop);
        var topLevels = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_levels", DefaultTop, MaxTop);
        var topComputers = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_computers", DefaultTop, MaxTop);

        var request = new EvtxStatsQueryRequest {
            FilePath = fullPath,
            EventIds = eventIds,
            ProviderName = providerName,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            MaxEventsScanned = maxScan,
            OldestFirst = oldestFirst,
            TopEventIds = topEventIds,
            TopProviders = topProviders,
            TopLevels = topLevels,
            TopComputers = topComputers
        };

        if (!EvtxStatsQueryExecutor.TryBuild(request, out var result, out var failure, cancellationToken)) {
            return Task.FromResult(ErrorFromEvtxFailure(failure));
        }

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.TopEventIds,
            viewRowsPath: "top_event_ids_view",
            title: "Top Event IDs (preview)",
            baseTruncated: result.Truncated,
            scanned: result.ScannedEvents,
            maxTop: MaxViewTop,
            metaMutate: meta => meta.Add("max_events_scanned", result.MaxEventsScanned));
        return Task.FromResult(response);
    }
}
