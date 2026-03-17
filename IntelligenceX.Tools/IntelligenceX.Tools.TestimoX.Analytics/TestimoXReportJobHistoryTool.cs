using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Reporting;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Lists monitoring report generation jobs from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXReportJobHistoryTool : TestimoXToolBase, ITool {
    private static readonly string[] StatusNames = Enum.GetNames(typeof(MonitoringReportJobStatus))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed record ReportJobHistoryRequest(
        string HistoryDirectory,
        string? JobKey,
        DateTime? SinceUtc,
        IReadOnlyCollection<MonitoringReportJobStatus>? StatusFilter,
        int? PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_report_job_history",
        "List monitoring report generation jobs from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots and contain monitoring.sqlite).")),
                ("job_key", ToolSchema.String("Optional exact report job key filter (this key is also emitted as report_key for snapshot follow-up).")),
                ("report_key", ToolSchema.String("Optional alias for job_key when following snapshot-oriented report flows.")),
                ("since_utc", ToolSchema.String("Optional ISO-8601 UTC lower bound for started_utc.")),
                ("statuses", ToolSchema.Array(ToolSchema.String("Monitoring report job status.").Enum(StatusNames), "Optional status filters (any-match).")),
                ("page_size", ToolSchema.Integer("Optional number of jobs to return in this page.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched jobs (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "history",
            "jobs",
            "monitoring",
            "reporting"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXReportJobHistoryTool"/> class.
    /// </summary>
    public TestimoXReportJobHistoryTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<ReportJobHistoryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<ReportJobHistoryRequest>.Failure("history_directory is required.");
            }

            var jobKey = reader.OptionalString("job_key");
            var reportKey = reader.OptionalString("report_key");
            if (!string.IsNullOrWhiteSpace(jobKey)
                && !string.IsNullOrWhiteSpace(reportKey)
                && !string.Equals(jobKey, reportKey, StringComparison.OrdinalIgnoreCase)) {
                return ToolRequestBindingResult<ReportJobHistoryRequest>.Failure("job_key and report_key must match when both are provided.");
            }

            var effectiveJobKey = !string.IsNullOrWhiteSpace(reportKey) ? reportKey : jobKey;
            if (!ToolTime.TryParseUtcOptional(reader.OptionalString("since_utc"), out var sinceUtc, out var timeError)) {
                return ToolRequestBindingResult<ReportJobHistoryRequest>.Failure($"since_utc: {timeError}");
            }

            var requestedStatuses = reader.DistinctStringArray("statuses");
            if (!TryParseStatusFilter(requestedStatuses, out var statusFilter, out var statusError)) {
                return ToolRequestBindingResult<ReportJobHistoryRequest>.Failure(statusError ?? "Invalid statuses argument.");
            }

            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxHistoryRowsInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<ReportJobHistoryRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<ReportJobHistoryRequest>.Success(new ReportJobHistoryRequest(
                HistoryDirectory: historyDirectory,
                JobKey: effectiveJobKey,
                SinceUtc: sinceUtc,
                StatusFilter: statusFilter,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<ReportJobHistoryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_report_job_history." },
                isTransient: false);
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryReadContext(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_report_job_history",
                out var historyContext,
                out var resolveError)) {
            return resolveError;
        }

        MonitoringReportJobQueryResult result;
        try {
            var service = new MonitoringReportJobQueryService(
                historyContext.DatabaseConfig,
                historyContext.SqliteOptions,
                historyContext.HistoryDirectory);
            result = await service.QueryRecentAsync(
                    new MonitoringReportJobQueryRequest(
                        JobKey: context.Request.JobKey,
                        SinceUtc: context.Request.SinceUtc.HasValue
                            ? new DateTimeOffset(DateTime.SpecifyKind(context.Request.SinceUtc.Value, DateTimeKind.Utc))
                            : null,
                        StatusFilter: context.Request.StatusFilter,
                        MaxRows: Options.MaxHistoryRowsInCatalog,
                        PageSize: context.Request.PageSize,
                        Offset: context.Request.Offset),
                    cancellationToken)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring report job history query failed.");
        }

        var rows = result.Rows
            .Select(static row => new ReportJobHistoryRow(
                JobId: row.JobId,
                JobKey: row.JobKey,
                ReportKey: row.ReportKey,
                Trigger: row.Trigger,
                ReportPath: row.ReportPath,
                Status: row.Status.ToString(),
                StartedUtc: row.StartedUtc,
                CompletedUtc: row.CompletedUtc,
                DurationSeconds: row.DurationSeconds,
                Outcome: row.Outcome,
                ErrorText: row.ErrorText,
                HistoryEntries: row.HistoryEntries,
                HistoryRootCount: row.HistoryRootCount,
                HistoryProbeCount: row.HistoryProbeCount,
                HistorySampleCount: row.HistorySampleCount,
                HistoryLoadSeconds: row.HistoryLoadSeconds,
                HistoryCacheMode: row.HistoryCacheMode,
                HistoryIndexWarning: row.HistoryIndexWarning,
                ReportBuildSeconds: row.ReportBuildSeconds,
                ReportRenderSeconds: row.ReportRenderSeconds,
                ReportWriteSeconds: row.ReportWriteSeconds,
                ReportBytes: row.ReportBytes,
                ReportHash: row.ReportHash,
                SourceUpdatedUtc: row.SourceUpdatedUtc))
            .ToList();
        var nextOffset = result.NextOffset;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new ReportJobHistoryResult(
            HistoryDirectory: historyContext.HistoryDirectory,
            DatabasePath: historyContext.DatabasePath,
            JobKey: result.JobKey,
            StatusFilters: result.StatusFilter.Select(static value => value.ToString()).ToArray(),
            SinceUtc: result.SinceUtc?.UtcDateTime,
            DiscoveredCount: result.DiscoveredCount,
            MatchedCount: result.MatchedCount,
            ReturnedCount: result.ReturnedCount,
            Offset: result.Offset,
            PageSize: result.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: result.TruncatedByPage,
            Truncated: result.TruncatedByPage,
            Jobs: rows);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "jobs_view",
            title: "Monitoring report jobs",
            baseTruncated: result.TruncatedByPage,
            maxTop: Math.Max(Options.MaxHistoryRowsInCatalog, result.MatchedCount),
            scanned: result.DiscoveredCount,
            metaMutate: meta => {
                meta.Add("history_directory", historyContext.HistoryDirectory);
                meta.Add("database_path", historyContext.DatabasePath);
                meta.Add("matched_count", result.MatchedCount);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", result.Offset);
                if (result.PageSize.HasValue) {
                    meta.Add("page_size", result.PageSize.Value);
                }
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
                meta.Add("truncated_by_page", result.TruncatedByPage);
            });
    }

    private static bool TryParseStatusFilter(
        IReadOnlyList<string> values,
        out IReadOnlyCollection<MonitoringReportJobStatus>? statusFilter,
        out string? error) {
        statusFilter = null;
        error = null;

        if (values is not { Count: > 0 }) {
            return true;
        }

        var seen = new HashSet<MonitoringReportJobStatus>();
        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (!Enum.TryParse<MonitoringReportJobStatus>(value.Trim(), ignoreCase: true, out var parsed)) {
                error = $"statuses contains unsupported value '{value}'. Supported values: {string.Join(", ", StatusNames)}.";
                return false;
            }

            seen.Add(parsed);
        }

        statusFilter = seen.Count > 0 ? seen.ToArray() : null;
        return true;
    }

    private sealed record ReportJobHistoryResult(
        string HistoryDirectory,
        string DatabasePath,
        string JobKey,
        IReadOnlyList<string> StatusFilters,
        DateTime? SinceUtc,
        int DiscoveredCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int? PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<ReportJobHistoryRow> Jobs);

    private sealed record ReportJobHistoryRow(
        string JobId,
        string JobKey,
        string ReportKey,
        string Trigger,
        string ReportPath,
        string Status,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        double? DurationSeconds,
        string Outcome,
        string ErrorText,
        int? HistoryEntries,
        int? HistoryRootCount,
        int? HistoryProbeCount,
        long? HistorySampleCount,
        double? HistoryLoadSeconds,
        string HistoryCacheMode,
        string HistoryIndexWarning,
        double? ReportBuildSeconds,
        double? ReportRenderSeconds,
        double? ReportWriteSeconds,
        long? ReportBytes,
        string ReportHash,
        DateTimeOffset? SourceUpdatedUtc);
}
