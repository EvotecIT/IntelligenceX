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
/// Returns Restrict NTLM policy values for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdNtlmRestrictionsPolicyTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_ntlm_restrictions_policy",
        "Assess Restrict NTLM policy values (incoming/outgoing traffic controls) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdNtlmRestrictionsPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        string TargetDn,
        uint? RestrictSending,
        uint? RestrictReceiving,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdNtlmRestrictionsPolicyTool"/> class.
    /// </summary>
    public AdNtlmRestrictionsPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool<NtlmRestrictionsPolicyService.View, AdNtlmRestrictionsPolicyResult>(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: NTLM Restrictions Policy (preview)",
            defaultErrorMessage: "NTLM restrictions policy query failed.",
            query: static domainName => NtlmRestrictionsPolicyService.Get(domainName),
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdNtlmRestrictionsPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                TargetDn: view.TargetDn,
                RestrictSending: view.RestrictSending,
                RestrictReceiving: view.RestrictReceiving,
                Attribution: rows));
    }
}

