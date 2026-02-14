using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Ensures bounded routing caches evict oldest/uninitialized entries first.
/// </summary>
public sealed class ChatServiceRoutingTrimTests {
    private const int MaxTrackedToolRoutingStats = 512;
    private const int MaxTrackedWeightedRoutingContexts = 256;

    private static readonly Type ChatServiceSessionType =
        Type.GetType("IntelligenceX.Chat.Service.ChatServiceSession, IntelligenceX.Chat.Service")
        ?? throw new InvalidOperationException("ChatServiceSession type not found.");

    private static readonly Type ToolRoutingStatsType = ChatServiceSessionType.GetNestedType(
        "ToolRoutingStats",
        BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ToolRoutingStats type not found.");

    private static readonly MethodInfo TrimToolRoutingStatsNoLockMethod = ChatServiceSessionType.GetMethod(
        "TrimToolRoutingStatsNoLock",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("TrimToolRoutingStatsNoLock method not found.");

    private static readonly MethodInfo TrimWeightedRoutingContextsNoLockMethod = ChatServiceSessionType.GetMethod(
        "TrimWeightedRoutingContextsNoLock",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("TrimWeightedRoutingContextsNoLock method not found.");

    private static readonly FieldInfo ToolRoutingStatsField = ChatServiceSessionType.GetField(
        "_toolRoutingStats",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingStats field not found.");

    private static readonly FieldInfo LastWeightedToolNamesByThreadIdField = ChatServiceSessionType.GetField(
        "_lastWeightedToolNamesByThreadId",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_lastWeightedToolNamesByThreadId field not found.");

    private static readonly FieldInfo LastWeightedToolSubsetSeenUtcTicksField = ChatServiceSessionType.GetField(
        "_lastWeightedToolSubsetSeenUtcTicks",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_lastWeightedToolSubsetSeenUtcTicks field not found.");

    [Fact]
    public void TrimToolRoutingStatsNoLock_RemovesNonPositiveTimestampEntriesFirst() {
        var setup = CreateSessionForTrimTests();
        for (var i = 0; i < MaxTrackedToolRoutingStats; i++) {
            setup.ToolRoutingStats.Add($"active-{i:D3}", CreateToolRoutingStats(10_000L + i, 0));
        }

        setup.ToolRoutingStats.Add("stale-zero", CreateToolRoutingStats(0, 0));
        setup.ToolRoutingStats.Add("stale-negative", CreateToolRoutingStats(-50, -50));

        TrimToolRoutingStatsNoLockMethod.Invoke(setup.Session, null);

        Assert.Equal(MaxTrackedToolRoutingStats, setup.ToolRoutingStats.Count);
        Assert.False(setup.ToolRoutingStats.Contains("stale-zero"));
        Assert.False(setup.ToolRoutingStats.Contains("stale-negative"));
        Assert.True(setup.ToolRoutingStats.Contains("active-000"));
        Assert.True(setup.ToolRoutingStats.Contains($"active-{MaxTrackedToolRoutingStats - 1:D3}"));
    }

    [Fact]
    public void TrimWeightedRoutingContextsNoLock_RemovesMissingAndZeroTickEntriesFirst() {
        var setup = CreateSessionForTrimTests();
        for (var i = 0; i < MaxTrackedWeightedRoutingContexts; i++) {
            var threadId = $"thread-{i:D3}";
            setup.WeightedToolNamesByThreadId[threadId] = new[] { $"tool-{i:D3}" };
            setup.WeightedToolSubsetSeenUtcTicks[threadId] = 50_000L + i;
        }

        setup.WeightedToolNamesByThreadId["thread-missing"] = new[] { "tool-missing" };
        setup.WeightedToolNamesByThreadId["thread-zero"] = new[] { "tool-zero" };
        setup.WeightedToolSubsetSeenUtcTicks["thread-zero"] = 0;

        TrimWeightedRoutingContextsNoLockMethod.Invoke(setup.Session, null);

        Assert.Equal(MaxTrackedWeightedRoutingContexts, setup.WeightedToolNamesByThreadId.Count);
        Assert.False(setup.WeightedToolNamesByThreadId.ContainsKey("thread-missing"));
        Assert.False(setup.WeightedToolNamesByThreadId.ContainsKey("thread-zero"));
        Assert.True(setup.WeightedToolNamesByThreadId.ContainsKey("thread-000"));
        Assert.True(setup.WeightedToolNamesByThreadId.ContainsKey($"thread-{MaxTrackedWeightedRoutingContexts - 1:D3}"));
    }

    private static SessionTrimTestState CreateSessionForTrimTests() {
        var session = RuntimeHelpers.GetUninitializedObject(ChatServiceSessionType);

        var routingStats = (IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), ToolRoutingStatsType),
            StringComparer.OrdinalIgnoreCase)!
            ?? throw new InvalidOperationException("Failed to create tool routing stats dictionary.");

        var weightedToolNamesByThreadId = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var weightedToolSubsetSeenUtcTicks = new Dictionary<string, long>(StringComparer.Ordinal);

        ToolRoutingStatsField.SetValue(session, routingStats);
        LastWeightedToolNamesByThreadIdField.SetValue(session, weightedToolNamesByThreadId);
        LastWeightedToolSubsetSeenUtcTicksField.SetValue(session, weightedToolSubsetSeenUtcTicks);

        return new SessionTrimTestState(
            session,
            routingStats,
            weightedToolNamesByThreadId,
            weightedToolSubsetSeenUtcTicks);
    }

    private static object CreateToolRoutingStats(long lastUsedUtcTicks, long lastSuccessUtcTicks) {
        var stats = Activator.CreateInstance(ToolRoutingStatsType)
            ?? throw new InvalidOperationException("Failed to create ToolRoutingStats.");

        SetToolRoutingStatsProperty(stats, "LastUsedUtcTicks", lastUsedUtcTicks);
        SetToolRoutingStatsProperty(stats, "LastSuccessUtcTicks", lastSuccessUtcTicks);
        return stats;
    }

    private static void SetToolRoutingStatsProperty(object stats, string propertyName, long value) {
        var property = ToolRoutingStatsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property {propertyName} not found on ToolRoutingStats.");

        property.SetValue(stats, value);
    }

    private readonly record struct SessionTrimTestState(
        object Session,
        IDictionary ToolRoutingStats,
        Dictionary<string, string[]> WeightedToolNamesByThreadId,
        Dictionary<string, long> WeightedToolSubsetSeenUtcTicks);
}
