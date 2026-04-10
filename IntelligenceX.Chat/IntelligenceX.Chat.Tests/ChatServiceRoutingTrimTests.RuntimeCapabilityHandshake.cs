using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void RuntimeCapabilityHandshake_IncludesCapabilitySnapshotWhenRuntimeMetadataPresent() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var nowTicks = DateTime.UtcNow.Ticks;
        session.SetToolOrchestrationCatalogForTesting(CreateCapabilityHandshakeOrchestrationCatalog());
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "AD Playground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    EngineId = "adplayground",
                    CapabilityTags = new[] { "directory", "remote_analysis" },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "eventlog",
                    Name = "Event Log",
                    SourceKind = "builtin",
                    EngineId = "eventviewerx",
                    CapabilityTags = new[] { "event_logs", "evtx" },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "disabled-pack",
                    Name = "Disabled",
                    SourceKind = "builtin",
                    Enabled = false
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 17,
                RoutingAwareTools = 12,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 5
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "public_domain",
                        ActionId = "query_whois",
                        ToolCount = 4
                    }
                }
            });
        session.SetToolRoutingStatsForTesting(new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> {
            ["ad_replication_summary"] = (nowTicks, nowTicks),
            ["eventlog_live_query"] = (nowTicks, nowTicks)
        });

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting(
            resolvedModel: "gpt-5",
            baseInstructions: "Base instructions");
        var instructionsText = Assert.IsType<string>(instructions);

        Assert.StartsWith("Base instructions", instructionsText);
        Assert.Contains("ix:runtime-identity:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:capability-snapshot:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:skills:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered_tools: 17", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_count: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugin_count: 3", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugin_count: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot: service_runtime, packs 3, plugins 3", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot_packs:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD Playground[enabled|active|", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot_plugins:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD Playground[enabled|active|", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("packs=active_directory", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_packs: active_directory, eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugins: active_directory, eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dangerous_tools_enabled: false", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_engines: adplayground, eventviewerx", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_capability_tags: directory, remote_analysis, event_logs, evtx", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("routing_families: ad_domain, public_domain", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("representative_live_examples: discover effective AD scope and the reachable domain controllers before choosing deeper directory tools", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deferred_work_affordances: background_followup", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deferred_work_affordance_details: background_followup[runtime_scheduler]", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_pack_followup_targets: Event Log, System", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_local_capable_tools: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_local_capable_packs: active_directory, eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_target_scoped_tools: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_target_scoped_packs: active_directory, eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_remote_host_targeting_tools: 1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_remote_host_targeting_packs: eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_environment_discover_tools: 1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_environment_discover_packs: active_directory", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_write_capable_tools: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_governed_write_tools: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_auth_required_tools: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_probe_capable_tools: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills: ad_domain.scope_hosts, public_domain.query_whois", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("healthy_tools:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_replication_summary", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog_live_query", instructionsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_RuntimeCapabilitySnapshotIncludesRegistrationFirstToolingProvenance() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetToolOrchestrationCatalogForTesting(CreateCapabilityHandshakeOrchestrationCatalog());
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "AD Playground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    EngineId = "adplayground",
                    CapabilityTags = new[] { "directory", "remote_analysis" },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "eventlog",
                    Name = "Event Log",
                    SourceKind = "builtin",
                    EngineId = "eventviewerx",
                    CapabilityTags = new[] { "event_logs", "evtx" },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "disabled-pack",
                    Name = "Disabled",
                    SourceKind = "builtin",
                    Enabled = false
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 17,
                RoutingAwareTools = 12,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.NotNull(snapshot.ToolingSnapshot);
        Assert.Equal("service_runtime", snapshot.ToolingSnapshot!.Source);
        Assert.Equal(3, snapshot.ToolingSnapshot.Packs.Length);
        Assert.Equal(3, snapshot.ToolingSnapshot.Plugins.Length);
        Assert.Contains(snapshot.ToolingSnapshot.Packs, static pack => string.Equals(pack.Name, "AD Playground", StringComparison.OrdinalIgnoreCase) && string.Equals(pack.EngineId, "adplayground", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.ToolingSnapshot.Plugins, static plugin => string.Equals(plugin.Id, "eventlog", StringComparison.OrdinalIgnoreCase) && plugin.Enabled);
        Assert.Contains(snapshot.ToolingSnapshot.Plugins, static plugin => string.Equals(plugin.Id, "disabled-pack", StringComparison.OrdinalIgnoreCase) && !plugin.Enabled);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_PrefersResolvedPluginSkillInventoryOverRoutingFallback() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    SourceKind = "open_source",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 4,
                RoutingAwareTools = 4,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 1,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 4
                    }
                }
            },
            pluginAvailability: new[] {
                new ToolPluginAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    Origin = "plugin_folder",
                    SourceKind = "open_source",
                    DefaultEnabled = true,
                    Enabled = true,
                    PackIds = new[] { "plugin-loader-test" },
                    SkillIds = new[] { "inventory-test", "network-recon" }
                }
            });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.Equal(new[] { "inventory-test", "network-recon" }, snapshot.Skills);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_MergesConnectedRuntimeSkillsWithPluginInventoryBeforeRoutingFallback() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    SourceKind = "open_source",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 4,
                RoutingAwareTools = 4,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 1,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 4
                    }
                }
            },
            pluginAvailability: new[] {
                new ToolPluginAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    Origin = "plugin_folder",
                    SourceKind = "open_source",
                    DefaultEnabled = true,
                    Enabled = true,
                    PackIds = new[] { "plugin-loader-test" },
                    SkillIds = new[] { "inventory-test", "network-recon" }
                }
            },
            connectedRuntimeSkills: new[] { "repo-search", "task-runner" });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.Equal(new[] { "inventory-test", "network-recon", "repo-search", "task-runner" }, snapshot.Skills);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_ProducesStructuredCapabilityArtifact() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var nowTicks = DateTime.UtcNow.Ticks;
        session.SetToolOrchestrationCatalogForTesting(CreateCapabilityHandshakeOrchestrationCatalog());
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "AD Playground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    EngineId = "adplayground",
                    CapabilityTags = new[] { "directory", "remote_analysis" },
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 9,
                RoutingAwareTools = 9,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 2
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "public_domain",
                        ActionId = "query_whois",
                        ToolCount = 1
                    }
                }
            });
        session.SetToolRoutingStatsForTesting(new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)> {
            ["ad_replication_summary"] = (nowTicks, nowTicks)
        });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.True(snapshot.ToolingAvailable);
        Assert.Equal(9, snapshot.RegisteredTools);
        Assert.Equal(1, snapshot.EnabledPackCount);
        Assert.Equal(1, snapshot.PluginCount);
        Assert.Equal(1, snapshot.EnabledPluginCount);
        Assert.Equal("active_directory", Assert.Single(snapshot.EnabledPackIds));
        Assert.Equal("active_directory", Assert.Single(snapshot.EnabledPluginIds));
        Assert.Equal("adplayground", Assert.Single(snapshot.EnabledPackEngineIds));
        Assert.Contains("directory", snapshot.EnabledCapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("remote_analysis", snapshot.EnabledCapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.False(snapshot.DangerousToolsEnabled);
        Assert.Empty(snapshot.DangerousPackIds);
        Assert.Equal(2, snapshot.RoutingFamilies.Length);
        Assert.Equal(2, snapshot.FamilyActions.Length);
        Assert.Equal("ad_domain.scope_hosts", snapshot.Skills[0]);
        Assert.Equal(
            "discover effective AD scope and the reachable domain controllers before choosing deeper directory tools",
            Assert.Single(snapshot.RepresentativeExamples));
        Assert.Equal(new[] { "Event Log", "System" }, snapshot.CrossPackTargetPackDisplayNames);
        Assert.Equal("ad_replication_summary", Assert.Single(snapshot.HealthyTools));
        Assert.NotNull(snapshot.Autonomy);
        Assert.Equal(1, snapshot.Autonomy!.TargetScopedToolCount);
        Assert.Equal(0, snapshot.Autonomy.RemoteHostTargetingToolCount);
        Assert.Equal(1, snapshot.Autonomy!.EnvironmentDiscoverToolCount);
        Assert.Equal(0, snapshot.Autonomy.WriteCapableToolCount);
        Assert.Equal(0, snapshot.Autonomy.AuthenticationRequiredToolCount);
        Assert.Equal(0, snapshot.Autonomy.ProbeCapableToolCount);
        Assert.Equal(new[] { "active_directory" }, snapshot.Autonomy.TargetScopedPackIds);
        Assert.Empty(snapshot.Autonomy.RemoteHostTargetingPackIds);
        Assert.Equal(new[] { "active_directory" }, snapshot.Autonomy.EnvironmentDiscoverPackIds);
        Assert.Equal("background_followup", Assert.Single(snapshot.DeferredWorkAffordances).CapabilityId);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_ExposesPackDeclaredDeferredAffordances() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new[] {
            new ToolDefinition(
                "email_smtp_send",
                "Send SMTP mail.",
                ToolSchema.Object(("send", ToolSchema.Boolean("Apply send."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational,
                    DomainIntentFamily = "message_delivery",
                    DomainIntentActionId = "send_message"
                }),
            new ToolDefinition(
                "testimox_report_snapshot_get",
                "Get monitoring report snapshot.",
                ToolSchema.Object(("report_key", ToolSchema.String("Report key."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "testimox_analytics",
                    Role = ToolRoutingTaxonomy.RoleResolver,
                    DomainIntentFamily = "monitoring_artifacts",
                    DomainIntentActionId = "open_report_snapshot"
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions, new IToolPack[] {
            new SyntheticDeferredAffordanceOverlayPack()
        }));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "email",
                    Name = "Email",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityEmail, "email", "smtp" },
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "testimox_analytics",
                    Name = "TestimoX Analytics",
                    SourceKind = "builtin",
                    CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityReporting, "analytics", "reporting" },
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 2,
                RoutingAwareTools = 2,
                ExplicitRoutingTools = 2,
                MissingRoutingContractTools = 0,
                MissingPackIdTools = 0,
                MissingRoleTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "message_delivery",
                        ActionId = "send_message",
                        ToolCount = 1
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "monitoring_artifacts",
                        ActionId = "open_report_snapshot",
                        ToolCount = 1
                    }
                }
            });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.Equal(
            new[] { "background_followup", "email", "reporting" },
            snapshot.DeferredWorkAffordances.Select(static affordance => affordance.CapabilityId).ToArray());
        var emailAffordance = Assert.Single(snapshot.DeferredWorkAffordances, static affordance => string.Equals(affordance.CapabilityId, "email", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Email", emailAffordance.DisplayName);
        Assert.Equal("pack_declared", emailAffordance.AvailabilityMode);
        Assert.False(emailAffordance.SupportsBackgroundExecution);
        Assert.Equal("email", Assert.Single(emailAffordance.PackIds));
        Assert.Equal("message_delivery", Assert.Single(emailAffordance.RoutingFamilies));
        Assert.Equal("send a verification or notification email with SMTP", Assert.Single(emailAffordance.RepresentativeExamples));
        var reportingAffordance = Assert.Single(snapshot.DeferredWorkAffordances, static affordance => string.Equals(affordance.CapabilityId, "reporting", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Reporting", reportingAffordance.DisplayName);
        Assert.Equal("testimox_analytics", Assert.Single(reportingAffordance.PackIds));
        Assert.Equal("monitoring_artifacts", Assert.Single(reportingAffordance.RoutingFamilies));
        Assert.Equal("open a stored monitoring HTML report snapshot from an allowed history directory", Assert.Single(reportingAffordance.RepresentativeExamples));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_UsesOrchestrationCatalogForPackIdentityWhenPackAvailabilityMissing() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetToolOrchestrationCatalogForTesting(CreateCapabilityHandshakeOrchestrationCatalog());
        session.SetCapabilitySnapshotContextForTesting(
            Array.Empty<ToolPackAvailabilityInfo>(),
            ToolRoutingCatalogDiagnosticsBuilder.Build(CreateCapabilityHandshakeAutonomyDefinitions()));

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.Contains("active_directory", snapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog", snapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("adplayground", snapshot.EnabledPackEngineIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("directory", snapshot.EnabledCapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("remote_analysis", snapshot.EnabledCapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.True(snapshot.ToolingAvailable);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_UsesOrchestrationCatalogForDeferredAffordancesWhenPackAvailabilityMissing() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new[] {
            new ToolDefinition(
                "email_smtp_send",
                "Send SMTP mail.",
                ToolSchema.Object(("send", ToolSchema.Boolean("Apply send."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational,
                    DomainIntentFamily = "message_delivery",
                    DomainIntentActionId = "send_message"
                }),
            new ToolDefinition(
                "testimox_report_snapshot_get",
                "Get monitoring report snapshot.",
                ToolSchema.Object(("report_key", ToolSchema.String("Report key."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "testimox_analytics",
                    Role = ToolRoutingTaxonomy.RoleResolver,
                    DomainIntentFamily = "monitoring_artifacts",
                    DomainIntentActionId = "open_report_snapshot"
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions, new IToolPack[] {
            new SyntheticDeferredAffordanceEmailCapabilityPack(),
            new SyntheticDeferredAffordanceReportingCapabilityPack(),
            new SyntheticDeferredAffordanceOverlayPack()
        }));
        session.SetCapabilitySnapshotContextForTesting(
            Array.Empty<ToolPackAvailabilityInfo>(),
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 2,
                RoutingAwareTools = 2,
                ExplicitRoutingTools = 2,
                MissingRoutingContractTools = 0,
                MissingPackIdTools = 0,
                MissingRoleTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "message_delivery",
                        ActionId = "send_message",
                        ToolCount = 1
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "monitoring_artifacts",
                        ActionId = "open_report_snapshot",
                        ToolCount = 1
                    }
                }
            });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.Contains(snapshot.DeferredWorkAffordances, static affordance => string.Equals(affordance.CapabilityId, "email", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.DeferredWorkAffordances, static affordance => string.Equals(affordance.CapabilityId, "reporting", StringComparison.OrdinalIgnoreCase));
        var emailAffordance = Assert.Single(snapshot.DeferredWorkAffordances, static affordance => string.Equals(affordance.CapabilityId, "email", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("email", Assert.Single(emailAffordance.PackIds));
        Assert.Equal("message_delivery", Assert.Single(emailAffordance.RoutingFamilies));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_ExposesDangerousPackVisibility() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new[] {
            new ToolDefinition(
                name: "ad_user_lifecycle",
                description: "Governed AD lifecycle write.",
                parameters: ToolSchema.Object(
                        ("identity", ToolSchema.String("Identity.")),
                        ("apply", ToolSchema.Boolean("Apply.")))
                    .WithWriteGovernanceDefaults(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue("apply"))
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "active_directory",
                    Name = "Active Directory",
                    SourceKind = "builtin",
                    Enabled = true,
                    IsDangerous = false,
                    Tier = ToolCapabilityTier.SensitiveRead
                }
            },
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

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.True(snapshot.DangerousToolsEnabled);
        Assert.Equal(new[] { "active_directory" }, snapshot.DangerousPackIds);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_TracksWriteCapableNonDangerousPackVisibility() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new[] {
            new ToolDefinition(
                name: "email_smtp_send",
                description: "Send SMTP mail.",
                parameters: ToolSchema.Object(
                        ("send", ToolSchema.Boolean("Apply send.")))
                    .WithWriteGovernanceDefaults(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue("send"))
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "email",
                    Name = "Email",
                    SourceKind = "builtin",
                    Enabled = true,
                    IsDangerous = false,
                    Tier = ToolCapabilityTier.SensitiveRead
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 1,
                RoutingAwareTools = 1,
                ExplicitRoutingTools = 1,
                MissingRoutingContractTools = 0,
                MissingPackIdTools = 0,
                MissingRoleTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.True(snapshot.DangerousToolsEnabled);
        Assert.Equal(new[] { "email" }, snapshot.DangerousPackIds);
        Assert.NotNull(snapshot.Autonomy);
        Assert.Equal(1, snapshot.Autonomy!.WriteCapableToolCount);
        Assert.Equal(new[] { "email" }, snapshot.Autonomy.WriteCapablePackIds);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_IncludesDangerousPackVisibilityWhenDangerousToolsAreEnabled() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new[] {
            new ToolDefinition(
                name: "ad_user_lifecycle",
                description: "Governed AD lifecycle write.",
                parameters: ToolSchema.Object(
                        ("identity", ToolSchema.String("Identity.")),
                        ("apply", ToolSchema.Boolean("Apply.")))
                    .WithWriteGovernanceDefaults(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue("apply"))
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "active_directory",
                    Name = "Active Directory",
                    SourceKind = "builtin",
                    Enabled = true,
                    IsDangerous = false,
                    Tier = ToolCapabilityTier.SensitiveRead
                }
            },
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

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5");
        var instructionsText = Assert.IsType<string>(instructions);

        Assert.Contains("dangerous_tools_enabled: true", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dangerous_packs: active_directory", instructionsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuildRuntimeCapabilitySnapshot_ExposesBackgroundSchedulerSummary() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerAllowedPackIds.Add("system");
        options.BackgroundSchedulerBlockedPackIds.Add("active_directory");
        options.BackgroundSchedulerAllowedThreadIds.Add("thread-runtime-capability-scheduler");
        options.BackgroundSchedulerBlockedThreadIds.Add("thread-runtime-capability-scheduler-blocked");
        var session = new ChatServiceSession(options, Stream.Null);
        const string threadId = "thread-runtime-capability-scheduler";
        var definitions = new[] {
            new ToolDefinition(
                "remote_disk_inventory",
                "Remote disk inventory",
                ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition("system_info", "system info", ToolSchema.Object().NoAdditionalProperties())
        };
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "system",
                    Name = "System",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 2,
                RoutingAwareTools = 2,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-cap.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.NotNull(snapshot.BackgroundScheduler);
        Assert.True(snapshot.BackgroundScheduler!.SupportsPersistentQueue);
        Assert.False(snapshot.BackgroundScheduler.DaemonEnabled);
        Assert.False(snapshot.BackgroundScheduler.AutoPauseEnabled);
        Assert.False(snapshot.BackgroundScheduler.ManualPauseActive);
        Assert.Equal(5, snapshot.BackgroundScheduler.FailureThreshold);
        Assert.Equal(300, snapshot.BackgroundScheduler.FailurePauseSeconds);
        Assert.False(snapshot.BackgroundScheduler.Paused);
        Assert.Equal(new[] { "system" }, snapshot.BackgroundScheduler.AllowedPackIds);
        Assert.Equal(new[] { "active_directory" }, snapshot.BackgroundScheduler.BlockedPackIds);
        Assert.Equal(new[] { "thread-runtime-capability-scheduler" }, snapshot.BackgroundScheduler.AllowedThreadIds);
        Assert.Equal(new[] { "thread-runtime-capability-scheduler-blocked" }, snapshot.BackgroundScheduler.BlockedThreadIds);
        Assert.Equal(1, snapshot.BackgroundScheduler.TrackedThreadCount);
        Assert.Equal(1, snapshot.BackgroundScheduler.ReadyThreadCount);
        Assert.Equal(1, snapshot.BackgroundScheduler.ReadyItemCount);
        Assert.Equal(0, snapshot.BackgroundScheduler.CompletedExecutionCount);
        Assert.Equal(0, snapshot.BackgroundScheduler.RequeuedExecutionCount);
        Assert.Equal(0, snapshot.BackgroundScheduler.ReleasedExecutionCount);
        Assert.Contains(threadId, snapshot.BackgroundScheduler.ReadyThreadIds, StringComparer.Ordinal);
        var threadSummary = Assert.Single(snapshot.BackgroundScheduler.ThreadSummaries);
        Assert.Equal(threadId, threadSummary.ThreadId);
        Assert.Equal(1, threadSummary.ReadyItemCount);
        Assert.Empty(snapshot.BackgroundScheduler.RecentActivity);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_ReportsDescriptorPreviewWhenBootstrapHasNotStarted() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5");
        var instructionsText = Assert.IsType<string>(instructions);

        Assert.Contains("ix:runtime-identity:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:capability-snapshot:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:skills:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered_tools: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot: deferred_descriptor_preview", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enabled_pack_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plugin_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enabled_plugin_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_packs:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugins:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deferred_work_affordances:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Null(TryReadInstructionLine(instructionsText, "skills:"));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BoundsCapabilityLists() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var nowTicks = DateTime.UtcNow.Ticks;
        session.SetCapabilitySnapshotContextForTesting(
            Enumerable.Range(1, 20)
                .Select(index => new ToolPackAvailabilityInfo {
                    Id = $"Pack_{index:00}",
                    Name = $"Pack {index:00}",
                    SourceKind = "builtin",
                    Enabled = true
                })
                .ToArray(),
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 120,
                RoutingAwareTools = 120,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 8,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Enumerable.Range(1, 8)
                    .Select(index => new ToolRoutingFamilyActionSummary {
                        Family = $"custom_{index:00}_domain",
                        ActionId = $"action_{index:00}",
                        ToolCount = 1
                    })
                    .ToArray()
            });
        session.SetToolRoutingStatsForTesting(
            Enumerable.Range(1, 20)
                .ToDictionary(
                    index => $"tool_{index:00}",
                    index => (LastUsedUtcTicks: nowTicks - index, LastSuccessUtcTicks: nowTicks - index)));

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5");
        var instructionsText = Assert.IsType<string>(instructions);

        var enabledPackLine = TryReadInstructionLine(instructionsText, "enabled_packs:");
        var healthyToolsLine = TryReadInstructionLine(instructionsText, "healthy_tools:");
        var routingFamiliesLine = TryReadInstructionLine(instructionsText, "routing_families:");
        var skillsLine = TryReadInstructionLine(instructionsText, "skills:");
        Assert.NotNull(enabledPackLine);
        var enabledPluginLine = TryReadInstructionLine(instructionsText, "enabled_plugins:");
        Assert.NotNull(healthyToolsLine);
        Assert.NotNull(routingFamiliesLine);
        Assert.NotNull(skillsLine);
        Assert.Equal(8, CountCsvItemsFromInstructionLine(enabledPackLine!, "enabled_packs:"));
        Assert.NotNull(enabledPluginLine);
        Assert.Equal(8, CountCsvItemsFromInstructionLine(enabledPluginLine!, "enabled_plugins:"));
        Assert.Equal(12, CountCsvItemsFromInstructionLine(healthyToolsLine!, "healthy_tools:"));
        Assert.Equal(6, CountCsvItemsFromInstructionLine(routingFamiliesLine!, "routing_families:"));
        Assert.Equal(8, CountCsvItemsFromInstructionLine(skillsLine!, "skills:"));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_HelloWarningsIncludeCapabilitySnapshot() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.AllowedRoots.Add(@"C:\logs");
        options.AllowedRoots.Add(@"D:\exports");
        options.BackgroundSchedulerAllowedPackIds.Add("system");
        options.BackgroundSchedulerBlockedPackIds.Add("active_directory");
        options.BackgroundSchedulerAllowedThreadIds.Add("thread-capability-scheduler-ready");
        options.BackgroundSchedulerBlockedThreadIds.Add("thread-capability-scheduler-blocked");
        var session = new ChatServiceSession(options, System.IO.Stream.Null);
        session.SetToolOrchestrationCatalogForTesting(CreateCapabilityHandshakeOrchestrationCatalog());
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "AD Playground",
                    Name = "AD Playground",
                    SourceKind = "builtin",
                    EngineId = "adplayground",
                    CapabilityTags = new[] { "directory", "remote_analysis" },
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 9,
                RoutingAwareTools = 9,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 2
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "public_domain",
                        ActionId = "query_whois",
                        ToolCount = 1
                    }
                }
            });

        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("marker='ix:capability-snapshot:v1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills_marker='ix:skills:v1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_count='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugin_count='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugin_count='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered_tools='9'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allowed_roots='2'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_available='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dangerous_tools_enabled='false'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote_reachability_mode='", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_daemon_enabled='false'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_auto_pause_enabled='false'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_manual_pause_active='false'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_failure_threshold='5'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_failure_pause_seconds='300'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_paused='false'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_allowed_packs='system'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_blocked_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_allowed_threads='thread-capability-scheduler-ready'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_blocked_threads='thread-capability-scheduler-blocked'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_ready_items='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_running_items='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_tracked_threads='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_completed_executions='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_requeued_executions='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_released_executions='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_consecutive_failures='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_persistent_queue='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_readonly_autoreplay='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_cross_thread='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count='2'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot_source='service_runtime'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot_pack_count='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot_plugin_count='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot_packs='AD Playground[enabled|active|", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot_plugins='AD Playground[enabled|active|", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("packs=active_directory", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugins='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_engines='adplayground'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_capability_tags='directory,remote_analysis'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("representative_examples='discover effective AD scope and the reachable domain controllers before choosing deeper directory tools'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_pack_followup_targets='Event Log,System'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_local_capable_tools='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_local_capable_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_target_scoped_tools='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_target_scoped_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_remote_host_targeting_tools='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_environment_discover_tools='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_environment_discover_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_write_capable_tools='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_governed_write_tools='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_auth_required_tools='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy_probe_capable_tools='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("routing_families='ad_domain,public_domain'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills='ad_domain.scope_hosts,public_domain.query_whois'", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_HelloWarningsExposeDangerousPackVisibility() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new[] {
            new ToolDefinition(
                name: "ad_user_lifecycle",
                description: "Governed AD lifecycle write.",
                parameters: ToolSchema.Object(
                        ("identity", ToolSchema.String("Identity.")),
                        ("apply", ToolSchema.Boolean("Apply.")))
                    .WithWriteGovernanceDefaults(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue("apply"))
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "active_directory",
                    Name = "Active Directory",
                    SourceKind = "builtin",
                    Enabled = true,
                    IsDangerous = false,
                    Tier = ToolCapabilityTier.SensitiveRead
                }
            },
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

        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("dangerous_tools_enabled='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dangerous_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_HelloWarningsIncludeBootstrapProgressAndCapabilitySnapshot() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var pendingBootstrap = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var warnings = session.BuildHelloStartupWarningsForTesting(pendingBootstrap.Task);

        Assert.Contains(
            warnings,
            static warning => warning.Contains("Tool bootstrap in progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeCapabilityHandshake_HelloWarningsAndRuntimeIdentityDescribeDeferredBootstrapWhenNotStarted() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var warnings = session.BuildHelloStartupWarningsForTesting(null);
        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
        var instructionsText = Assert.IsType<string>(session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5.4"));

        Assert.Contains(
            warnings,
            static warning => warning.Contains("Tool bootstrap deferred", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(snapshot.ToolingSnapshot);
        Assert.Equal("deferred_descriptor_preview", snapshot.ToolingSnapshot!.Source);
        Assert.Equal(0, snapshot.RegisteredTools);
        Assert.True(snapshot.ToolingSnapshot.Packs.Length > 0);
        Assert.Contains(
            snapshot.ToolingSnapshot.Packs,
            static pack => string.Equals(pack.Id, "eventlog", StringComparison.OrdinalIgnoreCase)
                           && pack.Enabled
                           && string.Equals(pack.ActivationState, ToolActivationStates.Deferred, StringComparison.OrdinalIgnoreCase)
                           && pack.CanActivateOnDemand);
        Assert.Contains(
            snapshot.ToolingSnapshot.Plugins,
            static plugin => string.Equals(plugin.Id, "eventlog", StringComparison.OrdinalIgnoreCase)
                             && plugin.Enabled
                             && string.Equals(plugin.ActivationState, ToolActivationStates.Deferred, StringComparison.OrdinalIgnoreCase)
                             && plugin.CanActivateOnDemand);
        Assert.Contains("runtime_bootstrap_state: deferred", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tooling_snapshot: deferred_descriptor_preview", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("|deferred|", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activatable_packs:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activatable_plugins:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("descriptor-only preview from known pack metadata", instructionsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_DeferredDescriptorPreviewIncludesManifestOnlyPlugins() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-runtime-plugin-preview");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), """
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin-loader-synthetic-catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "Ops.Bundle.dll",
              "entryType": "Ops.Bundle.PluginPack"
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);

            var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
            var instructionsText = Assert.IsType<string>(session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5.4"));

            Assert.NotNull(snapshot.ToolingSnapshot);
            Assert.Equal("deferred_descriptor_preview", snapshot.ToolingSnapshot!.Source);
            Assert.Equal(1, snapshot.PluginCount);
            Assert.Equal(1, snapshot.EnabledPluginCount);
            var declaredPackId = ToolPackBootstrap.NormalizePackId("plugin-loader-synthetic-catalog");
            Assert.Contains(
                snapshot.ToolingSnapshot.Plugins,
                plugin => string.Equals(plugin.Id, "ops_bundle", StringComparison.OrdinalIgnoreCase)
                          && plugin.PackIds.Contains(declaredPackId, StringComparer.OrdinalIgnoreCase)
                          && string.Equals(plugin.ActivationState, ToolActivationStates.Deferred, StringComparison.OrdinalIgnoreCase)
                          && plugin.CanActivateOnDemand);
            Assert.Contains("tooling_snapshot_plugins:", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("|deferred|", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("packs=" + declaredPackId, instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("activatable_plugins: ops_bundle", instructionsText, StringComparison.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RuntimeCapabilityHandshake_DeferredDescriptorPreviewBackgroundSchedulerSummaryDoesNotReenterPreviewConstruction() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-runtime-preview-background-scheduler");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), """
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["ops_inventory"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "Ops.Bundle.dll",
              "entryType": "Ops.Bundle.PluginPack",
              "tools": [
                {
                  "name": "ops_inventory_collect",
                  "description": "Collect host inventory.",
                  "category": "system",
                  "supportsLocalExecution": false,
                  "supportsRemoteExecution": true,
                  "supportsRemoteHostTargeting": true
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            const string threadId = "thread-deferred-preview-background-scheduler";
            var nowTicks = DateTime.UtcNow.Ticks;
            session.RememberThreadBackgroundWorkSnapshotForTesting(
                threadId,
                new ChatServiceSession.ThreadBackgroundWorkSnapshot(
                    QueuedCount: 0,
                    ReadyCount: 1,
                    RunningCount: 0,
                    CompletedCount: 0,
                    PendingReadOnlyCount: 1,
                    PendingUnknownCount: 0,
                    RecentEvidenceTools: new[] { "ops_inventory_collect" },
                    Items: new[] {
                        new ChatServiceSession.ThreadBackgroundWorkItem(
                            Id: "item-preview-background-scheduler",
                            Title: "Collect deferred inventory",
                            Request: "Collect deferred inventory for srv-preview.contoso.com",
                            State: "ready",
                            DependencyItemIds: Array.Empty<string>(),
                            EvidenceToolNames: new[] { "ops_inventory_collect" },
                            Kind: "tool_handoff",
                            Mutability: "read_only",
                            SourceToolName: "seed_plugin_probe_followup",
                            SourceCallId: "call-preview-background-scheduler",
                            TargetPackId: "ops_inventory",
                            TargetToolName: "ops_inventory_collect",
                            FollowUpKind: "tool_handoff",
                            FollowUpPriority: 100,
                            PreparedArgumentsJson: """{"target":"srv-preview.contoso.com"}""",
                            ResultReference: string.Empty,
                            ExecutionAttemptCount: 0,
                            LastExecutionCallId: string.Empty,
                            LastExecutionStartedUtcTicks: 0,
                            LastExecutionFinishedUtcTicks: 0,
                            LeaseExpiresUtcTicks: 0,
                            CreatedUtcTicks: nowTicks,
                            UpdatedUtcTicks: nowTicks)
                    }));
            session.ClearCachedToolDefinitionsForTesting();
            var persistedSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

            var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
            var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
            var handshake = Assert.Single(
                warnings,
                static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(snapshot.ToolingSnapshot);
            Assert.Equal("deferred_descriptor_preview", snapshot.ToolingSnapshot!.Source);
            Assert.Contains(
                snapshot.ToolingSnapshot.Plugins,
                static plugin => string.Equals(plugin.Id, "ops_bundle", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(plugin.ActivationState, ToolActivationStates.Deferred, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(snapshot.BackgroundScheduler);
            Assert.NotEmpty(persistedSnapshot.Items);
            Assert.Contains("background_scheduler_tracked_threads='", handshake, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("background_scheduler_ready_items='", handshake, StringComparison.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RuntimeCapabilityHandshake_BuiltInDeferredPreviewBackgroundSchedulerSummaryDoesNotReenterPreviewConstruction() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableDefaultPluginPaths = false;

        var session = new ChatServiceSession(options, Stream.Null);
        const string threadId = "thread-built-in-deferred-preview-background-scheduler";
        var nowTicks = DateTime.UtcNow.Ticks;
        session.RememberThreadBackgroundWorkSnapshotForTesting(
            threadId,
            new ChatServiceSession.ThreadBackgroundWorkSnapshot(
                QueuedCount: 0,
                ReadyCount: 1,
                RunningCount: 0,
                CompletedCount: 0,
                PendingReadOnlyCount: 1,
                PendingUnknownCount: 0,
                RecentEvidenceTools: new[] { "ad_environment_discover" },
                Items: new[] {
                    new ChatServiceSession.ThreadBackgroundWorkItem(
                        Id: "item-built-in-preview-background-scheduler",
                        Title: "Discover AD environment",
                        Request: "Show AD environment discovery for the current forest",
                        State: "ready",
                        DependencyItemIds: Array.Empty<string>(),
                        EvidenceToolNames: new[] { "ad_environment_discover" },
                        Kind: "tool_handoff",
                        Mutability: "read_only",
                        SourceToolName: "seed_builtin_probe_followup",
                        SourceCallId: "call-built-in-preview-background-scheduler",
                        TargetPackId: "active_directory",
                        TargetToolName: "ad_environment_discover",
                        FollowUpKind: "tool_handoff",
                        FollowUpPriority: 100,
                        PreparedArgumentsJson: """{"discovery_fallback":"current_forest"}""",
                        ResultReference: string.Empty,
                        ExecutionAttemptCount: 0,
                        LastExecutionCallId: string.Empty,
                        LastExecutionStartedUtcTicks: 0,
                        LastExecutionFinishedUtcTicks: 0,
                        LeaseExpiresUtcTicks: 0,
                        CreatedUtcTicks: nowTicks,
                        UpdatedUtcTicks: nowTicks)
                }));
        session.ClearCachedToolDefinitionsForTesting();
        var persistedSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(snapshot.ToolingSnapshot);
        Assert.Equal("deferred_descriptor_preview", snapshot.ToolingSnapshot!.Source);
        Assert.NotNull(snapshot.BackgroundScheduler);
        Assert.NotEmpty(persistedSnapshot.Items);
        Assert.Contains("background_scheduler_tracked_threads='", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_ready_items='", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_DeferredDescriptorPreviewInstructionsExposeDescriptorToolCandidates() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-runtime-plugin-preview-tools");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), """
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["ops_inventory"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "Ops.Bundle.dll",
              "entryType": "Ops.Bundle.PluginPack",
              "tools": [
                {
                  "name": "ops_inventory_collect",
                  "description": "Collect host inventory.",
                  "category": "system",
                  "supportsLocalExecution": false,
                  "supportsRemoteExecution": true,
                  "supportsRemoteHostTargeting": true,
                  "representativeExamples": ["collect inventory from a remote windows host"]
                },
                {
                  "name": "ops_inventory_report",
                  "description": "Summarize collected inventory.",
                  "category": "reporting",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false,
                  "representativeExamples": ["summarize inventory drift for the last collection"]
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);

            var instructionsText = Assert.IsType<string>(session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5.4"));

            Assert.Contains("runtime_bootstrap_state: deferred", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tooling_snapshot: deferred_descriptor_preview", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("descriptor_preview_tool_count: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("descriptor_preview_tools:", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ops_inventory_collect[pack=ops_inventory|category=system|scope=remote_only|read|remote_host]", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ops_inventory_report[pack=ops_inventory|category=reporting|scope=local_only|read]", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("descriptor_preview_examples:", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("collect inventory from a remote windows host", instructionsText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Descriptor preview tools are descriptor-only candidates. They are not live callable schemas yet", instructionsText, StringComparison.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RuntimeCapabilityHandshake_DeferredDescriptorPreviewInstructionsPreserveCatalogPriorityOrdering() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-runtime-plugin-preview-tool-order");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), """
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["ops_inventory"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "Ops.Bundle.dll",
              "entryType": "Ops.Bundle.PluginPack",
              "tools": [
                {
                  "name": "ops_pack_info",
                  "description": "Explain the ops inventory pack.",
                  "category": "system",
                  "routingRole": "pack_info",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                },
                {
                  "name": "ops_connectivity_probe",
                  "description": "Probe connectivity for ops inventory.",
                  "category": "system",
                  "routingRole": "diagnostic",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                },
                {
                  "name": "ops_zeta_query",
                  "description": "Collect host inventory.",
                  "category": "system",
                  "routingRole": "operational",
                  "supportsLocalExecution": false,
                  "supportsRemoteExecution": true,
                  "supportsRemoteHostTargeting": true,
                  "supportsConnectivityProbe": true,
                  "probeToolName": "ops_connectivity_probe",
                  "isSetupAware": true,
                  "setupToolName": "ops_setup",
                  "handoffTargetPackIds": ["system"],
                  "handoffTargetToolNames": ["system_info"],
                  "isRecoveryAware": true,
                  "recoveryToolNames": ["ops_setup"],
                  "representativeExamples": ["collect inventory from a remote windows host"]
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);

            var instructionsText = Assert.IsType<string>(session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5.4"));
            var prioritizedIndex = instructionsText.IndexOf("ops_zeta_query[", StringComparison.OrdinalIgnoreCase);
            var helperIndex = instructionsText.IndexOf("ops_connectivity_probe[", StringComparison.OrdinalIgnoreCase);
            var packInfoIndex = instructionsText.IndexOf("ops_pack_info[", StringComparison.OrdinalIgnoreCase);

            Assert.True(prioritizedIndex >= 0, "Expected deferred descriptor preview instructions to include ops_zeta_query.");
            Assert.True(helperIndex >= 0, "Expected deferred descriptor preview instructions to include ops_connectivity_probe.");
            Assert.True(packInfoIndex >= 0, "Expected deferred descriptor preview instructions to include ops_pack_info.");
            Assert.True(
                prioritizedIndex < helperIndex && helperIndex < packInfoIndex,
                "Expected deferred descriptor preview instructions to keep the entry tool ahead of the helper probe, and the helper probe ahead of pack-info guidance.");
            Assert.Contains(
                "ops_zeta_query[pack=ops_inventory|category=system|scope=remote_only|role=operational|read|remote_host|setup=ops_setup|probe=ops_connectivity_probe|recovery=ops_setup|handoff=system_info]",
                instructionsText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "ops_pack_info[pack=ops_inventory|category=system|scope=local_only|role=pack_info|read]",
                instructionsText,
                StringComparison.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RuntimeCapabilityHandshake_UsesExecutionContractsForRemoteReachabilityMode() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        IReadOnlyList<ToolDefinition> toolDefinitions = new List<ToolDefinition> {
            new ToolDefinition(
                name: "custom_remote_probe",
                description: "Probe a remote host.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                execution: new ToolExecutionContract {
                    IsExecutionAware = true,
                    ExecutionScope = ToolExecutionScopes.LocalOrRemote,
                    RemoteHostArguments = new[] { "machine_name" }
                })
        };
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "eventlog",
                    Name = "Event Log",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 1,
                RoutingAwareTools = 1,
                ExplicitRoutingTools = 1,
                InferredRoutingTools = 0,
                MissingRoutingContractTools = 0,
                MissingPackIdTools = 0,
                MissingRoleTools = 0,
                RemoteCapableTools = 1,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(toolDefinitions));

        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("remote_reachability_mode='remote_capable'", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_HelloWarningsExposeBackgroundSchedulerReadinessAcrossThreads() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-capability-scheduler-ready";
        var definitions = new[] {
            new ToolDefinition(
                "remote_disk_inventory",
                "remote disk inventory",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition("system_info", "system info", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "system",
                    Name = "System",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 2,
                RoutingAwareTools = 2,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-cap.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("background_scheduler_ready_items='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_running_items='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_tracked_threads='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_ready_threads='thread-capability-scheduler-ready'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_thread_summaries='thread-capability-scheduler-ready ready=1 running=0 queued=0", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeCapabilityHandshake_HelloWarningsExposeBackgroundSchedulerOutcomeTelemetry() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableBackgroundSchedulerDaemon = true;
        var session = new ChatServiceSession(options, Stream.Null);
        const string threadId = "thread-capability-scheduler-outcome";
        var definitions = new[] {
            new ToolDefinition(
                "remote_disk_inventory",
                "remote disk inventory",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition("system_info", "system info", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "system",
                    Name = "System",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 2,
                RoutingAwareTools = 2,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-cap.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        _ = await session.RunBackgroundSchedulerIterationAsyncForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            static (_, toolCall, _) => Task.FromResult<IReadOnlyList<ToolOutputDto>>(new[] {
                new ToolOutputDto {
                    CallId = toolCall.CallId,
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            }));

        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("background_scheduler_daemon_enabled='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_auto_pause_enabled='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_failure_threshold='5'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_failure_pause_seconds='300'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_paused='false'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_completed_executions='1'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_requeued_executions='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_released_executions='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_consecutive_failures='0'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_last_outcome='completed'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_recent_activity='completed tool=system_info thread=thread-capability-scheduler-outcome'", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeCapabilityHandshake_HelloWarningsExposeBackgroundSchedulerPauseState() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableBackgroundSchedulerDaemon = true;
        options.BackgroundSchedulerFailureThreshold = 2;
        options.BackgroundSchedulerFailurePauseSeconds = 120;
        var session = new ChatServiceSession(options, Stream.Null);
        var definitions = new[] {
            new ToolDefinition(
                "remote_disk_inventory",
                "remote disk inventory",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition("system_info", "system info", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "system",
                    Name = "System",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 2,
                RoutingAwareTools = 2,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 0,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = Array.Empty<ToolRoutingFamilyActionSummary>()
            });

        foreach (var threadId in new[] { "thread-capability-scheduler-pause-a", "thread-capability-scheduler-pause-b" }) {
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-" + threadId,
                        Name = "remote_disk_inventory",
                        ArgumentsJson = $$"""{"computer_name":"{{threadId}}.contoso.com"}"""
                    }
                },
                new[] {
                    new ToolOutputDto {
                        CallId = "call-" + threadId,
                        Ok = true,
                        Output = """{"ok":true}"""
                    }
                });
        }

        _ = await session.RunBackgroundSchedulerIterationAsyncForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            static (_, toolCall, _) => Task.FromResult<IReadOnlyList<ToolOutputDto>>(new[] {
                new ToolOutputDto {
                    CallId = toolCall.CallId,
                    Ok = false,
                    ErrorCode = "remote_probe_failed",
                    Output = """{"ok":false}"""
                }
            }));
        _ = await session.RunBackgroundSchedulerIterationAsyncForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            static (_, toolCall, _) => Task.FromResult<IReadOnlyList<ToolOutputDto>>(new[] {
                new ToolOutputDto {
                    CallId = toolCall.CallId,
                    Ok = false,
                    ErrorCode = "remote_probe_failed",
                    Output = """{"ok":false}"""
                }
            }));

        var warnings = session.BuildHelloStartupWarningsForTesting(Task.CompletedTask);
        var handshake = Assert.Single(
            warnings,
            static warning => warning.StartsWith("[startup] capability_handshake", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("background_scheduler_auto_pause_enabled='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_manual_pause_active='false'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_failure_threshold='2'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_failure_pause_seconds='120'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_paused='true'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_pause_reason='consecutive_failure_threshold_reached:requeued_after_tool_failure:system_info'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_scheduler_paused_until_utc_ticks='", handshake, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_RuntimeCapabilitySnapshotExposesStartupSchedulerPauseState() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableBackgroundSchedulerDaemon = true;
        options.BackgroundSchedulerStartPaused = true;
        options.BackgroundSchedulerStartupPauseSeconds = 300;
        var session = new ChatServiceSession(options, Stream.Null);

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.NotNull(snapshot.BackgroundScheduler);
        Assert.True(snapshot.BackgroundScheduler!.Paused);
        Assert.True(snapshot.BackgroundScheduler.ManualPauseActive);
        Assert.Equal("manual_pause:300s:startup", snapshot.BackgroundScheduler.PauseReason);
        Assert.True(snapshot.BackgroundScheduler.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_RuntimeCapabilitySnapshotExposesMaintenanceWindowSchedulerPauseState() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableBackgroundSchedulerDaemon = true;
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440");
        var session = new ChatServiceSession(options, Stream.Null);

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.NotNull(snapshot.BackgroundScheduler);
        Assert.True(snapshot.BackgroundScheduler!.Paused);
        Assert.False(snapshot.BackgroundScheduler.ManualPauseActive);
        Assert.True(snapshot.BackgroundScheduler.ScheduledPauseActive);
        Assert.Equal(new[] { "daily@00:00/1440" }, snapshot.BackgroundScheduler.MaintenanceWindowSpecs);
        Assert.Equal(new[] { "daily@00:00/1440" }, snapshot.BackgroundScheduler.ActiveMaintenanceWindowSpecs);
        Assert.Equal("maintenance_window:daily@00:00/1440", snapshot.BackgroundScheduler.PauseReason);
    }

    [Fact]
    public void RuntimeCapabilityHandshake_RuntimeCapabilitySnapshotExposesActiveScopedMaintenanceWindows() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableBackgroundSchedulerDaemon = true;
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440;pack=system");
        var session = new ChatServiceSession(options, Stream.Null);

        var snapshot = session.BuildRuntimeCapabilitySnapshotForTesting();

        Assert.NotNull(snapshot.BackgroundScheduler);
        Assert.False(snapshot.BackgroundScheduler!.Paused);
        Assert.False(snapshot.BackgroundScheduler.ScheduledPauseActive);
        Assert.Equal(new[] { "daily@00:00/1440;pack=system" }, snapshot.BackgroundScheduler.MaintenanceWindowSpecs);
        Assert.Equal(new[] { "daily@00:00/1440;pack=system" }, snapshot.BackgroundScheduler.ActiveMaintenanceWindowSpecs);
    }

    private static string? TryReadInstructionLine(string input, string prefix) {
        var normalizedPrefix = (prefix ?? string.Empty).Trim();
        if (normalizedPrefix.Length == 0) {
            return null;
        }

        var lines = (input ?? string.Empty).Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (line.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)) {
                return line;
            }
        }

        return null;
    }

    private static int CountCsvItemsFromInstructionLine(string line, string prefix) {
        var normalizedLine = (line ?? string.Empty).Trim();
        var normalizedPrefix = (prefix ?? string.Empty).Trim();
        if (normalizedLine.Length == 0 || normalizedPrefix.Length == 0) {
            return 0;
        }

        var value = normalizedLine.Substring(normalizedPrefix.Length).Trim();
        if (value.Length == 0) {
            return 0;
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static ToolOrchestrationCatalog CreateCapabilityHandshakeOrchestrationCatalog() {
        return ToolOrchestrationCatalog.Build(
            CreateCapabilityHandshakeAutonomyDefinitions(),
            new IToolPack[] { new SyntheticCapabilityHandshakePack() });
    }

    private static ToolDefinition[] CreateCapabilityHandshakeAutonomyDefinitions() {
        return new[] {
            new ToolDefinition(
                "ad_scope_discovery",
                "Discover AD scope.",
                ToolSchema.Object(("domain_name", ToolSchema.String("Domain."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "scope_hosts"
                },
                execution: new ToolExecutionContract {
                    IsExecutionAware = true,
                    ExecutionScope = ToolExecutionScopes.LocalOnly,
                    TargetScopeArguments = new[] { "domain_name" }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Query remote event logs.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                execution: new ToolExecutionContract {
                    IsExecutionAware = true,
                    ExecutionScope = ToolExecutionScopes.LocalOrRemote,
                    TargetScopeArguments = new[] { "machine_name" },
                    RemoteHostArguments = new[] { "machine_name" }
                })
        };
    }

    private sealed class SyntheticCapabilityHandshakePack : IToolPack, IToolPackCatalogProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "active_directory",
            Name = "Active Directory",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "builtin",
            EngineId = "adplayground",
            CapabilityTags = new[] { "directory", "remote_analysis" }
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "ad_scope_discovery",
                    Description = "Discover AD scope.",
                    IsEnvironmentDiscoverTool = true,
                    RepresentativeExamples = new[] {
                        "discover effective AD scope and the reachable domain controllers before choosing deeper directory tools"
                    },
                    Handoff = new ToolPackToolHandoffModel {
                        IsHandoffAware = true,
                        Routes = new[] {
                            new ToolPackToolHandoffRouteModel {
                                TargetPackId = "eventlog",
                                TargetToolName = "eventlog_live_query"
                            },
                            new ToolPackToolHandoffRouteModel {
                                TargetPackId = "system",
                                TargetToolName = "system_info"
                            }
                        }
                    }
                }
            };
        }
    }

    private sealed class SyntheticDeferredAffordanceOverlayPack : IToolPack, IToolPackCatalogProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "deferred-affordance-overlay",
            Name = "Deferred Affordance Overlay",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "builtin"
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "email_smtp_send",
                    RepresentativeExamples = new[] { "send a verification or notification email with SMTP" }
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "testimox_report_snapshot_get",
                    RepresentativeExamples = new[] { "open a stored monitoring HTML report snapshot from an allowed history directory" }
                }
            };
        }
    }

    private sealed class SyntheticDeferredAffordanceEmailCapabilityPack : IToolPack {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "email",
            Name = "Email",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "builtin",
            CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityEmail }
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }
    }

    private sealed class SyntheticDeferredAffordanceReportingCapabilityPack : IToolPack {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "testimox_analytics",
            Name = "TestimoX Analytics",
            Tier = ToolCapabilityTier.ReadOnly,
            SourceKind = "builtin",
            CapabilityTags = new[] { ToolPackCapabilityTags.DeferredCapabilityReporting }
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }
    }
}
