using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Audits delegation-related settings for AD user/computer accounts (read-only).
/// </summary>
public sealed class AdDelegationAuditTool : ActiveDirectoryToolBase, ITool {
    private const int MaxValuesPerAttributeCap = 200;
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_delegation_audit",
        "Audit delegation-related settings (unconstrained, protocol transition, constrained) for AD users/computers (read-only).",
        ToolSchema.Object(
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "computer")),
                ("enabled_only", ToolSchema.Boolean("When true, filter out disabled accounts. Default false.")),
                ("include_spns", ToolSchema.Boolean("When true, include (capped) servicePrincipalName values per object. Default false.")),
                ("include_allowed_to_delegate_to", ToolSchema.Boolean("When true, include (capped) msDS-AllowedToDelegateTo values per object. Default false.")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum objects to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDelegationAuditTool"/> class.
    /// </summary>
    public AdDelegationAuditTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var kindArg = ToolArgs.GetOptionalTrimmed(arguments, "kind");
        var kind = string.IsNullOrWhiteSpace(kindArg) ? "any" : kindArg.Trim().ToLowerInvariant();
        var enabledOnly = ToolArgs.GetBoolean(arguments, "enabled_only");
        var includeSpns = ToolArgs.GetBoolean(arguments, "include_spns");
        var includeAllowed = ToolArgs.GetBoolean(arguments, "include_allowed_to_delegate_to");

        var requestedMaxValues = arguments?.GetInt64("max_values_per_attribute");
        var maxValues = requestedMaxValues.HasValue && requestedMaxValues.Value > 0
            ? (int)Math.Min(requestedMaxValues.Value, MaxValuesPerAttributeCap)
            : 50;

        var requestedMax = arguments?.GetInt64("max_results");
        var maxResults = requestedMax.HasValue && requestedMax.Value > 0
            ? (int)Math.Min(requestedMax.Value, Options.MaxResults)
            : Options.MaxResults;

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        if (!LdapToolDelegationAuditService.TryExecute(
                request: new LdapToolDelegationAuditQueryRequest {
                    Kind = kind,
                    EnabledOnly = enabledOnly,
                    IncludeSpns = includeSpns,
                    IncludeAllowedToDelegateTo = includeAllowed,
                    MaxValuesPerAttribute = maxValues,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxResults = maxResults
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = queryResult!;
        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: result.Results,
            viewRowsPath: "results_view",
            title: "Active Directory: Delegation Audit (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.IsTruncated,
            response: out var response);
        return Task.FromResult(response);
    }
}
