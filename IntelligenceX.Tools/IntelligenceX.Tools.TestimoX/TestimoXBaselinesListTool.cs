using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using TestimoX.Baselines;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Lists TestimoX vendor baseline catalog entries with paging and selector support.
/// </summary>
public sealed class TestimoXBaselinesListTool : TestimoXToolBase, ITool {
    private const int DefaultPageSize = 25;
    private const int MaxIdPatterns = 16;

    private sealed record BaselinesListRequest(
        IReadOnlyList<string> RequestedBaselineIds,
        IReadOnlyList<string> IdPatterns,
        HashSet<string>? VendorFilter,
        HashSet<string>? ProductFilter,
        string VersionWildcard,
        string? SearchText,
        bool LatestOnly,
        int PageSize,
        int Offset);

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_baselines_list",
        "List available TestimoX vendor baselines and baseline IDs for catalog discovery and crosswalk planning.",
        ToolSchema.Object(
                ("baseline_ids", ToolSchema.Array(ToolSchema.String(), "Optional explicit baseline ids to include.")),
                ("id_patterns", ToolSchema.Array(ToolSchema.String("Wildcard matched against baseline_id/vendor_id/product_id/version (for example: MSB/*/1_*)."), "Optional wildcard selectors.")),
                ("vendor_ids", ToolSchema.Array(ToolSchema.String("Baseline vendor id.").Enum(TestimoXBaselineCatalogHelper.VendorNames), "Optional vendor filters (any-match).")),
                ("product_ids", ToolSchema.Array(ToolSchema.String("Baseline product id.").Enum(TestimoXBaselineCatalogHelper.ProductNames), "Optional product filters (any-match).")),
                ("version_wildcard", ToolSchema.String("Optional version wildcard (for example: 1_* or V2R*). Default *.")),
                ("search_text", ToolSchema.String("Optional case-insensitive search across baseline_id/vendor_id/product_id/version.")),
                ("latest_only", ToolSchema.Boolean("When true, return only the latest version per vendor/product. Default false.")),
                ("page_size", ToolSchema.Integer("Optional number of baselines to return in this page. Default 25.")),
                ("offset", ToolSchema.Integer("Optional zero-based offset into matched baselines (for paging).")),
                ("cursor", ToolSchema.String("Optional opaque paging cursor (alternative to offset).")))
            .WithTableViewOptions()
            .NoAdditionalProperties(),
        category: "testimox",
        tags: new[] {
            "compliance",
            "baselines",
            "catalog"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXBaselinesListTool"/> class.
    /// </summary>
    public TestimoXBaselinesListTool(TestimoXToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<BaselinesListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var requestedBaselineIds = reader.DistinctStringArray("baseline_ids");
            var idPatterns = reader.DistinctStringArray("id_patterns");
            if (idPatterns.Count > MaxIdPatterns) {
                return ToolRequestBindingResult<BaselinesListRequest>.Failure(
                    $"id_patterns supports at most {MaxIdPatterns} values.");
            }

            var requestedVendors = reader.DistinctStringArray("vendor_ids");
            if (!TestimoXBaselineCatalogHelper.TryParseSet(requestedVendors, TestimoXBaselineCatalogHelper.VendorNames, "vendor_ids", out var vendorFilter, out var vendorError)) {
                return ToolRequestBindingResult<BaselinesListRequest>.Failure(vendorError ?? "Invalid vendor_ids argument.");
            }

            var requestedProducts = reader.DistinctStringArray("product_ids");
            if (!TestimoXBaselineCatalogHelper.TryParseSet(requestedProducts, TestimoXBaselineCatalogHelper.ProductNames, "product_ids", out var productFilter, out var productError)) {
                return ToolRequestBindingResult<BaselinesListRequest>.Failure(productError ?? "Invalid product_ids argument.");
            }

            var versionWildcard = reader.OptionalString("version_wildcard");
            if (string.IsNullOrWhiteSpace(versionWildcard)) {
                versionWildcard = "*";
            }

            var searchText = reader.OptionalString("search_text");
            var latestOnly = reader.Boolean("latest_only", defaultValue: false);
            var pageSize = TestimoXPagingHelper.ResolvePageSize(arguments, Options.MaxRulesInCatalog) ?? Math.Min(DefaultPageSize, Options.MaxRulesInCatalog);
            if (!TestimoXPagingHelper.TryReadOffset(arguments, out var offset, out var offsetError)) {
                return ToolRequestBindingResult<BaselinesListRequest>.Failure(offsetError ?? "Invalid offset argument.");
            }

            return ToolRequestBindingResult<BaselinesListRequest>.Success(new BaselinesListRequest(
                RequestedBaselineIds: requestedBaselineIds,
                IdPatterns: idPatterns,
                VendorFilter: vendorFilter,
                ProductFilter: productFilter,
                VersionWildcard: versionWildcard,
                SearchText: searchText,
                LatestOnly: latestOnly,
                PageSize: pageSize,
                Offset: offset));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<BaselinesListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.TestimoX pack is disabled by policy.",
                hints: new[] { "Enable the TestimoX pack in host/service options before calling testimox_baselines_list." },
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
        var availableIds = (context.Request.LatestOnly
                ? latestIds
                : BaselineService.GetIds().Where(static id => !string.IsNullOrWhiteSpace(id)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var availableById = availableIds.ToDictionary(static id => id, StringComparer.OrdinalIgnoreCase);

        var unknown = context.Request.RequestedBaselineIds
            .Where(id => !availableById.ContainsKey(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length > 0) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: $"Unknown TestimoX baseline id(s): {string.Join(", ", unknown)}.",
                hints: new[] { "Call testimox_baselines_list without baseline_ids first to inspect available baseline ids." },
                isTransient: false));
        }

        var hasSelectorFilters =
            context.Request.IdPatterns.Count > 0 ||
            context.Request.VendorFilter is { Count: > 0 } ||
            context.Request.ProductFilter is { Count: > 0 } ||
            !string.Equals(context.Request.VersionWildcard, "*", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(context.Request.SearchText);

        var selectedIds = new HashSet<string>(context.Request.RequestedBaselineIds, StringComparer.OrdinalIgnoreCase);
        if (hasSelectorFilters || selectedIds.Count == 0) {
            foreach (var baselineId in availableIds) {
                var parsed = TestimoXBaselineCatalogHelper.ParseBaselineId(baselineId);
                if (!MatchesFilters(parsed, context.Request)) {
                    continue;
                }

                selectedIds.Add(baselineId);
            }
        }

        var matchedRows = selectedIds
            .Select(TestimoXBaselineCatalogHelper.ParseBaselineId)
            .OrderBy(static row => row.VendorId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.ProductId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Version, StringComparer.OrdinalIgnoreCase)
            .Select(row => new TestimoBaselineCatalogRow(
                BaselineId: row.BaselineId,
                VendorId: row.VendorId,
                ProductId: row.ProductId,
                Version: row.Version,
                IsLatestForVendorProduct: latestIds.Contains(row.BaselineId)))
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

        var model = new TestimoBaselinesListResult(
            DiscoveredCount: availableIds.Length,
            MatchedCount: matchedRows.Count,
            ReturnedCount: rows.Count,
            Offset: offset,
            PageSize: context.Request.PageSize,
            NextOffset: nextOffset,
            NextCursor: nextCursor,
            TruncatedByPage: truncatedByPage,
            Truncated: truncatedByPage,
            LatestOnly: context.Request.LatestOnly,
            Baselines: rows);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: "baselines_view",
            title: "TestimoX baselines",
            maxTop: Math.Max(context.Request.PageSize, rows.Count),
            baseTruncated: truncatedByPage,
            scanned: availableIds.Length,
            metaMutate: meta => {
                meta.Add("matched_count", matchedRows.Count);
                meta.Add("returned_count", rows.Count);
                meta.Add("offset", offset);
                meta.Add("page_size", context.Request.PageSize);
                meta.Add("latest_only", context.Request.LatestOnly);
                if (nextOffset.HasValue) {
                    meta.Add("next_offset", nextOffset.Value);
                }
                if (!string.IsNullOrWhiteSpace(nextCursor)) {
                    meta.Add("next_cursor", nextCursor);
                }
                meta.Add("truncated_by_page", truncatedByPage);
            }));
    }

    private static bool MatchesFilters(TestimoXBaselineCatalogHelper.ParsedBaselineId row, BaselinesListRequest request) {
        if (request.VendorFilter is { Count: > 0 } && !request.VendorFilter.Contains(row.VendorId)) {
            return false;
        }

        if (request.ProductFilter is { Count: > 0 } && !request.ProductFilter.Contains(row.ProductId)) {
            return false;
        }

        if (!TestimoXBaselineCatalogHelper.WildcardMatch(row.Version, request.VersionWildcard)) {
            return false;
        }

        if (request.IdPatterns.Count > 0 && !request.IdPatterns.Any(pattern => MatchesPattern(row, pattern))) {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.SearchText)) {
            var search = request.SearchText.Trim();
            if (!TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.BaselineId, search)
                && !TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.VendorId, search)
                && !TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.ProductId, search)
                && !TestimoXBaselineCatalogHelper.ContainsOrdinalIgnoreCase(row.Version, search)) {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesPattern(TestimoXBaselineCatalogHelper.ParsedBaselineId row, string pattern) {
        return TestimoXBaselineCatalogHelper.WildcardMatch(row.BaselineId, pattern)
            || TestimoXBaselineCatalogHelper.WildcardMatch(row.VendorId, pattern)
            || TestimoXBaselineCatalogHelper.WildcardMatch(row.ProductId, pattern)
            || TestimoXBaselineCatalogHelper.WildcardMatch(row.Version, pattern);
    }

    private sealed record TestimoBaselinesListResult(
        int DiscoveredCount,
        int MatchedCount,
        int ReturnedCount,
        int Offset,
        int PageSize,
        int? NextOffset,
        string NextCursor,
        bool TruncatedByPage,
        bool Truncated,
        bool LatestOnly,
        IReadOnlyList<TestimoBaselineCatalogRow> Baselines);

    private sealed record TestimoBaselineCatalogRow(
        string BaselineId,
        string VendorId,
        string ProductId,
        string Version,
        bool IsLatestForVendorProduct);
}
