using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using EventViewerX.Reports.Live;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Reads events from a local Windows Event Log channel using an XPath query.
/// </summary>
public sealed class EventLogLiveQueryTool : EventLogToolBase, ITool {
    private const int MaxViewTop = 5000;
    private sealed record LiveQueryRequest(
        string LogName,
        string? XPathRaw,
        List<int>? EventIds,
        string? ProviderName,
        DateTime? StartUtc,
        DateTime? EndUtc,
        Level? Level,
        Keywords? Keywords,
        string? UserId,
        List<long>? RecordIds,
        Hashtable? NamedDataFilter,
        Hashtable? NamedDataExcludeFilter,
        bool HasStructuredFilters,
        string XPath,
        bool OldestFirst,
        bool IncludeMessage,
        string? MachineName,
        int? SessionTimeoutMs,
        int MaxEvents);

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_live_query",
        "Read events from a Windows Event Log (local or remote machine) using log name + optional XPath filter.",
        ToolSchema.Object(
                ("log_name", ToolSchema.String("Windows Event Log name (for example: System, Security, Application).")),
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for local machine.")),
                ("xpath", ToolSchema.String("Optional XPath query (default: '*').")),
                ("event_ids", ToolSchema.Array(ToolSchema.Integer(), "Optional event IDs to include (structured filter mode).")),
                ("provider_name", ToolSchema.String("Optional provider name filter (structured filter mode).")),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (structured filter mode).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (structured filter mode).")),
                ("level", ToolSchema.String("Optional event level filter (structured filter mode).").Enum(EventLogStructuredFilters.LevelNames)),
                ("keywords", ToolSchema.String("Optional event keyword filter (structured filter mode).").Enum(EventLogStructuredFilters.KeywordNames)),
                ("user_id", ToolSchema.String("Optional user SID/account filter (structured filter mode).")),
                ("event_record_ids", ToolSchema.Array(ToolSchema.Integer(), "Optional event record IDs to include (structured filter mode).")),
                ("named_data_filter", EventLogStructuredFilters.ObjectMapSchema("Optional EventData include filters (structured filter mode).")),
                ("named_data_exclude_filter", EventLogStructuredFilters.ObjectMapSchema("Optional EventData exclude filters (structured filter mode).")),
                ("max_events", ToolSchema.Integer("Optional maximum events to return (capped).")),
                ("oldest_first", ToolSchema.Boolean("If true, read from oldest to newest (default false).")),
                ("include_message", ToolSchema.Boolean("If true, include formatted message text (may be large).")),
                ("session_timeout_ms", ToolSchema.Integer("Optional remote session timeout in milliseconds (capped).")))
            .WithTableViewOptions()
            .Required("log_name")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogLiveQueryTool"/> class.
    /// </summary>
    public EventLogLiveQueryTool(EventLogToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<LiveQueryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("log_name", out var logName, out var logNameError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(logNameError);
            }

            var xpathRaw = reader.OptionalString("xpath");

            if (!EventLogStructuredFilters.TryParseOptionalEventIds(
                    arguments,
                    "event_ids",
                    EventLogStructuredFilters.MaxEventIds,
                    out var eventIds,
                    out var eventIdsError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(eventIdsError ?? "event_ids is invalid.");
            }

            if (!EventLogStructuredFilters.TryReadOptionalBoundedString(
                    arguments,
                    "provider_name",
                    EventLogStructuredFilters.MaxProviderNameLength,
                    out var providerName,
                    out var providerNameError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(providerNameError ?? "provider_name is invalid.");
            }

            if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(timeErr ?? "Invalid time range.");
            }

            if (!EventLogStructuredFilters.TryParseOptionalLevel(arguments, "level", out var level, out var levelError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(levelError ?? "level is invalid.");
            }

            if (!EventLogStructuredFilters.TryParseOptionalKeywords(arguments, "keywords", out var keywords, out var keywordsError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(keywordsError ?? "keywords is invalid.");
            }

            if (!EventLogStructuredFilters.TryReadOptionalBoundedString(
                    arguments,
                    "user_id",
                    EventLogStructuredFilters.MaxUserIdLength,
                    out var userId,
                    out var userIdError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(userIdError ?? "user_id is invalid.");
            }

            if (!EventLogStructuredFilters.TryParseOptionalRecordIds(
                    arguments,
                    "event_record_ids",
                    EventLogStructuredFilters.MaxRecordIds,
                    out var recordIds,
                    out var recordIdsError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(recordIdsError ?? "event_record_ids is invalid.");
            }

            if (!EventLogStructuredFilters.TryParseOptionalNamedDataFilter(
                    arguments,
                    "named_data_filter",
                    out var namedDataFilter,
                    out var namedDataFilterError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(namedDataFilterError ?? "named_data_filter is invalid.");
            }

            if (!EventLogStructuredFilters.TryParseOptionalNamedDataFilter(
                    arguments,
                    "named_data_exclude_filter",
                    out var namedDataExcludeFilter,
                    out var namedDataExcludeFilterError)) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(namedDataExcludeFilterError ?? "named_data_exclude_filter is invalid.");
            }

            var hasStructuredFilters = EventLogStructuredFilters.HasAnyStructuredFilter(
                eventIds: eventIds,
                providerName: providerName,
                startTimeUtc: startUtc,
                endTimeUtc: endUtc,
                level: level,
                keywords: keywords,
                userId: userId,
                eventRecordIds: recordIds,
                namedDataFilter: namedDataFilter,
                namedDataExcludeFilter: namedDataExcludeFilter);

            if (!string.IsNullOrWhiteSpace(xpathRaw) && hasStructuredFilters) {
                return ToolRequestBindingResult<LiveQueryRequest>.Failure(
                    "xpath cannot be combined with structured filters. Provide either xpath or structured filter arguments.");
            }

            var xpath = !string.IsNullOrWhiteSpace(xpathRaw)
                ? xpathRaw!
                : hasStructuredFilters
                    ? EventLogStructuredFilters.BuildStructuredXPath(
                        eventIds: eventIds,
                        providerName: providerName,
                        keywords: keywords,
                        level: level,
                        startTimeUtc: startUtc,
                        endTimeUtc: endUtc,
                        userId: userId,
                        eventRecordIds: recordIds,
                        namedDataFilter: namedDataFilter,
                        namedDataExcludeFilter: namedDataExcludeFilter)
                    : "*";

            var request = new LiveQueryRequest(
                LogName: logName,
                XPathRaw: xpathRaw,
                EventIds: eventIds,
                ProviderName: providerName,
                StartUtc: startUtc,
                EndUtc: endUtc,
                Level: level,
                Keywords: keywords,
                UserId: userId,
                RecordIds: recordIds,
                NamedDataFilter: namedDataFilter,
                NamedDataExcludeFilter: namedDataExcludeFilter,
                HasStructuredFilters: hasStructuredFilters,
                XPath: xpath,
                OldestFirst: reader.Boolean("oldest_first", defaultValue: false),
                IncludeMessage: reader.Boolean("include_message", defaultValue: false),
                MachineName: reader.OptionalString("machine_name"),
                SessionTimeoutMs: ResolveSessionTimeoutMs(
                    TryReadOptionalInt64(arguments, "session_timeout_ms"),
                    minInclusive: MinSessionTimeoutMs,
                    maxInclusive: MaxSessionTimeoutMs),
                MaxEvents: ResolveBoundedOptionLimit(arguments, "max_events"));
            return ToolRequestBindingResult<LiveQueryRequest>.Success(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<LiveQueryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        if (!LiveEventQueryExecutor.TryRead(
                request: new LiveEventQueryRequest {
                    LogName = request.LogName,
                    MachineName = request.MachineName,
                    XPath = request.XPath,
                    MaxEvents = request.MaxEvents,
                    OldestFirst = request.OldestFirst,
                    IncludeMessage = request.IncludeMessage,
                    MaxMessageChars = Options.MaxMessageChars,
                    SessionTimeoutMs = request.SessionTimeoutMs
                },
                result: out var root,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromLiveQueryFailure(
                failure: failure,
                machineName: request.MachineName,
                logName: request.LogName));
        }

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: SanitizeProjectionArguments(context.Arguments, root.Events),
            model: root,
            sourceRows: root.Events,
            viewRowsPath: "events_view",
            title: "Live events (preview)",
            baseTruncated: root.Truncated,
            scanned: root.Events.Count,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                var queryMode = request.HasStructuredFilters
                    ? "structured_filters"
                    : string.IsNullOrWhiteSpace(request.XPathRaw) || string.Equals(request.XPathRaw, "*", StringComparison.Ordinal)
                        ? "wildcard"
                        : "xpath";
                AddReadOnlyTriageChainingMeta(
                    meta: meta,
                    currentTool: "eventlog_live_query",
                    logName: request.LogName,
                    machineName: request.MachineName,
                    suggestedMaxEvents: request.MaxEvents,
                    scanned: root.Events.Count,
                    truncated: root.Truncated,
                    queryMode: queryMode);
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
}
