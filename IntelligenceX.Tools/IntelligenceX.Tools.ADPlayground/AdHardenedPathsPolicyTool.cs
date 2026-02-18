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
/// Returns Hardened UNC paths policy posture for Domain Controllers OU in one domain (read-only).
/// </summary>
public sealed class AdHardenedPathsPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_hardened_paths_policy",
        "Assess Hardened UNC path policy posture for SYSVOL/NETLOGON in Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdHardenedPathsPolicyResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        string TargetDn,
        string? SysvolValue,
        string? NetlogonValue,
        IReadOnlyList<string> MissingPaths,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdHardenedPathsPolicyTool"/> class.
    /// </summary>
    public AdHardenedPathsPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

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

        var view = HardenedPathsPolicyService.Get(domainName);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "Hardened UNC paths policy query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
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

        var result = new AdHardenedPathsPolicyResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            Scanned: scanned,
            Truncated: truncated,
            TargetDn: view.TargetDn,
            SysvolValue: view.SysvolValue,
            NetlogonValue: view.NetlogonValue,
            MissingPaths: view.MissingPaths,
            Attribution: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: Hardened UNC Paths Policy (preview)",
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
