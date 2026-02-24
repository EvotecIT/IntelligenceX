using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Searches Active Directory using LDAP filters (read-only).
/// </summary>
public sealed class AdSearchTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_search",
        "Search Active Directory for users/groups/computers by query (read-only).",
        ToolSchema.Object(
                ("query", ToolSchema.String("Search term (samAccountName, UPN, mail, displayName, cn/name).")),
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "group", "computer")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum results to return (capped).")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional attributes to include (engine policy enforced).")))
            .WithTableViewOptions()
            .Required("query")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSearchTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdSearchTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var query = ToolArgs.GetOptionalTrimmed(arguments, "query") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) {
            return Task.FromResult(Error("invalid_argument", "query is required."));
        }

        var kindArg = ToolArgs.GetOptionalTrimmed(arguments, "kind");
        var kind = string.IsNullOrWhiteSpace(kindArg) ? "any" : kindArg.Trim().ToLowerInvariant();

        var maxResults = ResolveMaxResults(arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);

        var maxValuesPerAttribute = ToolArgs.GetCappedInt32(
            arguments,
            "max_values_per_attribute",
            LdapQueryPolicy.DefaultMaxValuesPerAttribute,
            1,
            LdapQueryPolicy.MaxValuesPerAttributeCap);

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        var attributes = ToolArgs.ReadStringArray(arguments?.GetArray("attributes"));

        if (!LdapToolSearchService.TryExecute(
                request: new LdapToolSearchQueryRequest {
                    Query = query,
                    Kind = kind,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxResults = maxResults,
                    MaxValuesPerAttribute = maxValuesPerAttribute,
                    Attributes = attributes
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = queryResult!;
        var root = new {
            result.Query,
            result.Kind,
            result.DomainController,
            result.SearchBaseDn,
            result.LdapFilter,
            result.MaxResults,
            result.MaxValuesPerAttribute,
            result.Count,
            result.IsTruncated,
            result.Results
        };
        var shapedArguments = SanitizeProjectionArguments(arguments, result.Results);

        AdDynamicTableView.TryBuildResponseFromQueryRows(
            arguments: shapedArguments,
            model: root,
            rows: result.Results,
            title: "Active Directory: Search (preview)",
            rowsPath: "results_view",
            baseTruncated: result.IsTruncated,
            response: out var response);
        return Task.FromResult(response);
    }

    private static JsonObject? SanitizeProjectionArguments(JsonObject? arguments, IReadOnlyList<LdapToolQueryRow> rows) {
        if (arguments is null || arguments.Count == 0) {
            return arguments;
        }

        var availableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var availableCanonicalColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++) {
            var attrs = rows[i]?.Attributes;
            if (attrs is null) {
                continue;
            }

            foreach (var pair in attrs) {
                var key = (pair.Key ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                availableColumns.Add(key);
                availableCanonicalColumns.Add(CanonicalizeColumnKey(key));
            }
        }

        var removeColumns = false;
        var removeSortBy = false;
        if (TryGetColumnsArgument(arguments, out var requestedColumns) && requestedColumns.Count > 0) {
            for (var i = 0; i < requestedColumns.Count; i++) {
                var requested = requestedColumns[i];
                if (requested.Length == 0) {
                    continue;
                }

                var canonical = CanonicalizeColumnKey(requested);
                if (!availableColumns.Contains(requested)
                    && (canonical.Length == 0 || !availableCanonicalColumns.Contains(canonical))) {
                    removeColumns = true;
                    break;
                }
            }
        }

        if (TryGetSortByArgument(arguments, out var sortBy) && sortBy.Length > 0) {
            var canonical = CanonicalizeColumnKey(sortBy);
            if (!availableColumns.Contains(sortBy)
                && (canonical.Length == 0 || !availableCanonicalColumns.Contains(canonical))) {
                removeSortBy = true;
            }
        }

        if (!removeColumns && !removeSortBy) {
            return arguments;
        }

        var clone = new JsonObject(StringComparer.Ordinal);
        foreach (var pair in arguments) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            if (removeColumns && string.Equals(key, "columns", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if ((removeColumns || removeSortBy) && string.Equals(key, "sort_by", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if ((removeColumns || removeSortBy) && string.Equals(key, "sort_direction", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            clone.Add(key, pair.Value);
        }

        return clone;
    }

    private static bool TryGetColumnsArgument(JsonObject arguments, out IReadOnlyList<string> columns) {
        columns = Array.Empty<string>();
        if (arguments is null) {
            return false;
        }

        foreach (var pair in arguments) {
            if (!string.Equals((pair.Key ?? string.Empty).Trim(), "columns", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (pair.Value?.AsArray() is not JsonArray array || array.Count == 0) {
                return false;
            }

            columns = ToolArgs.ReadDistinctStringArray(array)
                .Select(static x => (x ?? string.Empty).Trim())
                .Where(static x => x.Length > 0)
                .ToArray();
            return columns.Count > 0;
        }

        return false;
    }

    private static bool TryGetSortByArgument(JsonObject arguments, out string sortBy) {
        sortBy = string.Empty;
        if (arguments is null) {
            return false;
        }

        foreach (var pair in arguments) {
            if (!string.Equals((pair.Key ?? string.Empty).Trim(), "sort_by", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            sortBy = (pair.Value?.AsString() ?? string.Empty).Trim();
            return sortBy.Length > 0;
        }

        return false;
    }

    private static string CanonicalizeColumnKey(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var index = 0;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch)) {
                continue;
            }

            buffer[index++] = char.ToLowerInvariant(ch);
        }

        return index == 0 ? string.Empty : new string(buffer, 0, index);
    }
}
