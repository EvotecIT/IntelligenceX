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
    private sealed record SearchRequest(
        string Query,
        string? Kind,
        int MaxValuesPerAttribute,
        IReadOnlyList<string> Attributes);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<SearchRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("query", out var query, out var queryError)) {
                return ToolRequestBindingResult<SearchRequest>.Failure(queryError);
            }

            return ToolRequestBindingResult<SearchRequest>.Success(new SearchRequest(
                Query: query,
                Kind: reader.OptionalString("kind"),
                MaxValuesPerAttribute: reader.CappedInt32(
                    "max_values_per_attribute",
                    LdapQueryPolicy.DefaultMaxValuesPerAttribute,
                    1,
                    LdapQueryPolicy.MaxValuesPerAttributeCap),
                Attributes: reader.StringArray("attributes")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<SearchRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "any" : request.Kind.Trim().ToLowerInvariant();
        var maxResults = ResolveMaxResults(context.Arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        if (!LdapToolSearchService.TryExecute(
                request: new LdapToolSearchQueryRequest {
                    Query = request.Query,
                    Kind = kind,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxResults = maxResults,
                    MaxValuesPerAttribute = request.MaxValuesPerAttribute,
                    Attributes = request.Attributes
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
        var shapedArguments = AdProjectionArgumentSanitizer.RemoveUnsupportedProjectionArguments(
            context.Arguments,
            BuildAvailableProjectionColumns(result.Results));

        if (!AdDynamicTableView.TryBuildResponseFromQueryRows(
                arguments: shapedArguments,
                model: root,
                rows: result.Results,
                title: "Active Directory: Search (preview)",
                rowsPath: "results_view",
                baseTruncated: result.IsTruncated,
                response: out var response)) {
            return Task.FromResult(ToolResultV2.Error("query_failed", "Failed to build AD search table view response."));
        }

        return Task.FromResult(response);
    }

    private static IReadOnlyList<string> BuildAvailableProjectionColumns(IReadOnlyList<LdapToolQueryRow> rows) {
        var availableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            }
        }

        return availableColumns.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
