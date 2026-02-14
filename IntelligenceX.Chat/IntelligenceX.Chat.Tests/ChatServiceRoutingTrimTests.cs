using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Ensures bounded routing caches evict oldest/uninitialized entries first.
/// </summary>
public sealed class ChatServiceRoutingTrimTests {
    private const int MaxTrackedToolRoutingStats = 512;
    private const int MaxTrackedWeightedRoutingContexts = 256;

    [Fact]
    public void TrimToolRoutingStatsForTesting_RemovesNonPositiveTimestampEntriesFirst() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        var stats = new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < MaxTrackedToolRoutingStats; i++) {
            stats[$"active-{i:D3}"] = (10_000L + i, 0);
        }

        stats["stale-zero"] = (0, 0);
        stats["stale-negative"] = (-50, -50);

        session.SetToolRoutingStatsForTesting(stats);
        session.TrimToolRoutingStatsForTesting();

        var names = new HashSet<string>(session.GetTrackedToolRoutingStatNamesForTesting(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(MaxTrackedToolRoutingStats, names.Count);
        Assert.DoesNotContain("stale-zero", names);
        Assert.DoesNotContain("stale-negative", names);
        Assert.Contains("active-000", names);
        Assert.Contains($"active-{MaxTrackedToolRoutingStats - 1:D3}", names);
    }

    [Fact]
    public void TrimWeightedRoutingContextsForTesting_RemovesMissingAndZeroTickEntriesFirst() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        var names = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var seenTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < MaxTrackedWeightedRoutingContexts; i++) {
            var threadId = $"thread-{i:D3}";
            names[threadId] = new[] { $"tool-{i:D3}" };
            seenTicks[threadId] = 50_000L + i;
        }

        names["thread-missing"] = new[] { "tool-missing" };
        names["thread-zero"] = new[] { "tool-zero" };
        seenTicks["thread-zero"] = 0;

        session.SetWeightedRoutingContextsForTesting(names, seenTicks);
        session.TrimWeightedRoutingContextsForTesting();

        var trackedThreadIds = new HashSet<string>(session.GetTrackedWeightedRoutingContextThreadIdsForTesting(), StringComparer.Ordinal);

        Assert.Equal(MaxTrackedWeightedRoutingContexts, trackedThreadIds.Count);
        Assert.DoesNotContain("thread-missing", trackedThreadIds);
        Assert.DoesNotContain("thread-zero", trackedThreadIds);
        Assert.Contains("thread-000", trackedThreadIds);
        Assert.Contains($"thread-{MaxTrackedWeightedRoutingContexts - 1:D3}", trackedThreadIds);
    }
}
