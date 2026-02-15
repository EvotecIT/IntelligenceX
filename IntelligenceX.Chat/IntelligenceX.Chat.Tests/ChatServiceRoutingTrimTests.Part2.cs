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
    public void ExpandContinuationUserRequest_DoesNotCaptureActionsInsideCodeFence() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            ```text
            [Action]
            ix:action:v1
            id: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            reply: /act act_001
            ```
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("/act act_001", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ReturnsOriginalTextWhenNotAFollowUp() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var input = "  Please check the replication health for this domain today.  ";

        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_PreservesWhitespaceWhenNoIntentCached() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var input = "  run now  ";

        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_RespectsMaxAge() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        RememberUserIntentMethod.Invoke(session, new object?[] { "thread-001", "Please run forest-wide replication." });

        var gate = ToolRoutingContextLockField.GetValue(session)!;
        lock (gate) {
            var ticks = (Dictionary<string, long>)LastUserIntentSeenUtcTicksField.GetValue(session)!;
            ticks["thread-001"] = DateTime.UtcNow.AddHours(-1).Ticks;
        }

        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run now" });
        var expanded = Assert.IsType<string>(result);

        Assert.DoesNotContain("Follow-up:", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("run now", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ConsumesPendingActionsAfterSelection() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pick one:

            [Action]
            ix:action:v1
            id: act_001
            title: Run forest probe
            request: Run the forest-wide replication and LDAP diagnostics now.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });

        var first = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var firstExpanded = Assert.IsType<string>(first);
        Assert.Contains("ix_action_selection", firstExpanded, StringComparison.OrdinalIgnoreCase);

        var second = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var secondExpanded = Assert.IsType<string>(second);
        Assert.Equal("/act act_001", secondExpanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveNonDigitShortFollowUpToOrdinal() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pick one:

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var input = "ok";
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void TrimWeightedRoutingContextsForTesting_PrefersMostRecentTicksAcrossContexts() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        var names = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var seenTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < MaxTrackedWeightedRoutingContexts + 1; i++) {
            var threadId = $"thread-{i:D3}";
            names[threadId] = new[] { $"tool-{i:D3}" };
            seenTicks[threadId] = 10_000L + i;
        }

        // Make thread-000 look old in weighted context...
        seenTicks["thread-000"] = 1;
        session.SetWeightedRoutingContextsForTesting(names, seenTicks);

        // ...but recent in intent context, so it should not be evicted.
        var gate = ToolRoutingContextLockField.GetValue(session)!;
        lock (gate) {
            var intents = (Dictionary<string, string>)LastUserIntentByThreadIdField.GetValue(session)!;
            var intentTicks = (Dictionary<string, long>)LastUserIntentSeenUtcTicksField.GetValue(session)!;
            intents.Clear();
            intentTicks.Clear();
            intents["thread-000"] = "intent";
            intentTicks["thread-000"] = 999_999;
        }

        session.TrimWeightedRoutingContextsForTesting();
        var tracked = new HashSet<string>(session.GetTrackedWeightedRoutingContextThreadIdsForTesting(), StringComparer.Ordinal);

        Assert.Contains("thread-000", tracked);
        Assert.DoesNotContain("thread-001", tracked);
        Assert.Contains("thread-002", tracked);
    }

    [Fact]
    public void TrimWeightedRoutingContextsForTesting_EvictsMissingIntentTickEntriesFirst() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        var gate = ToolRoutingContextLockField.GetValue(session)!;
        lock (gate) {
            var intents = (Dictionary<string, string>)LastUserIntentByThreadIdField.GetValue(session)!;
            var intentTicks = (Dictionary<string, long>)LastUserIntentSeenUtcTicksField.GetValue(session)!;
            intents.Clear();
            intentTicks.Clear();

            for (var i = 0; i < MaxTrackedWeightedRoutingContexts + 1; i++) {
                var threadId = $"thread-{i:D3}";
                intents[threadId] = "intent";
                if (threadId != "thread-000") {
                    intentTicks[threadId] = 10_000L + i;
                }
            }

            // Ensure there's a clear oldest "known" tick so eviction is deterministic.
            intentTicks["thread-001"] = 1;
        }

        session.TrimWeightedRoutingContextsForTesting();
        HashSet<string> tracked;
        lock (gate) {
            var intents = (Dictionary<string, string>)LastUserIntentByThreadIdField.GetValue(session)!;
            tracked = new HashSet<string>(intents.Keys, StringComparer.Ordinal);
        }

        Assert.DoesNotContain("thread-000", tracked);
        Assert.Contains("thread-001", tracked);
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
