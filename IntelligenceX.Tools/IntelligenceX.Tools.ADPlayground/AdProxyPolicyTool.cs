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
/// Returns proxy/WPAD policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdProxyPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

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
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var includeAttribution = ToolArgs.GetBoolean(arguments, "include_attribution", defaultValue: true);
        var configuredAttributionOnly = ToolArgs.GetBoolean(arguments, "configured_attribution_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        ProxyPolicyService.View view;
        try {
            view = ProxyPolicyService.Get(domainName);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Proxy policy query failed.", invalidOperationErrorCode: "query_failed"));
        }

        var attributionRows = includeAttribution
            ? view.Attribution
                .Where(row => !configuredAttributionOnly || !string.IsNullOrWhiteSpace(row.Effective) && !string.Equals(row.Effective, "Not configured", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<PolicyAttribution>();

        var scanned = attributionRows.Length;
        IReadOnlyList<PolicyAttribution> rows = scanned > maxResults
            ? attributionRows.Take(maxResults).ToArray()
            : attributionRows;
        var truncated = scanned > rows.Count;

        var result = new AdProxyPolicyResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            Scanned: scanned,
            Truncated: truncated,
            ProxySettingsPerUser: view.ProxySettingsPerUser,
            EnableAutoProxyResultCache: view.EnableAutoProxyResultCache,
            WinHttpAutoProxySvcStart: view.WinHttpAutoProxySvcStart,
            ExampleMachineWideProxy: view.Example_MachineWideProxy,
            ExampleAutoProxyCacheDisabled: view.Example_AutoProxyCacheDisabled,
            ExampleWinHttpAutoProxySvcDisabled: view.Example_WinHttpAutoProxySvcDisabled,
            Attribution: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: Proxy Policy (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("include_attribution", includeAttribution);
                meta.Add("configured_attribution_only", configuredAttributionOnly);
                meta.Add("max_results", maxResults);
            });
        return Task.FromResult(response);
    }
}

