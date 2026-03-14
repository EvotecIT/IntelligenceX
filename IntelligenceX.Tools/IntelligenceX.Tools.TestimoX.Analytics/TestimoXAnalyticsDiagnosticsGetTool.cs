using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Loads a compact analytics diagnostics snapshot from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXAnalyticsDiagnosticsGetTool : TestimoXToolBase, ITool {
    private const int DefaultMaxSlowProbes = 5;
    private const int MaxSlowProbes = 20;

    private sealed record MonitoringDiagnosticsRequest(
        string HistoryDirectory,
        bool IncludeSlowProbes,
        int MaxSlowProbes);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_analytics_diagnostics_get",
        "Get a compact analytics diagnostics snapshot from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots).")),
                ("include_slow_probes", ToolSchema.Boolean("When true, include capped slow probe rows from the diagnostics snapshot. Default false.")),
                ("max_slow_probes", ToolSchema.Integer("Maximum number of slow probe rows returned when include_slow_probes=true.")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "diagnostics",
            "monitoring",
            "snapshot"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXAnalyticsDiagnosticsGetTool"/> class.
    /// </summary>
    public TestimoXAnalyticsDiagnosticsGetTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<MonitoringDiagnosticsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<MonitoringDiagnosticsRequest>.Failure("history_directory is required.");
            }

            return ToolRequestBindingResult<MonitoringDiagnosticsRequest>.Success(new MonitoringDiagnosticsRequest(
                HistoryDirectory: historyDirectory,
                IncludeSlowProbes: reader.Boolean("include_slow_probes", defaultValue: false),
                MaxSlowProbes: ToolArgs.GetCappedInt32(
                    arguments: arguments,
                    key: "max_slow_probes",
                    defaultValue: DefaultMaxSlowProbes,
                    minInclusive: 1,
                    maxInclusive: MaxSlowProbes)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<MonitoringDiagnosticsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_analytics_diagnostics_get." },
                isTransient: false));
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryFilePath(
                Options,
                context.Request.HistoryDirectory,
                MonitoringDiagnosticsSnapshot.DefaultFileName,
                toolName: "testimox_analytics_diagnostics_get",
                out var historyDirectory,
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
                    IntervalSeconds: probe.IntervalSeconds,
                    TimeoutSeconds: probe.TimeoutSeconds,
                    IntervalOverrun: probe.IntervalOverrun))
                .ToArray()
            : Array.Empty<SlowProbeRow>();

        var row = new MonitoringDiagnosticsRow(
            GeneratedUtc: snapshot.GeneratedUtc,
            SinceUtc: snapshot.SinceUtc,
            NotificationSent: snapshot.NotificationSent,
            NotificationFailed: snapshot.NotificationFailed,
            NotificationDeduped: snapshot.NotificationDeduped,
            NotificationCooldownSuppressions: snapshot.NotificationCooldownSuppressions,
            NotificationRateLimitHits: snapshot.NotificationRateLimitHits,
            NotificationQueueDepth: snapshot.NotificationQueueDepth,
            NotificationQueueCapacity: snapshot.NotificationQueueCapacity,
            NotificationQueueFallbacks: snapshot.NotificationQueueFallbacks,
            NotificationQueueDrops: snapshot.NotificationQueueDrops,
            NotificationLastFailedChannel: snapshot.NotificationLastFailedChannel ?? string.Empty,
            NotificationLastFailedError: snapshot.NotificationLastFailedError ?? string.Empty,
            ProactiveTriggers: snapshot.ProactiveTriggers,
            ProactiveFollowUpsScheduled: snapshot.ProactiveFollowUpsScheduled,
            HistoryQueueDepth: snapshot.HistoryQueueDepth,
            HistoryQueueMaxDepth: snapshot.HistoryQueueMaxDepth,
            HistoryWriteFailures: snapshot.HistoryWriteFailures,
            HistorySpoolFileCount: snapshot.HistorySpoolFileCount,
            HistorySpoolItemCount: snapshot.HistorySpoolItemCount,
            HistoryMaintenanceRuns: snapshot.HistoryMaintenanceRuns,
            HistoryMaintenanceFailures: snapshot.HistoryMaintenanceFailures,
            HistoryMaintenanceLastCompletedUtc: snapshot.HistoryMaintenanceLastCompletedUtc,
            HistoryMaintenanceLastError: snapshot.HistoryMaintenanceLastError ?? string.Empty,
            ProbeHardTimeoutInFlight: snapshot.ProbeHardTimeoutInFlight,
            AlertLogQueueDepth: snapshot.AlertLogQueueDepth,
            SmtpFailureStreak: snapshot.SmtpFailureStreak,
            SmtpCooldownUntilUtc: snapshot.SmtpCooldownUntilUtc,
            SlowProbeCount: slowProbes.Count,
            SlowProbesIncluded: context.Request.IncludeSlowProbes,
            SlowProbesReturned: includedSlowProbes.Length,
            SqliteLastCheckUtc: snapshot.SqliteHealth?.LastCheckUtc,
            SqliteLastCheckStatus: snapshot.SqliteHealth?.LastCheckStatus?.ToString() ?? string.Empty,
            SqliteLastCheckMessage: snapshot.SqliteHealth?.LastCheckMessage ?? string.Empty,
            SqliteLastBackupUtc: snapshot.SqliteHealth?.LastBackupUtc,
            SqliteLastBackupFile: SanitizePath(snapshot.SqliteHealth?.LastBackupPath),
            SqliteLastRestoreUtc: snapshot.SqliteHealth?.LastRestoreUtc,
            SqliteLastRestoreFile: SanitizePath(snapshot.SqliteHealth?.LastRestoreSource),
            SqliteLastRestoreMessage: snapshot.SqliteHealth?.LastRestoreMessage ?? string.Empty,
            ReachabilityAgent: snapshot.Reachability?.Agent ?? string.Empty,
            ReachabilityTargetsConfigured: snapshot.Reachability?.TargetsConfigured,
            ReachabilityHostsTracked: snapshot.Reachability?.HostsTracked,
            ReachabilityZonesTracked: snapshot.Reachability?.ZonesTracked,
            ReachabilitySchedulerQueueDepth: snapshot.Reachability?.SchedulerQueueDepth,
            ReachabilityPersistQueueDepth: snapshot.Reachability?.PersistQueueDepth,
            ReachabilityPersistQueueDropped: snapshot.Reachability?.PersistQueueDropped,
            ReachabilityPingsStarted: snapshot.Reachability?.PingsStarted,
            ReachabilityPingsSucceeded: snapshot.Reachability?.PingsSucceeded,
            ReachabilityPingsFailed: snapshot.Reachability?.PingsFailed,
            ReachabilityStoreFailureStreak: snapshot.Reachability?.StoreFailureStreak,
            ReachabilityStoreBackoffUntilUtc: snapshot.Reachability?.StoreBackoffUntilUtc);

        var model = new MonitoringDiagnosticsResult(
            HistoryDirectory: historyDirectory,
            SnapshotPath: snapshotPath,
            Snapshot: row,
            SlowProbes: includedSlowProbes);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "Analytics diagnostics snapshot",
            baseTruncated: !context.Request.IncludeSlowProbes && slowProbes.Count > 0,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("snapshot_path", snapshotPath);
                meta.Add("slow_probe_count", slowProbes.Count);
                meta.Add("slow_probes_included", context.Request.IncludeSlowProbes);
                meta.Add("slow_probes_returned", includedSlowProbes.Length);
            }));
    }

    private static string SanitizePath(string? path) {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFileName(path);
    }

    private sealed record MonitoringDiagnosticsResult(
        string HistoryDirectory,
        string SnapshotPath,
        MonitoringDiagnosticsRow Snapshot,
        IReadOnlyList<SlowProbeRow> SlowProbes);

    private sealed record MonitoringDiagnosticsRow(
        DateTimeOffset GeneratedUtc,
        DateTimeOffset SinceUtc,
        long NotificationSent,
        long NotificationFailed,
        long NotificationDeduped,
        long NotificationCooldownSuppressions,
        long NotificationRateLimitHits,
        int? NotificationQueueDepth,
        int? NotificationQueueCapacity,
        long? NotificationQueueFallbacks,
        long? NotificationQueueDrops,
        string NotificationLastFailedChannel,
        string NotificationLastFailedError,
        long ProactiveTriggers,
        long ProactiveFollowUpsScheduled,
        long? HistoryQueueDepth,
        int? HistoryQueueMaxDepth,
        long? HistoryWriteFailures,
        long? HistorySpoolFileCount,
        long? HistorySpoolItemCount,
        long? HistoryMaintenanceRuns,
        long? HistoryMaintenanceFailures,
        DateTimeOffset? HistoryMaintenanceLastCompletedUtc,
        string HistoryMaintenanceLastError,
        int? ProbeHardTimeoutInFlight,
        long? AlertLogQueueDepth,
        int? SmtpFailureStreak,
        DateTimeOffset? SmtpCooldownUntilUtc,
        int SlowProbeCount,
        bool SlowProbesIncluded,
        int SlowProbesReturned,
        DateTimeOffset? SqliteLastCheckUtc,
        string SqliteLastCheckStatus,
        string SqliteLastCheckMessage,
        DateTimeOffset? SqliteLastBackupUtc,
        string SqliteLastBackupFile,
        DateTimeOffset? SqliteLastRestoreUtc,
        string SqliteLastRestoreFile,
        string SqliteLastRestoreMessage,
        string ReachabilityAgent,
        int? ReachabilityTargetsConfigured,
        int? ReachabilityHostsTracked,
        int? ReachabilityZonesTracked,
        int? ReachabilitySchedulerQueueDepth,
        long? ReachabilityPersistQueueDepth,
        long? ReachabilityPersistQueueDropped,
        long? ReachabilityPingsStarted,
        long? ReachabilityPingsSucceeded,
        long? ReachabilityPingsFailed,
        int? ReachabilityStoreFailureStreak,
        DateTimeOffset? ReachabilityStoreBackoffUntilUtc);

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
}
