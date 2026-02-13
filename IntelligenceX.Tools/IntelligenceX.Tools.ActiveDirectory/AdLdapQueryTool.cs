using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Executes a read-only LDAP query and returns structured JSON results with safety caps.
/// </summary>
public sealed class AdLdapQueryTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_ldap_query",
        "Run a read-only LDAP query (filter/scope/base DN) and return matching objects (capped).",
        ToolSchema.Object(
                ("ldap_filter", ToolSchema.String("LDAP filter, e.g. '(objectClass=group)'.")),
                ("scope", ToolSchema.String("Search scope.").Enum("subtree", "onelevel", "base")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Attributes to include. If omitted, returns a small safe default set.")),
                ("allow_sensitive_attributes", ToolSchema.Boolean("When true, allows requesting sensitive attributes (not recommended). Default false.")),
                ("max_attributes", ToolSchema.Integer("Maximum attributes to include (capped). Default 20.")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")),
                ("max_results", ToolSchema.Integer("Maximum results to return (capped).")))
            .WithTableViewOptions()
            .Required("ldap_filter")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLdapQueryTool"/> class.
    /// </summary>
    public AdLdapQueryTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = ToolArgs.GetTrimmedOrDefault(arguments, "ldap_filter", string.Empty);
        var scope = ToolArgs.GetOptionalTrimmed(arguments, "scope");
        var allowSensitive = ToolArgs.GetBoolean(arguments, "allow_sensitive_attributes");

        var requestedMaxAttrs = arguments?.GetInt64("max_attributes");
        var maxAttributes = requestedMaxAttrs.HasValue && requestedMaxAttrs.Value > 0
            ? (int)Math.Min(requestedMaxAttrs.Value, LdapQueryPolicy.MaxAttributesCap)
            : LdapQueryPolicy.DefaultMaxAttributes;

        var requestedMaxValues = arguments?.GetInt64("max_values_per_attribute");
        var maxValues = requestedMaxValues.HasValue && requestedMaxValues.Value > 0
            ? (int)Math.Min(requestedMaxValues.Value, LdapQueryPolicy.MaxValuesPerAttributeCap)
            : LdapQueryPolicy.DefaultMaxValuesPerAttribute;

        var requestedMax = arguments?.GetInt64("max_results");
        var maxResults = requestedMax.HasValue && requestedMax.Value > 0
            ? (int)Math.Min(requestedMax.Value, Options.MaxResults)
            : Options.MaxResults;

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        if (!LdapToolAdLdapQueryService.TryExecute(
                request: new LdapToolAdLdapQueryRequest {
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    LdapFilter = filter,
                    Scope = scope,
                    RequestedAttributes = ToolArgs.ReadStringArray(arguments?.GetArray("attributes")),
                    AllowSensitiveAttributes = allowSensitive,
                    MaxAttributes = maxAttributes,
                    MaxValuesPerAttribute = maxValues,
                    MaxResults = maxResults
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = queryResult!;
        var root = new {
            result.DomainController,
            result.SearchBaseDn,
            result.LdapFilter,
            result.Scope,
            result.MaxResults,
            result.MaxAttributes,
            result.MaxValuesPerAttribute,
            result.Count,
            result.LimitReached,
            result.Attributes,
            result.Results
        };

        AdDynamicTableView.TryBuildResponseFromOutputRows(
            arguments: arguments,
            model: root,
            rows: result.Results,
            title: "Active Directory: LDAP Query (preview)",
            rowsPath: "results_view",
            baseTruncated: result.LimitReached,
            response: out var response);
        return Task.FromResult(response);
    }

}

