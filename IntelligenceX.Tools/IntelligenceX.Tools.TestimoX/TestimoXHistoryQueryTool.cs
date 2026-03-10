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
            "rollup",
            "fallback_hint_keys:history_directory,bucket_kind,start_utc,end_utc,root_probe_names,probe_name_contains"
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
                error: "IX.TestimoX Monitoring pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Monitoring pack in host/service options before calling testimox_history_query." },
                isTransient: false);
        }

        if (!TestimoXMonitoringHistoryHelper.TryResolveHistoryDatabasePath(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_history_query",
                out var historyDirectory,
                out var databasePath,
                out var resolveError)) {
            return resolveError;
        }

        IReadOnlyList<MonitoringAvailabilityRollupSample> discovered;
        try {
            using var store = new MonitoringAvailabilityRollupStore(
                TestimoXMonitoringHistoryHelper.CreateSqliteDatabaseConfig(databasePath),
                TestimoXMonitoringHistoryHelper.CreateSqliteOptions(),
                historyDirectory);
            discovered = context.Request.RootProbeNames.Count > 0 || context.Request.ExcludedProbeNamePrefixes.Count > 0
                ? await store.ReadFilteredAsync(
                        context.Request.BucketKind,
                        new DateTimeOffset(context.Request.StartUtc, TimeSpan.Zero),
                        new DateTimeOffset(context.Request.EndUtc, TimeSpan.Zero),
                        context.Request.RootProbeNames,
                        context.Request.ExcludedProbeNamePrefixes,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await store.ReadAsync(
                        context.Request.BucketKind,
                        new DateTimeOffset(context.Request.StartUtc, TimeSpan.Zero),
                        new DateTimeOffset(context.Request.EndUtc, TimeSpan.Zero),
                        cancellationToken)
                    .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring history query failed.");
        }

        IEnumerable<MonitoringAvailabilityRollupSample> filtered = discovered;
        if (!string.IsNullOrWhiteSpace(context.Request.ProbeNameContains)) {
            filtered = filtered.Where(sample =>
                sample.ProbeName.Contains(context.Request.ProbeNameContains, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(sample.RootProbe)
                    && sample.RootProbe.Contains(context.Request.ProbeNameContains, StringComparison.OrdinalIgnoreCase)));
        }

        var matchedRows = filtered
            .OrderByDescending(static sample => sample.BucketUtc)
            .ThenBy(static sample => sample.ProbeName, StringComparer.OrdinalIgnoreCase)
            .Select(static sample => new HistoryRollupRow(
                BucketKind: sample.BucketKind.ToString(),
                BucketUtc: sample.BucketUtc,
                ProbeName: sample.ProbeName,
                RootProbe: sample.RootProbe ?? string.Empty,
                ProbeType: sample.ProbeType.ToString(),
                Agent: sample.Agent ?? string.Empty,
                Zone: sample.Zone ?? string.Empty,
                Target: sample.Target ?? string.Empty,
                Protocol: sample.Protocol ?? string.Empty,
                DirectoryKind: sample.DirectoryKind ?? string.Empty,
                DirectoryScope: sample.DirectoryScope ?? string.Empty,
                ScopeRootDisabled: sample.ScopeRootDisabled,
                UpCount: sample.UpCount,
                DownCount: sample.DownCount,
                DegradedCount: sample.DegradedCount,
                RecoveringCount: sample.RecoveringCount,
                UnknownCount: sample.UnknownCount,
                MaintenanceCount: sample.MaintenanceCount,
                TotalCount: sample.TotalCount,
                UpRatioPercent: ComputePercent(sample.UpCount, sample.TotalCount),
                ProblemRatioPercent: ComputePercent(sample.DownCount + sample.DegradedCount + sample.UnknownCount, sample.TotalCount)))
            .ToList();

        var offset = context.Request.Offset > matchedRows.Count ? matchedRows.Count : context.Request.Offset;
        var pageRows = matchedRows.Skip(offset);
        var rows = context.Request.PageSize.HasValue
            ? pageRows.Take(context.Request.PageSize.Value).ToList()
            : pageRows.ToList();
        var truncatedByPage = context.Request.PageSize.HasValue && offset + rows.Count < matchedRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new HistoryQueryResult(
            HistoryDirectory: historyDirectory,
            DatabasePath: databasePath,
            BucketKind: context.Request.BucketKind.ToString(),
            StartUtc: context.Request.StartUtc,
            EndUtc: context.Request.EndUtc,
            RootProbeNames: context.Request.RootProbeNames,
            ExcludedProbeNamePrefixes: context.Request.ExcludedProbeNamePrefixes,
            ProbeNameContains: context.Request.ProbeNameContains ?? string.Empty,
            DiscoveredCount: discovered.Count,
            MatchedCount: matchedRows.Count,
            ReturnedCount: rows.Count,
            Offset: offset,
            PageSize: context.Request.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: truncatedByPage,
            Truncated: truncatedByPage,
            Rows: rows);

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Monitoring availability rollups",
            baseTruncated: truncatedByPage,
            maxTop: Math.Max(Options.MaxHistoryRowsInCatalog, matchedRows.Count),
            scanned: discovered.Count,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("database_path", databasePath);
                meta.Add("bucket_kind", context.Request.BucketKind.ToString());
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

    private static double? ComputePercent(int numerator, int denominator) {
        if (denominator <= 0) {
            return null;
        }

        return Math.Round((numerator / (double)denominator) * 100.0, 2);
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
