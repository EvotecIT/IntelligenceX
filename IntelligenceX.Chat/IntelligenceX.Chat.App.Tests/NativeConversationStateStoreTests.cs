using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards native conversation persistence through the shared desktop state owner.
/// </summary>
public sealed class NativeConversationStateStoreTests {
    /// <summary>
    /// Verifies conversation identity, service thread context, and transcript messages survive a store round trip.
    /// </summary>
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsConversationThreadsAndMessages() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            var first = new NativeConversation(
                "chat-one",
                "Directory review",
                "thread-one",
                DateTime.UtcNow.AddMinutes(-1),
                new[] {
                    new NativeChatTranscriptItem("user", "Review directory risk", DateTimeOffset.UtcNow.AddMinutes(-2)),
                    new NativeChatTranscriptItem("assistant", "One risk found", DateTimeOffset.UtcNow.AddMinutes(-1), "Complete")
                });
            var second = new NativeConversation("chat-two", "M365 review", "thread-two");

            await using (var store = new NativeConversationStateStore(path)) {
                await store.SaveAsync(
                    new NativeConversationWorkspace(new[] { first, second }, first.Id),
                    CancellationToken.None);
            }

            await using var reloadedStore = new NativeConversationStateStore(path);
            var loaded = await reloadedStore.LoadAsync(CancellationToken.None);

            Assert.Equal(first.Id, loaded.ActiveConversationId);
            var conversation = Assert.Single(loaded.Conversations, item => item.Id == first.Id);
            Assert.Equal("thread-one", conversation.ThreadId);
            Assert.Equal(new[] { "Review directory risk", "One risk found" }, conversation.Messages.Select(item => item.Text));
            Assert.Equal("Complete", conversation.Messages[1].Status);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Verifies the native adapter does not overwrite settings or the reserved system conversation.
    /// </summary>
    [Fact]
    public async Task SaveAsync_PreservesUnrelatedProfileStateAndSystemConversation() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ThemePreset = "dark",
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-system", Title = "System" }
                        }
                    },
                    CancellationToken.None);
            }

            await using (var nativeStore = new NativeConversationStateStore(path)) {
                var loaded = await nativeStore.LoadAsync(CancellationToken.None);
                await nativeStore.SaveAsync(loaded, CancellationToken.None);
            }

            using var verifier = new ChatAppStateStore(path);
            var state = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal("dark", state!.ThemePreset);
            Assert.Contains(state.Conversations, item => item.Id == "chat-system");
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ix-native-conversations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
    }
}
