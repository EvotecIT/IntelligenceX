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
/// Returns LLMNR policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdLlmnrPolicyTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_llmnr_policy",
        "Assess LLMNR policy posture (EnableMulticast) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdLlmnrPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        uint? EffectiveValue,
        bool LlmnrDisabled,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLlmnrPolicyTool"/> class.
    /// </summary>
    public AdLlmnrPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool<LlmnrPolicyService.View, AdLlmnrPolicyResult>(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: LLMNR Policy (preview)",
            defaultErrorMessage: "LLMNR policy query failed.",
            query: static domainName => LlmnrPolicyService.Get(domainName),
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdLlmnrPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                EffectiveValue: view.EffectiveValue,
                LlmnrDisabled: view.LlmnrDisabled,
                Attribution: rows));
    }
}

