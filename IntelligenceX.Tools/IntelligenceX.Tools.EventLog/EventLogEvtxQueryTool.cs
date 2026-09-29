using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Reads events from an EVTX file (restricted to allowed roots).
/// </summary>
public sealed class EventLogEvtxQueryTool : EventLogToolBase, ITool {
    private sealed class EvtxEventReportRow {
        public string? TimeCreatedUtc { get; init; }
        public int Id { get; init; }
        public long? RecordId { get; init; }
        public string LogName { get; init; } = string.Empty;
        public string ProviderName { get; init; } = string.Empty;
        public long? Level { get; init; }
        public string LevelDisplayName { get; init; } = string.Empty;
        public string ComputerName { get; init; } = string.Empty;
        public string QueriedMachine { get; init; } = string.Empty;
        public string GatheredFrom { get; init; } = string.Empty;
        public string MessageSubject { get; init; } = string.Empty;
        public string UserSid { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> MessageData { get; init; } = new Dictionary<string, string>();
        public string? Message { get; init; }
    }

    private sealed class EvtxEventReportResult {
        public string Path { get; init; } = string.Empty;
        public int Count { get; init; }
        public bool Truncated { get; init; }
        public IReadOnlyList<EvtxEventReportRow> Events { get; init; } = Array.Empty<EvtxEventReportRow>();
    }
    private const int MaxViewTop = 5000;
    private sealed record EvtxQueryToolRequest(
        string FullPath,
        EventStructuredQueryFilter? StructuredFilter,
        int MaxEvents,
        bool OldestFirst,
        bool IncludeMessage);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<EvtxQueryToolRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("path", out var inputPath, out var pathError)) {
                return ToolRequestBindingResult<EvtxQueryToolRequest>.Failure(pathError);
            }

            if (!TryResolveEvtxPath(inputPath, out var fullPath, out var errCode, out var err, out var hints)) {
                return ToolRequestBindingResult<EvtxQueryToolRequest>.Failure(
                    error: err,
                    errorCode: errCode,
                    hints: hints,
                    isTransient: false);
            }

            if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
                return ToolRequestBindingResult<EvtxQueryToolRequest>.Failure(timeErr ?? "Invalid time range.");
            }

            if (!EventLogStructuredFilters.TryNormalize(
                    arguments,
                    startUtc,
                    endUtc,
                    out var structuredFilter,
                    out var structuredFilterError)) {
                return ToolRequestBindingResult<EvtxQueryToolRequest>.Failure(
                    structuredFilterError ?? "Structured filters are invalid.");
            }

            var request = new EvtxQueryToolRequest(
                FullPath: fullPath,
                StructuredFilter: structuredFilter,
                MaxEvents: ResolveBoundedOptionLimit(arguments, "max_events"),
                OldestFirst: reader.Boolean("oldest_first", defaultValue: false),
                IncludeMessage: reader.Boolean("include_message", defaultValue: false));
            return ToolRequestBindingResult<EvtxQueryToolRequest>.Success(request);
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<EvtxQueryToolRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        EvtxEventReportResult root;
        try {
            root = BuildReport(
                context.Request.FullPath,
                context.Request.StructuredFilter,
                context.Request.MaxEvents,
                context.Request.OldestFirst,
                context.Request.IncludeMessage,
                Options.MaxMessageChars,
                cancellationToken);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromEvtxFailure(ex));
        }

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: SanitizeProjectionArguments(context.Arguments, root.Events),
            model: root,
            sourceRows: root.Events,
            viewRowsPath: "events_view",
            title: "Events (preview)",
            baseTruncated: root.Truncated,
            scanned: root.Events.Count,
            maxTop: MaxViewTop);
        return Task.FromResult(response);
    }

    private static EvtxEventReportResult BuildReport(
        string path,
        EventStructuredQueryFilter? structuredFilter,
        int maxEvents,
        bool oldestFirst,
        bool includeMessage,
        int maxMessageChars,
        CancellationToken cancellationToken) {
        if (maxMessageChars < 0) {
            throw new ArgumentOutOfRangeException(nameof(maxMessageChars));
        }

        var rows = new List<EvtxEventReportRow>();
        var truncated = false;
        var query = new EventLogFileQuery(path) {
            XPath = EventStructuredQueryFilterService.BuildXPath(structuredFilter),
            MaxEvents = maxEvents > 0 && maxEvents < int.MaxValue ? maxEvents + 1 : maxEvents,
            Oldest = oldestFirst,
            ReadMode = includeMessage ? EventReadMode.StructuredDataAndMessage : EventReadMode.StructuredData
        };
        foreach (var ev in EventLogEngine.ReadFile(query, cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (maxEvents > 0 && rows.Count == maxEvents) {
                    truncated = true;
                    break;
                }

                rows.Add(new EvtxEventReportRow {
                    TimeCreatedUtc = ev.TimeCreated == default ? null : ev.TimeCreated.ToUniversalTime().ToString("O"),
                    Id = ev.Id,
                    RecordId = ev.RecordId,
                    LogName = ev.LogName ?? string.Empty,
                    ProviderName = ev.ProviderName ?? string.Empty,
                    Level = ev.Level,
                    LevelDisplayName = ev.LevelDisplayName ?? string.Empty,
                    ComputerName = ev.ComputerName ?? string.Empty,
                    QueriedMachine = ev.QueriedMachine ?? string.Empty,
                    GatheredFrom = ev.GatheredFrom ?? string.Empty,
                    MessageSubject = includeMessage ? ev.MessageSubject : string.Empty,
                    UserSid = SafeGetUserSid(ev),
                    Data = NormalizeDict(ev.Data),
                    MessageData = includeMessage ? NormalizeDict(ev.MessageData) : new Dictionary<string, string>(),
                    Message = includeMessage ? TruncateSafe(SafeGetMessage(ev), maxMessageChars) : null
                });
        }

        return new EvtxEventReportResult {
            Path = path,
            Count = rows.Count,
            Truncated = truncated,
            Events = rows
        };
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
