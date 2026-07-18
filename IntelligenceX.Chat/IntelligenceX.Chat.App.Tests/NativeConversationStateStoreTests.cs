using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.Abstractions.Policy;
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
    /// Ensures a profile written before explicit runtime ownership keeps its migrated authority on the first native save.
    /// </summary>
    [Fact]
    public async Task SaveAsync_LegacyRuntimeProfilePersistsMigratedOverrideAuthority() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var bootstrap = new ChatAppStateStore(path)) {
            }
            SeedLegacyRuntimeProfile(path);

            await using var store = new NativeConversationStateStore(path);
            var workspace = await store.LoadAsync(CancellationToken.None);
            var conversation = Assert.Single(workspace.Conversations);

            await store.SaveAsync(workspace, CancellationToken.None);
            var options = store.CreateChatRequestOptions(conversation);

            Assert.Equal("legacy-model", options.Model);
            Assert.Equal("high", options.ReasoningEffort);
            using var verifier = new ChatAppStateStore(path);
            var persisted = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.True(persisted!.LocalProviderRuntimeOverrideActive);
            Assert.True(persisted.LocalProviderRuntimeOverrideActiveWasPresent);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures an atomic save promotes a legacy row discovered after this native store loaded a fresh profile.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ConcurrentlyDiscoveredLegacyProfileKeepsMigratedAuthority() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            await using var store = new NativeConversationStateStore(path);
            var workspace = await store.LoadAsync(CancellationToken.None);
            SeedLegacyRuntimeProfile(path);

            await store.SaveAsync(workspace, CancellationToken.None);

            using var verifier = new ChatAppStateStore(path);
            var persisted = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.True(persisted!.LocalProviderRuntimeOverrideActive);
            Assert.True(persisted.LocalProviderRuntimeOverrideActiveWasPresent);
            Assert.True(persisted.LocalProviderImageGenerationOverrideActive);
            Assert.True(persisted.LocalProviderImageGenerationOverrideActiveWasPresent);
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

                var liveConversation = Assert.Single(loaded.Conversations, item => item.Id == "chat-existing");
                Assert.Equal("Updated by legacy shell", liveConversation.Title);
                Assert.Equal("thread-legacy", liveConversation.ThreadId);
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
    /// Ensures simultaneous native and legacy turns retain both transcript branches instead of using last-writer-wins.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MergesConcurrentNativeAndLegacyConversationTurns() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            var baselineTime = DateTime.UtcNow.AddMinutes(-5);
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ActiveConversationId = "chat-shared",
                        Conversations = new List<ChatConversationState> {
                            new() {
                                Id = "chat-shared",
                                Title = "Shared chat",
                                ThreadId = "thread-baseline",
                                UpdatedUtc = baselineTime,
                                Messages = new List<ChatMessageState> {
                                    new() { Role = "user", Text = "baseline", TimeUtc = baselineTime }
                                }
                            }
                        }
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);
            var nativeConversation = Assert.Single(workspace.Conversations);
            var nativeTime = DateTime.UtcNow.AddSeconds(-2);
            nativeConversation.Title = "Native title";
            nativeConversation.Messages.Add(new NativeChatTranscriptItem(
                "assistant",
                "native answer",
                new DateTimeOffset(nativeTime, TimeSpan.Zero),
                "Complete",
                "native-model"));
            nativeConversation.ThreadId = "thread-native";
            nativeConversation.UpdatedUtc = nativeTime;

            var legacyTime = DateTime.UtcNow.AddSeconds(-1);
            using (var concurrentStore = new ChatAppStateStore(path)) {
                var concurrentState = await concurrentStore.GetAsync("default", CancellationToken.None);
                Assert.NotNull(concurrentState);
                var legacyConversation = Assert.Single(concurrentState!.Conversations);
                legacyConversation.Title = "Legacy title";
                legacyConversation.Messages.Add(new ChatMessageState {
                    Role = "assistant",
                    Text = "legacy answer",
                    TimeUtc = legacyTime,
                    Model = "legacy-model"
                });
                legacyConversation.ThreadId = "thread-legacy";
                legacyConversation.UpdatedUtc = legacyTime;
                await concurrentStore.UpsertAsync("default", concurrentState, CancellationToken.None);
            }

            await nativeStore.SaveAsync(workspace, CancellationToken.None);

            using var verifier = new ChatAppStateStore(path);
            var state = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(state);
            var merged = Assert.Single(state!.Conversations);
            Assert.Equal("Legacy title", merged.Title);
            Assert.Equal("thread-legacy", merged.ThreadId);
            Assert.Equal(
                new[] { "baseline", "native answer", "legacy answer" },
                merged.Messages.Select(message => message.Text));
            Assert.Equal("native-model", merged.Messages[1].Model);
            Assert.Equal("legacy-model", merged.Messages[2].Model);
            Assert.Equal(merged.Messages.Select(message => message.Text), state.Messages.Select(message => message.Text));
            Assert.Equal(merged.Title, nativeConversation.Title);
            Assert.Equal(merged.ThreadId, nativeConversation.ThreadId);
            Assert.Equal(merged.UpdatedUtc, nativeConversation.UpdatedUtc);
            Assert.Equal(legacyTime, nativeConversation.UpdatedUtc);
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
                        LocalProviderRuntimeOverrideActive = true,
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
                        LocalProviderRuntimeOverrideActive = true,
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
    /// Ensures settings written by the shared workspace replace the native shell's cached per-turn profile state.
    /// </summary>
    [Fact]
    public async Task ReloadProfileStateAsync_UsesLatestSharedSettingsWithoutReplacingWorkspace() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ThemePreset = "default",
                        LocalProviderRuntimeOverrideActive = true,
                        LocalProviderModel = "before-settings",
                        LocalProviderReasoningEffort = "low",
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-settings", Title = "Settings" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);
            var conversation = Assert.Single(workspace.Conversations);
            Assert.Equal("before-settings", nativeStore.CreateChatRequestOptions(conversation).Model);

            using (var sharedStore = new ChatAppStateStore(path)) {
                var state = await sharedStore.GetAsync("default", CancellationToken.None);
                Assert.NotNull(state);
                state!.ThemePreset = "graphite";
                state.LocalProviderModel = "after-settings";
                state.LocalProviderReasoningEffort = "high";
                state.DisabledTools = ["ad_user_disable"];
                await sharedStore.UpsertAsync("default", state, CancellationToken.None);
            }

            string? publishedTheme = null;
            nativeStore.EffectiveThemeChanged += theme => publishedTheme = theme;
            await nativeStore.ReloadProfileStateAsync(CancellationToken.None);
            var options = nativeStore.CreateChatRequestOptions(conversation);

            Assert.Equal("after-settings", options.Model);
            Assert.Equal("high", options.ReasoningEffort);
            Assert.NotNull(options.DisabledTools);
            Assert.Equal(["ad_user_disable"], options.DisabledTools!);
            Assert.Equal("graphite", nativeStore.EffectiveThemePreset);
            Assert.Equal("graphite", publishedTheme);
            Assert.Same(conversation, Assert.Single(workspace.Conversations));
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures a profile selected in the shared Settings workspace becomes the native request and history owner.
    /// </summary>
    [Fact]
    public async Task SelectProfile_LoadsSelectedProfileRequestStateAndWorkspace() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        LocalProviderRuntimeOverrideActive = true,
                        LocalProviderModel = "default-model",
                        Conversations = [new ChatConversationState { Id = "chat-default", Title = "Default" }]
                    },
                    CancellationToken.None);
                await sharedStore.UpsertAsync(
                    "operations",
                    new ChatAppState {
                        LocalProviderRuntimeOverrideActive = true,
                        LocalProviderModel = "operations-model",
                        Conversations = [new ChatConversationState { Id = "chat-operations", Title = "Operations" }]
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var initial = await nativeStore.LoadAsync(CancellationToken.None);
            Assert.Equal("default-model", nativeStore.CreateChatRequestOptions(Assert.Single(initial.Conversations)).Model);

            Assert.True(nativeStore.SelectProfile("operations"));
            Assert.False(nativeStore.SelectProfile("operations"));
            Assert.Equal("default", nativeStore.ActiveProfileName);
            var selected = await nativeStore.LoadAsync(CancellationToken.None);
            var conversation = Assert.Single(selected.Conversations);

            Assert.Equal("operations", nativeStore.ActiveProfileName);
            Assert.Equal("chat-operations", conversation.Id);
            Assert.Equal("operations-model", nativeStore.CreateChatRequestOptions(conversation).Model);
            Assert.Equal("operations", nativeStore.CreateServiceLaunchProfileOptions().LoadProfileName);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures a canceled profile load leaves the previous profile as the request and persistence owner.
    /// </summary>
    [Fact]
    public async Task SelectProfile_CanceledLoadKeepsPreviousProfileActive() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        LocalProviderRuntimeOverrideActive = true,
                        LocalProviderModel = "default-model",
                        Conversations = [new ChatConversationState { Id = "chat-default", Title = "Default" }]
                    },
                    CancellationToken.None);
                await sharedStore.UpsertAsync(
                    "operations",
                    new ChatAppState {
                        LocalProviderRuntimeOverrideActive = true,
                        LocalProviderModel = "operations-model",
                        Conversations = [new ChatConversationState { Id = "chat-operations", Title = "Operations" }]
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);
            Assert.True(nativeStore.SelectProfile("operations"));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => nativeStore.LoadAsync(canceled.Token));

            Assert.Equal("default", nativeStore.ActiveProfileName);
            Assert.True(nativeStore.SelectProfile("operations"));
            var conversation = Assert.Single(workspace.Conversations);
            Assert.Equal("default-model", nativeStore.CreateChatRequestOptions(conversation).Model);
            await nativeStore.SaveAsync(workspace, CancellationToken.None);
            using var verificationStore = new ChatAppStateStore(path);
            var operations = Assert.IsType<ChatAppState>(
                await verificationStore.GetAsync("operations", CancellationToken.None));
            Assert.Equal("chat-operations", Assert.Single(operations.Conversations).Id);
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
                        LocalProviderRuntimeOverrideActive = true,
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

    /// <summary>
    /// Ensures a draft collapsed by native load is retained if another shell populates it before save.
    /// </summary>
    [Fact]
    public async Task SaveAsync_PreservesCollapsedDraftPopulatedConcurrently() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-empty-new", Title = "New Chat", UpdatedUtc = DateTime.UtcNow },
                            new() { Id = "chat-empty-old", Title = "New Chat", UpdatedUtc = DateTime.UtcNow.AddMinutes(-1) }
                        }
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);
            Assert.Single(workspace.Conversations);

            using (var concurrentStore = new ChatAppStateStore(path)) {
                var state = await concurrentStore.GetAsync("default", CancellationToken.None);
                Assert.NotNull(state);
                var populated = Assert.Single(state!.Conversations, conversation => conversation.Id == "chat-empty-old");
                populated.Title = "Legacy follow-up";
                populated.ThreadId = "thread-legacy";
                populated.Messages.Add(new ChatMessageState { Role = "user", Text = "keep me" });
                populated.PendingActions.Add(new ChatPendingActionState {
                    Id = "pending-legacy",
                    Title = "Confirm",
                    Request = "keep",
                    Reply = "yes"
                });
                populated.UpdatedUtc = DateTime.UtcNow;
                await concurrentStore.UpsertAsync("default", state, CancellationToken.None);
            }

            await nativeStore.SaveAsync(workspace, CancellationToken.None);

            using var verifier = new ChatAppStateStore(path);
            var saved = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(saved);
            var preserved = Assert.Single(saved!.Conversations, conversation => conversation.Id == "chat-empty-old");
            Assert.Equal("keep me", Assert.Single(preserved.Messages).Text);
            Assert.Equal("pending-legacy", Assert.Single(preserved.PendingActions).Id);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures legacy provider aliases are repaired before the native sidecar is configured.
    /// </summary>
    [Theory]
    [InlineData("ollama", "http://127.0.0.1:11434")]
    [InlineData("lmstudio", "http://127.0.0.1:1234/v1")]
    [InlineData("http", "http://127.0.0.1:11434")]
    [InlineData("local", "http://127.0.0.1:11434")]
    public async Task CreateServiceLaunchProfileOptions_NormalizesLegacyProviderAliases(
        string alias,
        string expectedBaseUrl) {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState { LocalProviderTransport = alias, LocalProviderBaseUrl = null },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            _ = await nativeStore.LoadAsync(CancellationToken.None);
            var options = nativeStore.CreateServiceLaunchProfileOptions();

            Assert.Equal("compatible-http", options.OpenAITransport);
            Assert.Equal(expectedBaseUrl, options.OpenAIBaseUrl);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures failed and canceled assistant outcomes survive a native history round trip.
    /// </summary>
    [Theory]
    [InlineData("Canceled")]
    [InlineData("Error")]
    public async Task SaveAndLoadAsync_RoundTripsAssistantOutcomeStatus(string status) {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            var conversation = new NativeConversation(
                "chat-status",
                "Outcome",
                messages: new[] {
                    new NativeChatTranscriptItem("assistant", "Outcome text", DateTimeOffset.UtcNow, status)
                });
            await using (var store = new NativeConversationStateStore(path)) {
                await store.SaveAsync(
                    new NativeConversationWorkspace(new[] { conversation }, conversation.Id),
                    CancellationToken.None);
            }

            await using var reloaded = new NativeConversationStateStore(path);
            var workspace = await reloaded.LoadAsync(CancellationToken.None);

            Assert.Equal(status, Assert.Single(Assert.Single(workspace.Conversations).Messages).Status);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures native chat uses the shared prompt and response protocol instead of exposing private blocks.
    /// </summary>
    [Fact]
    public async Task NativeTurnProtocol_BuildsRichRequestAndAppliesStructuredResponse() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        UserName = "Przemek",
                        AssistantPersona = "friendly pragmatic engineer",
                        ThemePreset = "cobalt",
                        OnboardingCompleted = true,
                        ProactiveModeEnabled = true,
                        MemoryFacts = new List<ChatMemoryFactState> {
                            new() { Fact = "The preferred AD lab is ad.evotec.xyz", Weight = 5 }
                        },
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-protocol", Title = "Protocol" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var store = new NativeConversationStateStore(path);
            var workspace = await store.LoadAsync(CancellationToken.None);
            var conversation = Assert.Single(workspace.Conversations);
            conversation.Messages.Add(new NativeChatTranscriptItem("user", "Investigate this", DateTimeOffset.UtcNow));
            var requestText = store.BuildRequestText(
                conversation,
                "Investigate this",
                new SessionPolicyDto {
                    ReadOnly = true,
                    DangerousToolsEnabled = false,
                    MaxToolRounds = 9,
                    ParallelTools = false,
                    AllowMutatingParallelToolCalls = false
                });

            const string response = """
                I can continue safely.

                ```ix_profile
                {"userName":"Operator","assistantPersona":"friendly analyst","themePreset":"graphite","scope":"profile"}
                ```

                ```ix_memory
                {"upserts":[{"fact":"Prefer concise risk summaries","weight":4,"tags":["style"]}]}
                ```

                [Action]
                ix:action:v1
                id: act_continue
                title: Continue investigation
                request: Continue the current investigation.
                reply: /act act_continue
                """;
            var normalized = await store.NormalizeAssistantTurnAsync(
                conversation,
                response,
                CancellationToken.None);
            await store.SaveAsync(workspace, CancellationToken.None);

            Assert.Contains("[Execution behavior]", requestText, StringComparison.Ordinal);
            Assert.Contains("friendly pragmatic engineer", requestText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("The preferred AD lab is ad.evotec.xyz", requestText, StringComparison.Ordinal);
            Assert.Contains("ix_profile", requestText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("I can continue safely.", normalized.VisibleText);
            Assert.DoesNotContain("ix_profile", normalized.VisibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ix_memory", normalized.VisibleText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("act_continue", Assert.Single(conversation.PendingActions).Id);

            using var verifier = new ChatAppStateStore(path);
            var state = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal("Operator", state!.UserName);
            Assert.Equal("friendly analyst", state.AssistantPersona);
            Assert.Equal("graphite", state.ThemePreset);
            Assert.Contains(state.MemoryFacts, fact => fact.Fact == "Prefer concise risk summaries");
            Assert.Equal("act_continue", Assert.Single(Assert.Single(state.Conversations).PendingActions).Id);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures native runtime self-reports carry the active model and live service tool policy through the thin prompt path.
    /// </summary>
    [Fact]
    public async Task BuildRequestText_RuntimeSelfReportIncludesLiveRuntimeFacts() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        OnboardingCompleted = true,
                        LocalProviderRuntimeOverrideActive = false,
                        LocalProviderTransport = "native",
                        LocalProviderModel = "wrong-app-model",
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-runtime", Title = "Runtime" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var store = new NativeConversationStateStore(path);
            var conversation = Assert.Single((await store.LoadAsync(CancellationToken.None)).Conversations);
            var userText = string.Join(
                Environment.NewLine,
                RuntimeSelfReportDirective.BuildLines(
                    "Czego teraz uzywasz?",
                    compactReply: false,
                    detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                    modelRequested: true,
                    toolingRequested: true));
            var request = store.BuildRequestText(
                conversation,
                userText,
                new SessionPolicyDto {
                    ReadOnly = true,
                    DangerousToolsEnabled = false,
                    MaxToolRounds = 12,
                    ParallelTools = false,
                    AllowMutatingParallelToolCalls = false,
                    RuntimeIdentity = new SessionRuntimeIdentityDto {
                        ProfileName = "service-profile",
                        Transport = "compatible-http",
                        Model = "service-model"
                    },
                    Packs = [
                        new ToolPackInfoDto {
                            Id = "active-directory",
                            Name = "Active Directory",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false
                        }
                    ]
                });

            Assert.Contains("[Runtime capability handshake]", request, StringComparison.Ordinal);
            Assert.Contains("compatible-http", request, StringComparison.Ordinal);
            Assert.Contains("service-model", request, StringComparison.Ordinal);
            Assert.DoesNotContain("wrong-app-model", request, StringComparison.Ordinal);
            Assert.Contains("enabled packs: 1", request, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Session policy: read-only", request, StringComparison.OrdinalIgnoreCase);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures native chat uses the shared question-aware memory selector rather than a shell-specific top-weight list.
    /// </summary>
    [Fact]
    public async Task BuildRequestText_SelectsMemoryForCurrentQuestion() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        PersistentMemoryEnabled = true,
                        MemoryFacts = new List<ChatMemoryFactState> {
                            new() { Fact = "The UI uses a cobalt theme", Weight = 5 },
                            new() {
                                Fact = "The AD replication lab is ad.evotec.xyz",
                                Weight = 3,
                                Tags = ["directory", "replication"]
                            }
                        },
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-memory", Title = "Memory" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var store = new NativeConversationStateStore(path);
            var conversation = Assert.Single((await store.LoadAsync(CancellationToken.None)).Conversations);

            var request = store.BuildRequestText(conversation, "Check AD replication health");

            Assert.Contains("The AD replication lab is ad.evotec.xyz", request, StringComparison.Ordinal);
            Assert.DoesNotContain("The UI uses a cobalt theme", request, StringComparison.Ordinal);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures a profile-scoped update replaces an earlier temporary override during the same native session.
    /// </summary>
    [Fact]
    public async Task NormalizeAssistantTurn_ProfileUpdateClearsMatchingSessionOverride() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        AssistantPersona = "baseline analyst",
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-profile", Title = "Profile" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var store = new NativeConversationStateStore(path);
            var workspace = await store.LoadAsync(CancellationToken.None);
            var conversation = Assert.Single(workspace.Conversations);

            await store.NormalizeAssistantTurnAsync(
                conversation,
                """
                ```ix_profile
                {"assistantPersona":"temporary responder","scope":"session"}
                ```
                """,
                CancellationToken.None);
            var temporaryRequest = store.BuildRequestText(conversation, "Continue");
            Assert.Contains("temporary responder", temporaryRequest, StringComparison.OrdinalIgnoreCase);

            await store.NormalizeAssistantTurnAsync(
                conversation,
                """
                ```ix_profile
                {"assistantPersona":"persistent responder","scope":"profile"}
                ```
                """,
                CancellationToken.None);
            var persistentRequest = store.BuildRequestText(conversation, "Continue");
            Assert.Contains("persistent responder", persistentRequest, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("temporary responder", persistentRequest, StringComparison.OrdinalIgnoreCase);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures a structured theme update publishes the new effective native theme immediately.
    /// </summary>
    [Fact]
    public async Task NormalizeAssistantTurn_PublishesEffectiveThemeChange() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ThemePreset = "default",
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-theme", Title = "Theme" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var store = new NativeConversationStateStore(path);
            var conversation = Assert.Single((await store.LoadAsync(CancellationToken.None)).Conversations);
            string? publishedTheme = null;
            store.EffectiveThemeChanged += theme => publishedTheme = theme;

            await store.NormalizeAssistantTurnAsync(
                conversation,
                """
                ```ix_profile
                {"themePreset":"cobalt","scope":"session"}
                ```
                """,
                CancellationToken.None);

            Assert.Equal("cobalt", store.EffectiveThemePreset);
            Assert.Equal("cobalt", publishedTheme);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures a native message save does not resurrect an action concurrently consumed by another shell.
    /// </summary>
    [Fact]
    public async Task SaveAsync_PreservesConcurrentPendingActionConsumption() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            var baselineTime = DateTime.UtcNow.AddMinutes(-2);
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        ActiveConversationId = "chat-action",
                        Conversations = new List<ChatConversationState> {
                            new() {
                                Id = "chat-action",
                                Title = "Action",
                                UpdatedUtc = baselineTime,
                                PendingAssistantQuestionHint = "Run it?",
                                PendingActions = new List<ChatPendingActionState> {
                                    new() {
                                        Id = "act_run",
                                        Title = "Run",
                                        Request = "Run the check",
                                        Reply = "/act act_run"
                                    }
                                }
                            }
                        }
                    },
                    CancellationToken.None);
            }

            await using var nativeStore = new NativeConversationStateStore(path);
            var workspace = await nativeStore.LoadAsync(CancellationToken.None);
            var nativeConversation = Assert.Single(workspace.Conversations);
            var nativeTime = DateTime.UtcNow.AddSeconds(-2);
            nativeConversation.Messages.Add(new NativeChatTranscriptItem(
                "user",
                "Keep investigating",
                new DateTimeOffset(nativeTime, TimeSpan.Zero)));
            nativeConversation.UpdatedUtc = nativeTime;

            using (var concurrentStore = new ChatAppStateStore(path)) {
                var current = await concurrentStore.GetAsync("default", CancellationToken.None);
                Assert.NotNull(current);
                var consumed = Assert.Single(current!.Conversations);
                consumed.PendingActions.Clear();
                consumed.PendingAssistantQuestionHint = null;
                consumed.UpdatedUtc = DateTime.UtcNow.AddSeconds(-1);
                await concurrentStore.UpsertAsync("default", current, CancellationToken.None);
            }

            await nativeStore.SaveAsync(workspace, CancellationToken.None);

            using var verifier = new ChatAppStateStore(path);
            var saved = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(saved);
            var merged = Assert.Single(saved!.Conversations);
            Assert.Empty(merged.PendingActions);
            Assert.Null(merged.PendingAssistantQuestionHint);
            Assert.Equal("Keep investigating", Assert.Single(merged.Messages).Text);
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures two native store instances retain both turns when their saves overlap.
    /// </summary>
    [Fact]
    public async Task SaveAsync_AtomicallyMergesOverlappingStoreWrites() {
        var directory = CreateTemporaryDirectory();
        try {
            var path = Path.Combine(directory, "app-state.db");
            using (var sharedStore = new ChatAppStateStore(path)) {
                await sharedStore.UpsertAsync(
                    "default",
                    new ChatAppState {
                        Conversations = new List<ChatConversationState> {
                            new() { Id = "chat-shared", Title = "Shared" }
                        }
                    },
                    CancellationToken.None);
            }

            await using var firstStore = new NativeConversationStateStore(path);
            await using var secondStore = new NativeConversationStateStore(path);
            var firstWorkspace = await firstStore.LoadAsync(CancellationToken.None);
            var secondWorkspace = await secondStore.LoadAsync(CancellationToken.None);
            Assert.Single(firstWorkspace.Conversations).Messages.Add(
                new NativeChatTranscriptItem("assistant", "first", DateTimeOffset.UtcNow.AddSeconds(-1), "Complete"));
            Assert.Single(firstWorkspace.Conversations).UpdatedUtc = DateTime.UtcNow.AddSeconds(-1);
            Assert.Single(secondWorkspace.Conversations).Messages.Add(
                new NativeChatTranscriptItem("assistant", "second", DateTimeOffset.UtcNow, "Complete"));
            Assert.Single(secondWorkspace.Conversations).UpdatedUtc = DateTime.UtcNow;

            await Task.WhenAll(
                firstStore.SaveAsync(firstWorkspace, CancellationToken.None),
                secondStore.SaveAsync(secondWorkspace, CancellationToken.None));

            using var verifier = new ChatAppStateStore(path);
            var state = await verifier.GetAsync("default", CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal(
                new[] { "first", "second" },
                Assert.Single(state!.Conversations).Messages.Select(message => message.Text));
        } finally {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ix-native-conversations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SeedLegacyRuntimeProfile(string path) {
        using var database = new SQLite();
        database.ExecuteNonQuery(
            path,
            """
            INSERT INTO ix_app_profiles (profile_name, json, updated_utc)
            VALUES (@name, @json, @updated_utc);
            """,
            parameters: new Dictionary<string, object?> {
                ["@name"] = "default",
                ["@json"] = """
                    {
                      "profileName": "default",
                      "localProviderTransport": "compatible-http",
                      "localProviderBaseUrl": "http://127.0.0.1:1234/v1",
                      "localProviderModel": "legacy-model",
                      "localProviderReasoningEffort": "high",
                      "conversations": [
                        { "id": "chat-legacy-runtime", "title": "Legacy runtime" }
                      ]
                    }
                    """,
                ["@updated_utc"] = DateTime.UtcNow.ToString("O")
            });
    }

    private static void DeleteTemporaryDirectory(string path) {
        if (Directory.Exists(path)) {
            Directory.Delete(path, recursive: true);
        }
    }
}
