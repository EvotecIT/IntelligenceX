using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards shared profile normalization, cross-shell state merging, and native theme projection.
/// </summary>
public sealed class DesktopChatSharedStateTests {
    /// <summary>Ensures a stale legacy save preserves newer native profile, memory, and transcript state.</summary>
    [Fact]
    public async Task LegacyMerge_PreservesNativeChangesMadeAfterLegacyLoad() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-shared-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var path = Path.Combine(directory, "app-state.db");
            var initialTime = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
            using var store = new ChatAppStateStore(path);
            await store.UpsertAsync(
                "default",
                BuildState("Original", "Original memory", "Initial message", initialTime),
                CancellationToken.None);
            var staleLegacy = Assert.IsType<ChatAppState>(await store.GetAsync("default", CancellationToken.None));
            var baseline = store.CloneState(staleLegacy);

            _ = await store.UpdateAsync(
                "default",
                latest => {
                    var state = Assert.IsType<ChatAppState>(latest);
                    state.UserName = "Native name";
                    state.MemoryFacts.Add(new ChatMemoryFactState {
                        Id = "native-memory",
                        Fact = "Native memory",
                        UpdatedUtc = initialTime.AddMinutes(1)
                    });
                    var conversation = Assert.Single(state.Conversations);
                    conversation.Messages.Add(new ChatMessageState {
                        Role = "assistant",
                        Text = "Native answer",
                        TimeUtc = initialTime.AddMinutes(1)
                    });
                    conversation.UpdatedUtc = initialTime.AddMinutes(1);
                    return state;
                },
                CancellationToken.None);

            _ = await store.UpdateAsync(
                "default",
                latest => DesktopChatStateMerger.MergeLegacySnapshot(staleLegacy, baseline, latest),
                CancellationToken.None);
            var saved = Assert.IsType<ChatAppState>(await store.GetAsync("default", CancellationToken.None));

            Assert.Equal("Native name", saved.UserName);
            Assert.Contains(saved.MemoryFacts, fact => fact.Fact == "Native memory");
            Assert.Contains(Assert.Single(saved.Conversations).Messages, message => message.Text == "Native answer");
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Ensures concurrent message additions from both shells are retained.</summary>
    [Fact]
    public void LegacyMerge_CombinesConcurrentMessageAdditions() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        local.Conversations[0].Messages.Add(new ChatMessageState {
            Role = "user",
            Text = "Legacy addition",
            TimeUtc = time.AddMinutes(1)
        });
        latest.Conversations[0].Messages.Add(new ChatMessageState {
            Role = "assistant",
            Text = "Native addition",
            TimeUtc = time.AddMinutes(2)
        });

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);
        var messages = Assert.Single(merged.Conversations).Messages;

        Assert.Contains(messages, message => message.Text == "Legacy addition");
        Assert.Contains(messages, message => message.Text == "Native addition");
    }

    /// <summary>Ensures transcript persistence cannot overwrite newer runtime, autonomy, or tool settings.</summary>
    [Fact]
    public void LegacyMerge_PreservesSettingsChangedAfterLegacyLoad() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        local.Conversations[0].Messages.Add(new ChatMessageState {
            Role = "user",
            Text = "Legacy addition",
            TimeUtc = time.AddMinutes(1)
        });
        latest.LocalProviderTransport = "compatible-http";
        latest.LocalProviderBaseUrl = "http://127.0.0.1:1234/v1";
        latest.LocalProviderModel = "local-model";
        latest.AutonomyMaxToolRounds = 9;
        latest.DisabledTools = ["unsafe_tool"];
        latest.EnabledWriteTools = ["approved_write_tool"];
        var staleLocal = CloneForTest(local);

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.Equal("compatible-http", merged.LocalProviderTransport);
        Assert.Equal("http://127.0.0.1:1234/v1", merged.LocalProviderBaseUrl);
        Assert.Equal("local-model", merged.LocalProviderModel);
        Assert.Equal(9, merged.AutonomyMaxToolRounds);
        Assert.Equal(["unsafe_tool"], merged.DisabledTools);
        Assert.Equal(["approved_write_tool"], merged.EnabledWriteTools);
        Assert.Contains(Assert.Single(merged.Conversations).Messages, message => message.Text == "Legacy addition");
        Assert.False(DesktopChatStateMerger.RuntimeAndPreferenceStateEquals(staleLocal, merged));

        var afterAnotherStaleSave = DesktopChatStateMerger.MergeLegacySnapshot(staleLocal, baseline, CloneForTest(merged));
        Assert.Equal("compatible-http", afterAnotherStaleSave.LocalProviderTransport);
        Assert.Equal("local-model", afterAnotherStaleSave.LocalProviderModel);
        Assert.Equal(["unsafe_tool"], afterAnotherStaleSave.DisabledTools);
        Assert.Equal(["approved_write_tool"], afterAnotherStaleSave.EnabledWriteTools);
    }

    /// <summary>Ensures transcript persistence cannot overwrite a model catalog refreshed by another window.</summary>
    [Fact]
    public void LegacyMerge_PreservesModelCatalogRefreshedAfterLegacyLoad() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        latest.CachedModelsTransport = "compatible-http";
        latest.CachedModelsBaseUrl = "http://127.0.0.1:1234/v1";
        latest.CachedModels = [new ModelInfoDto {
            Id = "fresh-model",
            Model = "fresh-model",
            DisplayName = "Fresh model",
            Capabilities = ["tools", "vision"],
            SupportedReasoningEfforts = [new ReasoningEffortOptionDto {
                ReasoningEffort = "high",
                Description = "High reasoning"
            }]
        }];
        latest.CachedFavoriteModels = ["fresh-model"];
        latest.CachedRecentModels = ["fresh-model", "older-model"];
        latest.CachedModelListIsStale = true;
        latest.CachedModelListWarning = "Using the last successful refresh.";
        latest.CachedModelsUpdatedUtc = time.AddMinutes(2);
        var localBeforeMerge = CloneForTest(local);

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.Equal("compatible-http", merged.CachedModelsTransport);
        Assert.Equal("http://127.0.0.1:1234/v1", merged.CachedModelsBaseUrl);
        var model = Assert.Single(merged.CachedModels);
        Assert.Equal("fresh-model", model.Model);
        Assert.Equal(["tools", "vision"], model.Capabilities);
        Assert.Equal("high", Assert.Single(model.SupportedReasoningEfforts).ReasoningEffort);
        Assert.Equal(["fresh-model"], merged.CachedFavoriteModels);
        Assert.Equal(["fresh-model", "older-model"], merged.CachedRecentModels);
        Assert.True(merged.CachedModelListIsStale);
        Assert.Equal("Using the last successful refresh.", merged.CachedModelListWarning);
        Assert.Equal(time.AddMinutes(2), merged.CachedModelsUpdatedUtc);
        Assert.False(DesktopChatStateMerger.RuntimeAndPreferenceStateEquals(localBeforeMerge, merged));
    }

    /// <summary>Ensures concurrent provider changes cannot combine with another provider's refreshed models.</summary>
    [Fact]
    public void LegacyMerge_KeepsConflictingModelCatalogSnapshotCoherent() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        baseline.CachedModelsTransport = "compatible-http";
        baseline.CachedModelsBaseUrl = "http://provider-a.test/v1";
        baseline.CachedModelsUpdatedUtc = time;
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);

        local.CachedModelsTransport = "native";
        local.CachedModelsBaseUrl = null;
        local.CachedModels = [];
        local.CachedFavoriteModels = [];
        local.CachedRecentModels = [];
        local.CachedModelListIsStale = false;
        local.CachedModelListWarning = null;
        local.CachedModelsUpdatedUtc = time.AddMinutes(1);

        latest.CachedModels = [new ModelInfoDto {
            Id = "provider-a-model",
            Model = "provider-a-model"
        }];
        latest.CachedFavoriteModels = ["provider-a-model"];
        latest.CachedRecentModels = ["provider-a-model"];
        latest.CachedModelsUpdatedUtc = time.AddMinutes(2);

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.Equal("native", merged.CachedModelsTransport);
        Assert.Null(merged.CachedModelsBaseUrl);
        Assert.Empty(merged.CachedModels);
        Assert.Empty(merged.CachedFavoriteModels);
        Assert.Empty(merged.CachedRecentModels);
        Assert.Equal(time.AddMinutes(1), merged.CachedModelsUpdatedUtc);
    }

    /// <summary>Ensures timestamp-only save churn cannot replace a newer external catalog refresh.</summary>
    [Fact]
    public void LegacyMerge_IgnoresTimestampOnlyCatalogChurnWhenChoosingSnapshot() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        baseline.CachedModels = [new ModelInfoDto { Id = "old-model", Model = "old-model" }];
        baseline.CachedModelsUpdatedUtc = time;
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        local.CachedModelsUpdatedUtc = time.AddMinutes(1);
        latest.CachedModels = [new ModelInfoDto { Id = "new-model", Model = "new-model" }];
        latest.CachedModelsUpdatedUtc = time.AddMinutes(2);

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.Equal("new-model", Assert.Single(merged.CachedModels).Model);
        Assert.Equal(time.AddMinutes(2), merged.CachedModelsUpdatedUtc);
    }

    /// <summary>Ensures a local preference edit still wins when the persisted value has not changed.</summary>
    [Fact]
    public void LegacyMerge_PreservesLocalSettingsChangedFromBaseline() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        local.TimestampMode = "none";
        local.AutonomyMaxToolRounds = 7;

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.Equal("none", merged.TimestampMode);
        Assert.Equal(7, merged.AutonomyMaxToolRounds);
    }

    /// <summary>Ensures a stale window cannot erase turns queued by another window.</summary>
    [Fact]
    public void LegacyMerge_PreservesQueuedTurnsAddedAfterLegacyLoad() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        latest.PendingTurns.Add(BuildQueuedTurn("Run replication", "chat-one", time.AddMinutes(1)));
        latest.QueuedTurnsAfterLogin.Add(BuildQueuedTurn("Resume after sign-in", "chat-one", time.AddMinutes(2)));
        var localBeforeMerge = CloneForTest(local);

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.Equal("Run replication", Assert.Single(merged.PendingTurns).Text);
        Assert.Equal("Resume after sign-in", Assert.Single(merged.QueuedTurnsAfterLogin).Text);
        Assert.False(DesktopChatStateMerger.LiveOperationalStateEquals(localBeforeMerge, merged));
    }

    /// <summary>Ensures a stale transcript save cannot replace newer token, rate-limit, or credit usage.</summary>
    [Fact]
    public void LegacyMerge_PreservesAccountUsageChangedAfterLegacyLoad() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        baseline.AccountUsage.Add(new ChatAccountUsageState {
            Key = "native:account-1",
            Label = "Account 1",
            TotalTokens = 10,
            Turns = 1,
            LastSeenUtc = time
        });
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        latest.AccountUsage[0].TotalTokens = 400;
        latest.AccountUsage[0].Turns = 4;
        latest.AccountUsage[0].RateLimitReached = true;
        latest.AccountUsage[0].CreditsBalance = 12.5d;
        latest.AccountUsage[0].LastSeenUtc = time.AddMinutes(2);
        var localBeforeMerge = CloneForTest(local);

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        var usage = Assert.Single(merged.AccountUsage);
        Assert.Equal(400, usage.TotalTokens);
        Assert.Equal(4, usage.Turns);
        Assert.True(usage.RateLimitReached);
        Assert.Equal(12.5d, usage.CreditsBalance);
        Assert.False(DesktopChatStateMerger.SharedStateEquals(localBeforeMerge, merged));
    }

    /// <summary>Ensures independent turns and the newest provider snapshot survive a concurrent cross-window save.</summary>
    [Fact]
    public void LegacyMerge_CombinesConcurrentAccountUsageDeltasAndKeepsNewestSnapshot() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        baseline.AccountUsage.Add(new ChatAccountUsageState {
            Key = "native:account-1",
            Label = "Account 1",
            PromptTokens = 100,
            CompletionTokens = 50,
            TotalTokens = 150,
            Turns = 1,
            LastSeenUtc = time,
            UsageSnapshotRetrievedAtUtc = time,
            RateLimitReached = false,
            CreditsBalance = 20d
        });
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        local.AccountUsage[0].PromptTokens = 110;
        local.AccountUsage[0].CompletionTokens = 55;
        local.AccountUsage[0].TotalTokens = 165;
        local.AccountUsage[0].Turns = 2;
        local.AccountUsage[0].LastSeenUtc = time.AddMinutes(1);
        latest.AccountUsage[0].PromptTokens = 120;
        latest.AccountUsage[0].CompletionTokens = 58;
        latest.AccountUsage[0].TotalTokens = 178;
        latest.AccountUsage[0].Turns = 2;
        latest.AccountUsage[0].LastSeenUtc = time.AddMinutes(2);
        latest.AccountUsage[0].UsageSnapshotRetrievedAtUtc = time.AddMinutes(2);
        latest.AccountUsage[0].RateLimitReached = true;
        latest.AccountUsage[0].CreditsBalance = 12.5d;

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        var usage = Assert.Single(merged.AccountUsage);
        Assert.Equal(130, usage.PromptTokens);
        Assert.Equal(63, usage.CompletionTokens);
        Assert.Equal(193, usage.TotalTokens);
        Assert.Equal(3, usage.Turns);
        Assert.Equal(time.AddMinutes(2), usage.LastSeenUtc);
        Assert.Equal(time.AddMinutes(2), usage.UsageSnapshotRetrievedAtUtc);
        Assert.True(usage.RateLimitReached);
        Assert.Equal(12.5d, usage.CreditsBalance);
    }

    /// <summary>Ensures concurrent queue additions survive without resurrecting a consumed baseline entry.</summary>
    [Fact]
    public void LegacyMerge_CombinesConcurrentQueueAdditionsAndHonorsConsumption() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        baseline.PendingTurns.Add(BuildQueuedTurn("Already queued", "chat-one", time));
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        local.PendingTurns.RemoveAt(0);
        local.PendingTurns.Add(BuildQueuedTurn("Legacy addition", "chat-one", time.AddMinutes(2)));
        latest.PendingTurns.Add(BuildQueuedTurn("Native addition", "chat-one", time.AddMinutes(1)));

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.DoesNotContain(merged.PendingTurns, turn => turn.Text == "Already queued");
        Assert.Equal(new[] { "Native addition", "Legacy addition" }, merged.PendingTurns.Select(turn => turn.Text));
    }

    /// <summary>Ensures a cross-window merge never persists more turns than the live queue can restore.</summary>
    [Fact]
    public void LegacyMerge_BoundsConcurrentQueueAdditionsToLiveCapacity() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        for (var index = 0; index < ChatQueueContract.MaxTurns; index++) {
            local.PendingTurns.Add(BuildQueuedTurn("Local " + index, "chat-one", time.AddSeconds(index * 2)));
            latest.PendingTurns.Add(BuildQueuedTurn("Latest " + index, "chat-one", time.AddSeconds(index * 2 + 1)));
        }

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);

        Assert.Equal(ChatQueueContract.MaxTurns, merged.PendingTurns.Count);
        Assert.Equal(
            new[] { "Local 0", "Latest 0", "Local 1", "Latest 1", "Local 2", "Latest 2", "Local 3", "Latest 3" },
            merged.PendingTurns.Select(turn => turn.Text));
    }

    /// <summary>Ensures deserialization-only migration metadata does not look like a runtime preference change.</summary>
    [Fact]
    public void RuntimePreferenceComparison_IgnoresPropertyPresenceMetadata() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var original = BuildState("Operator", "Memory", "Initial message", time);
        var locallyChanged = CloneForTest(original);
        locallyChanged.TimestampMode = "none";
        var persistedState = DesktopChatStateMerger.MergeLegacySnapshot(
            locallyChanged,
            original,
            CloneForTest(original));
        persistedState.LocalProviderRuntimeOverrideActiveWasPresent = true;
        persistedState.LocalProviderImageGenerationOverrideActiveWasPresent = true;

        Assert.True(DesktopChatStateMerger.RuntimeAndPreferenceStateEquals(locallyChanged, persistedState));

        var advancedBaseline = CloneForTest(persistedState);
        var laterExternalChange = CloneForTest(persistedState);
        laterExternalChange.TimestampMode = "milliseconds";
        laterExternalChange.LocalProviderRuntimeOverrideActiveWasPresent = true;
        laterExternalChange.LocalProviderImageGenerationOverrideActiveWasPresent = true;

        var afterLaterSave = DesktopChatStateMerger.MergeLegacySnapshot(
            CloneForTest(persistedState),
            advancedBaseline,
            laterExternalChange);

        Assert.Equal("milliseconds", afterLaterSave.TimestampMode);
    }

    /// <summary>Ensures advancing the baseline to the reconciled snapshot allows a later legacy deletion.</summary>
    [Fact]
    public void LegacyMerge_ReconciledBaselineDoesNotResurrectDeletedState() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var original = BuildState("Operator", "Original memory", "Initial message", time);
        var latest = CloneForTest(original);
        latest.MemoryFacts.Add(new ChatMemoryFactState {
            Id = "native-memory",
            Fact = "Native memory",
            UpdatedUtc = time.AddMinutes(1)
        });
        latest.Conversations[0].Messages.Add(new ChatMessageState {
            Role = "assistant",
            Text = "Native answer",
            TimeUtc = time.AddMinutes(1)
        });
        latest.Conversations[0].UpdatedUtc = time.AddMinutes(1);

        var reconciled = DesktopChatStateMerger.MergeLegacySnapshot(CloneForTest(original), original, latest);
        var afterDeletion = CloneForTest(reconciled);
        afterDeletion.MemoryFacts.Clear();
        afterDeletion.Conversations.Clear();
        afterDeletion.Messages.Clear();
        afterDeletion.ActiveConversationId = null;

        var saved = DesktopChatStateMerger.MergeLegacySnapshot(afterDeletion, reconciled, CloneForTest(reconciled));

        Assert.Empty(saved.MemoryFacts);
        Assert.Empty(saved.Conversations);
    }

    /// <summary>Ensures structured profile values normalize identically regardless of desktop shell.</summary>
    [Fact]
    public void ProfileNormalizer_UsesOneDeterministicContract() {
        var longName = new string('N', 60);
        var longPersona = new string('P', 200);

        Assert.Equal(new string('N', 48), DesktopChatProfileNormalizer.NormalizeUserName(longName));
        Assert.Equal(new string('P', 180), DesktopChatProfileNormalizer.NormalizeAssistantPersona(longPersona));
        Assert.Null(DesktopChatProfileNormalizer.NormalizeUserName("skip"));
        Assert.Equal("analyst", DesktopChatProfileNormalizer.NormalizeAssistantPersona("analyst"));
    }

    private static ChatAppState BuildState(string userName, string memory, string message, DateTime timeUtc) {
        var conversation = new ChatConversationState {
            Id = "chat-one",
            Title = "Chat",
            Messages = new List<ChatMessageState> {
                new() { Role = "user", Text = message, TimeUtc = timeUtc }
            },
            UpdatedUtc = timeUtc
        };
        return new ChatAppState {
            UserName = userName,
            ActiveConversationId = conversation.Id,
            Conversations = new List<ChatConversationState> { conversation },
            Messages = conversation.Messages.Select(messageState => new ChatMessageState {
                Role = messageState.Role,
                Text = messageState.Text,
                TimeUtc = messageState.TimeUtc
            }).ToList(),
            MemoryFacts = new List<ChatMemoryFactState> {
                new() { Id = "memory-one", Fact = memory, UpdatedUtc = timeUtc }
            }
        };
    }

    private static ChatQueuedTurnState BuildQueuedTurn(string text, string conversationId, DateTime enqueuedUtc) =>
        new() {
            Text = text,
            ConversationId = conversationId,
            EnqueuedUtc = enqueuedUtc
        };

    private static ChatAppState CloneForTest(ChatAppState state) {
        var conversation = Assert.Single(state.Conversations);
        var clonedConversation = new ChatConversationState {
            Id = conversation.Id,
            Title = conversation.Title,
            ThreadId = conversation.ThreadId,
            Messages = conversation.Messages.Select(message => new ChatMessageState {
                Role = message.Role,
                Text = message.Text,
                TimeUtc = message.TimeUtc,
                Model = message.Model,
                Status = message.Status
            }).ToList(),
            UpdatedUtc = conversation.UpdatedUtc
        };
        return new ChatAppState {
            UserName = state.UserName,
            AssistantPersona = state.AssistantPersona,
            ThemePreset = state.ThemePreset,
            LocalProviderTransport = state.LocalProviderTransport,
            LocalProviderBaseUrl = state.LocalProviderBaseUrl,
            LocalProviderModel = state.LocalProviderModel,
            TimestampMode = state.TimestampMode,
            AutonomyMaxToolRounds = state.AutonomyMaxToolRounds,
            CachedModelsTransport = state.CachedModelsTransport,
            CachedModelsBaseUrl = state.CachedModelsBaseUrl,
            CachedModels = state.CachedModels.Select(model => model with {
                Capabilities = model.Capabilities.ToArray(),
                SupportedReasoningEfforts = model.SupportedReasoningEfforts
                    .Select(option => option with { })
                    .ToArray()
            }).ToList(),
            CachedFavoriteModels = state.CachedFavoriteModels.ToList(),
            CachedRecentModels = state.CachedRecentModels.ToList(),
            CachedModelListIsStale = state.CachedModelListIsStale,
            CachedModelListWarning = state.CachedModelListWarning,
            CachedModelsUpdatedUtc = state.CachedModelsUpdatedUtc,
            DisabledTools = state.DisabledTools.ToList(),
            EnabledWriteTools = state.EnabledWriteTools.ToList(),
            ActiveConversationId = state.ActiveConversationId,
            Conversations = new List<ChatConversationState> { clonedConversation },
            Messages = clonedConversation.Messages.ToList(),
            PendingTurns = state.PendingTurns.Select(turn => new ChatQueuedTurnState {
                Text = turn.Text,
                ConversationId = turn.ConversationId,
                EnqueuedUtc = turn.EnqueuedUtc,
                SkipUserBubbleOnDispatch = turn.SkipUserBubbleOnDispatch
            }).ToList(),
            QueuedTurnsAfterLogin = state.QueuedTurnsAfterLogin.Select(turn => new ChatQueuedTurnState {
                Text = turn.Text,
                ConversationId = turn.ConversationId,
                EnqueuedUtc = turn.EnqueuedUtc,
                SkipUserBubbleOnDispatch = turn.SkipUserBubbleOnDispatch
            }).ToList(),
            AccountUsage = state.AccountUsage.Select(value => new ChatAccountUsageState {
                Key = value.Key,
                Label = value.Label,
                PromptTokens = value.PromptTokens,
                CompletionTokens = value.CompletionTokens,
                TotalTokens = value.TotalTokens,
                CachedPromptTokens = value.CachedPromptTokens,
                ReasoningTokens = value.ReasoningTokens,
                Turns = value.Turns,
                LastSeenUtc = value.LastSeenUtc,
                UsageLimitHitUtc = value.UsageLimitHitUtc,
                UsageLimitRetryAfterUtc = value.UsageLimitRetryAfterUtc,
                PlanType = value.PlanType,
                Email = value.Email,
                RateLimitAllowed = value.RateLimitAllowed,
                RateLimitReached = value.RateLimitReached,
                RateLimitUsedPercent = value.RateLimitUsedPercent,
                RateLimitWindowResetUtc = value.RateLimitWindowResetUtc,
                UsageSnapshotRetrievedAtUtc = value.UsageSnapshotRetrievedAtUtc,
                UsageSnapshotSource = value.UsageSnapshotSource,
                CreditsHasCredits = value.CreditsHasCredits,
                CreditsUnlimited = value.CreditsUnlimited,
                CreditsBalance = value.CreditsBalance
            }).ToList(),
            MemoryFacts = state.MemoryFacts.Select(fact => new ChatMemoryFactState {
                Id = fact.Id,
                Fact = fact.Fact,
                Weight = fact.Weight,
                Tags = fact.Tags.ToArray(),
                UpdatedUtc = fact.UpdatedUtc
            }).ToList()
        };
    }
}
