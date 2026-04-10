using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Validates planner and lexical-routing prompts include schema hints.
/// </summary>
public sealed class ChatServicePlannerPromptTests {
    private static readonly MethodInfo TokenizeRoutingTokensMethod =
        typeof(ChatServiceSession).GetMethod("TokenizeRoutingTokens", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TokenizeRoutingTokens not found.");

    private static readonly MethodInfo SelectWeightedToolSubsetMethod =
        typeof(ChatServiceSession).GetMethod("SelectWeightedToolSubset", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("SelectWeightedToolSubset not found.");
    private static readonly MethodInfo EnsureMinimumToolSelectionMethod =
        typeof(ChatServiceSession).GetMethod("EnsureMinimumToolSelection", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("EnsureMinimumToolSelection not found.");

    private static readonly MethodInfo ResolveMaxCandidateToolsSettingMethod =
        typeof(ChatServiceSession).GetMethod("ResolveMaxCandidateToolsSetting", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveMaxCandidateToolsSetting not found.");
    private static readonly MethodInfo ResolveContextAwareCompatibleHttpDefaultMaxCandidateToolsMethod =
        typeof(ChatServiceSession).GetMethod("ResolveContextAwareCompatibleHttpDefaultMaxCandidateTools", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveContextAwareCompatibleHttpDefaultMaxCandidateTools not found.");
    private static readonly MethodInfo ResolveMaxCandidateToolsForTurnMethod =
        typeof(ChatServiceSession).GetMethod("ResolveMaxCandidateToolsForTurn", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveMaxCandidateToolsForTurn not found.");
    private static readonly FieldInfo ModelListCacheField =
        typeof(ChatServiceSession).GetField("_modelListCache", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_modelListCache not found.");
    private static readonly Type ModelListCacheEntryType =
        typeof(ChatServiceSession).GetNestedType("ModelListCacheEntry", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ModelListCacheEntry type not found.");

    [Fact]
    public void BuildModelPlannerPrompt_IncludesSchemaArgumentsRequiredAndTableViewTrait() {
        var definitions = new List<ToolDefinition> {
            new(
                "eventlog_top_events",
                "Return top events from a log.",
                ToolSchema.Object(
                        ("log_name", ToolSchema.String("Log name.")),
                        ("machine_name", ToolSchema.String("Remote host.")))
                    .WithTableViewOptions()
                    .Required("log_name")
                    .NoAdditionalProperties())
        };

        var prompt = BuildModelPlannerPrompt(
            "top 5 system events from AD0",
            definitions,
            6);

        Assert.Contains("required: log_name", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("args: ", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traits: execution(local_or_remote), table_view_projection", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesRemoteHostAndTargetScopeTraits() {
        var definitions = new List<ToolDefinition> {
            new(
                "system_tls_posture",
                "Inspect TLS posture for a remote host.",
                ToolSchema.Object(
                        ("computer_name", ToolSchema.String("Remote host.")),
                        ("search_base_dn", ToolSchema.String("Optional directory scope.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "inspect tls posture on the same domain controller",
            definitions,
            4);

        Assert.Contains("pack: system", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role: operational", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("execution(local_or_remote)", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_scoping(search_base_dn, computer_name)", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote_host_targeting(computer_name)", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPackEngineAliasesAndDeclaredPackTraits() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_gpo_health",
                "Summarize GPO health for the selected domain.",
                ToolSchema.Object(("domain_name", ToolSchema.String("Domain DNS name."))).Required("domain_name").NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "AD Playground",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "check the same domain gpo health",
            definitions,
            4,
            packAvailability: new[] {
                new ToolPackAvailabilityInfo {
                    Id = "ADPlayground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    EngineId = "ADPlayground",
                    Aliases = new[] { "ad", "adplayground" },
                    CapabilityTags = new[] { "Remote Analysis", "Directory", "GPO" },
                    Enabled = true
                }
            });

        Assert.Contains("pack: active_directory", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_name: AD Playground", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_aliases: ad, adplayground", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("engine: adplayground", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_traits: remote_analysis, directory, gpo", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_PrioritizesPackScopeTraitsAheadOfDomainLabels() {
        var definitions = new List<ToolDefinition> {
            new(
                "system_inventory_summary",
                "Inspect a host inventory snapshot.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Host name."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "System",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "check cpu and memory on the same server",
            definitions,
            4,
            packAvailability: new[] {
                new ToolPackAvailabilityInfo {
                    Id = "system",
                    Name = "ComputerX",
                    SourceKind = "builtin",
                    EngineId = "computerx",
                    Aliases = new[] { "computerx" },
                    CapabilityTags = new[] { "CPU", "Remote Analysis", "Local Analysis" },
                    Enabled = true
                }
            });

        Assert.Contains("pack_traits: local_analysis, remote_analysis, cpu", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesExecutionLocalitySummaryForMixedCatalog() {
        var definitions = new List<ToolDefinition> {
            new(
                "system_local_trace_query",
                "Inspect local traces only.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Host label."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                }),
            new(
                "eventlog_live_query",
                "Query remote event logs.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOrRemote
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "continue on remaining DCs",
            definitions,
            4);

        Assert.Contains("Execution locality:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Current candidate tools have mixed locality", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prefer remote-ready tools for host/DC-targeted work", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesExecutionLocalitySummaryForLocalOnlyCatalog() {
        var definitions = new List<ToolDefinition> {
            new(
                "system_local_trace_query",
                "Inspect local traces only.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Host label."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "continue on remaining DCs",
            definitions,
            4);

        Assert.Contains("Execution locality:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Current candidate tools are local-only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("available tools here are local-only", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesSetupRecoveryAndHandoffContractHints() {
        var definitions = new List<ToolDefinition> {
            new(
                "eventlog_timeline_query",
                "Query event timeline from a host.",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("channel", ToolSchema.String("Channel.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_channels_list"
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "machine_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    RecoveryToolNames = new[] { "eventlog_channels_list" }
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "inspect timeline on the same domain controller and continue into host diagnostics",
            definitions,
            4);

        Assert.Contains("setup: eventlog_channels_list", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recovery: eventlog_channels_list", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("handoff: system/system_info", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesWriteAuthenticationAndProbeContractHints() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_user_disable",
                "Disable an AD user.",
                ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }),
            new(
                "eventlog_live_query",
                "Inspect live event logs on a host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list",
                    ProbeIdArgumentName = "probe_id"
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "inspect live event logs and only then disable the affected account if needed",
            definitions,
            4);

        Assert.Contains("Contract-backed planning rules:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prefer declared probe/setup helpers before dependent remote or mutating follow-up tools.", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Treat tools marked auth-required as needing a valid runtime auth/profile context.", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Treat tools marked write-capable as confirmation-gated follow-up actions", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("write: mutating", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auth: required(ix.auth.runtime.v1)", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auth_args: profile_id, probe_id", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("probe: eventlog_channels_list", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesCategoryFamilyAndTagsHints() {
        var definitions = new List<ToolDefinition> {
            new(
                "domaindetective_domain_summary",
                "Domain posture summary.",
                ToolSchema.Object(("domain", ToolSchema.String("Domain name."))).Required("domain").NoAdditionalProperties(),
                category: "dns",
                tags: new[] { "intent:public_domain", "pack:domaindetective", "domain_family:public_domain" },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "domaindetective",
                    Role = ToolRoutingTaxonomy.RoleOperational,
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "summarize contoso.com",
            definitions,
            4);

        Assert.Contains("category: dns", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("family: public_domain", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent:public_domain", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:domaindetective", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesContinuationFocusUnresolvedAskWhenPresent() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_replication_summary",
                "Summarize AD replication health.",
                ToolSchema.Object(("forest_name", ToolSchema.String("Forest DNS name."))).NoAdditionalProperties())
        };

        var prompt = BuildModelPlannerPrompt(
            """
            [Continuation focus]
            ix:continuation-focus:v1
            last_user_goal: Summarize the forest replication state in a table.
            last_unresolved_ask: Explain why ADRODC is absent from the forest replication rows.
            last_primary_artifact: table

            [Working memory checkpoint]
            ix:working-memory:v1
            intent_anchor: Run forest-wide replication and LDAP diagnostics.
            follow_up: where is ADRODC in the table?
            """,
            definitions,
            4);

        Assert.Contains("User request:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("where is ADRODC in the table?", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Current unresolved follow-up focus:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Explain why ADRODC is absent from the forest replication rows.", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesContinuationFocusCachedEvidenceReusePreferenceWhenPresent() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_replication_summary",
                "Summarize AD replication health.",
                ToolSchema.Object(("forest_name", ToolSchema.String("Forest DNS name."))).NoAdditionalProperties())
        };

        var prompt = BuildModelPlannerPrompt(
            """
            [Continuation focus]
            ix:continuation-focus:v1
            last_user_goal: Continue from the same forest replication evidence.
            last_prefer_cached_evidence_reuse: true
            last_cached_evidence_reuse_reason: compact continuation should reuse the latest forest replication evidence snapshot
            last_primary_artifact: prose

            [Working memory checkpoint]
            ix:working-memory:v1
            intent_anchor: Continue from the same forest replication evidence.
            follow_up: continue replication AD2
            """,
            definitions,
            4);

        Assert.Contains("User request:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("continue replication AD2", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Continuation preference:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Reuse the latest fresh read-only evidence snapshot if it is still sufficient.",
            prompt,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Preference reason: compact continuation should reuse the latest forest replication evidence snapshot",
            prompt,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPlannerContextExecutionPreferencesHandoffAndSkills() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_ldap_diagnostics",
                "Run LDAP diagnostics.",
                ToolSchema.Object(("domain_name", ToolSchema.String("Domain DNS name."))).NoAdditionalProperties()),
            new(
                "system_hardware_summary",
                "Summarize hardware state.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties())
        };

        var prompt = BuildModelPlannerPrompt(
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            missing_live_evidence: cert status and memory usage
            preferred_pack_ids: active_directory, system
            preferred_tool_names: ad_ldap_diagnostics, system_hardware_summary
            structured_next_action_source_tools: ad_monitoring_probe_run
            structured_next_action_reason: inspect ldaps certificate details on the same domain controller
            structured_next_action_confidence: 0.88
            preferred_execution_backends: system_service_list=cim
            handoff_target_pack_ids: system
            handoff_target_tool_names: system_metrics_summary
            matching_skills: ad_domain.scope_hosts, system.host_baseline
            allow_cached_evidence_reuse: false

            continue from the same DC scope
            """,
            definitions,
            4);

        Assert.Contains("Execution intent:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fresh live execution is required", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Missing live evidence: cert status and memory usage", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred packs: active_directory, system", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred tools: ad_ldap_diagnostics, system_hardware_summary", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Structured source tools: ad_monitoring_probe_run", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Structured next-action reason: inspect ldaps certificate details on the same domain controller", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Structured next-action confidence: 0.88", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred execution backends: system_service_list=cim", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target packs: system", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target tools: system_metrics_summary", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Matching reusable skills:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_domain.scope_hosts, system.host_baseline", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesBackgroundPreparationHintsWhenPlannerContextPresent() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_ldap_diagnostics",
                "Run LDAP diagnostics.",
                ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties()),
            new(
                "system_certificate_posture",
                "Inspect certificate posture.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties())
        };

        var prompt = BuildModelPlannerPrompt(
            """
            [Planner context]
            ix:planner-context:v1
            background_preparation_allowed: true
            background_pending_read_only_actions: 1
            background_pending_unknown_actions: 1
            background_follow_up_classes: verification, normalization
            background_priority_focus: critical verification
            background_follow_up_focus: verify ldap certificate posture; prepare compact operator handoff
            background_recent_evidence_tools: ad_ldap_diagnostics, system_certificate_posture

            continue from the same domain controller
            """,
            definitions,
            4);

        Assert.Contains("Background preparation:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read-only follow-up preparation is allowed for this thread.", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending read-only actions: 1", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending unknown-mutability actions: 1", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Follow-up classes: verification, normalization", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Priority focus: critical verification", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preparation focus: verify ldap certificate posture; prepare compact operator handoff", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recent evidence tools:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_diagnostics", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system_certificate_posture", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesLdapCertificateFollowthroughTargetsWhenProvided() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_ldap_diagnostics",
                "Run LDAP diagnostics.",
                ToolSchema.Object(("domain_name", ToolSchema.String("Domain DNS name."))).NoAdditionalProperties()),
            new(
                "system_certificate_posture",
                "Inspect certificate posture.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties())
        };

        var prompt = BuildModelPlannerPrompt(
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            missing_live_evidence: ldap certificate posture
            preferred_pack_ids: active_directory, system
            preferred_tool_names: ad_ldap_diagnostics, system_certificate_posture
            handoff_target_pack_ids: system
            handoff_target_tool_names: system_certificate_posture
            allow_cached_evidence_reuse: false

            continue from the same discovered domain controllers and inspect their certificate posture
            """,
            definitions,
            4);

        Assert.Contains("Missing live evidence: ldap certificate posture", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred tools: ad_ldap_diagnostics, system_certificate_posture", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target tools: system_certificate_posture", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesRepresentativeContractExamplesAndCrossPackPivots() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_environment_discover",
                "Discover Active Directory environment scope.",
                ToolSchema.Object(
                        ("domain_controller", ToolSchema.String("Domain controller to target.")),
                        ("search_base_dn", ToolSchema.String("Base DN to scope the query.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "domain_controller",
                                    TargetArgument = "machine_name"
                                }
                            }
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetRole = ToolRoutingTaxonomy.RoleDiagnostic,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "domain_controller",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "eventlog_live_query",
                "Inspect live event logs on a host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "system_metrics_summary",
                "Collect CPU, memory, and disk health.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                })
        };

        var prompt = BuildModelPlannerPrompt(
            "investigate the affected domain controller and continue into host evidence",
            definitions,
            6);

        Assert.Contains("Contract-backed capability hints:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Representative live tool examples:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("domain controller or base DN", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event logs", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CPU, memory, and disk", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cross-pack follow-up pivots: Event Log, System", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildModelPlannerPrompt_FocusesRepresentativeExamplesOnPreferredPlannerPacks() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_environment_discover",
                "Discover Active Directory environment scope.",
                ToolSchema.Object(
                        ("domain_controller", ToolSchema.String("Domain controller to target.")),
                        ("search_base_dn", ToolSchema.String("Base DN to scope the query.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
                }),
            new(
                "eventlog_live_query",
                "Inspect live event logs on a host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "system_metrics_summary",
                "Collect CPU, memory, and disk health.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                })
        };

        var prompt = BuildModelPlannerPrompt(
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            preferred_pack_ids: eventlog

            continue with event evidence on the same host
            """,
            definitions,
            4);

        var sectionStart = prompt.IndexOf("Contract-backed capability hints:", StringComparison.OrdinalIgnoreCase);
        Assert.True(sectionStart >= 0);
        var sectionEnd = prompt.IndexOf("Return at most", sectionStart, StringComparison.OrdinalIgnoreCase);
        Assert.True(sectionEnd > sectionStart);
        var hintSection = prompt[sectionStart..sectionEnd];

        Assert.Contains("event logs", hintSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain controller or base DN", hintSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CPU, memory, and disk", hintSection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_UsesPackOwnedRepresentativeExamplesWithoutChatHardcoding() {
        var definitions = new List<ToolDefinition> {
            new(
                "custom_probe",
                "Probe custom runtime state.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticRepresentativeExamplePack() });
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);

        var prompt = session.BuildModelPlannerPromptForTesting(
            "inspect the custom endpoint state",
            definitions,
            limit: 3);

        Assert.Contains("Representative live tool examples:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inspect the custom endpoint state through pack-owned metadata", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("event logs", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CPU, memory, and disk", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_UsesPackOwnedPreferredProbeAndRecipeHintsWithoutChatHardcoding() {
        var definitions = new List<ToolDefinition> {
            new(
                "custom_connectivity_probe",
                "Probe custom runtime state.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "custom_followup",
                "Collect custom follow-up evidence.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticRepresentativeExamplePack() });
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);

        var prompt = session.BuildModelPlannerPromptForTesting(
            "inspect the remote custom endpoint",
            definitions,
            limit: 4);

        Assert.Contains("Prefer pack-declared preferred probe tools", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_probe: preferred", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_recipes: custom_runtime_triage", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adplayground", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesRegisteredDeferredWorkAffordancesForFocusedPacks() {
        var definitions = new List<ToolDefinition> {
            new(
                "email_message_compose",
                "Compose an email summary.",
                ToolSchema.Object(("subject", ToolSchema.String("Message subject."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "report_snapshot_publish",
                "Publish a report snapshot.",
                ToolSchema.Object(("report_name", ToolSchema.String("Report name."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "testimox_analytics",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticDeferredAffordancePromptPack() });
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "email",
                    Name = "Email",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityEmail },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "testimox_analytics",
                    Name = "TestimoX Analytics",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 0,
                RoutingAwareTools = 0,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });
        session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);

        var prompt = session.BuildModelPlannerPromptForTesting(
            """
            [Planner context]
            ix:planner-context:v1
            preferred_pack_ids: email

            send the follow-up summary
            """,
            definitions,
            limit: 4);

        Assert.Contains("Deferred follow-up affordances:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Email [email]", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reporting [reporting]", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("send an email summary after the run", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email[pack_declared:email]", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_EmitsPreferredDeferredCapabilityIdsFromPlannerContext() {
        var definitions = new List<ToolDefinition> {
            new(
                "email_message_compose",
                "Compose an email summary.",
                ToolSchema.Object(("subject", ToolSchema.String("Message subject."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "report_snapshot_publish",
                "Publish a report snapshot.",
                ToolSchema.Object(("report_name", ToolSchema.String("Report name."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "testimox_analytics",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticDeferredAffordancePromptPack() });
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "email",
                    Name = "Email",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityEmail },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "testimox_analytics",
                    Name = "TestimoX Analytics",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 0,
                RoutingAwareTools = 0,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });
        session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);

        var prompt = session.BuildModelPlannerPromptForTesting(
            """
            [Planner context]
            ix:planner-context:v1
            preferred_deferred_work_capability_ids: reporting

            prepare the deliverable
            """,
            definitions,
            limit: 4);

        Assert.Contains("Preferred deferred follow-up capabilities: reporting", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report_snapshot_publish", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesDeferredWorkAffordancesWithoutCapabilitySnapshot() {
        var definitions = new List<ToolDefinition> {
            new(
                "email_message_compose",
                "Compose an email summary.",
                ToolSchema.Object(("subject", ToolSchema.String("Message subject."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "report_snapshot_publish",
                "Publish a report snapshot.",
                ToolSchema.Object(("report_name", ToolSchema.String("Report name."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "testimox_analytics",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] {
                new SyntheticCapabilityDescriptorPack(
                    "email",
                    new[] { ToolPackCapabilityTags.DeferredCapabilityEmail },
                    "email_message_compose"),
                new SyntheticCapabilityDescriptorPack(
                    "testimox_analytics",
                    new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    "report_snapshot_publish"),
                new SyntheticDeferredAffordancePromptPack()
            });
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);

        var prompt = session.BuildModelPlannerPromptForTesting(
            """
            [Planner context]
            ix:planner-context:v1
            preferred_deferred_work_capability_ids: reporting

            prepare the deliverable
            """,
            definitions,
            limit: 4);

        Assert.Contains("Deferred follow-up affordances:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reporting [reporting]", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvePackIdsForDeferredWorkCapabilityPreferences_MapsCapabilityIdsToRegisteredPacks() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "email",
                    Name = "Email",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityEmail },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "testimox_analytics",
                    Name = "TestimoX Analytics",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "customx",
                    Name = "CustomX",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 0,
                RoutingAwareTools = 0,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });

        var resolvedPackIds = session.ResolvePackIdsForDeferredWorkCapabilityPreferencesForTesting(new[] { "Reporting", "email" });

        Assert.Equal(new[] { "email", "testimox_analytics" }, resolvedPackIds);
    }

    [Fact]
    public void ResolvePackIdsForDeferredWorkCapabilityPreferences_UsesOrchestrationCatalogWhenCapabilitySnapshotMissing() {
        var definitions = new List<ToolDefinition> {
            new(
                "email_message_compose",
                "Compose an email summary.",
                ToolSchema.Object(("subject", ToolSchema.String("Message subject."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "report_snapshot_publish",
                "Publish the prepared deliverable artifact for later sharing.",
                ToolSchema.Object(("report_name", ToolSchema.String("Report name."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "testimox_analytics",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] {
                new SyntheticCapabilityDescriptorPack(
                    "email",
                    new[] { ToolPackCapabilityTags.DeferredCapabilityEmail },
                    "email_message_compose"),
                new SyntheticCapabilityDescriptorPack(
                    "testimox_analytics",
                    new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    "report_snapshot_publish")
            });
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);

        var resolvedPackIds = session.ResolvePackIdsForDeferredWorkCapabilityPreferencesForTesting(new[] { "Reporting", "email" });

        Assert.Equal(new[] { "email", "testimox_analytics" }, resolvedPackIds);
    }

    [Fact]
    public void ResolvePackIdsForDeferredWorkCapabilityPreferences_DoesNotReturnDisabledPackFromAvailability() {
        var definitions = new List<ToolDefinition> {
            new(
                "report_snapshot_publish",
                "Publish the prepared deliverable artifact for later sharing.",
                ToolSchema.Object(("report_name", ToolSchema.String("Report name."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "testimox_analytics",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] {
                new SyntheticCapabilityDescriptorPack(
                    "testimox_analytics",
                    new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    "report_snapshot_publish")
            });
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "testimox_analytics",
                    Name = "TestimoX Analytics",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    Enabled = false
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 0,
                RoutingAwareTools = 0,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });

        var resolvedPackIds = session.ResolvePackIdsForDeferredWorkCapabilityPreferencesForTesting(new[] { "reporting" });

        Assert.Empty(resolvedPackIds);
    }

    [Fact]
    public void BuildPlannerContextAugmentedRequest_UsesStructuredNextActionHintsWithoutCheckpoint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var toolDefinitions = new List<ToolDefinition> {
            new(
                "ad_monitoring_probe_run",
                "Run AD monitoring probe.",
                ToolSchema.Object(("probe_kind", ToolSchema.String("Probe kind."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "ad_ldap_diagnostics",
                "Run LDAP diagnostics.",
                ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                })
        };
        var toolCalls = new List<IntelligenceX.Chat.Abstractions.Protocol.ToolCallDto> {
            new() { CallId = "call-ldap", Name = "ad_monitoring_probe_run" }
        };
        var toolOutputs = new List<IntelligenceX.Chat.Abstractions.Protocol.ToolOutputDto> {
            new() {
                CallId = "call-ldap",
                Ok = true,
                Output = """
                         {"ok":true,"chain_confidence":0.88,"next_actions":[{"tool":"ad_ldap_diagnostics","mutating":false,"reason":"inspect ldaps certificate details on the same domain controller","arguments":{"domain_controller":"ad0.contoso.com"}}]}
                         """
            }
        };

        session.RememberStructuredNextActionCarryoverForTesting(
            "thread-planner-carryover",
            toolDefinitions,
            toolCalls,
            toolOutputs,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                ["ad_ldap_diagnostics"] = false
            });

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            "thread-planner-carryover",
            "can you check ldap certificates now?",
            toolDefinitions);

        Assert.Contains("ix:planner-context:v1", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_pack_ids: active_directory", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_tool_names: ad_ldap_diagnostics", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured_next_action_source_tools: ad_monitoring_probe_run", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured_next_action_reason: inspect ldaps certificate details on the same domain controller", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured_next_action_confidence: 0.88", augmented, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlannerContextAugmentedRequest_SeedsDeferredDescriptorPreferencesWithoutCheckpoint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "eventlog_live_query",
                Description = "Query remote event logs.",
                PackId = "eventlog",
                Category = "event-log",
                ExecutionScope = "local_or_remote",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                RepresentativeExamples = new[] { "query event logs from remote host" }
            },
            new ToolDefinitionDto {
                Name = "ops_inventory_collect",
                Description = "Collect remote host inventory.",
                PackId = "ops_inventory",
                Category = "system",
                ExecutionScope = "remote_only",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                RepresentativeExamples = new[] { "collect inventory from remote host" }
            }
        });

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            "thread-planner-deferred-descriptor",
            "can you query event logs from srv1?",
            Array.Empty<ToolDefinition>());

        Assert.Contains("ix:planner-context:v1", augmented, StringComparison.OrdinalIgnoreCase);
        var parsed = ChatServiceSession.TryReadPlannerContextFromRequestTextForTesting(
            augmented,
            out _,
            out _,
            out var preferredPackIds,
            out var preferredToolNames,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);

        Assert.True(parsed);
        Assert.NotEmpty(preferredPackIds);
        Assert.NotEmpty(preferredToolNames);
        Assert.Equal("eventlog", preferredPackIds[0]);
        Assert.Equal("eventlog_live_query", preferredToolNames[0]);
    }

    [Fact]
    public void ResolveDeferredToolPreferenceHints_ProjectsDescriptorHandoffTargetsIntoPreferredHints() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "ops_inventory_collect",
                Description = "Collect remote host inventory.",
                PackId = "ops_inventory",
                Category = "system",
                ExecutionScope = "remote_only",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                HandoffTargetPackIds = new[] { "eventlog" },
                HandoffTargetToolNames = new[] { "eventlog_live_query" },
                RepresentativeExamples = new[] { "collect inventory from remote host" }
            },
            new ToolDefinitionDto {
                Name = "eventlog_live_query",
                Description = "Query remote event logs from a matched host.",
                PackId = "eventlog",
                Category = "event-log",
                ExecutionScope = "local_or_remote",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                RepresentativeExamples = new[] { "query event logs from remote host" }
            }
        });

        var hints = session.ResolveDeferredToolPreferenceHintsForTesting(
            "collect inventory from srv1",
            options: null,
            maxPreferredPackIds: 2,
            maxPreferredToolNames: 2);

        Assert.True(hints.HasAnyMatches);
        Assert.Equal(new[] { "ops_inventory", "eventlog" }, hints.PreferredPackIds);
        Assert.Equal(new[] { "ops_inventory_collect", "eventlog_live_query" }, hints.PreferredToolNames);
        Assert.Equal(new[] { "ops_inventory" }, hints.ActivatablePackIds);
    }

    [Fact]
    public void BuildPlannerContextAugmentedRequest_SeedsDeferredDescriptorHandoffPreferencesWithoutCheckpoint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "ops_inventory_collect",
                Description = "Collect remote host inventory.",
                PackId = "ops_inventory",
                Category = "system",
                ExecutionScope = "remote_only",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                HandoffTargetPackIds = new[] { "eventlog" },
                HandoffTargetToolNames = new[] { "eventlog_live_query" },
                RepresentativeExamples = new[] { "collect inventory from remote host" }
            },
            new ToolDefinitionDto {
                Name = "eventlog_live_query",
                Description = "Query remote event logs from a matched host.",
                PackId = "eventlog",
                Category = "event-log",
                ExecutionScope = "local_or_remote",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                RepresentativeExamples = new[] { "query event logs from remote host" }
            }
        });

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            "thread-planner-deferred-handoff-descriptor",
            "collect inventory from srv1",
            Array.Empty<ToolDefinition>());

        var parsed = ChatServiceSession.TryReadPlannerContextFromRequestTextForTesting(
            augmented,
            out _,
            out _,
            out var preferredPackIds,
            out var preferredToolNames,
            out _,
            out _,
            out var handoffTargetPackIds,
            out var handoffTargetToolNames,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);

        Assert.True(parsed);
        Assert.Equal(new[] { "ops_inventory", "eventlog" }, preferredPackIds);
        Assert.Equal(new[] { "ops_inventory_collect", "eventlog_live_query" }, preferredToolNames);
        Assert.Empty(handoffTargetPackIds);
        Assert.Empty(handoffTargetToolNames);
    }

    [Fact]
    public void BuildPlannerContextAugmentedRequest_CarriesPreferredDeferredWorkCapabilityIdsFromWorkingMemory() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-planner-deferred-capabilities";
        var toolDefinitions = new List<ToolDefinition> {
            new(
                "email_message_compose",
                "Compose an email summary.",
                ToolSchema.Object(("subject", ToolSchema.String("Message subject."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Prepare the operator deliverable and send the follow-up.",
            domainIntentFamily: "public_domain",
            recentToolNames: new[] { "email_message_compose" },
            recentEvidenceSnippets: new[] { "email_message_compose: draft summary is ready." },
            priorAnswerPlanUserGoal: "Prepare the follow-up deliverables.",
            priorAnswerPlanUnresolvedNow: "send the report and email follow-up",
            priorAnswerPlanPreferredDeferredWorkCapabilityIds: new[] { "Reporting", "email" });

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            threadId,
            "prepare the deliverable",
            toolDefinitions);

        var parsed = ChatServiceSession.TryReadPlannerContextFromRequestTextForTesting(
            augmented,
            out var requiresLiveExecution,
            out var missingLiveEvidence,
            out var preferredPackIds,
            out var preferredToolNames,
            out var preferredDeferredWorkCapabilityIds,
            out var preferredExecutionBackends,
            out var handoffTargetPackIds,
            out var handoffTargetToolNames,
            out var continuationSourceTool,
            out var continuationReason,
            out var continuationConfidence,
            out var backgroundPreparationAllowed,
            out var backgroundPendingReadOnlyActions,
            out var backgroundPendingUnknownActions,
            out var backgroundFollowUpFocus,
            out var backgroundRecentEvidenceTools,
            out var matchingSkills,
            out var allowCachedEvidenceReuse);

        Assert.True(parsed);
        Assert.False(requiresLiveExecution);
        Assert.Equal(string.Empty, missingLiveEvidence);
        Assert.Empty(preferredPackIds);
        Assert.Empty(preferredToolNames);
        Assert.Equal(new[] { "reporting", "email" }, preferredDeferredWorkCapabilityIds);
        Assert.Empty(preferredExecutionBackends);
        Assert.Empty(handoffTargetPackIds);
        Assert.Empty(handoffTargetToolNames);
        Assert.Equal(string.Empty, continuationSourceTool);
        Assert.Equal(string.Empty, continuationReason);
        Assert.Equal(string.Empty, continuationConfidence);
        Assert.False(backgroundPreparationAllowed);
        Assert.Equal(0, backgroundPendingReadOnlyActions);
        Assert.Equal(0, backgroundPendingUnknownActions);
        Assert.Equal(string.Empty, backgroundFollowUpFocus);
        Assert.Empty(backgroundRecentEvidenceTools);
        Assert.Empty(matchingSkills);
        Assert.False(allowCachedEvidenceReuse);
    }

    [Fact]
    public void BuildPlannerContextAugmentedRequest_IncludesBackgroundPreparationHintsFromPendingActionsAndEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var threadId = "thread-planner-background";
        var toolDefinitions = new List<ToolDefinition> {
            new(
                "ad_ldap_diagnostics",
                "Run LDAP diagnostics.",
                ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "system_certificate_posture",
                "Inspect certificate posture.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                })
        };

        session.RememberPendingActionsForTesting(
            threadId,
            """
            [Action]
            ix:action:v1
            id: verify_ldaps
            title: Verify LDAPS certificate posture
            request: verify ldap certificate posture on the same domain controller
            readonly: true
            reply: /act verify_ldaps

            [Action]
            ix:action:v1
            id: handoff_note
            title: Prepare operator handoff
            request: prepare compact operator handoff for the same incident scope
            reply: /act handoff_note
            """);
        session.RememberThreadToolEvidenceForTesting(
            threadId,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ldap",
                    Name = "ad_ldap_diagnostics",
                    ArgumentsJson = "{\"domain_controller\":\"ad0.contoso.com\"}"
                },
                new() {
                    CallId = "call-cert",
                    Name = "system_certificate_posture",
                    ArgumentsJson = "{\"computer_name\":\"ad0.contoso.com\"}"
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ldap",
                    Ok = true,
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "LDAP diagnostics completed."
                },
                new() {
                    CallId = "call-cert",
                    Ok = true,
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "Certificate posture collected."
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            threadId,
            "continue from the same DC",
            toolDefinitions);

        Assert.Contains("ix:planner-context:v1", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_preparation_allowed: true", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_pending_read_only_actions: 1", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_pending_unknown_actions: 1", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_follow_up_focus: verify ldap certificate posture on the same domain controller; prepare compact operator handoff for the same incident scope", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_recent_evidence_tools:", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_diagnostics", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system_certificate_posture", augmented, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlannerContextAugmentedRequest_IncludesBackgroundClassificationHintsFromTaggedHandoffs() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-planner-background-classification";
        var toolDefinitions = new List<ToolDefinition> {
            new(
                "ad_user_lifecycle",
                "Manage AD user lifecycle.",
                ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_object_get",
                            TargetRole = ToolRoutingTaxonomy.RoleResolver,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Critical,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "distinguished_name",
                                    TargetArgument = "identity"
                                }
                            }
                        }
                    }
                },
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }),
            new(
                "remote_disk_inventory",
                "Inspect remote disk inventory.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Enrichment,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Low,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "ad_object_get",
                "Get AD object.",
                ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties()),
            new(
                "system_info",
                "Inspect system info.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote host."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(toolDefinitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            toolDefinitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ad-write",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"dave","operation":"disable"}"""
                },
                new() {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv11.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ad-write",
                    Ok = true,
                    Output = """{"ok":true,"distinguished_name":"CN=dave,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                },
                new() {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            threadId,
            "continue with the prepared follow-up",
            toolDefinitions);

        Assert.Contains("background_follow_up_classes: verification, enrichment", augmented, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_priority_focus: critical verification", augmented, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlannerContextRoundTrip_ParsesAugmentedRequestEmittedByBuilder() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var threadId = "thread-planner-roundtrip";
        var toolDefinitions = new List<ToolDefinition> {
            new(
                "ad_monitoring_probe_run",
                "Run AD monitoring probe.",
                ToolSchema.Object(("probe_kind", ToolSchema.String("Probe kind."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "ad_ldap_diagnostics",
                "Run LDAP diagnostics.",
                ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                })
        };

        session.RememberStructuredNextActionCarryoverForTesting(
            threadId,
            toolDefinitions,
            new List<IntelligenceX.Chat.Abstractions.Protocol.ToolCallDto> {
                new() { CallId = "call-ldap", Name = "ad_monitoring_probe_run" }
            },
            new List<IntelligenceX.Chat.Abstractions.Protocol.ToolOutputDto> {
                new() {
                    CallId = "call-ldap",
                    Ok = true,
                    Output = """
                             {"ok":true,"next_actions":[{"tool":"ad_ldap_diagnostics","mutating":false,"reason":"prefer ad_ldap_diagnostics after prior tool output","confidence":"medium","arguments":{"domain_controller":"ad0.contoso.com"}}]}
                             """
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                ["ad_ldap_diagnostics"] = false
            });
        session.RememberThreadToolEvidenceForTesting(
            threadId,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ldap-pref",
                    Name = "ad_ldap_diagnostics",
                    ArgumentsJson = "{\"domain_controller\":\"ad0.contoso.com\",\"engine\":\"cim\"}"
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ldap-pref",
                    Ok = true,
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "LDAP diagnostics completed.",
                    MetaJson = "{\"engine_preference\":\"cim\"}"
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            threadId,
            "can you check ldap certificates now?",
            toolDefinitions);

        var parsed = ChatServiceSession.TryReadPlannerContextFromRequestTextForTesting(
            augmented,
            out var requiresLiveExecution,
            out var missingLiveEvidence,
            out var preferredPackIds,
            out var preferredToolNames,
            out var preferredDeferredWorkCapabilityIds,
            out var preferredExecutionBackends,
            out var handoffTargetPackIds,
            out var handoffTargetToolNames,
            out var continuationSourceTool,
            out var continuationReason,
            out var continuationConfidence,
            out var backgroundPreparationAllowed,
            out var backgroundPendingReadOnlyActions,
            out var backgroundPendingUnknownActions,
            out var backgroundFollowUpFocus,
            out var backgroundRecentEvidenceTools,
            out var matchingSkills,
            out var allowCachedEvidenceReuse);

        Assert.True(parsed);
        Assert.False(requiresLiveExecution);
        Assert.Equal(string.Empty, missingLiveEvidence);
        Assert.Contains("active_directory", preferredPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_diagnostics", preferredToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(preferredDeferredWorkCapabilityIds);
        Assert.Contains("ad_ldap_diagnostics=cim", preferredExecutionBackends, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(handoffTargetPackIds);
        Assert.Empty(handoffTargetToolNames);
        Assert.Equal("ad_monitoring_probe_run", continuationSourceTool);
        Assert.Equal("prefer ad_ldap_diagnostics after prior tool output", continuationReason);
        Assert.Equal("medium", continuationConfidence);
        Assert.False(backgroundPreparationAllowed);
        Assert.Equal(0, backgroundPendingReadOnlyActions);
        Assert.Equal(0, backgroundPendingUnknownActions);
        Assert.Equal(string.Empty, backgroundFollowUpFocus);
        Assert.Empty(backgroundRecentEvidenceTools);
        Assert.Empty(matchingSkills);
        Assert.False(allowCachedEvidenceReuse);
    }

    [Fact]
    public void TryReadPlannerContextFromRequestText_ParsesContextWhenBlockAppearsBeforeUserRequest() {
        var parsed = ChatServiceSession.TryReadPlannerContextFromRequestTextForTesting(
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            missing_live_evidence: ldap certificate posture
            preferred_pack_ids: active_directory, system
            preferred_tool_names: ad_ldap_diagnostics, system_hardware_summary
            preferred_deferred_work_capability_ids: reporting, Email
            handoff_target_pack_ids: system
            handoff_target_tool_names: system_metrics_summary
            continuation_source_tool: ad_monitoring_probe_run
            continuation_reason: prefer ad_ldap_diagnostics after prior tool output
            continuation_confidence: high
            matching_skills: ad_domain.scope_hosts, system.host_baseline
            allow_cached_evidence_reuse: false

            can you check ldap certificates now?
            """,
            out var requiresLiveExecution,
            out var missingLiveEvidence,
            out var preferredPackIds,
            out var preferredToolNames,
            out var preferredDeferredWorkCapabilityIds,
            out var preferredExecutionBackends,
            out var handoffTargetPackIds,
            out var handoffTargetToolNames,
            out var continuationSourceTool,
            out var continuationReason,
            out var continuationConfidence,
            out var backgroundPreparationAllowed,
            out var backgroundPendingReadOnlyActions,
            out var backgroundPendingUnknownActions,
            out var backgroundFollowUpFocus,
            out var backgroundRecentEvidenceTools,
            out var matchingSkills,
            out var allowCachedEvidenceReuse);

        Assert.True(parsed);
        Assert.True(requiresLiveExecution);
        Assert.Equal("ldap certificate posture", missingLiveEvidence);
        Assert.Contains("active_directory", preferredPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", preferredPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_diagnostics", preferredToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_hardware_summary", preferredToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reporting", preferredDeferredWorkCapabilityIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("email", preferredDeferredWorkCapabilityIds, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(preferredExecutionBackends);
        Assert.Contains("system", handoffTargetPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_metrics_summary", handoffTargetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ad_monitoring_probe_run", continuationSourceTool);
        Assert.Equal("prefer ad_ldap_diagnostics after prior tool output", continuationReason);
        Assert.Equal("high", continuationConfidence);
        Assert.False(backgroundPreparationAllowed);
        Assert.Equal(0, backgroundPendingReadOnlyActions);
        Assert.Equal(0, backgroundPendingUnknownActions);
        Assert.Equal(string.Empty, backgroundFollowUpFocus);
        Assert.Empty(backgroundRecentEvidenceTools);
        Assert.Contains("ad_domain.scope_hosts", matchingSkills, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system.host_baseline", matchingSkills, StringComparer.OrdinalIgnoreCase);
        Assert.False(allowCachedEvidenceReuse);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesSchemaTokens() {
        var definition = new ToolDefinition(
            "eventlog_top_events",
            "Return top events from a log.",
            ToolSchema.Object(
                    ("log_name", ToolSchema.String("Log name.")),
                    ("machine_name", ToolSchema.String("Remote host.")))
                .WithTableViewOptions()
                .Required("log_name")
                .NoAdditionalProperties());

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("log_name", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table view projection", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesRemoteHostAndTargetScopeTraitTokens() {
        var definition = new ToolDefinition(
            "system_tls_posture",
            "Inspect TLS posture for a remote host.",
            ToolSchema.Object(
                    ("computer_name", ToolSchema.String("Remote host.")),
                    ("server", ToolSchema.String("Server alias.")),
                    ("search_base_dn", ToolSchema.String("Optional directory scope.")))
                .NoAdditionalProperties());

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("remote_host_targeting", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_scope", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("execution local_or_remote", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("computer_name", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("search_base_dn", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesExplicitRoutingRoleTokens() {
        var definition = new ToolDefinition(
            "eventlog_pack_info",
            "Describe event log pack guidance.",
            ToolSchema.Object().NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RolePackInfo
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("role pack_info", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role:pack_info", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesSetupRecoveryAndHandoffTokens() {
        var definition = new ToolDefinition(
            "eventlog_timeline_query",
            "Query event timeline from a host.",
            ToolSchema.Object(
                    ("machine_name", ToolSchema.String("Remote machine.")),
                    ("channel", ToolSchema.String("Channel.")))
                .NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupToolName = "eventlog_channels_list"
            },
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "system",
                        TargetToolName = "system_metrics_summary",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "machine_name",
                                TargetArgument = "computer_name"
                            }
                        }
                    }
                }
            },
            recovery: new ToolRecoveryContract {
                IsRecoveryAware = true,
                RecoveryToolNames = new[] { "eventlog_channels_list" }
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("setup_tool eventlog_channels_list", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recovery_tool eventlog_channels_list", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("handoff_target system/system_metrics_summary", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesEnvironmentDiscoveryAndSetupMetadataTokens() {
        var definition = new ToolDefinition(
            "ad_environment_discover",
            "Discover Active Directory environment scope.",
            ToolSchema.Object(
                    ("domain_controller", ToolSchema.String("Domain controller.")),
                    ("search_base_dn", ToolSchema.String("Base DN.")))
                .NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupToolName = "ad_environment_catalog",
                SetupHintKeys = new[] { "ad.scope" },
                Requirements = new[] {
                    new ToolSetupRequirement {
                        RequirementId = "directory_scope",
                        Kind = ToolSetupRequirementKinds.Configuration,
                        HintKeys = new[] { "ad.dc" }
                    }
                }
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("environment_discover", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("environment_discovery", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setup_aware", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setup_tool ad_environment_catalog", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setup_requirement directory_scope", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setup_kind configuration", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setup_hint ad.scope", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setup_hint ad.dc", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_DistinguishesLdapCertificatesFromMachineCertificateStores() {
        var ldapDefinition = new ToolDefinition(
            "ad_ldap_diagnostics",
            "Test LDAP/LDAPS endpoints including LDAPS endpoint certificates and certificate metadata.",
            ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            tags: new[] { "intent:ldap_certificates", "protocol:ldaps" });
        var systemDefinition = new ToolDefinition(
            "system_certificate_posture",
            "Return machine certificate-store posture for the host, not LDAP/LDAPS endpoint certificates.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Computer"))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            tags: new[] { "scope:host_certificate_store" });

        var ldapSearchText = BuildToolRoutingSearchText(ldapDefinition);
        var systemSearchText = BuildToolRoutingSearchText(systemDefinition);

        Assert.Contains("ldap", ldapSearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ldaps", ldapSearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent:ldap_certificates", ldapSearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("machine certificate-store posture", systemSearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not ldap/ldaps endpoint certificates", systemSearchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesPackTokensFromExplicitRoutingMetadata() {
        var definition = new ToolDefinition(
            "ad_get_users",
            "Read directory users.",
            ToolSchema.Object(("domain", ToolSchema.String("Domain DNS name."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("pack active_directory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack adplayground", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack ad_playground", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesActiveDirectoryLiteralAliasWhenPackIsExplicit() {
        var definition = new ToolDefinition(
            "directory_scope_probe",
            "Directory scope probe.",
            ToolSchema.Object(("domain", ToolSchema.String("Domain DNS name."))).NoAdditionalProperties(),
            category: "ad",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("pack ad", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack active_directory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:active_directory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack ad_playground", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:ad_playground", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesComputerXAliasForExplicitSystemPack() {
        var definition = new ToolDefinition(
            "inventory_collect",
            "Collect host inventory.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            category: "computerx",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("pack system", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack computerx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack computer_x", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:computer_x", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesDnsClientXAliasTokensFromExplicitPackMetadata() {
        var definition = new ToolDefinition(
            "dns_client_x_query",
            "Query DNS records.",
            ToolSchema.Object(("name", ToolSchema.String("Name."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "dnsclientx",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("pack dnsclientx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:dnsclientx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack dns_client_x", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:dns_client_x", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesDomainDetectivePackTokensFromExplicitPackMetadata() {
        var definition = new ToolDefinition(
            "domain-detective-query",
            "Query domain evidence.",
            ToolSchema.Object(("name", ToolSchema.String("Name."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "domaindetective",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("pack domaindetective", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:domaindetective", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack domain_detective", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:domain_detective", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesTestimoXUnderscoreAliasTokensForExplicitPack() {
        var definition = new ToolDefinition(
            "health_rules",
            "Run TestimoX rules.",
            ToolSchema.Object(("scope", ToolSchema.String("Scope."))).NoAdditionalProperties(),
            category: "testimox",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "testimox",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("pack testimox", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:testimox", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack testimo_x", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:testimo_x", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesEventLogUnderscoreAliasTokensForExplicitPack() {
        var definition = new ToolDefinition(
            "event_log_query",
            "Query event log entries.",
            ToolSchema.Object(("log_name", ToolSchema.String("Log name."))).NoAdditionalProperties(),
            category: "event_log",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(definition);

        Assert.Contains("pack eventlog", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:eventlog", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack event_log", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:event_log", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_PrefersRuntimePackSearchTokens_WhenPackPublishesThem() {
        var definition = new ToolDefinition(
            "ops_inventory_query",
            "Query remote host inventory.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "ops_inventory",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(
            definition,
            packAvailability: new[] {
                new ToolPackAvailabilityInfo {
                    Id = "ops_inventory",
                    Name = "Ops Inventory",
                    SourceKind = "open_source",
                    Category = "system",
                    EngineId = "computerx",
                    SearchTokens = new[] { "server_inventory", "cpu", "memory", "disk", "computerx" },
                    Enabled = true
                }
            });

        Assert.Contains("pack ops_inventory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack server_inventory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack computerx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack cpu", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pack adplayground", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesRuntimePackAliases_WhenPackSelfRegistersThem() {
        var definition = new ToolDefinition(
            "ops_inventory_query",
            "Query remote host inventory.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "serverops",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(
            definition,
            packAvailability: new[] {
                new ToolPackAvailabilityInfo {
                    Id = "ops_inventory",
                    Name = "Ops Inventory",
                    SourceKind = "open_source",
                    Aliases = new[] { "serverops", "host_inventory" },
                    Enabled = true
                }
            });

        Assert.Contains("pack serverops", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack host_inventory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack ops_inventory", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesRuntimePackCategoryEngineAndCapabilityTags_WhenPackSelfRegistersThem() {
        var definition = new ToolDefinition(
            "ops_inventory_query",
            "Query remote host inventory.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "ops_inventory",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var searchText = BuildToolRoutingSearchText(
            definition,
            packAvailability: new[] {
                new ToolPackAvailabilityInfo {
                    Id = "ops_inventory",
                    Name = "Ops Inventory",
                    SourceKind = "open_source",
                    Category = "system",
                    EngineId = "computerx",
                    CapabilityTags = new[] { "remote_analysis", "host_inventory", "server_health" },
                    Enabled = true
                }
            });

        Assert.Contains("category system", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_category system", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("engine computerx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("engine:computerx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capability remote_analysis", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capability host_inventory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capability server_health", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_UsesPackOwnedRepresentativeExamplesFromOrchestrationCatalogWithoutChatHardcoding() {
        var definition = new ToolDefinition(
            "custom_probe",
            "Probe custom runtime state.",
            ToolSchema.Object(("target", ToolSchema.String("Target."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            });
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(new[] { definition }, new IToolPack[] { new SyntheticRepresentativeExamplePack() });

        var searchText = BuildToolRoutingSearchText(
            definition,
            orchestrationCatalog: orchestrationCatalog);

        Assert.Contains(
            "inspect the custom endpoint state through pack-owned metadata",
            searchText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adplayground", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_UsesPackOwnedPreferredProbeAndRecipeHintsWithoutChatHardcoding() {
        var definitions = new[] {
            new ToolDefinition(
                "custom_connectivity_probe",
                "Probe custom runtime state.",
                ToolSchema.Object(("target", ToolSchema.String("Target."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new ToolDefinition(
                "custom_followup",
                "Collect custom follow-up evidence.",
                ToolSchema.Object(("target", ToolSchema.String("Target."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticRepresentativeExamplePack() });

        var searchText = BuildToolRoutingSearchText(
            definitions[0],
            orchestrationCatalog: orchestrationCatalog);

        Assert.Contains("preferred_probe_tool", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custom_runtime_triage", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stabilize the remote endpoint before deeper follow-up", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eventviewerx", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TokenizeRoutingTokens_PreservesSeparatorAwarePackAliasTokensAndCompactVariants() {
        var result = TokenizeRoutingTokensMethod.Invoke(
            null,
            new object?[] { "Use pack:domain_detective and pack:dns-client-x with pack:testimo_x.", 16 });
        var tokens = Assert.IsType<string[]>(result);

        Assert.Contains(tokens, token => string.Equals(token, "domain_detective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "domaindetective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "dns_client_x", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "dnsclientx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "testimo_x", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "testimox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TokenizeRoutingTokens_PreservesShortPackAliasToken_WhenPrefixedByPackMarker() {
        var result = TokenizeRoutingTokensMethod.Invoke(
            null,
            new object?[] { "Use pack:ad and pack:system for this task.", 16 });
        var tokens = Assert.IsType<string[]>(result);

        Assert.Contains(tokens, token => string.Equals(token, "ad", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "system", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TokenizeRoutingTokens_PreservesNaturalLanguageTokensWithoutHardcodedCompoundPackExpansion() {
        var result = TokenizeRoutingTokensMethod.Invoke(
            null,
            new object?[] {
                "Run domain detective and dns client x checks, then active directory, ad playground, computer x, and testimo x posture.",
                48
            });
        var tokens = Assert.IsType<string[]>(result);

        Assert.Contains(tokens, token => string.Equals(token, "domain", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "detective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "dns", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "client", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "active", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "playground", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "computer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "testimo", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tokens, token => string.Equals(token, "domaindetective", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tokens, token => string.Equals(token, "dnsclientx", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tokens, token => string.Equals(token, "activedirectory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_UsesRequestedLimit_WhenPromptHasNoRoutingSignal() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        var args = new object?[] {
            definitions,
            "Please summarize release readiness trends for this quarter.",
            4,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.Equal(4, selected.Count);
        Assert.Equal("ix_probe_tool_00", selected[0].Name);
        Assert.Equal("ix_probe_tool_03", selected[3].Name);
    }

    [Fact]
    public void SelectWeightedToolSubset_NoSignalFallback_DiversifiesAcrossToolFamilies() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 10; i++) {
            definitions.Add(new ToolDefinition(
                $"ad_query_{i:D2}",
                "AD query.",
                ToolSchema.Object(("target", ToolSchema.String("Target"))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        for (var i = 0; i < 3; i++) {
            definitions.Add(new ToolDefinition(
                $"eventlog_query_{i:D2}",
                "Event query.",
                ToolSchema.Object(("target", ToolSchema.String("Target"))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        for (var i = 0; i < 3; i++) {
            definitions.Add(new ToolDefinition(
                $"system_info_{i:D2}",
                "System query.",
                ToolSchema.Object(("target", ToolSchema.String("Target"))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        var args = new object?[] {
            definitions,
            "Please summarize release readiness trends for this quarter.",
            4,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.Equal(4, selected.Count);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_query_00", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_query_00", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool => string.Equals(tool.Name, "system_info_00", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_UsesFullToolSet_WhenWeightedRoutingIsSkippedForShortPrompt() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        var args = new object?[] {
            definitions,
            "hello there",
            6,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.Equal(definitions.Count, selected.Count);
        Assert.Equal("ix_probe_tool_00", selected[0].Name);
        Assert.Equal("ix_probe_tool_19", selected[19].Name);
    }

    [Fact]
    public void SelectWeightedToolSubset_WidensSelection_WhenTopScoresAreAmbiguous() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 10; i++) {
            definitions.Add(new ToolDefinition(
                $"signal_tool_{i:D2}",
                "Collect telemetryx diagnostics.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        for (var i = 0; i < 10; i++) {
            definitions.Add(new ToolDefinition(
                $"other_tool_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        var args = new object?[] {
            definitions,
            "Please summarize telemetryx diagnostics across multiple hosts for this environment now.",
            8,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.Equal(10, selected.Count);
        Assert.Equal("signal_tool_00", selected[0].Name);
        Assert.Equal("signal_tool_09", selected[9].Name);

        var insights = Assert.IsAssignableFrom<System.Collections.IEnumerable>(args[3]);
        var hasAmbiguityMarker = false;
        foreach (var insight in insights) {
            if (insight is null) {
                continue;
            }

            var reasonProperty = insight.GetType().GetProperty("Reason", BindingFlags.Public | BindingFlags.Instance);
            var reason = reasonProperty?.GetValue(insight)?.ToString() ?? string.Empty;
            if (reason.IndexOf("ix:routing-ambiguity:v1", StringComparison.OrdinalIgnoreCase) >= 0) {
                hasAmbiguityMarker = true;
                break;
            }
        }

        Assert.True(hasAmbiguityMarker);
    }

    [Fact]
    public void SelectWeightedToolSubset_IncludesExplicitEscapedToolReferenceWhenScoresAreOtherwiseFlat() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"eventlog_evtx_probe_{i:D2}",
                "EventLog EVTX probe helper.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        definitions.Add(new ToolDefinition(
            "eventlog_evtx_probe",
            "Canonical EVTX probe tool.",
            ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));

        var args = new object?[] {
            definitions,
            "Please explain exactly what `eventlog\\_evtx\\_probe` does in this pack and whether I should use it for forensic timeline baselining across this environment.",
            4,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.InRange(selected.Count, 4, 8);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_evtx_probe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_PrefersAdLdapDiagnostics_ForLdapCertificateRequests() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        definitions.Add(new ToolDefinition(
            "ad_ldap_diagnostics",
            "Test LDAP/LDAPS endpoints for domain controllers, including LDAPS endpoint certificates, SAN/name match, and certificate metadata.",
            ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            tags: new[] { "intent:ldap_certificates", "protocol:ldap", "protocol:ldaps" }));
        definitions.Add(new ToolDefinition(
            "system_certificate_posture",
            "Return machine certificate-store posture for the host, not LDAP/LDAPS endpoint certificates.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Computer"))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            tags: new[] { "scope:host_certificate_store" }));

        var args = new object?[] {
            definitions,
            "Please rerun the LDAP certificates check on the same domain controllers and verify the LDAPS certificate details.",
            1,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.NotEmpty(selected);
        Assert.Equal("ad_ldap_diagnostics", selected[0].Name);
        Assert.DoesNotContain(
            selected.Take(1),
            static definition => string.Equals(definition.Name, "system_certificate_posture", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_BoostsToolsMatchingContinuationFocusUnresolvedAsk() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 12; i++) {
            definitions.Add(new ToolDefinition(
                $"forest_table_summary_{i:D2}",
                "Summarize the full forest replication state in a wide table and compare naming contexts, replication edges, largest delta, and error distribution per controller.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        for (var i = 0; i < 4; i++) {
            definitions.Add(new ToolDefinition(
                $"adrodc_gap_probe_{i:D2}",
                "Explain why ADRODC is absent from forest replication rows and identify the missing controller evidence.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        var requestText = """
            [Continuation focus]
            ix:continuation-focus:v1
            last_user_goal: Summarize the full forest replication state in a wide table and compare naming contexts, replication edges, largest delta, and error distribution per controller.
            last_unresolved_ask: Explain why ADRODC is absent from the forest replication rows.
            last_primary_artifact: table

            [Working memory checkpoint]
            ix:working-memory:v1
            intent_anchor: Run forest-wide replication and LDAP diagnostics.
            follow_up: please explain it carefully for me
            """;

        var args = new object?[] {
            definitions,
            requestText,
            4,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.InRange(selected.Count, 4, 10);
        Assert.Contains(selected, tool => tool.Name.StartsWith("adrodc_gap_probe_", StringComparison.OrdinalIgnoreCase));

        var insights = Assert.IsAssignableFrom<System.Collections.IEnumerable>(args[3]);
        var hasFocusReason = false;
        foreach (var insight in insights) {
            if (insight is null) {
                continue;
            }

            var reasonProperty = insight.GetType().GetProperty("Reason", BindingFlags.Public | BindingFlags.Instance);
            var reason = reasonProperty?.GetValue(insight)?.ToString() ?? string.Empty;
            if (reason.IndexOf("unresolved focus match", StringComparison.OrdinalIgnoreCase) >= 0) {
                hasFocusReason = true;
                break;
            }
        }

        Assert.True(hasFocusReason);
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersToolsMatchingContinuationFocusUnresolvedAsk() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"forest_table_summary_{i:D2}",
                "Summarize the full forest replication state in a wide table and compare naming contexts, replication edges, largest delta, and error distribution per controller.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        for (var i = 0; i < 4; i++) {
            definitions.Add(new ToolDefinition(
                $"adrodc_gap_probe_{i:D2}",
                "Explain why ADRODC is absent from forest replication rows and identify the missing controller evidence.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Continuation focus]
            ix:continuation-focus:v1
            last_user_goal: Summarize the forest replication state in a table.
            last_unresolved_ask: Explain why ADRODC is absent from the forest replication rows.
            last_primary_artifact: table

            [Working memory checkpoint]
            ix:working-memory:v1
            intent_anchor: Run forest-wide replication and LDAP diagnostics.
            follow_up: where is ADRODC in the table?
            """,
            4);

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => tool.Name.StartsWith("adrodc_gap_probe_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersPreferredPackAndToolTargetsFromPlannerContext() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "ad_ldap_diagnostics",
            "Run LDAP diagnostics.",
            ToolSchema.Object(("domain_name", ToolSchema.String("Domain DNS name."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "system_hardware_summary",
            "Summarize hardware state.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            preferred_pack_ids: system
            preferred_tool_names: ad_ldap_diagnostics
            handoff_target_pack_ids: system
            handoff_target_tool_names: system_hardware_summary
            allow_cached_evidence_reuse: false

            continue from the same DC scope
            """,
            4);

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool => string.Equals(tool.Name, "system_hardware_summary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersExactHandoffTargetToolBeforeSiblingPackHelpers() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "eventlog_connectivity_probe",
            "Probe remote event log connectivity.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));
        definitions.Add(new ToolDefinition(
            "eventlog_runtime_profile_validate",
            "Validate event log runtime profile readiness.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "eventlog_live_query",
            "Inspect recent event log records from a remote host.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: false
            preferred_pack_ids: eventlog
            preferred_tool_names: eventlog_live_query
            structured_next_action_source_tools: system_pack_info
            structured_next_action_reason: continue with the declared handoff target from the prior tool result
            handoff_target_pack_ids: eventlog
            handoff_target_tool_names: eventlog_live_query
            continuation_source_tool: system_pack_info
            continuation_reason: continue with the declared handoff target from the prior tool result
            allow_cached_evidence_reuse: false

            show me the recent login failures after the system probe
            """,
            4);

        Assert.InRange(selected.Count, 24, 24);
        Assert.Equal("eventlog_live_query", selected[0].Name);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_runtime_profile_validate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_SuppressesSiblingProbeHelperWhenExactTargetNeedsNoHelpers() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "custom_connectivity_probe",
            "Probe custom runtime reachability.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));
        definitions.Add(new ToolDefinition(
            "custom_followup",
            "Collect custom follow-up evidence.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "custom_secondary_followup",
            "Collect a different custom follow-up that still requires probe preflight.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            authentication: new ToolAuthenticationContract {
                IsAuthenticationAware = true,
                RequiresAuthentication = true,
                Mode = ToolAuthenticationMode.ProfileReference,
                SupportsConnectivityProbe = true,
                ProbeToolName = "custom_connectivity_probe"
            },
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            preferred_pack_ids: customx
            preferred_tool_names: custom_followup
            handoff_target_pack_ids: customx
            handoff_target_tool_names: custom_followup
            continuation_source_tool: system_pack_info
            continuation_reason: continue with the declared handoff target from the prior tool result
            allow_cached_evidence_reuse: false

            continue with the custom follow-up step
            """,
            4,
            orchestrationCatalog: orchestrationCatalog);

        Assert.InRange(selected.Count, 24, 24);
        Assert.Equal("custom_followup", selected[0].Name);
        Assert.DoesNotContain(selected, tool => string.Equals(tool.Name, "custom_connectivity_probe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersDeferredWorkCapabilityTargetsWithoutCapabilitySnapshot() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"identity_probe_{i:D2}",
                "Inspect identity lifecycle compliance ownership evidence for the current environment.",
                ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "report_snapshot_publish",
            "Publish the prepared deliverable artifact for later sharing.",
            ToolSchema.Object(("report_name", ToolSchema.String("Report name."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "testimox_analytics",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var orchestrationCatalog = ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] {
                new SyntheticCapabilityDescriptorPack(
                    "testimox_analytics",
                    new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    "report_snapshot_publish")
            });

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            preferred_deferred_work_capability_ids: reporting

            inspect identity lifecycle compliance ownership evidence for the current environment
            """,
            4,
            orchestrationCatalog: orchestrationCatalog);

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "report_snapshot_publish", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersStructuredNextActionHintsProjectedIntoPlannerContext() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "ad_monitoring_probe_run",
            "Run AD monitoring probe.",
            ToolSchema.Object(("probe_kind", ToolSchema.String("Probe kind."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "ad_ldap_diagnostics",
            "Run LDAP diagnostics.",
            ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));

        session.RememberStructuredNextActionCarryoverForTesting(
            "thread-planner-candidates-carryover",
            definitions,
            new List<IntelligenceX.Chat.Abstractions.Protocol.ToolCallDto> {
                new() { CallId = "call-ldap", Name = "ad_monitoring_probe_run" }
            },
            new List<IntelligenceX.Chat.Abstractions.Protocol.ToolOutputDto> {
                new() {
                    CallId = "call-ldap",
                    Ok = true,
                    Output = """
                             {"ok":true,"chain_confidence":0.88,"next_actions":[{"tool":"ad_ldap_diagnostics","mutating":false,"reason":"inspect ldaps certificate details on the same domain controller","arguments":{"domain_controller":"ad0.contoso.com"}}]}
                             """
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                ["ad_ldap_diagnostics"] = false
            });

        var augmented = session.BuildPlannerContextAugmentedRequestForTesting(
            "thread-planner-candidates-carryover",
            "can you check ldap certificates now?",
            definitions);

        var selected = BuildModelPlannerCandidates(definitions, augmented, 4);

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_UsesDeferredDescriptorHintsAsDirectRankingPrior() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "zzz_eventlog_live_query",
            "Inspect runtime records.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "zzz_eventlog_live_query",
                Description = "Query remote event logs.",
                PackId = "eventlog",
                Category = "event-log",
                ExecutionScope = "local_or_remote",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                RepresentativeExamples = new[] { "show recent login failures from remote domain controller" }
            }
        });

        var selected = session.BuildModelPlannerCandidatesForTesting(
            definitions,
            "show recent login failures from dc1",
            4,
            ToolOrchestrationCatalog.Build(definitions));

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "zzz_eventlog_live_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TryApplyDeferredActivatedPackToolScopeForTesting_ScopesToStrongActivePackMatch() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_info",
                "Inspect system details on a target machine.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "system_pack_info",
                "Inspect system pack metadata.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "system_pack_info",
                Description = "Inspect system pack metadata.",
                PackId = "system",
                RepresentativeExamples = new[] { "run system_pack_info" }
            },
            new ToolDefinitionDto {
                Name = "eventlog_live_query",
                Description = "Inspect login failures from a remote domain controller.",
                PackId = "eventlog",
                RepresentativeExamples = new[] { "show recent login failures from remote domain controller" }
            }
        });

        var activationOutput = await session.ExecuteToolAsyncForTesting(
            threadId: "thread-scope-system-pack",
            userRequest: "run system_pack_info",
            call: new ToolCall(
                callId: "call-scope-system-pack",
                name: "system_pack_info",
                input: "{}",
                arguments: new JsonObject(),
                raw: new JsonObject()),
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);
        Assert.True(activationOutput.Ok is true, activationOutput.Output);

        var scoped = session.TryApplyDeferredActivatedPackToolScopeForTesting(
            "run system_pack_info",
            options: null,
            definitions,
            hasExplicitToolEnableSelectors: false,
            continuationContractDetected: false,
            executionContractApplies: false,
            hasPendingActionContext: false,
            hasToolActivity: false,
            out var scopedDefinitions,
            out var scopedPackIds);

        Assert.True(scoped);
        Assert.Single(scopedPackIds);
        Assert.Equal("system", scopedPackIds[0], ignoreCase: true);
        Assert.Equal(2, scopedDefinitions.Count);
        Assert.Equal("system_pack_info", scopedDefinitions[0].Name, ignoreCase: true);
        Assert.All(scopedDefinitions, definition =>
            Assert.Equal("system", definition.Routing?.PackId, ignoreCase: true));
        Assert.DoesNotContain(scopedDefinitions, static definition =>
            string.Equals(definition.Name, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TryApplyDeferredActivatedPackToolScopeAfterRoundForTesting_ScopesToExecutedDeferredPack() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_pack_info",
                "Inspect system pack metadata.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "system_info",
                "Inspect system details on a target machine.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "system_pack_info",
                Description = "Inspect system pack metadata.",
                PackId = "system",
                RepresentativeExamples = new[] { "run system_pack_info" }
            },
            new ToolDefinitionDto {
                Name = "eventlog_live_query",
                Description = "Inspect login failures from a remote domain controller.",
                PackId = "eventlog",
                RepresentativeExamples = new[] { "show recent login failures from remote domain controller" }
            }
        });

        var activationOutput = await session.ExecuteToolAsyncForTesting(
            threadId: "thread-round-scope-system-pack",
            userRequest: "run system_pack_info",
            call: new ToolCall(
                callId: "call-round-scope-system-pack",
                name: "system_pack_info",
                input: "{}",
                arguments: new JsonObject(),
                raw: new JsonObject()),
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);
        Assert.True(activationOutput.Ok is true, activationOutput.Output);

        var scoped = session.TryApplyDeferredActivatedPackToolScopeAfterRoundForTesting(
            "run system_pack_info",
            options: null,
            definitions,
            new[] {
                new ToolCall(
                    callId: "call-round-scope-system-pack",
                    name: "system_pack_info",
                    input: "{}",
                    arguments: new JsonObject(),
                    raw: new JsonObject())
            },
            hasExplicitToolEnableSelectors: false,
            continuationContractDetected: false,
            executionContractApplies: false,
            hasPendingActionContext: false,
            out var scopedDefinitions,
            out var scopedPackIds);

        Assert.True(scoped);
        Assert.Single(scopedPackIds);
        Assert.Equal("system", scopedPackIds[0], ignoreCase: true);
        Assert.Equal(2, scopedDefinitions.Count);
        Assert.All(scopedDefinitions, definition =>
            Assert.Equal("system", definition.Routing?.PackId, ignoreCase: true));
    }

    [Fact]
    public async Task TryApplyDeferredActivatedPackToolScopeAfterRoundForTesting_ScopesToSourceAndHandoffTargetPacks() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_pack_info",
                "Inspect system pack metadata and hand off to event log follow-up.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "inventory_probe",
                "Inspect generic inventory state from a target host.",
                ToolSchema.Object(("target", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "inventory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "system_pack_info",
                Description = "Inspect system pack metadata.",
                PackId = "system",
                RepresentativeExamples = new[] { "run system_pack_info" }
            }
        });

        var activationOutput = await session.ExecuteToolAsyncForTesting(
            threadId: "thread-round-cross-pack-skip",
            userRequest: "run system_pack_info",
            call: new ToolCall(
                callId: "call-round-cross-pack-skip",
                name: "system_pack_info",
                input: "{}",
                arguments: new JsonObject(),
                raw: new JsonObject()),
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);
        Assert.True(activationOutput.Ok is true, activationOutput.Output);

        var scoped = session.TryApplyDeferredActivatedPackToolScopeAfterRoundForTesting(
            "run system_pack_info",
            options: null,
            definitions,
            new[] {
                new ToolCall(
                    callId: "call-round-cross-pack-skip",
                    name: "system_pack_info",
                    input: "{}",
                    arguments: new JsonObject(),
                    raw: new JsonObject())
            },
            hasExplicitToolEnableSelectors: false,
            continuationContractDetected: false,
            executionContractApplies: false,
            hasPendingActionContext: false,
            out var scopedDefinitions,
            out var scopedPackIds);

        Assert.True(scoped);
        Assert.Equal(2, scopedPackIds.Length);
        Assert.Contains(scopedPackIds, static packId => string.Equals(packId, "system", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scopedPackIds, static packId => string.Equals(packId, "eventlog", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, scopedDefinitions.Count);
        Assert.DoesNotContain(scopedDefinitions, static definition =>
            string.Equals(definition.Routing?.PackId, "inventory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrderToolDefinitionsForPromptExposureForTesting_PrefersDeferredMatchedToolFirst() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_info",
                "Inspect system details on a target machine.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "eventlog_connectivity_probe",
                "Probe event log runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "eventlog_live_query",
                "Inspect recent login failures from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "eventlog_live_query",
                Description = "Inspect login failures from a remote domain controller.",
                PackId = "eventlog",
                RepresentativeExamples = new[] { "show recent login failures from remote domain controller" }
            }
        });

        var ordered = session.OrderToolDefinitionsForPromptExposureForTesting(
            definitions,
            "show recent login failures from remote domain controller");

        Assert.Equal("eventlog_live_query", ordered[0].Name, ignoreCase: true);
    }

    [Fact]
    public void OrderToolDefinitionsForPromptExposureForTesting_PrefersPlannerContextHandoffTargetFirst() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "eventlog_bulk_export",
                "Export event log records after probe validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    Mode = ToolAuthenticationMode.ProfileReference,
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_connectivity_probe"
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "system_pack_info",
                "Inspect system pack metadata and hand off to event log follow-up.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "eventlog_live_query",
                "Inspect recent login failures from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        var ordered = session.OrderToolDefinitionsForPromptExposureForTesting(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            preferred_pack_ids: eventlog
            preferred_tool_names: eventlog_live_query
            handoff_target_pack_ids: eventlog
            handoff_target_tool_names: eventlog_live_query
            continuation_source_tool: system_pack_info
            continuation_reason: continue with the declared handoff target from the prior tool result
            allow_cached_evidence_reuse: false

            continue with the event log follow-up
            """);

        Assert.Equal("eventlog_live_query", ordered[0].Name, ignoreCase: true);
    }

    [Fact]
    public async Task TryApplyDeferredActivatedPackToolScopeAfterRoundForTesting_SuppressesRedundantHandoffHelperSiblings() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_pack_info",
                "Inspect system pack metadata and hand off to event log follow-up.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "eventlog_connectivity_probe",
                "Probe event log runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "eventlog_bulk_export",
                "Export event log records after probe validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    Mode = ToolAuthenticationMode.ProfileReference,
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_connectivity_probe"
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "inventory_probe",
                "Inspect generic inventory state from a target host.",
                ToolSchema.Object(("target", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "inventory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "system_pack_info",
                Description = "Inspect system pack metadata.",
                PackId = "system",
                RepresentativeExamples = new[] { "run system_pack_info" }
            }
        });

        var activationOutput = await session.ExecuteToolAsyncForTesting(
            threadId: "thread-round-cross-pack-helper-skip",
            userRequest: "run system_pack_info",
            call: new ToolCall(
                callId: "call-round-cross-pack-helper-skip",
                name: "system_pack_info",
                input: "{}",
                arguments: new JsonObject(),
                raw: new JsonObject()),
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);
        Assert.True(activationOutput.Ok is true, activationOutput.Output);

        var scoped = session.TryApplyDeferredActivatedPackToolScopeAfterRoundForTesting(
            "run system_pack_info",
            options: null,
            definitions,
            new[] {
                new ToolCall(
                    callId: "call-round-cross-pack-helper-skip",
                    name: "system_pack_info",
                    input: "{}",
                    arguments: new JsonObject(),
                    raw: new JsonObject())
            },
            hasExplicitToolEnableSelectors: false,
            continuationContractDetected: false,
            executionContractApplies: false,
            hasPendingActionContext: false,
            out var scopedDefinitions,
            out var scopedPackIds);

        Assert.True(scoped);
        Assert.Equal(2, scopedPackIds.Length);
        Assert.Equal("eventlog_live_query", scopedDefinitions[0].Name, ignoreCase: true);
        Assert.Contains(scopedDefinitions, static definition =>
            string.Equals(definition.Name, "system_pack_info", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scopedDefinitions, static definition =>
            string.Equals(definition.Name, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scopedDefinitions, static definition =>
            string.Equals(definition.Name, "eventlog_bulk_export", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scopedDefinitions, static definition =>
            string.Equals(definition.Name, "eventlog_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scopedDefinitions, static definition =>
            string.Equals(definition.Routing?.PackId, "inventory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildToolRoundReplayInputWithPlannerContextForTesting_PrefersHandoffTargetsForSameTurnReplay() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_pack_info",
                "Inspect system pack metadata and hand off to event log follow-up.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        var replayInput = session.BuildToolRoundReplayInputWithPlannerContextForTesting(
            "thread-replay-handoff-target",
            "show me the recent login failures after the system probe",
            definitions,
            new[] {
                new ToolCall(
                    callId: "call-replay-handoff-target",
                    name: "system_pack_info",
                    input: "{}",
                    arguments: new JsonObject(),
                    raw: new JsonObject())
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-replay-handoff-target",
                    Output = "{\"host\":\"srv1\"}",
                    Ok = true
                }
            });
        var plannerContextText = ExtractChatInputTextItems(replayInput)
            .FirstOrDefault(text => text.IndexOf("ix:planner-context:v1", StringComparison.OrdinalIgnoreCase) >= 0);

        Assert.False(string.IsNullOrWhiteSpace(plannerContextText));
        var parsed = ChatServiceSession.TryReadPlannerContextFromRequestTextForTesting(
            plannerContextText!,
            out _,
            out _,
            out var preferredPackIds,
            out var preferredToolNames,
            out _,
            out _,
            out var handoffTargetPackIds,
            out var handoffTargetToolNames,
            out var continuationSourceTool,
            out var continuationReason,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);

        Assert.True(parsed);
        Assert.Equal("eventlog", preferredPackIds[0]);
        Assert.Equal("eventlog_live_query", preferredToolNames[0]);
        Assert.Equal("eventlog", handoffTargetPackIds[0]);
        Assert.Equal("eventlog_live_query", handoffTargetToolNames[0]);
        Assert.Equal("system_pack_info", continuationSourceTool);
        Assert.Contains("declared handoff target", continuationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrderToolDefinitionsForPromptExposureForTesting_UsesReplayPlannerContextToPreferSameTurnHandoffTarget() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_pack_info",
                "Inspect system pack metadata and hand off to event log follow-up.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "eventlog_bulk_export",
                "Export event log records after probe validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    Mode = ToolAuthenticationMode.ProfileReference,
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_connectivity_probe"
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "eventlog_connectivity_probe",
                "Probe event log runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        var replayInput = session.BuildToolRoundReplayInputWithPlannerContextForTesting(
            "thread-replay-handoff-order",
            "show me the recent login failures after the system probe",
            definitions,
            new[] {
                new ToolCall(
                    callId: "call-replay-handoff-order",
                    name: "system_pack_info",
                    input: "{}",
                    arguments: new JsonObject(),
                    raw: new JsonObject())
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-replay-handoff-order",
                    Output = "{\"host\":\"srv1\"}",
                    Ok = true
                }
            });
        var replayPlannerContextText = ExtractChatInputTextItems(replayInput)
            .FirstOrDefault(text => text.IndexOf("ix:planner-context:v1", StringComparison.OrdinalIgnoreCase) >= 0);

        Assert.False(string.IsNullOrWhiteSpace(replayPlannerContextText));
        var ordered = session.OrderToolDefinitionsForPromptExposureForTesting(
            definitions,
            "show me the recent login failures after the system probe\n\n" + replayPlannerContextText);

        Assert.Equal("eventlog_live_query", ordered[0].Name, ignoreCase: true);
        var orderedToolNames = ordered.Select(static definition => definition.Name).ToArray();
        Assert.True(Array.IndexOf(orderedToolNames, "eventlog_live_query") < Array.IndexOf(orderedToolNames, "eventlog_connectivity_probe"));
        Assert.True(Array.IndexOf(orderedToolNames, "eventlog_live_query") < Array.IndexOf(orderedToolNames, "eventlog_bulk_export"));
        Assert.True(Array.IndexOf(orderedToolNames, "eventlog_live_query") < Array.IndexOf(orderedToolNames, "system_pack_info"));
    }

    [Fact]
    public void ExpandToFullToolAvailabilityForPromptExposureForTesting_PreservesReplayHandoffTargetOrdering() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_pack_info",
                "Inspect system pack metadata and hand off to event log follow-up.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "host",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new(
                "eventlog_bulk_export",
                "Export event log records after probe validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    Mode = ToolAuthenticationMode.ProfileReference,
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_connectivity_probe"
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "eventlog_connectivity_probe",
                "Probe event log runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        var replayInput = session.BuildToolRoundReplayInputWithPlannerContextForTesting(
            "thread-replay-handoff-full-expand",
            "show me the recent login failures after the system probe",
            definitions,
            new[] {
                new ToolCall(
                    callId: "call-replay-handoff-full-expand",
                    name: "system_pack_info",
                    input: "{}",
                    arguments: new JsonObject(),
                    raw: new JsonObject())
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-replay-handoff-full-expand",
                    Output = "{\"host\":\"srv1\"}",
                    Ok = true
                }
            });
        var replayPlannerContextText = ExtractChatInputTextItems(replayInput)
            .FirstOrDefault(text => text.IndexOf("ix:planner-context:v1", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.False(string.IsNullOrWhiteSpace(replayPlannerContextText));

        var ordered = session.ExpandToFullToolAvailabilityForPromptExposureForTesting(
            definitions,
            "show me the recent login failures after the system probe\n\n" + replayPlannerContextText,
            out var options);

        Assert.Equal("eventlog_live_query", ordered[0].Name, ignoreCase: true);
        var exposedTools = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(options.Tools);
        Assert.Equal("eventlog_live_query", exposedTools[0].Name, ignoreCase: true);
        Assert.Equal(IntelligenceX.OpenAI.ToolCalling.ToolChoice.Auto, options.ToolChoice);
    }

    [Fact]
    public void CopyChatOptionsWithPromptAwareToolOrderingForTesting_ReordersClonedOptionsWithoutMutatingSource() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "system_pack_info",
                "Inspect system pack metadata and hand off to event log follow-up.",
                ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "eventlog_connectivity_probe",
                "Probe event log runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "eventlog_live_query",
                "Inspect recent event log records from a remote host.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        var sourceOptions = new ChatOptions {
            Tools = definitions,
            ToolChoice = IntelligenceX.OpenAI.ToolCalling.ToolChoice.Auto
        };
        var clonedOptions = session.CopyChatOptionsWithPromptAwareToolOrderingForTesting(
            sourceOptions,
            """
            [Planner context]
            ix:planner-context:v1
            preferred_pack_ids: eventlog
            preferred_tool_names: eventlog_live_query
            handoff_target_pack_ids: eventlog
            handoff_target_tool_names: eventlog_live_query
            continuation_source_tool: system_pack_info
            continuation_reason: continue with the declared handoff target from the prior tool result
            allow_cached_evidence_reuse: false

            continue with the event log follow-up
            """);

        Assert.NotSame(sourceOptions, clonedOptions);
        Assert.Equal("system_pack_info", sourceOptions.Tools![0].Name, ignoreCase: true);
        var clonedTools = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(clonedOptions.Tools);
        Assert.Equal("eventlog_live_query", clonedTools[0].Name, ignoreCase: true);
        Assert.Equal(IntelligenceX.OpenAI.ToolCalling.ToolChoice.Auto, clonedOptions.ToolChoice);
    }

    [Fact]
    public void SelectWeightedToolSubset_PrefersExactHandoffTargetToolBeforeSiblingPackHelpers() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Generic diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        definitions.Add(new ToolDefinition(
            "eventlog_connectivity_probe",
            "Probe remote event log connectivity.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));
        definitions.Add(new ToolDefinition(
            "eventlog_runtime_profile_validate",
            "Validate event log runtime profile readiness.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "eventlog_live_query",
            "Inspect recent event log records from a remote host.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var args = new object?[] {
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: false
            preferred_pack_ids: eventlog
            preferred_tool_names: eventlog_live_query
            structured_next_action_source_tools: system_pack_info
            structured_next_action_reason: continue with the declared handoff target from the prior tool result
            handoff_target_pack_ids: eventlog
            handoff_target_tool_names: eventlog_live_query
            continuation_source_tool: system_pack_info
            continuation_reason: continue with the declared handoff target from the prior tool result
            allow_cached_evidence_reuse: false
            """,
            3,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.InRange(selected.Count, 3, 8);
        Assert.Equal("eventlog_live_query", selected[0].Name);
        Assert.Contains(selected, static tool => string.Equals(tool.Name, "eventlog_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, static tool => string.Equals(tool.Name, "eventlog_runtime_profile_validate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_SuppressesSiblingProbeHelperWhenExactTargetNeedsNoHelpers() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Generic diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        definitions.Add(new ToolDefinition(
            "custom_connectivity_probe",
            "Probe custom runtime reachability.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));
        definitions.Add(new ToolDefinition(
            "custom_followup",
            "Collect custom follow-up evidence.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "custom_secondary_followup",
            "Collect a different custom follow-up that still requires probe preflight.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            authentication: new ToolAuthenticationContract {
                IsAuthenticationAware = true,
                RequiresAuthentication = true,
                Mode = ToolAuthenticationMode.ProfileReference,
                SupportsConnectivityProbe = true,
                ProbeToolName = "custom_connectivity_probe"
            },
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        var args = new object?[] {
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            preferred_pack_ids: customx
            preferred_tool_names: custom_followup
            handoff_target_pack_ids: customx
            handoff_target_tool_names: custom_followup
            continuation_source_tool: system_pack_info
            continuation_reason: continue with the declared handoff target from the prior tool result
            allow_cached_evidence_reuse: false
            """,
            4,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.InRange(selected.Count, 4, 8);
        Assert.Equal("custom_followup", selected[0].Name);
        Assert.DoesNotContain(selected, static tool => string.Equals(tool.Name, "custom_connectivity_probe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryApplyDeferredActivatedPackToolScopeForTesting_DoesNotScopeInactiveDeferredPack() {
        var options = new ServiceOptions {
            EnableBuiltInPackLoading = false,
            EnableDefaultPluginPaths = false
        };
        var session = new ChatServiceSession(options, Stream.Null);
        var definitions = new List<ToolDefinition> {
            new(
                "ops_inventory_collect",
                "Collect inventory from an endpoint.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "ops_inventory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "generic_inventory_query",
                "Collect generic inventory from an endpoint.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "ops_inventory_collect",
                Description = "Collect endpoint inventory.",
                PackId = "ops_inventory",
                RepresentativeExamples = new[] { "collect inventory from endpoint" }
            }
        });

        var scoped = session.TryApplyDeferredActivatedPackToolScopeForTesting(
            "collect inventory from endpoint srv1",
            options: null,
            definitions,
            hasExplicitToolEnableSelectors: false,
            continuationContractDetected: false,
            executionContractApplies: false,
            hasPendingActionContext: false,
            hasToolActivity: false,
            out var scopedDefinitions,
            out var scopedPackIds);

        Assert.False(scoped);
        Assert.Empty(scopedPackIds);
        Assert.Equal(definitions.Count, scopedDefinitions.Count);
    }

    [Fact]
    public void BuildModelPlannerCandidates_UsesStructuredNextActionReasonTokensWithoutPreferredToolNames() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "ad_ldap_diagnostics",
            "Run LDAP diagnostics and inspect LDAPS endpoint certificate details for the same domain controller.",
            ToolSchema.Object(("domain_controller", ToolSchema.String("Domain controller."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            },
            tags: new[] { "intent:ldap_certificates", "protocol:ldaps" }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            structured_next_action_source_tools: ad_monitoring_probe_run
            structured_next_action_reason: inspect ldaps certificate details on the same domain controller
            structured_next_action_confidence: 0.88
            allow_cached_evidence_reuse: false

            continue with the same hosts
            """,
            4);

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersRemoteCapableToolsWhenHostHintIsPresent() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_local_probe_{i:D2}",
                "Collect generic local inventory details.",
                ToolSchema.Object(("path", ToolSchema.String("Path."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "eventlog_timeline_query",
            "Inspect timeline for a remote host.",
            ToolSchema.Object(
                    ("machine_name", ToolSchema.String("Remote machine.")),
                    ("channel", ToolSchema.String("Channel.")))
                .NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            "continue the investigation on ad0.contoso.com and inspect the event timeline there",
            4,
            ToolOrchestrationCatalog.Build(definitions));

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersEnvironmentDiscoverToolsForPackBootstrapTurns() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"active_directory_probe_{i:D2}",
                "Collect generic Active Directory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "zz_ad_environment_discover",
            "Discover Active Directory environment scope and target domain controllers.",
            ToolSchema.Object(
                    ("domain_controller", ToolSchema.String("Domain controller.")),
                    ("search_base_dn", ToolSchema.String("Base DN.")))
                .NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
            }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            preferred_pack_ids: active_directory
            allow_cached_evidence_reuse: false

            continue with the same directory scope
            """,
            4,
            ToolOrchestrationCatalog.Build(definitions));

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "zz_ad_environment_discover", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersProbeHelpersBeforeAuthRequiredFollowUpTools() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "eventlog_channels_list",
            "List available event log channels and validate access for the target machine.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "eventlog_live_query",
            "Inspect live event logs on a remote machine after runtime profile validation.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            authentication: new ToolAuthenticationContract {
                IsAuthenticationAware = true,
                RequiresAuthentication = true,
                AuthenticationContractId = "ix.auth.runtime.v1",
                Mode = ToolAuthenticationMode.ProfileReference,
                ProfileIdArgumentName = "profile_id",
                SupportsConnectivityProbe = true,
                ProbeToolName = "eventlog_channels_list"
            }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            preferred_pack_ids: eventlog
            missing_live_evidence: runtime event log access on srv-01.contoso.com
            allow_cached_evidence_reuse: false

            continue on srv-01.contoso.com and inspect live event log evidence
            """,
            4,
            ToolOrchestrationCatalog.Build(definitions));

        Assert.InRange(selected.Count, 24, 24);
        var helperIndex = selected.ToList().FindIndex(static tool => string.Equals(tool.Name, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        var authToolIndex = selected.ToList().FindIndex(static tool => string.Equals(tool.Name, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.True(helperIndex >= 0, "Expected the probe helper to be included.");
        Assert.True(authToolIndex >= 0, "Expected the auth-required dependent tool to be included.");
        Assert.True(helperIndex < authToolIndex, "Expected the probe helper to rank ahead of the auth-required follow-up tool.");
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersPackOwnedPreferredProbeToolsForRemoteFocusedPack() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "custom_connectivity_probe",
            "Probe custom runtime state.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleDiagnostic
            }));
        definitions.Add(new ToolDefinition(
            "custom_followup",
            "Collect custom follow-up evidence.",
            ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "customx",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, new IToolPack[] { new SyntheticRepresentativeExamplePack() });

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            preferred_pack_ids: customx
            allow_cached_evidence_reuse: false

            continue on the same remote endpoint and stabilize reachability first
            """,
            4,
            orchestrationCatalog);

        Assert.InRange(selected.Count, 24, 24);
        var probeIndex = selected.ToList().FindIndex(static tool => string.Equals(tool.Name, "custom_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        var followupIndex = selected.ToList().FindIndex(static tool => string.Equals(tool.Name, "custom_followup", StringComparison.OrdinalIgnoreCase));
        Assert.True(probeIndex >= 0, "Expected the pack-owned preferred probe to be included.");
        Assert.True(followupIndex >= 0, "Expected the pack follow-up tool to be included.");
        Assert.True(probeIndex < followupIndex, "Expected the pack-owned preferred probe to rank ahead of the follow-up tool.");
    }

    [Fact]
    public void BuildModelPlannerCandidates_DerivesHandoffTargetsFromSourceToolsInPlannerContext() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic inventory details.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "eventlog_timeline_query",
            "Inspect event timeline on the remote machine.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "system",
                        TargetToolName = "system_metrics_summary",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "machine_name",
                                TargetArgument = "computer_name"
                            }
                        }
                    }
                }
            }));
        definitions.Add(new ToolDefinition(
            "system_metrics_summary",
            "Summarize system metrics for a remote host.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            structured_next_action_source_tools: eventlog_timeline_query
            allow_cached_evidence_reuse: false

            continue on the same host
            """,
            4,
            ToolOrchestrationCatalog.Build(definitions));

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "system_metrics_summary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_PrefersRemoteCapableToolsWhenHostHintIsPresent() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_local_probe_{i:D2}",
                "Collect generic local inventory details.",
                ToolSchema.Object(("path", ToolSchema.String("Path."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "system_metrics_summary",
            "Summarize system metrics for a remote host.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        var selected = session.SelectWeightedToolSubsetForTesting(
            definitions,
            "check cpu and memory on srv-01.contoso.com",
            8,
            out var insights);

        Assert.Equal(8, selected.Count);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "system_metrics_summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            insights,
            insight => (insight.GetType().GetProperty("Reason", BindingFlags.Public | BindingFlags.Instance)?.GetValue(insight)?.ToString() ?? string.Empty)
                .IndexOf("remote-capable host targeting", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void SelectWeightedToolSubset_PrefersSetupAwareToolsForPlannerPackBootstrapTurns() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"eventlog_probe_{i:D2}",
                "Collect generic event log diagnostics.",
                ToolSchema.Object(("path", ToolSchema.String("Path."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "zz_eventlog_named_events_query",
            "Query named event detections once valid values are known.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupToolName = "eventlog_named_events_catalog",
                SetupHintKeys = new[] { "eventlog.named_values" }
            }));

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        var selected = session.SelectWeightedToolSubsetForTesting(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            missing_live_evidence: validated event values
            preferred_pack_ids: eventlog
            allow_cached_evidence_reuse: false

            continue with the same event evidence
            """,
            8,
            out var insights);

        Assert.Equal(8, selected.Count);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "zz_eventlog_named_events_query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            insights,
            insight => (insight.GetType().GetProperty("Reason", BindingFlags.Public | BindingFlags.Instance)?.GetValue(insight)?.ToString() ?? string.Empty)
                .IndexOf("setup-aware preflight support", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IndexesPolishReplicationMetadataForLiveAdTools() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definition = new AdReplicationSummaryTool(new ActiveDirectoryToolOptions()).Definition;

        var text = session.BuildToolRoutingSearchTextForTesting(definition);

        Assert.Contains("replikacja", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forestu", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("utc", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectWeightedToolSubset_PrefersReplicationToolsForPolishForestReplicationAsk() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 24; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic operational diagnostics for the current environment.",
                ToolSchema.Object(("path", ToolSchema.String("Path."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new AdReplicationSummaryTool(new ActiveDirectoryToolOptions()).Definition);
        definitions.Add(new AdReplicationStatusTool(new ActiveDirectoryToolOptions()).Definition);
        definitions.Add(new AdReplicationConnectionsTool(new ActiveDirectoryToolOptions()).Definition);
        definitions.Add(new AdMonitoringProbeRunTool(new ActiveDirectoryToolOptions()).Definition);

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        var selected = session.SelectWeightedToolSubsetForTesting(
            definitions,
            "Co tam slychac w AD replikacji forestu? Podsumuj w UTC.",
            8,
            out _);

        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_replication_summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool =>
            string.Equals(tool.Name, "ad_replication_status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildModelPlannerCandidates_PreservesReplicationToolsForPolishForestReplicationAsk() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 84; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic operational diagnostics for the current environment.",
                ToolSchema.Object(("path", ToolSchema.String("Path."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new AdReplicationSummaryTool(new ActiveDirectoryToolOptions()).Definition);
        definitions.Add(new AdReplicationStatusTool(new ActiveDirectoryToolOptions()).Definition);
        definitions.Add(new AdReplicationConnectionsTool(new ActiveDirectoryToolOptions()).Definition);
        definitions.Add(new AdMonitoringProbeRunTool(new ActiveDirectoryToolOptions()).Definition);

        var catalog = ToolOrchestrationCatalog.Build(definitions);
        session.SetToolOrchestrationCatalogForTesting(catalog);

        var candidates = session.BuildModelPlannerCandidatesForTesting(
            definitions,
            "Co tam slychac w AD replikacji forestu? Podsumuj w UTC.",
            8,
            catalog);

        Assert.Contains(candidates, tool => string.Equals(tool.Name, "ad_replication_summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(candidates, tool =>
            string.Equals(tool.Name, "ad_replication_status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tool.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_PreservesDeferredWorkTargetedPackWhenLexicalMatchesAreNoisy() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "generic",
                    Name = "Generic",
                    SourceKind = "builtin",
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "testimox_analytics",
                    Name = "TestimoX Analytics",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityReporting },
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 0,
                RoutingAwareTools = 0,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });

        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 6; i++) {
            definitions.Add(new ToolDefinition(
                $"identity_probe_{i:D2}",
                "Inspect identity lifecycle compliance ownership evidence for the current environment.",
                ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        for (var i = 0; i < 8; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_probe_{i:D2}",
                "Collect generic operational diagnostics.",
                ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "report_snapshot_publish",
            "Publish the prepared deliverable artifact for later sharing.",
            ToolSchema.Object(("report_name", ToolSchema.String("Report name."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "testimox_analytics",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        var selected = session.SelectWeightedToolSubsetForTesting(
            definitions,
            """
            [Planner context]
            ix:planner-context:v1
            preferred_deferred_work_capability_ids: reporting

            inspect identity lifecycle compliance ownership evidence for the current environment
            """,
            4,
            out _);

        Assert.InRange(selected.Count, 4, definitions.Count);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "report_snapshot_publish", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectWeightedToolSubset_PrefersProbeHelpersBeforeAuthRequiredFollowUpTools() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_local_probe_{i:D2}",
                "Collect generic local inventory details.",
                ToolSchema.Object(("path", ToolSchema.String("Path."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "generic",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "eventlog_channels_list",
            "List available event log channels and validate access for the target machine.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));
        definitions.Add(new ToolDefinition(
            "eventlog_live_query",
            "Inspect live event logs on a remote machine after runtime profile validation.",
            ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "eventlog",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            authentication: new ToolAuthenticationContract {
                IsAuthenticationAware = true,
                RequiresAuthentication = true,
                AuthenticationContractId = "ix.auth.runtime.v1",
                Mode = ToolAuthenticationMode.ProfileReference,
                ProfileIdArgumentName = "profile_id",
                SupportsConnectivityProbe = true,
                ProbeToolName = "eventlog_channels_list"
            }));

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        var selected = session.SelectWeightedToolSubsetForTesting(
            definitions,
            "continue on srv-01.contoso.com and inspect live event log evidence there",
            8,
            out var insights);

        Assert.Equal(8, selected.Count);
        var helperIndex = selected.ToList().FindIndex(static tool => string.Equals(tool.Name, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        var authToolIndex = selected.ToList().FindIndex(static tool => string.Equals(tool.Name, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.True(helperIndex >= 0, "Expected the probe helper to be selected.");
        Assert.True(authToolIndex >= 0, "Expected the auth-required tool to be selected.");
        Assert.True(helperIndex < authToolIndex, "Expected the probe helper to rank ahead of the auth-required follow-up tool.");
        Assert.Contains(
            insights,
            insight => (insight.GetType().GetProperty("Reason", BindingFlags.Public | BindingFlags.Instance)?.GetValue(insight)?.ToString() ?? string.Empty)
                .IndexOf("probe/setup prerequisite support", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void SelectWeightedToolSubset_DeprioritizesWriteCapableFollowUpToolsWithoutStructuredMutatingIntent() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_directory_probe_{i:D2}",
                "Collect generic directory posture details for the current environment.",
                ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "aa_user_disable_followup",
            "Inspect account state for the targeted directory identity.",
            ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            writeGovernance: new ToolWriteGovernanceContract {
                IsWriteCapable = true
            }));
        definitions.Add(new ToolDefinition(
            "zz_user_state_read",
            "Inspect account state for the targeted directory identity.",
            ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        var selected = session.SelectWeightedToolSubsetForTesting(
            definitions,
            "inspect account state for the targeted directory identity",
            1,
            out _);

        Assert.NotEmpty(selected);
        Assert.Equal("zz_user_state_read", selected[0].Name);
    }

    [Fact]
    public void BuildModelPlannerCandidates_PrefersWriteCapableFollowUpToolsWhenStructuredMutatingIntentIsPresent() {
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 80; i++) {
            definitions.Add(new ToolDefinition(
                $"generic_directory_probe_{i:D2}",
                "Collect generic directory posture details for the current environment.",
                ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }));
        }

        definitions.Add(new ToolDefinition(
            "aa_user_disable_followup",
            "Inspect account state for the targeted directory identity.",
            ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            writeGovernance: new ToolWriteGovernanceContract {
                IsWriteCapable = true
            }));
        definitions.Add(new ToolDefinition(
            "zz_user_state_read",
            "Inspect account state for the targeted directory identity.",
            ToolSchema.Object(("identity", ToolSchema.String("Target identity."))).NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "active_directory",
                Role = ToolRoutingTaxonomy.RoleOperational
            }));

        var selected = BuildModelPlannerCandidates(
            definitions,
            """
            {"ix_action_selection":{"id":"act_001","title":"Disable the selected user","request":"Disable the selected directory account after reviewing its state on the same host and continue with the requested follow-up now.","mutating":true}}
            """,
            4,
            ToolOrchestrationCatalog.Build(definitions));

        Assert.InRange(selected.Count, 24, 24);
        Assert.Equal("aa_user_disable_followup", selected[0].Name);
    }

    [Fact]
    public void EnsureMinimumToolSelection_ReplacesNonExplicitToolWhenExplicitToolIsRequestedAtLimit() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var allDefinitions = new List<ToolDefinition>();
        for (var i = 0; i < 12; i++) {
            allDefinitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        allDefinitions.Add(new ToolDefinition(
            "eventlog_evtx_query",
            "Read events from EVTX.",
            ToolSchema.Object(("path", ToolSchema.String("Path to EVTX."))).NoAdditionalProperties()));

        var initialSelected = new List<ToolDefinition>();
        for (var i = 0; i < 8; i++) {
            initialSelected.Add(allDefinitions[i]);
        }

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(EnsureMinimumToolSelectionMethod.Invoke(
            session,
            new object?[] {
                "dobra a co to `eventlog\\_evtx\\_query · Event Log (EventViewerX)` i kiedy to uzywac?",
                allDefinitions,
                initialSelected,
                8
            }));

        Assert.Equal(8, selected.Count);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_evtx_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureMinimumToolSelection_DoesNotBackfillBeyondRequestedLimitForSmallBudget() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var allDefinitions = new List<ToolDefinition>();
        for (var i = 0; i < 10; i++) {
            allDefinitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        allDefinitions.Add(new ToolDefinition(
            "eventlog_evtx_query",
            "Read events from EVTX.",
            ToolSchema.Object(("path", ToolSchema.String("Path to EVTX."))).NoAdditionalProperties()));

        var initialSelected = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(EnsureMinimumToolSelectionMethod.Invoke(
            session,
            new object?[] {
                "show me what `eventlog\\_evtx\\_query · Event Log (EventViewerX)` does",
                allDefinitions,
                initialSelected,
                4
            }));

        Assert.Equal(4, selected.Count);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_evtx_query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnsureMinimumToolSelection_BackfillsDeclaredSetupAndProbeHelpersBeforeGenericFallback() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var allDefinitions = new List<ToolDefinition> {
            new(
                "eventlog_live_query",
                "Inspect live logs after runtime validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_connectivity_probe"
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_runtime_profile_validate"
                }),
            new(
                "eventlog_runtime_profile_validate",
                "Validate event log runtime profile readiness.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()),
            new(
                "eventlog_connectivity_probe",
                "Probe remote event log connectivity.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        for (var i = 0; i < 9; i++) {
            allDefinitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Generic diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(allDefinitions));

        var initialSelected = new List<ToolDefinition> {
            allDefinitions[0]
        };

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(EnsureMinimumToolSelectionMethod.Invoke(
            session,
            new object?[] {
                "check live event logs on the same machine",
                allDefinitions,
                initialSelected,
                4
            }));

        Assert.Equal(4, selected.Count);
        Assert.Equal("eventlog_live_query", selected[0].Name);
        Assert.Equal("eventlog_runtime_profile_validate", selected[1].Name);
        Assert.Equal("eventlog_connectivity_probe", selected[2].Name);
    }

    [Fact]
    public void EnsureMinimumToolSelection_BackfillsRecipeDerivedHelpersBeforeGenericFallback() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var allDefinitions = new List<ToolDefinition> {
            new(
                "custom_followup",
                "Collect custom follow-up evidence.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "custom_connectivity_probe",
                "Probe custom runtime reachability.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "custom_recipe_resolver",
                "Resolve recipe-scoped runtime context.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties())
        };
        for (var i = 0; i < 9; i++) {
            allDefinitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Generic diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(allDefinitions, new IToolPack[] { new SyntheticRecipeHelperPack() }));

        var initialSelected = new List<ToolDefinition> {
            allDefinitions[0]
        };

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(EnsureMinimumToolSelectionMethod.Invoke(
            session,
            new object?[] {
                "continue the remote custom endpoint follow-up",
                allDefinitions,
                initialSelected,
                4
            }));

        Assert.Equal(4, selected.Count);
        Assert.Equal("custom_followup", selected[0].Name);
        Assert.Equal("custom_connectivity_probe", selected[1].Name);
        Assert.Equal("custom_recipe_resolver", selected[2].Name);
    }

    [Fact]
    public void EnsureMinimumToolSelection_DoesNotBackfillRecipeOverlapHelperWhenContractShapeDiffers() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var allDefinitions = new List<ToolDefinition> {
            new(
                "custom_followup",
                "Collect custom follow-up evidence.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "custom_connectivity_probe",
                "Probe custom runtime reachability.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "custom_recipe_resolver",
                "Resolve recipe-scoped runtime context.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleResolver
                }),
            new(
                "custom_recipe_directory_probe",
                "Probe unrelated directory recipe context.",
                ToolSchema.Object(("directory_id", ToolSchema.String("Directory target."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                })
        };
        for (var i = 0; i < 9; i++) {
            allDefinitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Generic diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(allDefinitions, new IToolPack[] { new SyntheticRecipeHelperPack() }));

        var initialSelected = new List<ToolDefinition> {
            allDefinitions[0]
        };

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(EnsureMinimumToolSelectionMethod.Invoke(
            session,
            new object?[] {
                "continue the remote custom endpoint follow-up",
                allDefinitions,
                initialSelected,
                3
            }));

        Assert.Equal(3, selected.Count);
        Assert.Contains(selected, static item => string.Equals(item.Name, "custom_connectivity_probe", StringComparison.Ordinal));
        Assert.Contains(selected, static item => string.Equals(item.Name, "custom_recipe_resolver", StringComparison.Ordinal));
        Assert.DoesNotContain(selected, static item => string.Equals(item.Name, "custom_recipe_directory_probe", StringComparison.Ordinal));
    }

    [Fact]
    public void EnsureMinimumToolSelection_DoesNotBackfillRecipeOverlapHelperFromDifferentPack() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var allDefinitions = new List<ToolDefinition> {
            new(
                "custom_followup",
                "Collect custom follow-up evidence.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new(
                "custom_connectivity_probe",
                "Probe custom runtime reachability.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new(
                "custom_recipe_resolver",
                "Resolve recipe-scoped runtime context.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "customx",
                    Role = ToolRoutingTaxonomy.RoleResolver
                }),
            new(
                "foreign_recipe_resolver",
                "Resolve the same recipe from another pack.",
                ToolSchema.Object(("endpoint", ToolSchema.String("Target endpoint."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "foreignx",
                    Role = ToolRoutingTaxonomy.RoleResolver
                })
        };
        for (var i = 0; i < 9; i++) {
            allDefinitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Generic diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            allDefinitions,
            new IToolPack[] { new SyntheticRecipeHelperPack(), new SyntheticForeignRecipeHelperPack() }));

        var initialSelected = new List<ToolDefinition> {
            allDefinitions[0]
        };

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(EnsureMinimumToolSelectionMethod.Invoke(
            session,
            new object?[] {
                "continue the remote custom endpoint follow-up",
                allDefinitions,
                initialSelected,
                3
            }));

        Assert.Equal(3, selected.Count);
        Assert.Contains(selected, static item => string.Equals(item.Name, "custom_connectivity_probe", StringComparison.Ordinal));
        Assert.Contains(selected, static item => string.Equals(item.Name, "custom_recipe_resolver", StringComparison.Ordinal));
        Assert.DoesNotContain(selected, static item => string.Equals(item.Name, "foreign_recipe_resolver", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_DefaultsCompatibleHttpToEight() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { null, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(8, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_PreservesExplicitRequest() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 17, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(17, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_ZeroUsesTransportDefaultForCompatibleHttp() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 0, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(8, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_ZeroUsesNoOverrideForNativeTransport() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 0, OpenAITransportKind.Native });
        Assert.Null(result);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_ClampsOversizedRequestToSafetyLimit() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 999, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(256, value);
    }

    [Theory]
    [InlineData(0L, 8)]
    [InlineData(4096L, 4)]
    [InlineData(8192L, 4)]
    [InlineData(12000L, 6)]
    [InlineData(16384L, 6)]
    [InlineData(32768L, 8)]
    public void ResolveContextAwareCompatibleHttpDefaultMaxCandidateTools_UsesContextBands(long effectiveContextLength, int expected) {
        var result = ResolveContextAwareCompatibleHttpDefaultMaxCandidateToolsMethod.Invoke(null, new object?[] { effectiveContextLength });
        var value = Assert.IsType<int>(result);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_PreservesExplicitRequestForCompatibleHttp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { 21, OpenAITransportKind.CompatibleHttp, "any-model" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(21, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_ReturnsNoOverrideForNativeWithoutRequest() {
        var session = BuildSessionWithModelCache(BuildModelInfo("native-model", loadedContextLength: 4096, maxContextLength: null));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.Native, "native-model" });

        Assert.Null(result);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_UsesMatchedModelLoadedContextForCompatibleHttp() {
        var session = BuildSessionWithModelCache(BuildModelInfo("ctx-small", loadedContextLength: 4096, maxContextLength: 32768));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "ctx-small" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(4, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_UsesMatchedModelMaxContextWhenLoadedContextMissing() {
        var session = BuildSessionWithModelCache(BuildModelInfo("ctx-max-only", loadedContextLength: null, maxContextLength: 12000));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "ctx-max-only" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(6, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_UsesSingleModelFallbackWhenSelectionDoesNotMatch() {
        var session = BuildSessionWithModelCache(BuildModelInfo("only-model", loadedContextLength: 12000, maxContextLength: null));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "unknown-model" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(6, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_FallsBackToCompatibleHttpDefaultWhenModelSelectionUnknownAndMultipleModelsExist() {
        var session = BuildSessionWithModelCache(
            BuildModelInfo("model-a", loadedContextLength: 4096, maxContextLength: null),
            BuildModelInfo("model-b", loadedContextLength: 12000, maxContextLength: null));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "unknown-model" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(8, value);
    }

    private static ChatServiceSession BuildSessionWithModelCache(params ModelInfo[] models) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var modelList = new ModelListResult(models, nextCursor: null, raw: new JsonObject(), additional: null);
        var cacheEntry = Activator.CreateInstance(
            ModelListCacheEntryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object?[] { "test-cache-key", DateTime.UtcNow.AddMinutes(5), modelList },
            culture: null);
        Assert.NotNull(cacheEntry);

        ModelListCacheField.SetValue(session, cacheEntry);
        return session;
    }

    private static ModelInfo BuildModelInfo(string id, long? loadedContextLength, long? maxContextLength) {
        return new ModelInfo(
            id: id,
            model: id,
            displayName: id,
            description: string.Empty,
            supportedReasoningEfforts: Array.Empty<ReasoningEffortOption>(),
            defaultReasoningEffort: null,
            isDefault: false,
            raw: new JsonObject(),
            additional: null,
            maxContextLength: maxContextLength,
            loadedContextLength: loadedContextLength,
            capabilities: Array.Empty<string>());
    }

    private static IReadOnlyList<ToolDefinition> BuildModelPlannerCandidates(
        IReadOnlyList<ToolDefinition> definitions,
        string requestText,
        int limit,
        ToolOrchestrationCatalog? orchestrationCatalog = null,
        IReadOnlyList<ToolPackAvailabilityInfo>? packAvailability = null) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        if (packAvailability is { Count: > 0 }) {
            session.SetCapabilitySnapshotContextForTesting(
                packAvailability,
                new ToolRoutingCatalogDiagnostics {
                    TotalTools = definitions.Count,
                    RoutingAwareTools = 0,
                    MissingRoutingContractTools = 0,
                    DomainFamilyTools = 0,
                    ExpectedDomainFamilyMissingTools = 0,
                    DomainFamilyMissingActionTools = 0,
                    ActionWithoutFamilyTools = 0,
                    FamilyActionConflictFamilies = 0,
                    FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
                });
        }

        return session.BuildModelPlannerCandidatesForTesting(
            definitions,
            requestText,
            limit,
            orchestrationCatalog ?? ToolOrchestrationCatalog.Build(definitions));
    }

    private static string BuildToolRoutingSearchText(
        ToolDefinition definition,
        IReadOnlyList<ToolPackAvailabilityInfo>? packAvailability = null,
        ToolOrchestrationCatalog? orchestrationCatalog = null) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        if (packAvailability is { Count: > 0 }) {
            session.SetCapabilitySnapshotContextForTesting(
                packAvailability,
                new ToolRoutingCatalogDiagnostics {
                    TotalTools = 1,
                    RoutingAwareTools = 1,
                    MissingRoutingContractTools = 0,
                    DomainFamilyTools = 0,
                    ExpectedDomainFamilyMissingTools = 0,
                    DomainFamilyMissingActionTools = 0,
                    ActionWithoutFamilyTools = 0,
                    FamilyActionConflictFamilies = 0,
                    FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
                });
        }

        if (orchestrationCatalog is not null) {
            session.SetToolOrchestrationCatalogForTesting(orchestrationCatalog);
        }

        return session.BuildToolRoutingSearchTextForTesting(definition);
    }

    private static string BuildModelPlannerPrompt(
        string requestText,
        IReadOnlyList<ToolDefinition> definitions,
        int limit,
        IReadOnlyList<ToolPackAvailabilityInfo>? packAvailability = null) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        if (packAvailability is { Count: > 0 }) {
            session.SetCapabilitySnapshotContextForTesting(
                packAvailability,
                new ToolRoutingCatalogDiagnostics {
                    TotalTools = definitions.Count,
                    RoutingAwareTools = 0,
                    MissingRoutingContractTools = 0,
                    DomainFamilyTools = 0,
                    ExpectedDomainFamilyMissingTools = 0,
                    DomainFamilyMissingActionTools = 0,
                    ActionWithoutFamilyTools = 0,
                    FamilyActionConflictFamilies = 0,
                    FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
                });
        }

        return session.BuildModelPlannerPromptForTesting(requestText, definitions, limit);
    }

    private static IReadOnlyList<string> ExtractChatInputTextItems(object input) {
        var toJson = input.GetType().GetMethod("ToJson", BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? throw new InvalidOperationException("ChatInput.ToJson not found.");
        var rawItems = Assert.IsType<JsonArray>(toJson.Invoke(input, Array.Empty<object>()));
        var items = new List<string>();
        for (var i = 0; i < rawItems.Count; i++) {
            var item = rawItems[i].AsObject();
            if (item is null
                || !string.Equals(item.GetString("type"), "text", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var text = item.GetString("text");
            if (!string.IsNullOrWhiteSpace(text)) {
                items.Add(text);
            }
        }

        return items;
    }

    private sealed class SyntheticRepresentativeExamplePack : IToolPack, IToolPackCatalogProvider, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "customx",
            Name = "CustomX",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_probe",
                    Description = "Probe custom runtime state.",
                    RepresentativeExamples = new[] {
                        "inspect the custom endpoint state through pack-owned metadata"
                    }
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_connectivity_probe",
                    Description = "Probe custom runtime reachability."
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_followup",
                    Description = "Collect custom follow-up evidence."
                }
            };
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                Pack = "customx",
                Engine = "CustomX",
                Tools = new[] { "custom_probe", "custom_connectivity_probe", "custom_followup" },
                RuntimeCapabilities = new ToolPackRuntimeCapabilitiesModel {
                    PreferredEntryTools = new[] { "custom_probe" },
                    PreferredProbeTools = new[] { "custom_connectivity_probe" }
                },
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "custom_runtime_triage",
                        Summary = "Stabilize the remote endpoint before deeper follow-up.",
                        WhenToUse = "Use when the remote endpoint is known but reachability or runtime readiness is still uncertain.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Probe the endpoint first",
                                SuggestedTools = new[] { "custom_connectivity_probe" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Collect follow-up evidence",
                                SuggestedTools = new[] { "custom_followup" }
                            }
                        },
                        VerificationTools = new[] { "custom_followup" }
                    }
                }
            };
        }
    }

    private sealed class SyntheticRecipeHelperPack : IToolPack, IToolPackCatalogProvider, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "customx",
            Name = "CustomX",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_connectivity_probe",
                    Description = "Probe custom runtime reachability."
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_recipe_resolver",
                    Description = "Resolve recipe-scoped runtime context."
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_recipe_directory_probe",
                    Description = "Probe unrelated directory recipe context."
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "custom_followup",
                    Description = "Collect custom follow-up evidence."
                }
            };
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                Pack = "customx",
                Engine = "CustomX",
                Tools = new[] { "custom_connectivity_probe", "custom_recipe_resolver", "custom_followup" },
                RuntimeCapabilities = new ToolPackRuntimeCapabilitiesModel {
                    PreferredProbeTools = new[] { "custom_connectivity_probe" }
                },
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "custom_runtime_triage",
                        Summary = "Stabilize the remote endpoint before deeper follow-up.",
                        WhenToUse = "Use when runtime reachability is uncertain before collecting follow-up evidence.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Probe the endpoint first",
                                SuggestedTools = new[] { "custom_connectivity_probe" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Resolve recipe-scoped runtime context",
                                SuggestedTools = new[] { "custom_recipe_resolver" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Probe unrelated directory recipe context",
                                SuggestedTools = new[] { "custom_recipe_directory_probe" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Collect follow-up evidence",
                                SuggestedTools = new[] { "custom_followup" }
                            }
                        },
                        VerificationTools = new[] { "custom_followup" }
                    }
                }
            };
        }
    }

    private sealed class SyntheticDeferredAffordancePromptPack : IToolPack, IToolPackCatalogProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "custom-deferred",
            Name = "Custom Deferred",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "email_message_compose",
                    Description = "Compose an email summary.",
                    RepresentativeExamples = new[] {
                        "send an email summary after the run"
                    }
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "report_snapshot_publish",
                    Description = "Publish a report snapshot.",
                    RepresentativeExamples = new[] {
                        "publish a monitoring report snapshot"
                    }
                }
            };
        }
    }

    private sealed class SyntheticCapabilityDescriptorPack : IToolPack, IToolPackCatalogProvider {
        private readonly IReadOnlyList<ToolPackToolCatalogEntryModel> _catalogEntries;

        public SyntheticCapabilityDescriptorPack(string packId, IReadOnlyList<string> capabilityTags, params string[] toolNames) {
            Descriptor = new ToolPackDescriptor {
                Id = packId,
                Name = packId,
                Tier = ToolCapabilityTier.ReadOnly,
                SourceKind = "open_source",
                CapabilityTags = capabilityTags
            };
            _catalogEntries = toolNames
                .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
                .Select(toolName => new ToolPackToolCatalogEntryModel {
                    Name = toolName,
                    Description = toolName
                })
                .ToArray();
        }

        public ToolPackDescriptor Descriptor { get; }

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return _catalogEntries;
        }
    }

    private sealed class SyntheticForeignRecipeHelperPack : IToolPack, IToolPackCatalogProvider, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "foreignx",
            Name = "ForeignX",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "open_source"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "foreign_recipe_resolver",
                    Description = "Resolve foreign recipe-scoped runtime context."
                }
            };
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                Pack = "foreignx",
                Engine = "ForeignX",
                Tools = new[] { "foreign_recipe_resolver" },
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "custom_runtime_triage",
                        Summary = "Cross-pack recipe reuse.",
                        WhenToUse = "Use when another pack reuses the same recipe identifier.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Resolve foreign runtime context",
                                SuggestedTools = new[] { "foreign_recipe_resolver" }
                            }
                        }
                    }
                }
            };
        }
    }
}
