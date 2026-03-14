using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Loads a compact AD monitoring diagnostics snapshot from an allowed monitoring directory.
/// </summary>
public sealed class AdMonitoringDiagnosticsGetTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMaxSlowProbes = 5;
    private const int MaxSlowProbes = 20;

    private sealed record DiagnosticsRequest(string MonitoringDirectory, bool IncludeSlowProbes, int MaxSlowProbes);

    private sealed record SlowProbeRow(
        string Name,
        string ProbeType,
        string Status,
        string Target,
        DateTimeOffset? CompletedUtc,
        double DurationSeconds,
        double? IntervalSeconds,
        double? TimeoutSeconds,
        bool IntervalOverrun);

    private sealed record DiagnosticsRow(
        DateTimeOffset GeneratedUtc,
        DateTimeOffset SinceUtc,
        long NotificationSent,
        long NotificationFailed,
        long NotificationDeduped,
        long NotificationCooldownSuppressions,
        long NotificationRateLimitHits,
        int? NotificationQueueDepth,
        int? NotificationQueueCapacity,
        long? HistoryQueueDepth,
        int? HistoryQueueMaxDepth,
        long? HistoryWriteFailures,
        long? HistorySpoolFileCount,
        long? HistorySpoolItemCount,
        long? HistoryMaintenanceRuns,
        long? HistoryMaintenanceFailures,
        int SlowProbeCount,
        bool SlowProbesIncluded,
        int SlowProbesReturned,
        DateTimeOffset? SqliteLastCheckUtc,
        string SqliteLastCheckStatus,
        string SqliteLastCheckMessage,
        int? ReachabilityTargetsConfigured,
        int? ReachabilityHostsTracked,
        int? ReachabilityZonesTracked);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_diagnostics_get",
        "Get a compact AD monitoring diagnostics snapshot from an allowed monitoring directory.",
        ToolSchema.Object(
                ("monitoring_directory", ToolSchema.String("Monitoring directory to inspect (must be inside AllowedMonitoringRoots).")),
                ("include_slow_probes", ToolSchema.Boolean("When true, include capped slow probe rows from the diagnostics snapshot. Default false.")),
                ("max_slow_probes", ToolSchema.Integer("Maximum number of slow probe rows returned when include_slow_probes=true.")))
            .Required("monitoring_directory")
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "active_directory",
        tags: new[] {
            "monitoring",
            "diagnostics",
            "snapshot"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringDiagnosticsGetTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdMonitoringDiagnosticsGetTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<DiagnosticsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var monitoringDirectory = reader.OptionalString("monitoring_directory");
            if (string.IsNullOrWhiteSpace(monitoringDirectory)) {
                return ToolRequestBindingResult<DiagnosticsRequest>.Failure("monitoring_directory is required.");
            }

            return ToolRequestBindingResult<DiagnosticsRequest>.Success(new DiagnosticsRequest(
                MonitoringDirectory: monitoringDirectory,
                IncludeSlowProbes: reader.Boolean("include_slow_probes", defaultValue: false),
                MaxSlowProbes: ToolArgs.GetCappedInt32(arguments, "max_slow_probes", DefaultMaxSlowProbes, 1, MaxSlowProbes)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DiagnosticsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!AdMonitoringArtifactHelper.TryResolveMonitoringFilePath(
                Options,
                context.Request.MonitoringDirectory,
                MonitoringDiagnosticsSnapshot.DefaultFileName,
                "ad_monitoring_diagnostics_get",
                out var monitoringDirectory,
                out var snapshotPath,
                out var resolveError)) {
            return Task.FromResult(resolveError);
        }

        if (!MonitoringDiagnosticsSnapshot.TryLoad(snapshotPath, out var snapshot) || snapshot is null) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "unreadable_snapshot",
                error: "Monitoring diagnostics snapshot could not be loaded.",
                hints: new[] {
                    $"Verify that {MonitoringDiagnosticsSnapshot.DefaultFileName} contains valid JSON.",
                    $"Snapshot path: {snapshotPath}"
                },
                isTransient: false));
        }

        var slowProbes = snapshot.SlowProbes ?? Array.Empty<MonitoringSlowProbeSnapshot>();
        var includedSlowProbes = context.Request.IncludeSlowProbes
            ? slowProbes
                .OrderByDescending(static probe => probe.DurationSeconds)
                .ThenBy(static probe => probe.Name, StringComparer.OrdinalIgnoreCase)
                .Take(context.Request.MaxSlowProbes)
                .Select(static probe => new SlowProbeRow(
                    Name: probe.Name,
                    ProbeType: probe.Type.ToString(),
                    Status: probe.Status.ToString(),
                    Target: probe.Target ?? string.Empty,
                    CompletedUtc: probe.CompletedUtc,
                    DurationSeconds: probe.DurationSeconds,
                    IntervalSeconds: probe.IntervalSeconds ?? 0d,
                    TimeoutSeconds: probe.TimeoutSeconds ?? 0d,
                    IntervalOverrun: probe.IntervalOverrun))
                .ToArray()
            : Array.Empty<SlowProbeRow>();

        var row = new DiagnosticsRow(
            GeneratedUtc: snapshot.GeneratedUtc,
            SinceUtc: snapshot.SinceUtc,
            NotificationSent: snapshot.NotificationSent,
            NotificationFailed: snapshot.NotificationFailed,
            NotificationDeduped: snapshot.NotificationDeduped,
            NotificationCooldownSuppressions: snapshot.NotificationCooldownSuppressions,
            NotificationRateLimitHits: snapshot.NotificationRateLimitHits,
            NotificationQueueDepth: snapshot.NotificationQueueDepth,
            NotificationQueueCapacity: snapshot.NotificationQueueCapacity,
            HistoryQueueDepth: snapshot.HistoryQueueDepth,
            HistoryQueueMaxDepth: snapshot.HistoryQueueMaxDepth,
            HistoryWriteFailures: snapshot.HistoryWriteFailures,
            HistorySpoolFileCount: snapshot.HistorySpoolFileCount,
            HistorySpoolItemCount: snapshot.HistorySpoolItemCount,
            HistoryMaintenanceRuns: snapshot.HistoryMaintenanceRuns,
            HistoryMaintenanceFailures: snapshot.HistoryMaintenanceFailures,
            SlowProbeCount: slowProbes.Count,
            SlowProbesIncluded: context.Request.IncludeSlowProbes,
            SlowProbesReturned: includedSlowProbes.Length,
            SqliteLastCheckUtc: snapshot.SqliteHealth?.LastCheckUtc,
            SqliteLastCheckStatus: snapshot.SqliteHealth?.LastCheckStatus?.ToString() ?? string.Empty,
            SqliteLastCheckMessage: snapshot.SqliteHealth?.LastCheckMessage ?? string.Empty,
            ReachabilityTargetsConfigured: snapshot.Reachability?.TargetsConfigured,
            ReachabilityHostsTracked: snapshot.Reachability?.HostsTracked,
            ReachabilityZonesTracked: snapshot.Reachability?.ZonesTracked);

        var model = new {
            MonitoringDirectory = monitoringDirectory,
            SnapshotPath = snapshotPath,
            Snapshot = row,
            SlowProbes = includedSlowProbes,
            Raw = snapshot
        };

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "AD monitoring diagnostics snapshot",
            baseTruncated: !context.Request.IncludeSlowProbes && slowProbes.Count > 0,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("monitoring_directory", monitoringDirectory);
                meta.Add("snapshot_path", snapshotPath);
                meta.Add("slow_probe_count", slowProbes.Count);
                meta.Add("slow_probes_included", context.Request.IncludeSlowProbes);
                meta.Add("slow_probes_returned", includedSlowProbes.Length);
            }));
    }
}
