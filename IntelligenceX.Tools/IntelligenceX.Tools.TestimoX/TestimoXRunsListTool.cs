using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Lists stored TestimoX execution runs from an allowed result store directory.
/// </summary>
public sealed class TestimoXRunsListTool : TestimoXToolBase, ITool {
    private const int DefaultPageSize = 25;

    private sealed record RunsListRequest(
        string StoreDirectory,
        string? RunIdContains,
        bool CompletedOnly,
        bool NewestFirst,
        int PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_runs_list",
        "List stored TestimoX execution runs from an allowed result store directory.",
        ToolSchema.Object(
                ("store_directory", ToolSchema.String("Result store directory to inspect (must be inside AllowedStoreRoots).")),
                ("run_id_contains", ToolSchema.String("Optional case-insensitive substring filter against run_id.")),
                ("completed_only", ToolSchema.Boolean("When true, keep only runs with an Ended timestamp. Default false.")),
                ("newest_first", ToolSchema.Boolean("When true, sort newest runs first. Default true.")),
                ("page_size", ToolSchema.Integer("Optional number of runs to return in this page. Default 25.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched runs (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "catalog",
            "history",
            "store"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXRunsListTool"/> class.
    /// </summary>
    public TestimoXRunsListTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<RunsListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var storeDirectory = reader.OptionalString("store_directory");
            if (string.IsNullOrWhiteSpace(storeDirectory)) {
                return ToolRequestBindingResult<RunsListRequest>.Failure("store_directory is required.");
            }

            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog) ?? Math.Min(DefaultPageSize, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<RunsListRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<RunsListRequest>.Success(new RunsListRequest(
                StoreDirectory: storeDirectory,
                RunIdContains: reader.OptionalString("run_id_contains"),
                CompletedOnly: reader.Boolean("completed_only", defaultValue: false),
                NewestFirst: reader.Boolean("newest_first", defaultValue: true),
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<RunsListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_runs_list." },
                isTransient: false));
        }

        if (!TestimoXStoreCatalogHelper.TryResolveStoreDirectory(
                options: Options,
                inputPath: context.Request.StoreDirectory,
                toolName: "testimox_runs_list",
                fullPath: out var storeDirectory,
                errorResponse: out var errorResponse)) {
            return Task.FromResult(errorResponse);
        }

        IEnumerable<TestimoXStoreCatalogHelper.StoredRunInfo> filtered = TestimoXStoreCatalogHelper.ListRuns(storeDirectory);
        if (!string.IsNullOrWhiteSpace(context.Request.RunIdContains)) {
            filtered = filtered.Where(run =>
                run.RunId.Contains(context.Request.RunIdContains.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (context.Request.CompletedOnly) {
            filtered = filtered.Where(static run => run.EndedUtc.HasValue);
        }

        filtered = context.Request.NewestFirst
            ? filtered
                .OrderByDescending(static run => run.StartedUtc)
                .ThenByDescending(static run => run.RunId, StringComparer.OrdinalIgnoreCase)
            : filtered
                .OrderBy(static run => run.StartedUtc)
                .ThenBy(static run => run.RunId, StringComparer.OrdinalIgnoreCase);

        var catalogRows = filtered
            .Select(static run => new TestimoXRunCatalogRow(
                RunId: run.RunId,
                StartedUtc: run.StartedUtc,
                EndedUtc: run.EndedUtc,
                DurationSeconds: run.DurationSeconds,
                StoredResultCount: run.StoredResultCount,
                PlannedTasks: run.PlannedTasks,
                CompletedTasks: run.CompletedTasks,
                EligibleForestFamilies: run.EligibleForestFamilies,
                EligibleDomainFamilies: run.EligibleDomainFamilies,
                EligibleDcFamilies: run.EligibleDcFamilies,
                Policy: run.Policy,
                MatchMode: run.MatchMode,
                RawMode: run.RawMode,
                ToolVersion: run.ToolVersion))
            .ToList();

        var offset = Math.Min(context.Request.Offset, catalogRows.Count);
        var rows = catalogRows
            .Skip(offset)
            .Take(context.Request.PageSize)
            .ToList();
        var truncatedByPage = offset + rows.Count < catalogRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new TestimoXRunCatalogResult(
            StoreDirectory: storeDirectory,
            RunIdContains: context.Request.RunIdContains ?? string.Empty,
            CompletedOnly: context.Request.CompletedOnly,
            NewestFirst: context.Request.NewestFirst,
            MatchedCount: catalogRows.Count,
            ReturnedCount: rows.Count,
            Offset: offset,
            PageSize: context.Request.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: truncatedByPage,
            Truncated: truncatedByPage,
            Runs: rows);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "runs_view",
            title: "TestimoX stored runs",
            maxTop: Math.Max(context.Request.PageSize, rows.Count),
            baseTruncated: truncatedByPage,
            scanned: catalogRows.Count,
            metaMutate: meta => {
                meta.Add("store_directory", storeDirectory);
                meta.Add("matched_count", catalogRows.Count);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                meta.Add("page_size", context.Request.PageSize);
                meta.Add("completed_only", context.Request.CompletedOnly);
                meta.Add("newest_first", context.Request.NewestFirst);
                if (!string.IsNullOrWhiteSpace(context.Request.RunIdContains)) {
                    meta.Add("run_id_contains", context.Request.RunIdContains);
                }
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
                meta.Add("truncated_by_page", truncatedByPage);
            }));
    }

    private sealed record TestimoXRunCatalogResult(
        string StoreDirectory,
        string RunIdContains,
        bool CompletedOnly,
        bool NewestFirst,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<TestimoXRunCatalogRow> Runs);

    private sealed record TestimoXRunCatalogRow(
        string RunId,
        DateTimeOffset StartedUtc,
        DateTimeOffset? EndedUtc,
        double? DurationSeconds,
        int StoredResultCount,
        int? PlannedTasks,
        int? CompletedTasks,
        int? EligibleForestFamilies,
        int? EligibleDomainFamilies,
        int? EligibleDcFamilies,
        string Policy,
        string MatchMode,
        string RawMode,
        string ToolVersion);
}
