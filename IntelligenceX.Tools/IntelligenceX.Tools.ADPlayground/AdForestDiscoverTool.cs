using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Helpers;
using ADPlayground.Infrastructure;
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

        var receipt = new List<object>();

        DomainInfoQueryResult? rootDseInfo = null;
        var rootDseStep = new DiscoveryStep("rootdse_context");
        receipt.Add(rootDseStep);
        rootDseStep.Start();
        try {
            // Best-effort context: explicit domain_controller is honored, otherwise RootDseReader selects a candidate.
            rootDseInfo = await RunWithTimeoutAsync(
                () => DomainInfoService.Query(explicitDomainController, cancellationToken),
                timeoutMs: DefaultRootDseTimeoutMs,
                cancellationToken);
            rootDseStep.Succeed(new {
                domain_controller = rootDseInfo.DomainController,
                dns_domain_name = rootDseInfo.DnsDomainName,
                forest_dns_name = rootDseInfo.ForestDnsName
            });
        } catch (Exception ex) {
            rootDseStep.Fail(ex);
        }

        var effectiveForest = NormalizeOptional(explicitForest)
                              ?? NormalizeOptional(rootDseInfo?.ForestDnsName)
                              ?? (discoveryFallback == DirectoryDiscoveryFallback.CurrentForest ? NormalizeOptional(DomainHelper.RootDomainName) : null);
        var effectiveDomain = NormalizeOptional(explicitDomain)
                              ?? NormalizeOptional(rootDseInfo?.DnsDomainName);

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

        // Domains: resolve with DirectoryTargetResolver semantics for filtering consistency, but we still record sources attempted.
        var domains = new List<string>();
        var domainsStep = new DiscoveryStep("domains");
        receipt.Add(domainsStep);
        domainsStep.Start();
        try {
            var domainSources = new List<object>();

            if (!string.IsNullOrWhiteSpace(effectiveDomain)) {
                var step = new DiscoveryStep("domains:explicit_domain_name");
                domainSources.Add(step);
                step.Start();
                domains.Add(effectiveDomain!);
                step.Succeed(new { count = 1, domain_name = effectiveDomain });
            } else if (includeDomains.Count > 0) {
                var step = new DiscoveryStep("domains:include_domains");
                domainSources.Add(step);
                step.Start();
                domains.AddRange(includeDomains);
                step.Succeed(new { count = includeDomains.Count });
            } else {
                var sdaStep = new DiscoveryStep("domains:active_directory");
                domainSources.Add(sdaStep);
                sdaStep.Start();
                List<string> sdaDomains;
                try {
                    sdaDomains = DomainHelper.EnumerateForestDomainNames(
                            forestName: effectiveForest,
                            includeTrustedDomains: includeTrustedDomains,
                            cancellationToken: cancellationToken)
                        .ToList();
                    sdaStep.Succeed(new { count = sdaDomains.Count, sample = sdaDomains.Take(5).ToArray(), include_trusts = includeTrustedDomains });
                } catch (Exception ex) {
                    sdaDomains = new List<string>();
                    sdaStep.Fail(ex);
                }

                domains.AddRange(sdaDomains);

                var ldapStep = new DiscoveryStep("domains:ldap_crossref");
                domainSources.Add(ldapStep);
                ldapStep.Start();
                if (sdaDomains.Count > 1) {
                    ldapStep.Succeed(new { enabled = false, reason = "active_directory_returned_multiple_domains" });
                } else if (discoveryFallback == DirectoryDiscoveryFallback.None) {
                    ldapStep.Succeed(new { enabled = false, reason = "discovery_fallback_none" });
                } else {
                    try {
                        var ldapDomains = await RunWithTimeoutAsync(
                            () => DomainHelper.EnumerateForestDomainNamesViaLdap(effectiveForest, cancellationToken).ToList(),
                            timeoutMs: DefaultLdapTimeoutMs,
                            cancellationToken);
                        domains.AddRange(ldapDomains);
                        ldapStep.Succeed(new { enabled = true, count = ldapDomains.Count, sample = ldapDomains.Take(5).ToArray() });
                    } catch (Exception ex) {
                        ldapStep.Fail(ex);
                    }
                }
            }

            // Apply include/exclude filters consistently across discovery modes.
            var includeDomainSet = BuildSet(includeDomains);
            var excludeDomainSet = BuildSet(excludeDomains);

            // Force a stable, normalized representation.
            domains = domains
                .Select(NormalizeHostOrName)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(d => includeDomainSet.Count == 0 || includeDomainSet.Contains(d))
                .Where(d => excludeDomainSet.Count == 0 || !excludeDomainSet.Contains(d))
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .Take(maxDomains)
                .ToList();

            domainsStep.Succeed(new {
                forest_name = effectiveForest ?? string.Empty,
                domain_name = effectiveDomain ?? string.Empty,
                include_trusts = includeTrustedDomains,
                count = domains.Count,
                max_domains = maxDomains,
                sources = domainSources
            });
        } catch (Exception ex) {
            domainsStep.Fail(ex);
        }

        // Domain controllers: enumerate per domain with a "what + how" receipt.
        var dcByDomain = new List<object>();
        var allDcs = new List<string>();
        var allDcsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dcStep = new DiscoveryStep("domain_controllers");
        receipt.Add(dcStep);
        dcStep.Start();

        try {
            var includedDcSet = BuildSet(includeDomainControllers);
            var excludedDcSet = BuildSet(excludeDomainControllers);
            bool AcceptDc(string dc) {
                if (excludedDcSet.Count > 0 && excludedDcSet.Contains(dc)) {
                    return false;
                }
                if (includedDcSet.Count > 0 && !includedDcSet.Contains(dc)) {
                    return false;
                }
                return true;
            }

            foreach (var domain in domains) {
                cancellationToken.ThrowIfCancellationRequested();

                var perDomainReceipt = new List<object>();
                var perDomain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await CollectDcSourceAsync(
                    perDomainReceipt,
                    sourceName: "active_directory",
                    () => DomainHelper.EnumerateDomainControllers(domain, cancellationToken: cancellationToken),
                    perDomain,
                    accept: AcceptDc,
                    maxCapture: maxDcsPerDomain,
                    timeoutMs: DefaultDcSourceTimeoutMs,
                    cancellationToken);

                if (discoveryFallback != DirectoryDiscoveryFallback.None) {
                    await CollectDcSourceAsync(
                        perDomainReceipt,
                        sourceName: "dns_srv",
                        () => DomainHelper.EnumerateDomainControllersViaDnsSrv(domain),
                        perDomain,
                        accept: AcceptDc,
                        maxCapture: maxDcsPerDomain,
                        timeoutMs: DefaultDcSourceTimeoutMs,
                        cancellationToken);

                    await CollectDcSourceAsync(
                        perDomainReceipt,
                        sourceName: "dsgetdcname",
                        () => DomainHelper.EnumerateDomainControllersViaDsGetDcName(domain),
                        perDomain,
                        accept: AcceptDc,
                        maxCapture: maxDcsPerDomain,
                        timeoutMs: DefaultDcSourceTimeoutMs,
                        cancellationToken);

                    await CollectDcSourceAsync(
                        perDomainReceipt,
                        sourceName: "ldap_sites",
                        () => DomainHelper.EnumerateDomainControllersViaLdap(domain, cancellationToken),
                        perDomain,
                        accept: AcceptDc,
                        maxCapture: maxDcsPerDomain,
                        timeoutMs: DefaultLdapTimeoutMs,
                        cancellationToken);
                }

                var perDomainList = perDomain
                    .Select(NormalizeHostOrName)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (skipRodc && perDomainList.Count > 0) {
                    perDomainList = perDomainList
                        .Where(dc => !IsRodcBestEffort(dc))
                        .ToList();
                }

                if (includedDcSet.Count > 0) {
                    perDomainList = perDomainList.Where(dc => includedDcSet.Contains(dc)).ToList();
                }
                if (excludedDcSet.Count > 0) {
                    perDomainList = perDomainList.Where(dc => !excludedDcSet.Contains(dc)).ToList();
                }

                if (perDomainList.Count > maxDcsPerDomain) {
                    perDomainList = perDomainList.Take(maxDcsPerDomain).ToList();
                }

                foreach (var dc in perDomainList) {
                    if (allDcsSet.Add(dc)) {
                        allDcs.Add(dc);
                    }
                }

                if (allDcs.Count >= maxDcsTotal) {
                    break;
                }

                dcByDomain.Add(new {
                    domain_name = domain,
                    domain_controllers = perDomainList,
                    receipt = perDomainReceipt,
                    domain_controller_count = perDomainList.Count
                });
            }

            if (allDcs.Count > maxDcsTotal) {
                allDcs = allDcs.Take(maxDcsTotal).ToList();
            }

            dcStep.Succeed(new {
                domain_count = domains.Count,
                domain_controller_count = allDcs.Count,
                max_domain_controllers_total = maxDcsTotal,
                max_domain_controllers_per_domain = maxDcsPerDomain,
                skip_rodc = skipRodc
            });
        } catch (Exception ex) {
            dcStep.Fail(ex);
        }

        // Trusts (best-effort).
        var trusts = new List<object>();
        var trustsStep = new DiscoveryStep("trusts");
        receipt.Add(trustsStep);
        trustsStep.Start();

        try {
            if (!includeTrustRelationships && !includeDomainTrustRelationships) {
                trustsStep.Succeed(new { enabled = false, count = 0 });
            } else {
                if (includeTrustRelationships && !string.IsNullOrWhiteSpace(effectiveForest)) {
                    foreach (var trust in SdaFast.GetForestTrusts(effectiveForest, trustTimeoutMs)) {
                        cancellationToken.ThrowIfCancellationRequested();
                        trusts.Add(MapTrust(trust, scope: "forest"));
                        if (trusts.Count >= maxTrusts) {
                            break;
                        }
                    }
                }

                if (includeDomainTrustRelationships) {
                    foreach (var domain in domains) {
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (var trust in SdaFast.GetDomainTrusts(domain, trustTimeoutMs)) {
                            trusts.Add(MapTrust(trust, scope: "domain"));
                            if (trusts.Count >= maxTrusts) {
                                break;
                            }
                        }
                        if (trusts.Count >= maxTrusts) {
                            break;
                        }
                    }
                }

                trustsStep.Succeed(new { enabled = true, count = trusts.Count, max_trusts = maxTrusts });
            }
        } catch (Exception ex) {
            trustsStep.Fail(ex);
        }
        var chain = BuildChainContract(
            discoveryFallback: discoveryFallback,
            effectiveForest: effectiveForest,
            effectiveDomain: effectiveDomain,
            domains: domains,
            domainControllers: allDcs,
            trusts: trusts,
            rootDseOk: rootDseStep.Ok,
            domainsOk: domainsStep.Ok,
            domainControllersOk: dcStep.Ok,
            trustsOk: trustsStep.Ok);

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
        bool rootDseOk,
        bool domainsOk,
        bool domainControllersOk,
        bool trustsOk) {
        var fallbackName = ToDiscoveryFallbackName(discoveryFallback);
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
                    ("forest_name", effectiveForest ?? string.Empty),
                    ("domain_name", effectiveDomain ?? string.Empty),
                    ("discovery_fallback", fallbackName)),
                mutating: false),
            ToolChainingHints.NextAction(
                tool: "ad_monitoring_probe_run",
                reason: "Run replication health probes across discovered scope.",
                suggestedArguments: ToolChainingHints.Map(
                    ("probe_kind", "replication"),
                    ("domain_name", effectiveDomain ?? string.Empty),
                    ("discovery_fallback", fallbackName)),
                arguments: ToolChainingHints.MapObject(
                    ("probe_kind", "replication"),
                    ("domain_name", effectiveDomain ?? string.Empty),
                    ("discovery_fallback", fallbackName),
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
                nextActions.Add(ToolChainingHints.NextAction(
                    tool: "ad_forest_discover",
                    reason: "expand_scope_via_current_forest_when_domain_controller_inventory_sparse",
                    suggestedArguments: ToolChainingHints.Map(
                        ("forest_name", effectiveForest ?? string.Empty),
                        ("domain_name", effectiveDomain ?? string.Empty),
                        ("discovery_fallback", "current_forest"),
                        ("include_trusts", true),
                        ("max_domains", 500),
                        ("max_domain_controllers_total", 5000),
                        ("max_domain_controllers_per_domain", 500)),
                    arguments: ToolChainingHints.MapObject(
                        ("forest_name", effectiveForest ?? string.Empty),
                        ("domain_name", effectiveDomain ?? string.Empty),
                        ("discovery_fallback", "current_forest"),
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
