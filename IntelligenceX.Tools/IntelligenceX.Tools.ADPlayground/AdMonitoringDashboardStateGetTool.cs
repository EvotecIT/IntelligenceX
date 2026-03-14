using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Loads persisted AD monitoring dashboard auto-generation state from an allowed monitoring directory.
/// </summary>
public sealed class AdMonitoringDashboardStateGetTool : ActiveDirectoryToolBase, ITool {
    private sealed record DashboardRequest(string MonitoringDirectory);

    private sealed record DashboardRow(
        DateTimeOffset GeneratedUtc,
        string ProfileName,
        bool Enabled,
        bool InFlight,
        int? InFlightRunId,
        DateTimeOffset? InFlightStartedUtc,
        string InFlightPhase,
        DateTimeOffset? NextRunUtc,
        int? LastRunId,
        DateTimeOffset? LastRunStartedUtc,
        DateTimeOffset? LastRunCompletedUtc,
        double? LastRunDurationSeconds,
        string LastOutcome,
        string LastOutcomeDetails,
        DateTimeOffset? LastSkipUtc,
        string LastSkipReason,
        int BusyDeferralBypassCount,
        DateTimeOffset? LastBusyDeferralBypassUtc,
        string LastBusyDeferralBypassReason,
        bool? LastRunFallbackGenerated,
        string LastFallbackReason,
        DateTimeOffset? HistoryBreakerOpenUntilUtc,
        DateTimeOffset? SqliteOomCooldownUntilUtc,
        string ReportFileName,
        DateTimeOffset? ReportLastWriteUtc,
        double? ReportAgeSeconds,
        int? SkippedIntervals,
        double? BehindScheduleSeconds,
        string LastError);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_dashboard_state_get",
        "Get persisted AD monitoring dashboard auto-generation state from an allowed monitoring directory.",
        ToolSchema.Object(
                ("monitoring_directory", ToolSchema.String("Monitoring directory to inspect (must be inside AllowedMonitoringRoots).")))
            .Required("monitoring_directory")
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "active_directory",
        tags: new[] {
            "monitoring",
            "dashboard",
            "snapshot"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringDashboardStateGetTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdMonitoringDashboardStateGetTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <summary>
    /// Gets the tool definition.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool pipeline.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Serialized tool response.</returns>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(arguments, cancellationToken, BindRequest, ExecuteAsync);
    }

    private static ToolRequestBindingResult<DashboardRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var monitoringDirectory = reader.OptionalString("monitoring_directory");
            if (string.IsNullOrWhiteSpace(monitoringDirectory)) {
                return ToolRequestBindingResult<DashboardRequest>.Failure("monitoring_directory is required.");
            }

            return ToolRequestBindingResult<DashboardRequest>.Success(new DashboardRequest(monitoringDirectory));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DashboardRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AdMonitoringArtifactHelper.TryResolveMonitoringFilePath(
                Options,
                context.Request.MonitoringDirectory,
                MonitoringDashboardAutoGenerateSnapshot.DefaultFileName,
                "ad_monitoring_dashboard_state_get",
                out var monitoringDirectory,
                out var snapshotPath,
                out var resolveError)) {
            return Task.FromResult(resolveError);
        }

        if (!MonitoringDashboardAutoGenerateSnapshot.TryLoad(snapshotPath, out var snapshot) || snapshot is null) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "unreadable_snapshot",
                error: "Monitoring dashboard state snapshot could not be loaded.",
                hints: new[] {
                    $"Verify that {MonitoringDashboardAutoGenerateSnapshot.DefaultFileName} contains valid JSON.",
                    $"Snapshot path: {snapshotPath}"
                },
                isTransient: false));
        }

        var row = new DashboardRow(
            GeneratedUtc: snapshot.GeneratedUtc,
            ProfileName: snapshot.ProfileName ?? string.Empty,
            Enabled: snapshot.Enabled,
            InFlight: snapshot.InFlight,
            InFlightRunId: snapshot.InFlightRunId,
            InFlightStartedUtc: snapshot.InFlightStartedUtc,
            InFlightPhase: snapshot.InFlightPhase ?? string.Empty,
            NextRunUtc: snapshot.NextRunUtc,
            LastRunId: snapshot.LastRunId,
            LastRunStartedUtc: snapshot.LastRunStartedUtc,
            LastRunCompletedUtc: snapshot.LastRunCompletedUtc,
            LastRunDurationSeconds: snapshot.LastRunDurationSeconds,
            LastOutcome: snapshot.LastOutcome ?? string.Empty,
            LastOutcomeDetails: snapshot.LastOutcomeDetails ?? string.Empty,
            LastSkipUtc: snapshot.LastSkipUtc,
            LastSkipReason: snapshot.LastSkipReason ?? string.Empty,
            BusyDeferralBypassCount: snapshot.BusyDeferralBypassCount,
            LastBusyDeferralBypassUtc: snapshot.LastBusyDeferralBypassUtc,
            LastBusyDeferralBypassReason: snapshot.LastBusyDeferralBypassReason ?? string.Empty,
            LastRunFallbackGenerated: snapshot.LastRunFallbackGenerated,
            LastFallbackReason: snapshot.LastFallbackReason ?? string.Empty,
            HistoryBreakerOpenUntilUtc: snapshot.HistoryBreakerOpenUntilUtc,
            SqliteOomCooldownUntilUtc: snapshot.SqliteOomCooldownUntilUtc,
            ReportFileName: string.IsNullOrWhiteSpace(snapshot.ReportPath) ? string.Empty : Path.GetFileName(snapshot.ReportPath),
            ReportLastWriteUtc: snapshot.ReportLastWriteUtc,
            ReportAgeSeconds: snapshot.ReportAgeSeconds,
            SkippedIntervals: snapshot.SkippedIntervals,
            BehindScheduleSeconds: snapshot.BehindScheduleSeconds,
            LastError: snapshot.LastError ?? string.Empty);

        var model = new {
            MonitoringDirectory = monitoringDirectory,
            SnapshotPath = snapshotPath,
            Snapshot = row,
            Raw = snapshot
        };

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "AD monitoring dashboard state",
            baseTruncated: false,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("monitoring_directory", monitoringDirectory);
                meta.Add("snapshot_path", snapshotPath);
            }));
    }
}
