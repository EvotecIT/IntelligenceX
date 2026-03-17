using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Reporting;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Queries resolved monitoring maintenance window history from an allowed monitoring history directory.
/// </summary>
public sealed class TestimoXMaintenanceWindowHistoryTool : TestimoXToolBase, ITool {
    private static readonly string[] ProbeTypeNames = Enum.GetNames(typeof(ProbeType))
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private sealed record MaintenanceWindowHistoryRequest(
        string HistoryDirectory,
        DateTime StartUtc,
        DateTime EndUtc,
        string? DefinitionKey,
        string? NameContains,
        string? ReasonContains,
        string? ProbeNamePatternContains,
        string? TargetPatternContains,
        int? PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_maintenance_window_history",
        "Query resolved monitoring maintenance window history from an allowed monitoring history directory.",
        ToolSchema.Object(
                ("history_directory", ToolSchema.String("Monitoring history directory to inspect (must be inside AllowedHistoryRoots and contain monitoring.sqlite).")),
                ("start_utc", ToolSchema.String("Optional ISO-8601 UTC lower bound for overlapping maintenance windows. Defaults to a recent lookback when omitted.")),
                ("end_utc", ToolSchema.String("Optional ISO-8601 UTC upper bound for overlapping maintenance windows. Defaults to now (UTC).")),
                ("definition_key", ToolSchema.String("Optional exact maintenance definition key filter.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive maintenance window name substring filter.")),
                ("reason_contains", ToolSchema.String("Optional case-insensitive maintenance reason substring filter.")),
                ("probe_name_pattern_contains", ToolSchema.String("Optional case-insensitive substring filter applied to probe_name_pattern.")),
                ("target_pattern_contains", ToolSchema.String("Optional case-insensitive substring filter applied to target_pattern and target_patterns.")),
                ("page_size", ToolSchema.Integer("Optional number of maintenance rows to return in this page.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched rows (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "history",
            "maintenance",
            "monitoring",
            "reporting"
        });

    private static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(14);

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXMaintenanceWindowHistoryTool"/> class.
    /// </summary>
    public TestimoXMaintenanceWindowHistoryTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<MaintenanceWindowHistoryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var historyDirectory = reader.OptionalString("history_directory");
            if (string.IsNullOrWhiteSpace(historyDirectory)) {
                return ToolRequestBindingResult<MaintenanceWindowHistoryRequest>.Failure("history_directory is required.");
            }

            if (!ToolTime.TryParseUtcRange(arguments, "start_utc", "end_utc", out var startUtc, out var endUtc, out var rangeError)) {
                return ToolRequestBindingResult<MaintenanceWindowHistoryRequest>.Failure(rangeError ?? "Invalid UTC range.");
            }

            var effectiveEndUtc = endUtc ?? DateTime.UtcNow;
            var effectiveStartUtc = startUtc ?? effectiveEndUtc.Subtract(DefaultLookback);
            if (effectiveStartUtc > effectiveEndUtc) {
                return ToolRequestBindingResult<MaintenanceWindowHistoryRequest>.Failure("start_utc must be <= end_utc.");
            }

            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxHistoryRowsInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<MaintenanceWindowHistoryRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<MaintenanceWindowHistoryRequest>.Success(new MaintenanceWindowHistoryRequest(
                HistoryDirectory: historyDirectory,
                StartUtc: DateTime.SpecifyKind(effectiveStartUtc, DateTimeKind.Utc),
                EndUtc: DateTime.SpecifyKind(effectiveEndUtc, DateTimeKind.Utc),
                DefinitionKey: reader.OptionalString("definition_key"),
                NameContains: reader.OptionalString("name_contains"),
                ReasonContains: reader.OptionalString("reason_contains"),
                ProbeNamePatternContains: reader.OptionalString("probe_name_pattern_contains"),
                TargetPatternContains: reader.OptionalString("target_pattern_contains"),
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<MaintenanceWindowHistoryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX Analytics pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Analytics pack in host/service options before calling testimox_maintenance_window_history." },
                isTransient: false);
        }

        if (!TestimoXAnalyticsHistoryHelper.TryResolveHistoryReadContext(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_maintenance_window_history",
                out var historyContext,
                out var resolveError)) {
            return resolveError;
        }

        MonitoringMaintenanceWindowQueryResult result;
        try {
            var service = new MonitoringMaintenanceWindowQueryService(
                historyContext.DatabaseConfig,
                historyContext.SqliteOptions,
                historyContext.HistoryDirectory);
            result = await service.QueryAsync(
                    new MonitoringMaintenanceWindowQueryRequest(
                        StartUtc: new DateTimeOffset(context.Request.StartUtc, TimeSpan.Zero),
                        EndUtc: new DateTimeOffset(context.Request.EndUtc, TimeSpan.Zero),
                        DefinitionKey: context.Request.DefinitionKey,
                        NameContains: context.Request.NameContains,
                        ReasonContains: context.Request.ReasonContains,
                        ProbeNamePatternContains: context.Request.ProbeNamePatternContains,
                        TargetPatternContains: context.Request.TargetPatternContains,
                        PageSize: context.Request.PageSize,
                        Offset: context.Request.Offset),
                    cancellationToken)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring maintenance window history query failed.");
        }

        var rows = result.Rows
            .Select(static row => new MaintenanceWindowHistoryRow(
                DefinitionKey: row.DefinitionKey,
                Name: row.Name,
                Reason: row.Reason,
                StartUtc: row.StartUtc,
                EndUtc: row.EndUtc,
                LastSeenUtc: row.LastSeenUtc,
                DurationMinutes: row.DurationMinutes,
                ProbeType: row.ProbeType?.ToString() ?? string.Empty,
                ProbeTypeKnown: row.ProbeTypeKnown
                    && row.ProbeType.HasValue
                    && ProbeTypeNames.Contains(row.ProbeType.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                ProbeNamePattern: row.ProbeNamePattern,
                ZonePattern: row.ZonePattern,
                AgentPattern: row.AgentPattern,
                TargetPattern: row.TargetPattern,
                TargetPatterns: row.TargetPatterns.ToArray(),
                ProtocolPattern: row.ProtocolPattern,
                ErrorPattern: row.ErrorPattern,
                MetadataFilterCount: row.MetadataFilterCount,
                DaysOfWeek: row.DaysOfWeek.ToArray(),
                StartTimeUtc: row.StartTimeUtc,
                EndTimeUtc: row.EndTimeUtc,
                Cron: row.Cron,
                Duration: row.Duration,
                SuppressNotifications: row.SuppressNotifications,
                SuppressSummaries: row.SuppressSummaries,
                SuppressReporting: row.SuppressReporting,
                PauseProbes: row.PauseProbes))
            .ToList();
        var nextOffset = result.NextOffset;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new MaintenanceWindowHistoryResult(
            HistoryDirectory: historyContext.HistoryDirectory,
            DatabasePath: historyContext.DatabasePath,
            StartUtc: result.StartUtc.UtcDateTime,
            EndUtc: result.EndUtc.UtcDateTime,
            DefinitionKey: result.DefinitionKey,
            NameContains: result.NameContains,
            ReasonContains: result.ReasonContains,
            ProbeNamePatternContains: result.ProbeNamePatternContains,
            TargetPatternContains: result.TargetPatternContains,
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
            title: "Monitoring maintenance window history",
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

    private sealed record MaintenanceWindowHistoryResult(
        string HistoryDirectory,
        string DatabasePath,
        DateTime StartUtc,
        DateTime EndUtc,
        string DefinitionKey,
        string NameContains,
        string ReasonContains,
        string ProbeNamePatternContains,
        string TargetPatternContains,
        int DiscoveredCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int? PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<MaintenanceWindowHistoryRow> Rows);

    private sealed record MaintenanceWindowHistoryRow(
        string DefinitionKey,
        string Name,
        string Reason,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        DateTimeOffset LastSeenUtc,
        double DurationMinutes,
        string ProbeType,
        bool ProbeTypeKnown,
        string ProbeNamePattern,
        string ZonePattern,
        string AgentPattern,
        string TargetPattern,
        IReadOnlyList<string> TargetPatterns,
        string ProtocolPattern,
        string ErrorPattern,
        int MetadataFilterCount,
        IReadOnlyList<string> DaysOfWeek,
        string? StartTimeUtc,
        string? EndTimeUtc,
        string Cron,
        string Duration,
        bool SuppressNotifications,
        bool SuppressSummaries,
        bool SuppressReporting,
        bool PauseProbes);
}
