using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using ADPlayground.Monitoring.Probes;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Discovers forest scope (forest/domains/trusts/domain controllers) and emits a receipt describing what was discovered and how.
/// </summary>
public sealed partial class AdForestDiscoverTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMaxDomains = 250;
    private const int MaxDomainsCap = 5000;
    private const int DefaultMaxDomainControllersTotal = 2000;
    private const int MaxDomainControllersTotalCap = 50_000;
    private const int DefaultMaxDomainControllersPerDomain = 200;
    private const int MaxDomainControllersPerDomainCap = 5000;
    private const int DefaultTrustTimeoutMs = 3000;
    private const int MaxTrustTimeoutMs = 30_000;
    private const int DefaultMaxTrusts = 2000;
    private const int MaxTrustsCap = 20_000;
    private const int DefaultRootDseTimeoutMs = 5000;
    private const int DefaultLdapTimeoutMs = 10_000;
    private const int DefaultDcSourceTimeoutMs = 5000;

    private static readonly IReadOnlyDictionary<string, DirectoryDiscoveryFallback> DiscoveryFallbackModes =
        new Dictionary<string, DirectoryDiscoveryFallback>(StringComparer.OrdinalIgnoreCase) {
            ["none"] = DirectoryDiscoveryFallback.None,
            ["current_domain"] = DirectoryDiscoveryFallback.CurrentDomain,
            ["current-domain"] = DirectoryDiscoveryFallback.CurrentDomain,
            ["currentdomain"] = DirectoryDiscoveryFallback.CurrentDomain,
            ["current_forest"] = DirectoryDiscoveryFallback.CurrentForest,
            ["current-forest"] = DirectoryDiscoveryFallback.CurrentForest,
            ["currentforest"] = DirectoryDiscoveryFallback.CurrentForest
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_forest_discover",
        "Discover effective AD forest scope (forest/domains/trusts/domain controllers) and return a discovery receipt (what was discovered and how).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name to enumerate (e.g. contoso.com).")),
                ("domain_name", ToolSchema.String("Optional DNS domain name to seed discovery (when forest_name is omitted).")),
                ("domain_controller", ToolSchema.String("Optional domain controller (host/FQDN) used for RootDSE context and LDAP-based discovery fallbacks.")),
                ("include_domains", ToolSchema.Array(ToolSchema.String(), "Optional include-domain filter for discovery.")),
                ("exclude_domains", ToolSchema.Array(ToolSchema.String(), "Optional exclude-domain filter for discovery.")),
                ("include_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional include-DC filter for discovery results.")),
                ("exclude_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional exclude-DC filter for discovery results.")),
                ("skip_rodc", ToolSchema.Boolean("When true, excludes RODCs from discovered domain controllers.")),
                ("include_trusts", ToolSchema.Boolean("When true, includes trusted forest domains during forest domain enumeration.")),
                ("discovery_fallback",
                    ToolSchema.String("Explicit discovery fallback policy when forest_name/domain_name are not provided.")
                        .Enum("none", "current_domain", "current_forest")),
                ("max_domains", ToolSchema.Integer("Maximum domains returned (capped). Default 250.")),
                ("max_domain_controllers_total", ToolSchema.Integer("Maximum domain controllers returned across the forest (capped). Default 2000.")),
                ("max_domain_controllers_per_domain", ToolSchema.Integer("Maximum domain controllers returned per domain (capped). Default 200.")),
                ("include_trust_relationships", ToolSchema.Boolean("When true, include forest trust relationships (best-effort). Default true.")),
                ("include_domain_trust_relationships", ToolSchema.Boolean("When true, include domain trust relationships for discovered domains (best-effort). Default false.")),
                ("trust_timeout_ms", ToolSchema.Integer("Timeout for trust enumeration (ms). Default 3000.")),
                ("max_trusts", ToolSchema.Integer("Maximum trust relationship rows returned (capped). Default 2000.")))
            .Required("discovery_fallback")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdForestDiscoverTool"/> class.
    /// </summary>
    public AdForestDiscoverTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var explicitForest = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var explicitDomain = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var explicitDomainController = ToolArgs.GetOptionalTrimmed(arguments, "domain_controller");

        var includeDomains = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("include_domains"));
        var excludeDomains = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("exclude_domains"));
        var includeDomainControllers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("include_domain_controllers"));
        var excludeDomainControllers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("exclude_domain_controllers"));
        var skipRodc = ToolArgs.GetBoolean(arguments, "skip_rodc", defaultValue: false);
        var includeTrustedDomains = ToolArgs.GetBoolean(arguments, "include_trusts", defaultValue: false);

        // Force explicitness: the caller must choose the fallback policy (even if they provide forest_name).
        var discoveryFallbackRaw = ToolArgs.GetOptionalTrimmed(arguments, "discovery_fallback");
        if (string.IsNullOrWhiteSpace(discoveryFallbackRaw)) {
            return Error("invalid_argument", "discovery_fallback is required (use: none/current_domain/current_forest).");
        }

        var discoveryFallbackNormalized = discoveryFallbackRaw.Trim();
        if (!DiscoveryFallbackModes.TryGetValue(discoveryFallbackNormalized, out var discoveryFallback)) {
            return Error(
                errorCode: "invalid_argument",
                error: "discovery_fallback must be one of: none, current_domain, current_forest.",
                hints: new[] { $"Received: '{discoveryFallbackNormalized}'." },
                isTransient: false);
        }

        var maxDomains = ToolArgs.GetCappedInt32(arguments, "max_domains", DefaultMaxDomains, 1, MaxDomainsCap);
        var maxDcsTotal = ToolArgs.GetCappedInt32(arguments, "max_domain_controllers_total", DefaultMaxDomainControllersTotal, 1, MaxDomainControllersTotalCap);
        var maxDcsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_domain_controllers_per_domain", DefaultMaxDomainControllersPerDomain, 1, MaxDomainControllersPerDomainCap);
        var includeTrustRelationships = ToolArgs.GetBoolean(arguments, "include_trust_relationships", defaultValue: true);
        var includeDomainTrustRelationships = ToolArgs.GetBoolean(arguments, "include_domain_trust_relationships", defaultValue: false);
        var trustTimeoutMs = ToolArgs.GetCappedInt32(arguments, "trust_timeout_ms", DefaultTrustTimeoutMs, 500, MaxTrustTimeoutMs);
        var maxTrusts = ToolArgs.GetCappedInt32(arguments, "max_trusts", DefaultMaxTrusts, 0, MaxTrustsCap);

        var discovery = await AdForestDiscoveryService.DiscoverAsync(
            new AdForestDiscoveryService.ForestDiscoveryRequest(
                ForestName: explicitForest,
                DomainName: explicitDomain,
                DomainController: explicitDomainController,
                IncludeDomains: includeDomains,
                ExcludeDomains: excludeDomains,
                IncludeDomainControllers: includeDomainControllers,
                ExcludeDomainControllers: excludeDomainControllers,
                SkipRodc: skipRodc,
                IncludeTrustedDomains: includeTrustedDomains,
                DiscoveryFallback: discoveryFallback switch {
                    DirectoryDiscoveryFallback.CurrentForest => AdForestDiscoveryService.ForestDiscoveryFallback.CurrentForest,
                    DirectoryDiscoveryFallback.CurrentDomain => AdForestDiscoveryService.ForestDiscoveryFallback.CurrentDomain,
                    _ => AdForestDiscoveryService.ForestDiscoveryFallback.None
                },
                MaxDomains: maxDomains,
                MaxDomainControllersTotal: maxDcsTotal,
                MaxDomainControllersPerDomain: maxDcsPerDomain,
                MaxTrusts: maxTrusts,
                IncludeTrustRelationships: includeTrustRelationships,
                IncludeDomainTrustRelationships: includeDomainTrustRelationships,
                TrustTimeoutMs: trustTimeoutMs,
                RootDseTimeoutMs: DefaultRootDseTimeoutMs,
                LdapTimeoutMs: DefaultLdapTimeoutMs,
                DcSourceTimeoutMs: DefaultDcSourceTimeoutMs),
            cancellationToken).ConfigureAwait(false);

        var effectiveForest = discovery.EffectiveForestName;
        var effectiveDomain = discovery.EffectiveDomainName;
        var domains = discovery.Domains;
        var allDcs = discovery.DomainControllers;
        var trusts = discovery.Trusts
            .Select(static trust => (object)new {
                scope = trust.Scope,
                source_name = trust.SourceName,
                target_name = trust.TargetName,
                trust_type = trust.TrustType,
                trust_direction = trust.TrustDirection
            })
            .ToList();
        var dcByDomain = discovery.DomainControllersByDomain
            .Select(static row => (object)new {
                domain_name = row.DomainName,
                domain_controllers = row.DomainControllers,
                receipt = row.Receipt.Select(ToDiscoveryStep).ToArray(),
                domain_controller_count = row.DomainControllerCount
            })
            .ToList();
        var receipt = discovery.Steps
            .Select(static step => (object)ToDiscoveryStep(step))
            .ToList();

        if (string.IsNullOrWhiteSpace(effectiveForest) &&
            string.IsNullOrWhiteSpace(effectiveDomain) &&
            discoveryFallback == DirectoryDiscoveryFallback.None &&
            includeDomains.Count == 0) {
            return Error(
                errorCode: "invalid_argument",
                error: "Forest/domain scope is missing. Provide forest_name, domain_name, include_domains, or set discovery_fallback to current_forest/current_domain.",
                hints: new[] {
                    "For a true forest-wide discovery, provide forest_name or set discovery_fallback=current_forest.",
                    "If you only know a DC, provide domain_controller to derive forest/domain via RootDSE."
                },
                isTransient: false);
        }

        var chain = BuildChainContract(
            discoveryFallback: discoveryFallback,
            effectiveForest: effectiveForest,
            effectiveDomain: effectiveDomain,
            domains: domains,
            domainControllers: allDcs,
            trusts: trusts,
            includeTrusts: includeTrustedDomains,
            rootDseOk: discovery.RootDseOk,
            domainsOk: discovery.DomainsOk,
            domainControllersOk: discovery.DomainControllersOk,
            trustsOk: discovery.TrustsOk);

        var model = new {
            RequestedScope = new {
                ForestName = explicitForest ?? string.Empty,
                DomainName = explicitDomain ?? string.Empty,
                DomainController = explicitDomainController ?? string.Empty,
                IncludeDomains = includeDomains.ToArray(),
                ExcludeDomains = excludeDomains.ToArray(),
                IncludeDomainControllers = includeDomainControllers.ToArray(),
                ExcludeDomainControllers = excludeDomainControllers.ToArray(),
                IncludeTrusts = includeTrustedDomains,
                SkipRodc = skipRodc,
                DiscoveryFallback = ToDiscoveryFallbackName(discoveryFallback),
                Limits = new {
                    MaxDomains = maxDomains,
                    MaxDomainControllersTotal = maxDcsTotal,
                    MaxDomainControllersPerDomain = maxDcsPerDomain,
                    MaxTrusts = maxTrusts
                }
            },
            EffectiveScope = new {
                ForestName = effectiveForest ?? string.Empty,
                DomainName = effectiveDomain ?? string.Empty
            },
            Domains = domains,
            DomainControllers = allDcs,
            DomainControllersByDomain = dcByDomain,
            Trusts = trusts,
            Receipt = new {
                Steps = receipt,
                Summary = new {
                    Domains = domains.Count,
                    DomainControllers = allDcs.Count,
                    Trusts = trusts.Count
                }
            },
            NextActions = chain.NextActions,
            Cursor = chain.Cursor,
            ResumeToken = chain.ResumeToken,
            FlowId = chain.FlowId,
            StepId = chain.StepId,
            Checkpoint = chain.Checkpoint,
            Handoff = chain.Handoff,
            Confidence = chain.Confidence
        };

        var summary = ToolMarkdown.SummaryText(
            title: "Active Directory: Forest Scope Discovery",
            $"Forest: `{effectiveForest ?? string.Empty}`; Domains: `{domains.Count}`; DCs: `{allDcs.Count}`; Trusts: `{trusts.Count}`.",
            "Receipt includes discovery steps and per-domain DC source attempts.");

        return ToolOutputEnvelope.OkFlatWithRenderValue(
            root: ToolJson.ToJsonObjectSnakeCase(model),
            summaryMarkdown: summary,
            render: BuildRenderHints(
                domainControllersByDomainCount: dcByDomain.Count,
                domainControllerCount: allDcs.Count,
                domainCount: domains.Count,
                trustCount: trusts.Count,
                receiptStepCount: receipt.Count,
                nextActionCount: chain.NextActions.Count));
    }

    private static JsonValue? BuildRenderHints(
        int domainControllersByDomainCount,
        int domainControllerCount,
        int domainCount,
        int trustCount,
        int receiptStepCount,
        int nextActionCount) {
        var hints = new JsonArray();

        if (domainControllersByDomainCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "domain_controllers_by_domain",
                    new ToolColumn("domain_name", "Domain", "string"),
                    new ToolColumn("domain_controller_count", "DC count", "int"))
                .Add("priority", 500));
        }

        if (domainControllerCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "domain_controllers",
                    new ToolColumn("value", "Domain controller", "string"))
                .Add("priority", 450));
        }

        if (domainCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "domains",
                    new ToolColumn("value", "Domain", "string"))
                .Add("priority", 400));
        }

        if (trustCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "trusts",
                    new ToolColumn("scope", "Scope", "string"),
                    new ToolColumn("source_name", "Source", "string"),
                    new ToolColumn("target_name", "Target", "string"),
                    new ToolColumn("trust_type", "Type", "string"),
                    new ToolColumn("trust_direction", "Direction", "string"))
                .Add("priority", 300));
        }

        if (receiptStepCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "receipt/steps",
                    new ToolColumn("name", "Step", "string"),
                    new ToolColumn("ok", "Ok", "bool"),
                    new ToolColumn("duration_ms", "Duration (ms)", "int"),
                    new ToolColumn("error_type", "Error type", "string"))
                .Add("priority", 200));
        }

        if (nextActionCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "next_actions",
                    new ToolColumn("tool", "Tool", "string"),
                    new ToolColumn("reason", "Reason", "string"),
                    new ToolColumn("mutating", "Mutating", "bool"))
                .Add("priority", 150));
        }

        if (hints.Count == 0) {
            return null;
        }

        return JsonValue.From(hints);
    }

    private static ToolChainContractModel BuildChainContract(
        DirectoryDiscoveryFallback discoveryFallback,
        string? effectiveForest,
        string? effectiveDomain,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> domainControllers,
        IReadOnlyList<object> trusts,
        bool includeTrusts,
        bool rootDseOk,
        bool domainsOk,
        bool domainControllersOk,
        bool trustsOk) {
        var fallbackName = ToDiscoveryFallbackName(discoveryFallback);
        var preserveForestScope = !string.IsNullOrWhiteSpace(effectiveForest)
                                  && (discoveryFallback == DirectoryDiscoveryFallback.CurrentForest
                                      || includeTrusts
                                      || domains.Count > 1
                                      || trusts.Count > 0);
        var chainedForestName = preserveForestScope ? effectiveForest ?? string.Empty : string.Empty;
        var chainedDomainName = preserveForestScope ? string.Empty : effectiveDomain ?? string.Empty;
        var handoff = ToolChainingHints.Map(
            ("contract", "ad_forest_discover_handoff"),
            ("version", 1),
            ("forest_name", effectiveForest ?? string.Empty),
            ("domain_name", effectiveDomain ?? string.Empty),
            ("discovery_fallback", fallbackName),
            ("domains_preview", string.Join(";", domains.Take(10))),
            ("domain_controllers_preview", string.Join(";", domainControllers.Take(15))),
            ("trusts_count", trusts.Count));

        var nextActions = new List<ToolNextActionModel> {
            ToolChainingHints.NextAction(
                tool: "ad_scope_discovery",
                reason: "Capture normalized naming contexts and per-domain probe receipts before deep AD follow-ups.",
                suggestedArguments: ToolChainingHints.Map(
                    ("forest_name", chainedForestName),
                    ("domain_name", chainedDomainName),
                    ("discovery_fallback", fallbackName),
                    ("include_trusts", includeTrusts)),
                mutating: false),
            ToolChainingHints.NextAction(
                tool: "ad_monitoring_probe_run",
                reason: "Run replication health probes across discovered scope.",
                suggestedArguments: ToolChainingHints.Map(
                    ("probe_kind", "replication"),
                    ("forest_name", chainedForestName),
                    ("domain_name", chainedDomainName),
                    ("discovery_fallback", fallbackName),
                    ("include_trusts", includeTrusts)),
                arguments: ToolChainingHints.MapObject(
                    ("probe_kind", "replication"),
                    ("forest_name", chainedForestName),
                    ("domain_name", chainedDomainName),
                    ("discovery_fallback", fallbackName),
                    ("include_trusts", includeTrusts),
                    ("include_domain_controllers", domainControllers.Where(static dc => !string.IsNullOrWhiteSpace(dc)).Take(50).ToArray())),
                mutating: false)
        };

        var firstDomain = domains.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstDomain)) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "ad_replication_connections",
                reason: "Inspect replication-connection details for one discovered domain.",
                suggestedArguments: ToolChainingHints.Map(("domain_name", firstDomain)),
                mutating: false));
        }

        if (domainControllers.Count <= 1) {
            if (discoveryFallback != DirectoryDiscoveryFallback.CurrentForest) {
                var sparseExpansionHasForestScope = !string.IsNullOrWhiteSpace(effectiveForest);
                var sparseExpansionForestName = sparseExpansionHasForestScope ? effectiveForest ?? string.Empty : string.Empty;
                var sparseExpansionDomainName = sparseExpansionHasForestScope ? string.Empty : effectiveDomain ?? string.Empty;
                var sparseExpansionFallback = sparseExpansionHasForestScope ? "current_forest" : fallbackName;
                nextActions.Add(ToolChainingHints.NextAction(
                    tool: "ad_forest_discover",
                    reason: "expand_scope_when_domain_controller_inventory_sparse",
                    suggestedArguments: ToolChainingHints.Map(
                        ("forest_name", sparseExpansionForestName),
                        ("domain_name", sparseExpansionDomainName),
                        ("discovery_fallback", sparseExpansionFallback),
                        ("include_trusts", true),
                        ("max_domains", 500),
                        ("max_domain_controllers_total", 5000),
                        ("max_domain_controllers_per_domain", 500)),
                    arguments: ToolChainingHints.MapObject(
                        ("forest_name", sparseExpansionForestName),
                        ("domain_name", sparseExpansionDomainName),
                        ("discovery_fallback", sparseExpansionFallback),
                        ("include_trusts", true),
                        ("max_domains", 500),
                        ("max_domain_controllers_total", 5000),
                        ("max_domain_controllers_per_domain", 500)),
                    mutating: false));
            }

            nextActions.Add(ToolChainingHints.NextAction(
                tool: "ad_domain_controllers",
                reason: "recover_domain_controller_inventory_via_domain_object_query",
                suggestedArguments: ToolChainingHints.Map(("max_results", 500)),
                mutating: false));

            var diagnosticsArgs = new List<(string Key, object? Value)> {
                ("max_issues", 2000),
                ("include_dns_srv_comparison", true),
                ("include_host_resolution", true),
                ("include_directory_topology", true)
            };
            if (!string.IsNullOrWhiteSpace(effectiveForest)) {
                diagnosticsArgs.Add(("forest_name", effectiveForest));
            }
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "ad_directory_discovery_diagnostics",
                reason: "compare_ad_dns_topology_receipts_to_explain_missing_domain_controllers",
                suggestedArguments: ToolChainingHints.Map(diagnosticsArgs.ToArray()),
                mutating: false));
        }

        var failedSteps = 0;
        if (!rootDseOk) {
            failedSteps++;
        }
        if (!domainsOk) {
            failedSteps++;
        }
        if (!domainControllersOk) {
            failedSteps++;
        }
        if (!trustsOk) {
            failedSteps++;
        }

        var confidence = 0.95d - (failedSteps * 0.14d);
        if (domains.Count == 0) {
            confidence -= 0.18d;
        }
        if (domainControllers.Count == 0) {
            confidence -= 0.22d;
        }

        return ToolChainingHints.Create(
            nextActions: nextActions,
            cursor: ToolChainingHints.BuildToken(
                "ad_forest_discover",
                ("forest", effectiveForest ?? string.Empty),
                ("domain", effectiveDomain ?? string.Empty),
                ("domains", domains.Count.ToString()),
                ("dcs", domainControllers.Count.ToString()),
                ("trusts", trusts.Count.ToString())),
            resumeToken: ToolChainingHints.BuildToken(
                "ad_forest_discover.resume",
                ("fallback", fallbackName),
                ("failed_steps", failedSteps.ToString())),
            handoff: handoff,
            confidence: confidence,
            flowId: ToolChainingHints.BuildToken(
                "ad_forest_discover.flow",
                ("forest", effectiveForest ?? string.Empty),
                ("domain", effectiveDomain ?? string.Empty)),
            stepId: "forest_receipt",
            checkpoint: ToolChainingHints.Map(
                ("domains", domains.Count),
                ("domain_controllers", domainControllers.Count),
                ("trusts", trusts.Count),
                ("failed_steps", failedSteps)));
    }

}
