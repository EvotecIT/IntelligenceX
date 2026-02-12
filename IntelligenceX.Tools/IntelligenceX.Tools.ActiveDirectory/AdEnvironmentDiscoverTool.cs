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

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Discovers effective AD query context (forest/domain/DC/base DN) for agent planning.
/// </summary>
public sealed class AdEnvironmentDiscoverTool : ActiveDirectoryToolBase, ITool {
    private const int MaxDomainControllersCap = 100;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_environment_discover",
        "Discover Active Directory context (forest/domain/domain controllers/base DN) and return effective scope hints for follow-up tools.",
        ToolSchema.Object(
                ("domain_controller", ToolSchema.String("Optional domain controller override (host/FQDN).")),
                ("search_base_dn", ToolSchema.String("Optional search base DN override.")),
                ("include_domain_controllers", ToolSchema.Boolean("When true, include discovered domain controller candidates. Default true.")),
                ("max_domain_controllers", ToolSchema.Integer("Maximum discovered domain controllers returned (capped). Default 20.")))
            .NoAdditionalProperties());

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

        LdapToolContextHelper.LdapSearchContext context;
        try {
            context = LdapToolContextHelper.ResolveSearchContext(
                explicitDomainController: ToolArgs.GetOptionalTrimmed(arguments, "domain_controller"),
                explicitBaseDn: ToolArgs.GetOptionalTrimmed(arguments, "search_base_dn"),
                defaultDomainController: ToolArgs.NormalizeOptional(Options.DomainController),
                defaultBaseDn: ToolArgs.NormalizeOptional(Options.DefaultSearchBaseDn),
                cancellationToken: cancellationToken);
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error(
                "not_configured",
                $"AD context discovery failed: {ex.Message}",
                hints: new[] {
                    "Try providing domain_controller (FQDN) and/or search_base_dn (DN).",
                    "Use ad_domain_info to validate RootDSE connectivity."
                }));
        }

        DomainInfoQueryResult info;
        try {
            info = DomainInfoService.Query(context.DomainController, cancellationToken);
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error(
                "not_configured",
                $"AD RootDSE query failed after context discovery: {ex.Message}",
                hints: new[] {
                    "Verify LDAP connectivity to the discovered domain controller.",
                    "Provide domain_controller explicitly if auto-discovery chose an unreachable endpoint."
                }));
        }

        var discoveredDomainControllers = includeDomainControllers
            ? DiscoverDomainControllers(info.DnsDomainName, context.DomainController, maxDomainControllers, cancellationToken)
            : Array.Empty<string>();

        var model = new {
            Context = new {
                DomainController = context.DomainController ?? string.Empty,
                SearchBaseDn = context.BaseDn,
                DomainControllerSource = ToSourceName(context.DomainControllerSource),
                SearchBaseDnSource = ToSourceName(context.BaseDnSource)
            },
            Domain = new {
                DnsDomainName = info.DnsDomainName,
                ForestDnsName = info.ForestDnsName
            },
            DomainControllers = discoveredDomainControllers,
            RootDse = new LdapToolOutputRow {
                Attributes = info.RootDse.Attributes,
                TruncatedAttributes = info.RootDse.TruncatedAttributes
            }
        };

        var facts = new List<(string Key, string Value)> {
            ("Domain controller", context.DomainController ?? string.Empty),
            ("Search base DN", context.BaseDn),
            ("DNS domain", info.DnsDomainName),
            ("Forest", info.ForestDnsName),
            ("Discovered DCs", discoveredDomainControllers.Count.ToString())
        };

        return Task.FromResult(ToolResponse.OkFactsModel(
            model: model,
            title: "Active Directory: Environment Discovery",
            facts: facts,
            meta: ToolOutputHints.Meta(count: 1, truncated: false),
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }

    private static IReadOnlyList<string> DiscoverDomainControllers(
        string? dnsDomainName,
        string? preferredDomainController,
        int maxDomainControllers,
        CancellationToken cancellationToken) {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddCandidate(List<string> target, HashSet<string> targetSeen, string? value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0) {
                return;
            }

            if (targetSeen.Add(normalized)) {
                target.Add(normalized);
            }
        }

        AddCandidate(list, seen, preferredDomainController);

        var domain = ToolArgs.NormalizeOptional(dnsDomainName);
        if (domain is null) {
            return list;
        }

        AddCandidate(list, seen, DomainHelper.TryGetPdcHostName(domain));

        try {
            foreach (var dc in DomainHelper.EnumerateDomainControllersViaDsGetDcName(domain)) {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(list, seen, dc);
                if (list.Count >= maxDomainControllers) {
                    return list;
                }
            }
        } catch {
            // Best-effort only.
        }

        try {
            foreach (var dc in DomainHelper.EnumerateDomainControllersViaDnsSrv(domain)) {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(list, seen, dc);
                if (list.Count >= maxDomainControllers) {
                    return list;
                }
            }
        } catch {
            // Best-effort only.
        }

        try {
            foreach (var dc in DomainHelper.EnumerateDomainControllers(domainName: domain, cancellationToken: cancellationToken)) {
                AddCandidate(list, seen, dc);
                if (list.Count >= maxDomainControllers) {
                    return list;
                }
            }
        } catch {
            // Best-effort only.
        }

        if (list.Count > maxDomainControllers) {
            return list.Take(maxDomainControllers).ToArray();
        }

        return list;
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
