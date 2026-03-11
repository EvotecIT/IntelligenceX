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
        HashSet<string>? StatusFilter,
        int? PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_report_job_history",
        "List monitoring report generation jobs from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots and contain monitoring.sqlite).")),
                ("job_key", ToolSchema.String("Optional exact report job key filter.")),
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
            "reporting",
            "fallback:requires_selection",
            "fallback_selection_keys:history_directory",
            "fallback_hint_keys:history_directory,job_key,since_utc,statuses"
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
                JobKey: jobKey,
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

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryDatabasePath(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_report_job_history",
                out var historyDirectory,
                out var databasePath,
                out var resolveError)) {
            return resolveError;
        }

        IReadOnlyList<MonitoringReportJobSummary> discovered;
        try {
            using var store = new MonitoringReportJobStore(
                TestimoXAnalyticsHistoryHelper.CreateSqliteDatabaseConfig(databasePath),
                TestimoXAnalyticsHistoryHelper.CreateSqliteOptions(),
                historyDirectory);
            discovered = await store.QueryRecentAsync(
                    context.Request.JobKey,
                    Options.MaxHistoryRowsInCatalog,
                    context.Request.SinceUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(context.Request.SinceUtc.Value, DateTimeKind.Utc)) : null,
                    cancellationToken)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring report job history query failed.");
        }

        IEnumerable<MonitoringReportJobSummary> filtered = discovered;
        if (context.Request.StatusFilter is { Count: > 0 }) {
            filtered = filtered.Where(job => context.Request.StatusFilter.Contains(job.Status.ToString()));
        }

        var matchedRows = filtered
            .OrderByDescending(static job => job.StartedUtc)
            .ThenBy(static job => job.JobId, StringComparer.OrdinalIgnoreCase)
            .Select(static job => new ReportJobHistoryRow(
                JobId: job.JobId,
                JobKey: job.JobKey,
                Trigger: job.Trigger ?? string.Empty,
                ReportPath: job.ReportPath ?? string.Empty,
                Status: job.Status.ToString(),
                StartedUtc: job.StartedUtc,
                CompletedUtc: job.CompletedUtc,
                DurationSeconds: job.Duration?.TotalSeconds,
                Outcome: job.Outcome ?? string.Empty,
                ErrorText: job.ErrorText ?? string.Empty,
                HistoryEntries: job.Metrics?.HistoryEntries,
                HistoryRootCount: job.Metrics?.HistoryRootCount,
                HistoryProbeCount: job.Metrics?.HistoryProbeCount,
                HistorySampleCount: job.Metrics?.HistorySampleCount,
                HistoryLoadSeconds: job.Metrics?.HistoryLoadSeconds,
                HistoryCacheMode: job.Metrics?.HistoryCacheMode ?? string.Empty,
                HistoryIndexWarning: job.Metrics?.HistoryIndexWarning ?? string.Empty,
                ReportBuildSeconds: job.Metrics?.ReportBuildSeconds,
                ReportRenderSeconds: job.Metrics?.ReportRenderSeconds,
                ReportWriteSeconds: job.Metrics?.ReportWriteSeconds,
                ReportBytes: job.Metrics?.ReportBytes,
                ReportHash: job.Metrics?.ReportHash ?? string.Empty,
                SourceUpdatedUtc: job.Metrics?.SourceUpdatedUtc))
            .ToList();

        var offset = context.Request.Offset > matchedRows.Count ? matchedRows.Count : context.Request.Offset;
        var pageRows = matchedRows.Skip(offset);
        var rows = context.Request.PageSize.HasValue
            ? pageRows.Take(context.Request.PageSize.Value).ToList()
            : pageRows.ToList();
        var truncatedByPage = context.Request.PageSize.HasValue && offset + rows.Count < matchedRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new ReportJobHistoryResult(
            HistoryDirectory: historyDirectory,
            DatabasePath: databasePath,
            JobKey: context.Request.JobKey ?? string.Empty,
            StatusFilters: context.Request.StatusFilter is { Count: > 0 }
                ? context.Request.StatusFilter.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>(),
            SinceUtc: context.Request.SinceUtc,
            DiscoveredCount: discovered.Count,
            MatchedCount: matchedRows.Count,
            ReturnedCount: rows.Count,
            Offset: offset,
            PageSize: context.Request.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: truncatedByPage,
            Truncated: truncatedByPage,
            Jobs: rows);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "jobs_view",
            title: "Monitoring report jobs",
            baseTruncated: truncatedByPage,
            maxTop: Math.Max(Options.MaxHistoryRowsInCatalog, matchedRows.Count),
            scanned: discovered.Count,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("database_path", databasePath);
                meta.Add("matched_count", matchedRows.Count);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                if (context.Request.PageSize.HasValue) {
                    meta.Add("page_size", context.Request.PageSize.Value);
                }
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
                meta.Add("truncated_by_page", truncatedByPage);
            });
    }

    private static bool TryParseStatusFilter(
        IReadOnlyList<string> values,
        out HashSet<string>? statusFilter,
        out string? error) {
        statusFilter = null;
        error = null;

        if (values is not { Count: > 0 }) {
            return true;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (!Enum.TryParse<MonitoringReportJobStatus>(value.Trim(), ignoreCase: true, out var parsed)) {
                error = $"statuses contains unsupported value '{value}'. Supported values: {string.Join(", ", StatusNames)}.";
                return false;
            }

            seen.Add(parsed.ToString());
        }

        statusFilter = seen.Count > 0 ? seen : null;
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
