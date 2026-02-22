using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Helpers;
using ADPlayground.Monitoring.Probes;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Discovers AD scope with explicit naming-context output and probe receipts.
/// </summary>
public sealed class AdScopeDiscoveryTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMaxDomains = 250;
    private const int MaxDomainsCap = 5000;
    private const int DefaultMaxDomainControllersTotal = 2000;
    private const int MaxDomainControllersTotalCap = 50_000;
    private const int DefaultMaxDomainControllersPerDomain = 200;
    private const int MaxDomainControllersPerDomainCap = 5000;
    private const int DefaultRootDseTimeoutMs = 5000;
    private const int MaxRootDseTimeoutMs = 60_000;
    private const int DefaultDomainEnumerationTimeoutMs = 10_000;
    private const int MaxDomainEnumerationTimeoutMs = 120_000;
    private const int DefaultDcSourceTimeoutMs = 5000;
    private const int MaxDcSourceTimeoutMs = 120_000;

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
        "ad_scope_discovery",
        "Discover effective AD scope with naming contexts, discovered domains/DCs, and step-by-step probe receipts.",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name to enumerate (for example contoso.com).")),
                ("domain_name", ToolSchema.String("Optional DNS domain name to seed discovery when forest_name is omitted.")),
                ("domain_controller", ToolSchema.String("Optional domain controller host/FQDN used for RootDSE and DC discovery probes.")),
                ("include_domains", ToolSchema.Array(ToolSchema.String(), "Optional include-domain filter applied to discovered domains.")),
                ("exclude_domains", ToolSchema.Array(ToolSchema.String(), "Optional exclude-domain filter applied to discovered domains.")),
                ("include_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional include-DC filter applied to discovered domain controllers.")),
                ("exclude_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional exclude-DC filter applied to discovered domain controllers.")),
                ("skip_rodc", ToolSchema.Boolean("When true, excludes RODCs from discovered domain controller lists.")),
                ("include_trusts", ToolSchema.Boolean("When true, includes trusted forest domains during domain discovery (default false).")),
                ("discovery_fallback",
                    ToolSchema.String("Fallback when explicit forest/domain is missing.")
                        .Enum("none", "current_domain", "current_forest")),
                ("max_domains", ToolSchema.Integer("Maximum domains returned (capped). Default 250.")),
                ("max_domain_controllers_total", ToolSchema.Integer("Maximum DCs returned across all domains (capped). Default 2000.")),
                ("max_domain_controllers_per_domain", ToolSchema.Integer("Maximum DCs returned per domain (capped). Default 200.")),
                ("rootdse_timeout_ms", ToolSchema.Integer("Timeout for RootDSE discovery step in milliseconds (default 5000).")),
                ("domain_enumeration_timeout_ms", ToolSchema.Integer("Timeout for domain-enumeration step in milliseconds (default 10000).")),
                ("dc_source_timeout_ms", ToolSchema.Integer("Timeout for each DC source step in milliseconds (default 5000).")))
            .Required("discovery_fallback")
            .NoAdditionalProperties());

    private sealed record ScopeDiscoveryRequest(
        string? ForestName,
        string? DomainName,
        string? DomainController,
        IReadOnlyList<string> IncludeDomains,
        IReadOnlyList<string> ExcludeDomains,
        IReadOnlyList<string> IncludeDomainControllers,
        IReadOnlyList<string> ExcludeDomainControllers,
        bool SkipRodc,
        bool IncludeTrusts,
        DirectoryDiscoveryFallback DiscoveryFallback,
        int MaxDomains,
        int MaxDomainControllersTotal,
        int MaxDomainControllersPerDomain,
        int RootDseTimeoutMs,
        int DomainEnumerationTimeoutMs,
        int DcSourceTimeoutMs);

    private sealed class StepExecutionResult<T> {
        public bool Ok { get; init; }
        public int DurationMs { get; init; }
        public int TimeoutMs { get; init; }
        public IReadOnlyList<string> EndpointsChecked { get; init; } = Array.Empty<string>();
        public int Retries { get; init; }
        public string? Error { get; init; }
        public string? ErrorType { get; init; }
        public T? Result { get; init; }
        public object? Output { get; init; }
    }

    private sealed record ScopeDiscoveryStep(
        string Name,
        bool Ok,
        int DurationMs,
        int TimeoutMs,
        IReadOnlyList<string> EndpointsChecked,
        int Retries,
        string? Error,
        string? ErrorType,
        object? Output);

    private sealed record NamingContextsModel(
        string? DefaultNamingContext,
        string? ConfigurationNamingContext,
        string? SchemaNamingContext,
        string? RootDomainNamingContext);

    private sealed record ScopeDiscoveryGap(
        string Area,
        string Reason);

    private sealed record DomainControllerDomainRow(
        string DomainName,
        IReadOnlyList<string> DomainControllers,
        IReadOnlyList<ScopeDiscoveryStep> Sources,
        IReadOnlyList<string> MissingReasons);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdScopeDiscoveryTool"/> class.
    /// </summary>
    public AdScopeDiscoveryTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<ScopeDiscoveryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("discovery_fallback", out var discoveryFallbackRaw, out var discoveryFallbackError)) {
                return ToolRequestBindingResult<ScopeDiscoveryRequest>.Failure(discoveryFallbackError);
            }

            if (!TryParseDiscoveryFallback(discoveryFallbackRaw, out var fallback, out var fallbackError)) {
                return ToolRequestBindingResult<ScopeDiscoveryRequest>.Failure(
                    fallbackError ?? "discovery_fallback must be one of: none, current_domain, current_forest.");
            }

            return ToolRequestBindingResult<ScopeDiscoveryRequest>.Success(
                new ScopeDiscoveryRequest(
                    ForestName: reader.OptionalString("forest_name"),
                    DomainName: reader.OptionalString("domain_name"),
                    DomainController: reader.OptionalString("domain_controller"),
                    IncludeDomains: reader.DistinctStringArray("include_domains"),
                    ExcludeDomains: reader.DistinctStringArray("exclude_domains"),
                    IncludeDomainControllers: reader.DistinctStringArray("include_domain_controllers"),
                    ExcludeDomainControllers: reader.DistinctStringArray("exclude_domain_controllers"),
                    SkipRodc: reader.Boolean("skip_rodc", defaultValue: false),
                    IncludeTrusts: reader.Boolean("include_trusts", defaultValue: false),
                    DiscoveryFallback: fallback,
                    MaxDomains: reader.CappedInt32("max_domains", DefaultMaxDomains, 1, MaxDomainsCap),
                    MaxDomainControllersTotal: reader.CappedInt32("max_domain_controllers_total", DefaultMaxDomainControllersTotal, 1, MaxDomainControllersTotalCap),
                    MaxDomainControllersPerDomain: reader.CappedInt32("max_domain_controllers_per_domain", DefaultMaxDomainControllersPerDomain, 1, MaxDomainControllersPerDomainCap),
                    RootDseTimeoutMs: reader.CappedInt32("rootdse_timeout_ms", DefaultRootDseTimeoutMs, 200, MaxRootDseTimeoutMs),
                    DomainEnumerationTimeoutMs: reader.CappedInt32("domain_enumeration_timeout_ms", DefaultDomainEnumerationTimeoutMs, 200, MaxDomainEnumerationTimeoutMs),
                    DcSourceTimeoutMs: reader.CappedInt32("dc_source_timeout_ms", DefaultDcSourceTimeoutMs, 200, MaxDcSourceTimeoutMs)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<ScopeDiscoveryRequest> context, CancellationToken cancellationToken) {
        var request = context.Request;
        var explicitForest = NormalizeOptional(request.ForestName);
        var explicitDomain = NormalizeOptional(request.DomainName);
        var explicitDomainController = NormalizeOptional(request.DomainController);

        if (explicitForest is null &&
            explicitDomain is null &&
            request.IncludeDomains.Count == 0 &&
            request.DiscoveryFallback == DirectoryDiscoveryFallback.None) {
            return ToolResponse.Error(
                "invalid_argument",
                "Forest/domain scope is missing. Provide forest_name, domain_name, include_domains, or set discovery_fallback to current_domain/current_forest.",
                hints: new[] {
                    "For forest-wide discovery set discovery_fallback=current_forest or provide forest_name.",
                    "For domain-seeded discovery set discovery_fallback=current_domain or provide domain_name."
                });
        }

        var steps = new List<ScopeDiscoveryStep>();
        var gaps = new List<ScopeDiscoveryGap>();

        var rootDseStep = await ExecuteStepAsync(
            name: "rootdse_context",
            timeoutMs: request.RootDseTimeoutMs,
            endpointsChecked: new[] { explicitDomainController ?? "<auto>" },
            retries: 0,
            operation: () => DomainInfoService.Query(explicitDomainController, cancellationToken),
            outputProjection: static value => new {
                domain_controller = NormalizeOptional(value.DomainController) ?? string.Empty,
                dns_domain_name = NormalizeOptional(value.DnsDomainName) ?? string.Empty,
                forest_dns_name = NormalizeOptional(value.ForestDnsName) ?? string.Empty
            },
            cancellationToken: cancellationToken);
        steps.Add(ToStepModel("rootdse_context", rootDseStep));

        if (!rootDseStep.Ok) {
            gaps.Add(new ScopeDiscoveryGap("rootdse_context", "RootDSE context could not be resolved."));
        }

        var rootDseInfo = rootDseStep.Result;
        var effectiveForest = explicitForest
                             ?? NormalizeOptional(rootDseInfo?.ForestDnsName)
                             ?? ResolveFallbackForest(request.DiscoveryFallback);
        var effectiveDomain = explicitDomain
                             ?? NormalizeOptional(rootDseInfo?.DnsDomainName)
                             ?? ResolveFallbackDomain(request.DiscoveryFallback);

        var domains = new List<string>();
        if (explicitDomain is not null) {
            domains.Add(explicitDomain);
        }
        if (request.IncludeDomains.Count > 0) {
            domains.AddRange(request.IncludeDomains.Select(NormalizeHostOrName).Where(static x => !string.IsNullOrWhiteSpace(x)));
        }

        if (domains.Count == 0 && !string.IsNullOrWhiteSpace(effectiveForest)) {
            var domainEnumerationStep = await ExecuteStepAsync(
                name: "domains:forest_enumeration",
                timeoutMs: request.DomainEnumerationTimeoutMs,
                endpointsChecked: new[] { effectiveForest! },
                retries: 0,
                operation: () => DomainHelper.EnumerateForestDomainNames(effectiveForest, includeTrustedDomains: request.IncludeTrusts, cancellationToken: cancellationToken)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeHostOrName)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                outputProjection: static value => new {
                    count = value.Length,
                    sample = value.Take(5).ToArray()
                },
                cancellationToken: cancellationToken);
            steps.Add(ToStepModel("domains:forest_enumeration", domainEnumerationStep));

            if (domainEnumerationStep.Ok && domainEnumerationStep.Result is not null) {
                domains.AddRange(domainEnumerationStep.Result);
            } else {
                gaps.Add(new ScopeDiscoveryGap("domains", "Forest domain enumeration failed."));
            }
        }

        if (domains.Count == 0 && !string.IsNullOrWhiteSpace(effectiveDomain)) {
            domains.Add(effectiveDomain!);
        }

        var includeDomainSet = BuildSet(request.IncludeDomains);
        var excludeDomainSet = BuildSet(request.ExcludeDomains);
        domains = domains
            .Select(NormalizeHostOrName)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(domain => includeDomainSet.Count == 0 || includeDomainSet.Contains(domain))
            .Where(domain => excludeDomainSet.Count == 0 || !excludeDomainSet.Contains(domain))
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .Take(request.MaxDomains)
            .ToList();

        if (domains.Count == 0) {
            gaps.Add(new ScopeDiscoveryGap("domains", "No domains resolved after fallback and include/exclude filtering."));
        }

        var namingContexts = ReadNamingContexts(rootDseInfo);
        if (string.IsNullOrWhiteSpace(namingContexts.DefaultNamingContext)) {
            gaps.Add(new ScopeDiscoveryGap("naming_contexts.default_naming_context", "defaultNamingContext is missing."));
        }
        if (string.IsNullOrWhiteSpace(namingContexts.ConfigurationNamingContext)) {
            gaps.Add(new ScopeDiscoveryGap("naming_contexts.configuration_naming_context", "configurationNamingContext is missing."));
        }
        if (string.IsNullOrWhiteSpace(namingContexts.SchemaNamingContext)) {
            gaps.Add(new ScopeDiscoveryGap("naming_contexts.schema_naming_context", "schemaNamingContext is missing."));
        }
        if (string.IsNullOrWhiteSpace(namingContexts.RootDomainNamingContext)) {
            gaps.Add(new ScopeDiscoveryGap("naming_contexts.root_domain_naming_context", "rootDomainNamingContext is missing."));
        }

        var includeDcSet = BuildSet(request.IncludeDomainControllers);
        var excludeDcSet = BuildSet(request.ExcludeDomainControllers);
        var allDcs = new List<string>();
        var allDcsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byDomain = new List<DomainControllerDomainRow>();

        foreach (var domain in domains) {
            cancellationToken.ThrowIfCancellationRequested();

            var perDomainCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perDomainSteps = new List<ScopeDiscoveryStep>();
            var perDomainMissingReasons = new List<string>();

            AddCandidate(perDomainCandidates, explicitDomainController);
            AddCandidate(perDomainCandidates, DomainHelper.TryGetPdcHostName(domain));

            var dsGetDcStep = await ExecuteStepAsync(
                name: "domain_controllers:dsgetdcname",
                timeoutMs: request.DcSourceTimeoutMs,
                endpointsChecked: new[] { domain },
                retries: 0,
                operation: () => DomainHelper.EnumerateDomainControllersViaDsGetDcName(domain)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeHostOrName)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                outputProjection: static value => new {
                    count = value.Length,
                    sample = value.Take(5).ToArray()
                },
                cancellationToken: cancellationToken);
            perDomainSteps.Add(ToStepModel("domain_controllers:dsgetdcname", dsGetDcStep));
            AddCandidates(perDomainCandidates, dsGetDcStep.Result);

            var dnsSrvStep = await ExecuteStepAsync(
                name: "domain_controllers:dns_srv",
                timeoutMs: request.DcSourceTimeoutMs,
                endpointsChecked: new[] { domain },
                retries: 0,
                operation: () => DomainHelper.EnumerateDomainControllersViaDnsSrv(domain)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeHostOrName)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                outputProjection: static value => new {
                    count = value.Length,
                    sample = value.Take(5).ToArray()
                },
                cancellationToken: cancellationToken);
            perDomainSteps.Add(ToStepModel("domain_controllers:dns_srv", dnsSrvStep));
            AddCandidates(perDomainCandidates, dnsSrvStep.Result);

            var adStep = await ExecuteStepAsync(
                name: "domain_controllers:active_directory",
                timeoutMs: request.DcSourceTimeoutMs,
                endpointsChecked: new[] { domain },
                retries: 0,
                operation: () => DomainHelper.EnumerateDomainControllers(domainName: domain, cancellationToken: cancellationToken)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeHostOrName)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                outputProjection: static value => new {
                    count = value.Length,
                    sample = value.Take(5).ToArray()
                },
                cancellationToken: cancellationToken);
            perDomainSteps.Add(ToStepModel("domain_controllers:active_directory", adStep));
            AddCandidates(perDomainCandidates, adStep.Result);

            var perDomain = perDomainCandidates
                .Select(NormalizeHostOrName)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(dc => includeDcSet.Count == 0 || includeDcSet.Contains(dc))
                .Where(dc => excludeDcSet.Count == 0 || !excludeDcSet.Contains(dc))
                .Where(dc => !request.SkipRodc || !IsRodcBestEffort(dc))
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .Take(request.MaxDomainControllersPerDomain)
                .ToArray();

            if (perDomain.Length == 0) {
                perDomainMissingReasons.Add("No domain controllers discovered after source probing and include/exclude filtering.");
            }

            for (var i = 0; i < perDomain.Length; i++) {
                if (allDcsSet.Add(perDomain[i])) {
                    allDcs.Add(perDomain[i]);
                }
                if (allDcs.Count >= request.MaxDomainControllersTotal) {
                    break;
                }
            }

            byDomain.Add(new DomainControllerDomainRow(
                DomainName: domain,
                DomainControllers: perDomain,
                Sources: perDomainSteps,
                MissingReasons: perDomainMissingReasons));

            if (allDcs.Count >= request.MaxDomainControllersTotal) {
                break;
            }
        }

        if (allDcs.Count == 0) {
            gaps.Add(new ScopeDiscoveryGap("domain_controllers", "No domain controllers discovered for the effective domain set."));
        }
        if (string.IsNullOrWhiteSpace(effectiveForest)) {
            gaps.Add(new ScopeDiscoveryGap("forest_name", "Effective forest name could not be resolved."));
        }
        if (string.IsNullOrWhiteSpace(effectiveDomain)) {
            gaps.Add(new ScopeDiscoveryGap("domain_name", "Effective domain name could not be resolved."));
        }

        var requestedScope = new {
            forest_name = explicitForest ?? string.Empty,
            domain_name = explicitDomain ?? string.Empty,
            domain_controller = explicitDomainController ?? string.Empty,
            include_domains = request.IncludeDomains,
            exclude_domains = request.ExcludeDomains,
            include_domain_controllers = request.IncludeDomainControllers,
            exclude_domain_controllers = request.ExcludeDomainControllers,
            include_trusts = request.IncludeTrusts,
            skip_rodc = request.SkipRodc,
            discovery_fallback = ToDiscoveryFallbackName(request.DiscoveryFallback),
            limits = new {
                max_domains = request.MaxDomains,
                max_domain_controllers_total = request.MaxDomainControllersTotal,
                max_domain_controllers_per_domain = request.MaxDomainControllersPerDomain
            },
            timeouts_ms = new {
                rootdse = request.RootDseTimeoutMs,
                domain_enumeration = request.DomainEnumerationTimeoutMs,
                dc_source = request.DcSourceTimeoutMs
            }
        };
        var chain = BuildChainContract(
            request: request,
            effectiveForest: effectiveForest,
            effectiveDomain: effectiveDomain,
            domains: domains,
            domainControllers: allDcs,
            gaps: gaps,
            steps: steps);

        var model = new {
            requested_scope = requestedScope,
            effective_scope = new {
                forest_name = effectiveForest ?? string.Empty,
                domain_name = effectiveDomain ?? string.Empty,
                naming_contexts = namingContexts
            },
            domains,
            domain_controllers = allDcs,
            domain_controllers_by_domain = byDomain,
            missing = gaps,
            receipt = new {
                steps,
                summary = new {
                    domains = domains.Count,
                    domain_controllers = allDcs.Count,
                    failed_steps = steps.Count(static step => !step.Ok),
                    total_steps = steps.Count
                }
            },
            next_actions = chain.NextActions,
            cursor = chain.Cursor,
            resume_token = chain.ResumeToken,
            flow_id = chain.FlowId,
            step_id = chain.StepId,
            checkpoint = chain.Checkpoint,
            handoff = chain.Handoff,
            confidence = chain.Confidence
        };

        var summary = ToolMarkdown.SummaryText(
            title: "Active Directory: Scope Discovery",
            $"Forest: `{effectiveForest ?? string.Empty}`; Domains: `{domains.Count}`; DCs: `{allDcs.Count}`; Gaps: `{gaps.Count}`.",
            "Use `receipt.steps` to review endpoints checked, timeouts, and failed probes.");

        return ToolResponse.OkModel(model, summaryMarkdown: summary);
    }

    private static ToolChainContractModel BuildChainContract(
        ScopeDiscoveryRequest request,
        string? effectiveForest,
        string? effectiveDomain,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> domainControllers,
        IReadOnlyList<ScopeDiscoveryGap> gaps,
        IReadOnlyList<ScopeDiscoveryStep> steps) {
        var fallbackName = ToDiscoveryFallbackName(request.DiscoveryFallback);
        var handoff = ToolChainingHints.Map(
            ("contract", "ad_scope_discovery_handoff"),
            ("version", 1),
            ("forest_name", effectiveForest ?? string.Empty),
            ("domain_name", effectiveDomain ?? string.Empty),
            ("discovery_fallback", fallbackName),
            ("domains_preview", string.Join(";", domains.Take(10))),
            ("domain_controllers_preview", string.Join(";", domainControllers.Take(15))),
            ("missing_areas", string.Join(";", gaps.Select(static gap => gap.Area).Take(10))));

        var nextActions = new List<ToolNextActionModel> {
            ToolChainingHints.NextAction(
                tool: "ad_forest_discover",
                reason: "Expand trust/domain-controller context and capture per-source discovery receipts for deeper diagnostics.",
                suggestedArguments: ToolChainingHints.Map(
                    ("forest_name", effectiveForest ?? string.Empty),
                    ("domain_name", effectiveDomain ?? string.Empty),
                    ("discovery_fallback", fallbackName))),
            ToolChainingHints.NextAction(
                tool: "ad_monitoring_probe_catalog",
                reason: "Pick the best probe_kind before running deeper health checks.")
        };

        if (!string.IsNullOrWhiteSpace(effectiveDomain) || domainControllers.Count > 0) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "ad_monitoring_probe_run",
                reason: "Run a replication probe against discovered scope to validate operational health.",
                suggestedArguments: ToolChainingHints.Map(
                    ("probe_kind", "replication"),
                    ("domain_name", effectiveDomain ?? string.Empty),
                    ("discovery_fallback", fallbackName))));
        }

        var failedSteps = steps.Count(static step => !step.Ok);
        var failureRatio = steps.Count == 0 ? 1d : (double)failedSteps / steps.Count;
        var gapPenalty = Math.Min(0.35d, gaps.Count * 0.06d);
        var confidence = 0.95d - (failureRatio * 0.45d) - gapPenalty;

        return ToolChainingHints.Create(
            nextActions: nextActions,
            cursor: ToolChainingHints.BuildToken(
                "ad_scope_discovery",
                ("forest", effectiveForest ?? string.Empty),
                ("domain", effectiveDomain ?? string.Empty),
                ("domains", domains.Count.ToString()),
                ("dcs", domainControllers.Count.ToString())),
            resumeToken: ToolChainingHints.BuildToken(
                "ad_scope_discovery.resume",
                ("fallback", fallbackName),
                ("failed_steps", failedSteps.ToString()),
                ("gaps", gaps.Count.ToString())),
            handoff: handoff,
            confidence: confidence,
            flowId: ToolChainingHints.BuildToken(
                "ad_scope_discovery.flow",
                ("forest", effectiveForest ?? string.Empty),
                ("domain", effectiveDomain ?? string.Empty)),
            stepId: "scope_receipt",
            checkpoint: ToolChainingHints.Map(
                ("domains", domains.Count),
                ("domain_controllers", domainControllers.Count),
                ("gaps", gaps.Count),
                ("failed_steps", failedSteps)));
    }

    private static async Task<StepExecutionResult<T>> ExecuteStepAsync<T>(
        string name,
        int timeoutMs,
        IReadOnlyList<string> endpointsChecked,
        int retries,
        Func<T> operation,
        Func<T, object?> outputProjection,
        CancellationToken cancellationToken) {
        var stopwatch = Stopwatch.StartNew();
        try {
            var task = Task.Run(operation, cancellationToken);
            var completed = await Task.WhenAny(task, Task.Delay(timeoutMs, cancellationToken));
            if (completed != task) {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"{name} exceeded timeout ({timeoutMs}ms).");
            }

            var result = await task;
            stopwatch.Stop();
            return new StepExecutionResult<T> {
                Ok = true,
                DurationMs = (int)Math.Min(int.MaxValue, stopwatch.Elapsed.TotalMilliseconds),
                TimeoutMs = timeoutMs,
                EndpointsChecked = endpointsChecked,
                Retries = retries,
                Result = result,
                Output = outputProjection(result)
            };
        } catch (Exception ex) {
            stopwatch.Stop();
            return new StepExecutionResult<T> {
                Ok = false,
                DurationMs = (int)Math.Min(int.MaxValue, stopwatch.Elapsed.TotalMilliseconds),
                TimeoutMs = timeoutMs,
                EndpointsChecked = endpointsChecked,
                Retries = retries,
                Error = SanitizeErrorMessage(ex.Message, $"{name} failed."),
                ErrorType = ex.GetType().FullName
            };
        }
    }

    private static ScopeDiscoveryStep ToStepModel<T>(string name, StepExecutionResult<T> step) {
        return new ScopeDiscoveryStep(
            Name: name,
            Ok: step.Ok,
            DurationMs: step.DurationMs,
            TimeoutMs: step.TimeoutMs,
            EndpointsChecked: step.EndpointsChecked,
            Retries: step.Retries,
            Error: step.Error,
            ErrorType: step.ErrorType,
            Output: step.Output);
    }

    private static NamingContextsModel ReadNamingContexts(DomainInfoQueryResult? info) {
        var attrs = info?.RootDse?.Attributes;
        if (attrs is null) {
            return new NamingContextsModel(null, null, null, null);
        }

        return new NamingContextsModel(
            DefaultNamingContext: ReadAttributeString(attrs, "defaultNamingContext"),
            ConfigurationNamingContext: ReadAttributeString(attrs, "configurationNamingContext"),
            SchemaNamingContext: ReadAttributeString(attrs, "schemaNamingContext"),
            RootDomainNamingContext: ReadAttributeString(attrs, "rootDomainNamingContext"));
    }

    private static string? ReadAttributeString(IReadOnlyDictionary<string, object?> attributes, string key) {
        if (!attributes.TryGetValue(key, out var value) || value is null) {
            return null;
        }

        if (value is string text) {
            return NormalizeOptional(text);
        }

        if (value is IEnumerable enumerable) {
            foreach (var item in enumerable) {
                var normalized = NormalizeOptional(item?.ToString());
                if (normalized is not null) {
                    return normalized;
                }
            }
        }

        return NormalizeOptional(value.ToString());
    }

    private static bool TryParseDiscoveryFallback(
        string raw,
        out DirectoryDiscoveryFallback fallback,
        out string? error) {
        var normalized = NormalizeHostOrName(raw);
        if (DiscoveryFallbackModes.TryGetValue(normalized, out fallback)) {
            error = null;
            return true;
        }

        error = "discovery_fallback must be one of: none, current_domain, current_forest.";
        fallback = DirectoryDiscoveryFallback.None;
        return false;
    }

    private static string ToDiscoveryFallbackName(DirectoryDiscoveryFallback fallback) {
        return fallback switch {
            DirectoryDiscoveryFallback.None => "none",
            DirectoryDiscoveryFallback.CurrentForest => "current_forest",
            _ => "current_domain"
        };
    }

    private static string? ResolveFallbackForest(DirectoryDiscoveryFallback fallback) {
        if (fallback != DirectoryDiscoveryFallback.CurrentForest) {
            return null;
        }

        return NormalizeOptional(DomainHelper.RootDomainName);
    }

    private static string? ResolveFallbackDomain(DirectoryDiscoveryFallback fallback) {
        if (fallback != DirectoryDiscoveryFallback.CurrentDomain) {
            return null;
        }

        return DomainHelper.TryGetCurrentDomainName(out var domain)
            ? NormalizeOptional(domain)
            : null;
    }

    private static HashSet<string> BuildSet(IEnumerable<string>? items) {
        if (items is null) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return items
            .Select(NormalizeHostOrName)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddCandidate(HashSet<string> store, string? value) {
        var normalized = NormalizeHostOrName(value);
        if (!string.IsNullOrWhiteSpace(normalized)) {
            store.Add(normalized);
        }
    }

    private static void AddCandidates(HashSet<string> store, IEnumerable<string>? values) {
        if (values is null) {
            return;
        }

        foreach (var value in values) {
            AddCandidate(store, value);
        }
    }

    private static string NormalizeHostOrName(string? input) {
        if (string.IsNullOrWhiteSpace(input)) {
            return string.Empty;
        }

        return input.Trim().TrimEnd('.');
    }

    private static string? NormalizeOptional(string? value) {
        var normalized = NormalizeHostOrName(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsRodcBestEffort(string host) {
        if (string.IsNullOrWhiteSpace(host)) {
            return false;
        }

        try {
            return DomainHelper.IsReadOnlyDc(host);
        } catch {
            var normalized = NormalizeHostOrName(host);
            var separator = normalized.IndexOf('.');
            var label = separator >= 0 ? normalized[..separator] : normalized;
            return label.StartsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
                   label.EndsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
                   label.Contains("-rodc", StringComparison.OrdinalIgnoreCase) ||
                   label.Contains("rodc-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
