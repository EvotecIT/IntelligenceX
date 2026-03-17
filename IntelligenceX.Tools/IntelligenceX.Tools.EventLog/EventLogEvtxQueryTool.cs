using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        var request = new EvtxQueryRequest {
            FilePath = context.Request.FullPath,
            EventIds = context.Request.StructuredFilter?.EventIds?.ToList(),
            ProviderName = context.Request.StructuredFilter?.ProviderName,
            StartTimeUtc = context.Request.StructuredFilter?.StartTimeUtc,
            EndTimeUtc = context.Request.StructuredFilter?.EndTimeUtc,
            MaxEvents = context.Request.MaxEvents,
            OldestFirst = context.Request.OldestFirst
        };

        var filter = context.Request.StructuredFilter;
        var hasAdvancedFilters = filter is not null &&
                                 (filter.Level.HasValue
                                  || filter.Keywords.HasValue
                                  || !string.IsNullOrWhiteSpace(filter.UserId)
                                  || (filter.RecordIds?.Count ?? 0) > 0
                                  || (filter.NamedDataFilter?.Count ?? 0) > 0
                                  || (filter.NamedDataExcludeFilter?.Count ?? 0) > 0);

        EvtxEventReportResult root;
        EvtxQueryFailure? failure;
        if (!hasAdvancedFilters) {
            if (!EvtxEventReportBuilder.TryBuild(
                    request: request,
                    includeMessage: context.Request.IncludeMessage,
                    maxMessageChars: Options.MaxMessageChars,
                    report: out root,
                    failure: out failure,
                    cancellationToken: cancellationToken)) {
                return Task.FromResult(ErrorFromEvtxFailure(failure));
            }
        } else {
            if (!TryBuildAdvancedReport(
                    request: request,
                    structuredFilter: filter,
                    includeMessage: context.Request.IncludeMessage,
                    maxMessageChars: Options.MaxMessageChars,
                    report: out root,
                    failure: out failure,
                    cancellationToken: cancellationToken)) {
                return Task.FromResult(ErrorFromEvtxFailure(failure));
            }
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

    private static bool TryBuildAdvancedReport(
        EvtxQueryRequest request,
        EventStructuredQueryFilter? structuredFilter,
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
                         keywords: structuredFilter?.Keywords,
                         level: structuredFilter?.Level,
                         startTime: request.StartTimeUtc,
                         endTime: request.EndTimeUtc,
                         userId: structuredFilter?.UserId,
                         maxEvents: request.MaxEvents,
                         eventRecordId: structuredFilter?.RecordIds?.ToList(),
                         oldest: request.OldestFirst,
                         namedDataFilter: structuredFilter?.NamedDataFilter,
                         namedDataExcludeFilter: structuredFilter?.NamedDataExcludeFilter,
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
