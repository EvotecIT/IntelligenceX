using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists Group Policy WMI filters for a domain (read-only).
/// </summary>
public sealed class AdWmiFiltersTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;
    private const int DefaultMaxQueriesPerFilter = 10;
    private const int MaxQueriesPerFilterCap = 50;
    private const int DefaultMaxQueryChars = 2000;
    private const int MaxQueryCharsCap = 20_000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_wmi_filters",
        "List AD Group Policy WMI filters for a domain with optional text filters and query preview rows (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to query.")),
                ("display_name_contains", ToolSchema.String("Optional case-insensitive substring filter for display name.")),
                ("author_contains", ToolSchema.String("Optional case-insensitive substring filter for author.")),
                ("query_contains", ToolSchema.String("Optional case-insensitive substring filter matched against rendered query text.")),
                ("include_queries", ToolSchema.Boolean("When true, include parsed query rows for each filter (default false).")),
                ("max_queries_per_filter", ToolSchema.Integer("Maximum parsed query rows per filter when include_queries=true (capped). Default 10.")),
                ("max_query_chars", ToolSchema.Integer("Maximum characters per query text when include_queries=true (capped). Default 2000.")),
                ("max_results", ToolSchema.Integer("Maximum filter rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record WmiFilterQueryRow(
        string Namespace,
        string Query);

    private sealed record WmiFilterRow(
        string DomainName,
        string DisplayName,
        Guid Id,
        string? Author,
        string? Description,
        int QueryCount,
        string? CanonicalName,
        string? DistinguishedName,
        DateTime? Created,
        DateTime? Modified,
        IReadOnlyList<WmiFilterQueryRow>? Queries);

    private sealed record AdWmiFiltersResult(
        string DomainName,
        bool IncludeQueries,
        int MaxQueriesPerFilter,
        int MaxQueryChars,
        string? DisplayNameContains,
        string? AuthorContains,
        string? QueryContains,
        int Scanned,
        bool Truncated,
        IReadOnlyList<WmiFilterRow> Filters);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdWmiFiltersTool"/> class.
    /// </summary>
    public AdWmiFiltersTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainQueryRequest(arguments, out var request, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var displayNameContains = ToolArgs.GetOptionalTrimmed(arguments, "display_name_contains");
        var authorContains = ToolArgs.GetOptionalTrimmed(arguments, "author_contains");
        var queryContains = ToolArgs.GetOptionalTrimmed(arguments, "query_contains");
        var includeQueries = ToolArgs.GetBoolean(arguments, "include_queries", defaultValue: false);
        var maxQueriesPerFilter = ToolArgs.GetCappedInt32(
            arguments,
            key: "max_queries_per_filter",
            defaultValue: DefaultMaxQueriesPerFilter,
            minInclusive: 1,
            maxInclusive: MaxQueriesPerFilterCap);
        var maxQueryChars = ToolArgs.GetCappedInt32(
            arguments,
            key: "max_query_chars",
            defaultValue: DefaultMaxQueryChars,
            minInclusive: 64,
            maxInclusive: MaxQueryCharsCap);

        if (!TryExecute(
                action: () => WmiFilterService.EnumerateFilters(request.DomainName, cancellationToken).ToArray(),
                result: out var filters,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "WMI filter query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var projected = filters
            .Where(filter => MatchesText(filter.DisplayName, displayNameContains))
            .Where(filter => MatchesText(filter.Author, authorContains))
            .Where(filter => string.IsNullOrWhiteSpace(queryContains) || MatchesText(filter.Query, queryContains))
            .Select(filter => new WmiFilterRow(
                DomainName: filter.DomainName,
                DisplayName: filter.DisplayName,
                Id: filter.Id,
                Author: filter.Author,
                Description: filter.Description,
                QueryCount: filter.QueryCount,
                CanonicalName: filter.CanonicalName,
                DistinguishedName: filter.DistinguishedName,
                Created: filter.Created?.ToUniversalTime(),
                Modified: filter.Modified?.ToUniversalTime(),
                Queries: includeQueries ? ProjectQueries(filter.Queries, maxQueriesPerFilter, maxQueryChars) : null))
            .OrderBy(static row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = CapRows(
            allRows: projected,
            maxResults: request.MaxResults,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdWmiFiltersResult(
            DomainName: request.DomainName,
            IncludeQueries: includeQueries,
            MaxQueriesPerFilter: maxQueriesPerFilter,
            MaxQueryChars: maxQueryChars,
            DisplayNameContains: displayNameContains,
            AuthorContains: authorContains,
            QueryContains: queryContains,
            Scanned: scanned,
            Truncated: truncated,
            Filters: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "filters_view",
            title: "Active Directory: WMI Filters (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("include_queries", includeQueries);
                meta.Add("max_queries_per_filter", maxQueriesPerFilter);
                meta.Add("max_query_chars", maxQueryChars);
                AddOptionalStringMeta(meta, "display_name_contains", displayNameContains);
                AddOptionalStringMeta(meta, "author_contains", authorContains);
                AddOptionalStringMeta(meta, "query_contains", queryContains);
                AddDomainAndMaxResultsMeta(meta, request.DomainName, request.MaxResults);
            }));
    }

    private static IReadOnlyList<WmiFilterQueryRow> ProjectQueries(
        IReadOnlyList<WmiFilterQuery>? source,
        int maxQueriesPerFilter,
        int maxQueryChars) {
        if (source is null || source.Count == 0) {
            return Array.Empty<WmiFilterQueryRow>();
        }

        return source
            .Where(static query => query is not null)
            .Take(maxQueriesPerFilter)
            .Select(query => new WmiFilterQueryRow(
                Namespace: string.IsNullOrWhiteSpace(query.Namespace) ? string.Empty : query.Namespace.Trim(),
                Query: TrimToMaxChars(query.Query, maxQueryChars)))
            .ToArray();
    }

    private static bool MatchesText(string? value, string? needle) {
        if (string.IsNullOrWhiteSpace(needle)) {
            return true;
        }

        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimToMaxChars(string? value, int maxChars) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxChars
            ? trimmed
            : trimmed[..maxChars];
    }
}
