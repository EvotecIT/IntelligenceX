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
/// Returns proxy/WPAD policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdProxyPolicyTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_proxy_policy",
        "Assess proxy/WPAD policy posture (machine-wide proxy, auto-proxy cache, WinHttpAutoProxySvc) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdProxyPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        uint? ProxySettingsPerUser,
        uint? EnableAutoProxyResultCache,
        uint? WinHttpAutoProxySvcStart,
        bool ExampleMachineWideProxy,
        bool ExampleAutoProxyCacheDisabled,
        bool ExampleWinHttpAutoProxySvcDisabled,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdProxyPolicyTool"/> class.
    /// </summary>
    public AdProxyPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool<ProxyPolicyService.View, AdProxyPolicyResult>(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Proxy Policy (preview)",
            defaultErrorMessage: "Proxy policy query failed.",
            query: static domainName => ProxyPolicyService.Get(domainName),
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdProxyPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                ProxySettingsPerUser: view.ProxySettingsPerUser,
                EnableAutoProxyResultCache: view.EnableAutoProxyResultCache,
                WinHttpAutoProxySvcStart: view.WinHttpAutoProxySvcStart,
                ExampleMachineWideProxy: view.Example_MachineWideProxy,
                ExampleAutoProxyCacheDisabled: view.Example_AutoProxyCacheDisabled,
                ExampleWinHttpAutoProxySvcDisabled: view.Example_WinHttpAutoProxySvcDisabled,
                Attribution: rows));
    }
}

