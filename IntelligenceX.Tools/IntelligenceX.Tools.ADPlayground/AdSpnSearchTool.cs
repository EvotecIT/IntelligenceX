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
/// Searches Active Directory objects by servicePrincipalName (SPN) (read-only).
/// </summary>
public sealed class AdSpnSearchTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_spn_search",
        "Search Active Directory accounts by servicePrincipalName (SPN) (read-only). Useful for auditing Kerberos service exposure.",
        ToolSchema.Object(
                ("spn_contains", ToolSchema.String("Optional case-insensitive substring match (LDAP contains) for SPNs. When omitted, returns any object with an SPN.")),
                ("spn_exact", ToolSchema.String("Optional exact SPN match (mutually exclusive with spn_contains).")),
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "computer")),
                ("enabled_only", ToolSchema.Boolean("When true, filter out disabled accounts (userAccountControl bit 2). Default false.")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum results to return (capped).")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional attributes to include (engine policy enforced).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSpnSearchTool"/> class.
    /// </summary>
    public AdSpnSearchTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var spnContains = ToolArgs.GetOptionalTrimmed(arguments, "spn_contains");
        var spnExact = ToolArgs.GetOptionalTrimmed(arguments, "spn_exact");
        if (!string.IsNullOrWhiteSpace(spnContains) && !string.IsNullOrWhiteSpace(spnExact)) {
            return Task.FromResult(Error("invalid_argument", "spn_contains and spn_exact are mutually exclusive."));
        }

        var kindArg = ToolArgs.GetOptionalTrimmed(arguments, "kind");
        var kind = string.IsNullOrWhiteSpace(kindArg) ? "any" : kindArg.Trim().ToLowerInvariant();
        var enabledOnly = ToolArgs.GetBoolean(arguments, "enabled_only");

        var maxResults = ResolveBoundedMaxResults(arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);

        var maxValuesPerAttribute = ToolArgs.GetCappedInt32(
            arguments,
            "max_values_per_attribute",
            LdapQueryPolicy.DefaultMaxValuesPerAttribute,
            1,
            LdapQueryPolicy.MaxValuesPerAttributeCap);

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        var attributes = ToolArgs.ReadStringArray(arguments?.GetArray("attributes"));

        if (!LdapToolSpnSearchService.TryExecute(
                request: new LdapToolSpnSearchQueryRequest {
                    SpnContains = spnContains,
                    SpnExact = spnExact,
                    Kind = kind,
                    EnabledOnly = enabledOnly,
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
            result.Kind,
            result.EnabledOnly,
            result.DomainController,
            result.SearchBaseDn,
            result.LdapFilter,
            result.MaxResults,
            result.MaxValuesPerAttribute,
            result.Count,
            result.IsTruncated,
            result.SpnContains,
            result.SpnExact,
            result.Results
        };

        AdDynamicTableView.TryBuildResponseFromQueryRows(
            arguments: arguments,
            model: root,
            rows: result.Results,
            title: "Active Directory: SPN Search (preview)",
            rowsPath: "results_view",
            baseTruncated: result.IsTruncated,
            response: out var response);
        return Task.FromResult(response);
    }
}

