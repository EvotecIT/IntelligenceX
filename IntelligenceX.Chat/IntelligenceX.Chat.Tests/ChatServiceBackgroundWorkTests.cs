using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.System;
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
            DependencyItemIds: Array.Empty<string>(),
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
    public void BackgroundWorkQueuedStatusMessages_ExposeDependencyHelpers() {
        var helperItem = new ChatServiceSession.ThreadBackgroundWorkItem(
            Id: "handoff:source:eventlog_channels_list:machine_name:srv-eventlog.contoso.com",
            Title: "Probe Event Log",
            Request: "probe event log",
            State: "ready",
            DependencyItemIds: Array.Empty<string>(),
            EvidenceToolNames: new[] { "seed_eventlog_live_followup" },
            Kind: "tool_handoff",
            Mutability: "read_only",
            SourceToolName: "seed_eventlog_live_followup",
            SourceCallId: "call-live-followup",
            TargetPackId: "eventlog",
            TargetToolName: "eventlog_channels_list",
            FollowUpKind: ToolHandoffFollowUpKinds.Verification,
            FollowUpPriority: ToolHandoffFollowUpPriorities.High,
            PreparedArgumentsJson: """{"machine_name":"srv-eventlog.contoso.com"}""",
            ResultReference: "helper_kind=probe",
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: DateTime.UtcNow.Ticks,
            UpdatedUtcTicks: DateTime.UtcNow.Ticks);
        var dependentItem = new ChatServiceSession.ThreadBackgroundWorkItem(
            Id: "handoff:source:eventlog_live_query:machine_name:srv-eventlog.contoso.com",
            Title: "Run Event Log Query",
            Request: "run event log live query",
            State: "queued",
            DependencyItemIds: new[] { helperItem.Id },
            EvidenceToolNames: new[] { "seed_eventlog_live_followup" },
            Kind: "tool_handoff",
            Mutability: "read_only",
            SourceToolName: "seed_eventlog_live_followup",
            SourceCallId: "call-live-followup",
            TargetPackId: "eventlog",
            TargetToolName: "eventlog_live_query",
            FollowUpKind: ToolHandoffFollowUpKinds.Verification,
            FollowUpPriority: ToolHandoffFollowUpPriorities.High,
            PreparedArgumentsJson: """{"machine_name":"srv-eventlog.contoso.com"}""",
            ResultReference: string.Empty,
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: DateTime.UtcNow.Ticks,
            UpdatedUtcTicks: DateTime.UtcNow.Ticks);

        var queued = ChatServiceSession.BuildBackgroundWorkQueuedStatusMessageForTesting(
            queuedCount: 1,
            items: new[] { dependentItem, helperItem });

        Assert.Equal(
            "Queued 1 safe follow-up item for background preparation. Waiting on prerequisites: eventlog_channels_list.",
            queued);
    }

    [Fact]
    public void BackgroundWorkStatusMessages_ExposeReusedHelperFreshness() {
        var reusedHelperItem = new ChatServiceSession.ThreadBackgroundWorkItem(
            Id: "handoff:source:eventlog_channels_list:machine_name:srv-eventlog.contoso.com",
            Title: "Probe Event Log",
            Request: "probe event log",
            State: "completed",
            DependencyItemIds: Array.Empty<string>(),
            EvidenceToolNames: new[] { "seed_eventlog_live_followup" },
            Kind: "tool_handoff",
            Mutability: "read_only",
            SourceToolName: "seed_eventlog_live_followup",
            SourceCallId: "call-live-followup",
            TargetPackId: "eventlog",
            TargetToolName: "eventlog_channels_list",
            FollowUpKind: ToolHandoffFollowUpKinds.Verification,
            FollowUpPriority: ToolHandoffFollowUpPriorities.High,
            PreparedArgumentsJson: """{"machine_name":"srv-eventlog.contoso.com"}""",
            ResultReference: "helper_kind=probe;helper_reuse=cached_tool_evidence;helper_reuse_age_seconds=42;helper_reuse_ttl_seconds=900",
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: DateTime.UtcNow.Ticks,
            UpdatedUtcTicks: DateTime.UtcNow.Ticks);
        var readyDependentItem = new ChatServiceSession.ThreadBackgroundWorkItem(
            Id: "handoff:source:eventlog_live_query:machine_name:srv-eventlog.contoso.com",
            Title: "Run Event Log Query",
            Request: "run event log live query",
            State: "ready",
            DependencyItemIds: new[] { reusedHelperItem.Id },
            EvidenceToolNames: new[] { "seed_eventlog_live_followup" },
            Kind: "tool_handoff",
            Mutability: "read_only",
            SourceToolName: "seed_eventlog_live_followup",
            SourceCallId: "call-live-followup",
            TargetPackId: "eventlog",
            TargetToolName: "eventlog_live_query",
            FollowUpKind: ToolHandoffFollowUpKinds.Verification,
            FollowUpPriority: ToolHandoffFollowUpPriorities.High,
            PreparedArgumentsJson: """{"machine_name":"srv-eventlog.contoso.com"}""",
            ResultReference: string.Empty,
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: DateTime.UtcNow.Ticks,
            UpdatedUtcTicks: DateTime.UtcNow.Ticks);
        var pendingHelperItem = new ChatServiceSession.ThreadBackgroundWorkItem(
            Id: "handoff:source:eventlog_runtime_profile_validate:machine_name:srv-eventlog.contoso.com",
            Title: "Validate Event Log Runtime Profile",
            Request: "validate runtime profile",
            State: "ready",
            DependencyItemIds: Array.Empty<string>(),
            EvidenceToolNames: new[] { "seed_eventlog_live_followup" },
            Kind: "tool_handoff",
            Mutability: "read_only",
            SourceToolName: "seed_eventlog_live_followup",
            SourceCallId: "call-live-followup",
            TargetPackId: "eventlog",
            TargetToolName: "eventlog_runtime_profile_validate",
            FollowUpKind: ToolHandoffFollowUpKinds.Verification,
            FollowUpPriority: ToolHandoffFollowUpPriorities.High,
            PreparedArgumentsJson: """{"machine_name":"srv-eventlog.contoso.com"}""",
            ResultReference: "helper_kind=setup",
            ExecutionAttemptCount: 0,
            LastExecutionCallId: string.Empty,
            LastExecutionStartedUtcTicks: 0,
            LastExecutionFinishedUtcTicks: 0,
            LeaseExpiresUtcTicks: 0,
            CreatedUtcTicks: DateTime.UtcNow.Ticks,
            UpdatedUtcTicks: DateTime.UtcNow.Ticks);
        var blockedDependentItem = readyDependentItem with {
            Id = "handoff:source:eventlog_live_query:machine_name:srv-eventlog-queued.contoso.com",
            State = "queued",
            DependencyItemIds = new[] { pendingHelperItem.Id, reusedHelperItem.Id },
            PreparedArgumentsJson = """{"machine_name":"srv-eventlog-queued.contoso.com"}"""
        };

        var ready = ChatServiceSession.BuildBackgroundWorkReadyStatusMessageForTesting(
            readyCount: 1,
            recentEvidenceTools: new[] { "seed_eventlog_live_followup" },
            items: new[] { readyDependentItem, reusedHelperItem });
        var queued = ChatServiceSession.BuildBackgroundWorkQueuedStatusMessageForTesting(
            queuedCount: 1,
            items: new[] { blockedDependentItem, pendingHelperItem, reusedHelperItem });

        Assert.Equal(
            "Prepared 1 read-only follow-up item from recent evidence. Evidence: seed_eventlog_live_followup. Reused fresh prerequisite evidence instead of rerunning helpers: eventlog_channels_list (42s old, within 15m freshness window). Priority: high verification.",
            ready);
        Assert.Equal(
            "Queued 1 safe follow-up item for background preparation. Waiting on prerequisites: eventlog_runtime_profile_validate. Reused fresh prerequisite evidence instead of rerunning helpers: eventlog_channels_list (42s old, within 15m freshness window).",
            queued);
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
        Assert.Equal(0, summary.LastSchedulerTickUtcTicks);
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
    public void BuildBackgroundSchedulerSummary_ExposesDependencyBlockedWorkAndHelpers() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-scheduler-dependency-blocked";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_runtime_profile_validate"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()),
            new ToolDefinition(
                "eventlog_runtime_profile_validate",
                "Validate runtime profile readiness for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        Assert.Equal(1, summary.DependencyBlockedThreadCount);
        Assert.Equal(1, summary.DependencyBlockedItemCount);
        Assert.Contains("eventlog_channels_list", summary.DependencyHelperToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, summary.DependencyRecoveryReason);
        Assert.Equal(string.Empty, summary.DependencyNextAction);
        var threadSummary = Assert.Single(summary.ThreadSummaries, static item => string.Equals(item.ThreadId, threadId, StringComparison.Ordinal));
        Assert.NotNull(threadSummary.ContinuationHint);
        Assert.Equal("wait_for_prerequisites", threadSummary.ContinuationHint!.NextAction);
        Assert.Equal("background_prerequisite_pending", threadSummary.ContinuationHint.RecoveryReason);
        Assert.Contains("eventlog_channels_list", threadSummary.ContinuationHint.HelperToolNames, StringComparer.OrdinalIgnoreCase);
        var refreshRequest = Assert.Single(threadSummary.ContinuationHint.SuggestedRequests);
        Assert.Equal("get_background_scheduler_status", refreshRequest.RequestKind);
        Assert.Equal("refresh_blocked_thread_status", refreshRequest.Purpose);
        Assert.Contains(refreshRequest.SuggestedArguments, static argument =>
            string.Equals(argument.Name, "threadId", StringComparison.Ordinal)
            && string.Equals(argument.Value, threadId, StringComparison.Ordinal)
            && string.Equals(argument.ValueKind, "string", StringComparison.Ordinal));
        Assert.Contains(refreshRequest.SuggestedArguments, static argument =>
            string.Equals(argument.Name, "includeThreadSummaries", StringComparison.Ordinal)
            && string.Equals(argument.Value, "true", StringComparison.Ordinal)
            && string.Equals(argument.ValueKind, "boolean", StringComparison.Ordinal));
        Assert.Equal(1, threadSummary.DependencyBlockedItemCount);
        Assert.Contains("eventlog_channels_list", threadSummary.DependencyHelperToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog_runtime_profile_validate", threadSummary.DependencyHelperToolNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_ExposesReusedHelperFreshnessForThread() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-scheduler-helper-reuse";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberThreadToolEvidenceForTesting(
            threadId,
            new[] {
                new ToolCallDto {
                    CallId = "call-cached-probe",
                    Name = "eventlog_channels_list",
                    ArgumentsJson = """{"machine_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-cached-probe",
                    Ok = true,
                    Output = """{"ok":true,"channels":["System"]}""",
                    SummaryMarkdown = "Event log channels are reachable on srv-eventlog.contoso.com."
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();

        var threadSummary = Assert.Single(summary.ThreadSummaries, static item => string.Equals(item.ThreadId, threadId, StringComparison.Ordinal));
        Assert.Equal(1, threadSummary.ReusedHelperItemCount);
        Assert.Contains("eventlog_channels_list", threadSummary.ReusedHelperToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("probe_reuse_window", threadSummary.ReusedHelperPolicyNames, StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(threadSummary.ReusedHelperFreshestAgeSeconds);
        Assert.NotNull(threadSummary.ReusedHelperOldestAgeSeconds);
        Assert.Equal(900, threadSummary.ReusedHelperFreshestTtlSeconds);
        Assert.Equal(900, threadSummary.ReusedHelperOldestTtlSeconds);
        Assert.True(threadSummary.ReusedHelperFreshestAgeSeconds >= 0);
        Assert.True(threadSummary.ReusedHelperOldestAgeSeconds >= threadSummary.ReusedHelperFreshestAgeSeconds);
    }

    [Fact]
    public void TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting_ShortensIdlePollForFreshPackScopedReuseWindow() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerPollSeconds = 300;
        var session = new ChatServiceSession(options, Stream.Null);
        const string threadId = "thread-background-adaptive-idle-pack-guided-probe-reuse";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_customx_followup",
                description: "seed custom follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "customx",
                            TargetToolName = "customx_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "customx_live_query",
                "Inspect runtime state after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "customx_connectivity_probe"
                }),
            new ToolDefinition(
                "customx_connectivity_probe",
                "Validate runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] { new PackSpecificProbeFreshnessGuidancePack() }));
        session.SeedThreadToolEvidenceEntryForTesting(
            threadId,
            toolName: "customx_connectivity_probe",
            argumentsJson: """{"machine_name":"srv-pack-guided.contoso.com"}""",
            output: """{"ok":true}""",
            summaryMarkdown: "Connectivity probe succeeded.",
            seenUtcTicks: DateTime.UtcNow.AddSeconds(-90).Ticks);

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-customx-followup",
                    Name = "seed_customx_followup",
                    ArgumentsJson = """{"computer_name":"srv-pack-guided.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-customx-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var dependentItem = Assert.Single(
            session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "customx_live_query", StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(dependentItem.State, "queued", StringComparison.OrdinalIgnoreCase)) {
            Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, dependentItem.Id, "queued"));
        }

        Assert.True(
            session.TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting(
                TimeSpan.FromSeconds(options.BackgroundSchedulerPollSeconds),
                out var delay,
                out var reason));
        session.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(delay, reason);
        var summary = session.BuildBackgroundSchedulerSummaryForTesting();
        Assert.InRange(delay.TotalSeconds, 5, 10);
        Assert.Contains("customx_probe_reuse_window", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thread=", reason, StringComparison.Ordinal);
        Assert.Contains("remaining=", reason, StringComparison.Ordinal);
        Assert.True(summary.AdaptiveIdleActive);
        Assert.Equal((int)Math.Ceiling(delay.TotalSeconds), summary.LastAdaptiveIdleDelaySeconds);
        Assert.Equal(reason, summary.LastAdaptiveIdleReason);
    }

    [Fact]
    public void TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting_DoesNotShortenWhenNoFreshReusedHelperEvidenceExists() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerPollSeconds = 300;
        var session = new ChatServiceSession(options, Stream.Null);
        const string threadId = "thread-background-adaptive-idle-no-fresh-reuse";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_customx_followup",
                description: "seed custom follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "customx",
                            TargetToolName = "customx_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "customx_live_query",
                "Inspect runtime state after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "customx_connectivity_probe"
                }),
            new ToolDefinition(
                "customx_connectivity_probe",
                "Validate runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] { new PackSpecificProbeFreshnessGuidancePack() }));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-customx-followup",
                    Name = "seed_customx_followup",
                    ArgumentsJson = """{"computer_name":"srv-pack-guided.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-customx-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.False(
            session.TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting(
                TimeSpan.FromSeconds(options.BackgroundSchedulerPollSeconds),
                out var delay,
                out var reason));
        Assert.Equal(TimeSpan.FromSeconds(options.BackgroundSchedulerPollSeconds), delay);
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting_UsesShortestRemainingFreshnessAcrossMixedHelpers() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.BackgroundSchedulerPollSeconds = 300;
        var session = new ChatServiceSession(options, Stream.Null);
        const string threadId = "thread-background-adaptive-idle-mixed-helper-freshness";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_customx_followup",
                description: "seed custom follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "customx",
                            TargetToolName = "customx_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "customx_live_query",
                "Inspect runtime state after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "customx_connectivity_probe"
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "customx_runtime_profile_validate"
                }),
            new ToolDefinition(
                "customx_connectivity_probe",
                "Validate runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()),
            new ToolDefinition(
                "customx_runtime_profile_validate",
                "Validate runtime profile readiness.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] { new PackSpecificProbeFreshnessGuidancePack() }));
        session.SeedThreadToolEvidenceEntryForTesting(
            threadId,
            toolName: "customx_connectivity_probe",
            argumentsJson: """{"machine_name":"srv-mixed.contoso.com"}""",
            output: """{"ok":true}""",
            summaryMarkdown: "Connectivity probe succeeded.",
            seenUtcTicks: DateTime.UtcNow.AddSeconds(-10).Ticks);
        session.SeedThreadToolEvidenceEntryForTesting(
            threadId,
            toolName: "customx_runtime_profile_validate",
            argumentsJson: """{"machine_name":"srv-mixed.contoso.com"}""",
            output: """{"ok":true}""",
            summaryMarkdown: "Runtime profile validation succeeded.",
            seenUtcTicks: DateTime.UtcNow.AddSeconds(-100).Ticks);

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-customx-followup-mixed",
                    Name = "seed_customx_followup",
                    ArgumentsJson = """{"computer_name":"srv-mixed.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-customx-followup-mixed",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var dependentItem = Assert.Single(
            session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "customx_live_query", StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(dependentItem.State, "queued", StringComparison.OrdinalIgnoreCase)) {
            Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, dependentItem.Id, "queued"));
        }

        Assert.True(
            session.TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting(
                TimeSpan.FromSeconds(options.BackgroundSchedulerPollSeconds),
                out var delay,
                out var reason));

        Assert.Equal(TimeSpan.FromSeconds(28), delay);
        Assert.Contains("customx_probe_reuse_window", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remaining=110s", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_DoesNotReuseStaleProbeEvidenceOutsidePolicyWindow() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-helper-evidence-stale";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.SeedThreadToolEvidenceEntryForTesting(
            threadId,
            toolName: "eventlog_channels_list",
            argumentsJson: """{"machine_name":"srv-stale-eventlog.contoso.com"}""",
            output: """{"ok":true,"channels":["System"]}""",
            summaryMarkdown: "Event log channels are reachable on srv-stale-eventlog.contoso.com.",
            seenUtcTicks: DateTime.UtcNow.AddHours(-1).Ticks);

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup-stale",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-stale-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup-stale",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var helperItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", helperItem.State);
        Assert.DoesNotContain("helper_reuse=cached_tool_evidence", helperItem.ResultReference, StringComparison.Ordinal);

        var dependentItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queued", dependentItem.State);
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_ExposesDependencyRecoveryReasonAndAuthArguments() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-scheduler-dependency-auth";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var helperItem = Assert.Single(
            session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, helperItem.Id, "running"));
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            helperItem.Id,
            "call-probe",
            new[] {
                new ToolOutputDto {
                    CallId = "call-probe",
                    Ok = false,
                    ErrorCode = "authentication_failed",
                    Error = "Missing runtime profile for remote eventlog access.",
                    Output = """{"ok":false}"""
                }
            });

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();
        var threadSummary = Assert.Single(summary.ThreadSummaries, static item => string.Equals(item.ThreadId, threadId, StringComparison.Ordinal));

        Assert.Equal("background_prerequisite_auth_context_required", threadSummary.DependencyRecoveryReason);
        Assert.Equal("request_runtime_auth_context", threadSummary.DependencyNextAction);
        Assert.NotNull(threadSummary.ContinuationHint);
        Assert.Equal("request_runtime_auth_context", threadSummary.ContinuationHint!.NextAction);
        Assert.Equal("background_prerequisite_auth_context_required", threadSummary.ContinuationHint.RecoveryReason);
        Assert.Contains("profile_id", threadSummary.ContinuationHint.InputArgumentNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, threadSummary.ContinuationHint.SuggestedRequests.Length);
        var listProfilesRequest = Assert.Single(
            threadSummary.ContinuationHint.SuggestedRequests,
            static request => string.Equals(request.RequestKind, "list_profiles", StringComparison.Ordinal));
        Assert.Equal("discover_runtime_profiles", listProfilesRequest.Purpose);
        Assert.Empty(listProfilesRequest.RequiredArgumentNames);
        Assert.Empty(listProfilesRequest.SuggestedArguments);
        var setProfileRequest = Assert.Single(
            threadSummary.ContinuationHint.SuggestedRequests,
            static request => string.Equals(request.RequestKind, "set_profile", StringComparison.Ordinal));
        Assert.Equal("apply_runtime_auth_context", setProfileRequest.Purpose);
        Assert.Contains("profileName", setProfileRequest.RequiredArgumentNames, StringComparer.Ordinal);
        Assert.Contains("profile_id", setProfileRequest.SatisfiesInputArgumentNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(setProfileRequest.SuggestedArguments, static argument =>
            string.Equals(argument.Name, "newThread", StringComparison.Ordinal)
            && string.Equals(argument.Value, "false", StringComparison.Ordinal)
            && string.Equals(argument.ValueKind, "boolean", StringComparison.Ordinal));
        Assert.Contains("eventlog_channels_list", threadSummary.DependencyAuthenticationHelperToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("profile_id", threadSummary.DependencyAuthenticationArgumentNames, StringComparer.OrdinalIgnoreCase);
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
    public void TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting_RehydratesPersistedReuseUrgencyAcrossSessions() {
        var (options, _, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        options.BackgroundSchedulerPollSeconds = 300;
        const string threadId = "thread-background-work-persisted-adaptive-idle";

        var definitions = new[] {
            new ToolDefinition(
                name: "seed_customx_followup",
                description: "seed custom follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "customx",
                            TargetToolName = "customx_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "customx_live_query",
                "Inspect runtime state after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "customx_connectivity_probe"
                }),
            new ToolDefinition(
                "customx_connectivity_probe",
                "Validate runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };

        var writerSession = new ChatServiceSession(options, Stream.Null);
        writerSession.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] { new PackSpecificProbeFreshnessGuidancePack() }));
        writerSession.SeedThreadToolEvidenceEntryForTesting(
            threadId,
            toolName: "customx_connectivity_probe",
            argumentsJson: """{"machine_name":"srv-pack-guided.contoso.com"}""",
            output: """{"ok":true}""",
            summaryMarkdown: "Connectivity probe succeeded.",
            seenUtcTicks: DateTime.UtcNow.AddSeconds(-90).Ticks);
        writerSession.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-customx-followup",
                    Name = "seed_customx_followup",
                    ArgumentsJson = """{"computer_name":"srv-pack-guided.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-customx-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var dependentItem = Assert.Single(
            writerSession.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "customx_live_query", StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(dependentItem.State, "queued", StringComparison.OrdinalIgnoreCase)) {
            Assert.True(writerSession.TrySetThreadBackgroundWorkItemStateForTesting(threadId, dependentItem.Id, "queued"));
        }

        Assert.True(
            writerSession.TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting(
                TimeSpan.FromSeconds(options.BackgroundSchedulerPollSeconds),
                out var initialDelay,
                out var initialReason));
        writerSession.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(initialDelay, initialReason);
        Assert.InRange(initialDelay.TotalSeconds, 5, 10);
        Assert.Contains("customx_probe_reuse_window", initialReason, StringComparison.OrdinalIgnoreCase);

        var backgroundWorkStorePath = writerSession.ResolveBackgroundWorkStorePathForTesting();
        Assert.True(File.Exists(backgroundWorkStorePath));
        var backgroundSchedulerRuntimeStorePath = writerSession.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        Assert.True(File.Exists(backgroundSchedulerRuntimeStorePath));

        var toolEvidenceStorePath = Path.Combine(persistenceDirectory, "tool-evidence-cache.json");
        if (File.Exists(toolEvidenceStorePath)) {
            File.Delete(toolEvidenceStorePath);
        }

        var resumedSession = new ChatServiceSession(options, Stream.Null);
        var resumedSummary = resumedSession.BuildBackgroundSchedulerSummaryForTesting();
        Assert.True(resumedSummary.AdaptiveIdleActive);
        Assert.Equal((int)Math.Ceiling(initialDelay.TotalSeconds), resumedSummary.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("customx_probe_reuse_window", resumedSummary.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            resumedSession.TryResolveBackgroundSchedulerAdaptiveIdleDelayForTesting(
                TimeSpan.FromSeconds(options.BackgroundSchedulerPollSeconds),
                out var resumedDelay,
                out var resumedReason));
        Assert.InRange(resumedDelay.TotalSeconds, 5, 10);
        Assert.Contains("customx_probe_reuse_window", resumedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remaining=", resumedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_RehydratesIndependentlyPerStorePath() {
        var (optionsA, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        var (optionsB, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writerA = new ChatServiceSession(optionsA, Stream.Null);
        var writerB = new ChatServiceSession(optionsB, Stream.Null);

        var pathA = writerA.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        var pathB = writerB.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        Assert.NotEqual(pathA, pathB);

        writerA.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(180),
            "policy=path_a");
        writerB.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(420),
            "policy=path_b");

        var resumedA = new ChatServiceSession(optionsA, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();
        var resumedB = new ChatServiceSession(optionsB, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();

        Assert.True(resumedA.AdaptiveIdleActive);
        Assert.Equal(180, resumedA.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("policy=path_a", resumedA.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);

        Assert.True(resumedB.AdaptiveIdleActive);
        Assert.Equal(420, resumedB.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("policy=path_b", resumedB.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_RehydratesLatestPersistedSummaryForSharedStorePath() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writerA = new ChatServiceSession(options, Stream.Null);
        var writerB = new ChatServiceSession(options, Stream.Null);

        writerA.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(90),
            "policy=first_writer");
        writerB.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(240),
            "policy=second_writer");

        var resumed = new ChatServiceSession(options, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();

        Assert.True(resumed.AdaptiveIdleActive);
        Assert.Equal(240, resumed.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("policy=second_writer", resumed.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_PersistsPinnedCamelCaseContract() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writer = new ChatServiceSession(options, Stream.Null);
        writer.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(150),
            "policy=camel_case_contract");

        var runtimeStorePath = writer.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        Assert.True(File.Exists(runtimeStorePath));

        using var document = JsonDocument.Parse(File.ReadAllText(runtimeStorePath));
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("version", out var versionNode));
        Assert.Equal(1, versionNode.GetInt32());
        Assert.True(root.TryGetProperty("lastAdaptiveIdleDelaySeconds", out var delayNode));
        Assert.Equal(150, delayNode.GetInt32());
        Assert.True(root.TryGetProperty("lastAdaptiveIdleReason", out var reasonNode));
        Assert.Equal("policy=camel_case_contract", reasonNode.GetString());
        Assert.False(root.TryGetProperty("Version", out _));
        Assert.False(root.TryGetProperty("LastAdaptiveIdleDelaySeconds", out _));
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_RehydratesLegacyPascalCasePayload() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var session = new ChatServiceSession(options, Stream.Null);
        var runtimeStorePath = session.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        var runtimeDirectory = Path.GetDirectoryName(runtimeStorePath);
        Assert.False(string.IsNullOrWhiteSpace(runtimeDirectory));
        Directory.CreateDirectory(runtimeDirectory!);
        var activeTicks = DateTime.UtcNow.AddSeconds(-15).Ticks;
        File.WriteAllText(
            runtimeStorePath,
            $$"""
            {"Version":1,"LastAdaptiveIdleUtcTicks":{{activeTicks}},"LastAdaptiveIdleDelaySeconds":180,"LastAdaptiveIdleReason":"policy=legacy_pascal","RecentActivity":[]}
            """);

        var resumed = new ChatServiceSession(options, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();

        Assert.False(resumed.RuntimeStoreRehydratePending);
        Assert.Equal("loaded", resumed.RuntimeStoreLoadState);
        Assert.True(resumed.AdaptiveIdleActive);
        Assert.Equal(180, resumed.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("policy=legacy_pascal", resumed.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_RehydratesPersistedRuntimeStorePayload() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writer = new ChatServiceSession(options, Stream.Null);
        writer.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(180),
            "policy=write_roundtrip");

        var runtimeStorePath = writer.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        Assert.True(File.Exists(runtimeStorePath));

        using (var document = JsonDocument.Parse(File.ReadAllText(runtimeStorePath))) {
            var root = document.RootElement;
            Assert.True(root.TryGetProperty("version", out var versionNode));
            Assert.Equal(1, versionNode.GetInt32());
        }

        var resumed = new ChatServiceSession(options, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();

        Assert.False(resumed.RuntimeStoreRehydratePending);
        Assert.Equal("loaded", resumed.RuntimeStoreLoadState);
        Assert.True(resumed.AdaptiveIdleActive);
        Assert.Equal(180, resumed.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("policy=write_roundtrip", resumed.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_IgnoresUnsupportedRuntimeStoreVersion() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var session = new ChatServiceSession(options, Stream.Null);
        var runtimeStorePath = session.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        var runtimeDirectory = Path.GetDirectoryName(runtimeStorePath);
        Assert.False(string.IsNullOrWhiteSpace(runtimeDirectory));
        Directory.CreateDirectory(runtimeDirectory!);
        File.WriteAllText(
            runtimeStorePath,
            """
            {"version":2,"lastAdaptiveIdleUtcTicks":638770000000000000,"lastAdaptiveIdleDelaySeconds":180,"lastAdaptiveIdleReason":"policy=unsupported","recentActivity":[]}
            """);

        var resumed = new ChatServiceSession(options, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();

        Assert.False(resumed.RuntimeStoreRehydratePending);
        Assert.Equal("invalid", resumed.RuntimeStoreLoadState);
        Assert.False(resumed.AdaptiveIdleActive);
        Assert.Equal(0, resumed.LastAdaptiveIdleUtcTicks);
        Assert.Equal(0, resumed.LastAdaptiveIdleDelaySeconds);
        Assert.Equal(string.Empty, resumed.LastAdaptiveIdleReason);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_IgnoresCorruptedRuntimeStorePayload() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var session = new ChatServiceSession(options, Stream.Null);
        var runtimeStorePath = session.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        var runtimeDirectory = Path.GetDirectoryName(runtimeStorePath);
        Assert.False(string.IsNullOrWhiteSpace(runtimeDirectory));
        Directory.CreateDirectory(runtimeDirectory!);
        File.WriteAllText(runtimeStorePath, "{ this is not valid json");

        var resumed = new ChatServiceSession(options, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();

        Assert.False(resumed.RuntimeStoreRehydratePending);
        Assert.Equal("invalid", resumed.RuntimeStoreLoadState);
        Assert.False(resumed.AdaptiveIdleActive);
        Assert.Equal(0, resumed.LastAdaptiveIdleUtcTicks);
        Assert.Equal(0, resumed.LastAdaptiveIdleDelaySeconds);
        Assert.Equal(string.Empty, resumed.LastAdaptiveIdleReason);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_ClearsExpiredAdaptiveIdleMetadataAcrossRestart() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writer = new ChatServiceSession(options, Stream.Null);
        writer.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(30),
            "policy=expired_writer",
            utcTicks: DateTime.UtcNow.AddMinutes(-10).Ticks);

        var resumed = new ChatServiceSession(options, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();

        Assert.False(resumed.AdaptiveIdleActive);
        Assert.Equal(0, resumed.LastAdaptiveIdleUtcTicks);
        Assert.Equal(0, resumed.LastAdaptiveIdleDelaySeconds);
        Assert.Equal(string.Empty, resumed.LastAdaptiveIdleReason);
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_RehydratesAfterStartupLockTimeoutResolves() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writer = new ChatServiceSession(options, Stream.Null);
        writer.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(45),
            "policy=rehydrate_blocked");

        var runtimeStorePath = writer.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        Assert.True(File.Exists(runtimeStorePath));

        ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(
            path => string.Equals(path, runtimeStorePath, StringComparison.OrdinalIgnoreCase) ? false : null);
        try {
            var resumedSession = new ChatServiceSession(options, Stream.Null);
            var blockedSummary = resumedSession.BuildBackgroundSchedulerSummaryForTesting();

            Assert.True(blockedSummary.RuntimeStoreRehydratePending);
            Assert.Equal("deferred", blockedSummary.RuntimeStoreLoadState);
            Assert.False(blockedSummary.AdaptiveIdleActive);
            Assert.Equal(0, blockedSummary.LastAdaptiveIdleUtcTicks);
            Assert.Equal(0, blockedSummary.LastAdaptiveIdleDelaySeconds);
            Assert.Equal(string.Empty, blockedSummary.LastAdaptiveIdleReason);

            ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(null);

            var recoveredSummary = resumedSession.BuildBackgroundSchedulerSummaryForTesting();
            Assert.False(recoveredSummary.RuntimeStoreRehydratePending);
            Assert.Equal("loaded", recoveredSummary.RuntimeStoreLoadState);
            Assert.True(recoveredSummary.AdaptiveIdleActive);
            Assert.Equal(45, recoveredSummary.LastAdaptiveIdleDelaySeconds);
            Assert.Contains("policy=rehydrate_blocked", recoveredSummary.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
        } finally {
            ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(null);
        }
    }

    [Fact]
    public void BuildBackgroundSchedulerSummary_DoesNotRewriteRuntimeStoreWhenDeferredRehydrateRecovers() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var writer = new ChatServiceSession(options, Stream.Null);
        writer.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(45),
            "policy=summary_read_only");

        var runtimeStorePath = writer.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        Assert.True(File.Exists(runtimeStorePath));
        var initialContents = File.ReadAllText(runtimeStorePath);
        var initialWriteUtc = File.GetLastWriteTimeUtc(runtimeStorePath);

        ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(
            path => string.Equals(path, runtimeStorePath, StringComparison.OrdinalIgnoreCase) ? false : null);
        try {
            var resumedSession = new ChatServiceSession(options, Stream.Null);
            var blockedSummary = resumedSession.BuildBackgroundSchedulerSummaryForTesting();

            Assert.True(blockedSummary.RuntimeStoreRehydratePending);
            Assert.Equal("deferred", blockedSummary.RuntimeStoreLoadState);
            Assert.Equal(initialContents, File.ReadAllText(runtimeStorePath));
            Assert.Equal(initialWriteUtc, File.GetLastWriteTimeUtc(runtimeStorePath));

            ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(null);
            Thread.Sleep(1100);

            var recoveredSummary = resumedSession.BuildBackgroundSchedulerSummaryForTesting();

            Assert.False(recoveredSummary.RuntimeStoreRehydratePending);
            Assert.Equal("loaded", recoveredSummary.RuntimeStoreLoadState);
            Assert.Equal(initialContents, File.ReadAllText(runtimeStorePath));
            Assert.Equal(initialWriteUtc, File.GetLastWriteTimeUtc(runtimeStorePath));
        } finally {
            ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(null);
        }
    }

    [Fact]
    public void BackgroundSchedulerRuntimeState_DoesNotPersistWhenLockUnavailable() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();

        var session = new ChatServiceSession(options, Stream.Null);
        var runtimeStorePath = session.ResolveBackgroundSchedulerRuntimeStorePathForTesting();
        if (File.Exists(runtimeStorePath)) {
            File.Delete(runtimeStorePath);
        }

        ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(
            path => string.Equals(path, runtimeStorePath, StringComparison.OrdinalIgnoreCase) ? false : null);
        try {
            session.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
                TimeSpan.FromSeconds(45),
                "policy=write_blocked");
        } finally {
            ChatServiceSession.SetBackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting(null);
        }

        Assert.False(File.Exists(runtimeStorePath));
    }

    [Fact]
    public async Task BackgroundSchedulerRuntimeState_ClearsAdaptiveIdleMetadataAfterWorkResumesAcrossRestart() {
        var (options, _, _) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        const string threadId = "thread-background-scheduler-adaptive-idle-clear-after-work";
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

        var writer = new ChatServiceSession(options, Stream.Null);
        writer.RememberBackgroundSchedulerAdaptiveIdleDecisionForTesting(
            TimeSpan.FromSeconds(45),
            "policy=clear_after_work");
        writer.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        writer.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-clear-after-work",
                    Name = "remote_disk_inventory",
                    ArgumentsJson = """{"computer_name":"srv-clear.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-clear-after-work",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var initialSummary = writer.BuildBackgroundSchedulerSummaryForTesting();
        Assert.True(initialSummary.AdaptiveIdleActive);

        var result = await writer.RunBackgroundSchedulerIterationAsyncForTesting(
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            (scheduledThreadId, toolCall, _) => Task.FromResult<IReadOnlyList<ToolOutputDto>>(new[] {
                new ToolOutputDto {
                    CallId = toolCall.CallId,
                    Ok = true,
                    Output = """{"computer_name":"srv-clear.contoso.com","ok":true}"""
                }
            }));

        Assert.Equal(ChatServiceSession.BackgroundSchedulerIterationOutcomeKind.Completed, result.Outcome);

        var resumed = new ChatServiceSession(options, Stream.Null).BuildBackgroundSchedulerSummaryForTesting();
        Assert.False(resumed.AdaptiveIdleActive);
        Assert.Equal(0, resumed.LastAdaptiveIdleUtcTicks);
        Assert.Equal(0, resumed.LastAdaptiveIdleDelaySeconds);
        Assert.Equal(string.Empty, resumed.LastAdaptiveIdleReason);
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
    public void RememberToolHandoffBackgroundWork_SeedsComputerLifecycleSystemVerificationFromComputerName() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-computer-lifecycle";
        var definitions = new[] {
            CreateDefinition(
                name: "ad_computer_lifecycle",
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
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_object_resolve",
                            TargetRole = ToolRoutingTaxonomy.RoleResolver,
                            FollowUpKind = ToolHandoffFollowUpKinds.Normalization,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "distinguished_name",
                                    TargetArgument = "identity"
                                }
                            }
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_metrics_summary",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Investigation,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_channels_list",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                },
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }),
            new ToolDefinition("ad_object_get", "AD object get", ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties()),
            new ToolDefinition("ad_object_resolve", "AD object resolve", ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties()),
            new ToolDefinition("system_info", "system info", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties()),
            new ToolDefinition("system_metrics_summary", "system metrics", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties()),
            new ToolDefinition("eventlog_channels_list", "event log channels", ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ad-computer-write",
                    Name = "ad_computer_lifecycle",
                    ArgumentsJson = """{"identity":"CN=SRV-SQL-01,OU=Servers,DC=contoso,DC=com","operation":"move","apply":true}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ad-computer-write",
                    Ok = true,
                    Output = """{"ok":true,"identity":"SRV-SQL-01$","distinguished_name":"CN=SRV-SQL-01,OU=Tier0,DC=contoso,DC=com","computer_name":"srv-sql-01.contoso.com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(5, snapshot.ReadyCount);
        Assert.Equal(0, snapshot.QueuedCount);
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"computer_name\":\"srv-sql-01.contoso.com\"", StringComparison.Ordinal));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "system_metrics_summary", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"computer_name\":\"srv-sql-01.contoso.com\"", StringComparison.Ordinal));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"machine_name\":\"srv-sql-01.contoso.com\"", StringComparison.Ordinal));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "ad_object_get", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "ad_object_resolve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsUserLifecycleMembershipVerification() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-user-membership";
        var definitions = new[] {
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
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_object_resolve",
                            TargetRole = ToolRoutingTaxonomy.RoleResolver,
                            FollowUpKind = ToolHandoffFollowUpKinds.Normalization,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "distinguished_name",
                                    TargetArgument = "identity"
                                }
                            }
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_user_groups_resolved",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
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
            new ToolDefinition("ad_object_get", "AD object get", ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties()),
            new ToolDefinition("ad_object_resolve", "AD object resolve", ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties()),
            new ToolDefinition("ad_user_groups_resolved", "AD user groups resolved", ToolSchema.Object(("identity", ToolSchema.String("User identity."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ad-user-write",
                    Name = "ad_user_lifecycle",
                    ArgumentsJson = """{"identity":"alice","operation":"update","groups_to_add":["GG-SQL-Users"],"apply":true}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ad-user-write",
                    Ok = true,
                    Output = """{"ok":true,"identity":"alice","distinguished_name":"CN=Alice Smith,OU=Users,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(3, snapshot.ReadyCount);
        Assert.Equal(0, snapshot.QueuedCount);
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "ad_user_groups_resolved", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"identity\":\"CN=Alice Smith,OU=Users,DC=contoso,DC=com\"", StringComparison.Ordinal));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "ad_object_get", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "ad_object_resolve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsGroupLifecycleMembershipVerification() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-group-lifecycle";
        var definitions = new[] {
            CreateDefinition(
                name: "ad_group_lifecycle",
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
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_object_resolve",
                            TargetRole = ToolRoutingTaxonomy.RoleResolver,
                            FollowUpKind = ToolHandoffFollowUpKinds.Normalization,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "distinguished_name",
                                    TargetArgument = "identity"
                                }
                            }
                        },
                        new ToolHandoffRoute {
                            TargetPackId = "active_directory",
                            TargetToolName = "ad_group_members_resolved",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
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
            new ToolDefinition("ad_object_get", "AD object get", ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties()),
            new ToolDefinition("ad_object_resolve", "AD object resolve", ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties()),
            new ToolDefinition("ad_group_members_resolved", "AD group members resolved", ToolSchema.Object(("identity", ToolSchema.String("Group identity."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ad-group-write",
                    Name = "ad_group_lifecycle",
                    ArgumentsJson = """{"identity":"GG-SQL-Admins","operation":"update","members_to_add":["alice"],"apply":true}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ad-group-write",
                    Ok = true,
                    Output = """{"ok":true,"identity":"GG-SQL-Admins","distinguished_name":"CN=GG-SQL-Admins,OU=Groups,DC=contoso,DC=com"}""",
                    MetaJson = """{"write_applied":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(3, snapshot.ReadyCount);
        Assert.Equal(0, snapshot.QueuedCount);
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "ad_group_members_resolved", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"identity\":\"CN=GG-SQL-Admins,OU=Groups,DC=contoso,DC=com\"", StringComparison.Ordinal));
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
    public void RememberToolHandoffBackgroundWork_UsesNestedSourcePathsForPreparedFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-nested-paths";
        var definitions = new[] {
            CreateDefinition(
                name: "eventlog_timeline_query",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "meta/entity_handoff/computer_candidates/0/value",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition("system_info", "system info", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-eventlog-timeline",
                    Name = "eventlog_timeline_query",
                    ArgumentsJson = """{"machine_name":"srv-eventlog-01.contoso.com","log_name":"Security"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-eventlog-timeline",
                    Ok = true,
                    Output = """{"ok":true,"meta":{"entity_handoff":{"computer_candidates":[{"value":"srv-eventlog-01.contoso.com"}]}}}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("system_info", item.TargetToolName);
        Assert.Contains("\"computer_name\":\"srv-eventlog-01.contoso.com\"", item.PreparedArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsMultiArgumentProbeFollowUpFromProbeMeta() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-eventlog-probe";
        var definitions = new[] {
            CreateDefinition(
                name: "eventlog_connectivity_probe",
                handoff: EventLogContractCatalog.CreateConnectivityProbeHandoffContract()),
            new ToolDefinition(
                "eventlog_top_events",
                "event log top events",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("log_name", ToolSchema.String("Log name.")))
                    .NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-eventlog-probe",
                    Name = "eventlog_connectivity_probe",
                    ArgumentsJson = """{"machine_name":"srv-eventlog-02.contoso.com","log_name":"Security"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-eventlog-probe",
                    Ok = true,
                    Output = """{"ok":true,"probe_status":"healthy"}""",
                    MetaJson = """{"machine_name":"srv-eventlog-02.contoso.com","requested_log_name":"Security"}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"log_name\":\"Security\"", StringComparison.Ordinal)
                                                       && item.PreparedArgumentsJson.Contains("\"machine_name\":\"srv-eventlog-02.contoso.com\"", StringComparison.Ordinal));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_top_events", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"log_name\":\"Security\"", StringComparison.Ordinal)
                                                       && item.PreparedArgumentsJson.Contains("\"machine_name\":\"srv-eventlog-02.contoso.com\"", StringComparison.Ordinal)
                                                       && item.ResultReference.Contains("prepared_arg_log_name=Security", StringComparison.Ordinal)
                                                       && item.ResultReference.Contains("prepared_arg_machine_name=srv-eventlog-02.contoso.com", StringComparison.Ordinal));
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SkipsPartialMultiArgumentProbeFollowUpWhenLogNameMissing() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-eventlog-probe-missing-log";
        var definitions = new[] {
            CreateDefinition(
                name: "eventlog_connectivity_probe",
                handoff: EventLogContractCatalog.CreateConnectivityProbeHandoffContract()),
            new ToolDefinition(
                "eventlog_top_events",
                "event log top events",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("log_name", ToolSchema.String("Log name.")))
                    .NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-eventlog-probe-missing-log",
                    Name = "eventlog_connectivity_probe",
                    ArgumentsJson = """{"machine_name":"srv-eventlog-03.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-eventlog-probe-missing-log",
                    Ok = true,
                    Output = """{"ok":true,"probe_status":"healthy"}""",
                    MetaJson = """{"machine_name":"srv-eventlog-03.contoso.com"}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Empty(snapshot.Items);
        Assert.Equal(0, snapshot.ReadyCount);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsAdProbeEnvironmentDiscoverFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-ad-probe";
        var definitions = new[] {
            CreateDefinition(
                name: "ad_connectivity_probe",
                handoff: ActiveDirectoryContractCatalog.CreateConnectivityProbeHandoff()),
            new ToolDefinition(
                "ad_environment_discover",
                "AD environment discover",
                ToolSchema.Object(
                        ("domain_controller", ToolSchema.String("Domain controller.")),
                        ("search_base_dn", ToolSchema.String("Search base DN.")),
                        ("include_domain_controllers", ToolSchema.Boolean("Include DCs.")),
                        ("max_domain_controllers", ToolSchema.Integer("Max DCs.")),
                        ("include_forest_domains", ToolSchema.Boolean("Include forest domains.")),
                        ("include_trusts", ToolSchema.Boolean("Include trusts.")))
                    .NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-ad-probe",
                    Name = "ad_connectivity_probe",
                    ArgumentsJson = """{"domain_controller":"dc01.contoso.com","search_base_dn":"DC=contoso,DC=com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-ad-probe",
                    Ok = true,
                    Output = """{"ok":true,"probe_status":"healthy"}""",
                    MetaJson = """{"effective_domain_controller":"dc01.contoso.com","effective_search_base_dn":"DC=contoso,DC=com"}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("ad_environment_discover", item.TargetToolName);
        Assert.Contains("\"domain_controller\":\"dc01.contoso.com\"", item.PreparedArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("\"search_base_dn\":\"DC=contoso,DC=com\"", item.PreparedArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsSystemProbeRuntimeFollowUps() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-system-probe";
        var definitions = new[] {
            CreateDefinition(
                name: "system_connectivity_probe",
                handoff: SystemContractCatalog.CreateConnectivityProbeHandoff()),
            new ToolDefinition("system_info", "system info", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties()),
            new ToolDefinition("system_metrics_summary", "system metrics", ToolSchema.Object(("computer_name", ToolSchema.String("Remote computer."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-system-probe",
                    Name = "system_connectivity_probe",
                    ArgumentsJson = """{"computer_name":"srv-runtime-01.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-system-probe",
                    Ok = true,
                    Output = """{"ok":true,"target":"srv-runtime-01.contoso.com","probe_status":"healthy"}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(2, snapshot.ReadyCount);
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"computer_name\":\"srv-runtime-01.contoso.com\"", StringComparison.Ordinal));
        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "system_metrics_summary", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"computer_name\":\"srv-runtime-01.contoso.com\"", StringComparison.Ordinal));
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_UsesSourceToolEvidenceForSetupHelpers() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-handoff-source-helper-credit";
        var definitions = new[] {
            new ToolDefinition(
                "seed_runtime_probe",
                "Seed runtime probe helper.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "custom",
                            TargetToolName = "dependent_diagnostic",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "machine_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "dependent_diagnostic",
                "Inspect runtime after a setup preflight succeeds.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "seed_runtime_probe"
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-runtime-probe",
                    Name = "seed_runtime_probe",
                    ArgumentsJson = """{"machine_name":"srv-runtime-setup-01.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-runtime-probe",
                    Ok = true,
                    Output = """{"ok":true,"machine_name":"srv-runtime-setup-01.contoso.com"}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Contains(snapshot.Items, static item => string.Equals(item.TargetToolName, "dependent_diagnostic", StringComparison.OrdinalIgnoreCase)
                                                       && string.Equals(item.State, "ready", StringComparison.OrdinalIgnoreCase)
                                                       && item.PreparedArgumentsJson.Contains("\"machine_name\":\"srv-runtime-setup-01.contoso.com\"", StringComparison.Ordinal));

        var helperItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "seed_runtime_probe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("completed", helperItem.State);
        Assert.Contains("helper_reuse=source_tool_evidence", helperItem.ResultReference, StringComparison.Ordinal);
        Assert.Contains("dependent_tool=dependent_diagnostic", helperItem.ResultReference, StringComparison.Ordinal);
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
    public void TryBuildReadyBackgroundWorkToolCallForTesting_PrefersProbeHelperFollowUpBeforeDependentAuthTool() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-contract-helper-order";
        var definitions = new[] {
            CreateDefinition(
                name: "seed_eventlog_live_followup",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            CreateDefinition(
                name: "seed_eventlog_probe_followup",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_channels_list",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after runtime profile validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                },
                new() {
                    CallId = "call-probe-followup",
                    Name = "seed_eventlog_probe_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                },
                new() {
                    CallId = "call-probe-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
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

        Assert.Equal("eventlog_channels_list", toolName);
        Assert.Contains("\"machine_name\":\"srv-eventlog.contoso.com\"", argumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsContractHelperPrerequisitesForDependentFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-contract-helper-seeding";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_runtime_profile_validate"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()),
            new ToolDefinition(
                "eventlog_runtime_profile_validate",
                "Validate runtime profile readiness for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        Assert.Equal(2, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.QueuedCount);
        Assert.Equal(3, snapshot.PendingReadOnlyCount);
        var dependentItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queued", dependentItem.State);
        Assert.Equal(2, dependentItem.DependencyItemIds.Length);

        var probeItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"machine_name\":\"srv-eventlog.contoso.com\"", probeItem.PreparedArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("dependent_tool=eventlog_live_query", probeItem.ResultReference, StringComparison.Ordinal);
        Assert.Contains("helper_kind=probe", probeItem.ResultReference, StringComparison.Ordinal);

        var setupItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_runtime_profile_validate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"machine_name\":\"srv-eventlog.contoso.com\"", setupItem.PreparedArgumentsJson, StringComparison.Ordinal);
        Assert.Contains("helper_kind=setup", setupItem.ResultReference, StringComparison.Ordinal);

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var toolName,
            out var argumentsJson,
            out _));

        Assert.Equal("eventlog_channels_list", toolName);
        Assert.Contains("\"machine_name\":\"srv-eventlog.contoso.com\"", argumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SkipsContractHelperSeedWhenRequiredArgumentsCannotBeDerived() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-contract-helper-skip";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.Normal,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs after helper validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_profile_validate"
                }),
            new ToolDefinition(
                "eventlog_profile_validate",
                "Validate runtime profile availability before running the query.",
                ToolSchema.Object(("profile_id", ToolSchema.String("Runtime profile id."))).Required("profile_id").NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal("eventlog_live_query", item.TargetToolName);
        Assert.DoesNotContain(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_profile_validate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RememberBackgroundWorkExecutionOutcomeForTesting_CompletedHelpersPromoteDependentFollowUpWhenAllDependenciesSucceed() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-contract-helper-promotion";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_runtime_profile_validate"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()),
            new ToolDefinition(
                "eventlog_runtime_profile_validate",
                "Validate runtime profile readiness for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var initialSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var dependentItem = Assert.Single(initialSnapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queued", dependentItem.State);

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var probeItemId,
            out var firstToolName,
            out _,
            out _));
        Assert.Equal("eventlog_channels_list", firstToolName);
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            probeItemId,
            "host_background_work_eventlog_channels_list_001",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_eventlog_channels_list_001",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var afterProbeSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        dependentItem = Assert.Single(afterProbeSnapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queued", dependentItem.State);

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var setupItemId,
            out var secondToolName,
            out _,
            out _));
        Assert.Equal("eventlog_runtime_profile_validate", secondToolName);
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            setupItemId,
            "host_background_work_eventlog_runtime_profile_validate_001",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_eventlog_runtime_profile_validate_001",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var promotedSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        dependentItem = Assert.Single(promotedSnapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", dependentItem.State);
        Assert.Equal(1, promotedSnapshot.ReadyCount);

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var promotedToolName,
            out var promotedArgumentsJson,
            out _));
        Assert.Equal("eventlog_live_query", promotedToolName);
        Assert.Contains("\"machine_name\":\"srv-eventlog.contoso.com\"", promotedArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_UsesPackOwnedPreferredProbeAndRecipeHelpersBeforeDependentFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-pack-guidance-helpers";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_customx_followup",
                description: "seed customx follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "customx_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "customx_live_query",
                "Inspect live event logs after pack-owned preflight helpers run.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }),
            new ToolDefinition(
                "customx_connectivity_probe",
                "Validate remote runtime reachability before the live query.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleDiagnostic
                }),
            new ToolDefinition(
                "customx_recipe_resolver",
                "Resolve recipe-scoped runtime details before the live query.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleResolver
                })
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] { new PreferredProbeAndRecipeBackgroundWorkPack() }));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-customx-followup",
                    Name = "seed_customx_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-customx-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var initialSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var dependentItem = Assert.Single(initialSnapshot.Items, static item => string.Equals(item.TargetToolName, "customx_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queued", dependentItem.State);
        Assert.Equal(2, dependentItem.DependencyItemIds.Length);
        Assert.Contains(initialSnapshot.Items, static item => string.Equals(item.TargetToolName, "customx_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(initialSnapshot.Items, static item => string.Equals(item.TargetToolName, "customx_recipe_resolver", StringComparison.OrdinalIgnoreCase));

        var probeItem = Assert.Single(initialSnapshot.Items, static item => string.Equals(item.TargetToolName, "customx_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("helper_kind=probe", probeItem.ResultReference, StringComparison.Ordinal);
        var recipeItem = Assert.Single(initialSnapshot.Items, static item => string.Equals(item.TargetToolName, "customx_recipe_resolver", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("helper_kind=recipe", recipeItem.ResultReference, StringComparison.Ordinal);

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var probeItemId,
            out var firstToolName,
            out _,
            out _));
        Assert.Equal("customx_connectivity_probe", firstToolName);
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            probeItemId,
            "host_background_work_customx_connectivity_probe_001",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_customx_connectivity_probe_001",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var recipeItemId,
            out var secondToolName,
            out _,
            out _));
        Assert.Equal("customx_recipe_resolver", secondToolName);
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            recipeItemId,
            "host_background_work_customx_recipe_resolver_001",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_customx_recipe_resolver_001",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var promotedSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        dependentItem = Assert.Single(promotedSnapshot.Items, static item => string.Equals(item.TargetToolName, "customx_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", dependentItem.State);
        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var promotedToolName,
            out var promotedArgumentsJson,
            out _));
        Assert.Equal("customx_live_query", promotedToolName);
        Assert.Contains("\"machine_name\":\"srv-eventlog.contoso.com\"", promotedArgumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberBackgroundWorkExecutionOutcomeForTesting_FailedHelperKeepsDependentFollowUpQueued() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-contract-helper-failure-block";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var helperItemId,
            out var helperToolName,
            out _,
            out _));
        Assert.Equal("eventlog_channels_list", helperToolName);

        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            helperItemId,
            "host_background_work_eventlog_channels_list_002",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_eventlog_channels_list_002",
                    Ok = false,
                    ErrorCode = "remote_unavailable",
                    Output = """{"ok":false}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var dependentItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("queued", dependentItem.State);

        var helperItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", helperItem.State);
        Assert.Equal(1, snapshot.ReadyCount);
        Assert.Equal(1, snapshot.QueuedCount);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_ReusesFreshHelperEvidenceForMatchingToolAndArguments() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-helper-evidence-reuse";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberThreadToolEvidenceForTesting(
            threadId,
            new[] {
                new ToolCallDto {
                    CallId = "call-cached-probe",
                    Name = "eventlog_channels_list",
                    ArgumentsJson = """{"machine_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-cached-probe",
                    Ok = true,
                    Output = """{"ok":true,"channels":["System"]}""",
                    SummaryMarkdown = "Event log channels are reachable on srv-eventlog.contoso.com."
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var helperItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("completed", helperItem.State);
        Assert.Contains("helper_reuse=cached_tool_evidence", helperItem.ResultReference, StringComparison.Ordinal);

        var dependentItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_live_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ready", dependentItem.State);

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out _,
            out var toolName,
            out var argumentsJson,
            out _));
        Assert.Equal("eventlog_live_query", toolName);
        Assert.Contains("\"machine_name\":\"srv-eventlog.contoso.com\"", argumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_PrefersReadyFollowUpBackedByFreshReusedHelperEvidence() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string freshReuseThreadId = "thread-background-scheduler-fresh-reuse";
        const string executedHelperThreadId = "thread-background-scheduler-executed-helper";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberThreadToolEvidenceForTesting(
            freshReuseThreadId,
            new[] {
                new ToolCallDto {
                    CallId = "call-cached-probe-fresh",
                    Name = "eventlog_channels_list",
                    ArgumentsJson = """{"machine_name":"srv-fresh.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-cached-probe-fresh",
                    Ok = true,
                    Output = """{"ok":true,"channels":["System"]}""",
                    SummaryMarkdown = "Event log channels are reachable on srv-fresh.contoso.com."
                }
            },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberToolHandoffBackgroundWorkForTesting(
            freshReuseThreadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup-fresh",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-fresh.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup-fresh",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        session.RememberToolHandoffBackgroundWorkForTesting(
            executedHelperThreadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup-executed",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-executed.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup-executed",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var executedSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(executedHelperThreadId);
        var executedHelperItem = Assert.Single(executedSnapshot.Items, static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            executedHelperThreadId,
            executedHelperItem.Id,
            "host_background_work_eventlog_channels_list_executed",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_eventlog_channels_list_executed",
                    Ok = true,
                    Output = """{"ok":true,"channels":["System"]}"""
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
        Assert.Equal(freshReuseThreadId, scheduledThreadId);
        Assert.Equal("eventlog_live_query", toolName);
        Assert.Contains("\"machine_name\":\"srv-fresh.contoso.com\"", argumentsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_UsesPackPublishedProbeFreshnessWindowForReuse() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-pack-guided-probe-reuse";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_customx_followup",
                description: "seed custom follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "customx",
                            TargetToolName = "customx_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "customx_live_query",
                "Inspect runtime state after validation.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "customx_connectivity_probe"
                }),
            new ToolDefinition(
                "customx_connectivity_probe",
                "Validate runtime reachability.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            definitions,
            new IToolPack[] { new PackSpecificProbeFreshnessGuidancePack() }));
        session.SeedThreadToolEvidenceEntryForTesting(
            threadId,
            toolName: "customx_connectivity_probe",
            argumentsJson: """{"machine_name":"srv-pack-guided.contoso.com"}""",
            output: """{"ok":true}""",
            summaryMarkdown: "Connectivity probe succeeded.",
            seenUtcTicks: DateTime.UtcNow.AddSeconds(-90).Ticks);

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-customx-followup",
                    Name = "seed_customx_followup",
                    ArgumentsJson = """{"computer_name":"srv-pack-guided.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-customx-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var helperItem = Assert.Single(snapshot.Items, static item => string.Equals(item.TargetToolName, "customx_connectivity_probe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("completed", helperItem.State);
        Assert.Contains("helper_reuse=cached_tool_evidence", helperItem.ResultReference, StringComparison.Ordinal);
        Assert.Contains("helper_reuse_ttl_seconds=120", helperItem.ResultReference, StringComparison.Ordinal);
        Assert.Contains("helper_reuse_policy=customx_probe_reuse_window", helperItem.ResultReference, StringComparison.Ordinal);

        var summary = session.BuildBackgroundSchedulerSummaryForTesting();
        var threadSummary = Assert.Single(summary.ThreadSummaries, static item => string.Equals(item.ThreadId, threadId, StringComparison.Ordinal));
        Assert.Contains("customx_connectivity_probe", threadSummary.ReusedHelperToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("customx_probe_reuse_window", threadSummary.ReusedHelperPolicyNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(120, threadSummary.ReusedHelperFreshestTtlSeconds);
        Assert.Equal(120, threadSummary.ReusedHelperOldestTtlSeconds);
    }

    [Fact]
    public void TryBuildReadyBackgroundWorkToolCallForTesting_ReturnsWaitingOnPrerequisitesReasonWhenOnlyBlockedQueuedItemsRemain() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-waiting-on-prerequisites";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs after prerequisites.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List channels for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        Assert.True(session.TryBuildReadyBackgroundWorkToolCallForTesting(
            threadId,
            "continue",
            definitions,
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            out var helperItemId,
            out var helperToolName,
            out _,
            out _));
        Assert.Equal("eventlog_channels_list", helperToolName);

        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            helperItemId,
            "host_background_work_eventlog_channels_list_003",
            new[] {
                new ToolOutputDto {
                    CallId = "host_background_work_eventlog_channels_list_003",
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
        Assert.Equal("background_work_waiting_on_prerequisites", reason);
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

    [Fact]
    public void TryBuildBackgroundWorkDependencyRecoveryPromptForTesting_PrefersRuntimeAuthContextWhenProbeFailureMatchesBlockedDependentTool() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-dependency-auth-recovery";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        var helperItem = Assert.Single(
            session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, helperItem.Id, "running"));
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            helperItem.Id,
            "call-probe",
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-probe",
                    Ok = false,
                    ErrorCode = "authentication_failed",
                    Error = "Missing runtime profile for remote eventlog access.",
                    Output = """{"ok":false}"""
                }
            });

        var built = session.TryBuildBackgroundWorkDependencyRecoveryPromptForTesting(
            threadId,
            "continue",
            "I can keep going with the prepared follow-up.",
            definitions,
            out var prompt,
            out var reason);

        Assert.True(built);
        Assert.Equal("background_prerequisite_auth_context_required", reason);
        Assert.Contains("runtime_auth_args: profile_id", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("helper_tools: eventlog_channels_list", prompt, StringComparison.OrdinalIgnoreCase);

        var builtBlocker = session.TryBuildBackgroundWorkDependencyRecoveryBlockerTextForTesting(
            threadId,
            "continue",
            "I can keep going with the prepared follow-up.",
            definitions,
            out var blockerText,
            out var blockerReason);

        Assert.True(builtBlocker);
        Assert.Equal("background_prerequisite_auth_context_required", blockerReason);
        Assert.Contains("ix:execution-contract:v1", blockerText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profile_id", blockerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildBackgroundWorkDependencyRecoveryPromptForTesting_PrefersSetupContextWhenSetupHelperFailsValidation() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-dependency-setup-recovery";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_runtime_profile_validate"
                }),
            new ToolDefinition(
                "eventlog_runtime_profile_validate",
                "Validate runtime profile readiness for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        var helperItem = Assert.Single(
            session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "eventlog_runtime_profile_validate", StringComparison.OrdinalIgnoreCase));
        Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, helperItem.Id, "running"));
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            helperItem.Id,
            "call-setup",
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-setup",
                    Ok = false,
                    ErrorCode = "validation_failed",
                    Error = "Runtime profile details were incomplete.",
                    Output = """{"ok":false}"""
                }
            });

        var built = session.TryBuildBackgroundWorkDependencyRecoveryPromptForTesting(
            threadId,
            "continue",
            "I can keep going with the prepared follow-up.",
            definitions,
            out var prompt,
            out var reason);

        Assert.True(built);
        Assert.Equal("background_prerequisite_setup_context_required", reason);
        Assert.Contains("setup_helpers: eventlog_runtime_profile_validate", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildBackgroundWorkDependencyRecoveryPromptForTesting_UsesRetryCooldownReasonForNonAuthHelperFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-dependency-cooldown-recovery";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_eventlog_live_followup",
                description: "seed live event log follow-up",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "eventlog",
                            TargetToolName = "eventlog_live_query",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "computer_name",
                                    TargetArgument = "machine_name"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "eventlog_live_query",
                "Inspect live event logs on a remote machine after validation and setup.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list"
                }),
            new ToolDefinition(
                "eventlog_channels_list",
                "List available event log channels and validate access for the target machine.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties())
        };
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new List<ToolCallDto> {
                new() {
                    CallId = "call-live-followup",
                    Name = "seed_eventlog_live_followup",
                    ArgumentsJson = """{"computer_name":"srv-eventlog.contoso.com"}"""
                }
            },
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-live-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });
        var helperItem = Assert.Single(
            session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, helperItem.Id, "running"));
        session.RememberBackgroundWorkExecutionOutcomeForTesting(
            threadId,
            helperItem.Id,
            "call-probe",
            new List<ToolOutputDto> {
                new() {
                    CallId = "call-probe",
                    Ok = false,
                    ErrorCode = "remote_unavailable",
                    Error = "Remote endpoint was temporarily unavailable.",
                    Output = """{"ok":false}"""
                }
            });

        var built = session.TryBuildBackgroundWorkDependencyRecoveryPromptForTesting(
            threadId,
            "continue",
            "I can keep going with the prepared follow-up.",
            definitions,
            out var prompt,
            out var reason);

        Assert.True(built);
        Assert.Equal("background_prerequisite_retry_cooldown", reason);
        Assert.Contains("cooldown_helpers: eventlog_channels_list", prompt, StringComparison.OrdinalIgnoreCase);

        var builtBlocker = session.TryBuildBackgroundWorkDependencyRecoveryBlockerTextForTesting(
            threadId,
            "continue",
            "I can keep going with the prepared follow-up.",
            definitions,
            out var blockerText,
            out var blockerReason);

        Assert.True(builtBlocker);
        Assert.Equal("background_prerequisite_retry_cooldown", blockerReason);
        Assert.Contains("ix:execution-contract:v1", blockerText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("helper retry is still pending", blockerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsProbeKindSpecificMonitoringFollowUpsForLdap() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-monitoring-ldap";
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        var definitions = registry.GetDefinitions();
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-monitoring-ldap",
                    Name = "ad_monitoring_probe_run",
                    ArgumentsJson = """{"probe_kind":"ldap","domain_controller":"dc1.contoso.com","targets":["dc1.contoso.com"]}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-monitoring-ldap",
                    Ok = true,
                    Output = """{"probe_kind":"ldap","normalized_request":{"domain_controller":"dc1.contoso.com","targets":["dc1.contoso.com"]},"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var targetToolNames = snapshot.Items
            .Select(static item => item.TargetToolName)
            .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
            .ToArray();

        Assert.Contains("system_info", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog_channels_list", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_ldap_diagnostics", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_ldap_policy_posture", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("system_time_sync", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("system_network_client_posture", targetToolNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsDirectoryMonitoringFollowUpsOnlyForMatchingDirectoryProbeKind() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-monitoring-directory-rpc";
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        var definitions = registry.GetDefinitions();
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-monitoring-directory",
                    Name = "ad_monitoring_probe_run",
                    ArgumentsJson = """{"probe_kind":"directory","directory_probe_kind":"rpc_endpoint","domain_controller":"dc2.contoso.com","targets":["dc2.contoso.com"]}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-monitoring-directory",
                    Ok = true,
                    Output = """{"probe_kind":"directory","normalized_request":{"domain_controller":"dc2.contoso.com","targets":["dc2.contoso.com"]},"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var targetToolNames = snapshot.Items
            .Select(static item => item.TargetToolName)
            .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
            .ToArray();

        Assert.Contains("system_info", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog_channels_list", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_ports_list", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_service_list", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ad_ldap_diagnostics", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("system_network_client_posture", targetToolNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsKerberosTransportSplitMonitoringFollowUpsWhenProfileActive() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-monitoring-kerberos-transport-split";
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        var definitions = registry.GetDefinitions();
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-monitoring-kerberos",
                    Name = "ad_monitoring_probe_run",
                    ArgumentsJson = """{"probe_kind":"kerberos","protocol":"both","domain_controller":"dc3.contoso.com","targets":["dc3.contoso.com"]}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-monitoring-kerberos",
                    Ok = true,
                    Output = """{"probe_kind":"kerberos","normalized_request":{"domain_controller":"dc3.contoso.com","targets":["dc3.contoso.com"]},"ok":true}""",
                    MetaJson = """{"active_follow_up_profile_ids":["transport_split"]}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var targetToolNames = snapshot.Items
            .Select(static item => item.TargetToolName)
            .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
            .ToArray();

        Assert.Contains("system_time_sync", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_metrics_summary", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_ports_list", targetToolNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_DoesNotSeedWindowsUpdateInventoryFollowUpWithoutProfile() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-monitoring-windows-update-without-profile";
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        var definitions = registry.GetDefinitions();
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-monitoring-windows-update",
                    Name = "ad_monitoring_probe_run",
                    ArgumentsJson = """{"probe_kind":"windows_update","domain_controller":"dc4.contoso.com","targets":["dc4.contoso.com"]}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-monitoring-windows-update",
                    Ok = true,
                    Output = """{"probe_kind":"windows_update","normalized_request":{"domain_controller":"dc4.contoso.com","targets":["dc4.contoso.com"]},"ok":true}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var targetToolNames = snapshot.Items
            .Select(static item => item.TargetToolName)
            .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
            .ToArray();

        Assert.Contains("system_windows_update_client_status", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_windows_update_telemetry", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_patch_compliance", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("system_updates_installed", targetToolNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RememberToolHandoffBackgroundWork_SeedsWindowsUpdateInventoryFollowUpWhenProfileActive() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-background-work-monitoring-windows-update-with-profile";
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        var definitions = registry.GetDefinitions();
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));

        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-monitoring-windows-update-profile",
                    Name = "ad_monitoring_probe_run",
                    ArgumentsJson = """{"probe_kind":"windows_update","domain_controller":"dc5.contoso.com","targets":["dc5.contoso.com"]}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-monitoring-windows-update-profile",
                    Ok = true,
                    Output = """{"probe_kind":"windows_update","normalized_request":{"domain_controller":"dc5.contoso.com","targets":["dc5.contoso.com"]},"ok":true}""",
                    MetaJson = """{"active_follow_up_profile_ids":["patch_inventory_focus"]}"""
                }
            });

        var snapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        var targetToolNames = snapshot.Items
            .Select(static item => item.TargetToolName)
            .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
            .ToArray();

        Assert.Contains("system_updates_installed", targetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_windows_update_client_status", targetToolNames, StringComparer.OrdinalIgnoreCase);
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
            DependencyItemIds: Array.Empty<string>(),
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

    private sealed class PreferredProbeAndRecipeBackgroundWorkPack : IToolPack, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "eventlog",
            Name = "EventLog",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic background-work guidance."
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                RuntimeCapabilities = new ToolPackRuntimeCapabilitiesModel {
                    PreferredProbeTools = new[] { "customx_connectivity_probe" }
                },
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "customx_runtime_triage",
                        Summary = "Stabilize the remote endpoint before deeper live analysis.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Validate remote runtime reachability.",
                                SuggestedTools = new[] { "customx_connectivity_probe" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Resolve recipe-scoped runtime context.",
                                SuggestedTools = new[] { "customx_recipe_resolver" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Run the main live query.",
                                SuggestedTools = new[] { "customx_live_query" }
                            }
                        }
                    }
                }
            };
        }
    }

    private sealed class PackSpecificProbeFreshnessGuidancePack : IToolPack, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "customx",
            Name = "CustomX",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic probe freshness guidance."
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                RuntimeCapabilities = new ToolPackRuntimeCapabilitiesModel {
                    PreferredProbeTools = new[] { "customx_connectivity_probe" },
                    ProbeHelperFreshnessWindowSeconds = 120,
                    SetupHelperFreshnessWindowSeconds = 600
                }
            };
        }
    }
}
