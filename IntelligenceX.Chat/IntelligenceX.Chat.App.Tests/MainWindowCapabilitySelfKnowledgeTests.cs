using System;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for human-facing capability self-knowledge derived from session policy.
/// </summary>
public sealed class MainWindowCapabilitySelfKnowledgeTests {
    /// <summary>
    /// Ensures capability self-knowledge summarizes enabled capability areas without hardcoded pack-family rewrites.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_SummarizesEnabledCapabilities() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(new SessionPolicyDto {
            ReadOnly = true,
            DangerousToolsEnabled = false,
            MaxToolRounds = 24,
            ParallelTools = true,
            AllowMutatingParallelToolCalls = false,
            Packs = new[] {
                new ToolPackInfoDto { Id = "adplayground", Name = "Active Directory", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                new ToolPackInfoDto { Id = "eventlog", Name = "Event Viewer", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                new ToolPackInfoDto { Id = "dnsclientx", Name = "DnsClientX", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            RoutingCatalog = new SessionRoutingCatalogDiagnosticsDto {
                AutonomyReadinessHighlights = new[] {
                    "remote host-targeting is ready for 4 tool(s).",
                    "cross-pack continuation is ready for 2 tool(s)."
                }
            },
            CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                RegisteredTools = 12,
                EnabledPackCount = 2,
                PluginCount = 2,
                EnabledPluginCount = 2,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                FamilyActions = new[] {
                    new SessionRoutingFamilyActionSummaryDto { Family = "ad_domain", ActionId = "act_ad", ToolCount = 5 }
                },
                HealthyTools = new[] { "ad_search", "eventlog_live_query" },
                RemoteReachabilityMode = "remote_capable",
                Autonomy = new SessionCapabilityAutonomySummaryDto {
                    RemoteCapableToolCount = 4,
                    SetupAwareToolCount = 1,
                    HandoffAwareToolCount = 2,
                    RecoveryAwareToolCount = 1,
                    CrossPackHandoffToolCount = 1,
                    RemoteCapablePackIds = new[] { "adplayground", "eventlog" },
                    CrossPackReadyPackIds = new[] { "adplayground" },
                    CrossPackTargetPackIds = new[] { "eventlog", "system" }
                }
            }
        });

        Assert.Contains(lines, line => line.Contains("Active Directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("source of truth", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("enabled areas above", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("live session tools", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Recently healthy tool count", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("remote-capable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Remote-ready capability areas currently include", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Cross-pack follow-up pivots", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("contract-guided setup, handoff, and recovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Routing autonomy right now includes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Concrete examples you can mention", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("few practical examples", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures runtime-introspection mode avoids generic capability-answer tail guidance.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_RuntimeIntrospectionMode_UsesModeSpecificTailGuidance() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "local_only",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            },
            runtimeIntrospectionMode: true);

        Assert.Contains(lines, line => line.Contains("only the live tooling or capability areas", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("runtime capability handshake", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Concrete examples you can mention", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("source of truth", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("invite the user's task", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures the richer generic guidance lines are still present on normal capability answers.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_NonRuntimeMode_KeepsCategoryAndExampleGuidance() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "adplayground", Name = "Active Directory", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "dnsclientx", Name = "DnsClientX", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 2,
                    EnabledPackCount = 2,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "remote_capable",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            },
            runtimeIntrospectionMode: false);

        Assert.Contains(lines, line => line.Contains("source of truth", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Concrete examples you can mention", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("best match the user's request", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("invite the user's task", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures runtime-introspection mode stays informative even when pack metadata has not loaded yet.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_RuntimeIntrospectionMode_AddsSparseMetadataFallback() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false
            },
            runtimeIntrospectionMode: true);

        Assert.Contains(lines, line => line.Contains("still sparse", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("runtime or model facts", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures operational status lines are prioritized ahead of category/example filler so budgeting cannot hide them later.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_PrioritizesToolingAndReachabilityStatus() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "adplayground", Name = "Active Directory", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "eventlog", Name = "Event Viewer", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "dnsclientx", Name = "DnsClientX", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 12,
                    EnabledPackCount = 4,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = false,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "local_only",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            });

        var toolingIndex = FindLineIndex(lines, "Tooling is not currently available");
        var reachabilityIndex = FindLineIndex(lines, "Remote reachability right now is local-only.");
        var exampleIndex = FindLineIndex(lines, "Concrete examples you can mention");

        Assert.True(toolingIndex >= 0);
        Assert.True(reachabilityIndex >= 0);
        Assert.True(exampleIndex >= 0);
        Assert.True(toolingIndex < exampleIndex);
        Assert.True(reachabilityIndex < exampleIndex);
    }

    /// <summary>
    /// Ensures capability self-knowledge stays cautious when session policy/tooling data is not ready yet.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_StaysCautiousWithoutPolicy() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(sessionPolicy: null);

        Assert.Contains(lines, line => line.Contains("still loading", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("invite the task", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability self-knowledge can fall back to tool-list pack and routing autonomy when full session policy has not loaded yet.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_FallsBackToToolCatalogAutonomyWhenPolicyUnavailable() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "eventlog",
                    Name = "Event Viewer",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false,
                    AutonomySummary = new ToolPackAutonomySummaryDto {
                        RemoteCapableTools = 2,
                        SetupAwareTools = 1,
                        HandoffAwareTools = 1,
                        RecoveryAwareTools = 1,
                        CrossPackHandoffTools = 1,
                        CrossPackTargetPacks = new[] { "system" }
                    }
                },
                new ToolPackInfoDto {
                    Id = "system",
                    Name = "System",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false,
                    AutonomySummary = new ToolPackAutonomySummaryDto {
                        RemoteCapableTools = 1
                    }
                }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 3,
                EnabledPackCount = 2,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "remote_capable",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            toolCatalogRoutingCatalog: new SessionRoutingCatalogDiagnosticsDto {
                AutonomyReadinessHighlights = new[] {
                    "remote host-targeting is ready for 3 tool(s).",
                    "cross-pack continuation is ready for 1 tool(s)."
                }
            });

        Assert.Contains(lines, line => line.Contains("Event Viewer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("live session tools", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("remote-capable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Remote-ready capability areas currently include", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Cross-pack follow-up pivots", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("contract-guided setup, handoff, and recovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Routing autonomy right now includes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("still loading", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures disabled packs do not contribute cross-pack fallback hints to capability self-knowledge text.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_IgnoresDisabledCrossPackFallbackSources() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "eventlog",
                    Name = "Event Viewer",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = false,
                    IsDangerous = false,
                    AutonomySummary = new ToolPackAutonomySummaryDto {
                        CrossPackHandoffTools = 1,
                        CrossPackTargetPacks = new[] { "system" }
                    }
                },
                new ToolPackInfoDto {
                    Id = "system",
                    Name = "System",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            },
            toolCatalogRoutingCatalog: null,
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            });

        Assert.DoesNotContain(lines, line => line.Contains("Cross-pack follow-up pivots", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability self-knowledge describes mixed local-only and remote-ready execution locality from the live tool catalog.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_DescribesMixedExecutionLocalityFromToolCatalog() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "system",
                    Name = "System",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                },
                new ToolPackInfoDto {
                    Id = "eventlog",
                    Name = "Event Viewer",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 2,
                EnabledPackCount = 2,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "remote_capable",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            toolCatalogExecutionSummary: new ToolCatalogExecutionSummary {
                ExecutionAwareToolCount = 2,
                LocalOnlyToolCount = 1,
                LocalOrRemoteToolCount = 1,
                LocalOnlyPackIds = new[] { "system" },
                RemoteCapablePackIds = new[] { "eventlog" }
            });

        Assert.Contains(lines, line => line.Contains("Execution locality is mixed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("local-only tools currently include System", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("explicit remote-ready tools currently include Event Viewer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("execution-aware tools", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability answers can use contract-backed live examples from representative tools,
    /// instead of relying only on pack-level autonomy summaries.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_UsesRepresentativeToolContracts_ForConcreteExamples() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto { Id = "directory_ops", Name = "Directory Ops", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                new ToolPackInfoDto { Id = "ops_events", Name = "Ops Events", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                new ToolPackInfoDto { Id = "ops_inventory", Name = "Ops Inventory", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            toolCatalogRoutingCatalog: new SessionRoutingCatalogDiagnosticsDto {
                AutonomyReadinessHighlights = new[] { "remote host-targeting is ready for representative tools." }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 3,
                EnabledPackCount = 3,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "remote_capable",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            toolCatalogTools: new[] {
                new ToolDefinitionDto {
                    Name = "ad_environment_discover",
                    Description = "Discover Active Directory environment context.",
                    PackId = "directory_ops",
                    PackName = "Directory Ops",
                    IsEnvironmentDiscoverTool = true,
                    SupportsTargetScoping = true,
                    TargetScopeArguments = new[] { "domain_controller", "search_base_dn" }
                },
                new ToolDefinitionDto {
                    Name = "eventlog_live_query",
                    Description = "Query Windows event logs.",
                    PackId = "ops_events",
                    PackName = "Ops Events",
                    RoutingEntity = "event",
                    ExecutionScope = "local_or_remote",
                    SupportsRemoteHostTargeting = true,
                    RemoteHostArguments = new[] { "machine_name" }
                },
                new ToolDefinitionDto {
                    Name = "system_metrics_summary",
                    Description = "Collect system metrics.",
                    PackId = "ops_inventory",
                    PackName = "Ops Inventory",
                    RoutingScope = "host",
                    RoutingEntity = "host",
                    ExecutionScope = "local_or_remote",
                    SupportsRemoteHostTargeting = true,
                    RemoteHostArguments = new[] { "computer_name" },
                    IsHandoffAware = true,
                    HandoffTargetPackIds = new[] { "ops_events", "ops_inventory" }
                }
            });

        Assert.Contains(lines, line => line.Contains("domain controller or base DN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("event logs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("CPU, memory, and disk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("pivot findings into Ops Events, Ops Inventory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("live tool contracts", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability self-knowledge surfaces deferred/reporting-style affordances from the runtime snapshot
    /// instead of requiring chat-core family-specific wording.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_SurfacesDeferredWorkAffordances_FromCapabilitySnapshot() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "email", Name = "Email", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "testimox_analytics", Name = "Reporting", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 2,
                    EnabledPackCount = 2,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "remote_capable",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    DeferredWorkAffordances = new[] {
                        new SessionCapabilityDeferredWorkAffordanceDto {
                            CapabilityId = "background_followup",
                            DisplayName = "Background Follow-up",
                            Summary = "Runtime scheduler can continue deferred work.",
                            AvailabilityMode = "runtime_scheduler",
                            SupportsBackgroundExecution = true,
                            PackIds = Array.Empty<string>(),
                            RoutingFamilies = Array.Empty<string>(),
                            RepresentativeExamples = Array.Empty<string>()
                        },
                        new SessionCapabilityDeferredWorkAffordanceDto {
                            CapabilityId = "email",
                            DisplayName = "Email",
                            Summary = "Compose or send email follow-up.",
                            AvailabilityMode = "pack_declared",
                            SupportsBackgroundExecution = true,
                            PackIds = new[] { "email" },
                            RoutingFamilies = new[] { "notification_delivery" },
                            RepresentativeExamples = new[] { "send an email summary after the run" }
                        },
                        new SessionCapabilityDeferredWorkAffordanceDto {
                            CapabilityId = "reporting",
                            DisplayName = "Reporting",
                            Summary = "Generate reporting artifacts.",
                            AvailabilityMode = "pack_declared",
                            SupportsBackgroundExecution = false,
                            PackIds = new[] { "testimox_analytics" },
                            RoutingFamilies = new[] { "monitoring_artifacts" },
                            RepresentativeExamples = new[] { "publish a monitoring report snapshot" }
                        }
                    }
                }
            });

        Assert.Contains(lines, line => line.Contains("Deferred follow-up work currently registered includes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Background Follow-up", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Reporting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("background follow-up", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("send an email summary after the run", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("publish a monitoring report snapshot", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability self-knowledge can describe registered plugin sources without inferring them from pack wording alone.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_SummarizesRegisteredPluginSources_FromSessionPolicy() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "eventlog", Name = "Event Viewer", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false },
                    new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                Plugins = new[] {
                    new PluginInfoDto {
                        Id = "ops_bundle",
                        Name = "Ops Bundle",
                        Origin = "plugin_folder",
                        SourceKind = ToolPackSourceKind.ClosedSource,
                        DefaultEnabled = true,
                        Enabled = true,
                        IsDangerous = false,
                        PackIds = new[] { "eventlog", "system" }
                    }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 2,
                    EnabledPackCount = 2,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            });

        Assert.Contains(lines, line => line.Contains("Registered tool sources currently active include", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Ops Bundle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("plugin folder", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Event Viewer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("System", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures runtime-introspection guidance keeps deferred affordances concise
    /// instead of spilling into broader follow-up example text.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_RuntimeMode_SummarizesDeferredWorkAffordances_WithoutExamples() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "email", Name = "Email", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "local_only",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    DeferredWorkAffordances = new[] {
                        new SessionCapabilityDeferredWorkAffordanceDto {
                            CapabilityId = "email",
                            DisplayName = "Email",
                            Summary = "Compose or send email follow-up.",
                            AvailabilityMode = "pack_declared",
                            SupportsBackgroundExecution = true,
                            PackIds = new[] { "email" },
                            RoutingFamilies = new[] { "notification_delivery" },
                            RepresentativeExamples = new[] { "send an email summary after the run" }
                        }
                    }
                }
            },
            runtimeIntrospectionMode: true);

        Assert.Contains(lines, line => line.Contains("Deferred follow-up affordances currently registered", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Deferred follow-up examples you can mention", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures runtime-introspection answers stay concise and do not pick up broader tool-example guidance.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_RuntimeMode_DoesNotAddRepresentativeToolExamples() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            toolCatalogPlugins: new[] {
                new PluginInfoDto {
                    Id = "ops_bundle",
                    Name = "Ops Bundle",
                    Origin = "folder",
                    SourceKind = ToolPackSourceKind.ClosedSource,
                    DefaultEnabled = true,
                    Enabled = true,
                    IsDangerous = false,
                    PackIds = new[] { "system" }
                }
            },
            toolCatalogRoutingCatalog: null,
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "local_only",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            toolCatalogTools: new[] {
                new ToolDefinitionDto {
                    Name = "system_metrics_summary",
                    Description = "Collect system metrics.",
                    PackId = "system",
                    PackName = "System",
                    ExecutionScope = "local_or_remote",
                    SupportsRemoteHostTargeting = true
                }
            },
            runtimeIntrospectionMode: true);

        Assert.DoesNotContain(lines, line => line.Contains("Concrete examples you can mention", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("runtime capability handshake", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Registered tool sources currently visible include", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Ops Bundle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("plugin folder", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures runtime introspection can still report disabled registered plugin sources for support/provenance answers.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_RuntimeMode_IncludesDisabledPluginSources() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "system",
                    Name = "System",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            },
            toolCatalogPlugins: new[] {
                new PluginInfoDto {
                    Id = "ops_bundle",
                    Name = "Ops Bundle",
                    Origin = "plugin_folder",
                    SourceKind = ToolPackSourceKind.ClosedSource,
                    DefaultEnabled = true,
                    Enabled = false,
                    DisabledReason = "disabled for maintenance",
                    IsDangerous = false,
                    PackIds = new[] { "system" }
                }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 1,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "local_only",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            runtimeIntrospectionMode: true);

        Assert.Contains(lines, line => line.Contains("Registered tool sources currently visible include", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Ops Bundle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("plugin folder", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("(disabled)", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures lexical-fallback runtime introspection narrows broader provenance/autonomy guidance until the user asks more explicitly.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_RuntimeMode_LexicalFallbackSuppressesBroaderProvenanceGuidance() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            toolCatalogPlugins: new[] {
                new PluginInfoDto {
                    Id = "ops_bundle",
                    Name = "Ops Bundle",
                    Origin = "plugin_folder",
                    SourceKind = ToolPackSourceKind.ClosedSource,
                    DefaultEnabled = true,
                    Enabled = true,
                    IsDangerous = false,
                    PackIds = new[] { "system" }
                }
            },
            toolCatalogRoutingCatalog: new SessionRoutingCatalogDiagnosticsDto {
                AutonomyReadinessHighlights = new[] { "remote host-targeting is ready for representative tools." }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 1,
                EnabledPluginCount = 1,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "local_only",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                DeferredWorkAffordances = new[] {
                    new SessionCapabilityDeferredWorkAffordanceDto {
                        CapabilityId = "email",
                        DisplayName = "Email",
                        Summary = "Compose or send email follow-up.",
                        AvailabilityMode = "pack_declared",
                        SupportsBackgroundExecution = true,
                        PackIds = new[] { "system" },
                        RoutingFamilies = Array.Empty<string>(),
                        RepresentativeExamples = new[] { "send an email summary after the run" }
                    }
                }
            },
            toolCatalogTools: new[] {
                new ToolDefinitionDto {
                    Name = "system_metrics_summary",
                    Description = "Collect system metrics.",
                    PackId = "system",
                    PackName = "System",
                    ExecutionScope = "local_or_remote",
                    SupportsRemoteHostTargeting = true
                }
            },
            runtimeIntrospectionMode: true,
            runtimeSelfReportDetectionSource: RuntimeSelfReportDetectionSource.LexicalFallback);

        Assert.Contains(lines, line => line.Contains("confirmed enabled areas", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Registered tool sources currently visible include", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Deferred follow-up affordances currently registered", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("Routing autonomy right now includes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("execution-aware tools", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures plugin/source guidance can come from capability-snapshot tooling provenance even when legacy plugin arrays are empty.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_PrefersCapabilitySnapshotToolingSnapshotForPluginGuidance() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                        Source = "service_runtime",
                        Packs = new[] {
                            new ToolPackInfoDto {
                                Id = "system",
                                Name = "System",
                                Tier = CapabilityTier.ReadOnly,
                                Enabled = true,
                                IsDangerous = false
                            }
                        },
                        Plugins = new[] {
                            new PluginInfoDto {
                                Id = "ops_bundle",
                                Name = "Ops Bundle",
                                Origin = "plugin_folder",
                                SourceKind = ToolPackSourceKind.ClosedSource,
                                DefaultEnabled = true,
                                Enabled = true,
                                IsDangerous = false,
                                PackIds = new[] { "system" }
                            }
                        }
                    }
                }
            });

        Assert.Contains(lines, line => line.Contains("System", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Ops Bundle", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("plugin folder", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability self-knowledge uses pack-owned representative examples when tool metadata publishes them directly.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_PrefersPackOwnedRepresentativeExamples_WhenPresent() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto { Id = "customx", Name = "CustomX", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            toolCatalogTools: new[] {
                new ToolDefinitionDto {
                    Name = "custom_probe",
                    Description = "Probe custom runtime state.",
                    PackId = "customx",
                    PackName = "CustomX",
                    RepresentativeExamples = new[] {
                        "inspect the custom endpoint state through pack-owned metadata"
                    }
                }
            });

        Assert.Contains(lines, line => line.Contains("inspect the custom endpoint state through pack-owned metadata", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("event logs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("CPU, memory, and disk", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures capability self-knowledge warns when the enabled tool catalog is entirely local-only.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_WarnsWhenEnabledCatalogIsLocalOnly() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "system",
                    Name = "System",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 1,
                RemoteReachabilityMode = "local_only",
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            },
            toolCatalogExecutionSummary: new ToolCatalogExecutionSummary {
                ExecutionAwareToolCount = 1,
                LocalOnlyToolCount = 1,
                LocalOnlyPackIds = new[] { "system" }
            });

        Assert.Contains(lines, line => line.Contains("currently local-only", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("local-only tools currently include System", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("explicit remote-ready tools", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures alias-based pack identifiers still resolve to the live pack display name in capability self-knowledge.
    /// </summary>
    [Fact]
    public void BuildCapabilitySelfKnowledgeLines_ResolvesRuntimePackAliasIds_ToPackDisplayName() {
        var lines = MainWindow.BuildCapabilitySelfKnowledgeLines(
            sessionPolicy: null,
            toolCatalogPacks: new[] {
                new ToolPackInfoDto {
                    Id = "ops_inventory",
                    Name = "Ops Inventory",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false,
                    Aliases = new[] { "serverops" }
                }
            },
            toolCatalogCapabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 2,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                Autonomy = new SessionCapabilityAutonomySummaryDto {
                    RemoteCapableToolCount = 1,
                    RemoteCapablePackIds = new[] { "serverops" }
                }
            });

        Assert.Contains(lines, line => line.Contains("Ops Inventory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, line => line.Contains("serverops", StringComparison.OrdinalIgnoreCase));
    }

    private static int FindLineIndex(IReadOnlyList<string> lines, string expectedFragment) {
        for (var i = 0; i < lines.Count; i++) {
            if (lines[i].Contains(expectedFragment, StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }
}
