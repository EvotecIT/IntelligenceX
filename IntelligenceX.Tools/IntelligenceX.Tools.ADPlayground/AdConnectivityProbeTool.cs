using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Runs a lightweight ADPlayground preflight before broader Active Directory discovery or monitoring work.
/// </summary>
public sealed class AdConnectivityProbeTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMaxDomainControllers = 10;
    private const int MaxDomainControllersCap = 50;

    private sealed record ProbeRequest(
        string? DomainController,
        string? SearchBaseDn,
        bool IncludeDomainControllers,
        int MaxDomainControllers);

    private sealed record ProbeResultModel(
        string ProbeStatus,
        string EffectiveDomainController,
        string EffectiveSearchBaseDn,
        string DomainControllerSource,
        string SearchBaseDnSource,
        bool RootDseProbeSucceeded,
        string DnsDomainName,
        string ForestDnsName,
        int DomainControllerSampleCount,
        IReadOnlyList<string> DomainControllerSample,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> RecommendedFollowUpTools);

    private static readonly ToolPipelineReliabilityOptions ReliabilityOptions =
        ToolPipelineReliabilityProfiles.FastNetworkProbeWith(static options => {
            options.CircuitKey = "ad_connectivity_probe";
        });

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_connectivity_probe",
        "Run a lightweight Active Directory preflight to confirm RootDSE/context reachability before broader discovery, monitoring, or LDAP queries.",
        ToolSchema.Object(
                ("domain_controller", ToolSchema.String("Optional domain controller override (host/FQDN).")),
                ("search_base_dn", ToolSchema.String("Optional search base DN override.")),
                ("include_domain_controllers", ToolSchema.Boolean("When true, include a small discovered domain-controller sample. Default true.")),
                ("max_domain_controllers", ToolSchema.Integer("Maximum discovered domain controllers returned (capped). Default 10.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdConnectivityProbeTool"/> class.
    /// </summary>
    public AdConnectivityProbeTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync,
            reliability: ReliabilityOptions).ConfigureAwait(false);
    }

    private static ToolRequestBindingResult<ProbeRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => ToolRequestBindingResult<ProbeRequest>.Success(new ProbeRequest(
            DomainController: reader.OptionalString("domain_controller"),
            SearchBaseDn: reader.OptionalString("search_base_dn"),
            IncludeDomainControllers: reader.Boolean("include_domain_controllers", defaultValue: true),
            MaxDomainControllers: reader.CappedInt32(
                "max_domain_controllers",
                DefaultMaxDomainControllers,
                1,
                MaxDomainControllersCap))));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ProbeRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        LdapToolContextHelper.LdapSearchContext searchContext;
        try {
            searchContext = LdapToolContextHelper.ResolveSearchContext(
                explicitDomainController: context.Request.DomainController,
                explicitBaseDn: context.Request.SearchBaseDn,
                defaultDomainController: ToolArgs.NormalizeOptional(Options.DomainController),
                defaultBaseDn: ToolArgs.NormalizeOptional(Options.DefaultSearchBaseDn),
                cancellationToken: cancellationToken);
        } catch (Exception ex) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "probe_failed",
                error: SanitizeErrorMessage(ex.Message, "Active Directory context probe failed."),
                hints: new[] {
                    "Provide domain_controller and/or search_base_dn when auto-discovery cannot resolve Active Directory context.",
                    "Use ad_environment_discover to bootstrap domain controller, naming context, and forest/domain hints before deeper AD work.",
                    "Verify this runtime can reach an Active Directory domain or a specific domain controller."
                },
                isTransient: false));
        }

        DomainInfoQueryResult info;
        try {
            info = DomainInfoService.Query(searchContext.DomainController, cancellationToken);
        } catch (Exception ex) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "probe_failed",
                error: SanitizeErrorMessage(ex.Message, "Active Directory RootDSE probe failed."),
                hints: new[] {
                    "Verify LDAP connectivity to the effective or requested domain controller.",
                    "Use ad_environment_discover if you need to confirm the effective domain controller and naming context first.",
                    "Retry with an explicit domain_controller when the default endpoint is unreachable or ambiguous."
                },
                isTransient: true));
        }

        var warnings = new List<string>();
        var dnsDomainName = ToolArgs.NormalizeOptional(info.DnsDomainName);
        if (dnsDomainName is null && TryExtractDomainNameFromDistinguishedName(searchContext.BaseDn, out var derivedDomainName)) {
            dnsDomainName = derivedDomainName;
        }

        var forestDnsName = ToolArgs.NormalizeOptional(info.ForestDnsName) ?? dnsDomainName;
        var domainControllers = Array.Empty<string>();
        if (context.Request.IncludeDomainControllers && !string.IsNullOrWhiteSpace(dnsDomainName)) {
            try {
                domainControllers = DomainHelper.EnumerateDomainControllers(dnsDomainName, cancellationToken: cancellationToken)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(context.Request.MaxDomainControllers)
                    .ToArray();
            } catch (Exception ex) {
                warnings.Add("domain_controller_discovery: " + SanitizeErrorMessage(ex.Message, "Domain controller discovery failed."));
            }
        }

        var recommendedFollowUpTools = domainControllers.Length > 0
            ? new[] { "ad_scope_discovery", "ad_domain_controller_facts", "ad_monitoring_probe_run" }
            : new[] { "ad_scope_discovery", "ad_search", "ad_object_resolve" };
        var probeStatus = warnings.Count == 0 ? "healthy" : "degraded";
        var result = new ProbeResultModel(
            ProbeStatus: probeStatus,
            EffectiveDomainController: searchContext.DomainController ?? string.Empty,
            EffectiveSearchBaseDn: searchContext.BaseDn,
            DomainControllerSource: ToSourceName(searchContext.DomainControllerSource),
            SearchBaseDnSource: ToSourceName(searchContext.BaseDnSource),
            RootDseProbeSucceeded: true,
            DnsDomainName: dnsDomainName ?? string.Empty,
            ForestDnsName: forestDnsName ?? string.Empty,
            DomainControllerSampleCount: domainControllers.Length,
            DomainControllerSample: domainControllers,
            Warnings: warnings,
            RecommendedFollowUpTools: recommendedFollowUpTools);

        var facts = new List<(string Key, string Value)> {
            ("Probe status", probeStatus),
            ("Effective domain controller", searchContext.DomainController ?? string.Empty),
            ("Effective search base DN", searchContext.BaseDn),
            ("DNS domain", dnsDomainName ?? string.Empty),
            ("Forest", forestDnsName ?? string.Empty),
            ("RootDSE probe", "ok"),
            ("Discovered DC sample", domainControllers.Length.ToString(CultureInfo.InvariantCulture)),
            ("Domain controller source", result.DomainControllerSource),
            ("Search base source", result.SearchBaseDnSource)
        };
        if (domainControllers.Length > 0) {
            facts.Add(("Domain controller sample", string.Join(", ", domainControllers)));
        }

        var meta = ToolOutputHints.Meta(count: Math.Max(1, domainControllers.Length), truncated: false)
            .Add("probe_status", probeStatus)
            .Add("effective_domain_controller", searchContext.DomainController ?? string.Empty)
            .Add("effective_search_base_dn", searchContext.BaseDn)
            .Add("dns_domain_name", dnsDomainName ?? string.Empty)
            .Add("forest_dns_name", forestDnsName ?? string.Empty)
            .Add("rootdse_probe_succeeded", true)
            .Add("domain_controller_source", result.DomainControllerSource)
            .Add("search_base_dn_source", result.SearchBaseDnSource)
            .Add("domain_controller_sample_count", domainControllers.Length)
            .Add("recommended_follow_up_tools", ToolJson.ToJsonArray(recommendedFollowUpTools));

        return Task.FromResult(ToolResultV2.OkFactsModel(
            model: result,
            title: "Active Directory connectivity probe",
            facts: facts,
            meta: meta));
    }

    private static bool TryExtractDomainNameFromDistinguishedName(string? distinguishedName, out string domainName) {
        domainName = string.Empty;
        var dn = (distinguishedName ?? string.Empty).Trim();
        if (dn.Length == 0) {
            return false;
        }

        var labels = new List<string>();
        var segments = dn.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++) {
            var segment = segments[i].Trim();
            if (!segment.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var label = segment.Substring(3).Trim();
            if (label.Length == 0) {
                continue;
            }

            labels.Add(label);
        }

        if (labels.Count == 0) {
            return false;
        }

        domainName = string.Join(".", labels);
        return domainName.Length > 0;
    }

    private static string ToSourceName(LdapToolContextHelper.ContextValueSource source) {
        return source switch {
            LdapToolContextHelper.ContextValueSource.ExplicitArgument => "explicit_argument",
            LdapToolContextHelper.ContextValueSource.ToolDefault => "tool_default",
            LdapToolContextHelper.ContextValueSource.RootDse => "root_dse",
            LdapToolContextHelper.ContextValueSource.DerivedHint => "derived_hint",
            _ => "unspecified"
        };
    }
}
