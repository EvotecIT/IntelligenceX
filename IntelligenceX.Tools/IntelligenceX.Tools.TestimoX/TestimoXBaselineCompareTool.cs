using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Controls;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX.Baselines;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Compares vendor baseline controls for a single product/version slice.
/// </summary>
public sealed class TestimoXBaselineCompareTool : TestimoXToolBase, ITool {
    private const int DefaultPageSize = 25;

    private sealed record BaselineCompareRequest(
        string ProductId,
        HashSet<string>? VendorFilter,
        string VersionWildcard,
        bool LatestOnly,
        bool OnlyDiff,
        string? SearchText,
        int PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_baseline_compare",
        "Compare vendor baselines for a product and highlight desired-value/comparator/value-kind deltas.",
        ToolSchema.Object(
                ("product_id", ToolSchema.String("Baseline product id to compare.").Enum(TestimoXBaselineCatalogHelper.ProductNames)),
                ("vendor_ids", ToolSchema.Array(ToolSchema.String("Baseline vendor id.").Enum(TestimoXBaselineCatalogHelper.VendorNames), "Optional vendor filters (defaults to MSB/CIS/STIG).")),
                ("version_wildcard", ToolSchema.String("Optional version wildcard (for example: 1_* or V2R*). Default *.")),
                ("latest_only", ToolSchema.Boolean("When true, compare only the latest baseline version per requested vendor. Default false.")),
                ("only_diff", ToolSchema.Boolean("When true, keep only rows where desired/comparator/value kind differs across vendors. Default false.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across anchor/title/kind/applies_to and vendor rule ids.")),
                ("page_size", ToolSchema.Integer("Optional number of comparison rows to return in this page. Default 25.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched comparison rows (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "baselines",
            "compare",
            "fallback_hint_keys:product_id,vendor_ids,version_wildcard,latest_only,only_diff,search_text"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXBaselineCompareTool"/> class.
    /// </summary>
    public TestimoXBaselineCompareTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<BaselineCompareRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var productId = reader.OptionalString("product_id");
            if (string.IsNullOrWhiteSpace(productId)) {
                return ToolRequestBindingResult<BaselineCompareRequest>.Failure(
                    $"product_id is required. Supported values: {string.Join(", ", TestimoXBaselineCatalogHelper.ProductNames)}.");
            }

            if (!TestimoXBaselineCatalogHelper.ProductNames.Contains(productId, StringComparer.OrdinalIgnoreCase)) {
                return ToolRequestBindingResult<BaselineCompareRequest>.Failure(
                    $"Invalid product_id '{productId}'. Supported values: {string.Join(", ", TestimoXBaselineCatalogHelper.ProductNames)}.");
            }

            var requestedVendors = reader.DistinctStringArray("vendor_ids");
            if (!TestimoXBaselineCatalogHelper.TryParseSet(
                    requestedVendors,
                    TestimoXBaselineCatalogHelper.VendorNames,
                    "vendor_ids",
                    out var vendorFilter,
                    out var vendorError)) {
                return ToolRequestBindingResult<BaselineCompareRequest>.Failure(vendorError ?? "Invalid vendor_ids argument.");
            }

            var versionWildcard = reader.OptionalString("version_wildcard");
            if (string.IsNullOrWhiteSpace(versionWildcard)) {
                versionWildcard = "*";
            }

            var latestOnly = reader.Boolean("latest_only", defaultValue: false);
            var onlyDiff = reader.Boolean("only_diff", defaultValue: false);
            var searchText = reader.OptionalString("search_text");
            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog) ?? Math.Min(DefaultPageSize, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<BaselineCompareRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<BaselineCompareRequest>.Success(new BaselineCompareRequest(
                ProductId: productId,
                VendorFilter: vendorFilter,
                VersionWildcard: versionWildcard,
                LatestOnly: latestOnly,
                OnlyDiff: onlyDiff,
                SearchText: searchText,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<BaselineCompareRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_baseline_compare." },
                isTransient: false));
        }

        try {
            BaselineCatalog.WarmUp();
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, "TestimoX baseline catalog warm-up failed."));
        }

        var latestIds = BaselineCatalog.GetAllLatest()
            .Select(static entry => entry.Id)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vendorScope = context.Request.VendorFilter is { Count: > 0 }
            ? context.Request.VendorFilter
            : new HashSet<string>(new[] { "MSB", "CIS", "STIG" }, StringComparer.OrdinalIgnoreCase);

        var matchedBaselineIds = BaselineService.GetIds()
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(TestimoXBaselineCatalogHelper.ParseBaselineId)
            .Where(row => vendorScope.Contains(row.VendorId))
            .Where(row => string.Equals(row.ProductId, context.Request.ProductId, StringComparison.OrdinalIgnoreCase))
            .Where(row => TestimoXBaselineCatalogHelper.WildcardMatch(row.Version, context.Request.VersionWildcard))
            .Where(row => !context.Request.LatestOnly || latestIds.Contains(row.BaselineId))
            .OrderBy(static row => row.VendorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Version, StringComparer.OrdinalIgnoreCase)
            .Select(static row => row.BaselineId)
            .ToArray();

        if (matchedBaselineIds.Length == 0) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "no_results",
                error: "No baseline ids matched the requested product/vendor/version selectors.",
                hints: new[] { "Call testimox_baselines_list first to inspect available baseline ids and version strings." },
                isTransient: false));
        }

        IReadOnlyList<BaselineComparisonRow> comparisonRows;
        try {
            comparisonRows = BaselineComparer.Build(matchedBaselineIds);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, "TestimoX baseline comparison failed."));
        }

        IEnumerable<BaselineComparisonRow> filtered = comparisonRows;
        if (context.Request.OnlyDiff) {
            filtered = filtered.Where(static row => !(row.SameDesired && row.SameComparator && row.SameValueKind));
        }

        if (!string.IsNullOrWhiteSpace(context.Request.SearchText)) {
            var search = context.Request.SearchText.Trim();
            filtered = filtered.Where(row =>
                TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.Anchor, search)
                || TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.Title, search)
                || TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.Kind, search)
                || TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.AppliesTo, search)
                || TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.CxId, search)
                || TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.CisId, search)
                || TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.StigId, search));
        }

        var matchedRows = filtered
            .OrderBy(static row => row.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Anchor, StringComparer.OrdinalIgnoreCase)
            .Select(row => new TestimoBaselineCompareRow(
                Anchor: row.Anchor,
                Kind: row.Kind,
                AppliesTo: row.AppliesTo ?? string.Empty,
                Title: row.Title,
                MsbRuleId: row.CxId ?? string.Empty,
                CisRuleId: row.CisId ?? string.Empty,
                StigRuleId: row.StigId ?? string.Empty,
                DesiredMsb: TestimoXBaselineCatalogHelper.FormatDesiredValue(row.DesiredCx),
                DesiredCis: TestimoXBaselineCatalogHelper.FormatDesiredValue(row.DesiredCis),
                DesiredStig: TestimoXBaselineCatalogHelper.FormatDesiredValue(row.DesiredStig),
                ComparatorMsb: row.ComparatorCx ?? string.Empty,
                ComparatorCis: row.ComparatorCis ?? string.Empty,
                ComparatorStig: row.ComparatorStig ?? string.Empty,
                ValueKindMsb: row.ValueKindCx ?? string.Empty,
                ValueKindCis: row.ValueKindCis ?? string.Empty,
                ValueKindStig: row.ValueKindStig ?? string.Empty,
                SameDesired: row.SameDesired,
                SameComparator: row.SameComparator,
                SameValueKind: row.SameValueKind))
            .ToList();

        var offset = context.Request.Offset;
        if (offset > matchedRows.Count) {
            offset = matchedRows.Count;
        }

        var rows = matchedRows
            .Skip(offset)
            .Take(context.Request.PageSize)
            .ToList();
        var truncatedByPage = offset + rows.Count < matchedRows.Count;
        var nextOffset = truncatedByPage ? offset + rows.Count : (int?)null;
        var nextCursor = nextOffset.HasValue ? OffsetCursor.Encode(nextOffset.Value) : string.Empty;

        var model = new TestimoBaselineCompareResult(
            ProductId: context.Request.ProductId,
            VersionWildcard: context.Request.VersionWildcard,
            LatestOnly: context.Request.LatestOnly,
            OnlyDiff: context.Request.OnlyDiff,
            MatchedBaselineCount: matchedBaselineIds.Length,
            MatchedBaselineIds: matchedBaselineIds,
            ComparedVendorIds: vendorScope.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            SourceRowCount: comparisonRows.Count,
            MatchedCount: matchedRows.Count,
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
            viewRowsPath: "comparison_view",
            title: "TestimoX baseline comparison",
            maxTop: Math.Max(context.Request.PageSize, rows.Count),
            baseTruncated: truncatedByPage,
            scanned: comparisonRows.Count,
            metaMutate: meta => {
                meta.Add("product_id", context.Request.ProductId);
                meta.Add("version_wildcard", context.Request.VersionWildcard);
                meta.Add("latest_only", context.Request.LatestOnly);
                meta.Add("only_diff", context.Request.OnlyDiff);
                meta.Add("matched_baseline_count", matchedBaselineIds.Length);
                meta.Add("matched_count", matchedRows.Count);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                meta.Add("page_size", context.Request.PageSize);
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
                meta.Add("truncated_by_page", truncatedByPage);
            }));
    }

    private sealed record TestimoBaselineCompareResult(
        string ProductId,
        string VersionWildcard,
        bool LatestOnly,
        bool OnlyDiff,
        int MatchedBaselineCount,
        IReadOnlyList<string> MatchedBaselineIds,
        IReadOnlyList<string> ComparedVendorIds,
        int SourceRowCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        IReadOnlyList<TestimoBaselineCompareRow> Rows);

    private sealed record TestimoBaselineCompareRow(
        string Anchor,
        string Kind,
        string AppliesTo,
        string Title,
        string MsbRuleId,
        string CisRuleId,
        string StigRuleId,
        string DesiredMsb,
        string DesiredCis,
        string DesiredStig,
        string ComparatorMsb,
        string ComparatorCis,
        string ComparatorStig,
        string ValueKindMsb,
        string ValueKindCis,
        string ValueKindStig,
        bool SameDesired,
        bool SameComparator,
        bool SameValueKind);
}
