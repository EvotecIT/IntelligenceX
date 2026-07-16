using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private const string SystemConversationId = "chat-system";
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

    public async Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken) {
        _state = await _stateStore.GetAsync(_profileName, cancellationToken).ConfigureAwait(false)
                 ?? new ChatAppState { ProfileName = _profileName };

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
            _state ??= new ChatAppState { ProfileName = _profileName };
            var systemConversations = _state.Conversations
                .Where(state => IsSystemConversation(state.Id))
                .ToList();
            var persisted = workspace.Conversations
                .Select(MapConversation)
                .OrderByDescending(static state => state.UpdatedUtc)
                .ToList();
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

    private static ChatConversationState MapConversation(NativeConversation conversation) =>
        new() {
            Id = conversation.Id,
            Title = conversation.Title,
            ThreadId = conversation.ThreadId,
            Messages = conversation.Messages.Select(MapMessage).ToList(),
            UpdatedUtc = conversation.UpdatedUtc
        };

    private static NativeChatTranscriptItem MapMessage(ChatMessageState state) {
        var role = string.IsNullOrWhiteSpace(state.Role) ? "system" : state.Role.Trim().ToLowerInvariant();
        var status = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Complete" : string.Empty;
        return new NativeChatTranscriptItem(role, state.Text ?? string.Empty, new DateTimeOffset(EnsureUtc(state.TimeUtc)), status);
    }

    private static ChatMessageState MapMessage(NativeChatTranscriptItem message) =>
        new() {
            Role = message.Role,
            Text = message.Text,
            TimeUtc = message.CreatedAt.UtcDateTime
        };

    private static bool IsSystemConversation(string? id) =>
        string.Equals((id ?? string.Empty).Trim(), SystemConversationId, StringComparison.OrdinalIgnoreCase);

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
