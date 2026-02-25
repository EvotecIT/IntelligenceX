using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo ClearToolRoutingCachesMethod =
        typeof(ChatServiceSession).GetMethod("ClearToolRoutingCaches", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ClearToolRoutingCaches not found.");

    [Fact]
    public void ClearToolRoutingCaches_RemovesPersistedRoutingSnapshots() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-clear-caches-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");

        var threadId = "thread-clear-" + Guid.NewGuid().ToString("N");
        var domainThreadId = "thread-clear-domain-" + Guid.NewGuid().ToString("N");

        var allTools = new List<ToolDefinition> {
            new("ad_scope_discovery", "AD scope"),
            new("ad_domain_controllers", "AD controllers"),
            new("dnsclientx_query", "DNS query")
        };
        var subsetTools = new[] { allTools[0], allTools[1] };

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);

            RememberUserIntentMethod.Invoke(session1, new object?[] { threadId, "Please run forest-wide replication diagnostics." });
            RememberWeightedToolSubsetMethod.Invoke(session1, new object?[] { threadId, subsetTools, allTools.Count });
            session1.RememberPendingDomainIntentClarificationRequestForTesting(threadId);
            session1.RememberPreferredDomainIntentFamilyForTesting(
                domainThreadId,
                new[] { new ToolCallDto { CallId = "1", Name = "ad_scope_discovery", ArgumentsJson = "{}" } },
                new[] { new ToolOutputDto { CallId = "1", Output = "{\"ok\":true}", Ok = true } },
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            ClearToolRoutingCachesMethod.Invoke(session1, Array.Empty<object>());

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);

            var expandedFollowUpObj = ExpandContinuationUserRequestMethod.Invoke(session2, new object?[] { threadId, "run now" });
            Assert.Equal("run now", Assert.IsType<string>(expandedFollowUpObj));

            var continuationArgs = new object?[] { threadId, "run now", allTools, null };
            var continuationResolvedObj = TryGetContinuationToolSubsetMethod.Invoke(session2, continuationArgs);
            Assert.False(Assert.IsType<bool>(continuationResolvedObj));

            var clarificationResolved = session2.TryResolvePendingDomainIntentClarificationSelectionForTesting(threadId, "1", out _);
            Assert.False(clarificationResolved);

            var affinityApplied = session2.TryApplyDomainIntentAffinityForTesting(
                domainThreadId,
                new[] { allTools[0], allTools[2] },
                out _,
                out _,
                out _);
            Assert.False(affinityApplied);
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
}
