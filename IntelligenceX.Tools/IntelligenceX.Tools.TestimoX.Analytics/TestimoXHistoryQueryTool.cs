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
/// Queries monitoring availability rollups from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXHistoryQueryTool : TestimoXToolBase, ITool {
    private static readonly string[] BucketKindNames = Enum.GetNames(typeof(MonitoringAvailabilityRollupBucketKind))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed record HistoryQueryRequest(
        string HistoryDirectory,
        MonitoringAvailabilityRollupBucketKind BucketKind,
        DateTime StartUtc,
        DateTime EndUtc,
        IReadOnlyList<string> RootProbeNames,
        IReadOnlyList<string> ExcludedProbeNamePrefixes,
        string? ProbeNameContains,
        int? PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_history_query",
        "Query monitoring availability rollups from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots and contain monitoring.sqlite).")),
                ("bucket_kind", ToolSchema.String("Rollup bucket granularity. Default Hour.").Enum(BucketKindNames)),
                ("start_utc", ToolSchema.String("Optional ISO-8601 UTC lower bound for bucket_utc. Defaults to a recent lookback when omitted.")),
                ("end_utc", ToolSchema.String("Optional ISO-8601 UTC upper bound for bucket_utc. Defaults to now (UTC).")),
                ("root_probe_names", ToolSchema.Array(ToolSchema.String("Root probe or probe name to include."), "Optional root probe names to keep.")),
                ("exclude_probe_name_prefixes", ToolSchema.Array(ToolSchema.String("Probe-name prefix to exclude."), "Optional probe-name prefixes to exclude.")),
                ("probe_name_contains", ToolSchema.String("Optional case-insensitive probe-name substring filter applied after reading rollups.")),
                ("page_size", ToolSchema.Integer("Optional number of rollup rows to return in this page.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched rows (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "availability",
            "history",
            "monitoring",
            "rollup"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXHistoryQueryTool"/> class.
    /// </summary>
    public TestimoXHistoryQueryTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<HistoryQueryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<HistoryQueryRequest>.Failure("history_directory is required.");
            }

            var bucketKind = MonitoringAvailabilityRollupBucketKind.Hour;
            var rawBucketKind = reader.OptionalString("bucket_kind");
            if (!string.IsNullOrWhiteSpace(rawBucketKind)
                && !Enum.TryParse(rawBucketKind, ignoreCase: true, out bucketKind)) {
                return ToolRequestBindingResult<HistoryQueryRequest>.Failure(
                    $"bucket_kind contains unsupported value '{rawBucketKind}'. Supported values: {string.Join(", ", BucketKindNames)}.");
            }

            if (!ToolTime.TryParseUtcRange(arguments, "start_utc", "end_utc", out var startUtc, out var endUtc, out var rangeError)) {
                return ToolRequestBindingResult<HistoryQueryRequest>.Failure(rangeError ?? "Invalid UTC range.");
            }

            var effectiveEndUtc = endUtc ?? DateTime.UtcNow;
            var defaultLookback = bucketKind == MonitoringAvailabilityRollupBucketKind.Day
                ? TimeSpan.FromDays(14)
                : TimeSpan.FromDays(2);
            var effectiveStartUtc = startUtc ?? effectiveEndUtc.Subtract(defaultLookback);
            if (effectiveStartUtc > effectiveEndUtc) {
                return ToolRequestBindingResult<HistoryQueryRequest>.Failure("start_utc must be <= end_utc.");
            }

            var rootProbeNames = reader.DistinctStringArray("root_probe_names");
            var excludedPrefixes = reader.DistinctStringArray("exclude_probe_name_prefixes");
            var probeNameContains = reader.OptionalString("probe_name_contains");
            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxHistoryRowsInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<HistoryQueryRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<HistoryQueryRequest>.Success(new HistoryQueryRequest(
                HistoryDirectory: historyDirectory,
                BucketKind: bucketKind,
                StartUtc: DateTime.SpecifyKind(effectiveStartUtc, DateTimeKind.Utc),
                EndUtc: DateTime.SpecifyKind(effectiveEndUtc, DateTimeKind.Utc),
                RootProbeNames: rootProbeNames,
                ExcludedProbeNamePrefixes: excludedPrefixes,
                ProbeNameContains: probeNameContains,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<HistoryQueryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_history_query." },
                isTransient: false);
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryReadContext(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_history_query",
                out var historyContext,
                out var resolveError)) {
            return resolveError;
        }

        MonitoringAvailabilityRollupQueryResult result;
        try {
            var service = new MonitoringAvailabilityRollupQueryService(
                historyContext.DatabaseConfig,
                historyContext.SqliteOptions,
                historyContext.HistoryDirectory);
            result = await service.QueryAsync(
                    new MonitoringAvailabilityRollupQueryRequest(
                        BucketKind: context.Request.BucketKind,
                        StartUtc: new DateTimeOffset(context.Request.StartUtc, TimeSpan.Zero),
                        EndUtc: new DateTimeOffset(context.Request.EndUtc, TimeSpan.Zero),
                        RootProbeNames: context.Request.RootProbeNames,
                        ExcludedProbeNamePrefixes: context.Request.ExcludedProbeNamePrefixes,
                        ProbeNameContains: context.Request.ProbeNameContains,
                        PageSize: context.Request.PageSize,
                        Offset: context.Request.Offset),
                    cancellationToken)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring history query failed.");
        }

        var rows = result.Rows
            .Select(static row => new HistoryRollupRow(
                BucketKind: row.BucketKind.ToString(),
                BucketUtc: row.BucketUtc,
                ProbeName: row.ProbeName,
                RootProbe: row.RootProbe,
                ProbeType: row.ProbeType.ToString(),
                Agent: row.Agent,
                Zone: row.Zone,
                Target: row.Target,
                Protocol: row.Protocol,
                DirectoryKind: row.DirectoryKind,
                DirectoryScope: row.DirectoryScope,
                ScopeRootDisabled: row.ScopeRootDisabled,
                UpCount: row.UpCount,
                DownCount: row.DownCount,
                DegradedCount: row.DegradedCount,
                RecoveringCount: row.RecoveringCount,
                UnknownCount: row.UnknownCount,
                MaintenanceCount: row.MaintenanceCount,
                TotalCount: row.TotalCount,
                UpRatioPercent: row.UpRatioPercent,
                ProblemRatioPercent: row.ProblemRatioPercent))
            .ToList();
        var nextOffset = result.NextOffset;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new HistoryQueryResult(
            HistoryDirectory: historyContext.HistoryDirectory,
            DatabasePath: historyContext.DatabasePath,
            BucketKind: result.BucketKind.ToString(),
            StartUtc: result.StartUtc.UtcDateTime,
            EndUtc: result.EndUtc.UtcDateTime,
            RootProbeNames: result.RootProbeNames,
            ExcludedProbeNamePrefixes: result.ExcludedProbeNamePrefixes,
            ProbeNameContains: result.ProbeNameContains,
            DiscoveredCount: result.DiscoveredCount,
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
            title: "Monitoring availability rollups",
            baseTruncated: result.TruncatedByPage,
            maxTop: Math.Max(Options.MaxHistoryRowsInCatalog, result.MatchedCount),
            scanned: result.DiscoveredCount,
            metaMutate: meta => {
                meta.Add("history_directory", historyContext.HistoryDirectory);
                meta.Add("database_path", historyContext.DatabasePath);
                meta.Add("bucket_kind", result.BucketKind.ToString());
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

    private sealed record HistoryQueryResult(
        string HistoryDirectory,
        string DatabasePath,
        string BucketKind,
        DateTime StartUtc,
        DateTime EndUtc,
        IReadOnlyList<string> RootProbeNames,
        IReadOnlyList<string> ExcludedProbeNamePrefixes,
        string ProbeNameContains,
        int DiscoveredCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int? PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<HistoryRollupRow> Rows);

    private sealed record HistoryRollupRow(
        string BucketKind,
        DateTimeOffset BucketUtc,
        string ProbeName,
        string RootProbe,
        string ProbeType,
        string Agent,
        string Zone,
        string Target,
        string Protocol,
        string DirectoryKind,
        string DirectoryScope,
        bool? ScopeRootDisabled,
        int UpCount,
        int DownCount,
        int DegradedCount,
        int RecoveringCount,
        int UnknownCount,
        int MaintenanceCount,
        int TotalCount,
        double? UpRatioPercent,
        double? ProblemRatioPercent);
}
