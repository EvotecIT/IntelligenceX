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
/// Returns deny-logon-rights posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdDenyLogonRightsPolicyTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_deny_logon_rights_policy",
        "Assess deny-logon-rights assignments and attribution for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdDenyLogonRightsPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int AssignmentCount,
        int Scanned,
        bool Truncated,
        IReadOnlyList<DenyLogonRightsPolicyService.Item> Assignments,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDenyLogonRightsPolicyTool"/> class.
    /// </summary>
    public AdDenyLogonRightsPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool<DenyLogonRightsPolicyService.View, AdDenyLogonRightsPolicyResult>(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Deny Logon Rights Policy (preview)",
            defaultErrorMessage: "Deny-logon-rights policy query failed.",
            query: static domainName => DenyLogonRightsPolicyService.Get(domainName),
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdDenyLogonRightsPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                AssignmentCount: view.Assignments.Count,
                Scanned: scanned,
                Truncated: truncated,
                Assignments: view.Assignments,
                Attribution: rows));
    }
}

