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
/// Executes a read-only LDAP query and returns structured JSON results with safety caps.
/// </summary>
public sealed class AdLdapQueryTool : ActiveDirectoryToolBase, ITool {
    private sealed record LdapQueryRequest(
        string LdapFilter,
        string? Scope,
        bool AllowSensitiveAttributes,
        int MaxAttributes,
        int MaxValuesPerAttribute,
        IReadOnlyList<string> RequestedAttributes);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<LdapQueryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("ldap_filter", out var ldapFilter, out var ldapFilterError)) {
                return ToolRequestBindingResult<LdapQueryRequest>.Failure(ldapFilterError);
            }

            var requestedMaxAttrs = reader.OptionalInt64("max_attributes");
            var maxAttributes = requestedMaxAttrs.HasValue && requestedMaxAttrs.Value > 0
                ? (int)Math.Min(requestedMaxAttrs.Value, LdapQueryPolicy.MaxAttributesCap)
                : LdapQueryPolicy.DefaultMaxAttributes;

            var requestedMaxValues = reader.OptionalInt64("max_values_per_attribute");
            var maxValues = requestedMaxValues.HasValue && requestedMaxValues.Value > 0
                ? (int)Math.Min(requestedMaxValues.Value, LdapQueryPolicy.MaxValuesPerAttributeCap)
                : LdapQueryPolicy.DefaultMaxValuesPerAttribute;

            return ToolRequestBindingResult<LdapQueryRequest>.Success(new LdapQueryRequest(
                LdapFilter: ldapFilter,
                Scope: reader.OptionalString("scope"),
                AllowSensitiveAttributes: reader.Boolean("allow_sensitive_attributes"),
                MaxAttributes: maxAttributes,
                MaxValuesPerAttribute: maxValues,
                RequestedAttributes: reader.StringArray("attributes")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<LdapQueryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var maxResults = ResolveMaxResults(context.Arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        if (!LdapToolAdLdapQueryService.TryExecute(
                request: new LdapToolAdLdapQueryRequest {
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    LdapFilter = request.LdapFilter,
                    Scope = request.Scope,
                    RequestedAttributes = request.RequestedAttributes,
                    AllowSensitiveAttributes = request.AllowSensitiveAttributes,
                    MaxAttributes = request.MaxAttributes,
                    MaxValuesPerAttribute = request.MaxValuesPerAttribute,
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

        if (!AdDynamicTableView.TryBuildResponseFromOutputRows(
                arguments: context.Arguments,
                model: root,
                rows: result.Results,
                title: "Active Directory: LDAP Query (preview)",
                rowsPath: "results_view",
                baseTruncated: result.LimitReached,
                response: out var response)) {
            return Task.FromResult(ToolResultV2.Error("query_failed", "Failed to build LDAP query table view response."));
        }

        return Task.FromResult(response);
    }

}
