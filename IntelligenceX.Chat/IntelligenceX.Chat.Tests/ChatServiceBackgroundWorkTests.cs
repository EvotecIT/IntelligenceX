using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceBackgroundWorkTests {
    [Fact]
    public void ResolveThreadBackgroundWorkSnapshot_ClassifiesReadyAndQueuedItemsFromPendingActionsAndEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work";

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
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ldap",
                    Ok = true,
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "LDAP diagnostics completed."
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.QueuedCount);
        Assert.Equal(1, snapshot.PendingReadOnlyCount);
        Assert.Equal(1, snapshot.PendingUnknownCount);
        Assert.Contains("ad_ldap_diagnostics", snapshot.RecentEvidenceTools, StringComparer.OrdinalIgnoreCase);

        var readyItem = Assert.Single(snapshot.Items, static item => string.Equals(item.Id, "pending_action:verify_ldaps", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", readyItem.State);
        Assert.Equal("pending_action", readyItem.Kind);
        Assert.Contains("ad_ldap_diagnostics", readyItem.EvidenceToolNames, StringComparer.OrdinalIgnoreCase);

        var queuedItem = Assert.Single(snapshot.Items, static item => string.Equals(item.Id, "pending_action:handoff_note", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queued", queuedItem.State);
        Assert.Equal("pending_action", queuedItem.Kind);
        Assert.Empty(queuedItem.EvidenceToolNames);
    }

    [Fact]
    public void BackgroundWorkStatusMessages_StayStableAndIncludeEvidenceSummary() {
        var queued = ChatServiceSession.BuildBackgroundWorkQueuedStatusMessageForTesting(queuedCount: 2);
        var ready = ChatServiceSession.BuildBackgroundWorkReadyStatusMessageForTesting(
            readyCount: 1,
            recentEvidenceTools: new[] { "ad_ldap_diagnostics", "system_certificate_posture" });
        var running = ChatServiceSession.BuildBackgroundWorkRunningStatusMessageForTesting(runningCount: 2);
        var completed = ChatServiceSession.BuildBackgroundWorkCompletedStatusMessageForTesting(completedCount: 1);

        Assert.Equal("Queued 2 safe follow-up items for background preparation.", queued);
        Assert.Equal(
            "Prepared 1 read-only follow-up item from recent evidence. Evidence: ad_ldap_diagnostics, system_certificate_posture.",
            ready);
        Assert.Equal("Background preparation started for 2 follow-up items.", running);
        Assert.Equal("Background follow-up item completed.", completed);
    }

    [Fact]
    public void BackgroundWorkStatusMessages_ExposeTaggedFollowUpPriorityFocus() {
        var taggedItem = new ChatServiceSession.ThreadBackgroundWorkItem(
            Id: "tool_handoff:verify_bob",
            Title: "Verify Bob",
            Request: "verify bob after disable",
            State: "ready",
            EvidenceToolNames: new[] { "ad_user_lifecycle" },
            Kind: "tool_handoff",
            Mutability: "read_only",
            SourceToolName: "ad_user_lifecycle",
            SourceCallId: "call-ad-write",
            TargetPackId: "active_directory",
            TargetToolName: "ad_object_get",
            FollowUpKind: ToolHandoffFollowUpKinds.Verification,
            FollowUpPriority: ToolHandoffFollowUpPriorities.Critical,
            PreparedArgumentsJson: """{"identity":"CN=bob,OU=Users,DC=contoso,DC=com"}""",
            ResultReference: string.Empty,
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: DateTime.UtcNow.Ticks,
            UpdatedUtcTicks: DateTime.UtcNow.Ticks);

        var ready = ChatServiceSession.BuildBackgroundWorkReadyStatusMessageForTesting(
            readyCount: 1,
            recentEvidenceTools: new[] { "ad_user_lifecycle" },
            items: new[] { taggedItem });
        var running = ChatServiceSession.BuildBackgroundWorkRunningStatusMessageForTesting(1, new[] { taggedItem });
        var completed = ChatServiceSession.BuildBackgroundWorkCompletedStatusMessageForTesting(1, new[] { taggedItem });

        Assert.Equal(
            "Prepared 1 read-only follow-up item from recent evidence. Evidence: ad_user_lifecycle. Priority: critical verification.",
            ready);
        Assert.Equal("Background follow-up preparation started. Focus: critical verification.", running);
        Assert.Equal("Background follow-up item completed. Focus: critical verification.", completed);
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_AggregatesReadyAndRunningWorkAcrossThreads() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string readyThreadId = "thread-background-scheduler-ready";
        const string runningThreadId = "thread-background-scheduler-running";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            readyThreadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ready",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-ready.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ready",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        session.RememberToolHandoffBackgroundWorkForTesting(
            runningThreadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-running",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-running.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-running",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            runningThreadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out _,
            out _,
            out _,
            out _));

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        Assert.True(summary.SupportsPersistentQueue);
        Assert.True(summary.SupportsReadOnlyAutoReplay);
        Assert.True(summary.SupportsCrossThreadScheduling);
        Assert.Equal(2, summary.TrackedThreadCount);
        Assert.Equal(1, summary.ReadyThreadCount);
        Assert.Equal(1, summary.RunningThreadCount);
        Assert.Equal(1, summary.ReadyItemCount);
        Assert.Equal(1, summary.RunningItemCount);
        Assert.Contains(readyThreadId, summary.ReadyThreadIds, StringComparer.Ordinal);
        Assert.Contains(runningThreadId, summary.RunningThreadIds, StringComparer.Ordinal);
        Assert.Equal(2, summary.ThreadSummaries.Length);
        Assert.Contains(summary.ThreadSummaries, static thread => string.Equals(thread.ThreadId, readyThreadId, StringComparison.Ordinal));
        Assert.Contains(summary.ThreadSummaries, static thread => string.Equals(thread.ThreadId, runningThreadId, StringComparison.Ordinal));
        Assert.True(summary.LastSchedulerTickUtcTicks > 0);
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_CountsReadyThreadsBeyondReturnedIdSample() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        for (var i = 0; i < 8; i++) {
            var threadId = $"thread-background-scheduler-ready-{i:00}";
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = $"call-ready-{i:00}",
                        Name = "remote_disk_inventory",
                        ArgumentsJson = $$"""{"computer_name":"srv-ready-{{i:00}}.contoso.com"}"""
                    }
                },
                new[] {
                    new ToolOutputDto {
                        CallId = $"call-ready-{i:00}",
                        Ok = true,
                        Output = """{"ok":true}"""
                    }
                });
        }

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        Assert.Equal(8, summary.TrackedThreadCount);
        Assert.Equal(8, summary.ReadyThreadCount);
        Assert.Equal(8, summary.ReadyItemCount);
        Assert.Equal(6, summary.ReadyThreadIds.Length);
        Assert.Equal(4, summary.ThreadSummaries.Length);
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_PicksHighestPriorityReadyItemAcrossThreads() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string lowPriorityThreadId = "thread-background-scheduler-low";
        const string highPriorityThreadId = "thread-background-scheduler-high";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            CreateDefinition(
                name: "ad_user_lifecycle",
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
            new ToolDefinition("system_info", "system info", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_object_get", "ad object get", ToolSchema.Object().NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            lowPriorityThreadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-low",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-low.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-low",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        session.RememberToolHandoffBackgroundWorkForTesting(
            highPriorityThreadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-high",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"bob","operation":"disable"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-high",
                    Ok = true,
                    Output = """{"ok":true,"distinguished_name":"CN=bob,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        Assert.True(session.TryBuildScheduledBackgroundWorkToolCallForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var scheduledThreadId,
            out _,
            out var toolName,
            out var argumentsJson,
            out var reason));

        Assert.Equal("background_scheduler_claimed_ready_work", reason);
        Assert.Equal(highPriorityThreadId, scheduledThreadId);
        Assert.Equal("ad_object_get", toolName);
        Assert.Contains("\"identity\":\"CN=bob,OU=Users,DC=contoso,DC=com\"", argumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_RespectsBackgroundSchedulerPackAllowlist() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerAllowedPackIds.Add("system");
        var session = new ChatServiceSession(options, Stream.Null);
        const string systemThreadId = "thread-background-scheduler-allowed-system";
        const string activeDirectoryThreadId = "thread-background-scheduler-blocked-ad";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            CreateDefinition(
                name: "ad_user_lifecycle",
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
            new ToolDefinition("system_info", "system info", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_object_get", "ad object get", ToolSchema.Object().NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            systemThreadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-system",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-system.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-system",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        session.RememberToolHandoffBackgroundWorkForTesting(
            activeDirectoryThreadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-ad",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"bob","operation":"disable"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-ad",
                    Ok = true,
                    Output = """{"ok":true,"distinguished_name":"CN=bob,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        Assert.True(session.TryBuildScheduledBackgroundWorkToolCallForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var scheduledThreadId,
            out _,
            out var toolName,
            out var argumentsJson,
            out var reason));

        Assert.Equal("background_scheduler_claimed_ready_work", reason);
        Assert.Equal(systemThreadId, scheduledThreadId);
        Assert.Equal("system_info", toolName);
        Assert.Contains("\"computer_name\":\"srv-system.contoso.com\"", argumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_RespectsPackScopedMaintenanceWindows() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440;pack=active_directory");
        var session = new ChatServiceSession(options, Stream.Null);
        const string systemThreadId = "thread-background-scheduler-maint-system";
        const string activeDirectoryThreadId = "thread-background-scheduler-maint-ad";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            CreateDefinition(
                name: "ad_user_lifecycle",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_object_get",
                            TargetRole = ToolRoutingTaxonomy.RoleResolver,
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
            new ToolDefinition("system_info", "system info", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_object_get", "ad object get", ToolSchema.Object().NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            systemThreadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-system-maint",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-system.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-system-maint",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        session.RememberToolHandoffBackgroundWorkForTesting(
            activeDirectoryThreadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-ad-maint",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"bob","operation":"disable"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-ad-maint",
                    Ok = true,
                    Output = """{"ok":true,"distinguished_name":"CN=bob,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        Assert.True(session.TryBuildScheduledBackgroundWorkToolCallForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var scheduledThreadId,
            out _,
            out var toolName,
            out _,
            out var reason));

        Assert.Equal("background_scheduler_claimed_ready_work", reason);
        Assert.Equal(systemThreadId, scheduledThreadId);
        Assert.Equal("system_info", toolName);
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_RespectsBackgroundSchedulerThreadAllowlist() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerAllowedThreadIds.Add("thread-background-scheduler-allowed-system");
        var session = new ChatServiceSession(options, Stream.Null);
        const string systemThreadId = "thread-background-scheduler-allowed-system";
        const string blockedThreadId = "thread-background-scheduler-blocked";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        foreach (var threadId in new[] { blockedThreadId, systemThreadId }) {
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-" + threadId,
                        Name = "remote_disk_inventory",
                        ArgumentsJson = """{"computer_name":"srv-thread.contoso.com"}"""
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

        Assert.True(session.TryBuildScheduledBackgroundWorkToolCallForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var scheduledThreadId,
            out _,
            out var toolName,
            out _,
            out var reason));

        Assert.Equal("background_scheduler_claimed_ready_work", reason);
        Assert.Equal(systemThreadId, scheduledThreadId);
        Assert.Equal("system_info", toolName);
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_RespectsThreadScopedMaintenanceWindows() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440;thread=thread-background-scheduler-blocked-by-maintenance");
        var session = new ChatServiceSession(options, Stream.Null);
        const string allowedThreadId = "thread-background-scheduler-allowed-during-maintenance";
        const string blockedThreadId = "thread-background-scheduler-blocked-by-maintenance";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        foreach (var threadId in new[] { blockedThreadId, allowedThreadId }) {
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-" + threadId,
                        Name = "remote_disk_inventory",
                        ArgumentsJson = """{"computer_name":"srv-thread.contoso.com"}"""
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

        Assert.True(session.TryBuildScheduledBackgroundWorkToolCallForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var scheduledThreadId,
            out _,
            out var toolName,
            out _,
            out var reason));

        Assert.Equal("background_scheduler_claimed_ready_work", reason);
        Assert.Equal(allowedThreadId, scheduledThreadId);
        Assert.Equal("system_info", toolName);
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_RespectsBackgroundSchedulerThreadBlocklist() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerBlockedThreadIds.Add("thread-background-scheduler-blocked");
        var session = new ChatServiceSession(options, Stream.Null);
        const string allowedThreadId = "thread-background-scheduler-allowed";
        const string blockedThreadId = "thread-background-scheduler-blocked";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        foreach (var threadId in new[] { blockedThreadId, allowedThreadId }) {
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-" + threadId,
                        Name = "remote_disk_inventory",
                        ArgumentsJson = """{"computer_name":"srv-thread.contoso.com"}"""
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

        Assert.True(session.TryBuildScheduledBackgroundWorkToolCallForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var scheduledThreadId,
            out _,
            out var toolName,
            out _,
            out var reason));

        Assert.Equal("background_scheduler_claimed_ready_work", reason);
        Assert.Equal(allowedThreadId, scheduledThreadId);
        Assert.Equal("system_info", toolName);
    }

    [Fact]
    public void TryReleaseScheduledBackgroundWorkReplayCandidateForTesting_ReturnsClaimedWorkToReadyState() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-scheduler-release";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-claim",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-release.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-claim",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildScheduledBackgroundWorkToolCallForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var scheduledThreadId,
            out var itemId,
            out _,
            out _,
            out _));
        Assert.Equal(threadId, scheduledThreadId);

        var runningSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.Equal(0, runningSummary.ReadyItemCount);
        Assert.Equal(1, runningSummary.RunningItemCount);

        Assert.True(session.TryReleaseScheduledBackgroundWorkReplayCandidateForTesting(threadId, itemId));

        var releasedSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.Equal(1, releasedSummary.ReadyItemCount);
        Assert.Equal(0, releasedSummary.RunningItemCount);
        Assert.Contains(threadId, releasedSummary.ReadyThreadIds, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RunBackgroundSchedulerIterationAsyncForTesting_CompletesClaimedReadyWorkOnSuccessfulOutput() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-scheduler-iteration-success";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            new ToolDefinition(
                "system_info",
                "system info",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-success",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-success.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-success",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var result = await session.RunBackgroundSchedulerIterationAsyncForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            (scheduledThreadId, toolCall, _) => Task.FromResult<IReadOnlyList<ToolOutputDto>>(new[] {
                new ToolOutputDto {
                    CallId = toolCall.CallId,
                    Ok = true,
                    Output = """{"computer_name":"srv-success.contoso.com","ok":true}"""
                }
            }));

        Assert.Equal(ChatServiceSession.BackgroundSchedulerIterationOutcomeKind.Completed, result.Outcome);
        Assert.Equal(threadId, result.ThreadId);
        Assert.Equal("system_info", result.ToolName);
        Assert.True(result.WorkCompleted);

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal("completed", item.State);

        var schedulerSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.Equal(1, schedulerSummary.CompletedExecutionCount);
        Assert.Equal(0, schedulerSummary.RequeuedExecutionCount);
        Assert.Equal(0, schedulerSummary.ReleasedExecutionCount);
        Assert.Equal(0, schedulerSummary.ConsecutiveFailureCount);
        Assert.Equal("completed", schedulerSummary.LastOutcome);
        Assert.True(schedulerSummary.LastSuccessUtcTicks > 0);
        var recentActivity = Assert.Single(schedulerSummary.RecentActivity);
        Assert.Equal("completed", recentActivity.Outcome);
        Assert.Equal(threadId, recentActivity.ThreadId);
        Assert.Equal("system_info", recentActivity.ToolName);
        var threadSummary = Assert.Single(schedulerSummary.ThreadSummaries);
        Assert.Equal(threadId, threadSummary.ThreadId);
        Assert.Equal(1, threadSummary.CompletedItemCount);
    }

    [Fact]
    public async Task RunBackgroundSchedulerIterationAsyncForTesting_RequeuesClaimedWorkOnToolFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-scheduler-iteration-failure";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            new ToolDefinition(
                "system_info",
                "system info",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-failure",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-failure.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-failure",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var result = await session.RunBackgroundSchedulerIterationAsyncForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            (scheduledThreadId, toolCall, _) => Task.FromResult<IReadOnlyList<ToolOutputDto>>(new[] {
                new ToolOutputDto {
                    CallId = toolCall.CallId,
                    Ok = false,
                    ErrorCode = "remote_probe_failed",
                    Error = "Remote probe failed.",
                    Output = """{"ok":false}"""
                }
            }));

        Assert.Equal(ChatServiceSession.BackgroundSchedulerIterationOutcomeKind.RequeuedAfterToolFailure, result.Outcome);
        Assert.Equal("remote_probe_failed", result.FailureDetail);
        Assert.True(result.WorkRequeued);

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal("ready", item.State);

        var schedulerSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.Equal(0, schedulerSummary.CompletedExecutionCount);
        Assert.Equal(1, schedulerSummary.RequeuedExecutionCount);
        Assert.Equal(0, schedulerSummary.ReleasedExecutionCount);
        Assert.Equal(1, schedulerSummary.ConsecutiveFailureCount);
        Assert.Equal("requeued_after_tool_failure", schedulerSummary.LastOutcome);
        Assert.True(schedulerSummary.LastFailureUtcTicks > 0);
        var recentActivity = Assert.Single(schedulerSummary.RecentActivity);
        Assert.Equal("requeued_after_tool_failure", recentActivity.Outcome);
        Assert.Equal("system_info", recentActivity.ToolName);
        Assert.Contains("remote_probe_failed", recentActivity.FailureDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunBackgroundSchedulerIterationAsyncForTesting_ReleasesClaimedWorkOnExecutorException() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-scheduler-iteration-exception";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            new ToolDefinition(
                "system_info",
                "system info",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-exception",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-exception.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-exception",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var result = await session.RunBackgroundSchedulerIterationAsyncForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            static (_, _, _) => throw new InvalidOperationException("Executor exploded."));

        Assert.Equal(ChatServiceSession.BackgroundSchedulerIterationOutcomeKind.ReleasedAfterException, result.Outcome);
        Assert.True(result.ReleasedLease);
        Assert.Equal("Executor exploded.", result.FailureDetail);

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal("ready", item.State);

        var schedulerSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.Equal(0, schedulerSummary.CompletedExecutionCount);
        Assert.Equal(0, schedulerSummary.RequeuedExecutionCount);
        Assert.Equal(1, schedulerSummary.ReleasedExecutionCount);
        Assert.Equal(1, schedulerSummary.ConsecutiveFailureCount);
        Assert.Equal("released_after_exception", schedulerSummary.LastOutcome);
        Assert.True(schedulerSummary.LastFailureUtcTicks > 0);
        var recentActivity = Assert.Single(schedulerSummary.RecentActivity);
        Assert.Equal("released_after_exception", recentActivity.Outcome);
        Assert.Contains("Executor exploded.", recentActivity.FailureDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunBackgroundSchedulerIterationAsyncForTesting_AutoPausesAndClearsAfterRecovery() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableBackgroundSchedulerDaemon = true;
        options.BackgroundSchedulerFailureThreshold = 2;
        options.BackgroundSchedulerFailurePauseSeconds = 120;
        var session = new ChatServiceSession(options, Stream.Null);
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            new ToolDefinition(
                "system_info",
                "system info",
                ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        foreach (var threadId in new[] { "thread-background-scheduler-pause-a", "thread-background-scheduler-pause-b", "thread-background-scheduler-pause-c" }) {
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
        var firstFailureSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.False(firstFailureSummary.Paused);
        Assert.Equal(1, firstFailureSummary.ConsecutiveFailureCount);

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

        var pausedSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.True(pausedSummary.AutoPauseEnabled);
        Assert.Equal(2, pausedSummary.FailureThreshold);
        Assert.Equal(120, pausedSummary.FailurePauseSeconds);
        Assert.True(pausedSummary.Paused);
        Assert.True(pausedSummary.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
        Assert.Contains("consecutive_failure_threshold_reached", pausedSummary.PauseReason, StringComparison.Ordinal);
        Assert.Contains("requeued_after_tool_failure", pausedSummary.PauseReason, StringComparison.Ordinal);
        Assert.Equal(2, pausedSummary.ConsecutiveFailureCount);

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

        var recoveredSummary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.False(recoveredSummary.Paused);
        Assert.Equal(string.Empty, recoveredSummary.PauseReason);
        Assert.Equal(0, recoveredSummary.ConsecutiveFailureCount);
        Assert.Equal("completed", recoveredSummary.LastOutcome);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerStatusAsync_ReturnsStructuredSchedulerSummary() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerAllowedPackIds.Add("system");
        options.BackgroundSchedulerAllowedThreadIds.Add("thread-background-scheduler-handler");
        options.BackgroundSchedulerBlockedThreadIds.Add("thread-background-scheduler-handler-blocked");
        var session = new ChatServiceSession(options, Stream.Null);
        const string threadId = "thread-background-scheduler-handler";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-handler",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-handler.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-handler",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var request = new GetBackgroundSchedulerStatusRequest {
            RequestId = "req_scheduler_status"
        };

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<BackgroundSchedulerStatusMessage>(response);

        Assert.Equal("req_scheduler_status", typed.RequestId);
        Assert.Equal(1, typed.Scheduler.TrackedThreadCount);
        Assert.Equal(1, typed.Scheduler.ReadyItemCount);
        Assert.Equal(new[] { "system" }, typed.Scheduler.AllowedPackIds);
        Assert.Equal(new[] { threadId }, typed.Scheduler.AllowedThreadIds);
        Assert.Equal(new[] { "thread-background-scheduler-handler-blocked" }, typed.Scheduler.BlockedThreadIds);
        Assert.Contains(threadId, typed.Scheduler.ReadyThreadIds, StringComparer.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerStatusAsync_CanScopeAndTrimSchedulerSummary() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        var session = new ChatServiceSession(options, Stream.Null);
        const string threadIdOne = "thread-background-scheduler-scope-1";
        const string threadIdTwo = "thread-background-scheduler-scope-2";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadIdOne,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-scope-1",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-scope-1.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-scope-1",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadIdTwo,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-scope-2",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-scope-2.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-scope-2",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var request = new GetBackgroundSchedulerStatusRequest {
            RequestId = "req_scheduler_status_scope",
            ThreadId = threadIdTwo,
            IncludeRecentActivity = false,
            MaxReadyThreadIds = 1,
            MaxRunningThreadIds = 0,
            MaxThreadSummaries = 1
        };

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<BackgroundSchedulerStatusMessage>(response);

        Assert.Equal(threadIdTwo, typed.Scheduler.ScopeThreadId);
        Assert.Equal(1, typed.Scheduler.TrackedThreadCount);
        Assert.Equal(1, typed.Scheduler.ReadyItemCount);
        Assert.Equal(new[] { threadIdTwo }, typed.Scheduler.ReadyThreadIds);
        Assert.Empty(typed.Scheduler.RunningThreadIds);
        Assert.Empty(typed.Scheduler.RecentActivity);
        Assert.Single(typed.Scheduler.ThreadSummaries);
        Assert.Equal(threadIdTwo, typed.Scheduler.ThreadSummaries[0].ThreadId);
    }

    [Fact]
    public void HandleBackgroundSchedulerStatusAsync_InvalidStatusLimitIsRejectedByRequestContract() {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new GetBackgroundSchedulerStatusRequest {
            RequestId = "req_scheduler_status_invalid",
            MaxRecentActivity = ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems + 1
        });

        Assert.Contains("MaxRecentActivity", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerStateAsync_ManualPauseAndResumeReturnUpdatedSchedulerSummary() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerStateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using (var pauseStream = new MemoryStream())
        using (var pauseWriter = new StreamWriter(pauseStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var pauseRequest = new SetBackgroundSchedulerStateRequest {
                RequestId = "req_scheduler_pause",
                Paused = true,
                PauseSeconds = 120,
                Reason = "maintenance"
            };

            var pauseTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { pauseWriter, pauseRequest, default(System.Threading.CancellationToken) }));
            await pauseTask;
            await pauseWriter.FlushAsync();

            pauseStream.Position = 0;
            using var pauseDocument = await JsonDocument.ParseAsync(pauseStream);
            var pauseResponse = JsonSerializer.Deserialize(pauseDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var paused = Assert.IsType<BackgroundSchedulerStatusMessage>(pauseResponse);
            Assert.True(paused.Scheduler.Paused);
            Assert.True(paused.Scheduler.ManualPauseActive);
            Assert.True(paused.Scheduler.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
            Assert.Contains("manual_pause:120s:maintenance", paused.Scheduler.PauseReason, StringComparison.Ordinal);
        }

        using (var resumeStream = new MemoryStream())
        using (var resumeWriter = new StreamWriter(resumeStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var resumeRequest = new SetBackgroundSchedulerStateRequest {
                RequestId = "req_scheduler_resume",
                Paused = false
            };

            var resumeTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { resumeWriter, resumeRequest, default(System.Threading.CancellationToken) }));
            await resumeTask;
            await resumeWriter.FlushAsync();

            resumeStream.Position = 0;
            using var resumeDocument = await JsonDocument.ParseAsync(resumeStream);
            var resumeResponse = JsonSerializer.Deserialize(resumeDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var resumed = Assert.IsType<BackgroundSchedulerStatusMessage>(resumeResponse);
            Assert.False(resumed.Scheduler.Paused);
            Assert.False(resumed.Scheduler.ManualPauseActive);
            Assert.Equal(0, resumed.Scheduler.PausedUntilUtcTicks);
            Assert.Equal(string.Empty, resumed.Scheduler.PauseReason);
        }
    }

    [Fact]
    public async Task HandleBackgroundSchedulerStateAsync_ManualPauseIsSharedAcrossSessions() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        var sharedControlState = new ChatServiceBackgroundSchedulerControlState(options);
        var operatorSession = new ChatServiceSession(options, Stream.Null, backgroundSchedulerControlState: sharedControlState);
        var daemonSession = new ChatServiceSession(options, Stream.Null, backgroundSchedulerControlState: sharedControlState);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerStateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var pauseStream = new MemoryStream();
        using var pauseWriter = new StreamWriter(pauseStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var pauseRequest = new SetBackgroundSchedulerStateRequest {
            RequestId = "req_scheduler_pause_shared",
            Paused = true,
            PauseSeconds = 180,
            Reason = "shared-maintenance"
        };

        var pauseTask = Assert.IsAssignableFrom<Task>(method!.Invoke(operatorSession, new object?[] { pauseWriter, pauseRequest, default(System.Threading.CancellationToken) }));
        await pauseTask;

        var daemonSummary = daemonSession.BuildBackgroundSchedulerSummaryForTesting();
        Assert.True(daemonSummary.Paused);
        Assert.True(daemonSummary.ManualPauseActive);
        Assert.True(daemonSummary.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
        Assert.Contains("manual_pause:180s:shared-maintenance", daemonSummary.PauseReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerStateAsync_InvalidPauseSecondsReturnsError() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerStateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var request = new SetBackgroundSchedulerStateRequest {
            RequestId = "req_scheduler_pause_invalid",
            Paused = false,
            PauseSeconds = 120
        };

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<ErrorMessage>(response);

        Assert.Equal("req_scheduler_pause_invalid", typed.RequestId);
        Assert.Equal("invalid_argument", typed.Code);
        Assert.Contains("pauseSeconds can only be set when paused=true", typed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerMaintenanceWindowsAsync_AddAndResetReturnUpdatedSchedulerSummary() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerMaintenanceWindowsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using (var addStream = new MemoryStream())
        using (var addWriter = new StreamWriter(addStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var addRequest = new SetBackgroundSchedulerMaintenanceWindowsRequest(
                "req_scheduler_windows_add",
                "add",
                new[] { "monday@02:00/30;pack=system", "daily@23:30/120;thread=thread-maintenance" });

            var addTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { addWriter, addRequest, default(System.Threading.CancellationToken) }));
            await addTask;
            await addWriter.FlushAsync();

            addStream.Position = 0;
            using var addDocument = await JsonDocument.ParseAsync(addStream);
            var addResponse = JsonSerializer.Deserialize(addDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var updated = Assert.IsType<BackgroundSchedulerStatusMessage>(addResponse);
            Assert.Equal(new[] { "mon@02:00/30;pack=system", "daily@23:30/120;thread=thread-maintenance" }, updated.Scheduler.MaintenanceWindowSpecs);
        }

        using (var resetStream = new MemoryStream())
        using (var resetWriter = new StreamWriter(resetStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var resetRequest = new SetBackgroundSchedulerMaintenanceWindowsRequest(
                "req_scheduler_windows_reset",
                "reset");

            var resetTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { resetWriter, resetRequest, default(System.Threading.CancellationToken) }));
            await resetTask;
            await resetWriter.FlushAsync();

            resetStream.Position = 0;
            using var resetDocument = await JsonDocument.ParseAsync(resetStream);
            var resetResponse = JsonSerializer.Deserialize(resetDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var reset = Assert.IsType<BackgroundSchedulerStatusMessage>(resetResponse);
            Assert.Empty(reset.Scheduler.MaintenanceWindowSpecs);
        }
    }

    [Fact]
    public void HandleBackgroundSchedulerMaintenanceWindowsAsync_InvalidOperationIsRejectedByRequestContract() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerMaintenanceWindowsRequest(
            "req_scheduler_windows_invalid",
            "merge",
            new[] { "mon@02:00/60" }));

        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerBlockedThreadsAsync_AddAndResetReturnUpdatedSchedulerSummary() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerBlockedThreadsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using (var addStream = new MemoryStream())
        using (var addWriter = new StreamWriter(addStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var addRequest = new SetBackgroundSchedulerBlockedThreadsRequest(
                "req_scheduler_threads_add",
                "add",
                new[] { "thread-muted-a", "thread-muted-b" },
                durationSeconds: 90);

            var addTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { addWriter, addRequest, default(System.Threading.CancellationToken) }));
            await addTask;
            await addWriter.FlushAsync();

            addStream.Position = 0;
            using var addDocument = await JsonDocument.ParseAsync(addStream);
            var addResponse = JsonSerializer.Deserialize(addDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var updated = Assert.IsType<BackgroundSchedulerStatusMessage>(addResponse);
            Assert.Equal(new[] { "thread-muted-a", "thread-muted-b" }, updated.Scheduler.BlockedThreadIds);
            Assert.All(updated.Scheduler.BlockedThreadSuppressions, static item => Assert.True(item.Temporary));
        }

        using (var resetStream = new MemoryStream())
        using (var resetWriter = new StreamWriter(resetStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var resetRequest = new SetBackgroundSchedulerBlockedThreadsRequest(
                "req_scheduler_threads_reset",
                "reset");

            var resetTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { resetWriter, resetRequest, default(System.Threading.CancellationToken) }));
            await resetTask;
            await resetWriter.FlushAsync();

            resetStream.Position = 0;
            using var resetDocument = await JsonDocument.ParseAsync(resetStream);
            var resetResponse = JsonSerializer.Deserialize(resetDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var reset = Assert.IsType<BackgroundSchedulerStatusMessage>(resetResponse);
            Assert.Empty(reset.Scheduler.BlockedThreadIds);
        }
    }

    [Fact]
    public void HandleBackgroundSchedulerBlockedThreadsAsync_InvalidOperationIsRejectedByRequestContract() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_invalid",
            "merge",
            new[] { "thread-a" }));

        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleBackgroundSchedulerBlockedThreadsAsync_DurationRequiresAddOperation() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_invalid_duration",
            "remove",
            new[] { "thread-a" },
            durationSeconds: 60));

        Assert.Contains("only supported for add operations", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerBlockedThreadsAsync_UntilNextMaintenanceWindowCreatesTemporarySuppression() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440;thread=thread-maintenance");
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerBlockedThreadsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var request = new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_until_window",
            "add",
            new[] { "thread-maintenance" },
            untilNextMaintenanceWindow: true);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<BackgroundSchedulerStatusMessage>(response);

        Assert.Equal(new[] { "thread-maintenance" }, typed.Scheduler.BlockedThreadIds);
        var suppression = Assert.Single(typed.Scheduler.BlockedThreadSuppressions);
        Assert.Equal("thread-maintenance", suppression.Id);
        Assert.True(suppression.Temporary);
        Assert.True(suppression.ExpiresUtcTicks > DateTime.UtcNow.Ticks);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerBlockedThreadsAsync_UntilNextMaintenanceWindowWithoutRelevantWindowReturnsError() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440;thread=other-thread");
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerBlockedThreadsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var request = new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_until_window_missing",
            "add",
            new[] { "thread-maintenance" },
            untilNextMaintenanceWindow: true);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<ErrorMessage>(response);

        Assert.Equal("invalid_argument", typed.Code);
        Assert.Contains("No relevant maintenance window was found for thread 'thread-maintenance'.", typed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerBlockedThreadsAsync_UntilNextMaintenanceWindowStartCreatesTemporarySuppression() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@23:59/1;thread=thread-maintenance");
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerBlockedThreadsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var request = new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_until_window_start",
            "add",
            new[] { "thread-maintenance" },
            untilNextMaintenanceWindowStart: true);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<BackgroundSchedulerStatusMessage>(response);

        Assert.Equal(new[] { "thread-maintenance" }, typed.Scheduler.BlockedThreadIds);
        var suppression = Assert.Single(typed.Scheduler.BlockedThreadSuppressions);
        Assert.Equal("thread-maintenance", suppression.Id);
        Assert.True(suppression.Temporary);
        Assert.True(suppression.ExpiresUtcTicks > DateTime.UtcNow.Ticks);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerBlockedPacksAsync_AddAndResetReturnUpdatedSchedulerSummary() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerBlockedPacksAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using (var addStream = new MemoryStream())
        using (var addWriter = new StreamWriter(addStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var addRequest = new SetBackgroundSchedulerBlockedPacksRequest(
                "req_scheduler_packs_add",
                "add",
                new[] { "system", "ad" },
                durationSeconds: 120);

            var addTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { addWriter, addRequest, default(System.Threading.CancellationToken) }));
            await addTask;
            await addWriter.FlushAsync();

            addStream.Position = 0;
            using var addDocument = await JsonDocument.ParseAsync(addStream);
            var addResponse = JsonSerializer.Deserialize(addDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var updated = Assert.IsType<BackgroundSchedulerStatusMessage>(addResponse);
            Assert.Equal(new[] { "active_directory", "system" }, updated.Scheduler.BlockedPackIds.OrderBy(static value => value, StringComparer.Ordinal).ToArray());
            Assert.All(updated.Scheduler.BlockedPackSuppressions, static item => Assert.True(item.Temporary));
        }

        using (var resetStream = new MemoryStream())
        using (var resetWriter = new StreamWriter(resetStream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" }) {
            var resetRequest = new SetBackgroundSchedulerBlockedPacksRequest(
                "req_scheduler_packs_reset",
                "reset");

            var resetTask = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { resetWriter, resetRequest, default(System.Threading.CancellationToken) }));
            await resetTask;
            await resetWriter.FlushAsync();

            resetStream.Position = 0;
            using var resetDocument = await JsonDocument.ParseAsync(resetStream);
            var resetResponse = JsonSerializer.Deserialize(resetDocument.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
            var reset = Assert.IsType<BackgroundSchedulerStatusMessage>(resetResponse);
            Assert.Empty(reset.Scheduler.BlockedPackIds);
        }
    }

    [Fact]
    public void HandleBackgroundSchedulerBlockedPacksAsync_InvalidOperationIsRejectedByRequestContract() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_invalid",
            "merge",
            new[] { "system" }));

        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleBackgroundSchedulerBlockedPacksAsync_DurationRequiresAddOperation() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_invalid_duration",
            "replace",
            new[] { "system" },
            durationSeconds: 60));

        Assert.Contains("only supported for add operations", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerBlockedPacksAsync_UntilNextMaintenanceWindowCreatesTemporarySuppression() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440;pack=system");
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerBlockedPacksAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var request = new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_until_window",
            "add",
            new[] { "system" },
            untilNextMaintenanceWindow: true);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<BackgroundSchedulerStatusMessage>(response);

        Assert.Equal(new[] { "system" }, typed.Scheduler.BlockedPackIds);
        var suppression = Assert.Single(typed.Scheduler.BlockedPackSuppressions);
        Assert.Equal("system", suppression.Id);
        Assert.True(suppression.Temporary);
        Assert.True(suppression.ExpiresUtcTicks > DateTime.UtcNow.Ticks);
    }

    [Fact]
    public async Task HandleBackgroundSchedulerBlockedPacksAsync_UntilNextMaintenanceWindowStartCreatesTemporarySuppression() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@23:59/1;pack=system");
        var session = new ChatServiceSession(options, Stream.Null);
        var method = typeof(ChatServiceSession).GetMethod("HandleBackgroundSchedulerBlockedPacksAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        var request = new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_until_window_start",
            "add",
            new[] { "system" },
            untilNextMaintenanceWindowStart: true);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(session, new object?[] { writer, request, default(System.Threading.CancellationToken) }));
        await task;
        await writer.FlushAsync();

        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        var response = JsonSerializer.Deserialize(document.RootElement.GetRawText(), ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<BackgroundSchedulerStatusMessage>(response);

        Assert.Equal(new[] { "system" }, typed.Scheduler.BlockedPackIds);
        var suppression = Assert.Single(typed.Scheduler.BlockedPackSuppressions);
        Assert.Equal("system", suppression.Id);
        Assert.True(suppression.Temporary);
        Assert.True(suppression.ExpiresUtcTicks > DateTime.UtcNow.Ticks);
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_ReflectsStartupPauseFromOptions() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerStartPaused = true;
        options.BackgroundSchedulerStartupPauseSeconds = 240;
        var session = new ChatServiceSession(options, Stream.Null);

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        Assert.True(summary.Paused);
        Assert.True(summary.ManualPauseActive);
        Assert.True(summary.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
        Assert.Equal("manual_pause:240s:startup", summary.PauseReason);
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_ReflectsActiveMaintenanceWindow() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440");
        var session = new ChatServiceSession(options, Stream.Null);

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        Assert.True(summary.Paused);
        Assert.False(summary.ManualPauseActive);
        Assert.True(summary.ScheduledPauseActive);
        Assert.Equal(new[] { "daily@00:00/1440" }, summary.MaintenanceWindowSpecs);
        Assert.Equal(new[] { "daily@00:00/1440" }, summary.ActiveMaintenanceWindowSpecs);
        Assert.StartsWith("maintenance_window:daily@00:00/1440", summary.PauseReason, StringComparison.Ordinal);
        Assert.True(summary.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_ExposesActiveScopedMaintenanceWindowWithoutPausingDaemon() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("daily@00:00/1440;pack=system");
        var session = new ChatServiceSession(options, Stream.Null);

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        Assert.False(summary.Paused);
        Assert.False(summary.ManualPauseActive);
        Assert.False(summary.ScheduledPauseActive);
        Assert.Equal(new[] { "daily@00:00/1440;pack=system" }, summary.MaintenanceWindowSpecs);
        Assert.Equal(new[] { "daily@00:00/1440;pack=system" }, summary.ActiveMaintenanceWindowSpecs);
        Assert.Equal(string.Empty, summary.PauseReason);
        Assert.Equal(0, summary.PausedUntilUtcTicks);
    }

    [Fact]
    public void BackgroundSchedulerControlState_PersistsManualPauseAcrossFreshInstances_AndClearsOnResume() {
        var (options, _, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writerState = new ChatServiceBackgroundSchedulerControlState(options);
        var paused = writerState.SetManualPause(paused: true, pauseSeconds: 300, pauseReason: "maintenance-window");
        var storePath = writerState.ResolveStorePathForTesting();

        Assert.True(paused.ManualPauseActive);
        Assert.True(File.Exists(storePath));
        Assert.StartsWith(persistenceDirectory, storePath, StringComparison.OrdinalIgnoreCase);

        var readerState = new ChatServiceBackgroundSchedulerControlState(options);
        var rehydrated = readerState.GetSnapshot(DateTime.UtcNow.Ticks);

        Assert.True(rehydrated.ManualPauseActive);
        Assert.True(rehydrated.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
        Assert.Equal("manual_pause:300s:maintenance-window", rehydrated.PauseReason);

        _ = readerState.SetManualPause(paused: false, pauseSeconds: null, pauseReason: string.Empty);

        var resumedState = new ChatServiceBackgroundSchedulerControlState(options);
        var resumed = resumedState.GetSnapshot(DateTime.UtcNow.Ticks);
        Assert.False(resumed.ManualPauseActive);
        Assert.Equal(0, resumed.PausedUntilUtcTicks);
        Assert.Equal(string.Empty, resumed.PauseReason);
        Assert.False(File.Exists(storePath));
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_RehydratesPersistedManualPauseFromFreshControlState() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        var writerState = new ChatServiceBackgroundSchedulerControlState(options);
        _ = writerState.SetManualPause(paused: true, pauseSeconds: 180, pauseReason: "restart-safe");

        var session = new ChatServiceSession(options, Stream.Null);
        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        Assert.True(summary.Paused);
        Assert.True(summary.ManualPauseActive);
        Assert.True(summary.PausedUntilUtcTicks > DateTime.UtcNow.Ticks);
        Assert.Equal("manual_pause:180s:restart-safe", summary.PauseReason);
    }

    [Fact]
    public void BackgroundSchedulerControlState_PersistsRuntimeMaintenanceWindowOverridesAcrossFreshInstances() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        options.BackgroundSchedulerMaintenanceWindows.Add("sun@01:00/120");

        var writerState = new ChatServiceBackgroundSchedulerControlState(options);
        writerState.UpdateMaintenanceWindows("replace", new[] { "monday@02:00/30;pack=system", "daily@23:30/120;thread=thread-maintenance" });

        var readerState = new ChatServiceBackgroundSchedulerControlState(options);

        Assert.Equal(new[] { "mon@02:00/30;pack=system", "daily@23:30/120;thread=thread-maintenance" }, readerState.GetMaintenanceWindowSpecs());

        readerState.UpdateMaintenanceWindows("reset", null);
        var resetState = new ChatServiceBackgroundSchedulerControlState(options);
        Assert.Equal(new[] { "sun@01:00/120" }, resetState.GetMaintenanceWindowSpecs());
    }

    [Fact]
    public void BackgroundSchedulerControlState_PersistsRuntimeBlockedThreadOverridesAcrossFreshInstances() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        options.BackgroundSchedulerBlockedThreadIds.Add("thread-default-blocked");

        var writerState = new ChatServiceBackgroundSchedulerControlState(options);
        writerState.UpdateBlockedThreads("replace", new[] { "thread-muted-a", "thread-muted-b" });

        var readerState = new ChatServiceBackgroundSchedulerControlState(options);
        Assert.Equal(new[] { "thread-muted-a", "thread-muted-b" }, readerState.GetBlockedThreadIds(DateTime.UtcNow.Ticks));

        readerState.UpdateBlockedThreads("reset", null);
        var resetState = new ChatServiceBackgroundSchedulerControlState(options);
        Assert.Equal(new[] { "thread-default-blocked" }, resetState.GetBlockedThreadIds(DateTime.UtcNow.Ticks));
    }

    [Fact]
    public void BackgroundSchedulerControlState_PersistsRuntimeBlockedPackOverridesAcrossFreshInstances() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        options.BackgroundSchedulerBlockedPackIds.Add("eventlog");

        var writerState = new ChatServiceBackgroundSchedulerControlState(options);
        writerState.UpdateBlockedPacks("replace", new[] { "system", "ad" });

        var readerState = new ChatServiceBackgroundSchedulerControlState(options);
        Assert.Equal(new[] { "active_directory", "system" }, readerState.GetBlockedPackIds(DateTime.UtcNow.Ticks).OrderBy(static value => value, StringComparer.Ordinal).ToArray());

        readerState.UpdateBlockedPacks("reset", null);
        var resetState = new ChatServiceBackgroundSchedulerControlState(options);
        Assert.Equal(new[] { "eventlog" }, resetState.GetBlockedPackIds(DateTime.UtcNow.Ticks));
    }

    [Fact]
    public void BackgroundSchedulerControlState_PersistsTemporaryBlockedThreadSuppressionsAcrossFreshInstances_AndExpiresThem() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writerState = new ChatServiceBackgroundSchedulerControlState(options);
        writerState.UpdateBlockedThreads("add", new[] { "thread-temp" }, durationSeconds: 120);

        var readerState = new ChatServiceBackgroundSchedulerControlState(options);
        Assert.Equal(new[] { "thread-temp" }, readerState.GetBlockedThreadIds(DateTime.UtcNow.Ticks));
        var suppression = Assert.Single(readerState.GetBlockedThreadSuppressions(DateTime.UtcNow.Ticks));
        Assert.True(suppression.Temporary);
        Assert.True(suppression.ExpiresUtcTicks > DateTime.UtcNow.Ticks);

        Assert.Empty(readerState.GetBlockedThreadIds(DateTime.UtcNow.AddMinutes(5).Ticks));
        Assert.Empty(readerState.GetBlockedThreadSuppressions(DateTime.UtcNow.AddMinutes(5).Ticks));
    }

    [Fact]
    public void BackgroundSchedulerControlState_PersistsTemporaryBlockedPackSuppressionsAcrossFreshInstances_AndExpiresThem() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writerState = new ChatServiceBackgroundSchedulerControlState(options);
        writerState.UpdateBlockedPacks("add", new[] { "system" }, durationSeconds: 120);

        var readerState = new ChatServiceBackgroundSchedulerControlState(options);
        Assert.Equal(new[] { "system" }, readerState.GetBlockedPackIds(DateTime.UtcNow.Ticks));
        var suppression = Assert.Single(readerState.GetBlockedPackSuppressions(DateTime.UtcNow.Ticks));
        Assert.True(suppression.Temporary);
        Assert.True(suppression.ExpiresUtcTicks > DateTime.UtcNow.Ticks);

        Assert.Empty(readerState.GetBlockedPackIds(DateTime.UtcNow.AddMinutes(5).Ticks));
        Assert.Empty(readerState.GetBlockedPackSuppressions(DateTime.UtcNow.AddMinutes(5).Ticks));
    }

    [Fact]
    public void BuildAutonomyCounterMetrics_IncludesActiveBackgroundFollowUpClasses() {
        var verificationItem = CreateBackgroundWorkItem(
            id: "tool_handoff:verify_alice",
            state: "ready",
            followUpKind: ToolHandoffFollowUpKinds.Verification,
            followUpPriority: ToolHandoffFollowUpPriorities.Critical);
        var enrichmentItem = CreateBackgroundWorkItem(
            id: "tool_handoff:enrich_srv42",
            state: "queued",
            followUpKind: ToolHandoffFollowUpKinds.Enrichment,
            followUpPriority: ToolHandoffFollowUpPriorities.Low);
        var completedItem = CreateBackgroundWorkItem(
            id: "tool_handoff:done_bob",
            state: "completed",
            followUpKind: ToolHandoffFollowUpKinds.Investigation,
            followUpPriority: ToolHandoffFollowUpPriorities.High);

        var counters = ChatServiceSession.BuildAutonomyCounterMetricsForTesting(
            nudgeUnknownEnvelopeReplanCount: 0,
            noTextRecoveryHitCount: 0,
            noTextToolOutputRecoveryHitCount: 0,
            proactiveSkipMutatingCount: 0,
            proactiveSkipReadOnlyCount: 0,
            proactiveSkipUnknownCount: 0,
            backgroundWorkSnapshot: new ChatServiceSession.ThreadBackgroundWorkSnapshot(
                QueuedCount: 1,
                ReadyCount: 1,
                RunningCount: 0,
                CompletedCount: 1,
                PendingReadOnlyCount: 0,
                PendingUnknownCount: 0,
                RecentEvidenceTools: Array.Empty<string>(),
                Items: new[] { verificationItem, enrichmentItem, completedItem }));

        Assert.Contains(
            counters,
            counter => string.Equals(counter.Name, "background_follow_up_verification_active", StringComparison.Ordinal)
                       && counter.Count == 1);
        Assert.Contains(
            counters,
            counter => string.Equals(counter.Name, "background_follow_up_enrichment_active", StringComparison.Ordinal)
                       && counter.Count == 1);
        Assert.Contains(
            counters,
            counter => string.Equals(counter.Name, "background_follow_up_high_priority_active", StringComparison.Ordinal)
                       && counter.Count == 1);
        Assert.DoesNotContain(
            counters,
            counter => string.Equals(counter.Name, "background_follow_up_investigation_active", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildAutonomyTelemetrySummary_DoesNotCountBackgroundFollowUpCountersAsRecoveryEvents() {
        var telemetry = ChatServiceSession.BuildAutonomyTelemetrySummaryForTesting(
            toolRounds: 2,
            projectionFallbackCount: 1,
            toolErrors: Array.Empty<ToolErrorMetricDto>(),
            autonomyCounters: new[] {
                new TurnCounterMetricDto {
                    Name = "background_follow_up_verification_active",
                    Count = 2
                },
                new TurnCounterMetricDto {
                    Name = "no_text_recovery_hits",
                    Count = 1
                }
            },
            completed: true);

        Assert.Equal(2, telemetry.AutonomyDepth);
        Assert.Equal(2, telemetry.RecoveryEvents);
        Assert.Equal(1.0d, telemetry.CompletionRate);
    }

    [Fact]
    public void ResolveThreadBackgroundWorkSnapshot_RehydratesFromPersistedLedgerAcrossSessions() {
        var (options, pendingActionsStorePath, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        const string threadId = "thread-background-work-persisted";

        var session = new ChatServiceSession(options, Stream.Null);
        session.RememberPendingActionsForTesting(
            threadId,
            """
            [Action]
            ix:action:v1
            id: verify_kerberos
            title: Verify Kerberos posture
            request: verify kerberos health for the same domain controller
            readonly: true
            reply: /act verify_kerberos
            """);
        session.RememberThreadToolEvidenceForTesting(
            threadId,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-kerberos",
                    Name = "ad_kerberos_diagnostics",
                    ArgumentsJson = "{\"domain_controller\":\"ad0.contoso.com\"}"
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-kerberos",
                    Ok = true,
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "Kerberos diagnostics completed."
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        var initialSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        Assert.Equal(1, initialSnapshot.ReadyCount);

        var backgroundWorkStorePath = session.ResolveBackgroundWorkStorePathForTesting();
        Assert.True(File.Exists(backgroundWorkStorePath));

        var toolEvidenceStorePath = Path.Combine(persistenceDirectory, "tool-evidence-cache.json");
        File.Delete(pendingActionsStorePath);
        File.Delete(toolEvidenceStorePath);

        var rehydratedSession = new ChatServiceSession(options, Stream.Null);
        var rehydratedSnapshot = rehydratedSession.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(1, rehydratedSnapshot.ReadyCount);
        Assert.Equal(1, rehydratedSnapshot.PendingReadOnlyCount);
        Assert.Contains("ad_kerberos_diagnostics", rehydratedSnapshot.RecentEvidenceTools, StringComparer.OrdinalIgnoreCase);
        var readyItem = Assert.Single(rehydratedSnapshot.Items);
        Assert.Equal("pending_action:verify_kerberos", readyItem.Id);
        Assert.Equal("ready", readyItem.State);
    }

    [Fact]
    public void ResolveThreadBackgroundWorkSnapshot_IgnoresNullPersistedItems() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        const string threadId = "thread-background-work-null-item";
        var nowTicks = DateTime.UtcNow.Ticks;

        var writerSession = new ChatServiceSession(options, Stream.Null);
        var backgroundWorkStorePath = writerSession.ResolveBackgroundWorkStorePathForTesting();
        Directory.CreateDirectory(Path.GetDirectoryName(backgroundWorkStorePath)!);
        File.WriteAllText(
            backgroundWorkStorePath,
            $$"""
            {
              "Version": 4,
              "Threads": {
                "{{threadId}}": {
                  "SeenUtcTicks": {{nowTicks}},
                  "QueuedCount": 0,
                  "ReadyCount": 1,
                  "RunningCount": 0,
                  "CompletedCount": 0,
                  "PendingReadOnlyCount": 1,
                  "PendingUnknownCount": 0,
                  "RecentEvidenceTools": [ "ad_user_lifecycle" ],
                  "Items": [
                    null,
                    {
                      "Id": "tool_handoff:verify_alice",
                      "Title": "Verify Alice",
                      "Request": "verify alice after disable",
                      "State": "ready",
                      "EvidenceToolNames": [ "ad_user_lifecycle" ],
                      "Kind": "tool_handoff",
                      "Mutability": "read_only",
                      "SourceToolName": "ad_user_lifecycle",
                      "SourceCallId": "call-ad-write",
                      "TargetPackId": "active_directory",
                      "TargetToolName": "ad_object_get",
                      "FollowUpKind": "verification",
                      "FollowUpPriority": 100,
                      "PreparedArgumentsJson": "{\"identity\":\"CN=alice,OU=Users,DC=contoso,DC=com\"}",
                      "ResultReference": "",
                      "ExecutionAttemptCount": 0,
                      "LastExecutionCallId": "",
                      "LastExecutionStartedUtcTicks": 0,
                      "LastExecutionFinishedUtcTicks": 0,
                      "LeaseExpiresUtcTicks": 0,
                      "CreatedUtcTicks": {{nowTicks}},
                      "UpdatedUtcTicks": {{nowTicks}}
                    }
                  ]
                }
              }
            }
            """);

        var session = new ChatServiceSession(options, Stream.Null);
        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.PendingReadOnlyCount);
        Assert.Contains("ad_user_lifecycle", snapshot.RecentEvidenceTools, StringComparer.OrdinalIgnoreCase);
        var item = Assert.Single(snapshot.Items);
        Assert.Equal("tool_handoff:verify_alice", item.Id);
        Assert.Equal("ready", item.State);
        Assert.Equal(ToolHandoffFollowUpKinds.Verification, item.FollowUpKind);
    }

    [Fact]
    public void ClearPendingActions_RemovesPersistedBackgroundWorkLedger() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        const string threadId = "thread-background-work-clear";

        var session = new ChatServiceSession(options, Stream.Null);
        session.RememberPendingActionsForTesting(
            threadId,
            """
            [Action]
            ix:action:v1
            id: verify_dns
            title: Verify DNS health
            request: verify dns posture for the same domain controller
            readonly: true
            reply: /act verify_dns
            """);
        session.RememberThreadToolEvidenceForTesting(
            threadId,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-dns",
                    Name = "ad_dns_diagnostics",
                    ArgumentsJson = "{\"domain_controller\":\"ad0.contoso.com\"}"
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-dns",
                    Ok = true,
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "DNS diagnostics completed."
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        _ = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        var backgroundWorkStorePath = session.ResolveBackgroundWorkStorePathForTesting();
        Assert.True(File.Exists(backgroundWorkStorePath));

        session.RememberPendingActionsForTesting(threadId, "Nothing else is queued here.");

        var rehydratedSession = new ChatServiceSession(options, Stream.Null);
        var rehydratedSnapshot = rehydratedSession.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(0, rehydratedSnapshot.ReadyCount);
        Assert.Equal(0, rehydratedSnapshot.QueuedCount);
        Assert.Empty(rehydratedSnapshot.Items);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsReadyExecutableItemsFromSuccessfulLifecycleWrite() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-write";
        var definitions = new[] {
            CreateDefinition(
                name: "ad_user_lifecycle",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_object_get",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
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
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ad-write",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"alice","operation":"create"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ad-write",
                    Ok = true,
                    Output = """{"ok":true,"identity":"alice","distinguished_name":"CN=alice,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(0, snapshot.QueuedCount);
        Assert.Equal(1, snapshot.PendingReadOnlyCount);
        Assert.Contains("ad_user_lifecycle", snapshot.RecentEvidenceTools, StringComparer.OrdinalIgnoreCase);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("tool_handoff", item.Kind);
        Assert.Equal("read_only", item.Mutability);
        Assert.Equal("ad_user_lifecycle", item.SourceToolName);
        Assert.Equal("call-ad-write", item.SourceCallId);
        Assert.Equal("active_directory", item.TargetPackId);
        Assert.Equal("ad_object_get", item.TargetToolName);
        Assert.Equal(ToolHandoffFollowUpKinds.Verification, item.FollowUpKind);
        Assert.Equal(ToolHandoffFollowUpPriorities.Critical, item.FollowUpPriority);
        Assert.Contains("\"identity\":\"CN=alice,OU=Users,DC=contoso,DC=com\"", item.PreparedArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("source_call_id=call-ad-write", item.ResultReference, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_UsesCallArgumentsForRemoteHostFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-host";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-remote-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv1.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-remote-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("tool_handoff", item.Kind);
        Assert.Equal("system", item.TargetPackId);
        Assert.Equal("system_info", item.TargetToolName);
        Assert.Contains("\"computer_name\":\"srv1.contoso.com\"", item.PreparedArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("target_value=srv1.contoso.com", item.ResultReference, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySetThreadBackgroundWorkItemState_TransitionsLifecycleAndPersistsAcrossSessions() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        const string threadId = "thread-background-work-lifecycle";

        var session = new ChatServiceSession(options, Stream.Null);
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-host",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv2.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-host",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var initialSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var item = Assert.Single(initialSnapshot.Items);
        Assert.Equal("ready", item.State);
        Assert.Equal(1, initialSnapshot.ReadyCount);
        Assert.Equal(0, initialSnapshot.RunningCount);
        Assert.Equal(0, initialSnapshot.CompletedCount);
        Assert.Equal(0, item.ExecutionAttemptCount);
        Assert.Equal(string.Empty, item.LastExecutionCallId);
        Assert.Equal(0, item.LastExecutionStartedUtcTicks);
        Assert.Equal(0, item.LastExecutionFinishedUtcTicks);
        Assert.Equal(0, item.LeaseExpiresUtcTicks);

        Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, item.Id, "running"));
        var runningSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var runningItem = Assert.Single(runningSnapshot.Items);
        Assert.Equal("running", runningItem.State);
        Assert.Equal(0, runningSnapshot.ReadyCount);
        Assert.Equal(1, runningSnapshot.RunningCount);
        Assert.Equal(0, runningSnapshot.CompletedCount);
        Assert.Equal(1, runningItem.ExecutionAttemptCount);
        Assert.True(runningItem.LastExecutionStartedUtcTicks > 0);
        Assert.Equal(0, runningItem.LastExecutionFinishedUtcTicks);
        Assert.True(runningItem.LeaseExpiresUtcTicks > runningItem.LastExecutionStartedUtcTicks);

        Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(
            threadId,
            item.Id,
            "completed",
            resultReference: "source_tool=remote_disk_inventory;target_tool=system_info;status=completed"));
        var completedSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var completedItem = Assert.Single(completedSnapshot.Items);
        Assert.Equal("completed", completedItem.State);
        Assert.Equal(0, completedSnapshot.ReadyCount);
        Assert.Equal(0, completedSnapshot.RunningCount);
        Assert.Equal(1, completedSnapshot.CompletedCount);
        Assert.Contains("status=completed", completedItem.ResultReference, StringComparison.Ordinal);
        Assert.Equal(1, completedItem.ExecutionAttemptCount);
        Assert.True(completedItem.LastExecutionStartedUtcTicks > 0);
        Assert.True(completedItem.LastExecutionFinishedUtcTicks >= completedItem.LastExecutionStartedUtcTicks);
        Assert.Equal(0, completedItem.LeaseExpiresUtcTicks);

        var rehydratedSession = new ChatServiceSession(options, Stream.Null);
        var rehydratedSnapshot = rehydratedSession.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var rehydratedItem = Assert.Single(rehydratedSnapshot.Items);
        Assert.Equal("completed", rehydratedItem.State);
        Assert.Equal(1, rehydratedSnapshot.CompletedCount);
        Assert.Contains("status=completed", rehydratedItem.ResultReference, StringComparison.Ordinal);
        Assert.Equal(1, rehydratedItem.ExecutionAttemptCount);
        Assert.True(rehydratedItem.LastExecutionStartedUtcTicks > 0);
        Assert.True(rehydratedItem.LastExecutionFinishedUtcTicks >= rehydratedItem.LastExecutionStartedUtcTicks);
        Assert.Equal(0, rehydratedItem.LeaseExpiresUtcTicks);
    }

    [Fact]
    public void TryBuildReadyBackgroundWorkToolCallForTesting_ClaimsReadyItemAndCompletionStampsOutcome() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-ready-replay";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv3.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var itemId,
            out var toolName,
            out var argumentsJson,
            out var reason));
        Assert.Equal("background_work_ready_readonly_autorun", reason);
        Assert.Equal("system_info", toolName);
        Assert.Contains("\"computer_name\":\"srv3.contoso.com\"", argumentsJson, StringComparison.Ordinal);

        var runningSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var runningItem = Assert.Single(runningSnapshot.Items);
        Assert.Equal(itemId, runningItem.Id);
        Assert.Equal("running", runningItem.State);
        Assert.Equal(1, runningSnapshot.RunningCount);
        Assert.Equal(1, runningItem.ExecutionAttemptCount);
        Assert.True(runningItem.LastExecutionStartedUtcTicks > 0);
        Assert.Equal(0, runningItem.LastExecutionFinishedUtcTicks);
        Assert.StartsWith("host_background_work_system_info_", runningItem.LastExecutionCallId, StringComparison.Ordinal);
        Assert.True(runningItem.LeaseExpiresUtcTicks > runningItem.LastExecutionStartedUtcTicks);

        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            itemId,
            "host_background_work_system_info_001",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_system_info_001",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var completedSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var completedItem = Assert.Single(completedSnapshot.Items);
        Assert.Equal("completed", completedItem.State);
        Assert.Equal(1, completedSnapshot.CompletedCount);
        Assert.Equal(1, completedItem.ExecutionAttemptCount);
        Assert.Equal("host_background_work_system_info_001", completedItem.LastExecutionCallId);
        Assert.True(completedItem.LastExecutionStartedUtcTicks > 0);
        Assert.True(completedItem.LastExecutionFinishedUtcTicks >= completedItem.LastExecutionStartedUtcTicks);
        Assert.Equal(0, completedItem.LeaseExpiresUtcTicks);
        Assert.Contains("execution_call_id=host_background_work_system_info_001", completedItem.ResultReference, StringComparison.Ordinal);
        Assert.Contains("execution_status=completed", completedItem.ResultReference, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberBackgroundWorkExecutionOutcomeForTesting_FailedReplayReturnsItemToReadyWithFailureReference() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-failed-replay";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv5.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var itemId,
            out _,
            out _,
            out _));

        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            itemId,
            "host_background_work_system_info_002",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_system_info_002",
                    Ok = false,
                    ErrorCode = "remote_unavailable",
                    Output = """{"ok":false}"""
                }
            });

        var readySnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var readyItem = Assert.Single(readySnapshot.Items);
        Assert.Equal("ready", readyItem.State);
        Assert.Equal(1, readySnapshot.ReadyCount);
        Assert.Equal(0, readySnapshot.RunningCount);
        Assert.Equal(0, readySnapshot.CompletedCount);
        Assert.Equal(1, readyItem.ExecutionAttemptCount);
        Assert.Equal("host_background_work_system_info_002", readyItem.LastExecutionCallId);
        Assert.True(readyItem.LastExecutionStartedUtcTicks > 0);
        Assert.True(readyItem.LastExecutionFinishedUtcTicks >= readyItem.LastExecutionStartedUtcTicks);
        Assert.Equal(0, readyItem.LeaseExpiresUtcTicks);
        Assert.Contains("execution_call_id=host_background_work_system_info_002", readyItem.ResultReference, StringComparison.Ordinal);
        Assert.Contains("execution_status=failed", readyItem.ResultReference, StringComparison.Ordinal);
        Assert.Contains("execution_error_code=remote_unavailable", readyItem.ResultReference, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildReadyBackgroundWorkToolCallForTesting_PrefersLeastRetriedOldestReadyItem() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-priority";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-disk-a",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv6.contoso.com"}"""
                },
                new() {
                    CallId = "call-disk-b",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv7.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-disk-a",
                    Ok = true,
                    Output = """{"ok":true}"""
                },
                new() {
                    CallId = "call-disk-b",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var firstItemId,
            out _,
            out var firstArgumentsJson,
            out _));
        Assert.Contains("\"computer_name\":\"srv6.contoso.com\"", firstArgumentsJson, StringComparison.Ordinal);

        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            firstItemId,
            "host_background_work_system_info_010",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_system_info_010",
                    Ok = false,
                    ErrorCode = "remote_unavailable",
                    Output = """{"ok":false}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var secondItemId,
            out _,
            out var secondArgumentsJson,
            out _));
        Assert.NotEqual(firstItemId, secondItemId);
        Assert.Contains("\"computer_name\":\"srv7.contoso.com\"", secondArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildReadyBackgroundWorkToolCallForTesting_PrefersHigherPriorityFollowUpOverOlderReadyItem() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-followup-priority";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
            CreateDefinition(
                name: "ad_user_lifecycle",
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
            new ToolDefinition("system_info", "system info", ToolSchema.Object().NoAdditionalProperties()),
            new ToolDefinition("ad_object_get", "ad object get", ToolSchema.Object().NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv10.contoso.com"}"""
                },
                new() {
                    CallId = "call-ad-write",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"bob","operation":"disable"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                },
                new() {
                    CallId = "call-ad-write",
                    Ok = true,
                    Output = """{"ok":true,"distinguished_name":"CN=bob,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var toolName,
            out var argumentsJson,
            out _));

        Assert.Equal("ad_object_get", toolName);
        Assert.Contains("\"identity\":\"CN=bob,OU=Users,DC=contoso,DC=com\"", argumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildReadyBackgroundWorkToolCallForTesting_ThrottlesImmediateRetryAfterFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-cooldown";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv8.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var itemId,
            out _,
            out _,
            out _));

        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            itemId,
            "host_background_work_system_info_011",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_system_info_011",
                    Ok = false,
                    ErrorCode = "remote_unavailable",
                    Output = """{"ok":false}"""
                }
            });

        Assert.False(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out _,
            out _,
            out _,
            out var reason));
        Assert.Equal("background_work_retry_cooldown_active", reason);
    }

    [Fact]
    public void ResolveThreadBackgroundWorkSnapshotForTesting_RequeuesExpiredRunningLease() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-lease-expiry";
        var definitions = new[] {
            CreateDefinition(
                name: "remote_disk_inventory",
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
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-disk",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv9.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-disk",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var itemId,
            out _,
            out _,
            out _));

        Assert.True(session.TrySetThreadBackgroundWorkLeaseExpiryForTesting(
            threadId,
            itemId,
            DateTime.UtcNow.AddMinutes(-5).Ticks));

        var recoveredSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var recoveredItem = Assert.Single(recoveredSnapshot.Items);
        Assert.Equal("ready", recoveredItem.State);
        Assert.Equal(1, recoveredSnapshot.ReadyCount);
        Assert.Equal(0, recoveredSnapshot.RunningCount);
        Assert.Equal(1, recoveredItem.ExecutionAttemptCount);
        Assert.Equal(0, recoveredItem.LeaseExpiresUtcTicks);

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var replayedItemId,
            out _,
            out var replayedArgumentsJson,
            out _));
        Assert.Equal(itemId, replayedItemId);
        Assert.Contains("\"computer_name\":\"srv9.contoso.com\"", replayedArgumentsJson, StringComparison.Ordinal);
    }

    private static ToolDefinition CreateDefinition(
        string name,
        ToolHandoffContract? handoff = null,
        ToolWriteGovernanceContract? writeGovernance = null) {
        return new ToolDefinition(
            name: name,
            description: "test tool",
            handoff: handoff,
            writeGovernance: writeGovernance);
    }

    private static ChatServiceSession.ThreadBackgroundWorkItem CreateBackgroundWorkItem(
        string id,
        string state,
        string followUpKind,
        int followUpPriority) {
        var nowTicks = DateTime.UtcNow.Ticks;
        return new ChatServiceSession.ThreadBackgroundWorkItem(
            Id: id,
            Title: id,
            Request: id,
            State: state,
            EvidenceToolNames: Array.Empty<string>(),
            Kind: "tool_handoff",
            Mutability: "read_only",
            SourceToolName: "source_tool",
            SourceCallId: "call-source",
            TargetPackId: "active_directory",
            TargetToolName: "ad_object_get",
            FollowUpKind: followUpKind,
            FollowUpPriority: followUpPriority,
            PreparedArgumentsJson: """{"identity":"CN=test,DC=contoso,DC=com"}""",
            ResultReference: string.Empty,
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: nowTicks,
            UpdatedUtcTicks: nowTicks);
    }
}
