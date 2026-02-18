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
/// Returns SeEnableDelegationPrivilege policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdEnableDelegationPrivilegePolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_enable_delegation_privilege_policy",
        "Assess SeEnableDelegationPrivilege assignment and attribution for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdEnableDelegationPrivilegePolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int AssignedCount,
        int Scanned,
        bool Truncated,
        IReadOnlyList<string> Assigned,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdEnableDelegationPrivilegePolicyTool"/> class.
    /// </summary>
    public AdEnableDelegationPrivilegePolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

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

        if (!TryExecute(
                action: () => EnableDelegationPrivilegePolicyService.Get(domainName),
                result: out EnableDelegationPrivilegePolicyService.View view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Enable-delegation-privilege policy query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var rows = PreparePolicyAttributionRows(
            attribution: view.Attribution,
            includeAttribution: includeAttribution,
            configuredAttributionOnly: configuredAttributionOnly,
            maxResults: maxResults,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdEnableDelegationPrivilegePolicyResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            AssignedCount: view.Assigned.Count,
            Scanned: scanned,
            Truncated: truncated,
            Assigned: view.Assigned,
            Attribution: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: Enable Delegation Privilege Policy (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddStandardPolicyAttributionMeta(meta, domainName, includeAttribution, configuredAttributionOnly, maxResults);
            }));
    }
}


