using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Lists EventViewerX named-event rules that can be queried by the named-events tool surface.
/// </summary>
public sealed class EventLogNamedEventsCatalogTool : EventLogToolBase, ITool {
    private const int MaxViewTop = 5000;
    private const int MaxCategoryFilters = 16;
    private const int MaxEventIdsPerRowCap = 256;
    private static readonly string[] CategoryNames = EventLogNamedEventsHelper.GetKnownCategories().ToArray();

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_named_events_catalog",
        "List EventViewerX named-event rules with query aliases, categories, channels, and mapped Event IDs.",
        ToolSchema.Object(
                ("name_contains", ToolSchema.String("Optional case-insensitive filter applied to enum_name/query_name/category.")),
                ("categories", ToolSchema.Array(ToolSchema.String("Category name (for example: active_directory, kerberos, group_policy).").Enum(CategoryNames), "Optional category filter list.")),
                ("available_only", ToolSchema.Boolean("When true, return only named events with discovered log/event-id mappings.")),
                ("include_event_ids", ToolSchema.Boolean("When true, include event_ids arrays in each row (default true).")),
                ("max_event_ids_per_row", ToolSchema.Integer("Maximum event IDs returned per row when include_event_ids=true (capped).")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record NamedEventsCatalogViewRow(
        string EnumName,
        string QueryName,
        string Category,
        IReadOnlyList<string> LogNames,
        IReadOnlyList<int> EventIds,
        int EventIdCount,
        bool Available);

    private sealed record NamedEventsCatalogResult(
        int Total,
        int Matched,
        bool Truncated,
        bool IncludeEventIds,
        int MaxEventIdsPerRow,
        IReadOnlyList<NamedEventsCatalogViewRow> Items);

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogNamedEventsCatalogTool"/> class.
    /// </summary>
    public EventLogNamedEventsCatalogTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = ToolArgs.GetOptionalTrimmed(arguments, "name_contains");
        var availableOnly = ToolArgs.GetBoolean(arguments, "available_only");
        var includeEventIds = arguments?.GetBoolean("include_event_ids") ?? true;
        var maxEventIdsPerRow = ToolArgs.GetCappedInt32(arguments, "max_event_ids_per_row", 32, 1, MaxEventIdsPerRowCap);
        var maxResults = ResolveMaxResults(arguments);

        var categoriesFilter = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("categories"));
        List<string>? categories = null;
        if (categoriesFilter.Count > 0) {
            if (!EventLogNamedEventsHelper.TryParseCategories(categoriesFilter, MaxCategoryFilters, out var parsedCategories, out var categoryError)) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", categoryError ?? "Invalid categories argument."));
            }

            categories = parsedCategories;
        }

        var all = EventLogNamedEventsHelper.GetCatalogRows();
        IEnumerable<EventLogNamedEventCatalogRow> query = all;
        if (!string.IsNullOrWhiteSpace(filter)) {
            query = query.Where(row =>
                row.EnumName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.QueryName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                row.Category.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        if (categories is not null && categories.Count > 0) {
            var set = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
            query = query.Where(row => set.Contains(row.Category));
        }
        if (availableOnly) {
            query = query.Where(static row => row.Available);
        }

        var matchedRows = query.ToList();
        var truncated = matchedRows.Count > maxResults;
        var selectedRawRows = truncated ? matchedRows.Take(maxResults).ToList() : matchedRows;
        var selectedRows = selectedRawRows
            .Select(row => new NamedEventsCatalogViewRow(
                EnumName: row.EnumName,
                QueryName: row.QueryName,
                Category: row.Category,
                LogNames: row.LogNames,
                EventIds: includeEventIds ? row.EventIds.Take(maxEventIdsPerRow).ToArray() : Array.Empty<int>(),
                EventIdCount: row.EventIdCount,
                Available: row.Available))
            .ToList();

        var result = new NamedEventsCatalogResult(
            Total: all.Count,
            Matched: matchedRows.Count,
            Truncated: truncated,
            IncludeEventIds: includeEventIds,
            MaxEventIdsPerRow: maxEventIdsPerRow,
            Items: selectedRows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: selectedRows,
            viewRowsPath: "items_view",
            title: "Named events catalog (preview)",
            baseTruncated: truncated,
            scanned: selectedRows.Count,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                meta.Add("total", all.Count);
                meta.Add("matched", matchedRows.Count);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("available_only", availableOnly);
                meta.Add("include_event_ids", includeEventIds);
                if (includeEventIds) {
                    meta.Add("max_event_ids_per_row", maxEventIdsPerRow);
                }
                if (!string.IsNullOrWhiteSpace(filter)) {
                    meta.Add("name_contains", filter);
                }
                if (categories is not null && categories.Count > 0) {
                    meta.Add("categories", ToolJson.ToJsonArray(categories));
                }
            });
        return Task.FromResult(response);
    }
}
