using System;
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

        if (!TestimoXAnalyticsHistoryHelper.TryResolveExistingHistoryArtifactPath(
                Options,
                context.Request.HistoryDirectory,
                MonitoringDiagnosticsSnapshot.DefaultFileName,
                toolName: "testimox_analytics_diagnostics_get",
                out var historyContext,
                out var snapshotPath,
                out var resolveError)) {
            return Task.FromResult(resolveError);
        }

        var result = MonitoringDiagnosticsQueryService.Query(
            snapshotPath,
            new MonitoringDiagnosticsQueryRequest(
                context.Request.IncludeSlowProbes,
                context.Request.MaxSlowProbes));
        if (result is null) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "unreadable_snapshot",
                error: "Monitoring diagnostics snapshot could not be loaded.",
                hints: new[] {
                    $"Verify that {MonitoringDiagnosticsSnapshot.DefaultFileName} contains valid JSON.",
                    $"Snapshot path: {snapshotPath}"
                },
                isTransient: false));
        }

        var projected = result.Snapshot;
        var includedSlowProbes = result.SlowProbes
            .Select(static probe => new SlowProbeRow(
                Name: probe.Name,
                ProbeType: probe.ProbeType,
                Status: probe.Status,
                Target: probe.Target,
                CompletedUtc: probe.CompletedUtc,
                DurationSeconds: probe.DurationSeconds,
                IntervalSeconds: probe.IntervalSeconds,
                TimeoutSeconds: probe.TimeoutSeconds,
                IntervalOverrun: probe.IntervalOverrun))
            .ToArray();

        var row = new MonitoringDiagnosticsRow(
            GeneratedUtc: projected.GeneratedUtc,
            SinceUtc: projected.SinceUtc,
            NotificationSent: projected.NotificationSent,
            NotificationFailed: projected.NotificationFailed,
            NotificationDeduped: projected.NotificationDeduped,
            NotificationCooldownSuppressions: projected.NotificationCooldownSuppressions,
            NotificationRateLimitHits: projected.NotificationRateLimitHits,
            NotificationQueueDepth: projected.NotificationQueueDepth,
            NotificationQueueCapacity: projected.NotificationQueueCapacity,
            NotificationQueueFallbacks: projected.NotificationQueueFallbacks,
            NotificationQueueDrops: projected.NotificationQueueDrops,
            NotificationLastFailedChannel: projected.NotificationLastFailedChannel,
            NotificationLastFailedError: projected.NotificationLastFailedError,
            ProactiveTriggers: projected.ProactiveTriggers,
            ProactiveFollowUpsScheduled: projected.ProactiveFollowUpsScheduled,
            HistoryQueueDepth: projected.HistoryQueueDepth,
            HistoryQueueMaxDepth: projected.HistoryQueueMaxDepth,
            HistoryWriteFailures: projected.HistoryWriteFailures,
            HistorySpoolFileCount: projected.HistorySpoolFileCount,
            HistorySpoolItemCount: projected.HistorySpoolItemCount,
            HistoryMaintenanceRuns: projected.HistoryMaintenanceRuns,
            HistoryMaintenanceFailures: projected.HistoryMaintenanceFailures,
            HistoryMaintenanceLastCompletedUtc: projected.HistoryMaintenanceLastCompletedUtc,
            HistoryMaintenanceLastError: projected.HistoryMaintenanceLastError,
            ProbeHardTimeoutInFlight: projected.ProbeHardTimeoutInFlight,
            AlertLogQueueDepth: projected.AlertLogQueueDepth,
            SmtpFailureStreak: projected.SmtpFailureStreak,
            SmtpCooldownUntilUtc: projected.SmtpCooldownUntilUtc,
            SlowProbeCount: projected.SlowProbeCount,
            SlowProbesIncluded: projected.SlowProbesIncluded,
            SlowProbesReturned: projected.SlowProbesReturned,
            SqliteLastCheckUtc: projected.SqliteLastCheckUtc,
            SqliteLastCheckStatus: projected.SqliteLastCheckStatus,
            SqliteLastCheckMessage: projected.SqliteLastCheckMessage,
            SqliteLastBackupUtc: projected.SqliteLastBackupUtc,
            SqliteLastBackupFile: projected.SqliteLastBackupFile,
            SqliteLastRestoreUtc: projected.SqliteLastRestoreUtc,
            SqliteLastRestoreFile: projected.SqliteLastRestoreFile,
            SqliteLastRestoreMessage: projected.SqliteLastRestoreMessage,
            ReachabilityAgent: projected.ReachabilityAgent,
            ReachabilityTargetsConfigured: projected.ReachabilityTargetsConfigured,
            ReachabilityHostsTracked: projected.ReachabilityHostsTracked,
            ReachabilityZonesTracked: projected.ReachabilityZonesTracked,
            ReachabilitySchedulerQueueDepth: projected.ReachabilitySchedulerQueueDepth,
            ReachabilityPersistQueueDepth: projected.ReachabilityPersistQueueDepth,
            ReachabilityPersistQueueDropped: projected.ReachabilityPersistQueueDropped,
            ReachabilityPingsStarted: projected.ReachabilityPingsStarted,
            ReachabilityPingsSucceeded: projected.ReachabilityPingsSucceeded,
            ReachabilityPingsFailed: projected.ReachabilityPingsFailed,
            ReachabilityStoreFailureStreak: projected.ReachabilityStoreFailureStreak,
            ReachabilityStoreBackoffUntilUtc: projected.ReachabilityStoreBackoffUntilUtc);

        var model = new MonitoringDiagnosticsResult(
            HistoryDirectory: historyContext.HistoryDirectory,
            SnapshotPath: snapshotPath,
            Snapshot: row,
            SlowProbes: includedSlowProbes);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "Analytics diagnostics snapshot",
            baseTruncated: !context.Request.IncludeSlowProbes && projected.SlowProbeCount > 0,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("history_directory", historyContext.HistoryDirectory);
                meta.Add("snapshot_path", snapshotPath);
                meta.Add("slow_probe_count", projected.SlowProbeCount);
                meta.Add("slow_probes_included", context.Request.IncludeSlowProbes);
                meta.Add("slow_probes_returned", includedSlowProbes.Length);
            }));
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
