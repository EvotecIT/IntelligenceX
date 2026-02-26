using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo ClearToolRoutingCachesMethod =
        typeof(ChatServiceSession).GetMethod("ClearToolRoutingCaches", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ClearToolRoutingCaches not found.");
    private static readonly MethodInfo ResolvePendingActionsStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolvePendingActionsStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolvePendingActionsStorePath not found.");
    private static readonly MethodInfo ResolveUserIntentStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveUserIntentStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveUserIntentStorePath not found.");
    private static readonly MethodInfo ResolveDomainIntentStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveDomainIntentStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveDomainIntentStorePath not found.");
    private static readonly MethodInfo ResolveDomainIntentClarificationStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveDomainIntentClarificationStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveDomainIntentClarificationStorePath not found.");
    private static readonly MethodInfo ResolveWeightedSubsetStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveWeightedSubsetStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveWeightedSubsetStorePath not found.");
    private static readonly MethodInfo ResolveStructuredNextActionStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveStructuredNextActionStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveStructuredNextActionStorePath not found.");
    private static readonly MethodInfo ResolveThreadRecoveryAliasStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveThreadRecoveryAliasStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveThreadRecoveryAliasStorePath not found.");
    private static readonly MethodInfo ResolvePlannerThreadContextStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolvePlannerThreadContextStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolvePlannerThreadContextStorePath not found.");
    private static readonly MethodInfo ResolveToolRoutingStatsStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveToolRoutingStatsStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveToolRoutingStatsStorePath not found.");
    private static readonly MethodInfo ResolveWorkingMemoryCheckpointStorePathMethod =
        typeof(ChatServiceSession).GetMethod("ResolveWorkingMemoryCheckpointStorePath", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveWorkingMemoryCheckpointStorePath not found.");

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
        var carryoverToolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "Discover", ToolSchema.Object().NoAdditionalProperties()),
            new(
                "ad_scope_discovery",
                "Scope",
                ToolSchema.Object(
                        ("include_trusts", ToolSchema.Boolean()),
                        ("max_domains", ToolSchema.Integer()))
                    .NoAdditionalProperties())
        };
        var carryoverToolCalls = new List<ToolCallDto> {
            new() { CallId = "call-carryover-1", Name = "ad_environment_discover", ArgumentsJson = "{}" }
        };
        var carryoverToolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-carryover-1",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":false,"arguments":{"include_trusts":"true","max_domains":"2"}}]}
                         """,
                Ok = true
            }
        };
        var carryoverMutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };
        var actionDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: Run failed logons (4625)
            request: Run failed logons report.
            mutating: false
            reply: /act act_001
            """;

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);

            RememberUserIntentMethod.Invoke(session1, new object?[] { threadId, "Please run forest-wide replication diagnostics." });
            RememberWeightedToolSubsetMethod.Invoke(session1, new object?[] { threadId, subsetTools, allTools.Count });
            RememberPendingActionsMethod.Invoke(session1, new object?[] { threadId, actionDraft });
            RememberStructuredNextActionCarryoverMethod.Invoke(
                session1,
                new object?[] { threadId, carryoverToolDefinitions, carryoverToolCalls, carryoverToolOutputs, carryoverMutabilityHints });
            session1.RememberPendingDomainIntentClarificationRequestForTesting(threadId);
            session1.RememberRecoveredThreadAliasForTesting("legacy-thread", threadId);
            session1.RememberPlannerThreadContextForTesting("active-thread", "planner-thread", DateTime.UtcNow.Ticks);
            session1.SetToolRoutingStatsForTesting(
                new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)>(StringComparer.OrdinalIgnoreCase) {
                    ["ad_scope_discovery"] = (DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks)
                });
            session1.PersistToolRoutingStatsForTesting();
            session1.RememberWorkingMemoryCheckpointForTesting(
                threadId,
                "Please run forest-wide replication diagnostics.",
                "ad_domain",
                new[] { "ad_replication_summary" },
                new[] { "ad_replication_summary: all DCs healthy." });
            session1.RememberPreferredDomainIntentFamilyForTesting(
                domainThreadId,
                new[] { new ToolCallDto { CallId = "1", Name = "ad_scope_discovery", ArgumentsJson = "{}" } },
                new[] { new ToolOutputDto { CallId = "1", Output = "{\"ok\":true}", Ok = true } },
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            var pendingPath = Assert.IsType<string>(ResolvePendingActionsStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var userIntentPath = Assert.IsType<string>(ResolveUserIntentStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var domainIntentPath = Assert.IsType<string>(ResolveDomainIntentStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var domainClarificationPath = Assert.IsType<string>(ResolveDomainIntentClarificationStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var weightedSubsetPath = Assert.IsType<string>(ResolveWeightedSubsetStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var structuredNextActionPath = Assert.IsType<string>(ResolveStructuredNextActionStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var threadRecoveryAliasPath = Assert.IsType<string>(ResolveThreadRecoveryAliasStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var plannerThreadContextPath = Assert.IsType<string>(ResolvePlannerThreadContextStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var toolRoutingStatsPath = Assert.IsType<string>(ResolveToolRoutingStatsStorePathMethod.Invoke(session1, Array.Empty<object>()));
            var workingMemoryPath = Assert.IsType<string>(ResolveWorkingMemoryCheckpointStorePathMethod.Invoke(session1, Array.Empty<object>()));

            ClearToolRoutingCachesMethod.Invoke(session1, Array.Empty<object>());

            Assert.False(File.Exists(pendingPath));
            Assert.False(File.Exists(userIntentPath));
            Assert.False(File.Exists(domainIntentPath));
            Assert.False(File.Exists(domainClarificationPath));
            Assert.False(File.Exists(weightedSubsetPath));
            Assert.False(File.Exists(structuredNextActionPath));
            Assert.False(File.Exists(threadRecoveryAliasPath));
            Assert.False(File.Exists(plannerThreadContextPath));
            Assert.False(File.Exists(toolRoutingStatsPath));
            Assert.False(File.Exists(workingMemoryPath));

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);

            var expandedFollowUpObj = ExpandContinuationUserRequestMethod.Invoke(session2, new object?[] { threadId, "run now" });
            Assert.Equal("run now", Assert.IsType<string>(expandedFollowUpObj));

            var continuationArgs = new object?[] { threadId, "run now", allTools, null };
            var continuationResolvedObj = TryGetContinuationToolSubsetMethod.Invoke(session2, continuationArgs);
            Assert.False(Assert.IsType<bool>(continuationResolvedObj));
            var workingMemoryAugmented = session2.TryAugmentRoutedUserRequestFromWorkingMemoryCheckpointForTesting(
                threadId,
                "run now",
                "run now",
                out var workingMemoryRoutedRequest);
            Assert.False(workingMemoryAugmented);
            Assert.Equal("run now", workingMemoryRoutedRequest);

            var clarificationResolved = session2.TryResolvePendingDomainIntentClarificationSelectionForTesting(threadId, "1", out _);
            Assert.False(clarificationResolved);

            var affinityApplied = session2.TryApplyDomainIntentAffinityForTesting(
                domainThreadId,
                new[] { allTools[0], allTools[2] },
                out _,
                out _,
                out _);
            Assert.False(affinityApplied);
            Assert.Equal("legacy-thread", session2.ResolveRecoveredThreadAliasForTesting("legacy-thread"));
            Assert.False(session2.TryResolvePlannerThreadContextForTesting("active-thread", out _));
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
