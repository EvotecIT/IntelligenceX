using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Evtx;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Reads events from an EVTX file (restricted to allowed roots).
/// </summary>
public sealed class EventLogEvtxQueryTool : EventLogToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_evtx_query",
        "Read events from a local .evtx file with basic filters (restricted to allowed roots).",
        ToolSchema.Object(
                ("path", ToolSchema.String("Path to the .evtx file (absolute or relative).")),
                ("event_ids", ToolSchema.Array(ToolSchema.Integer(), "Optional event IDs to include.")),
                ("provider_name", ToolSchema.String("Optional provider name filter.")),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("max_events", ToolSchema.Integer("Optional maximum events to return (capped).")),
                ("oldest_first", ToolSchema.Boolean("If true, read from oldest to newest (default false).")),
                ("include_message", ToolSchema.Boolean("If true, include formatted message text (may be large).")))
            .WithTableViewOptions()
            .Required("path")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogEvtxQueryTool"/> class.
    /// </summary>
    public EventLogEvtxQueryTool(EventLogToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var inputPath = arguments?.GetString("path") ?? string.Empty;
        if (!TryResolveEvtxPath(inputPath, out var fullPath, out var errCode, out var err, out var hints)) {
            return Task.FromResult(ToolResponse.Error(errCode, err, hints: hints, isTransient: false));
        }

        var providerName = arguments?.GetString("provider_name");
        var oldestFirst = arguments?.GetBoolean("oldest_first") ?? false;
        var includeMessage = arguments?.GetBoolean("include_message") ?? false;

        if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", timeErr ?? "Invalid time range."));
        }

        var maxEvents = ToolArgs.GetCappedInt32(arguments, "max_events", Options.MaxResults, 1, Options.MaxResults);

        var eventIds = ToolArgs.TryReadPositiveInt32Array(arguments?.GetArray("event_ids"), "event_ids", out var eventIdsError);
        if (!string.IsNullOrWhiteSpace(eventIdsError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", eventIdsError));
        }

        var request = new EvtxQueryRequest {
            FilePath = fullPath,
            EventIds = eventIds,
            ProviderName = providerName,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            MaxEvents = maxEvents,
            OldestFirst = oldestFirst
        };

        if (!EvtxEventReportBuilder.TryBuild(
                request: request,
                includeMessage: includeMessage,
                maxMessageChars: Options.MaxMessageChars,
                report: out var root,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromEvtxFailure(failure));
        }

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: root,
            sourceRows: root.Events,
            viewRowsPath: "events_view",
            title: "Events (preview)",
            maxTop: MaxViewTop,
            baseTruncated: root.Truncated,
            response: out var response);
        return Task.FromResult(response);
    }
}

