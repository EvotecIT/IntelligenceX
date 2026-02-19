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
/// Returns NetBT name-resolution policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdNameResolutionPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_name_resolution_policy",
        "Assess name-resolution policy posture (EnableLMHOSTS and NetBT NodeType) for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdNameResolutionPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        uint? EnableLmHostsLookup,
        uint? NetbtNodeType,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdNameResolutionPolicyTool"/> class.
    /// </summary>
    public AdNameResolutionPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Name Resolution Policy (preview)",
            defaultErrorMessage: "Name-resolution policy query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = NameResolutionPolicyService.Get(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "Name-resolution policy query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdNameResolutionPolicyResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                EnableLmHostsLookup: view.EnableLmHostsLookup,
                NetbtNodeType: view.NetbtNodeType,
                Attribution: rows)
            );
    }
}

