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
/// Returns RDS redirection policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdTerminalServicesRedirectionPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_terminal_services_redirection_policy",
        "Assess terminal services redirection policy (drive/clipboard redirection) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdTerminalServicesRedirectionPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        bool? DisableDriveRedirect,
        bool? DisableClipboardRedirect,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdTerminalServicesRedirectionPolicyTool"/> class.
    /// </summary>
    public AdTerminalServicesRedirectionPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Terminal Services Redirection Policy (preview)",
            defaultErrorMessage: "Terminal services redirection policy query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = TerminalServicesRedirectionPolicyService.Get(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "Terminal services redirection policy query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdTerminalServicesRedirectionPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                DisableDriveRedirect: view.DisableDriveRedirect,
                DisableClipboardRedirect: view.DisableClipboardRedirect,
                Attribution: rows)
            );
    }
}

