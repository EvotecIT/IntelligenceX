using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Loads persisted AD monitoring metrics from an allowed monitoring directory.
/// </summary>
public sealed class AdMonitoringMetricsGetTool : ActiveDirectoryToolBase, ITool {
    private sealed record MetricsRequest(string MonitoringDirectory);

    private sealed record MetricsRow(
        DateTimeOffset GeneratedUtc,
        DateTimeOffset SinceUtc,
        string Agent,
        int ScheduledProbes,
        int InFlightProbes,
        int DueProbes,
        double DueDelayAverageSeconds,
        double DueDelayMaxSeconds,
        int ConfiguredMaxConcurrentProbes,
        int EffectiveMaxConcurrentProbes,
        bool SchedulerAutoPilotEnabled,
        bool SchedulerAutoPilotBusy,
        double? CpuLoadPercent,
        double? MemoryUsagePercent,
        long? ProcessWorkingSetBytes,
        long? ProcessPrivateBytes,
        long SchedulerTicks,
        long SchedulerStarted,
        long SchedulerSkippedNoSlots,
        long SchedulerSkippedGated,
        long SchedulerDeferredBacklog,
        long SchedulerDeferredHealth,
        long RetryBudgetExceeded,
        long ProbeTimeouts,
        int StatusUp,
        int StatusDown,
        int StatusDegraded,
        int StatusUnknown,
        int StatusRecovering,
        double LastDurationAverageMs,
        double LastDurationMaxMs,
        double LastResultAverageAgeSeconds,
        double LastResultMaxAgeSeconds);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_metrics_get",
        "Get persisted AD monitoring scheduler and runtime metrics from an allowed monitoring directory.",
        ToolSchema.Object(
                ("monitoring_directory", ToolSchema.String("Monitoring directory to inspect (must be inside AllowedMonitoringRoots).")))
            .Required("monitoring_directory")
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "active_directory",
        tags: new[] {
            "monitoring",
            "metrics",
            "snapshot"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringMetricsGetTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdMonitoringMetricsGetTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<MetricsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var monitoringDirectory = reader.OptionalString("monitoring_directory");
            if (string.IsNullOrWhiteSpace(monitoringDirectory)) {
                return ToolRequestBindingResult<MetricsRequest>.Failure("monitoring_directory is required.");
            }

            return ToolRequestBindingResult<MetricsRequest>.Success(new MetricsRequest(monitoringDirectory));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<MetricsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AdMonitoringArtifactHelper.TryResolveMonitoringFilePath(
                Options,
                context.Request.MonitoringDirectory,
                MonitoringMetricsSnapshot.DefaultFileName,
                "ad_monitoring_metrics_get",
                out var monitoringDirectory,
                out var snapshotPath,
                out var resolveError)) {
            return Task.FromResult(resolveError);
        }

        if (!MonitoringMetricsSnapshot.TryLoad(snapshotPath, out var snapshot) || snapshot is null) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "unreadable_snapshot",
                error: "Monitoring metrics snapshot could not be loaded.",
                hints: new[] {
                    $"Verify that {MonitoringMetricsSnapshot.DefaultFileName} contains valid JSON.",
                    $"Snapshot path: {snapshotPath}"
                },
                isTransient: false));
        }

        var row = new MetricsRow(
            GeneratedUtc: snapshot.GeneratedUtc,
            SinceUtc: snapshot.SinceUtc,
            Agent: snapshot.Agent,
            ScheduledProbes: snapshot.ScheduledProbes,
            InFlightProbes: snapshot.InFlightProbes,
            DueProbes: snapshot.DueProbes,
            DueDelayAverageSeconds: snapshot.DueDelayAverageSeconds,
            DueDelayMaxSeconds: snapshot.DueDelayMaxSeconds,
            ConfiguredMaxConcurrentProbes: snapshot.ConfiguredMaxConcurrentProbes,
            EffectiveMaxConcurrentProbes: snapshot.EffectiveMaxConcurrentProbes,
            SchedulerAutoPilotEnabled: snapshot.SchedulerAutoPilotEnabled,
            SchedulerAutoPilotBusy: snapshot.SchedulerAutoPilotBusy,
            CpuLoadPercent: snapshot.CpuLoadPercent,
            MemoryUsagePercent: snapshot.MemoryUsagePercent,
            ProcessWorkingSetBytes: snapshot.ProcessWorkingSetBytes,
            ProcessPrivateBytes: snapshot.ProcessPrivateBytes,
            SchedulerTicks: snapshot.SchedulerTicks,
            SchedulerStarted: snapshot.SchedulerStarted,
            SchedulerSkippedNoSlots: snapshot.SchedulerSkippedNoSlots,
            SchedulerSkippedGated: snapshot.SchedulerSkippedGated,
            SchedulerDeferredBacklog: snapshot.SchedulerDeferredBacklog,
            SchedulerDeferredHealth: snapshot.SchedulerDeferredHealth,
            RetryBudgetExceeded: snapshot.RetryBudgetExceeded,
            ProbeTimeouts: snapshot.ProbeTimeouts,
            StatusUp: snapshot.StatusUp,
            StatusDown: snapshot.StatusDown,
            StatusDegraded: snapshot.StatusDegraded,
            StatusUnknown: snapshot.StatusUnknown,
            StatusRecovering: snapshot.StatusRecovering,
            LastDurationAverageMs: snapshot.LastDurationAverageMs,
            LastDurationMaxMs: snapshot.LastDurationMaxMs,
            LastResultAverageAgeSeconds: snapshot.LastResultAverageAgeSeconds,
            LastResultMaxAgeSeconds: snapshot.LastResultMaxAgeSeconds);

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
            title: "AD monitoring metrics snapshot",
            baseTruncated: false,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("monitoring_directory", monitoringDirectory);
                meta.Add("snapshot_path", snapshotPath);
                meta.Add("probe_entries_total", snapshot.ProbeEntriesTotal);
                meta.Add("probe_entries_returned", snapshot.ProbeEntriesReturned);
            }));
    }
}
