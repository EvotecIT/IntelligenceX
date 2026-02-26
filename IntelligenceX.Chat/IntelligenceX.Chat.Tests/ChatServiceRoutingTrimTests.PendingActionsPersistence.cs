using System;
using System.IO;
using System.Text.Json;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ExpandContinuationUserRequest_RehydratesPendingActionsAfterRestart() {
        var (opts, storePath, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        try {
            var threadId = "thread-001";
            var assistantDraft = """
                If you say "run now", I'll execute it.

                [Action]
                ix:action:v1
                id: act_001
                title: Run forest probe
                request: Run the forest-wide replication and LDAP diagnostics now.
                mutating: false
                reply: /act act_001
                """;

            var session1 = new ChatServiceSession(opts, Stream.Null);
            RememberPendingActionsMethod.Invoke(session1, new object?[] { threadId, assistantDraft });

            // "Restart": new session instance with empty in-memory caches.
            var session2 = new ChatServiceSession(opts, Stream.Null);
            var expandedObj = ExpandContinuationUserRequestMethod.Invoke(session2, new object?[] { threadId, "run now" });
            var expanded = Assert.IsType<string>(expandedObj);

            using var doc = JsonDocument.Parse(expanded);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("ix_action_selection", out var selection));
            Assert.Equal("act_001", selection.GetProperty("id").GetString());
            Assert.Contains(
                "forest-wide replication",
                selection.GetProperty("request").GetString(),
                StringComparison.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(persistenceDirectory)) {
                Directory.Delete(persistenceDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotConfirmCtaAfterRestartWhenMultiplePendingActionsExist() {
        var (opts, _, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        try {
            var threadId = "thread-001";
            var assistantDraft = """
                If you say "run now", I'll execute it.

                [Action]
                ix:action:v1
                id: act_001
                title: First
                request: Do first thing.
                mutating: false
                reply: /act act_001

                [Action]
                ix:action:v1
                id: act_002
                title: Second
                request: Do second thing.
                mutating: false
                reply: /act act_002
                """;

            var session1 = new ChatServiceSession(opts, Stream.Null);
            RememberPendingActionsMethod.Invoke(session1, new object?[] { threadId, assistantDraft });

            var session2 = new ChatServiceSession(opts, Stream.Null);
            var expandedObj = ExpandContinuationUserRequestMethod.Invoke(session2, new object?[] { threadId, "run now" });
            var expanded = Assert.IsType<string>(expandedObj);

            Assert.Equal("run now", expanded);
        } finally {
            if (Directory.Exists(persistenceDirectory)) {
                Directory.Delete(persistenceDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExpandContinuationUserRequest_RehydratesPendingActionsAfterRestart_WhenPersistedCtaTokenHasWrappers() {
        var (opts, storePath, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        try {
            var threadId = "thread-001";
            var ticks = DateTime.UtcNow.Ticks;
            var json = $$"""
                {
                  "Version": 1,
                  "Threads": {
                    "{{threadId}}": {
                      "SeenUtcTicks": {{ticks}},
                      "CallToActionTokens": [ "\"run now\"", "run now." ],
                      "Actions": [
                        {
                          "Id": "act_001",
                          "Title": "First",
                          "Request": "Do first thing.",
                          "Mutating": false
                        }
                      ]
                    }
                  }
                }
                """;

            File.WriteAllText(storePath, json);

            var session = new ChatServiceSession(opts, Stream.Null);
            var expandedObj = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { threadId, "run now" });
            var expanded = Assert.IsType<string>(expandedObj);

            using var doc = JsonDocument.Parse(expanded);
            Assert.Equal(
                "act_001",
                doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
        } finally {
            if (Directory.Exists(persistenceDirectory)) {
                Directory.Delete(persistenceDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotConfirmWhenUserMatchesNonCtaQuotedPhrase() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var assistantDraft = """
            Note: this is not a CTA. The text "run now" appeared inside an error message.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            mutating: false
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var input = "run now";
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotThrowOnCorruptPendingActionsStore() {
        var (opts, storePath, persistenceDirectory) = ChatServiceTestSessionFactory.CreateIsolatedPersistenceOptions();
        try {
            File.WriteAllText(storePath, "{ this is not valid json");

            var session = new ChatServiceSession(opts, Stream.Null);
            var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
            var expanded = Assert.IsType<string>(result);

            Assert.Equal("/act act_001", expanded);
        } finally {
            if (Directory.Exists(persistenceDirectory)) {
                Directory.Delete(persistenceDirectory, recursive: true);
            }
        }
    }
}
