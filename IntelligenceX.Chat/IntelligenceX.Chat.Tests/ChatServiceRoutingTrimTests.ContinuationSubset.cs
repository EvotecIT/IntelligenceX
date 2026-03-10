using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void TryGetContinuationToolSubset_ReusesSubsetForGenericContinuationFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-generic";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = "continue";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Equal(2, subset.Count);
        Assert.Equal("dnsclientx_query", subset[0].Name);
        Assert.Equal("dnsclientx_ping", subset[1].Name);
    }

    [Fact]
    public void TryGetContinuationToolSubset_ReusesSubsetForFocusedLongQuestionFollowUpFromWorkingMemory() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-focused-question";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        const string userRequest = "Where is ADRODC in the full forest replication table above, and why are those rows still missing from it?";

        Assert.False(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run forest-wide replication and LDAP diagnostics.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest rows still omit ADRODC." },
            priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
            priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
            priorAnswerPlanPrimaryArtifact: "table",
            enabledPackIds: new[] { "active_directory" },
            routingFamilies: new[] { "ad_domain" },
            healthyToolNames: new[] { "dnsclientx_query", "dnsclientx_ping" });

        var result = session.TryGetContinuationToolSubsetForTesting(threadId, userRequest, allDefinitions, out var subset);

        Assert.True(result);
        Assert.Equal(2, subset.Count);
        Assert.Equal("dnsclientx_query", subset[0].Name);
        Assert.Equal("dnsclientx_ping", subset[1].Name);
    }

    [Fact]
    public void TryGetContinuationToolSubset_SkipsSubsetWhenFollowUpMentionsToolOutsideSubset() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-explicit-tool";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = "continue with eventlog_live_query";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Empty(subset);
    }

    [Fact]
    public void TryGetContinuationToolSubset_SkipsSubsetWhenFollowUpMentionsEscapedMarkdownToolOutsideSubset() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-explicit-tool-escaped";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = @"continue with `eventlog\_live\_query`";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Empty(subset);
    }

    [Fact]
    public void TryGetContinuationToolSubset_SkipsSubsetForMultiTokenFollowUpQuestion() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-followup-question";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = "go ahead, but do you have event log tools?";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Empty(subset);
    }

    [Fact]
    public void TryGetContinuationToolSubset_SkipsSubsetForUnrelatedLongQuestionEvenWithWorkingMemory() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-unrelated-question";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        const string userRequest = "Which firewall ports should I open for LDAP and Kerberos troubleshooting in another environment?";

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run forest-wide replication and LDAP diagnostics.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest rows still omit ADRODC." },
            priorAnswerPlanUserGoal: "Summarize the forest replication state in a table.",
            priorAnswerPlanUnresolvedNow: "Explain why ADRODC is absent from the forest replication rows.",
            priorAnswerPlanPrimaryArtifact: "table");

        var result = session.TryGetContinuationToolSubsetForTesting(threadId, userRequest, allDefinitions, out var subset);

        Assert.False(result);
        Assert.Empty(subset);
    }

    [Fact]
    public void TryGetContinuationToolSubset_ReusesSubsetForShortAcknowledgementQuestion() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-short-question";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };
        var userRequest = "go ahead?";

        Assert.True(Assert.IsType<bool>(LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userRequest })));

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });

        var args = new object?[] { threadId, userRequest, allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Equal(2, subset.Count);
        Assert.Equal("dnsclientx_query", subset[0].Name);
        Assert.Equal("dnsclientx_ping", subset[1].Name);
    }

    [Fact]
    public void TryGetContinuationToolSubset_SkipsSubsetWhenContinuationFocusPrefersCachedEvidenceReuse() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-subset-cache-reuse";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var previousSubset = new List<ToolDefinition> {
            allDefinitions[0],
            allDefinitions[1]
        };

        RememberWeightedToolSubsetMethod.Invoke(session, new object?[] { threadId, previousSubset, allDefinitions.Count });
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Continue from the same forest replication evidence.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: forest replication is healthy for AD0, AD1, and AD2." },
            priorAnswerPlanUserGoal: "Continue from the same forest replication evidence.",
            priorAnswerPlanUnresolvedNow: string.Empty,
            priorAnswerPlanPreferCachedEvidenceReuse: true,
            priorAnswerPlanCachedEvidenceReuseReason: "compact continuation should reuse the latest forest replication evidence snapshot",
            priorAnswerPlanPrimaryArtifact: "prose");

        var result = session.TryGetContinuationToolSubsetForTesting(threadId, "continue replication AD2", allDefinitions, out var subset);

        Assert.False(result);
        Assert.Empty(subset);
    }

    [Fact]
    public void TryGetContinuationToolSubset_ReusesCapabilitySnapshotWhenWeightedSubsetMissing() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-capability-subset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-continuation-capability-snapshot";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberWorkingMemoryCheckpointForTesting(
                threadId: threadId,
                intentAnchor: "continue dns diagnostics",
                domainIntentFamily: "public_domain",
                recentToolNames: Array.Empty<string>(),
                recentEvidenceSnippets: Array.Empty<string>(),
                enabledPackIds: new[] { "dnsclientx" },
                routingFamilies: new[] { "public_domain" },
                healthyToolNames: new[] { "dnsclientx_query", "dnsclientx_ping" });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);

            var args = new object?[] { threadId, "continue", allDefinitions, null };
            var result = TryGetContinuationToolSubsetMethod.Invoke(session2, args);

            Assert.True(Assert.IsType<bool>(result));
            var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
            Assert.Equal(2, subset.Count);
            Assert.Equal("dnsclientx_query", subset[0].Name);
            Assert.Equal("dnsclientx_ping", subset[1].Name);
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }

    [Fact]
    public void TryGetContinuationToolSubset_DoesNotReuseStaleCapabilitySnapshot() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-capability-subset-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-continuation-capability-snapshot-stale";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var staleSeenUtcTicks = DateTime.UtcNow.Subtract(TimeSpan.FromDays(2)).Ticks;

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberWorkingMemoryCheckpointForTesting(
                threadId: threadId,
                intentAnchor: "continue dns diagnostics",
                domainIntentFamily: "public_domain",
                recentToolNames: Array.Empty<string>(),
                recentEvidenceSnippets: Array.Empty<string>(),
                enabledPackIds: new[] { "dnsclientx" },
                routingFamilies: new[] { "public_domain" },
                healthyToolNames: new[] { "dnsclientx_query", "dnsclientx_ping" },
                seenUtcTicks: staleSeenUtcTicks);

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);

            var args = new object?[] { threadId, "continue", allDefinitions, null };
            var result = TryGetContinuationToolSubsetMethod.Invoke(session2, args);

            Assert.False(Assert.IsType<bool>(result));
            var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
            Assert.Empty(subset);
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }

    [Fact]
    public void TryGetContinuationToolSubset_UsesCapabilitySnapshotWhenWeightedSubsetIsStale() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-weighted-stale-capability-fresh";
        var allDefinitions = BuildContinuationSubsetTestToolDefinitions();
        var staleSeenUtcTicks = DateTime.UtcNow.Subtract(TimeSpan.FromDays(2)).Ticks;

        session.SetWeightedRoutingContextsForTesting(
            new Dictionary<string, string[]>(StringComparer.Ordinal) {
                [threadId] = new[] { "dnsclientx_query", "dnsclientx_ping" }
            },
            new Dictionary<string, long>(StringComparer.Ordinal) {
                [threadId] = staleSeenUtcTicks
            });

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "continue dns diagnostics",
            domainIntentFamily: "public_domain",
            recentToolNames: Array.Empty<string>(),
            recentEvidenceSnippets: Array.Empty<string>(),
            enabledPackIds: new[] { "dnsclientx" },
            routingFamilies: new[] { "public_domain" },
            healthyToolNames: new[] { "dnsclientx_query", "dnsclientx_ping" },
            seenUtcTicks: DateTime.UtcNow.Ticks);

        var args = new object?[] { threadId, "continue", allDefinitions, null };
        var result = TryGetContinuationToolSubsetMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
        Assert.Equal(2, subset.Count);
        Assert.Equal("dnsclientx_query", subset[0].Name);
        Assert.Equal("dnsclientx_ping", subset[1].Name);
    }

    [Fact]
    public void TryGetContinuationToolSubset_UsesStructuredNextActionHintsFromCapabilitySnapshot() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-continuation-structured-next-action";
        var allDefinitions = new List<ToolDefinition> {
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
                }),
            new(
                "system_info",
                "Inspect system identity.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Computer"))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Run LDAP health checks for the same domain controllers.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_monitoring_probe_run" },
            recentEvidenceSnippets: new[] { "ad_monitoring_probe_run: LDAP health is green but certificate details are still missing." },
            priorAnswerPlanUserGoal: "Check LDAP health and certificate status for the same domain controllers.",
            priorAnswerPlanUnresolvedNow: "Inspect the LDAP certificates for the same domain controllers.",
            priorAnswerPlanRequiresLiveExecution: true,
            priorAnswerPlanMissingLiveEvidence: "ldap certificate details",
            enabledPackIds: new[] { "active_directory", "system" },
            routingFamilies: new[] { "ad_domain" },
            healthyToolNames: new[] { "ad_monitoring_probe_run" });

        session.RememberStructuredNextActionCarryoverForTesting(
            threadId,
            allDefinitions,
            new List<ToolCallDto> {
                new() { CallId = "call-ldap", Name = "ad_monitoring_probe_run" }
            },
            new List<ToolOutputDto> {
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

        var result = session.TryGetContinuationToolSubsetForTesting(
            threadId,
            "can you check the ldap certificates now?",
            allDefinitions,
            out var subset);

        Assert.True(result);
        Assert.Contains(subset, tool => string.Equals(tool.Name, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(subset, tool => string.Equals(tool.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase));
    }

    private static List<ToolDefinition> BuildContinuationSubsetTestToolDefinitions() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        return new List<ToolDefinition> {
            new("dnsclientx_query", "dns query", schema),
            new("dnsclientx_ping", "dns ping", schema),
            new("eventlog_live_query", "eventlog query", schema),
            new("eventlog_top_events", "eventlog top", schema)
        };
    }
}
