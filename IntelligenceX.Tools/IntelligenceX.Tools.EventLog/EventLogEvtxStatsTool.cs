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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<EvtxStatsQueryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("path", out var inputPath, out var pathError)) {
                return ToolRequestBindingResult<EvtxStatsQueryRequest>.Failure(pathError);
            }

            if (!TryResolveEvtxPath(inputPath, out var fullPath, out var errCode, out var err, out var hints)) {
                return ToolRequestBindingResult<EvtxStatsQueryRequest>.Failure(
                    error: err,
                    errorCode: errCode,
                    hints: hints,
                    isTransient: false);
            }

            if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
                return ToolRequestBindingResult<EvtxStatsQueryRequest>.Failure(timeErr ?? "Invalid time range.");
            }

            // Preserve existing behavior: default scales with Options.MaxResults; caller value is capped.
            var maxScanCap = Math.Min(MaxScanCap, Options.MaxResults * 500);
            var maxScanDefault = Math.Min(MaxScanCap, Options.MaxResults * 50);
            var maxScan = reader.CappedInt32("max_events_scanned", maxScanDefault, 1, maxScanCap);

            if (!TryReadPositiveInt32Array(arguments, "event_ids", out var eventIds, out var eventIdsError)) {
                return ToolRequestBindingResult<EvtxStatsQueryRequest>.Failure(eventIdsError ?? "event_ids is invalid.");
            }

            var request = new EvtxStatsQueryRequest {
                FilePath = fullPath,
                EventIds = eventIds,
                ProviderName = reader.OptionalString("provider_name"),
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                MaxEventsScanned = maxScan,
                OldestFirst = reader.Boolean("oldest_first", defaultValue: false),
                TopEventIds = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_event_ids", DefaultTop, MaxTop),
                TopProviders = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_providers", DefaultTop, MaxTop),
                TopLevels = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_levels", DefaultTop, MaxTop),
                TopComputers = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "top_computers", DefaultTop, MaxTop)
            };

            return ToolRequestBindingResult<EvtxStatsQueryRequest>.Success(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<EvtxStatsQueryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        if (!EvtxStatsQueryExecutor.TryBuild(request, out var result, out var failure, cancellationToken)) {
            return Task.FromResult(ErrorFromEvtxFailure(failure));
        }

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
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

    private static bool TryReadPositiveInt32Array(
        JsonObject? arguments,
        string key,
        out IReadOnlyList<int>? values,
        out string? error) {
        values = null;
        error = null;

        if (!TryGetArray(arguments, key, out var array)) {
            return true;
        }

        values = ToolArgs.TryReadPositiveInt32Array(array, key, out error);
        return string.IsNullOrWhiteSpace(error);
    }

    private static bool TryGetArray(JsonObject? arguments, string key, out JsonArray? array) {
        array = null;
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return false;
        }

        foreach (var kv in arguments) {
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            array = kv.Value.AsArray();
            return array is not null;
        }

        return false;
    }
}
