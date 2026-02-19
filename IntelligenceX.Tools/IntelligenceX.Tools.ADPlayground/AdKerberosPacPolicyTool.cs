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
/// Returns Kerberos PAC policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdKerberosPacPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_kerberos_pac_policy",
        "Assess Kerberos PAC policy posture (PacRequestorEnforcement and KrbtgtFullPacSignature) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdKerberosPacPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        string TargetDn,
        uint? PacRequestorEnforcement,
        uint? KrbtgtFullPacSignature,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdKerberosPacPolicyTool"/> class.
    /// </summary>
    public AdKerberosPacPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Kerberos PAC Policy (preview)",
            defaultErrorMessage: "Kerberos PAC policy query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = KerberosPacPolicyService.Get(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "Kerberos PAC policy query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdKerberosPacPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                TargetDn: view.TargetDn,
                PacRequestorEnforcement: view.PacRequestorEnforcement,
                KrbtgtFullPacSignature: view.KrbtgtFullPacSignature,
                Attribution: rows));
    }
}

