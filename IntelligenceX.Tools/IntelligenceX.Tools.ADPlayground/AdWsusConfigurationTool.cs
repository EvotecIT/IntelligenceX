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
/// Returns consolidated WSUS configuration posture for a domain (read-only).
/// </summary>
public sealed class AdWsusConfigurationTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_wsus_configuration",
        "Inspect effective WSUS endpoint, pinning, proxy, and hygiene posture with optional policy attribution rows (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("include_diagnostics", ToolSchema.Boolean("When true, include WSUS diagnostics text entries in output.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdWsusConfigurationResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        bool IncludeDiagnostics,
        int Scanned,
        bool Truncated,
        WsusEndpointsView Endpoints,
        WsusPinningView Pinning,
        WsusProxyView Proxy,
        WsusHygieneView Hygiene,
        string? AttributionTopWriters,
        IReadOnlyList<string> Diagnostics,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdWsusConfigurationTool"/> class.
    /// </summary>
    public AdWsusConfigurationTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var includeDiagnostics = ToolArgs.GetBoolean(arguments, "include_diagnostics", defaultValue: true);

        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: WSUS Configuration (preview)",
            defaultErrorMessage: "WSUS configuration query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = WsusConfigurationService.Get(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "WSUS configuration query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: (request, view, scanned, truncated, rows) => new AdWsusConfigurationResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                IncludeDiagnostics: includeDiagnostics,
                Scanned: scanned,
                Truncated: truncated,
                Endpoints: view.Endpoints,
                Pinning: view.Pinning,
                Proxy: view.Proxy,
                Hygiene: view.Hygiene,
                AttributionTopWriters: view.AttributionTopWriters,
                Diagnostics: includeDiagnostics
                    ? view.Diagnostics.ToArray()
                    : System.Array.Empty<string>(),
                Attribution: rows),
            additionalMetaMutate: (meta, _, view, _) => {
                meta.Add("include_diagnostics", includeDiagnostics);
                meta.Add("endpoint_count", view.Endpoints.All.Count);
                meta.Add("non_https_count", view.Endpoints.NonHttps.Count);
                meta.Add("pinning_disabled", view.Pinning.PinningDisabled);
                meta.Add("proxy_compliant", view.Proxy.Compliant);
                meta.Add("wsus_configured", view.Hygiene.WsUsConfigured);
                meta.Add("diagnostic_count", view.Diagnostics.Count);
            });
    }
}
