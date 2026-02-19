using System;
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

        var maxResults = ResolveBoundedMaxResults(arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);

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

        AdDynamicTableView.TryBuildResponseFromQueryRows(
            arguments: arguments,
            model: root,
            rows: result.Results,
            title: "Active Directory: Search (preview)",
            rowsPath: "results_view",
            baseTruncated: result.IsTruncated,
            response: out var response);
        return Task.FromResult(response);
    }
}

