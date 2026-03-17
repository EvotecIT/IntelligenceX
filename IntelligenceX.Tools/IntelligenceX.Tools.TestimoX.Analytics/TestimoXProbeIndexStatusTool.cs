using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.History;
using ADPlayground.Monitoring.Probes;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Reads the latest per-probe status index from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXProbeIndexStatusTool : TestimoXToolBase, ITool {
    private static readonly string[] ProbeStatusNames = Enum.GetNames(typeof(ProbeStatus))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed record ProbeIndexStatusRequest(
        string HistoryDirectory,
        IReadOnlyList<string> ProbeNames,
        DateTime? SinceUtc,
        string? ProbeNameContains,
        IReadOnlyCollection<ProbeStatus>? StatusFilter,
        int? PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_probe_index_status",
        "Read the latest per-probe status index from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots and contain monitoring.sqlite).")),
                ("probe_names", ToolSchema.Array(ToolSchema.String("Exact probe name to inspect."), "Optional explicit probe names to inspect. When omitted, names are discovered from the recent probe index.")),
                ("since_utc", ToolSchema.String("Optional ISO-8601 UTC lower bound used to discover recent probe names when probe_names is omitted.")),
                ("probe_name_contains", ToolSchema.String("Optional case-insensitive substring filter applied to discovered or requested probe names.")),
                ("statuses", ToolSchema.Array(ToolSchema.String("Probe status.").Enum(ProbeStatusNames), "Optional latest-status filters (any-match).")),
                ("page_size", ToolSchema.Integer("Optional number of probe status rows to return in this page.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched rows (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "history",
            "index",
            "monitoring",
            "status"
        });

    private static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(2);

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXProbeIndexStatusTool"/> class.
    /// </summary>
    public TestimoXProbeIndexStatusTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<ProbeIndexStatusRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<ProbeIndexStatusRequest>.Failure("history_directory is required.");
            }

            if (!ToolTime.TryParseUtcOptional(reader.OptionalString("since_utc"), out var sinceUtc, out var timeError)) {
                return ToolRequestBindingResult<ProbeIndexStatusRequest>.Failure($"since_utc: {timeError}");
            }

            var requestedStatuses = reader.DistinctStringArray("statuses");
            if (!TryParseStatusFilter(requestedStatuses, out var statusFilter, out var statusError)) {
                return ToolRequestBindingResult<ProbeIndexStatusRequest>.Failure(statusError ?? "Invalid statuses argument.");
            }

            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxHistoryRowsInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<ProbeIndexStatusRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<ProbeIndexStatusRequest>.Success(new ProbeIndexStatusRequest(
                HistoryDirectory: historyDirectory,
                ProbeNames: reader.DistinctStringArray("probe_names"),
                SinceUtc: sinceUtc,
                ProbeNameContains: reader.OptionalString("probe_name_contains"),
                StatusFilter: statusFilter,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<ProbeIndexStatusRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_probe_index_status." },
                isTransient: false);
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryReadContext(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_probe_index_status",
                out var historyContext,
                out var resolveError)) {
            return resolveError;
        }

        MonitoringProbeIndexQueryResult result;
        try {
            var service = new MonitoringProbeIndexQueryService(
                historyContext.DatabaseConfig,
                historyContext.SqliteOptions,
                historyContext.HistoryDirectory);
            result = await service.QueryAsync(
                    new MonitoringProbeIndexQueryRequest(
                        ProbeNames: context.Request.ProbeNames,
                        SinceUtc: context.Request.SinceUtc.HasValue
                            ? new DateTimeOffset(DateTime.SpecifyKind(context.Request.SinceUtc.Value, DateTimeKind.Utc))
                            : null,
                        ProbeNameContains: context.Request.ProbeNameContains,
                        StatusFilter: context.Request.StatusFilter,
                        PageSize: context.Request.PageSize,
                        Offset: context.Request.Offset,
                        DefaultLookback: DefaultLookback),
                    cancellationToken)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring probe index status query failed.");
        }

        var rows = result.Rows
            .Select(static row => new ProbeIndexStatusRow(
                ProbeName: row.ProbeName,
                Status: row.Status.ToString(),
                CompletedUtc: row.CompletedUtc,
                AgeMinutes: row.AgeMinutes))
            .ToList();
        var nextOffset = result.NextOffset;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new ProbeIndexStatusResult(
            HistoryDirectory: historyContext.HistoryDirectory,
            DatabasePath: historyContext.DatabasePath,
            DiscoveryMode: result.DiscoveryMode == MonitoringProbeIndexDiscoveryMode.ExplicitProbeNames ? "explicit_probe_names" : "recent_probe_index",
            ProbeNames: result.ProbeNames,
            SinceUtc: result.SinceUtc.UtcDateTime,
            ProbeNameContains: result.ProbeNameContains,
            StatusFilters: result.StatusFilter.Select(static value => value.ToString()).ToArray(),
            DiscoveredProbeCount: result.DiscoveredProbeCount,
            IndexedProbeCount: result.IndexedProbeCount,
            MatchedCount: result.MatchedCount,
            ReturnedCount: result.ReturnedCount,
            Offset: result.Offset,
            PageSize: result.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: result.TruncatedByPage,
            Truncated: result.TruncatedByPage,
            Rows: rows);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Monitoring probe index status",
            baseTruncated: result.TruncatedByPage,
            maxTop: Math.Max(Options.MaxHistoryRowsInCatalog, result.MatchedCount),
            scanned: result.IndexedProbeCount,
            metaMutate: meta => {
                meta.Add("history_directory", historyContext.HistoryDirectory);
                meta.Add("database_path", historyContext.DatabasePath);
                meta.Add("discovery_mode", result.DiscoveryMode == MonitoringProbeIndexDiscoveryMode.ExplicitProbeNames ? "explicit_probe_names" : "recent_probe_index");
                meta.Add("indexed_probe_count", result.IndexedProbeCount);
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
        out IReadOnlyCollection<ProbeStatus>? statusFilter,
        out string? error) {
        statusFilter = null;
        error = null;

        if (values is not { Count: > 0 }) {
            return true;
        }

        var seen = new HashSet<ProbeStatus>();
        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            if (!Enum.TryParse<ProbeStatus>(value.Trim(), ignoreCase: true, out var parsed)) {
                error = $"statuses contains unsupported value '{value}'. Supported values: {string.Join(", ", ProbeStatusNames)}.";
                return false;
            }

            seen.Add(parsed);
        }

        statusFilter = seen.Count > 0 ? seen.ToArray() : null;
        return true;
    }

    private sealed record ProbeIndexStatusResult(
        string HistoryDirectory,
        string DatabasePath,
        string DiscoveryMode,
        IReadOnlyList<string> ProbeNames,
        DateTime SinceUtc,
        string ProbeNameContains,
        IReadOnlyList<string> StatusFilters,
        int DiscoveredProbeCount,
        int IndexedProbeCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int? PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<ProbeIndexStatusRow> Rows);

    private sealed record ProbeIndexStatusRow(
        string ProbeName,
        string Status,
        DateTimeOffset CompletedUtc,
        double AgeMinutes);
}
