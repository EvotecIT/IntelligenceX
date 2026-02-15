using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
    public void ExpandContinuationUserRequest_ResolvesExplicitActSelectionWhenSinglePendingActionExists() {
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
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_001", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesExplicitOrdinalSelectionWhenSinglePendingActionExists() {
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
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "1" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_001", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("ok:")]
    [InlineData("ok.")]
    [InlineData("ok,")]
    [InlineData("`ok`")]
    [InlineData(" ok! ")]
    [InlineData("ok！")]
    [InlineData("ok。")]
    [InlineData("do   it")]
    [InlineData("ＧＯ")]
    public void ExpandContinuationUserRequest_AutoConfirmsSinglePendingActionForCommonAcknowledgements(string input) {
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
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("ix_action_selection", out var selection));
        Assert.Equal("act_001", selection.GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotAutoConfirmWhenMultiplePendingActionsExist() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pick one:

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001

            [Action]
            ix:action:v1
            id: act_002
            title: Second
            request: Do second thing.
            reply: /act act_002
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var input = "ok";
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Theory]
    [InlineData("why?")]
    [InlineData("ok?")]
    [InlineData("ok？")]
    [InlineData("dalej?")]
    [InlineData("¿dalej")]
    [InlineData("dalej؟")]
    public void ExpandContinuationUserRequest_DoesNotAutoConfirmOnQuestionLikeFollowUps(string input) {
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
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Theory]
    [InlineData("tomorrow")]
    [InlineData("later")]
    [InlineData("wait")]
    [InlineData("thanks")]
    [InlineData("no thanks")]
    [InlineData("no, thanks")]
    [InlineData("no thank you")]
    [InlineData("not now")]
    [InlineData("dont")]
    [InlineData("don't")]
    [InlineData("don't.")]
    [InlineData("don’t")]
    [InlineData("do not")]
    [InlineData("no")]
    [InlineData("no.")]
    [InlineData("no!")]
    [InlineData("no؟")]
    [InlineData("nie")]
    [InlineData("nie!")]
    [InlineData("\"ok\"")]
    [InlineData("{\"a\": 1}")]
    [InlineData("<xml/>")]
    [InlineData("x=y")]
    [InlineData("`code`")]
    public void ExpandContinuationUserRequest_DoesNotAutoConfirmForBenignShortNonConfirmations(string input) {
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
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_RehydratesPendingActionsAfterRestart() {
        var storeFile = $"pending-actions-test-{Guid.NewGuid():N}.json";
        var storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelligenceX.Chat",
            storeFile);
        try {
            var threadId = "thread-001";
            var assistantDraft = """
                Pick one:

                [Action]
                ix:action:v1
                id: act_001
                title: Run forest probe
                request: Run the forest-wide replication and LDAP diagnostics now.
                reply: /act act_001
                """;

            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            var opts = new ServiceOptions { PendingActionsStorePath = storeFile };
            var session1 = new ChatServiceSession(opts, Stream.Null);
            RememberPendingActionsMethod.Invoke(session1, new object?[] { threadId, assistantDraft });

            // "Restart": new session instance with empty in-memory caches.
            var session2 = new ChatServiceSession(opts, Stream.Null);
            var expandedObj = ExpandContinuationUserRequestMethod.Invoke(session2, new object?[] { threadId, "/act act_001" });
            var expanded = Assert.IsType<string>(expandedObj);

            using var doc = JsonDocument.Parse(expanded);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("ix_action_selection", out var selection));
            Assert.Equal("act_001", selection.GetProperty("id").GetString());
            Assert.Contains("forest-wide replication", selection.GetProperty("request").GetString(), StringComparison.OrdinalIgnoreCase);
        } finally {
            if (File.Exists(storePath)) {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotThrowOnCorruptPendingActionsStore() {
        var storeFile = $"pending-actions-test-{Guid.NewGuid():N}.json";
        var storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelligenceX.Chat",
            storeFile);
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            File.WriteAllText(storePath, "{ this is not valid json");

            var opts = new ServiceOptions { PendingActionsStorePath = storeFile };
            var session = new ChatServiceSession(opts, Stream.Null);
            var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
            var expanded = Assert.IsType<string>(result);

            Assert.Equal("/act act_001", expanded);
        } finally {
            if (File.Exists(storePath)) {
                File.Delete(storePath);
            }
        }
    }

    [Fact]
    public void ExpandContinuationUserRequest_PrunesPendingActionsWithInvalidTicks() {
        var storeFile = $"pending-actions-test-{Guid.NewGuid():N}.json";
        var storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelligenceX.Chat",
            storeFile);
        try {
            var threadId = "thread-001";
            var invalidTicks = 0;
            var json = $$"""
                {
                  "Version": 1,
                  "Threads": {
                    "{{threadId}}": {
                      "SeenUtcTicks": {{invalidTicks}},
                      "Actions": [
                        { "Id": "act_001", "Title": "T", "Request": "R" }
                      ]
                    }
                  }
                }
                """;
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            File.WriteAllText(storePath, json);

            var opts = new ServiceOptions { PendingActionsStorePath = storeFile };
            var session = new ChatServiceSession(opts, Stream.Null);
            var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { threadId, "/act act_001" });
            var expanded = Assert.IsType<string>(result);
            Assert.Equal("/act act_001", expanded);

            var updated = File.ReadAllText(storePath);
            Assert.DoesNotContain(threadId, updated, StringComparison.Ordinal);
        } finally {
            if (File.Exists(storePath)) {
                File.Delete(storePath);
            }
        }
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
