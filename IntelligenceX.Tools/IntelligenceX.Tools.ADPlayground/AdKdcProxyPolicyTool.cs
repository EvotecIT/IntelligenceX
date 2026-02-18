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
/// Returns KDC proxy policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdKdcProxyPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_kdc_proxy_policy",
        "Assess KDC proxy policy posture (mapping enabled and configured values) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdKdcProxyPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        string TargetDn,
        bool EnabledPolicy,
        string? AttributionTopWriters,
        IReadOnlyList<object> Values,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdKdcProxyPolicyTool"/> class.
    /// </summary>
    public AdKdcProxyPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

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

        var view = KdcProxyPolicyService.Get(domainName);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "KDC proxy policy query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        var rows = PreparePolicyAttributionRows(
            attribution: view.Attribution,
            includeAttribution: includeAttribution,
            configuredAttributionOnly: configuredAttributionOnly,
            maxResults: maxResults,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdKdcProxyPolicyResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            Scanned: scanned,
            Truncated: truncated,
            TargetDn: view.TargetDn,
            EnabledPolicy: view.EnabledPolicy,
            AttributionTopWriters: view.AttributionTopWriters,
            Values: view.Values,
            Attribution: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: KDC Proxy Policy (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddStandardPolicyAttributionMeta(meta, domainName, includeAttribution, configuredAttributionOnly, maxResults);
            }));
    }
}
