using System;
using System.Collections.Generic;
using System.IO;
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
