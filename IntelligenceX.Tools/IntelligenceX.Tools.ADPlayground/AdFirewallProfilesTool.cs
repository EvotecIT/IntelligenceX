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
/// Returns Domain Controllers OU firewall profile posture and policy attribution for one domain (read-only).
/// </summary>
public sealed class AdFirewallProfilesTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_firewall_profiles",
        "Assess Domain/Private/Public firewall profile posture for Domain Controllers OU (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdFirewallProfilesResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        bool? DomainEnabled,
        bool? PrivateEnabled,
        bool? PublicEnabled,
        string? DomainDefaultInbound,
        string? DomainDefaultOutbound,
        string? PrivateDefaultInbound,
        string? PrivateDefaultOutbound,
        string? PublicDefaultInbound,
        string? PublicDefaultOutbound,
        bool? DomainNotificationsDisabled,
        bool? PrivateNotificationsDisabled,
        bool? PublicNotificationsDisabled,
        bool? DomainLogDroppedPackets,
        bool? PrivateLogDroppedPackets,
        bool? PublicLogDroppedPackets,
        bool? DomainLogSuccessfulConnections,
        bool? PrivateLogSuccessfulConnections,
        bool? PublicLogSuccessfulConnections,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdFirewallProfilesTool"/> class.
    /// </summary>
    public AdFirewallProfilesTool(ActiveDirectoryToolOptions options) : base(options) { }

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

        var view = FirewallProfilesService.Get(domainName);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "Firewall profile query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        var attributionRows = includeAttribution
            ? view.Attribution
                .Where(row => !configuredAttributionOnly || !string.IsNullOrWhiteSpace(row.Effective) && !string.Equals(row.Effective, "Not configured", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<PolicyAttribution>();

        var scanned = attributionRows.Length;
        IReadOnlyList<PolicyAttribution> projectedRows = scanned > maxResults
            ? attributionRows.Take(maxResults).ToArray()
            : attributionRows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdFirewallProfilesResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            Scanned: scanned,
            Truncated: truncated,
            DomainEnabled: view.DomainEnabled,
            PrivateEnabled: view.PrivateEnabled,
            PublicEnabled: view.PublicEnabled,
            DomainDefaultInbound: view.DomainDefaultInbound,
            DomainDefaultOutbound: view.DomainDefaultOutbound,
            PrivateDefaultInbound: view.PrivateDefaultInbound,
            PrivateDefaultOutbound: view.PrivateDefaultOutbound,
            PublicDefaultInbound: view.PublicDefaultInbound,
            PublicDefaultOutbound: view.PublicDefaultOutbound,
            DomainNotificationsDisabled: view.DomainNotificationsDisabled,
            PrivateNotificationsDisabled: view.PrivateNotificationsDisabled,
            PublicNotificationsDisabled: view.PublicNotificationsDisabled,
            DomainLogDroppedPackets: view.DomainLogDroppedPackets,
            PrivateLogDroppedPackets: view.PrivateLogDroppedPackets,
            PublicLogDroppedPackets: view.PublicLogDroppedPackets,
            DomainLogSuccessfulConnections: view.DomainLogSuccessfulConnections,
            PrivateLogSuccessfulConnections: view.PrivateLogSuccessfulConnections,
            PublicLogSuccessfulConnections: view.PublicLogSuccessfulConnections,
            Attribution: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: Firewall Profiles (preview)",
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
