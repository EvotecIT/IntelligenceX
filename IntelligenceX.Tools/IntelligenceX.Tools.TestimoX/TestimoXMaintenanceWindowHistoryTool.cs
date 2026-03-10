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
            "reporting",
            "fallback_hint_keys:history_directory,start_utc,end_utc,definition_key,name_contains,reason_contains,probe_name_pattern_contains,target_pattern_contains"
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
                error: "IX.TestimoX Monitoring pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX Monitoring pack in host/service options before calling testimox_maintenance_window_history." },
                isTransient: false);
        }

        if (!TestimoXMonitoringHistoryHelper.TryResolveHistoryDatabasePath(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_maintenance_window_history",
                out var historyDirectory,
                out var databasePath,
                out var resolveError)) {
            return resolveError;
        }

        IReadOnlyList<MaintenanceWindowHistoryEntry> discovered;
        try {
            using var store = new MonitoringMaintenanceWindowHistoryStore(
                TestimoXMonitoringHistoryHelper.CreateSqliteDatabaseConfig(databasePath),
                TestimoXMonitoringHistoryHelper.CreateSqliteOptions(),
                historyDirectory);
            discovered = await store.ReadEntriesAsync(
                    new DateTimeOffset(context.Request.StartUtc, TimeSpan.Zero),
                    new DateTimeOffset(context.Request.EndUtc, TimeSpan.Zero),
                    cancellationToken)
                .ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring maintenance window history query failed.");
        }

        IEnumerable<MaintenanceWindowHistoryEntry> filtered = discovered;
        if (!string.IsNullOrWhiteSpace(context.Request.DefinitionKey)) {
            filtered = filtered.Where(entry => string.Equals(
                entry.DefinitionKey,
                context.Request.DefinitionKey,
                StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(context.Request.NameContains)) {
            filtered = filtered.Where(entry => ContainsIgnoreCase(entry.Window.Name, context.Request.NameContains));
        }
        if (!string.IsNullOrWhiteSpace(context.Request.ReasonContains)) {
            filtered = filtered.Where(entry => ContainsIgnoreCase(entry.Window.Reason, context.Request.ReasonContains));
        }
        if (!string.IsNullOrWhiteSpace(context.Request.ProbeNamePatternContains)) {
            filtered = filtered.Where(entry => ContainsIgnoreCase(entry.Window.ProbeNamePattern, context.Request.ProbeNamePatternContains));
        }
        if (!string.IsNullOrWhiteSpace(context.Request.TargetPatternContains)) {
            filtered = filtered.Where(entry => ContainsIgnoreCase(entry.Window.TargetPattern, context.Request.TargetPatternContains)
                || entry.Window.TargetPatterns.Any(pattern => ContainsIgnoreCase(pattern, context.Request.TargetPatternContains)));
        }

        var matchedRows = filtered
            .OrderByDescending(static entry => entry.StartUtc)
            .ThenBy(static entry => entry.DefinitionKey, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => new MaintenanceWindowHistoryRow(
                DefinitionKey: entry.DefinitionKey,
                Name: entry.Window.Name ?? string.Empty,
                Reason: entry.Window.Reason ?? string.Empty,
                StartUtc: entry.StartUtc,
                EndUtc: entry.EndUtc,
                LastSeenUtc: entry.LastSeenUtc,
                DurationMinutes: Math.Round((entry.EndUtc - entry.StartUtc).TotalMinutes, 2),
                ProbeType: entry.Window.ProbeType?.ToString() ?? string.Empty,
                ProbeTypeKnown: entry.Window.ProbeType.HasValue
                    && ProbeTypeNames.Contains(entry.Window.ProbeType.Value.ToString(), StringComparer.OrdinalIgnoreCase),
                ProbeNamePattern: entry.Window.ProbeNamePattern ?? string.Empty,
                ZonePattern: entry.Window.ZonePattern ?? string.Empty,
                AgentPattern: entry.Window.AgentPattern ?? string.Empty,
                TargetPattern: entry.Window.TargetPattern ?? string.Empty,
                TargetPatterns: entry.Window.TargetPatterns.ToArray(),
                ProtocolPattern: entry.Window.ProtocolPattern ?? string.Empty,
                ErrorPattern: entry.Window.ErrorPattern ?? string.Empty,
                MetadataFilterCount: entry.Window.MetadataFilters.Count,
                DaysOfWeek: entry.Window.DaysOfWeek?.Select(static day => day.ToString()).ToArray() ?? Array.Empty<string>(),
                StartTimeUtc: entry.Window.StartTimeUtc?.ToString(),
                EndTimeUtc: entry.Window.EndTimeUtc?.ToString(),
                Cron: entry.Window.Cron ?? string.Empty,
                Duration: entry.Window.Duration?.ToString() ?? string.Empty,
                SuppressNotifications: entry.Window.SuppressNotifications,
                SuppressSummaries: entry.Window.SuppressSummaries,
                SuppressReporting: entry.Window.SuppressReporting,
                PauseProbes: entry.Window.PauseProbes))
            .ToList();

        var offset = context.Request.Offset > matchedRows.Count ? matchedRows.Count : context.Request.Offset;
        var pageRows = matchedRows.Skip(offset);
        var rows = context.Request.PageSize.HasValue
            ? pageRows.Take(context.Request.PageSize.Value).ToList()
            : pageRows.ToList();
        var truncatedByPage = context.Request.PageSize.HasValue && offset + rows.Count < matchedRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new MaintenanceWindowHistoryResult(
            HistoryDirectory: historyDirectory,
            DatabasePath: databasePath,
            StartUtc: context.Request.StartUtc,
            EndUtc: context.Request.EndUtc,
            DefinitionKey: context.Request.DefinitionKey ?? string.Empty,
            NameContains: context.Request.NameContains ?? string.Empty,
            ReasonContains: context.Request.ReasonContains ?? string.Empty,
            ProbeNamePatternContains: context.Request.ProbeNamePatternContains ?? string.Empty,
            TargetPatternContains: context.Request.TargetPatternContains ?? string.Empty,
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
            title: "Monitoring maintenance window history",
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

    private static bool ContainsIgnoreCase(string? value, string? candidate) {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(candidate)) {
            return false;
        }

        return value.Contains(candidate, StringComparison.OrdinalIgnoreCase);
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
