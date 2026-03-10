using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Validates planner and lexical-routing prompts include schema hints.
/// </summary>
public sealed class ChatServicePlannerPromptTests {
    private static readonly MethodInfo BuildModelPlannerPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildModelPlannerPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildModelPlannerPrompt not found.");

    private static readonly MethodInfo BuildToolRoutingSearchTextMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoutingSearchText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoutingSearchText not found.");
    private static readonly MethodInfo TokenizeRoutingTokensMethod =
        typeof(ChatServiceSession).GetMethod("TokenizeRoutingTokens", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TokenizeRoutingTokens not found.");

    private static readonly MethodInfo SelectWeightedToolSubsetMethod =
        typeof(ChatServiceSession).GetMethod("SelectWeightedToolSubset", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("SelectWeightedToolSubset not found.");
    private static readonly MethodInfo BuildModelPlannerCandidatesMethod =
        typeof(ChatServiceSession).GetMethod("BuildModelPlannerCandidates", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildModelPlannerCandidates not found.");
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

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "top 5 system events from AD0",
            definitions,
            6
        }));

        Assert.Contains("required: log_name", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("args: ", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traits: table_view_projection", prompt, StringComparison.OrdinalIgnoreCase);
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

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "inspect tls posture on the same domain controller",
            definitions,
            4
        }));

        Assert.Contains("target_scoping(search_base_dn, computer_name)", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote_host_targeting(computer_name)", prompt, StringComparison.OrdinalIgnoreCase);
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

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "summarize contoso.com",
            definitions,
            4
        }));

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

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
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
            4
        }));

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

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
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
            4
        }));

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

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            """
            [Planner context]
            ix:planner-context:v1
            requires_live_execution: true
            missing_live_evidence: cert status and memory usage
            preferred_pack_ids: active_directory, system
            preferred_tool_names: ad_ldap_diagnostics, system_hardware_summary
            handoff_target_pack_ids: system
            handoff_target_tool_names: system_metrics_summary
            matching_skills: ad_domain.scope_hosts, system.host_baseline
            allow_cached_evidence_reuse: false

            continue from the same DC scope
            """,
            definitions,
            4
        }));

        Assert.Contains("Execution intent:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fresh live execution is required", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Missing live evidence: cert status and memory usage", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred packs: active_directory, system", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred tools: ad_ldap_diagnostics, system_hardware_summary", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target packs: system", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target tools: system_metrics_summary", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Matching reusable skills:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_domain.scope_hosts, system.host_baseline", prompt, StringComparison.OrdinalIgnoreCase);
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

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
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
            4
        }));

        Assert.Contains("Missing live evidence: ldap certificate posture", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred tools: ad_ldap_diagnostics, system_certificate_posture", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Target tools: system_certificate_posture", prompt, StringComparison.OrdinalIgnoreCase);
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
                         {"ok":true,"next_actions":[{"tool":"ad_ldap_diagnostics","mutating":false,"arguments":{"domain_controller":"ad0.contoso.com"}}]}
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
            out var handoffTargetPackIds,
            out var handoffTargetToolNames,
            out var continuationSourceTool,
            out var continuationReason,
            out var continuationConfidence,
            out var matchingSkills,
            out var allowCachedEvidenceReuse);

        Assert.True(parsed);
        Assert.False(requiresLiveExecution);
        Assert.Equal(string.Empty, missingLiveEvidence);
        Assert.Contains("active_directory", preferredPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_diagnostics", preferredToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(handoffTargetPackIds);
        Assert.Empty(handoffTargetToolNames);
        Assert.Equal("ad_monitoring_probe_run", continuationSourceTool);
        Assert.Equal("prefer ad_ldap_diagnostics after prior tool output", continuationReason);
        Assert.Equal("medium", continuationConfidence);
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
            out var handoffTargetPackIds,
            out var handoffTargetToolNames,
            out var continuationSourceTool,
            out var continuationReason,
            out var continuationConfidence,
            out var matchingSkills,
            out var allowCachedEvidenceReuse);

        Assert.True(parsed);
        Assert.True(requiresLiveExecution);
        Assert.Equal("ldap certificate posture", missingLiveEvidence);
        Assert.Contains("active_directory", preferredPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", preferredPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_diagnostics", preferredToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_hardware_summary", preferredToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", handoffTargetPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_metrics_summary", handoffTargetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ad_monitoring_probe_run", continuationSourceTool);
        Assert.Equal("prefer ad_ldap_diagnostics after prior tool output", continuationReason);
        Assert.Equal("high", continuationConfidence);
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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("remote_host_targeting", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_scope", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("computer_name", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("search_base_dn", searchText, StringComparison.OrdinalIgnoreCase);
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

        var ldapSearchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { ldapDefinition }));
        var systemSearchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { systemDefinition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

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

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack eventlog", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:eventlog", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack event_log", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:event_log", searchText, StringComparison.OrdinalIgnoreCase);
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

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(BuildModelPlannerCandidatesMethod.Invoke(
            null,
            new object?[] {
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
                4,
                ToolOrchestrationCatalog.Build(definitions)
            }));

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

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(BuildModelPlannerCandidatesMethod.Invoke(
            null,
            new object?[] {
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
                4,
                ToolOrchestrationCatalog.Build(definitions)
            }));

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool => string.Equals(tool.Name, "system_hardware_summary", StringComparison.OrdinalIgnoreCase));
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
                             {"ok":true,"next_actions":[{"tool":"ad_ldap_diagnostics","mutating":false,"arguments":{"domain_controller":"ad0.contoso.com"}}]}
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

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(BuildModelPlannerCandidatesMethod.Invoke(
            null,
            new object?[] {
                definitions,
                augmented,
                4,
                ToolOrchestrationCatalog.Build(definitions)
            }));

        Assert.InRange(selected.Count, 24, 24);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase));
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
}
