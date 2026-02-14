using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Ensures bounded routing caches evict oldest/uninitialized entries first.
/// </summary>
public sealed class ChatServiceRoutingTrimTests {
    private const int MaxTrackedToolRoutingStats = 512;
    private const int MaxTrackedWeightedRoutingContexts = 256;
    private static readonly MethodInfo LooksLikeContinuationFollowUpMethod =
        typeof(ChatServiceSession).GetMethod("LooksLikeContinuationFollowUp", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("LooksLikeContinuationFollowUp not found.");
    private static readonly MethodInfo ShouldAttemptToolExecutionNudgeMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptToolExecutionNudge", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptToolExecutionNudge not found.");
    private static readonly MethodInfo ExtractPrimaryUserRequestMethod =
        typeof(ChatServiceSession).GetMethod("ExtractPrimaryUserRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractPrimaryUserRequest not found.");
    private static readonly MethodInfo RememberUserIntentMethod =
        typeof(ChatServiceSession).GetMethod("RememberUserIntent", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RememberUserIntent not found.");
    private static readonly MethodInfo ExpandContinuationUserRequestMethod =
        typeof(ChatServiceSession).GetMethod("ExpandContinuationUserRequest", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ExpandContinuationUserRequest not found.");
    private static readonly MethodInfo ParsePlannerSelectedDefinitionsMethod =
        typeof(ChatServiceSession).GetMethod("ParsePlannerSelectedDefinitions", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ParsePlannerSelectedDefinitions not found.");

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

    [Fact]
    public void UpdateToolRoutingStats_TracksOutputsWhenCallIdsDifferOnlyByWhitespace() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var calls = new List<ToolCall> {
            new("  call-001  ", "ad_replication_summary", null, null, new JsonObject())
        };
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-001",
                Output = "{\"ok\":true}",
                Ok = true
            }
        };

        var updateMethod = typeof(ChatServiceSession).GetMethod("UpdateToolRoutingStats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(updateMethod);

        updateMethod!.Invoke(session, new object[] { calls, outputs });

        var names = session.GetTrackedToolRoutingStatNamesForTesting();
        Assert.Contains(names, static name => string.Equals(name, "ad_replication_summary", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("run now")]
    [InlineData("go for it")]
    [InlineData("do it")]
    [InlineData("yes run it")]
    [InlineData("dzialaj")]
    [InlineData("uruchom to")]
    [InlineData("dalej?")]
    [InlineData("继续")]
    [InlineData("继续执行")]
    public void LooksLikeContinuationFollowUp_RecognizesCompactFollowUpsAcrossLanguages(string userText) {
        var result = LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userText });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForDeferredDraftWithoutToolCalls() {
        var userRequest = "run now?";
        var assistantDraft = "If you say \"run now\", I'll execute forest-wide checks immediately.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, true });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForExplicitCapabilityBlocker() {
        var userRequest = "Get top 5 events from ADO system log.";
        var assistantDraft = "I can't query remote ADO live logs directly without machine access.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, true });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForGratitudeFollowUps() {
        var userRequest = "thanks";
        var assistantDraft = "You're welcome.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, true });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ExtractPrimaryUserRequest_StripsCodeFencesAndInlineCode() {
        var input = """
            Please check this:
            ```powershell
            Get-EventLog -LogName System
            ```
            and also `C:\Temp\ADO-System.evtx`
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.DoesNotContain("Get-EventLog", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("C:\\Temp\\ADO-System.evtx", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Please check this:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("and also", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_DoesNotDropIntentWhenFenceUnclosedAfterIntent() {
        var input = """
            Please run the checks first.
            ```powershell
            Get-EventLog -LogName System
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Contains("Please run the checks first.", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-EventLog", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_DoesNotConcatenateTokensWhenBackticksAreOdd() {
        var input = "please `run now";

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Contains("run now", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractPrimaryUserRequest_ReturnsEmptyForAllCodeMessages() {
        var input = """
            ```powershell
            Get-EventLog -LogName System
            ```
            """;

        var result = ExtractPrimaryUserRequestMethod.Invoke(null, new object?[] { input });
        var text = Assert.IsType<string>(result);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ExpandContinuationUserRequest_IncludesLastIntent() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        RememberUserIntentMethod.Invoke(session, new object?[] { "thread-001", "Please run forest-wide replication and LDAP diagnostics." });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run now" });
        var expanded = Assert.IsType<string>(result);

        Assert.Contains("forest-wide replication", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Follow-up:", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run now", expanded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParsePlannerSelectedDefinitions_ParsesStrictJsonToolNames() {
        var defs = BuildPlannerTestDefinitions();
        var plannerText = "{\"tool_names\":[\"ad_replication_summary\",\"eventlog_live_query\"]}";

        var result = ParsePlannerSelectedDefinitionsMethod.Invoke(
            null,
            new object?[] { plannerText, defs, 4 });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Collection(
            selected,
            item => Assert.Equal("ad_replication_summary", item.Name),
            item => Assert.Equal("eventlog_live_query", item.Name));
    }

    [Fact]
    public void ParsePlannerSelectedDefinitions_ParsesFencedJsonAndHonorsLimit() {
        var defs = BuildPlannerTestDefinitions();
        var plannerText = """
            ```json
            {"tool_names":["eventlog_live_query","system_services_list","ad_replication_summary"]}
            ```
            """;

        var result = ParsePlannerSelectedDefinitionsMethod.Invoke(
            null,
            new object?[] { plannerText, defs, 2 });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Equal(2, selected.Count);
        Assert.Equal("eventlog_live_query", selected[0].Name);
        Assert.Equal("system_services_list", selected[1].Name);
    }

    private static IReadOnlyList<ToolDefinition> BuildPlannerTestDefinitions() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        return new[] {
            new ToolDefinition("ad_replication_summary", "AD replication summary", schema),
            new ToolDefinition("eventlog_live_query", "Event log live query", schema),
            new ToolDefinition("system_services_list", "System services list", schema)
        };
    }
}
