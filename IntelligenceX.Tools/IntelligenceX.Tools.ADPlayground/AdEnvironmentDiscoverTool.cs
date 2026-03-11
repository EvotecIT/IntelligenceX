using System;
using System.Collections.Generic;
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
/// Discovers effective AD query context (forest/domain/DC/base DN) for agent planning.
/// </summary>
public sealed class AdEnvironmentDiscoverTool : ActiveDirectoryToolBase, ITool {
    private const int MaxDomainControllersCap = 100;
    private const int MaxDiscoverySamplePerSource = 5;

    private sealed record DomainControllerSourceReceipt(
        string Source,
        bool Ok,
        int Added,
        IReadOnlyList<string> Sample,
        string? ErrorCode,
        string? Error);

    private sealed record DomainControllerDiscoveryResult(
        IReadOnlyList<string> DomainControllers,
        IReadOnlyList<string> DiscoveredDomains,
        IReadOnlyList<DomainControllerSourceReceipt> Sources,
        IReadOnlyList<string> MissingReasons) {
        public static DomainControllerDiscoveryResult Empty { get; } =
            new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<DomainControllerSourceReceipt>(), Array.Empty<string>());
    }

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreateEnvironmentDiscoverDefinition(
        toolName: "ad_environment_discover",
        description: "Discover Active Directory context (forest/domain/domain controllers/base DN) and return effective scope hints for follow-up tools.",
        parameters: ToolSchema.Object(
                ("domain_controller", ToolSchema.String("Optional domain controller override (host/FQDN).")),
                ("search_base_dn", ToolSchema.String("Optional search base DN override.")),
                ("include_domain_controllers", ToolSchema.Boolean("When true, include discovered domain controller candidates. Default true.")),
                ("max_domain_controllers", ToolSchema.Integer("Maximum discovered domain controllers returned (capped). Default 20.")),
                ("include_forest_domains", ToolSchema.Boolean("When true, fan out domain-controller discovery across discovered forest domains. Default true.")),
                ("include_trusts", ToolSchema.Boolean("When true, include trusted-forest domains while fanning out discovery. Default false.")))
            .NoAdditionalProperties(),
        packId: "active_directory",
        domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
        domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
        domainSignalTokens: new[] {
            "dc",
            "ldap",
            "gpo",
            "kerberos",
            "replication",
            "sysvol",
            "netlogon",
            "ntds",
            "forest",
            "trust",
            "active_directory",
            "adplayground"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AdEnvironmentDiscoverTool"/> class.
    /// </summary>
    public AdEnvironmentDiscoverTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var includeDomainControllers = ToolArgs.GetBoolean(arguments, "include_domain_controllers", true);
        var maxDomainControllers = ToolArgs.GetCappedInt32(arguments, "max_domain_controllers", 20, 1, MaxDomainControllersCap);
        var includeForestDomains = ToolArgs.GetBoolean(arguments, "include_forest_domains", true);
        var includeTrusts = ToolArgs.GetBoolean(arguments, "include_trusts", false);

        LdapToolContextHelper.LdapSearchContext context;
        try {
            context = LdapToolContextHelper.ResolveSearchContext(
                explicitDomainController: ToolArgs.GetOptionalTrimmed(arguments, "domain_controller"),
                explicitBaseDn: ToolArgs.GetOptionalTrimmed(arguments, "search_base_dn"),
                defaultDomainController: ToolArgs.NormalizeOptional(Options.DomainController),
                defaultBaseDn: ToolArgs.NormalizeOptional(Options.DefaultSearchBaseDn),
                cancellationToken: cancellationToken);
        } catch (Exception ex) {
            var message = SanitizeErrorMessage(ex.Message, "AD context discovery failed.");
            return Task.FromResult(ToolResponse.Error(
                "not_configured",
                message,
                hints: new[] {
                    "Try providing domain_controller (FQDN) and/or search_base_dn (DN).",
                    "Use ad_domain_info to validate RootDSE connectivity."
                }));
        }

        DomainInfoQueryResult info;
        try {
            info = DomainInfoService.Query(context.DomainController, cancellationToken);
        } catch (Exception ex) {
            var message = SanitizeErrorMessage(ex.Message, "AD RootDSE query failed after context discovery.");
            return Task.FromResult(ToolResponse.Error(
                "not_configured",
                message,
                hints: new[] {
                    "Verify LDAP connectivity to the discovered domain controller.",
                    "Provide domain_controller explicitly if auto-discovery chose an unreachable endpoint."
                }));
        }

        var effectiveDnsDomainName = ToolArgs.NormalizeOptional(info.DnsDomainName);
        if (effectiveDnsDomainName is null
            && TryExtractDomainNameFromDistinguishedName(context.BaseDn, out var derivedDomainName)) {
            effectiveDnsDomainName = derivedDomainName;
        }

        var effectiveForestDnsName = ToolArgs.NormalizeOptional(info.ForestDnsName) ?? effectiveDnsDomainName;

        var domainControllerDiscovery = includeDomainControllers
            ? DiscoverDomainControllers(
                dnsDomainName: effectiveDnsDomainName,
                forestDnsName: effectiveForestDnsName,
                preferredDomainController: context.DomainController,
                maxDomainControllers: maxDomainControllers,
                includeForestDomains: includeForestDomains,
                includeTrusts: includeTrusts,
                cancellationToken: cancellationToken)
            : DomainControllerDiscoveryResult.Empty;
        var discoveredDomainControllers = domainControllerDiscovery.DomainControllers;
        var hasLimitedDiscovery = includeDomainControllers && discoveredDomainControllers.Count <= 1;
        var nextActions = BuildReadOnlyEnvironmentNextActions(
            hasLimitedDiscovery: hasLimitedDiscovery,
            domainController: context.DomainController,
            dnsDomainName: effectiveDnsDomainName,
            forestDnsName: effectiveForestDnsName,
            includeForestDomains: includeForestDomains,
            includeTrusts: includeTrusts);

        var model = new {
            Context = new {
                DomainController = context.DomainController ?? string.Empty,
                SearchBaseDn = context.BaseDn,
                DomainControllerSource = ToSourceName(context.DomainControllerSource),
                SearchBaseDnSource = ToSourceName(context.BaseDnSource)
            },
            Domain = new {
                DnsDomainName = effectiveDnsDomainName ?? string.Empty,
                ForestDnsName = effectiveForestDnsName ?? string.Empty
            },
            DomainControllers = discoveredDomainControllers,
            DiscoveryStatus = new {
                IncludeDomainControllers = includeDomainControllers,
                IncludeForestDomains = includeForestDomains,
                IncludeTrusts = includeTrusts,
                DomainControllerCount = discoveredDomainControllers.Count,
                DiscoveredDomainCount = domainControllerDiscovery.DiscoveredDomains.Count,
                LimitedDiscovery = hasLimitedDiscovery
            },
            DomainControllerDiscovery = new {
                Domains = domainControllerDiscovery.DiscoveredDomains,
                Sources = domainControllerDiscovery.Sources,
                MissingReasons = domainControllerDiscovery.MissingReasons
            },
            NextActions = nextActions,
            RootDse = new LdapToolOutputRow {
                Attributes = info.RootDse.Attributes,
                TruncatedAttributes = info.RootDse.TruncatedAttributes
            }
        };

        var facts = new List<(string Key, string Value)> {
            ("Domain controller", context.DomainController ?? string.Empty),
            ("Search base DN", context.BaseDn),
            ("DNS domain", effectiveDnsDomainName ?? string.Empty),
            ("Forest", effectiveForestDnsName ?? string.Empty),
            ("Discovered domains", includeDomainControllers ? domainControllerDiscovery.DiscoveredDomains.Count.ToString() : "0"),
            ("Discovered DCs", discoveredDomainControllers.Count.ToString()),
            ("Discovery status", hasLimitedDiscovery ? "limited" : "sufficient"),
            ("Forest fan-out", includeForestDomains ? "enabled" : "disabled"),
            ("Discovery sources", includeDomainControllers ? domainControllerDiscovery.Sources.Count.ToString() : "0")
        };
        var meta = ToolOutputHints.Meta(count: 1, truncated: false);
        AddReadOnlyEnvironmentChainingMeta(
            meta: meta,
            nextActions: nextActions,
            hasLimitedDiscovery: hasLimitedDiscovery,
            includeForestDomains: includeForestDomains,
            includeTrusts: includeTrusts,
            discoveredDomainCount: domainControllerDiscovery.DiscoveredDomains.Count,
            discoveredDomainControllerCount: discoveredDomainControllers.Count,
            preferredDomainController: context.DomainController,
            dnsDomainName: effectiveDnsDomainName,
            forestDnsName: effectiveForestDnsName);

        var factRows = new List<IReadOnlyList<string>>(facts.Count);
        for (var i = 0; i < facts.Count; i++) {
            var fact = facts[i];
            factRows.Add(new[] { fact.Key, fact.Value });
        }

        var summaryMarkdown = ToolMarkdownContract.Create()
            .AddTable(
                title: "Active Directory: Environment Discovery",
                headers: new[] { "Field", "Value" },
                rows: factRows,
                totalCount: factRows.Count,
                truncated: false)
            .Build();

        return Task.FromResult(ToolOutputEnvelope.OkFlatWithRenderValue(
            root: ToolJson.ToJsonObjectSnakeCase(model),
            meta: meta,
            summaryMarkdown: summaryMarkdown,
            render: BuildRenderHints(
                domainControllerCount: discoveredDomainControllers.Count,
                discoveredDomainCount: domainControllerDiscovery.DiscoveredDomains.Count,
                discoverySourceCount: domainControllerDiscovery.Sources.Count,
                nextActionCount: nextActions.Count,
                missingReasonCount: domainControllerDiscovery.MissingReasons.Count)));
    }

    private static JsonValue? BuildRenderHints(
        int domainControllerCount,
        int discoveredDomainCount,
        int discoverySourceCount,
        int nextActionCount,
        int missingReasonCount) {
        var hints = new JsonArray();

        if (discoverySourceCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "domain_controller_discovery/sources",
                    new ToolColumn("source", "Source", "string"),
                    new ToolColumn("ok", "Ok", "bool"),
                    new ToolColumn("added", "Added", "int"),
                    new ToolColumn("error_code", "Error code", "string"),
                    new ToolColumn("error", "Error", "string"))
                .Add("priority", 450));
        }

        if (domainControllerCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "domain_controllers",
                    new ToolColumn("value", "Domain controller", "string"))
                .Add("priority", 400));
        }

        if (nextActionCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "next_actions",
                    new ToolColumn("tool", "Tool", "string"),
                    new ToolColumn("reason", "Reason", "string"),
                    new ToolColumn("mutating", "Mutating", "bool"))
                .Add("priority", 300));
        }

        if (discoveredDomainCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "domain_controller_discovery/domains",
                    new ToolColumn("value", "Domain", "string"))
                .Add("priority", 200));
        }

        if (missingReasonCount > 0) {
            hints.Add(ToolOutputHints.RenderTable(
                    "domain_controller_discovery/missing_reasons",
                    new ToolColumn("value", "Missing reason", "string"))
                .Add("priority", 100));
        }

        if (hints.Count == 0) {
            return null;
        }

        return JsonValue.From(hints);
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

    private static DomainControllerDiscoveryResult DiscoverDomainControllers(
        string? dnsDomainName,
        string? forestDnsName,
        string? preferredDomainController,
        int maxDomainControllers,
        bool includeForestDomains,
        bool includeTrusts,
        CancellationToken cancellationToken) {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<DomainControllerSourceReceipt>();
        var missingReasons = new List<string>();
        var domainSeeds = new List<string>();
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static int AddCandidate(List<string> target, HashSet<string> targetSeen, string? value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return 0;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0) {
                return 0;
            }

            if (targetSeen.Add(normalized)) {
                target.Add(normalized);
                return 1;
            }

            return 0;
        }

        static int AddDomainSeed(List<string> target, HashSet<string> targetSeen, string? value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return 0;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0) {
                return 0;
            }

            if (targetSeen.Add(normalized)) {
                target.Add(normalized);
                return 1;
            }

            return 0;
        }

        var preferredAdded = AddCandidate(list, seen, preferredDomainController);
        sources.Add(new DomainControllerSourceReceipt(
            Source: "preferred_domain_controller",
            Ok: true,
            Added: preferredAdded,
            Sample: preferredAdded > 0 ? new[] { preferredDomainController!.Trim() } : Array.Empty<string>(),
            ErrorCode: null,
            Error: null));

        var domain = ToolArgs.NormalizeOptional(dnsDomainName);
        var forest = ToolArgs.NormalizeOptional(forestDnsName);
        if (domain is not null) {
            AddDomainSeed(domainSeeds, seenDomains, domain);
        }

        if (includeForestDomains) {
            var forestSeed = forest ?? domain;
            if (string.IsNullOrWhiteSpace(forestSeed)) {
                forestSeed = ToolArgs.NormalizeOptional(DomainHelper.RootDomainName);
            }

            if (!string.IsNullOrWhiteSpace(forestSeed)) {
                try {
                    var beforeDomains = domainSeeds.Count;
                    foreach (var discoveredDomain in DomainHelper.EnumerateForestDomainNames(
                                 forestName: forestSeed,
                                 includeTrustedDomains: includeTrusts,
                                 cancellationToken: cancellationToken)) {
                        cancellationToken.ThrowIfCancellationRequested();
                        AddDomainSeed(domainSeeds, seenDomains, discoveredDomain);
                    }
                    sources.Add(new DomainControllerSourceReceipt(
                        Source: "forest_domains",
                        Ok: true,
                        Added: domainSeeds.Count - beforeDomains,
                        Sample: domainSeeds.Skip(beforeDomains).Take(MaxDiscoverySamplePerSource).ToArray(),
                        ErrorCode: null,
                        Error: null));
                } catch {
                    sources.Add(new DomainControllerSourceReceipt(
                        Source: "forest_domains",
                        Ok: false,
                        Added: 0,
                        Sample: Array.Empty<string>(),
                        ErrorCode: "discovery_failed",
                        Error: "Forest domain enumeration failed."));
                }
            }
        }

        if (domainSeeds.Count == 0 && domain is not null) {
            AddDomainSeed(domainSeeds, seenDomains, domain);
        }

        if (domainSeeds.Count == 0) {
            missingReasons.Add("dns_domain_name and forest scope are empty; domain controller discovery sources requiring domain scope were skipped.");
            return new DomainControllerDiscoveryResult(
                DomainControllers: list,
                DiscoveredDomains: domainSeeds,
                Sources: sources,
                MissingReasons: missingReasons);
        }

        for (var domainIndex = 0; domainIndex < domainSeeds.Count; domainIndex++) {
            cancellationToken.ThrowIfCancellationRequested();
            if (list.Count >= maxDomainControllers) {
                break;
            }

            var seedDomain = domainSeeds[domainIndex];
            var isPrimaryDomain = domain is not null && string.Equals(seedDomain, domain, StringComparison.OrdinalIgnoreCase);
            var sourceSuffix = isPrimaryDomain ? string.Empty : $":{seedDomain}";

            var pdcCandidate = DomainHelper.TryGetPdcHostName(seedDomain);
            var pdcAdded = AddCandidate(list, seen, pdcCandidate);
            sources.Add(new DomainControllerSourceReceipt(
                Source: "pdc_hint" + sourceSuffix,
                Ok: true,
                Added: pdcAdded,
                Sample: pdcAdded > 0 ? new[] { pdcCandidate!.Trim() } : Array.Empty<string>(),
                ErrorCode: null,
                Error: null));

            sources.Add(CollectDomainControllerSource(
                sourceName: "dsgetdcname" + sourceSuffix,
                enumerate: () => DomainHelper.EnumerateDomainControllersViaDsGetDcName(seedDomain),
                target: list,
                seen: seen,
                maxDomainControllers: maxDomainControllers,
                cancellationToken: cancellationToken));

            if (list.Count >= maxDomainControllers) {
                break;
            }

            sources.Add(CollectDomainControllerSource(
                sourceName: "dns_srv" + sourceSuffix,
                enumerate: () => DomainHelper.EnumerateDomainControllersViaDnsSrv(seedDomain),
                target: list,
                seen: seen,
                maxDomainControllers: maxDomainControllers,
                cancellationToken: cancellationToken));

            if (list.Count >= maxDomainControllers) {
                break;
            }

            sources.Add(CollectDomainControllerSource(
                sourceName: "active_directory" + sourceSuffix,
                enumerate: () => DomainHelper.EnumerateDomainControllers(domainName: seedDomain, cancellationToken: cancellationToken),
                target: list,
                seen: seen,
                maxDomainControllers: maxDomainControllers,
                cancellationToken: cancellationToken));
        }

        if (includeForestDomains && domainSeeds.Count > 1 && list.Count < maxDomainControllers) {
            sources.Add(CollectDomainControllerSource(
                sourceName: "active_directory_forest",
                enumerate: () => DomainHelper.EnumerateDomainControllers(domainName: null, cancellationToken: cancellationToken),
                target: list,
                seen: seen,
                maxDomainControllers: maxDomainControllers,
                cancellationToken: cancellationToken));
        }

        if (list.Count == 0) {
            missingReasons.Add("No domain controllers discovered across preferred host, forest/domain probes, DsGetDcName, DNS SRV, or Active Directory APIs.");
        } else if (list.Count == 1) {
            missingReasons.Add("Only one domain controller candidate discovered; run ad_scope_discovery/ad_forest_discover for broader context.");
        }

        if (includeForestDomains && domainSeeds.Count <= 1) {
            missingReasons.Add("Forest fan-out yielded one or fewer domain seeds in this context.");
        }

        if (list.Count > maxDomainControllers) {
            list = list.Take(maxDomainControllers).ToList();
        }

        return new DomainControllerDiscoveryResult(
            DomainControllers: list,
            DiscoveredDomains: domainSeeds,
            Sources: sources,
            MissingReasons: missingReasons);
    }

    private static DomainControllerSourceReceipt CollectDomainControllerSource(
        string sourceName,
        Func<IEnumerable<string>> enumerate,
        List<string> target,
        HashSet<string> seen,
        int maxDomainControllers,
        CancellationToken cancellationToken) {
        try {
            var beforeCount = target.Count;
            foreach (var candidate in enumerate()) {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(candidate)) {
                    continue;
                }

                var normalized = candidate.Trim();
                if (normalized.Length == 0) {
                    continue;
                }

                if (seen.Add(normalized)) {
                    target.Add(normalized);
                }

                if (target.Count >= maxDomainControllers) {
                    break;
                }
            }

            return new DomainControllerSourceReceipt(
                Source: sourceName,
                Ok: true,
                Added: target.Count - beforeCount,
                Sample: target.Skip(beforeCount).Take(MaxDiscoverySamplePerSource).ToArray(),
                ErrorCode: null,
                Error: null);
        } catch {
            return new DomainControllerSourceReceipt(
                Source: sourceName,
                Ok: false,
                Added: 0,
                Sample: Array.Empty<string>(),
                ErrorCode: "discovery_failed",
                Error: $"Domain controller discovery via {sourceName} failed.");
        }
    }

    private static IReadOnlyList<ToolNextActionModel> BuildReadOnlyEnvironmentNextActions(
        bool hasLimitedDiscovery,
        string? domainController,
        string? dnsDomainName,
        string? forestDnsName,
        bool includeForestDomains,
        bool includeTrusts) {
        if (!hasLimitedDiscovery) {
            return Array.Empty<ToolNextActionModel>();
        }

        var broadenForestDomains = includeForestDomains || hasLimitedDiscovery;
        var broadenTrusts = includeTrusts || hasLimitedDiscovery;
        var scopeArgs = new List<(string Key, object? Value)> {
            ("discovery_fallback", broadenForestDomains ? "current_forest" : "current_domain"),
            ("include_trusts", broadenTrusts),
            ("include_forest_domains", broadenForestDomains),
            ("max_domain_controllers_total", 5000),
            ("max_domain_controllers_per_domain", 500)
        };
        if (!string.IsNullOrWhiteSpace(domainController)) {
            scopeArgs.Add(("domain_controller", domainController));
        }
        if (!string.IsNullOrWhiteSpace(dnsDomainName)) {
            scopeArgs.Add(("domain_name", dnsDomainName));
        }
        if (!string.IsNullOrWhiteSpace(forestDnsName)) {
            scopeArgs.Add(("forest_name", forestDnsName));
        }

        var actions = new List<ToolNextActionModel> {
            ToolChainingHints.NextAction(
                tool: "ad_scope_discovery",
                reason: "expand_scope_with_probe_receipts",
                suggestedArguments: ToolChainingHints.Map(scopeArgs.ToArray()),
                mutating: false),
            ToolChainingHints.NextAction(
                tool: "ad_forest_discover",
                reason: "confirm_forest_wide_domain_controller_inventory",
                suggestedArguments: ToolChainingHints.Map(scopeArgs.ToArray()),
                mutating: false)
        };

        actions.Add(ToolChainingHints.NextAction(
            tool: "ad_domain_controllers",
            reason: "recover_current_domain_controller_inventory",
            suggestedArguments: ToolChainingHints.Map(("max_results", 500)),
            mutating: false));

        var diagnosticsArgs = new List<(string Key, object? Value)> {
            ("max_issues", 2000),
            ("include_dns_srv_comparison", true),
            ("include_host_resolution", true),
            ("include_directory_topology", true)
        };
        if (!string.IsNullOrWhiteSpace(forestDnsName)) {
            diagnosticsArgs.Add(("forest_name", forestDnsName));
        }
        actions.Add(ToolChainingHints.NextAction(
            tool: "ad_directory_discovery_diagnostics",
            reason: "diagnose_discovery_path_gaps_across_ad_dns_topology",
            suggestedArguments: ToolChainingHints.Map(diagnosticsArgs.ToArray()),
            mutating: false));

        return actions;
    }

    private static void AddReadOnlyEnvironmentChainingMeta(
        JsonObject meta,
        IReadOnlyList<ToolNextActionModel> nextActions,
        bool hasLimitedDiscovery,
        bool includeForestDomains,
        bool includeTrusts,
        int discoveredDomainCount,
        int discoveredDomainControllerCount,
        string? preferredDomainController,
        string? dnsDomainName,
        string? forestDnsName) {
        if (meta is null) {
            throw new ArgumentNullException(nameof(meta));
        }

        var chain = ToolChainingHints.Create(
            nextActions: nextActions,
            confidence: hasLimitedDiscovery ? 0.63d : 0.90d,
            checkpoint: ToolChainingHints.Map(
                ("current_tool", "ad_environment_discover"),
                ("limited_discovery", hasLimitedDiscovery),
                ("include_forest_domains", includeForestDomains),
                ("include_trusts", includeTrusts),
                ("domain_count", discoveredDomainCount),
                ("domain_controller_count", discoveredDomainControllerCount)));

        var nextActionsJson = new JsonArray();
        for (var i = 0; i < chain.NextActions.Count; i++) {
            nextActionsJson.Add(ToolJson.ToJsonObjectSnakeCase(chain.NextActions[i]));
        }

        meta.Add("next_actions", nextActionsJson);
        meta.Add("discovery_status", ToolJson.ToJsonObjectSnakeCase(new {
            limited_discovery = hasLimitedDiscovery,
            include_forest_domains = includeForestDomains,
            include_trusts = includeTrusts,
            discovered_domains = discoveredDomainCount,
            discovered_domain_controllers = discoveredDomainControllerCount,
            preferred_domain_controller = preferredDomainController ?? string.Empty,
            dns_domain_name = dnsDomainName ?? string.Empty,
            forest_dns_name = forestDnsName ?? string.Empty
        }));
        meta.Add("chain_confidence", chain.Confidence);
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
