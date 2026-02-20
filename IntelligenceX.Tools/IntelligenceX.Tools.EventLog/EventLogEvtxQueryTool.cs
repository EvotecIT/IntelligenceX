using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
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
                ("level", ToolSchema.String("Optional event level filter.").Enum(EventLogStructuredFilters.LevelNames)),
                ("keywords", ToolSchema.String("Optional event keyword filter.").Enum(EventLogStructuredFilters.KeywordNames)),
                ("user_id", ToolSchema.String("Optional user SID/account filter.")),
                ("event_record_ids", ToolSchema.Array(ToolSchema.Integer(), "Optional event record IDs to include.")),
                ("named_data_filter", EventLogStructuredFilters.ObjectMapSchema("Optional EventData include filters (object map).")),
                ("named_data_exclude_filter", EventLogStructuredFilters.ObjectMapSchema("Optional EventData exclude filters (object map).")),
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

        if (!EventLogStructuredFilters.TryReadOptionalBoundedString(
                arguments,
                "provider_name",
                EventLogStructuredFilters.MaxProviderNameLength,
                out var providerName,
                out var providerNameError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", providerNameError ?? "provider_name is invalid."));
        }

        var oldestFirst = arguments?.GetBoolean("oldest_first") ?? false;
        var includeMessage = arguments?.GetBoolean("include_message") ?? false;

        if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", timeErr ?? "Invalid time range."));
        }

        var maxEvents = ResolveBoundedOptionLimit(arguments, "max_events");

        if (!EventLogStructuredFilters.TryParseOptionalEventIds(
                arguments,
                "event_ids",
                EventLogStructuredFilters.MaxEventIds,
                out var eventIds,
                out var eventIdsError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", eventIdsError ?? "event_ids is invalid."));
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

        var request = new EvtxQueryRequest {
            FilePath = fullPath,
            EventIds = eventIds,
            ProviderName = providerName,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            MaxEvents = maxEvents,
            OldestFirst = oldestFirst
        };

        var hasAdvancedFilters = EventLogStructuredFilters.HasAnyStructuredFilter(
            eventIds: null,
            providerName: null,
            startTimeUtc: null,
            endTimeUtc: null,
            level: level,
            keywords: keywords,
            userId: userId,
            eventRecordIds: eventRecordIds,
            namedDataFilter: namedDataFilter,
            namedDataExcludeFilter: namedDataExcludeFilter);

        EvtxEventReportResult root;
        EvtxQueryFailure? failure;
        if (!hasAdvancedFilters) {
            if (!EvtxEventReportBuilder.TryBuild(
                    request: request,
                    includeMessage: includeMessage,
                    maxMessageChars: Options.MaxMessageChars,
                    report: out root,
                    failure: out failure,
                    cancellationToken: cancellationToken)) {
                return Task.FromResult(ErrorFromEvtxFailure(failure));
            }
        } else {
            if (!TryBuildAdvancedReport(
                    request: request,
                    level: level,
                    keywords: keywords,
                    userId: userId,
                    eventRecordIds: eventRecordIds,
                    namedDataFilter: namedDataFilter,
                    namedDataExcludeFilter: namedDataExcludeFilter,
                    includeMessage: includeMessage,
                    maxMessageChars: Options.MaxMessageChars,
                    report: out root,
                    failure: out failure,
                    cancellationToken: cancellationToken)) {
                return Task.FromResult(ErrorFromEvtxFailure(failure));
            }
        }

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: root,
            sourceRows: root.Events,
            viewRowsPath: "events_view",
            title: "Events (preview)",
            baseTruncated: root.Truncated,
            scanned: root.Events.Count,
            maxTop: MaxViewTop);
        return Task.FromResult(response);
    }

    private static bool TryBuildAdvancedReport(
        EvtxQueryRequest request,
        Level? level,
        Keywords? keywords,
        string? userId,
        List<long>? eventRecordIds,
        Hashtable? namedDataFilter,
        Hashtable? namedDataExcludeFilter,
        bool includeMessage,
        int maxMessageChars,
        out EvtxEventReportResult report,
        out EvtxQueryFailure? failure,
        CancellationToken cancellationToken) {
        if (request is null) {
            report = new EvtxEventReportResult();
            failure = new EvtxQueryFailure {
                Kind = EvtxQueryFailureKind.InvalidArgument,
                Message = "request is required."
            };
            return false;
        }

        if (maxMessageChars < 0) {
            report = new EvtxEventReportResult();
            failure = new EvtxQueryFailure {
                Kind = EvtxQueryFailureKind.InvalidArgument,
                Message = "maxMessageChars must be greater than or equal to 0."
            };
            return false;
        }

        try {
            var rows = new List<EvtxEventReportRow>();
            var eventIds = request.EventIds is null ? null : new List<int>(request.EventIds);

            foreach (var ev in SearchEvents.QueryLogFile(
                         filePath: request.FilePath,
                         eventIds: eventIds,
                         providerName: request.ProviderName,
                         keywords: keywords,
                         level: level,
                         startTime: request.StartTimeUtc,
                         endTime: request.EndTimeUtc,
                         userId: userId,
                         maxEvents: request.MaxEvents,
                         eventRecordId: eventRecordIds,
                         oldest: request.OldestFirst,
                         namedDataFilter: namedDataFilter,
                         namedDataExcludeFilter: namedDataExcludeFilter,
                         cancellationToken: cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();

                rows.Add(new EvtxEventReportRow {
                    TimeCreatedUtc = ev.TimeCreated.ToUniversalTime().ToString("O"),
                    Id = ev.Id,
                    RecordId = ev.RecordId ?? 0,
                    LogName = ev.LogName ?? string.Empty,
                    ProviderName = ev.ProviderName ?? string.Empty,
                    Level = (long)(ev.Level ?? 0),
                    LevelDisplayName = ev.LevelDisplayName ?? string.Empty,
                    ComputerName = ev.ComputerName ?? string.Empty,
                    QueriedMachine = ev.QueriedMachine ?? string.Empty,
                    GatheredFrom = ev.GatheredFrom ?? string.Empty,
                    MessageSubject = ev.MessageSubject ?? string.Empty,
                    UserSid = SafeGetUserSid(ev),
                    Data = NormalizeDict(ev.Data),
                    MessageData = NormalizeDict(ev.MessageData),
                    Message = includeMessage ? TruncateSafe(SafeGetMessage(ev), maxMessageChars) : null
                });
            }

            report = new EvtxEventReportResult {
                Path = request.FilePath,
                Count = rows.Count,
                Truncated = request.MaxEvents > 0 && rows.Count >= request.MaxEvents,
                Events = rows
            };

            failure = null;
            return true;
        } catch (OperationCanceledException) {
            throw;
        } catch (ArgumentException ex) {
            report = new EvtxEventReportResult();
            failure = new EvtxQueryFailure {
                Kind = EvtxQueryFailureKind.InvalidArgument,
                Message = ex.Message
            };
            return false;
        } catch (FileNotFoundException ex) {
            report = new EvtxEventReportResult();
            failure = new EvtxQueryFailure {
                Kind = EvtxQueryFailureKind.NotFound,
                Message = ex.Message
            };
            return false;
        } catch (UnauthorizedAccessException ex) {
            report = new EvtxEventReportResult();
            failure = new EvtxQueryFailure {
                Kind = EvtxQueryFailureKind.AccessDenied,
                Message = ex.Message
            };
            return false;
        } catch (IOException ex) {
            report = new EvtxEventReportResult();
            failure = new EvtxQueryFailure {
                Kind = EvtxQueryFailureKind.IoError,
                Message = ex.Message
            };
            return false;
        } catch (Exception ex) {
            report = new EvtxEventReportResult();
            failure = new EvtxQueryFailure {
                Kind = EvtxQueryFailureKind.Exception,
                Message = ex.Message
            };
            return false;
        }
    }

    private static string SafeGetUserSid(EventObject ev) {
        try {
            return ev.UserId?.Value ?? string.Empty;
        } catch {
            return string.Empty;
        }
    }

    private static string SafeGetMessage(EventObject ev) {
        try {
            return ev.Message ?? string.Empty;
        } catch {
            return string.Empty;
        }
    }

    private static string TruncateSafe(string value, int maxChars) {
        if (maxChars <= 0 || string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        if (value.Length <= maxChars) {
            return value;
        }

        return value.Substring(0, maxChars);
    }

    private static IReadOnlyDictionary<string, string> NormalizeDict(IReadOnlyDictionary<string, string>? dict) {
        if (dict is null || dict.Count == 0) {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var normalized = new Dictionary<string, string>(dict.Count, StringComparer.Ordinal);
        foreach (var kvp in dict) {
            normalized[kvp.Key] = kvp.Value ?? string.Empty;
        }

        return normalized;
    }
}
