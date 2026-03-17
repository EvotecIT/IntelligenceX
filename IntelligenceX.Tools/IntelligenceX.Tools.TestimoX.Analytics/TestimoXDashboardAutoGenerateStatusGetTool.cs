using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Loads dashboard auto-generate runtime status from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXDashboardAutoGenerateStatusGetTool : TestimoXToolBase, ITool {
    private sealed record DashboardStatusRequest(string HistoryDirectory);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_dashboard_autogenerate_status_get",
        "Get dashboard auto-generate runtime status from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "dashboard",
            "monitoring",
            "snapshot",
            "status"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXDashboardAutoGenerateStatusGetTool"/> class.
    /// </summary>
    public TestimoXDashboardAutoGenerateStatusGetTool(TestimoXToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<DashboardStatusRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<DashboardStatusRequest>.Failure("history_directory is required.");
            }

            return ToolRequestBindingResult<DashboardStatusRequest>.Success(new DashboardStatusRequest(historyDirectory));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DashboardStatusRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_dashboard_autogenerate_status_get." },
                isTransient: false));
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryFilePath(
                Options,
                context.Request.HistoryDirectory,
                MonitoringDashboardAutoGenerateSnapshot.DefaultFileName,
                toolName: "testimox_dashboard_autogenerate_status_get",
                out var historyDirectory,
                out var snapshotPath,
                out var resolveError)) {
            return Task.FromResult(resolveError);
        }

        if (!MonitoringDashboardAutoGenerateSnapshot.TryLoad(snapshotPath, out var snapshot) || snapshot is null) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "unreadable_snapshot",
                error: "Dashboard auto-generate snapshot could not be loaded.",
                hints: new[] {
                    $"Verify that {MonitoringDashboardAutoGenerateSnapshot.DefaultFileName} contains valid JSON.",
                    $"Snapshot path: {snapshotPath}"
                },
                isTransient: false));
        }

        var row = new DashboardStatusRow(
            GeneratedUtc: snapshot.GeneratedUtc,
            ProfileName: snapshot.ProfileName ?? string.Empty,
            Enabled: snapshot.Enabled,
            IntervalSeconds: snapshot.Interval.TotalSeconds,
            ConfiguredTimeoutSeconds: snapshot.ConfiguredTimeout.TotalSeconds,
            EffectiveTimeoutSeconds: snapshot.EffectiveTimeout.TotalSeconds,
            NextRunUtc: snapshot.NextRunUtc,
            InFlight: snapshot.InFlight,
            InFlightRunId: snapshot.InFlightRunId,
            InFlightStartedUtc: snapshot.InFlightStartedUtc,
            InFlightPhase: snapshot.InFlightPhase ?? string.Empty,
            InFlightPhaseStartedUtc: snapshot.InFlightPhaseStartedUtc,
            LastRunId: snapshot.LastRunId,
            LastRunStartedUtc: snapshot.LastRunStartedUtc,
            LastRunCompletedUtc: snapshot.LastRunCompletedUtc,
            LastRunDurationSeconds: snapshot.LastRunDurationSeconds,
            LastRunHistoryLoadSeconds: snapshot.LastRunHistoryLoadSeconds,
            LastRunReportBuildSeconds: snapshot.LastRunReportBuildSeconds,
            LastRunReportRenderSeconds: snapshot.LastRunReportRenderSeconds,
            LastRunReportWriteSeconds: snapshot.LastRunReportWriteSeconds,
            LastOutcome: snapshot.LastOutcome ?? string.Empty,
            LastOutcomeDetails: snapshot.LastOutcomeDetails ?? string.Empty,
            LastSkipUtc: snapshot.LastSkipUtc,
            LastSkipReason: snapshot.LastSkipReason ?? string.Empty,
            BusyDeferralBypassCount: snapshot.BusyDeferralBypassCount,
            LastBusyDeferralBypassUtc: snapshot.LastBusyDeferralBypassUtc,
            LastBusyDeferralBypassReason: snapshot.LastBusyDeferralBypassReason ?? string.Empty,
            LastRunHistoryLoadCompleted: snapshot.LastRunHistoryLoadCompleted,
            LastRunFallbackGenerated: snapshot.LastRunFallbackGenerated,
            LastFallbackReason: snapshot.LastFallbackReason ?? string.Empty,
            HistoryBreakerOpenUntilUtc: snapshot.HistoryBreakerOpenUntilUtc,
            SqliteOomCooldownUntilUtc: snapshot.SqliteOomCooldownUntilUtc,
            ReportFile: SanitizePath(snapshot.ReportPath),
            ReportLastWriteUtc: snapshot.ReportLastWriteUtc,
            LiveSnapshotUpdatedUtc: snapshot.LiveSnapshotUpdatedUtc,
            ReportAgeSeconds: snapshot.ReportAgeSeconds,
            SkippedIntervals: snapshot.SkippedIntervals,
            BehindScheduleSeconds: snapshot.BehindScheduleSeconds,
            LastError: snapshot.LastError ?? string.Empty,
            HistoryEntriesOverride: snapshot.HistoryEntriesOverride,
            LastRunHistoryCacheMode: snapshot.LastRunHistoryCacheMode ?? string.Empty,
            LastRunHistoryIndexWarning: snapshot.LastRunHistoryIndexWarning ?? string.Empty,
            LastRunFallbackReportGenerated: snapshot.LastRunFallbackGenerated ?? false);

        var model = new DashboardStatusResult(
            HistoryDirectory: historyDirectory,
            SnapshotPath: snapshotPath,
            ReportPath: snapshot.ReportPath ?? string.Empty,
            Snapshot: row);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "Dashboard auto-generate status",
            baseTruncated: false,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("snapshot_path", snapshotPath);
                meta.Add("profile_name", row.ProfileName);
                meta.Add("enabled", row.Enabled);
                meta.Add("in_flight", row.InFlight);
                meta.Add("last_outcome", row.LastOutcome);
                meta.Add("report_file", row.ReportFile);
            }));
    }

    private static string SanitizePath(string? path) {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path);
    }

    private sealed record DashboardStatusResult(
        string HistoryDirectory,
        string SnapshotPath,
        string ReportPath,
        DashboardStatusRow Snapshot);

    private sealed record DashboardStatusRow(
        DateTimeOffset GeneratedUtc,
        string ProfileName,
        bool Enabled,
        double IntervalSeconds,
        double ConfiguredTimeoutSeconds,
        double EffectiveTimeoutSeconds,
        DateTimeOffset? NextRunUtc,
        bool InFlight,
        int? InFlightRunId,
        DateTimeOffset? InFlightStartedUtc,
        string InFlightPhase,
        DateTimeOffset? InFlightPhaseStartedUtc,
        int? LastRunId,
        DateTimeOffset? LastRunStartedUtc,
        DateTimeOffset? LastRunCompletedUtc,
        double? LastRunDurationSeconds,
        double? LastRunHistoryLoadSeconds,
        double? LastRunReportBuildSeconds,
        double? LastRunReportRenderSeconds,
        double? LastRunReportWriteSeconds,
        string LastOutcome,
        string LastOutcomeDetails,
        DateTimeOffset? LastSkipUtc,
        string LastSkipReason,
        int BusyDeferralBypassCount,
        DateTimeOffset? LastBusyDeferralBypassUtc,
        string LastBusyDeferralBypassReason,
        bool? LastRunHistoryLoadCompleted,
        bool? LastRunFallbackGenerated,
        string LastFallbackReason,
        DateTimeOffset? HistoryBreakerOpenUntilUtc,
        DateTimeOffset? SqliteOomCooldownUntilUtc,
        string ReportFile,
        DateTimeOffset? ReportLastWriteUtc,
        DateTimeOffset? LiveSnapshotUpdatedUtc,
        double? ReportAgeSeconds,
        int? SkippedIntervals,
        double? BehindScheduleSeconds,
        string LastError,
        int? HistoryEntriesOverride,
        string LastRunHistoryCacheMode,
        string LastRunHistoryIndexWarning,
        bool LastRunFallbackReportGenerated);
}
