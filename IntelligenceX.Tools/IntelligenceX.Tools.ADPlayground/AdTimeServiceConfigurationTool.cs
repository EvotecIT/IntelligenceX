using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns Windows Time Service (W32Time) posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdTimeServiceConfigurationTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_time_service_configuration",
        "Assess Windows Time Service policy posture (PDC and non-PDC) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdTimeServiceConfigurationResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        bool PdcAcceptable,
        bool NonPdcAcceptable,
        TimeServiceEffectivePolicyService.TimeServiceEffectivePolicyView? Pdc,
        TimeServiceEffectivePolicyService.TimeServiceEffectivePolicyView? NonPdc,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdTimeServiceConfigurationTool"/> class.
    /// </summary>
    public AdTimeServiceConfigurationTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Time Service Configuration (preview)",
            defaultErrorMessage: "Time service configuration query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = TimeServiceConfigurationService.Get(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "Time service configuration query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdTimeServiceConfigurationResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                PdcAcceptable: view.PdcAcceptable,
                NonPdcAcceptable: view.NonPdcAcceptable,
                Pdc: view.Pdc,
                NonPdc: view.NonPdc,
                Attribution: rows),
            additionalMetaMutate: static (meta, _, view, _) => {
                meta.Add("pdc_acceptable", view.PdcAcceptable);
                meta.Add("non_pdc_acceptable", view.NonPdcAcceptable);
            }
            );
    }
}

