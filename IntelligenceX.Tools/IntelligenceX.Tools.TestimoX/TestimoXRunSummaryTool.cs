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
/// Summarizes a stored TestimoX execution run from an allowed result store directory.
/// </summary>
public sealed class TestimoXRunSummaryTool : TestimoXToolBase, ITool {
    private const int DefaultPageSize = 25;

    private sealed record RunSummaryRequest(
        string StoreDirectory,
        string RunId,
        string ScopeGroup,
        string? ScopeIdContains,
        string? RuleNameContains,
        int PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_run_summary",
        "Summarize a stored TestimoX execution run and preview stored rule outcomes.",
        ToolSchema.Object(
                ("store_directory", ToolSchema.String("Result store directory to inspect (must be inside AllowedStoreRoots).")),
                ("run_id", ToolSchema.String("Stored run identifier to summarize.")),
                ("scope_group", ToolSchema.String("Optional scope group filter. Default any.").Enum("any", "forest", "domain", "dc")),
                ("scope_id_contains", ToolSchema.String("Optional case-insensitive substring filter against stored scope_id.")),
                ("rule_name_contains", ToolSchema.String("Optional case-insensitive substring filter against stored rule_name.")),
                ("page_size", ToolSchema.Integer("Optional number of stored rows to return in this page. Default 25.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched rows (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "history",
            "store",
            "summary"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXRunSummaryTool"/> class.
    /// </summary>
    public TestimoXRunSummaryTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<RunSummaryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var storeDirectory = reader.OptionalString("store_directory");
            if (string.IsNullOrWhiteSpace(storeDirectory)) {
                return ToolRequestBindingResult<RunSummaryRequest>.Failure("store_directory is required.");
            }

            var runId = reader.OptionalString("run_id");
            if (string.IsNullOrWhiteSpace(runId)) {
                return ToolRequestBindingResult<RunSummaryRequest>.Failure("run_id is required.");
            }

            var scopeGroup = reader.OptionalString("scope_group");
            if (string.IsNullOrWhiteSpace(scopeGroup)) {
                scopeGroup = "any";
            } else if (!new[] { "any", "forest", "domain", "dc" }.Contains(scopeGroup, StringComparer.OrdinalIgnoreCase)) {
                return ToolRequestBindingResult<RunSummaryRequest>.Failure("scope_group must be one of: any, forest, domain, dc.");
            }

            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog) ?? Math.Min(DefaultPageSize, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<RunSummaryRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<RunSummaryRequest>.Success(new RunSummaryRequest(
                StoreDirectory: storeDirectory,
                RunId: runId,
                ScopeGroup: scopeGroup,
                ScopeIdContains: reader.OptionalString("scope_id_contains"),
                RuleNameContains: reader.OptionalString("rule_name_contains"),
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<RunSummaryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_run_summary." },
                isTransient: false));
        }

        if (!TestimoXStoreCatalogHelper.TryResolveStoreDirectory(
                options: Options,
                inputPath: context.Request.StoreDirectory,
                toolName: "testimox_run_summary",
                fullPath: out var storeDirectory,
                errorResponse: out var errorResponse)) {
            return Task.FromResult(errorResponse);
        }

        if (!TestimoXStoreCatalogHelper.TryLoadRun(storeDirectory, context.Request.RunId, out var runData, out var loadError)) {
            return Task.FromResult(loadError ?? ToolResultV2.Error("query_failed", "Unable to load the requested TestimoX stored run."));
        }

        var allRows = runData!.Rows;
        IEnumerable<TestimoXStoreCatalogHelper.StoredRunRow> filtered = allRows;
        if (!string.Equals(context.Request.ScopeGroup, "any", StringComparison.OrdinalIgnoreCase)) {
            filtered = filtered.Where(row => string.Equals(row.ScopeGroup, context.Request.ScopeGroup, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(context.Request.ScopeIdContains)) {
            filtered = filtered.Where(row => row.ScopeId.Contains(context.Request.ScopeIdContains.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(context.Request.RuleNameContains)) {
            filtered = filtered.Where(row => row.RuleName.Contains(context.Request.RuleNameContains.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var filteredRows = filtered
            .OrderByDescending(static row => row.CompletedUtc)
            .ThenBy(static row => row.ScopeGroup, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ScopeId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.RuleName, StringComparer.OrdinalIgnoreCase)
            .Select(static row => new TestimoXStoredRunRow(
                ScopeGroup: row.ScopeGroup,
                ScopeId: row.ScopeId,
                Domain: row.Domain,
                DomainController: row.DomainController,
                RuleName: row.RuleName,
                OverallStatus: row.OverallStatus,
                CompletedUtc: row.CompletedUtc,
                TestsSecurityCount: row.TestsSecurityCount,
                TestsHealthCount: row.TestsHealthCount,
                PenaltySecurity: row.PenaltySecurity,
                PenaltyHealth: row.PenaltyHealth,
                PenaltyTotal: row.PenaltyTotal))
            .ToList();

        var totalScores = TestimoXStoreCatalogHelper.ComputeScores(allRows);
        var filteredScoreSource = filteredRows.Select(row => new TestimoXStoreCatalogHelper.StoredRunRow(
            ScopeGroup: row.ScopeGroup,
            ScopeId: row.ScopeId,
            Domain: row.Domain,
            DomainController: row.DomainController,
            RuleName: row.RuleName,
            OverallStatus: row.OverallStatus,
            CompletedUtc: row.CompletedUtc,
            TestsSecurityCount: row.TestsSecurityCount,
            TestsHealthCount: row.TestsHealthCount,
            PenaltySecurity: row.PenaltySecurity,
            PenaltyHealth: row.PenaltyHealth,
            PenaltyTotal: row.PenaltyTotal));
        var filteredScores = TestimoXStoreCatalogHelper.ComputeScores(filteredScoreSource);

        var offset = Math.Min(context.Request.Offset, filteredRows.Count);
        var rows = filteredRows
            .Skip(offset)
            .Take(context.Request.PageSize)
            .ToList();
        var truncatedByPage = offset + rows.Count < filteredRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var distinctDomains = allRows
            .Select(static row => row.Domain)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var distinctDomainControllers = allRows
            .Select(static row => row.DomainController)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var distinctRules = allRows
            .Select(static row => row.RuleName)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var model = new TestimoXStoredRunSummaryResult(
            StoreDirectory: storeDirectory,
            RunId: runData.Run.RunId,
            StartedUtc: runData.Run.StartedUtc,
            EndedUtc: runData.Run.EndedUtc,
            DurationSeconds: runData.Run.DurationSeconds,
            Policy: runData.Run.Policy,
            StoredResultCount: runData.Run.StoredResultCount,
            DistinctRuleCount: distinctRules,
            DistinctDomainCount: distinctDomains,
            DistinctDomainControllerCount: distinctDomainControllers,
            ScopeGroup: context.Request.ScopeGroup,
            ScopeIdContains: context.Request.ScopeIdContains ?? string.Empty,
            RuleNameContains: context.Request.RuleNameContains ?? string.Empty,
            TotalSecurityScore: totalScores.SecurityScore,
            TotalHealthScore: totalScores.HealthScore,
            TotalOverallScore: totalScores.OverallScore,
            TotalSecurityPenalty: totalScores.SecurityPenaltyTotal,
            TotalHealthPenalty: totalScores.HealthPenaltyTotal,
            TotalPenalty: totalScores.TotalPenalty,
            FilteredSecurityScore: filteredScores.SecurityScore,
            FilteredHealthScore: filteredScores.HealthScore,
            FilteredOverallScore: filteredScores.OverallScore,
            FilteredSecurityPenalty: filteredScores.SecurityPenaltyTotal,
            FilteredHealthPenalty: filteredScores.HealthPenaltyTotal,
            FilteredPenalty: filteredScores.TotalPenalty,
            MatchedCount: filteredRows.Count,
            ReturnedCount: rows.Count,
            Offset: offset,
            PageSize: context.Request.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: truncatedByPage,
            Truncated: truncatedByPage,
            Rows: rows);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "TestimoX stored run summary",
            maxTop: Math.Max(context.Request.PageSize, rows.Count),
            baseTruncated: truncatedByPage,
            scanned: filteredRows.Count,
            metaMutate: meta => {
                meta.Add("store_directory", storeDirectory);
                meta.Add("run_id", runData.Run.RunId);
                meta.Add("stored_result_count", runData.Run.StoredResultCount);
                meta.Add("matched_count", filteredRows.Count);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                meta.Add("page_size", context.Request.PageSize);
                meta.Add("scope_group", context.Request.ScopeGroup);
                meta.Add("truncated_by_page", truncatedByPage);
                if (!string.IsNullOrWhiteSpace(context.Request.ScopeIdContains)) {
                    meta.Add("scope_id_contains", context.Request.ScopeIdContains);
                }
                if (!string.IsNullOrWhiteSpace(context.Request.RuleNameContains)) {
                    meta.Add("rule_name_contains", context.Request.RuleNameContains);
                }
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
            }));
    }

    private sealed record TestimoXStoredRunSummaryResult(
        string StoreDirectory,
        string RunId,
        DateTimeOffset StartedUtc,
        DateTimeOffset? EndedUtc,
        double? DurationSeconds,
        string Policy,
        int StoredResultCount,
        int DistinctRuleCount,
        int DistinctDomainCount,
        int DistinctDomainControllerCount,
        string ScopeGroup,
        string ScopeIdContains,
        string RuleNameContains,
        int TotalSecurityScore,
        int TotalHealthScore,
        int TotalOverallScore,
        int TotalSecurityPenalty,
        int TotalHealthPenalty,
        int TotalPenalty,
        int FilteredSecurityScore,
        int FilteredHealthScore,
        int FilteredOverallScore,
        int FilteredSecurityPenalty,
        int FilteredHealthPenalty,
        int FilteredPenalty,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<TestimoXStoredRunRow> Rows);

    private sealed record TestimoXStoredRunRow(
        string ScopeGroup,
        string ScopeId,
        string Domain,
        string DomainController,
        string RuleName,
        string OverallStatus,
        DateTimeOffset CompletedUtc,
        int TestsSecurityCount,
        int TestsHealthCount,
        int PenaltySecurity,
        int PenaltyHealth,
        int PenaltyTotal);
}
