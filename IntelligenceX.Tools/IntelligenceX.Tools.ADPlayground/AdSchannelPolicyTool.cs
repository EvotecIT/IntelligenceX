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
/// Returns Schannel protocol policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdSchannelPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_schannel_policy",
        "Assess Schannel server protocol posture (SSL3/TLS1.0/1.1/1.2/1.3, cipher order, .NET strong crypto) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdSchannelPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        int? Ssl3Enabled,
        int? Tls10Enabled,
        int? Tls11Enabled,
        int? Tls12Enabled,
        int? Tls13Enabled,
        bool? CipherSuiteOrderConfigured,
        bool? DotNetStrongCrypto64,
        bool? DotNetStrongCrypto32,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSchannelPolicyTool"/> class.
    /// </summary>
    public AdSchannelPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Schannel Policy (preview)",
            defaultErrorMessage: "Schannel policy query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = SchannelPolicyService.Get(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "Schannel policy query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdSchannelPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                Ssl3Enabled: view.Ssl3Enabled,
                Tls10Enabled: view.Tls10Enabled,
                Tls11Enabled: view.Tls11Enabled,
                Tls12Enabled: view.Tls12Enabled,
                Tls13Enabled: view.Tls13Enabled,
                CipherSuiteOrderConfigured: view.CipherSuiteOrderConfigured,
                DotNetStrongCrypto64: view.DotNetStrongCrypto64,
                DotNetStrongCrypto32: view.DotNetStrongCrypto32,
                Attribution: rows)
            );
    }
}

