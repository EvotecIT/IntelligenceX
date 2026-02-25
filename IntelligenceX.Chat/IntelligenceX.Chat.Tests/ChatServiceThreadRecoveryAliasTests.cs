using System;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceThreadRecoveryAliasTests {
    [Fact]
    public void ResolveRecoveredThreadAlias_ReturnsOriginal_WhenNoAliasExists() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        var resolved = session.ResolveRecoveredThreadAliasForTesting("thread-a");

        Assert.Equal("thread-a", resolved);
    }

    [Fact]
    public void ResolveRecoveredThreadAlias_FollowsAliasChain_ToLatestRecoveredThread() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberRecoveredThreadAliasForTesting("thread-a", "thread-b");
        session.RememberRecoveredThreadAliasForTesting("thread-b", "thread-c");

        var resolved = session.ResolveRecoveredThreadAliasForTesting("thread-a");

        Assert.Equal("thread-c", resolved);
    }

    [Fact]
    public void ResolveRecoveredThreadAlias_DropsExpiredAliasEntries() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.RememberRecoveredThreadAliasForTesting(
            originalThreadId: "thread-old",
            recoveredThreadId: "thread-new",
            seenUtcTicks: DateTime.UtcNow.AddHours(-18).Ticks);

        var resolved = session.ResolveRecoveredThreadAliasForTesting("thread-old");

        Assert.Equal("thread-old", resolved);
    }

    [Fact]
    public void RememberRecoveredThreadAlias_EvictsOldestAliasInsteadOfClearingAll() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var start = DateTime.UtcNow.AddHours(-6);
        for (var i = 0; i < 260; i++) {
            session.RememberRecoveredThreadAliasForTesting(
                originalThreadId: "thread-" + i,
                recoveredThreadId: "recovered-" + i,
                seenUtcTicks: start.AddSeconds(i).Ticks);
        }

        var oldestResolved = session.ResolveRecoveredThreadAliasForTesting("thread-0");
        var newestResolved = session.ResolveRecoveredThreadAliasForTesting("thread-259");

        Assert.Equal("thread-0", oldestResolved);
        Assert.Equal("recovered-259", newestResolved);
    }
}
