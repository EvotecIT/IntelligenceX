using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports;
using EventViewerX.Reports.Security;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Builds focused security summaries from local EVTX files (restricted to allowed roots).
/// </summary>
public sealed class EventLogEvtxSecuritySummaryTool : EventLogToolBase, ITool {
    private const int MaxViewTop = 5000;
    private const int MaxEventsScannedCap = 200_000;
    private const int DefaultTopPerDimension = 20;
    private const int MaxTopPerDimension = 100;
    private const int DefaultSampleSize = 20;
    private const int MaxSampleSize = 200;

    private static readonly string[] ReportKinds = {
        "user_logons",
        "failed_logons",
        "account_lockouts"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_evtx_security_summary",
        "Build authentication-focused security summaries from a local .evtx file (user_logons, failed_logons, account_lockouts).",
        ToolSchema.Object(
                ("path", ToolSchema.String("Path to the .evtx file (absolute or relative).")),
                ("report_kind", ToolSchema.String("Security report kind to build.").Enum(ReportKinds)),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("max_events_scanned", ToolSchema.Integer("Maximum number of events to scan (capped).")),
                ("top_per_dimension", ToolSchema.Integer("How many top rows to keep per report dimension (capped).")),
                ("include_samples", ToolSchema.Boolean("When true, include sample events in output (default false).")),
                ("sample_size", ToolSchema.Integer("How many sample events to include when include_samples=true (capped).")))
            .WithTableViewOptions()
            .Required("path", "report_kind")
            .NoAdditionalProperties());

    private enum SecurityReportKind {
        UserLogons,
        FailedLogons,
        AccountLockouts
    }

    private sealed record SecuritySummaryRequest(
        string Path,
        SecurityReportKind ReportKind,
        DateTime? StartTimeUtc,
        DateTime? EndTimeUtc,
        int MaxEventsScanned,
        int TopPerDimension,
        bool IncludeSamples,
        int SampleSize);

    private sealed record SecuritySummaryTopRow(
        string Dimension,
        string Key,
        long Count);

    private sealed record SecuritySummaryEnvelope(
        string ReportKind,
        object Report,
        IReadOnlyList<SecuritySummaryTopRow> TopRows);

    private sealed record SecurityHandoffRow(
        string? Who,
        string? ObjectAffected,
        string? Computer);

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogEvtxSecuritySummaryTool"/> class.
    /// </summary>
    public EventLogEvtxSecuritySummaryTool(EventLogToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<SecuritySummaryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("path", out var path, out var pathError)) {
                return ToolRequestBindingResult<SecuritySummaryRequest>.Failure(pathError);
            }

            if (!reader.TryReadRequiredString("report_kind", out var reportKindRaw, out var reportKindError)) {
                return ToolRequestBindingResult<SecuritySummaryRequest>.Failure(reportKindError);
            }

            if (!TryParseReportKind(reportKindRaw, out var reportKind, out var reportKindParseError)) {
                return ToolRequestBindingResult<SecuritySummaryRequest>.Failure(
                    reportKindParseError ?? $"report_kind must be one of: {string.Join(", ", ReportKinds)}.");
            }

            if (!ToolTime.TryParseUtcRange(
                    arguments,
                    startKey: "start_time_utc",
                    endKey: "end_time_utc",
                    startUtc: out var startUtc,
                    endUtc: out var endUtc,
                    error: out var timeError)) {
                return ToolRequestBindingResult<SecuritySummaryRequest>.Failure(timeError ?? "Invalid time range.");
            }

            var maxEventsScannedDefault = Math.Min(MaxEventsScannedCap, Math.Max(1, Options.MaxResults * 50));
            var maxEventsScanned = ToolArgs.GetCappedInt32(
                arguments,
                key: "max_events_scanned",
                defaultValue: maxEventsScannedDefault,
                minInclusive: 1,
                maxInclusive: MaxEventsScannedCap);
            var topPerDimension = ToolArgs.GetCappedInt32(
                arguments,
                key: "top_per_dimension",
                defaultValue: DefaultTopPerDimension,
                minInclusive: 1,
                maxInclusive: MaxTopPerDimension);
            var includeSamples = ToolArgs.GetBoolean(arguments, "include_samples", defaultValue: false);
            var sampleSize = ToolArgs.GetCappedInt32(
                arguments,
                key: "sample_size",
                defaultValue: DefaultSampleSize,
                minInclusive: 1,
                maxInclusive: MaxSampleSize);

            return ToolRequestBindingResult<SecuritySummaryRequest>.Success(
                new SecuritySummaryRequest(
                    Path: path,
                    ReportKind: reportKind,
                    StartTimeUtc: startUtc,
                    EndTimeUtc: endUtc,
                    MaxEventsScanned: maxEventsScanned,
                    TopPerDimension: topPerDimension,
                    IncludeSamples: includeSamples,
                    SampleSize: sampleSize));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<SecuritySummaryRequest> context, CancellationToken cancellationToken) {
        var request = context.Request;
        if (!TryResolveEvtxPath(request.Path, out var fullPath, out var errCode, out var err, out var hints)) {
            return Task.FromResult(ToolResultV2.Error(errCode, err, hints: hints, isTransient: false));
        }

        var queryRequest = new SecurityEvtxQueryRequest {
            FilePath = fullPath,
            StartTimeUtc = request.StartTimeUtc,
            EndTimeUtc = request.EndTimeUtc,
            MaxEventsScanned = request.MaxEventsScanned,
            Top = request.TopPerDimension,
            IncludeSamples = request.IncludeSamples,
            SampleSize = request.SampleSize
        };

        return request.ReportKind switch {
            SecurityReportKind.UserLogons => Task.FromResult(ExecuteUserLogons(context.Arguments, request, queryRequest, cancellationToken)),
            SecurityReportKind.FailedLogons => Task.FromResult(ExecuteFailedLogons(context.Arguments, request, queryRequest, cancellationToken)),
            SecurityReportKind.AccountLockouts => Task.FromResult(ExecuteAccountLockouts(context.Arguments, request, queryRequest, cancellationToken)),
            _ => Task.FromResult(ToolResultV2.Error("invalid_argument", $"report_kind must be one of: {string.Join(", ", ReportKinds)}."))
        };
    }

    private string ExecuteUserLogons(
        JsonObject? arguments,
        SecuritySummaryRequest request,
        SecurityEvtxQueryRequest queryRequest,
        CancellationToken cancellationToken) {
        if (!SecurityEvtxQueryExecutor.TryBuildUserLogons(
                request: queryRequest,
                result: out var result,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return ErrorFromEvtxFailure(failure);
        }

        var topRows = BuildTopRows(result);
        var entityHandoff = BuildEntityHandoff(result);
        var model = new SecuritySummaryEnvelope(
            ReportKind: ToReportKindName(request.ReportKind),
            Report: result,
            TopRows: topRows);

        return BuildResultResponse(
            arguments: arguments,
            request: request,
            model: model,
            topRows: topRows,
            scannedEvents: result.ScannedEvents,
            matchedEvents: result.MatchedEvents,
            truncated: result.Truncated,
            entityHandoff: entityHandoff);
    }

    private string ExecuteFailedLogons(
        JsonObject? arguments,
        SecuritySummaryRequest request,
        SecurityEvtxQueryRequest queryRequest,
        CancellationToken cancellationToken) {
        if (!SecurityEvtxQueryExecutor.TryBuildFailedLogons(
                request: queryRequest,
                result: out var result,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return ErrorFromEvtxFailure(failure);
        }

        var topRows = BuildTopRows(result);
        var entityHandoff = BuildEntityHandoff(result);
        var model = new SecuritySummaryEnvelope(
            ReportKind: ToReportKindName(request.ReportKind),
            Report: result,
            TopRows: topRows);

        return BuildResultResponse(
            arguments: arguments,
            request: request,
            model: model,
            topRows: topRows,
            scannedEvents: result.ScannedEvents,
            matchedEvents: result.MatchedEvents,
            truncated: result.Truncated,
            entityHandoff: entityHandoff);
    }

    private string ExecuteAccountLockouts(
        JsonObject? arguments,
        SecuritySummaryRequest request,
        SecurityEvtxQueryRequest queryRequest,
        CancellationToken cancellationToken) {
        if (!SecurityEvtxQueryExecutor.TryBuildAccountLockouts(
                request: queryRequest,
                result: out var result,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return ErrorFromEvtxFailure(failure);
        }

        var topRows = BuildTopRows(result);
        var entityHandoff = BuildEntityHandoff(result);
        var model = new SecuritySummaryEnvelope(
            ReportKind: ToReportKindName(request.ReportKind),
            Report: result,
            TopRows: topRows);

        return BuildResultResponse(
            arguments: arguments,
            request: request,
            model: model,
            topRows: topRows,
            scannedEvents: result.ScannedEvents,
            matchedEvents: result.MatchedEvents,
            truncated: result.Truncated,
            entityHandoff: entityHandoff);
    }

    private string BuildResultResponse(
        JsonObject? arguments,
        SecuritySummaryRequest request,
        SecuritySummaryEnvelope model,
        IReadOnlyList<SecuritySummaryTopRow> topRows,
        int scannedEvents,
        int matchedEvents,
        bool truncated,
        JsonObject entityHandoff) {
        return ToolResultV2.OkAutoTableResponse(
            arguments: arguments,
            model: model,
            sourceRows: topRows,
            viewRowsPath: "top_rows_view",
            title: "Security summary top rows (preview)",
            baseTruncated: truncated,
            scanned: scannedEvents,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                meta.Add("report_kind", ToReportKindName(request.ReportKind));
                meta.Add("max_events_scanned", request.MaxEventsScanned);
                meta.Add("top_per_dimension", request.TopPerDimension);
                meta.Add("include_samples", request.IncludeSamples);
                if (request.IncludeSamples) {
                    meta.Add("sample_size", request.SampleSize);
                }
                meta.Add("matched_events", matchedEvents);
                meta.Add("entity_handoff", entityHandoff);
                if (request.StartTimeUtc.HasValue) {
                    meta.Add("start_time_utc", ToolTime.FormatUtc(request.StartTimeUtc));
                }
                if (request.EndTimeUtc.HasValue) {
                    meta.Add("end_time_utc", ToolTime.FormatUtc(request.EndTimeUtc));
                }
            });
    }

    private static List<SecuritySummaryTopRow> BuildTopRows(SecurityUserLogonsQueryResult result) {
        var rows = new List<SecuritySummaryTopRow>();
        AddTopRows(rows, "event_id", result.ByEventId);
        AddTopRows(rows, "target_user", result.ByTargetUser);
        AddTopRows(rows, "target_domain", result.ByTargetDomain);
        AddTopRows(rows, "logon_type", result.ByLogonType);
        AddTopRows(rows, "ip_address", result.ByIpAddress);
        AddTopRows(rows, "workstation_name", result.ByWorkstationName);
        AddTopRows(rows, "computer_name", result.ByComputerName);
        return rows;
    }

    private static List<SecuritySummaryTopRow> BuildTopRows(SecurityFailedLogonsQueryResult result) {
        var rows = new List<SecuritySummaryTopRow>();
        AddTopRows(rows, "target_user", result.ByTargetUser);
        AddTopRows(rows, "target_domain", result.ByTargetDomain);
        AddTopRows(rows, "logon_type", result.ByLogonType);
        AddTopRows(rows, "ip_address", result.ByIpAddress);
        AddTopRows(rows, "workstation_name", result.ByWorkstationName);
        AddTopRows(rows, "computer_name", result.ByComputerName);
        AddTopRows(rows, "status", result.ByStatus);
        AddTopRows(rows, "status_name", result.ByStatusName);
        AddTopRows(rows, "sub_status", result.BySubStatus);
        AddTopRows(rows, "sub_status_name", result.BySubStatusName);
        AddTopRows(rows, "failure_reason", result.ByFailureReason);
        return rows;
    }

    private static List<SecuritySummaryTopRow> BuildTopRows(SecurityAccountLockoutsQueryResult result) {
        var rows = new List<SecuritySummaryTopRow>();
        AddTopRows(rows, "target_user", result.ByTargetUser);
        AddTopRows(rows, "target_domain", result.ByTargetDomain);
        AddTopRows(rows, "caller_computer_name", result.ByCallerComputerName);
        AddTopRows(rows, "subject_user", result.BySubjectUser);
        AddTopRows(rows, "computer_name", result.ByComputerName);
        return rows;
    }

    private static void AddTopRows(List<SecuritySummaryTopRow> rows, string dimension, IReadOnlyList<ReportTopRow> reportRows) {
        for (var i = 0; i < reportRows.Count; i++) {
            var row = reportRows[i];
            rows.Add(new SecuritySummaryTopRow(
                Dimension: dimension,
                Key: FormatTopRowKey(row.Key),
                Count: row.Count));
        }
    }

    private static string FormatTopRowKey(IReadOnlyDictionary<string, object?> key) {
        if (key.Count == 0) {
            return string.Empty;
        }

        if (key.Count == 1) {
            var value = key.First().Value;
            return NormalizeKeyValue(value);
        }

        return string.Join("; ", key
            .OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static x => $"{x.Key}={NormalizeKeyValue(x.Value)}"));
    }

    private static string NormalizeKeyValue(object? value) {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) {
            return "<empty>";
        }
        return text.Trim();
    }

    private static JsonObject BuildEntityHandoff(SecurityUserLogonsQueryResult result) {
        var rows = new List<SecurityHandoffRow>();
        AddWhoRows(rows, result.ByTargetUser, "user");
        AddComputerRows(rows, result.ByComputerName, "computer");
        AddComputerRows(rows, result.ByWorkstationName, "workstation");

        if (result.Samples is not null) {
            for (var i = 0; i < result.Samples.Count; i++) {
                var sample = result.Samples[i];
                rows.Add(new SecurityHandoffRow(
                    Who: NormalizeCandidate(sample.TargetUser),
                    ObjectAffected: CombineDomainAndUser(sample.TargetDomain, sample.TargetUser),
                    Computer: NormalizeCandidate(sample.ComputerName)));
                rows.Add(new SecurityHandoffRow(
                    Who: NormalizeCandidate(sample.SubjectUser),
                    ObjectAffected: null,
                    Computer: NormalizeCandidate(sample.WorkstationName)));
            }
        }

        return EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);
    }

    private static JsonObject BuildEntityHandoff(SecurityFailedLogonsQueryResult result) {
        var rows = new List<SecurityHandoffRow>();
        AddWhoRows(rows, result.ByTargetUser, "user");
        AddComputerRows(rows, result.ByComputerName, "computer");
        AddComputerRows(rows, result.ByWorkstationName, "workstation");

        if (result.Samples is not null) {
            for (var i = 0; i < result.Samples.Count; i++) {
                var sample = result.Samples[i];
                rows.Add(new SecurityHandoffRow(
                    Who: NormalizeCandidate(sample.TargetUser),
                    ObjectAffected: CombineDomainAndUser(sample.TargetDomain, sample.TargetUser),
                    Computer: NormalizeCandidate(sample.ComputerName)));
                rows.Add(new SecurityHandoffRow(
                    Who: NormalizeCandidate(sample.SubjectUser),
                    ObjectAffected: null,
                    Computer: NormalizeCandidate(sample.WorkstationName)));
            }
        }

        return EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);
    }

    private static JsonObject BuildEntityHandoff(SecurityAccountLockoutsQueryResult result) {
        var rows = new List<SecurityHandoffRow>();
        AddWhoRows(rows, result.ByTargetUser, "user");
        AddWhoRows(rows, result.BySubjectUser, "user");
        AddComputerRows(rows, result.ByComputerName, "computer");
        AddComputerRows(rows, result.ByCallerComputerName, "computer");

        if (result.Samples is not null) {
            for (var i = 0; i < result.Samples.Count; i++) {
                var sample = result.Samples[i];
                rows.Add(new SecurityHandoffRow(
                    Who: NormalizeCandidate(sample.TargetUser),
                    ObjectAffected: CombineDomainAndUser(sample.TargetDomain, sample.TargetUser),
                    Computer: NormalizeCandidate(sample.ComputerName)));
                rows.Add(new SecurityHandoffRow(
                    Who: NormalizeCandidate(sample.SubjectUser),
                    ObjectAffected: null,
                    Computer: NormalizeCandidate(sample.CallerComputerName)));
            }
        }

        return EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);
    }

    private static void AddWhoRows(List<SecurityHandoffRow> rows, IReadOnlyList<ReportTopRow> reportRows, string keyName) {
        for (var i = 0; i < reportRows.Count; i++) {
            var who = ReadTopRowValue(reportRows[i], keyName);
            if (who is null) {
                continue;
            }

            rows.Add(new SecurityHandoffRow(
                Who: who,
                ObjectAffected: null,
                Computer: null));
        }
    }

    private static void AddComputerRows(List<SecurityHandoffRow> rows, IReadOnlyList<ReportTopRow> reportRows, string keyName) {
        for (var i = 0; i < reportRows.Count; i++) {
            var computer = ReadTopRowValue(reportRows[i], keyName);
            if (computer is null) {
                continue;
            }

            rows.Add(new SecurityHandoffRow(
                Who: null,
                ObjectAffected: null,
                Computer: computer));
        }
    }

    private static string? ReadTopRowValue(ReportTopRow row, string keyName) {
        if (row.Key.TryGetValue(keyName, out var value)) {
            return NormalizeCandidate(value?.ToString());
        }

        if (row.Key.Count == 1) {
            return NormalizeCandidate(row.Key.First().Value?.ToString());
        }

        return null;
    }

    private static string? CombineDomainAndUser(string? domain, string? user) {
        var normalizedUser = NormalizeCandidate(user);
        if (normalizedUser is null) {
            return null;
        }

        var normalizedDomain = NormalizeCandidate(domain);
        if (normalizedDomain is null) {
            return normalizedUser;
        }

        return $"{normalizedDomain}\\{normalizedUser}";
    }

    private static string? NormalizeCandidate(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 1 && normalized[0] == '-') {
            return null;
        }

        return normalized;
    }

    private static bool TryParseReportKind(
        string raw,
        out SecurityReportKind reportKind,
        out string? error) {
        var normalized = EventLogNamedEventsQueryShared.ToSnakeCase(raw);
        error = null;
        reportKind = normalized switch {
            "user_logons" => SecurityReportKind.UserLogons,
            "failed_logons" => SecurityReportKind.FailedLogons,
            "account_lockouts" => SecurityReportKind.AccountLockouts,
            _ => default
        };

        if (normalized is "user_logons" or "failed_logons" or "account_lockouts") {
            return true;
        }

        error = $"report_kind must be one of: {string.Join(", ", ReportKinds)}.";
        return false;
    }

    private static string ToReportKindName(SecurityReportKind kind) {
        return kind switch {
            SecurityReportKind.UserLogons => "user_logons",
            SecurityReportKind.FailedLogons => "failed_logons",
            SecurityReportKind.AccountLockouts => "account_lockouts",
            _ => "user_logons"
        };
    }
}
