using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns SeEnableDelegationPrivilege policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdEnableDelegationPrivilegePolicyTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_enable_delegation_privilege_policy",
        "Assess SeEnableDelegationPrivilege assignment and attribution for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdEnableDelegationPrivilegePolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int AssignedCount,
        int Scanned,
        bool Truncated,
        IReadOnlyList<string> Assigned,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdEnableDelegationPrivilegePolicyTool"/> class.
    /// </summary>
    public AdEnableDelegationPrivilegePolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool<EnableDelegationPrivilegePolicyService.View, AdEnableDelegationPrivilegePolicyResult>(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Enable Delegation Privilege Policy (preview)",
            defaultErrorMessage: "Enable-delegation-privilege policy query failed.",
            query: static domainName => EnableDelegationPrivilegePolicyService.Get(domainName),
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdEnableDelegationPrivilegePolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                AssignedCount: view.Assigned.Count,
                Scanned: scanned,
                Truncated: truncated,
                Assigned: view.Assigned,
                Attribution: rows));
    }
}

