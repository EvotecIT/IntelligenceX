using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Launch;

namespace IntelligenceX.Chat.App.Native;

internal interface INativeConversationStore : IAsyncDisposable {
    Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken);
}

/// <summary>
/// Native conversation adapter over the shared desktop application state store.
/// </summary>
internal sealed class NativeConversationStateStore : INativeConversationStore {
    private readonly ChatAppStateStore _stateStore;
    private readonly string _profileName;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private ChatAppState? _state;

    public NativeConversationStateStore(string? databasePath = null, string profileName = "default") {
        _profileName = ChatServiceLaunchProfileMapper.NormalizeProfileName(profileName);
        _stateStore = new ChatAppStateStore(
            string.IsNullOrWhiteSpace(databasePath) ? ChatAppStateStore.GetDefaultDbPath() : databasePath);
    }

    /// <summary>
    /// Creates local service launch options from the profile loaded by this native store.
    /// </summary>
    internal ChatServiceLaunchProfileOptions CreateServiceLaunchProfileOptions() {
        if (_state is null) {
            throw new InvalidOperationException("Native profile state must be loaded before the local chat service starts.");
        }

        return ChatServiceLaunchProfileMapper.Create(_state);
    }

    /// <summary>
    /// Creates the per-turn request options shared with the legacy desktop shell.
    /// </summary>
    internal ChatRequestOptions CreateChatRequestOptions(NativeConversation conversation) {
        ArgumentNullException.ThrowIfNull(conversation);
        if (_state is null) {
            throw new InvalidOperationException("Native profile state must be loaded before chat requests are created.");
        }

        var modelOverride = (_state.Conversations ?? new List<ChatConversationState>())
            .FirstOrDefault(state => string.Equals(state.Id, conversation.Id, StringComparison.OrdinalIgnoreCase))
            ?.ModelOverride;
        return ChatRequestOptionsFactory.CreateFromState(_state, modelOverride);
    }

    public async Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken) {
        var loadedState = await _stateStore.GetAsync(_profileName, cancellationToken).ConfigureAwait(false);
        _state = loadedState ?? new ChatAppState { ProfileName = _profileName };
        _state.LocalProviderImageGenerationOverrideActive =
            ChatServiceLaunchProfileMapper.ResolveImageGenerationOverrideActive(
                _state,
                loadedState is not null);

        var conversations = new List<NativeConversation>();
        if (_state.Conversations is { Count: > 0 }) {
            foreach (var state in _state.Conversations) {
                if (IsSystemConversation(state.Id) || string.IsNullOrWhiteSpace(state.Id)) {
                    continue;
                }

                conversations.Add(MapConversation(state));
            }
        }

        if (conversations.Count == 0 && _state.Messages is { Count: > 0 }) {
            conversations.Add(new NativeConversation(
                NativeConversation.CreateNew().Id,
                "New Chat",
                _state.ThreadId,
                _state.UpdatedUtc,
                _state.Messages.Select(MapMessage)));
            conversations[0].UpdateTitleFromFirstUserMessage();
        }

        if (conversations.Count == 0) {
            conversations.Add(NativeConversation.CreateNew());
        }

        conversations.Sort(static (left, right) => right.UpdatedUtc.CompareTo(left.UpdatedUtc));
        RemoveDuplicateEmptyDrafts(conversations);
        var activeId = conversations.Any(item => string.Equals(item.Id, _state.ActiveConversationId, StringComparison.OrdinalIgnoreCase))
            ? _state.ActiveConversationId!
            : conversations[0].Id;
        return new NativeConversationWorkspace(conversations, activeId);
    }

    public async Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken) {
        if (workspace == null) throw new ArgumentNullException(nameof(workspace));

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var latestState = await _stateStore.GetAsync(_profileName, cancellationToken).ConfigureAwait(false);
            _state = latestState ?? _state ?? new ChatAppState { ProfileName = _profileName };
            var existingConversations = _state.Conversations ?? new List<ChatConversationState>();
            var existingById = existingConversations
                .Where(static state => !string.IsNullOrWhiteSpace(state.Id))
                .GroupBy(static state => state.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
            var systemConversations = existingConversations
                .Where(state => IsSystemConversation(state.Id))
                .ToList();
            var workspaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var persisted = new List<ChatConversationState>(workspace.Conversations.Count + existingConversations.Count);
            foreach (var conversation in workspace.Conversations) {
                workspaceIds.Add(conversation.Id);
                existingById.TryGetValue(conversation.Id, out var existing);
                persisted.Add(MapConversation(conversation, existing));
            }

            foreach (var existing in existingConversations) {
                if (!IsSystemConversation(existing.Id) && !workspaceIds.Contains(existing.Id)) {
                    persisted.Add(existing);
                }
            }

            persisted.Sort(static (left, right) => right.UpdatedUtc.CompareTo(left.UpdatedUtc));
            persisted.AddRange(systemConversations);
            _state.Conversations = persisted;
            _state.ActiveConversationId = workspace.ActiveConversationId;

            var active = workspace.Conversations.FirstOrDefault(item =>
                string.Equals(item.Id, workspace.ActiveConversationId, StringComparison.OrdinalIgnoreCase));
            if (active is not null) {
                _state.ThreadId = active.ThreadId;
                _state.Messages = active.Messages.Select(MapMessage).ToList();
            }

            await _stateStore.UpsertAsync(_profileName, _state, cancellationToken).ConfigureAwait(false);
        } finally {
            _saveGate.Release();
        }
    }

    public ValueTask DisposeAsync() {
        _saveGate.Dispose();
        _stateStore.Dispose();
        return ValueTask.CompletedTask;
    }

    private static NativeConversation MapConversation(ChatConversationState state) =>
        new(
            state.Id,
            state.Title,
            state.ThreadId,
            state.UpdatedUtc,
            (state.Messages ?? new List<ChatMessageState>()).Select(MapMessage));

    private static ChatConversationState MapConversation(
        NativeConversation conversation,
        ChatConversationState? existing) {
        var state = existing ?? new ChatConversationState();
        state.Id = conversation.Id;
        state.Title = conversation.Title;
        state.ThreadId = conversation.ThreadId;
        state.Messages = conversation.Messages.Select(MapMessage).ToList();
        state.UpdatedUtc = conversation.UpdatedUtc;
        return state;
    }

    private static NativeChatTranscriptItem MapMessage(ChatMessageState state) {
        var role = string.IsNullOrWhiteSpace(state.Role) ? "system" : state.Role.Trim().ToLowerInvariant();
        var status = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Complete" : string.Empty;
        return new NativeChatTranscriptItem(
            role,
            state.Text ?? string.Empty,
            new DateTimeOffset(EnsureUtc(state.TimeUtc)),
            status,
            state.Model);
    }

    private static ChatMessageState MapMessage(NativeChatTranscriptItem message) =>
        new() {
            Role = message.Role,
            Text = message.Text,
            TimeUtc = message.CreatedAt.UtcDateTime,
            Model = message.Model
        };

    private static bool IsSystemConversation(string? id) =>
        string.Equals(
            (id ?? string.Empty).Trim(),
            ChatConversationIdentity.SystemConversationId,
            StringComparison.OrdinalIgnoreCase);

    private static void RemoveDuplicateEmptyDrafts(List<NativeConversation> conversations) {
        var foundEmptyDraft = false;
        for (var index = 0; index < conversations.Count;) {
            if (!conversations[index].IsEmptyDraft) {
                index++;
                continue;
            }

            if (!foundEmptyDraft) {
                foundEmptyDraft = true;
                index++;
                continue;
            }

            conversations.RemoveAt(index);
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
