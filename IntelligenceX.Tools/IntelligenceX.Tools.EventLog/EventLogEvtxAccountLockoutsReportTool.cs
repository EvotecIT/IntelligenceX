using System;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Security;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Builds a summary report for Windows account lockout events from an EVTX file (read-only, restricted to allowed roots).
/// </summary>
public sealed class EventLogEvtxAccountLockoutsReportTool : EventLogToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_evtx_report_account_lockouts",
        "Report Windows account lockout events (4740) from an EVTX file (restricted to allowed roots).",
        ToolSchema.Object(
                ("path", ToolSchema.String("Path to the .evtx file (absolute or relative).")),
                ("start_time_utc", ToolSchema.String("ISO-8601 UTC lower bound (optional).")),
                ("end_time_utc", ToolSchema.String("ISO-8601 UTC upper bound (optional).")),
                ("max_events_scanned", ToolSchema.Integer("Maximum events to scan (capped). Default 5000.")),
                ("top", ToolSchema.Integer("How many top values to return per dimension (capped). Default 20.")),
                ("include_samples", ToolSchema.Boolean("When true, include a small sample of matching events. Default false.")),
                ("sample_size", ToolSchema.Integer("Sample size when include_samples=true (capped). Default 20.")))
            .WithTableViewOptions()
            .Required("path")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogEvtxAccountLockoutsReportTool"/> class.
    /// </summary>
    public EventLogEvtxAccountLockoutsReportTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var inputPath = ToolArgs.GetOptionalTrimmed(arguments, "path") ?? string.Empty;
        if (!TryResolveEvtxPath(inputPath, out var fullPath, out var errCode, out var err, out var hints)) {
            return Task.FromResult(ToolResponse.Error(errCode, err, hints: hints, isTransient: false));
        }

        if (!ToolTime.TryParseUtcRange(arguments, "start_time_utc", "end_time_utc", out var startUtc, out var endUtc, out var timeErr)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", timeErr ?? "Invalid time range."));
        }

        if (!SecurityEvtxQueryRequestNormalizer.TryCreate(
                filePath: fullPath,
                startTimeUtc: startUtc,
                endTimeUtc: endUtc,
                maxEventsScanned: ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("max_events_scanned")),
                top: ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("top")),
                includeSamples: ToolArgs.GetBoolean(arguments, "include_samples"),
                sampleSize: ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("sample_size")),
                request: out var request,
                error: out var commonErr)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", commonErr ?? "Invalid report arguments."));
        }

        if (!SecurityEvtxQueryExecutor.TryBuildAccountLockouts(request, out var queryResult, out var failure, cancellationToken)) {
            return Task.FromResult(ErrorFromEvtxFailure(failure));
        }

        var response = EventLogEvtxSecurityReportHelper.BuildTopUserResponse(
            arguments: arguments,
            model: queryResult,
            title: "Account lockouts: top target users (preview)",
            byTargetUser: queryResult.ByTargetUser,
            byTargetDomain: queryResult.ByTargetDomain,
            matchedEvents: queryResult.MatchedEvents,
            scannedEvents: queryResult.ScannedEvents,
            maxEventsScanned: queryResult.MaxEventsScanned,
            truncated: queryResult.Truncated);
        return Task.FromResult(response);
    }
}
