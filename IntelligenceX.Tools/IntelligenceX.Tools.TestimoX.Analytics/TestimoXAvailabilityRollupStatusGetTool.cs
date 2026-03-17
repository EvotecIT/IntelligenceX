using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Loads availability rollup refresh status from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXAvailabilityRollupStatusGetTool : TestimoXToolBase, ITool {
    private sealed record RollupStatusRequest(string HistoryDirectory);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_availability_rollup_status_get",
        "Get availability rollup refresh status from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "availability",
            "monitoring",
            "rollup",
            "snapshot",
            "status"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXAvailabilityRollupStatusGetTool"/> class.
    /// </summary>
    public TestimoXAvailabilityRollupStatusGetTool(TestimoXToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<RollupStatusRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<RollupStatusRequest>.Failure("history_directory is required.");
            }

            return ToolRequestBindingResult<RollupStatusRequest>.Success(new RollupStatusRequest(historyDirectory));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<RollupStatusRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_availability_rollup_status_get." },
                isTransient: false));
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryFilePath(
                Options,
                context.Request.HistoryDirectory,
                MonitoringAvailabilityRollupSnapshot.DefaultFileName,
                toolName: "testimox_availability_rollup_status_get",
                out var historyDirectory,
                out var snapshotPath,
                out var resolveError)) {
            return Task.FromResult(resolveError);
        }

        if (!MonitoringAvailabilityRollupSnapshot.TryLoad(snapshotPath, out var snapshot) || snapshot is null) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "unreadable_snapshot",
                error: "Availability rollup snapshot could not be loaded.",
                hints: new[] {
                    $"Verify that {MonitoringAvailabilityRollupSnapshot.DefaultFileName} contains valid JSON.",
                    $"Snapshot path: {snapshotPath}"
                },
                isTransient: false));
        }

        var row = new RollupStatusRow(
            RefreshedUtc: snapshot.RefreshedUtc,
            HourlyLatestBucketUtc: snapshot.HourlyLatestBucketUtc,
            DailyLatestBucketUtc: snapshot.DailyLatestBucketUtc,
            HourlyRowCount: snapshot.HourlyRowCount,
            DailyRowCount: snapshot.DailyRowCount,
            LastError: snapshot.LastError ?? string.Empty);

        var model = new RollupStatusResult(
            HistoryDirectory: historyDirectory,
            SnapshotPath: snapshotPath,
            Snapshot: row);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "Availability rollup status",
            baseTruncated: false,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("snapshot_path", snapshotPath);
                meta.Add("hourly_row_count", row.HourlyRowCount);
                meta.Add("daily_row_count", row.DailyRowCount);
                meta.Add("has_error", !string.IsNullOrWhiteSpace(row.LastError));
            }));
    }

    private sealed record RollupStatusResult(
        string HistoryDirectory,
        string SnapshotPath,
        RollupStatusRow Snapshot);

    private sealed record RollupStatusRow(
        System.DateTimeOffset RefreshedUtc,
        System.DateTimeOffset? HourlyLatestBucketUtc,
        System.DateTimeOffset? DailyLatestBucketUtc,
        int HourlyRowCount,
        int DailyRowCount,
        string LastError);
}
