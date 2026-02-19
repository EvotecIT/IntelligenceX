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
        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Firewall Profiles (preview)",
            defaultErrorMessage: "Firewall profile query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = FirewallProfilesService.Get(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "Firewall profile query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdFirewallProfilesResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
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
                Attribution: rows)
            );
    }
}

