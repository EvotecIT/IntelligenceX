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
        HashSet<string>? StatusFilter,
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
            "status",
            "fallback_hint_keys:history_directory,probe_names,since_utc,probe_name_contains,statuses"
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
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_probe_index_status." },
                isTransient: false);
        }

        if (!TestimoXMonitoringHistoryHelper.TryResolveHistoryDatabasePath(
                Options,
                context.Request.HistoryDirectory,
                toolName: "testimox_probe_index_status",
                out var historyDirectory,
                out var databasePath,
                out var resolveError)) {
            return resolveError;
        }

        IReadOnlyCollection<string> candidateNames;
        Dictionary<string, ProbeIndexStatusEntry> statusByProbe;
        var effectiveSinceUtc = context.Request.SinceUtc ?? DateTime.UtcNow.Subtract(DefaultLookback);
        var usedExplicitProbeNames = context.Request.ProbeNames.Count > 0;
        try {
            using var store = new DbaClientXHistoryStore(
                TestimoXMonitoringHistoryHelper.CreateSqliteDatabaseConfig(databasePath),
                historyDirectory,
                sqliteOptions: TestimoXMonitoringHistoryHelper.CreateSqliteOptions());
            candidateNames = usedExplicitProbeNames
                ? context.Request.ProbeNames
                : await store.ListProbeNamesSinceAsync(new DateTimeOffset(DateTime.SpecifyKind(effectiveSinceUtc, DateTimeKind.Utc), TimeSpan.Zero), cancellationToken)
                    .ConfigureAwait(false);

            if (!usedExplicitProbeNames && candidateNames.Count == 0) {
                candidateNames = await store.ListProbeNamesAsync(cancellationToken).ConfigureAwait(false);
            }

            statusByProbe = await store.ReadProbeIndexStatusAsync(candidateNames, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return ErrorFromException(ex, "Monitoring probe index status query failed.");
        }

        IEnumerable<KeyValuePair<string, ProbeIndexStatusEntry>> filtered = statusByProbe;
        if (!string.IsNullOrWhiteSpace(context.Request.ProbeNameContains)) {
            filtered = filtered.Where(entry => entry.Key.Contains(context.Request.ProbeNameContains, StringComparison.OrdinalIgnoreCase));
        }
        if (context.Request.StatusFilter is { Count: > 0 }) {
            filtered = filtered.Where(entry => context.Request.StatusFilter.Contains(entry.Value.Status.ToString()));
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var matchedRows = filtered
            .OrderByDescending(static entry => entry.Value.CompletedUtc)
            .ThenBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ProbeIndexStatusRow(
                ProbeName: entry.Key,
                Status: entry.Value.Status.ToString(),
                CompletedUtc: entry.Value.CompletedUtc,
                AgeMinutes: Math.Round((nowUtc - entry.Value.CompletedUtc).TotalMinutes, 2)))
            .ToList();

        var offset = context.Request.Offset > matchedRows.Count ? matchedRows.Count : context.Request.Offset;
        var pageRows = matchedRows.Skip(offset);
        var rows = context.Request.PageSize.HasValue
            ? pageRows.Take(context.Request.PageSize.Value).ToList()
            : pageRows.ToList();
        var truncatedByPage = context.Request.PageSize.HasValue && offset + rows.Count < matchedRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new ProbeIndexStatusResult(
            HistoryDirectory: historyDirectory,
            DatabasePath: databasePath,
            DiscoveryMode: usedExplicitProbeNames ? "explicit_probe_names" : "recent_probe_index",
            ProbeNames: context.Request.ProbeNames,
            SinceUtc: DateTime.SpecifyKind(effectiveSinceUtc, DateTimeKind.Utc),
            ProbeNameContains: context.Request.ProbeNameContains ?? string.Empty,
            StatusFilters: context.Request.StatusFilter is { Count: > 0 }
                ? context.Request.StatusFilter.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>(),
            DiscoveredProbeCount: candidateNames.Count,
            IndexedProbeCount: statusByProbe.Count,
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
            title: "Monitoring probe index status",
            baseTruncated: truncatedByPage,
            maxTop: Math.Max(Options.MaxHistoryRowsInCatalog, matchedRows.Count),
            scanned: statusByProbe.Count,
            metaMutate: meta => {
                meta.Add("history_directory", historyDirectory);
                meta.Add("database_path", databasePath);
                meta.Add("discovery_mode", usedExplicitProbeNames ? "explicit_probe_names" : "recent_probe_index");
                meta.Add("indexed_probe_count", statusByProbe.Count);
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

            if (!Enum.TryParse<ProbeStatus>(value.Trim(), ignoreCase: true, out var parsed)) {
                error = $"statuses contains unsupported value '{value}'. Supported values: {string.Join(", ", ProbeStatusNames)}.";
                return false;
            }

            seen.Add(parsed.ToString());
        }

        statusFilter = seen.Count > 0 ? seen : null;
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
