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
            var persistedMessageTime = DateTime.UtcNow.AddMinutes(-1);
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ThemePreset = "dark",
                        ActiveConversationId = "chat-existing",
                        ThreadId = "thread-original",
                        Messages = new List<ChatMessageState> {
                            new() {
                                Role = "assistant",
                                Text = "Existing answer",
                                TimeUtc = persistedMessageTime,
                                Model = "gpt-test"
                            }
                        },
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-system", Title = "System" },
                            new() {
                                Id = "chat-existing",
                                Title = "Existing conversation",
                                RuntimeLabel = "OpenAI",
                                ModelLabel = "gpt-test",
                                ModelOverride = "gpt-test",
                                PendingAssistantQuestionHint = "Confirm the directory scope",
                                Messages = new List<ChatMessageState> {
                                    new() {
                                        Role = "assistant",
                                        Text = "Existing answer",
                                        TimeUtc = persistedMessageTime,
                                        Model = "gpt-test"
                                    }
                                },
                                PendingActions = new List<ChatPendingActionState> {
                                    new() {
                                        Id = "pending-one",
                                        Title = "Confirm",
                                        Request = "Confirm the directory scope",
                                        Reply = "Use the current forest"
                                    }
                                }
                            }
                        }
                    },
                    CancellationToken.None);
            }

            await using (var nativeStore = new NativeConversationStateStore(path)) {
                var loaded = await nativeStore.LoadAsync(CancellationToken.None);

                using (var concurrentStore = new ChatAppStateStore(path)) {
                    var concurrentlyUpdated = await concurrentStore.GetAsync("default", CancellationToken.None);
                    Assert.NotNull(concurrentlyUpdated);
                    concurrentlyUpdated!.ThemePreset = "light";
                    var concurrentlyEdited = Assert.Single(
                        concurrentlyUpdated.Conversations,
                        item => item.Id == "chat-existing");
                    concurrentlyEdited.Title = "Updated by legacy shell";
                    concurrentlyEdited.ThreadId = "thread-legacy";
                    concurrentlyEdited.Messages = new List<ChatMessageState> {
                        new() {
                            Role = "assistant",
                            Text = "Newer legacy answer",
                            TimeUtc = DateTime.UtcNow,
                            Model = "gpt-legacy"
                        }
                    };
                    concurrentlyEdited.UpdatedUtc = DateTime.UtcNow;
                    concurrentlyUpdated.ThreadId = concurrentlyEdited.ThreadId;
                    concurrentlyUpdated.Messages = concurrentlyEdited.Messages.Select(message => new ChatMessageState {
                        Role = message.Role,
                        Text = message.Text,
                        TimeUtc = message.TimeUtc,
                        Model = message.Model
                    }).ToList();
                    concurrentlyUpdated.Conversations.Add(new ChatConversationState {
                        Id = "chat-external",
                        Title = "Added after native load"
                    });
                    await concurrentStore.UpsertAsync("default", concurrentlyUpdated, CancellationToken.None);
                }

                await nativeStore.SaveAsync(loaded, CancellationToken.None);
            }

            using var verifier = new ChatAppStateStore(path);
            var state = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal("light", state!.ThemePreset);
            Assert.Contains(state.Conversations, item => item.Id == "chat-system");
            Assert.Contains(state.Conversations, item => item.Id == "chat-external");
            var existing = Assert.Single(state.Conversations, item => item.Id == "chat-existing");
            Assert.Equal("OpenAI", existing.RuntimeLabel);
            Assert.Equal("gpt-test", existing.ModelLabel);
            Assert.Equal("gpt-test", existing.ModelOverride);
            Assert.Equal("Confirm the directory scope", existing.PendingAssistantQuestionHint);
            Assert.Equal("Updated by legacy shell", existing.Title);
            Assert.Equal("thread-legacy", existing.ThreadId);
            Assert.Equal("Newer legacy answer", Assert.Single(existing.Messages).Text);
            Assert.Equal("gpt-legacy", Assert.Single(existing.Messages).Model);
            Assert.Equal("pending-one", Assert.Single(existing.PendingActions).Id);
            Assert.Equal("thread-legacy", state.ThreadId);
            Assert.Equal("Newer legacy answer", Assert.Single(state.Messages).Text);
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
    /// Ensures native turns use persisted profile controls and conversation-specific model selection.
    /// </summary>
    [Fact]
    public async Task CreateChatRequestOptions_UsesLoadedProfileAndConversationModel() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        LocalProviderModel = "profile-model",
                        LocalProviderReasoningEffort = "high",
                        DisabledTools = ["ad_user_disable"],
                        Conversations = new List<ChatConversationState> {
                            new() {
                                Id = "chat-one",
                                Title = "Directory task",
                                ModelOverride = "conversation-model"
                            }
                        }
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);
            var options = nativeStore.CreateChatRequestOptions(
                Assert.Single(workspace.Conversations, item => item.Id == "chat-one"));

            Assert.Equal("conversation-model", options.Model);
            Assert.Equal("high", options.ReasoningEffort);
            Assert.NotNull(options.DisabledTools);
            Assert.Equal(["ad_user_disable"], options.DisabledTools!);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures the native shell resolves stale cloud defaults through the cached local runtime catalog.
    /// </summary>
    [Fact]
    public async Task CreateChatRequestOptions_UsesCachedLocalRuntimeModel() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        LocalProviderTransport = "compatible-http",
                        LocalProviderBaseUrl = "http://127.0.0.1:1234/v1",
                        LocalProviderModel = "gpt-5.4",
                        CachedModelsTransport = "compatible-http",
                        CachedModelsBaseUrl = "http://127.0.0.1:1234/v1/",
                        CachedModels = new List<IntelligenceX.Chat.Abstractions.Protocol.ModelInfoDto> {
                            new() { Id = "local/secondary", Model = "local/secondary" },
                            new() { Id = "local/default", Model = "local/default", IsDefault = true }
                        },
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-local", Title = "Local chat" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);
            var options = nativeStore.CreateChatRequestOptions(
                Assert.Single(workspace.Conversations, item => item.Id == "chat-local"));

            Assert.Equal("local/default", options.Model);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures legacy top-level transcripts are migrated into the conversation collection on first native save.
    /// </summary>
    [Fact]
    public async Task SaveAsync_PersistsConversationCreatedFromLegacyTopLevelTranscript() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ThreadId = "legacy-thread",
                        Messages = new List<ChatMessageState> {
                            new() { Role = "user", Text = "Legacy question" }
                        }
                    },
                    CancellationToken.None);
            }

            await using (var nativeStore = new NativeConversationStateStore(path)) {
                var workspace = await nativeStore.LoadAsync(CancellationToken.None);
                await nativeStore.SaveAsync(workspace, CancellationToken.None);
            }

            using var verifier = new ChatAppStateStore(path);
            var state = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(state);
            var conversation = Assert.Single(state!.Conversations);
            Assert.Equal("legacy-thread", conversation.ThreadId);
            Assert.Equal("Legacy question", Assert.Single(conversation.Messages).Text);
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

            await nativeStore.SaveAsync(workspace, CancellationToken.None);
            using var verifier = new ChatAppStateStore(path);
            var state = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(state);
            Assert.DoesNotContain(state!.Conversations, conversation => conversation.Id == "chat-empty-old");
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
