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
    /// Ensures service startup cannot silently replace an unloaded profile with defaults.
    /// </summary>
    [Fact]
    public async Task CreateServiceLaunchProfileOptions_BeforeLoadThrows() {
        var directory = CreateTemporaryDirectory();
        try {
            await using var store = new NativeConversationStateStore(Path.Combine(directory, "app-state.db"));

            var exception = Assert.Throws<InvalidOperationException>(store.CreateServiceLaunchProfileOptions);

            Assert.Contains("must be loaded", exception.Message, StringComparison.OrdinalIgnoreCase);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

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

    /// <summary>
    /// Verifies native service startup uses provider settings from the selected persisted profile.
    /// </summary>
    [Fact]
    public async Task CreateServiceLaunchProfileOptions_UsesLoadedProfileState() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "operations",
                    new ChatAppState {
                        ProfileName = "operations",
                        LocalProviderTransport = "compatible-http",
                        LocalProviderBaseUrl = "http://127.0.0.1:1234/v1",
                        LocalProviderModel = "operations-model",
                        LocalProviderReasoningEffort = "high"
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path, "operations");
            _ = await nativeStore.LoadAsync(CancellationToken.None);
            var options = nativeStore.CreateServiceLaunchProfileOptions();

            Assert.Equal("operations", options.LoadProfileName);
            Assert.Equal("operations", options.SaveProfileName);
            Assert.Equal("compatible-http", options.OpenAITransport);
            Assert.Equal("http://127.0.0.1:1234/v1", options.OpenAIBaseUrl);
            Assert.Equal("operations-model", options.Model);
            Assert.Equal("high", options.ReasoningEffort);
            Assert.True(options.OpenAIAllowInsecureHttp);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures stale empty drafts are collapsed without removing real conversations.
    /// </summary>
    [Fact]
    public async Task LoadAsync_CollapsesDuplicateEmptyDrafts() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ActiveConversationId = "chat-empty-old",
                        Conversations = new List<ChatConversationState> {
                            new() {
                                Id = "chat-real",
                                Title = "Real",
                                Messages = new List<ChatMessageState> {
                                    new() { Role = "user", Text = "Keep me" }
                                }
                            },
                            new() { Id = "chat-empty-new", Title = "New Chat", UpdatedUtc = DateTime.UtcNow },
                            new() { Id = "chat-empty-old", Title = "New Chat", UpdatedUtc = DateTime.UtcNow.AddMinutes(-1) }
                        }
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);

            Assert.Equal(2, workspace.Conversations.Count);
            Assert.Single(workspace.Conversations, conversation => conversation.IsEmptyDraft);
            Assert.Contains(workspace.Conversations, conversation => conversation.Id == "chat-real");
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
