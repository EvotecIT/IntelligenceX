using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Reporting;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Retrieves a stored monitoring HTML report snapshot from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXReportSnapshotGetTool : TestimoXToolBase, ITool {
    private sealed record ReportSnapshotRequest(
        string HistoryDirectory,
        string ReportKey,
        bool IncludeHtml,
        int MaxChars);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_report_snapshot_get",
        "Get a monitoring HTML report snapshot from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots and contain monitoring.sqlite).")),
                ("report_key", ToolSchema.String("Exact report key to load.")),
                ("include_html", ToolSchema.Boolean("When true, include html content (capped by max_chars). Default false.")),
                ("max_chars", ToolSchema.Integer("Maximum characters returned for html when include_html=true.")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "html",
            "monitoring",
            "reporting",
            "snapshot",
            "fallback:requires_selection",
            "fallback_selection_keys:history_directory,report_key",
            "fallback_hint_keys:history_directory,report_key,include_html,max_chars"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXReportSnapshotGetTool"/> class.
    /// </summary>
    public TestimoXReportSnapshotGetTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<ReportSnapshotRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<ReportSnapshotRequest>.Failure("history_directory is required.");
            }

            var reportKey = reader.OptionalString("report_key");
            if (string.IsNullOrWhiteSpace(reportKey)) {
                return ToolRequestBindingResult<ReportSnapshotRequest>.Failure("report_key is required.");
            }

            return ToolRequestBindingResult<ReportSnapshotRequest>.Success(new ReportSnapshotRequest(
                HistoryDirectory: historyDirectory,
                ReportKey: reportKey,
                IncludeHtml: reader.Boolean("include_html", defaultValue: false),
                MaxChars: TestimoXMonitoringHistoryHelper.ResolveContentCharLimit(arguments, Options.MaxSnapshotContentChars)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<ReportSnapshotRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Monitoring pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Monitoring pack in host/service options before calling testimox_report_snapshot_get." },
                isTransient: false);
        }

        if (!TestimoXMonitoringHistoryHelper.TryResolveHistoryDatabasePath(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_report_snapshot_get",
                out var historyDirectory,
                out var databasePath,
                out var resolveError)) {
            return resolveError;
        }

        MonitoringReportSnapshot? snapshot;
        try {
            using var store = new MonitoringReportSnapshotStore(
                TestimoXMonitoringHistoryHelper.CreateSqliteDatabaseConfig(databasePath),
                TestimoXMonitoringHistoryHelper.CreateSqliteOptions(),
                historyDirectory);
            snapshot = await store.TryLoadAsync(context.Request.ReportKey, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring report snapshot query failed.");
        }

        if (snapshot is null) {
            return ToolResultV2.Error(
                errorCode: "not_found",
                error: $"Monitoring report snapshot '{context.Request.ReportKey}' was not found.",
                hints: new[] {
                    "Call testimox_report_job_history first to inspect available report_path/report_key values.",
                    "Verify that history_directory points at the correct monitoring history folder."
                },
                isTransient: false);
        }

        var htmlProjection = TestimoXMonitoringHistoryHelper.ProjectText(
            snapshot.Html,
            context.Request.IncludeHtml,
            context.Request.MaxChars);

        var row = new ReportSnapshotRow(
            ReportKey: snapshot.ReportKey,
            GeneratedUtc: snapshot.GeneratedUtc,
            SourceUpdatedUtc: snapshot.SourceUpdatedUtc,
            HtmlBytes: snapshot.HtmlBytes,
            HtmlHash: snapshot.HtmlHash ?? string.Empty,
            MetadataJson: TestimoXMonitoringHistoryHelper.NormalizeJsonPreview(snapshot.MetadataJson),
            HtmlPreview: htmlProjection.Preview,
            Html: htmlProjection.Content,
            HtmlIncluded: htmlProjection.Included,
            HtmlTruncated: htmlProjection.Truncated,
            HtmlCharsReturned: htmlProjection.ReturnedChars);

        var model = new ReportSnapshotResult(
            HistoryDirectory: historyDirectory,
            DatabasePath: databasePath,
            ReportKey: snapshot.ReportKey,
            Snapshot: row);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: new[] { row },
            viewRowsPath: "snapshot_view",
            title: "Monitoring HTML report snapshot",
            baseTruncated: row.HtmlTruncated,
            maxTop: 1,
            scanned: 1,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("database_path", databasePath);
                meta.Add("report_key", snapshot.ReportKey);
                meta.Add("html_included", row.HtmlIncluded);
                meta.Add("html_truncated", row.HtmlTruncated);
                meta.Add("html_chars_returned", row.HtmlCharsReturned);
            });
    }

    private sealed record ReportSnapshotResult(
        string HistoryDirectory,
        string DatabasePath,
        string ReportKey,
        ReportSnapshotRow Snapshot);

    private sealed record ReportSnapshotRow(
        string ReportKey,
        DateTimeOffset GeneratedUtc,
        DateTimeOffset? SourceUpdatedUtc,
        int HtmlBytes,
        string HtmlHash,
        string MetadataJson,
        string HtmlPreview,
        string Html,
        bool HtmlIncluded,
        bool HtmlTruncated,
        int HtmlCharsReturned);
}
