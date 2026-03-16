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
        Assert.Contains("enabled_packs: active_directory, eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugins: active_directory, eventlog", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_engines: adplayground, eventviewerx", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_capability_tags: directory, remote_analysis, event_logs, evtx", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("routing_families: ad_domain, public_domain", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count: 2", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills: ad_domain.scope_hosts, public_domain.query_whois", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("healthy_tools:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_replication_summary", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog_live_query", instructionsText, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(2, snapshot.RoutingFamilies.Length);
        Assert.Equal(2, snapshot.FamilyActions.Length);
        Assert.Equal("ad_domain.scope_hosts", snapshot.Skills[0]);
        Assert.Equal("ad_replication_summary", Assert.Single(snapshot.HealthyTools));
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
    public void RuntimeCapabilityHandshake_ReportsZeroToolingWhenNoPacksLoaded() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var instructions = session.BuildTurnInstructionsWithRuntimeIdentityForTesting("gpt-5");
        var instructionsText = Assert.IsType<string>(instructions);

        Assert.Contains("ix:runtime-identity:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:capability-snapshot:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:skills:v1", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered_tools: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugin_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugin_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skill_count: 0", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enabled_packs:", instructionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("enabled_plugins:", instructionsText, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("enabled_packs='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_plugins='active_directory'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_pack_engines='adplayground'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled_capability_tags='directory,remote_analysis'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("routing_families='ad_domain,public_domain'", handshake, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills='ad_domain.scope_hosts,public_domain.query_whois'", handshake, StringComparison.OrdinalIgnoreCase);
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
}
