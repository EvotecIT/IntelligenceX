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
/// Searches Active Directory objects by servicePrincipalName (SPN) (read-only).
/// </summary>
public sealed class AdSpnSearchTool : ActiveDirectoryToolBase, ITool {
    private sealed record SpnSearchRequest(
        string? SpnContains,
        string? SpnExact,
        string? Kind,
        bool EnabledOnly,
        int MaxValuesPerAttribute,
        IReadOnlyList<string> Attributes);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<SpnSearchRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var spnContains = reader.OptionalString("spn_contains");
            var spnExact = reader.OptionalString("spn_exact");
            if (!string.IsNullOrWhiteSpace(spnContains) && !string.IsNullOrWhiteSpace(spnExact)) {
                return ToolRequestBindingResult<SpnSearchRequest>.Failure("spn_contains and spn_exact are mutually exclusive.");
            }

            return ToolRequestBindingResult<SpnSearchRequest>.Success(new SpnSearchRequest(
                SpnContains: spnContains,
                SpnExact: spnExact,
                Kind: reader.OptionalString("kind"),
                EnabledOnly: reader.Boolean("enabled_only"),
                MaxValuesPerAttribute: reader.CappedInt32(
                    "max_values_per_attribute",
                    LdapQueryPolicy.DefaultMaxValuesPerAttribute,
                    1,
                    LdapQueryPolicy.MaxValuesPerAttributeCap),
                Attributes: reader.StringArray("attributes")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<SpnSearchRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "any" : request.Kind.Trim().ToLowerInvariant();
        var maxResults = ResolveMaxResults(context.Arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        if (!LdapToolSpnSearchService.TryExecute(
                request: new LdapToolSpnSearchQueryRequest {
                    SpnContains = request.SpnContains,
                    SpnExact = request.SpnExact,
                    Kind = kind,
                    EnabledOnly = request.EnabledOnly,
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

        if (!AdDynamicTableView.TryBuildResponseFromQueryRows(
                arguments: context.Arguments,
                model: root,
                rows: result.Results,
                title: "Active Directory: SPN Search (preview)",
                rowsPath: "results_view",
                baseTruncated: result.IsTruncated,
                response: out var response)) {
            return Task.FromResult(ToolResultV2.Error("query_failed", "Failed to build SPN search table view response."));
        }

        return Task.FromResult(response);
    }
}
