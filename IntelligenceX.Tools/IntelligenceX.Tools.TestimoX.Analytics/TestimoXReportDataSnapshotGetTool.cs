using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Reporting;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Retrieves a stored monitoring report data snapshot from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXReportDataSnapshotGetTool : TestimoXToolBase, ITool {
    private sealed record ReportDataSnapshotRequest(
        string HistoryDirectory,
        string ReportKey,
        bool IncludePayload,
        int MaxChars);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_report_data_snapshot_get",
        "Get a monitoring report data snapshot from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots and contain monitoring.sqlite).")),
                ("report_key", ToolSchema.String("Exact report key to load.")),
                ("include_payload", ToolSchema.Boolean("When true, include payload_json (capped by max_chars). Default false.")),
                ("max_chars", ToolSchema.Integer("Maximum characters returned for payload_json when include_payload=true.")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "monitoring",
            "reporting",
            "snapshot",
            "data"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXReportDataSnapshotGetTool"/> class.
    /// </summary>
    public TestimoXReportDataSnapshotGetTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<ReportDataSnapshotRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<ReportDataSnapshotRequest>.Failure("history_directory is required.");
            }

            var reportKey = reader.OptionalString("report_key");
            if (string.IsNullOrWhiteSpace(reportKey)) {
                return ToolRequestBindingResult<ReportDataSnapshotRequest>.Failure("report_key is required.");
            }

            return ToolRequestBindingResult<ReportDataSnapshotRequest>.Success(new ReportDataSnapshotRequest(
                HistoryDirectory: historyDirectory,
                ReportKey: reportKey,
                IncludePayload: reader.Boolean("include_payload", defaultValue: false),
                MaxChars: TestimoXAnalyticsHistoryHelper.ResolveContentCharLimit(arguments, Options.MaxSnapshotContentChars)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<ReportDataSnapshotRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_report_data_snapshot_get." },
                isTransient: false);
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryDatabasePath(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_report_data_snapshot_get",
                out var historyDirectory,
                out var databasePath,
                out var resolveError)) {
            return resolveError;
        }

        MonitoringReportDataSnapshot? snapshot;
        try {
            using var store = new MonitoringReportDataSnapshotStore(
                TestimoXAnalyticsHistoryHelper.CreateSqliteDatabaseConfig(databasePath),
                TestimoXAnalyticsHistoryHelper.CreateSqliteOptions(),
                historyDirectory);
            snapshot = await store.TryLoadAsync(context.Request.ReportKey, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring report data snapshot query failed.");
        }

        if (snapshot is null) {
            return ToolResultV2.Error(
                errorCode: "not_found",
                error: $"Monitoring report data snapshot '{context.Request.ReportKey}' was not found.",
                hints: new[] {
                    "Call testimox_report_job_history first to inspect available report_path/report_key values.",
                    "Verify that history_directory points at the correct monitoring history folder."
                },
                isTransient: false);
        }

        var payloadProjection = TestimoXAnalyticsHistoryHelper.ProjectText(
            snapshot.PayloadJson,
            context.Request.IncludePayload,
            context.Request.MaxChars);

        var row = new ReportDataSnapshotRow(
            ReportKey: snapshot.ReportKey,
            GeneratedUtc: snapshot.GeneratedUtc,
            SourceUpdatedUtc: snapshot.SourceUpdatedUtc,
            PayloadBytes: snapshot.PayloadBytes,
            PayloadHash: snapshot.PayloadHash ?? string.Empty,
            MetadataJson: TestimoXAnalyticsHistoryHelper.NormalizeJsonPreview(snapshot.MetadataJson),
            PayloadPreview: payloadProjection.Preview,
            PayloadJson: payloadProjection.Content,
            PayloadIncluded: payloadProjection.Included,
            PayloadTruncated: payloadProjection.Truncated,
            PayloadCharsReturned: payloadProjection.ReturnedChars);

        var model = new ReportDataSnapshotResult(
            HistoryDirectory: historyDirectory,
            DatabasePath: databasePath,
            ReportKey: snapshot.ReportKey,
            Snapshot: row);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "Monitoring report data snapshot",
            baseTruncated: row.PayloadTruncated,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("database_path", databasePath);
                meta.Add("report_key", snapshot.ReportKey);
                meta.Add("payload_included", row.PayloadIncluded);
                meta.Add("payload_truncated", row.PayloadTruncated);
                meta.Add("payload_chars_returned", row.PayloadCharsReturned);
            });
    }

    private sealed record ReportDataSnapshotResult(
        string HistoryDirectory,
        string DatabasePath,
        string ReportKey,
        ReportDataSnapshotRow Snapshot);

    private sealed record ReportDataSnapshotRow(
        string ReportKey,
        DateTimeOffset GeneratedUtc,
        DateTimeOffset? SourceUpdatedUtc,
        int PayloadBytes,
        string PayloadHash,
        string MetadataJson,
        string PayloadPreview,
        string PayloadJson,
        bool PayloadIncluded,
        bool PayloadTruncated,
        int PayloadCharsReturned);
}
