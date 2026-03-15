using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
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
