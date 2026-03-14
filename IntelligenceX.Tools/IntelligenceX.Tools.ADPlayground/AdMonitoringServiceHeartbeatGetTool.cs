using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Loads a persisted AD monitoring service heartbeat snapshot from an allowed monitoring directory.
/// </summary>
public sealed class AdMonitoringServiceHeartbeatGetTool : ActiveDirectoryToolBase, ITool {
    private sealed record HeartbeatRequest(string MonitoringDirectory);

    private sealed record HeartbeatRow(
        DateTimeOffset GeneratedUtc,
        string AgentName,
        int ProbeCount,
        double UptimeSeconds,
        DateTimeOffset? LastProbeStartedUtc,
        string LastProbeStartedName,
        string LastProbeStartedType,
        DateTimeOffset? LastProbeCompletedUtc,
        string LastProbeCompletedName,
        string LastProbeCompletedType,
        string LastProbeCompletedStatus,
        double? LastProbeCompletedDurationSeconds,
        bool DashboardInFlight,
        double? DashboardReportAgeSeconds,
        string DashboardReportStaleLevel,
        int? NotificationQueueCount,
        int? NotificationQueueCapacity,
        long? HistoryQueueDepth,
        int? HistoryQueueMaxDepth,
        long? HistoryWriteInFlight,
        bool StallDetected,
        double? StallAgeSeconds,
        double? StallThresholdSeconds,
        string StallReason,
        string DirectoryRefreshLastStatus,
        DateTimeOffset? DirectoryRefreshLastCheckUtc,
        long? DirectoryRefreshFailureCount);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_service_heartbeat_get",
        "Get a persisted AD monitoring service heartbeat snapshot from an allowed monitoring directory.",
        ToolSchema.Object(
                ("monitoring_directory", ToolSchema.String("Monitoring directory to inspect (must be inside AllowedMonitoringRoots).")))
            .Required("monitoring_directory")
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "active_directory",
        tags: new[] {
            "monitoring",
            "heartbeat",
            "snapshot"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringServiceHeartbeatGetTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdMonitoringServiceHeartbeatGetTool(ActiveDirectoryToolOptions options) : base(options) { }

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<HeartbeatRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var monitoringDirectory = reader.OptionalString("monitoring_directory");
            if (string.IsNullOrWhiteSpace(monitoringDirectory)) {
                return ToolRequestBindingResult<HeartbeatRequest>.Failure("monitoring_directory is required.");
            }

            return ToolRequestBindingResult<HeartbeatRequest>.Success(new HeartbeatRequest(monitoringDirectory));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<HeartbeatRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AdMonitoringArtifactHelper.TryResolveMonitoringFilePath(
                Options,
                context.Request.MonitoringDirectory,
                MonitoringServiceHeartbeatSnapshot.DefaultFileName,
                "ad_monitoring_service_heartbeat_get",
                out var monitoringDirectory,
                out var snapshotPath,
                out var resolveError)) {
            return Task.FromResult(resolveError);
        }

        if (!MonitoringServiceHeartbeatSnapshot.TryLoad(snapshotPath, out var snapshot) || snapshot is null) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "unreadable_snapshot",
                error: "Monitoring service heartbeat snapshot could not be loaded.",
                hints: new[] {
                    $"Verify that {MonitoringServiceHeartbeatSnapshot.DefaultFileName} contains valid JSON.",
                    $"Snapshot path: {snapshotPath}"
                },
                isTransient: false));
        }

        var row = new HeartbeatRow(
            GeneratedUtc: snapshot.GeneratedUtc,
            AgentName: snapshot.AgentName,
            ProbeCount: snapshot.ProbeCount,
            UptimeSeconds: snapshot.UptimeSeconds,
            LastProbeStartedUtc: snapshot.LastProbeStartedUtc,
            LastProbeStartedName: snapshot.LastProbeStartedName ?? string.Empty,
            LastProbeStartedType: snapshot.LastProbeStartedType.ToString(),
            LastProbeCompletedUtc: snapshot.LastProbeCompletedUtc,
            LastProbeCompletedName: snapshot.LastProbeCompletedName ?? string.Empty,
            LastProbeCompletedType: snapshot.LastProbeCompletedType.ToString(),
            LastProbeCompletedStatus: snapshot.LastProbeCompletedStatus.ToString(),
            LastProbeCompletedDurationSeconds: snapshot.LastProbeCompletedDurationSeconds,
            DashboardInFlight: snapshot.DashboardInFlight,
            DashboardReportAgeSeconds: snapshot.DashboardReportAgeSeconds,
            DashboardReportStaleLevel: snapshot.DashboardReportStaleLevel ?? string.Empty,
            NotificationQueueCount: snapshot.NotificationQueueCount,
            NotificationQueueCapacity: snapshot.NotificationQueueCapacity,
            HistoryQueueDepth: snapshot.HistoryQueueDepth,
            HistoryQueueMaxDepth: snapshot.HistoryQueueMaxDepth,
            HistoryWriteInFlight: snapshot.HistoryWriteInFlight,
            StallDetected: snapshot.StallDetected,
            StallAgeSeconds: snapshot.StallAgeSeconds,
            StallThresholdSeconds: snapshot.StallThresholdSeconds,
            StallReason: snapshot.StallReason ?? string.Empty,
            DirectoryRefreshLastStatus: snapshot.DirectoryRefreshLastStatus ?? string.Empty,
            DirectoryRefreshLastCheckUtc: snapshot.DirectoryRefreshLastCheckUtc,
            DirectoryRefreshFailureCount: snapshot.DirectoryRefreshFailureCount);

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
            title: "AD monitoring service heartbeat",
            baseTruncated: false,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("monitoring_directory", monitoringDirectory);
                meta.Add("snapshot_path", snapshotPath);
            }));
    }
}
