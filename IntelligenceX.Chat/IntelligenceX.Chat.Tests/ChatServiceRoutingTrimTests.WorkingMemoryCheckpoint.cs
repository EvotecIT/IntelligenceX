using System;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void WorkingMemoryCheckpoint_AugmentsCompactFollowUpAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-working-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-working-memory";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberWorkingMemoryCheckpointForTesting(
                threadId: threadId,
                intentAnchor: "Run AD replication + failed-logon diagnostics across DCs and summarize top risks.",
                domainIntentFamily: "ad_domain",
                recentToolNames: new[] { "ad_replication_summary", "eventlog_live_query" },
                recentEvidenceSnippets: new[] { "ad_replication_summary: replication failures were concentrated on DC02." });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var augmented = session2.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
                threadId,
                userRequest: "run now",
                routedUserRequest: "run now",
                out var routedFromCheckpoint);

            Assert.True(augmented);
            Assert.Contains("ix:working-memory:v1", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("intent_anchor:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("domain_scope_family: ad_domain", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("recent_tools:", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("follow_up: run now", routedFromCheckpoint, StringComparison.OrdinalIgnoreCase);
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
    public void WorkingMemoryCheckpoint_DoesNotAugmentWhenRoutedRequestAlreadyExpanded() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-expanded",
            intentAnchor: "Analyze DNS + AD context and compare anomalies.",
            domainIntentFamily: "public_domain",
            recentToolNames: new[] { "domaindetective_domain_summary" },
            recentEvidenceSnippets: new[] { "domaindetective_domain_summary: SPF and DMARC are valid." });

        var augmented = session.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
            threadId: "thread-working-memory-expanded",
            userRequest: "run now",
            routedUserRequest: "Analyze DNS + AD context and compare anomalies.\nFollow-up: run now",
            out var routedFromCheckpoint);

        Assert.False(augmented);
        Assert.Equal("Analyze DNS + AD context and compare anomalies.\nFollow-up: run now", routedFromCheckpoint);
    }
}
