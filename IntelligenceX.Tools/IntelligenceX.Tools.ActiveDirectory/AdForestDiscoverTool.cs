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

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Discovers forest scope (forest/domains/trusts/domain controllers) and emits a receipt describing what was discovered and how.
/// </summary>
public sealed class AdForestDiscoverTool : ActiveDirectoryToolBase, ITool {
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
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
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
            return Task.FromResult(Error("invalid_argument", "discovery_fallback is required (use: none/current_domain/current_forest)."));
        }
        var discoveryFallback = ToolEnumBinders.ParseOrDefault(
            value: discoveryFallbackRaw,
            map: DiscoveryFallbackModes,
            defaultValue: DirectoryDiscoveryFallback.None);

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
            rootDseInfo = DomainInfoService.Query(explicitDomainController, cancellationToken);
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
            return Task.FromResult(Error(
                errorCode: "invalid_argument",
                error: "Forest/domain scope is missing. Provide forest_name, domain_name, include_domains, or set discovery_fallback to current_forest/current_domain.",
                hints: new[] {
                    "For a true forest-wide discovery, provide forest_name or set discovery_fallback=current_forest.",
                    "If you only know a DC, provide domain_controller to derive forest/domain via RootDSE."
                },
                isTransient: false));
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
                        var ldapDomains = DomainHelper.EnumerateForestDomainNamesViaLdap(effectiveForest, cancellationToken).ToList();
                        domains.AddRange(ldapDomains);
                        ldapStep.Succeed(new { enabled = true, count = ldapDomains.Count, sample = ldapDomains.Take(5).ToArray() });
                    } catch (Exception ex) {
                        ldapStep.Fail(ex);
                    }
                }

                // Apply forest/domain resolver semantics for excludes (only in forest mode).
                if (excludeDomains.Count > 0) {
                    var excludeSet = BuildSet(excludeDomains);
                    if (excludeSet.Count > 0) {
                        domains = domains.Where(d => !excludeSet.Contains(NormalizeHostOrName(d))).ToList();
                    }
                }
            }

            // Force a stable, normalized representation.
            domains = domains
                .Select(NormalizeHostOrName)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

        var dcStep = new DiscoveryStep("domain_controllers");
        receipt.Add(dcStep);
        dcStep.Start();

        try {
            var includedDcSet = BuildSet(includeDomainControllers);
            var excludedDcSet = BuildSet(excludeDomainControllers);

            foreach (var domain in domains) {
                cancellationToken.ThrowIfCancellationRequested();

                var perDomainReceipt = new List<object>();
                var perDomain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                CollectDcSource(
                    perDomainReceipt,
                    sourceName: "active_directory",
                    () => DomainHelper.EnumerateDomainControllers(domain, cancellationToken: cancellationToken),
                    perDomain,
                    cancellationToken);

                if (discoveryFallback != DirectoryDiscoveryFallback.None) {
                    CollectDcSource(
                        perDomainReceipt,
                        sourceName: "dns_srv",
                        () => DomainHelper.EnumerateDomainControllersViaDnsSrv(domain),
                        perDomain,
                        cancellationToken);

                    CollectDcSource(
                        perDomainReceipt,
                        sourceName: "dsgetdcname",
                        () => DomainHelper.EnumerateDomainControllersViaDsGetDcName(domain),
                        perDomain,
                        cancellationToken);

                    CollectDcSource(
                        perDomainReceipt,
                        sourceName: "ldap_sites",
                        () => DomainHelper.EnumerateDomainControllersViaLdap(domain, cancellationToken),
                        perDomain,
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
                    perDomainList = perDomainList.Where(dc => includedDcSet.Contains(NormalizeHostOrName(dc))).ToList();
                }
                if (excludedDcSet.Count > 0) {
                    perDomainList = perDomainList.Where(dc => !excludedDcSet.Contains(NormalizeHostOrName(dc))).ToList();
                }

                if (perDomainList.Count > maxDcsPerDomain) {
                    perDomainList = perDomainList.Take(maxDcsPerDomain).ToList();
                }

                foreach (var dc in perDomainList) {
                    if (!allDcs.Contains(dc, StringComparer.OrdinalIgnoreCase)) {
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
            }
        };

        var summary = ToolMarkdown.SummaryText(
            title: "Active Directory: Forest Scope Discovery",
            $"Forest: `{effectiveForest ?? string.Empty}`; Domains: `{domains.Count}`; DCs: `{allDcs.Count}`; Trusts: `{trusts.Count}`.",
            "Receipt includes discovery steps and per-domain DC source attempts.");

        return Task.FromResult(ToolResponse.OkModel(model, summaryMarkdown: summary));
    }

    private static void CollectDcSource(
        List<object> perDomainReceipt,
        string sourceName,
        Func<IEnumerable<string>> enumerate,
        HashSet<string> target,
        CancellationToken cancellationToken) {
        var step = new DiscoveryStep($"domain_controllers:{sourceName}");
        perDomainReceipt.Add(step);
        step.Start();

        try {
            var raw = new List<string>();
            foreach (var dc in enumerate()) {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(dc)) {
                    continue;
                }
                raw.Add(NormalizeHostOrName(dc));
            }

            foreach (var dc in raw) {
                if (!string.IsNullOrWhiteSpace(dc)) {
                    target.Add(dc);
                }
            }

            step.Succeed(new {
                count = raw.Count,
                sample = raw.Take(5).ToArray()
            });
        } catch (Exception ex) {
            step.Fail(ex);
        }
    }

    private static HashSet<string> BuildSet(IEnumerable<string>? items) {
        if (items is null) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = items
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => NormalizeHostOrName(x))
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return normalized;
    }

    private static bool LooksLikeRodc(string host) {
        if (string.IsNullOrWhiteSpace(host)) {
            return false;
        }

        var normalized = NormalizeHostOrName(host);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return false;
        }

        int separator = normalized.IndexOf('.');
        var label = separator >= 0 ? normalized.Substring(0, separator) : normalized;
        if (string.IsNullOrWhiteSpace(label)) {
            return false;
        }

        return label.StartsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
               label.EndsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("-rodc", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("rodc-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRodcBestEffort(string host) {
        if (string.IsNullOrWhiteSpace(host)) {
            return false;
        }

        try {
            return DomainHelper.IsReadOnlyDc(NormalizeHostOrName(host));
        } catch {
            return LooksLikeRodc(host);
        }
    }

    private static object MapTrust(System.DirectoryServices.ActiveDirectory.TrustRelationshipInformation trust, string scope) {
        if (trust is null) {
            return new { scope, source_name = string.Empty, target_name = string.Empty, trust_type = string.Empty, trust_direction = string.Empty };
        }

        string source = string.Empty;
        string target = string.Empty;
        string type = string.Empty;
        string direction = string.Empty;

        try { source = trust.SourceName ?? string.Empty; } catch { }
        try { target = trust.TargetName ?? string.Empty; } catch { }
        try { type = trust.TrustType.ToString(); } catch { }
        try { direction = trust.TrustDirection.ToString(); } catch { }

        return new {
            scope,
            source_name = source,
            target_name = target,
            trust_type = type,
            trust_direction = direction
        };
    }

    private static string? NormalizeOptional(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var trimmed = value!.Trim().TrimEnd('.');
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeHostOrName(string input) {
        return (input ?? string.Empty).Trim().TrimEnd('.');
    }

    private static string ToDiscoveryFallbackName(DirectoryDiscoveryFallback fallback) {
        return fallback switch {
            DirectoryDiscoveryFallback.None => "none",
            DirectoryDiscoveryFallback.CurrentForest => "current_forest",
            _ => "current_domain"
        };
    }

    private sealed class DiscoveryStep {
        private readonly Stopwatch _sw = new();

        public DiscoveryStep(string name) {
            Name = name;
        }

        public string Name { get; }

        public void Start() {
            _sw.Restart();
        }

        public void Succeed(object? output = null) {
            _sw.Stop();
            Ok = true;
            DurationMs = (int)Math.Min(int.MaxValue, _sw.Elapsed.TotalMilliseconds);
            Output = output;
        }

        public void Fail(Exception ex) {
            _sw.Stop();
            Ok = false;
            DurationMs = (int)Math.Min(int.MaxValue, _sw.Elapsed.TotalMilliseconds);
            Error = ex.Message;
            ErrorType = ex.GetType().FullName ?? "Exception";
        }

        public bool Ok { get; private set; }
        public int DurationMs { get; private set; }
        public object? Output { get; private set; }
        public string? Error { get; private set; }
        public string? ErrorType { get; private set; }
    }
}
