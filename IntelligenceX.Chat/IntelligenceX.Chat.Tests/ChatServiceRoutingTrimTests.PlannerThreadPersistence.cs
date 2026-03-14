using System;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void TryResolvePlannerThreadContextForTesting_RehydratesPersistedPlannerThreadAfterRestart() {
        var pendingActionsStorePath = CreateAllowedPendingActionsStorePath("ix-chat-planner-thread", out var root);

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberPlannerThreadContextForTesting("active-thread-1", "planner-thread-1", DateTime.UtcNow.Ticks);

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var resolved = session2.TryResolvePlannerThreadContextForTesting("active-thread-1", out var plannerThreadId);

            Assert.True(resolved);
            Assert.Equal("planner-thread-1", plannerThreadId);
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
    public void TryResolvePlannerThreadContextForTesting_DoesNotRehydrateExpiredPlannerThreadSnapshot() {
        var pendingActionsStorePath = CreateAllowedPendingActionsStorePath("ix-chat-planner-thread-expired", out var root);

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberPlannerThreadContextForTesting(
                "active-thread-expired",
                "planner-thread-expired",
                DateTime.UtcNow.AddHours(-12).Ticks);

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var resolved = session2.TryResolvePlannerThreadContextForTesting("active-thread-expired", out _);

            Assert.False(resolved);
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
