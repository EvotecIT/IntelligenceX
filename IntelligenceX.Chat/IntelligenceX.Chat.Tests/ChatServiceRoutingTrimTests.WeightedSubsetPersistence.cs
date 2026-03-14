using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo RememberWeightedToolSubsetMethod =
        typeof(ChatServiceSession).GetMethod("RememberWeightedToolSubset", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RememberWeightedToolSubset not found.");

    private static readonly MethodInfo TryGetContinuationToolSubsetMethod =
        typeof(ChatServiceSession).GetMethod("TryGetContinuationToolSubset", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("TryGetContinuationToolSubset not found.");

    [Fact]
    public void TryGetContinuationToolSubset_RehydratesPersistedWeightedSubsetAfterRestart() {
        var pendingActionsStorePath = CreateAllowedPendingActionsStorePath("ix-chat-weighted-subset", out var root);
        const string threadId = "thread-weighted-subset";

        var allTools = new List<ToolDefinition> {
            new("ad_scope_discovery", "AD scope"),
            new("ad_domain_controllers", "AD controllers"),
            new("dnsclientx_query", "DNS query"),
            new("domaindetective_domain_summary", "Domain summary")
        };
        var selectedSubset = allTools.Take(2).ToArray();

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            RememberWeightedToolSubsetMethod.Invoke(session1, new object?[] { threadId, selectedSubset, allTools.Count });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var args = new object?[] { threadId, "run now", allTools, null };
            var resolved = TryGetContinuationToolSubsetMethod.Invoke(session2, args);

            Assert.True(Assert.IsType<bool>(resolved));
            var subset = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
            Assert.Equal(2, subset.Count);
            Assert.Contains(subset, static tool => string.Equals(tool.Name, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(subset, static tool => string.Equals(tool.Name, "ad_domain_controllers", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(subset, static tool => string.Equals(tool.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase));
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
    public void TryGetContinuationToolSubset_DoesNotRehydrateWhenFullToolSetClearedSubsetSnapshot() {
        var pendingActionsStorePath = CreateAllowedPendingActionsStorePath("ix-chat-weighted-subset-clear", out var root);
        const string threadId = "thread-weighted-subset-clear";

        var allTools = new List<ToolDefinition> {
            new("ad_scope_discovery", "AD scope"),
            new("ad_domain_controllers", "AD controllers"),
            new("dnsclientx_query", "DNS query")
        };
        var selectedSubset = allTools.Take(2).ToArray();

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            RememberWeightedToolSubsetMethod.Invoke(session1, new object?[] { threadId, selectedSubset, allTools.Count });
            RememberWeightedToolSubsetMethod.Invoke(session1, new object?[] { threadId, allTools.ToArray(), allTools.Count });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var args = new object?[] { threadId, "run now", allTools, null };
            var resolved = TryGetContinuationToolSubsetMethod.Invoke(session2, args);

            Assert.False(Assert.IsType<bool>(resolved));
            Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(args[3]);
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
