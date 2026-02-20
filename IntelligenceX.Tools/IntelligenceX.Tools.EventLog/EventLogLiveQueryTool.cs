using System;
using System.Threading;
using System.Threading.Tasks;
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
        cancellationToken.ThrowIfCancellationRequested();

        var logName = arguments?.GetString("log_name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(logName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "log_name is required."));
        }

        var xpathRaw = ToolArgs.GetOptionalTrimmed(arguments, "xpath");

        if (!EventLogStructuredFilters.TryParseOptionalEventIds(
                arguments,
                "event_ids",
                EventLogStructuredFilters.MaxEventIds,
                out var eventIds,
                out var eventIdsError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", eventIdsError ?? "event_ids is invalid."));
        }

        if (!EventLogStructuredFilters.TryReadOptionalBoundedString(
                arguments,
                "provider_name",
                EventLogStructuredFilters.MaxProviderNameLength,
                out var providerName,
                out var providerNameError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", providerNameError ?? "provider_name is invalid."));
        }

        if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", timeErr ?? "Invalid time range."));
        }

        if (!EventLogStructuredFilters.TryParseOptionalLevel(arguments, "level", out var level, out var levelError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", levelError ?? "level is invalid."));
        }

        if (!EventLogStructuredFilters.TryParseOptionalKeywords(arguments, "keywords", out var keywords, out var keywordsError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", keywordsError ?? "keywords is invalid."));
        }

        if (!EventLogStructuredFilters.TryReadOptionalBoundedString(
                arguments,
                "user_id",
                EventLogStructuredFilters.MaxUserIdLength,
                out var userId,
                out var userIdError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", userIdError ?? "user_id is invalid."));
        }

        if (!EventLogStructuredFilters.TryParseOptionalRecordIds(
                arguments,
                "event_record_ids",
                EventLogStructuredFilters.MaxRecordIds,
                out var eventRecordIds,
                out var eventRecordIdsError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", eventRecordIdsError ?? "event_record_ids is invalid."));
        }

        if (!EventLogStructuredFilters.TryParseOptionalNamedDataFilter(
                arguments,
                "named_data_filter",
                out var namedDataFilter,
                out var namedDataFilterError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", namedDataFilterError ?? "named_data_filter is invalid."));
        }

        if (!EventLogStructuredFilters.TryParseOptionalNamedDataFilter(
                arguments,
                "named_data_exclude_filter",
                out var namedDataExcludeFilter,
                out var namedDataExcludeFilterError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", namedDataExcludeFilterError ?? "named_data_exclude_filter is invalid."));
        }

        var hasStructuredFilters = EventLogStructuredFilters.HasAnyStructuredFilter(
            eventIds: eventIds,
            providerName: providerName,
            startTimeUtc: startUtc,
            endTimeUtc: endUtc,
            level: level,
            keywords: keywords,
            userId: userId,
            eventRecordIds: eventRecordIds,
            namedDataFilter: namedDataFilter,
            namedDataExcludeFilter: namedDataExcludeFilter);

        if (!string.IsNullOrWhiteSpace(xpathRaw) && hasStructuredFilters) {
            return Task.FromResult(ToolResponse.Error(
                "invalid_argument",
                "xpath cannot be combined with structured filters. Provide either xpath or structured filter arguments."));
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
                    eventRecordIds: eventRecordIds,
                    namedDataFilter: namedDataFilter,
                    namedDataExcludeFilter: namedDataExcludeFilter)
                : "*";

        var oldestFirst = arguments?.GetBoolean("oldest_first") ?? false;
        var includeMessage = arguments?.GetBoolean("include_message") ?? false;
        var machineName = ToolArgs.GetOptionalTrimmed(arguments, "machine_name");
        var sessionTimeoutMs = ResolveSessionTimeoutMs(arguments, minInclusive: MinSessionTimeoutMs, maxInclusive: MaxSessionTimeoutMs);

        var maxEvents = ResolveBoundedOptionLimit(arguments, "max_events");

        if (!LiveEventQueryExecutor.TryRead(
                request: new LiveEventQueryRequest {
                    LogName = logName,
                    MachineName = machineName,
                    XPath = xpath,
                    MaxEvents = maxEvents,
                    OldestFirst = oldestFirst,
                    IncludeMessage = includeMessage,
                    MaxMessageChars = Options.MaxMessageChars,
                    SessionTimeoutMs = sessionTimeoutMs
                },
                result: out var root,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromLiveQueryFailure(
                failure: failure,
                machineName: machineName,
                logName: logName));
        }

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: root,
            sourceRows: root.Events,
            viewRowsPath: "events_view",
            title: "Live events (preview)",
            baseTruncated: root.Truncated,
            scanned: root.Events.Count,
            maxTop: MaxViewTop);
        return Task.FromResult(response);
    }
}
