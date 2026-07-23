using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Theming;

namespace IntelligenceX.Chat.App.Native;

internal interface INativeConversationStore : IAsyncDisposable {
    Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken);
}

internal interface INativeQueuedTurnStore {
    Task<bool> CompleteQueuedTurnAsync(NativeQueuedTurn turn, CancellationToken cancellationToken);

    Task ClearQueuedTurnsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Native conversation adapter over the shared desktop application state store.
/// </summary>
internal sealed partial class NativeConversationStateStore : INativeConversationStore, INativeQueuedTurnStore {
    private readonly ChatAppStateStore _stateStore;
    private string _profileName;
    private string? _pendingProfileName;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, ChatConversationState> _baselineConversations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _baselinePersistedConversationIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _discardedConversationIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChatConversationState> _discardedConversationBaselines =
        new(StringComparer.OrdinalIgnoreCase);
    private ChatAppState? _state;
    private string? _sessionUserName;
    private string? _sessionAssistantPersona;
    private string? _sessionThemePreset;
    private string? _baselineActiveConversationId;
    private bool _hasBaseline;
    private bool _profileStateWasMissingAtLoad;

    internal event Action<string>? EffectiveThemeChanged;

    internal string EffectiveThemePreset =>
        _sessionThemePreset
        ?? _state?.ThemePreset
        ?? ThemeContract.DefaultPreset;

    internal string ActiveProfileName => _profileName;

    public NativeConversationStateStore(string? databasePath = null, string profileName = "default") {
        _profileName = ChatServiceLaunchProfileMapper.NormalizeProfileName(profileName);
        _stateStore = new ChatAppStateStore(
            string.IsNullOrWhiteSpace(databasePath) ? ChatAppStateStore.GetDefaultDbPath() : databasePath);
    }

    internal bool SelectProfile(string? profileName) {
        var normalized = ChatServiceLaunchProfileMapper.NormalizeProfileName(profileName);
        if (string.Equals(_profileName, normalized, StringComparison.OrdinalIgnoreCase)) {
            _pendingProfileName = null;
            return false;
        }

        if (string.Equals(_pendingProfileName, normalized, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        _pendingProfileName = normalized;
        return true;
    }

    /// <summary>
    /// Creates local service launch options from the profile loaded by this native store.
    /// </summary>
    internal ChatServiceLaunchProfileOptions CreateServiceLaunchProfileOptions() {
        if (_state is null) {
            throw new InvalidOperationException("Native profile state must be loaded before the local chat service starts.");
        }

        return ChatServiceLaunchProfileMapper.Create(
            _state,
            packToggles: null,
            bootstrapMissingProfile: _profileStateWasMissingAtLoad);
    }

    /// <summary>
    /// Creates the per-turn request options shared with the legacy desktop shell.
    /// </summary>
    internal ChatRequestOptions CreateChatRequestOptions(
        NativeConversation conversation,
        SessionPolicyDto? sessionPolicy = null,
        IReadOnlyList<ToolDefinitionDto>? availableTools = null) {
        ArgumentNullException.ThrowIfNull(conversation);
        if (_state is null) {
            throw new InvalidOperationException("Native profile state must be loaded before chat requests are created.");
        }

        var modelOverride = (_state.Conversations ?? new List<ChatConversationState>())
            .FirstOrDefault(state => string.Equals(state.Id, conversation.Id, StringComparison.OrdinalIgnoreCase))
            ?.ModelOverride;
        return ChatRequestOptionsFactory.CreateFromState(_state, modelOverride, sessionPolicy, availableTools);
    }

    public async Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken) {
        var targetProfileName = _pendingProfileName ?? _profileName;
        ChatAppState? loadedState;
        var profileStateWasMissing = false;
        string? loadWarning = null;
        try {
            loadedState = await _stateStore.GetAsync(targetProfileName, cancellationToken).ConfigureAwait(false);
            profileStateWasMissing = loadedState is null;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            if (string.Equals(_pendingProfileName, targetProfileName, StringComparison.OrdinalIgnoreCase)) {
                _pendingProfileName = null;
            }
            throw;
        } catch (Exception ex) {
            StartupLog.Write("Native profile state could not be loaded; using fresh state: " + ex.Message);
            loadWarning = "History load failed; started a fresh chat. " + ex.Message;
            loadedState = null;
        }
        _profileName = targetProfileName;
        _pendingProfileName = null;
        _profileStateWasMissingAtLoad = profileStateWasMissing;
        _state = NormalizeLoadedProfileState(loadedState);
        var persistedConversationsById = (_state.Conversations ?? new List<ChatConversationState>())
            .Where(state => !IsSystemConversation(state.Id) && !string.IsNullOrWhiteSpace(state.Id))
            .GroupBy(static state => state.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var persistedConversationIds = new HashSet<string>(
            persistedConversationsById.Keys,
            StringComparer.OrdinalIgnoreCase);

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
        var retainedConversationIds = new HashSet<string>(
            conversations.Select(static conversation => conversation.Id),
            StringComparer.OrdinalIgnoreCase);
        _discardedConversationIds.Clear();
        _discardedConversationBaselines.Clear();
        foreach (var id in persistedConversationIds) {
            if (!retainedConversationIds.Contains(id)) {
                _discardedConversationIds.Add(id);
                _discardedConversationBaselines[id] =
                    DesktopChatConversationStateMerger.CloneConversation(persistedConversationsById[id]);
            }
        }

        var activeId = conversations.Any(item => string.Equals(item.Id, _state.ActiveConversationId, StringComparison.OrdinalIgnoreCase))
            ? _state.ActiveConversationId!
            : conversations[0].Id;
        var queuedTurns = MapQueuedTurns(_state);
        var workspace = new NativeConversationWorkspace(conversations, activeId, loadWarning, queuedTurns);
        CaptureBaseline(workspace, persistedConversationIds);
        EffectiveThemeChanged?.Invoke(EffectiveThemePreset);
        return workspace;
    }

    /// <summary>
    /// Reloads profile-owned runtime and presentation settings without replacing the live conversation workspace.
    /// </summary>
    internal async Task ReloadProfileStateAsync(CancellationToken cancellationToken) {
        var previousTheme = EffectiveThemePreset;
        var loadedState = await _stateStore.GetAsync(_profileName, cancellationToken).ConfigureAwait(false);
        _profileStateWasMissingAtLoad = loadedState is null;
        _state = NormalizeLoadedProfileState(loadedState);
        _sessionUserName = null;
        _sessionAssistantPersona = null;
        _sessionThemePreset = null;
        if (!string.Equals(previousTheme, EffectiveThemePreset, StringComparison.OrdinalIgnoreCase)) {
            EffectiveThemeChanged?.Invoke(EffectiveThemePreset);
        }
    }

    private ChatAppState NormalizeLoadedProfileState(ChatAppState? loadedState) {
        var state = loadedState ?? new ChatAppState { ProfileName = _profileName };
        ChatServiceLaunchProfileMapper.NormalizeProviderState(state);
        state.LocalProviderRuntimeOverrideActive =
            ChatServiceLaunchProfileMapper.ResolveRuntimeOverrideActive(
                state,
                loadedState is not null);
        state.LocalProviderImageGenerationOverrideActive =
            ChatServiceLaunchProfileMapper.ResolveImageGenerationOverrideActive(
                state,
                loadedState is not null);
        return state;
    }

    public async Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken) {
        if (workspace == null) throw new ArgumentNullException(nameof(workspace));

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            _state = await _stateStore.UpdateAsync(
                _profileName,
                latestState => MergeWorkspaceIntoState(workspace, latestState),
                cancellationToken).ConfigureAwait(false);
            var savedConversationIds = new HashSet<string>(
                (_state.Conversations ?? new List<ChatConversationState>())
                .Where(state => !IsSystemConversation(state.Id))
                .Select(static state => state.Id),
                StringComparer.OrdinalIgnoreCase);
            CaptureBaseline(
                workspace,
                savedConversationIds);
            workspace.ClearDiscardedConversations();
            _discardedConversationIds.RemoveWhere(savedConversationIds.Contains);
            foreach (var id in savedConversationIds) {
                _discardedConversationBaselines.Remove(id);
            }
        } finally {
            _saveGate.Release();
        }
    }

    public async Task<bool> CompleteQueuedTurnAsync(NativeQueuedTurn turn, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(turn);
        var claimed = false;
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            _state = await _stateStore.UpdateAsync(
                _profileName,
                latestState => {
                    var state = latestState ?? _state ?? new ChatAppState { ProfileName = _profileName };
                    state.PendingTurns ??= new List<ChatQueuedTurnState>();
                    state.QueuedTurnsAfterLogin ??= new List<ChatQueuedTurnState>();
                    var queue = turn.Source == NativeQueuedTurnSource.AfterLogin
                        ? state.QueuedTurnsAfterLogin
                        : state.PendingTurns;
                    claimed = RemoveQueuedTurn(queue, turn);
                    return state;
                },
                cancellationToken).ConfigureAwait(false);
        } finally {
            _saveGate.Release();
        }

        return claimed;
    }

    public async Task ClearQueuedTurnsAsync(CancellationToken cancellationToken) {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            _state = await _stateStore.UpdateAsync(
                _profileName,
                latestState => {
                    var state = latestState ?? _state ?? new ChatAppState { ProfileName = _profileName };
                    state.PendingTurns = new List<ChatQueuedTurnState>();
                    state.QueuedTurnsAfterLogin = new List<ChatQueuedTurnState>();
                    return state;
                },
                cancellationToken).ConfigureAwait(false);
        } finally {
            _saveGate.Release();
        }
    }

    private ChatAppState MergeWorkspaceIntoState(
        NativeConversationWorkspace workspace,
        ChatAppState? latestState) {
        var mergedState = ResolveWorkspaceMergeState(latestState);
        var existingConversations = mergedState.Conversations ?? new List<ChatConversationState>();
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
            _baselineConversations.TryGetValue(conversation.Id, out var baseline);
            var unchangedSinceLoad = _hasBaseline
                                     && baseline is not null
                                     && ConversationMatches(conversation, baseline);
            if (unchangedSinceLoad) {
                if (existing is not null) {
                    ApplyPersistedMetadata(conversation, existing);
                    persisted.Add(existing);
                    _discardedConversationIds.Remove(conversation.Id);
                    _discardedConversationBaselines.Remove(conversation.Id);
                } else if (!_baselinePersistedConversationIds.Contains(conversation.Id)
                           && !_discardedConversationIds.Contains(conversation.Id)) {
                    persisted.Add(MapConversation(conversation, seed: null));
                } else {
                    _discardedConversationIds.Add(conversation.Id);
                }

                continue;
            }

            _discardedConversationIds.Remove(conversation.Id);
            _discardedConversationBaselines.Remove(conversation.Id);
            var merged = MergeConversation(conversation, existing, baseline);
            ApplyPersistedMetadata(conversation, merged);
            persisted.Add(merged);
        }

        foreach (var existing in existingConversations) {
            if (IsSystemConversation(existing.Id) || workspaceIds.Contains(existing.Id)) {
                continue;
            }

            if (workspace.IsConversationDiscarded(existing.Id)
                && _baselineConversations.TryGetValue(existing.Id, out var deleteBaseline)
                && DesktopChatConversationStateMerger.ConversationEqualsIncludingTimestamp(existing, deleteBaseline)) {
                continue;
            }

            if (_discardedConversationIds.Contains(existing.Id)) {
                if (_discardedConversationBaselines.TryGetValue(existing.Id, out var discardedBaseline)
                    && DesktopChatConversationStateMerger.ConversationEqualsIncludingTimestamp(
                        existing,
                        discardedBaseline)) {
                    continue;
                }

                _discardedConversationIds.Remove(existing.Id);
                _discardedConversationBaselines.Remove(existing.Id);
            }

            persisted.Add(existing);
        }

        persisted.Sort(static (left, right) => right.UpdatedUtc.CompareTo(left.UpdatedUtc));
        persisted.AddRange(systemConversations);
        mergedState.Conversations = persisted;
        var nativeActiveChanged = !_hasBaseline
                                  || !string.Equals(
                                      workspace.ActiveConversationId,
                                      _baselineActiveConversationId,
                                      StringComparison.OrdinalIgnoreCase);
        var activeId = nativeActiveChanged ? workspace.ActiveConversationId : mergedState.ActiveConversationId;
        var active = persisted.FirstOrDefault(item =>
            string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase));
        if (active is null) {
            active = persisted.FirstOrDefault(item => !IsSystemConversation(item.Id));
            activeId = active?.Id;
        }

        mergedState.ActiveConversationId = activeId;
        if (active is not null) {
            mergedState.ThreadId = active.ThreadId;
            mergedState.Messages = (active.Messages ?? new List<ChatMessageState>())
                .Select(DesktopChatConversationStateMerger.CloneMessage)
                .ToList();
        }

        return mergedState;
    }

    private ChatAppState ResolveWorkspaceMergeState(ChatAppState? latestState) {
        var mergedState = latestState ?? _state ?? new ChatAppState { ProfileName = _profileName };
        if (latestState is null || _state is null) {
            return mergedState;
        }

        if (!latestState.LocalProviderRuntimeOverrideActiveWasPresent) {
            latestState.LocalProviderRuntimeOverrideActive =
                ChatServiceLaunchProfileMapper.ResolveRuntimeOverrideActive(latestState, isLoadedProfile: true);
        }

        if (!latestState.LocalProviderImageGenerationOverrideActiveWasPresent) {
            latestState.LocalProviderImageGenerationOverrideActive =
                ChatServiceLaunchProfileMapper.ResolveImageGenerationOverrideActive(latestState, isLoadedProfile: true);
        }

        return mergedState;
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
            (state.Messages ?? new List<ChatMessageState>()).Select(MapMessage),
            (state.PendingActions ?? new List<ChatPendingActionState>()).Select(MapPendingAction),
            state.PendingAssistantQuestionHint);

    private static ChatConversationState MapConversation(
        NativeConversation conversation,
        ChatConversationState? seed) {
        var state = seed is null
            ? new ChatConversationState()
            : DesktopChatConversationStateMerger.CloneConversation(seed);
        state.Id = conversation.Id;
        state.Title = conversation.Title;
        state.ThreadId = conversation.ThreadId;
        state.Messages = conversation.Messages.Select(MapMessage).ToList();
        state.PendingActions = conversation.PendingActions.Select(MapPendingAction).ToList();
        state.PendingAssistantQuestionHint = conversation.PendingAssistantQuestionHint;
        state.UpdatedUtc = conversation.UpdatedUtc;
        return state;
    }

    private static ChatConversationState MergeConversation(
        NativeConversation conversation,
        ChatConversationState? existing,
        ChatConversationState? baseline) {
        var local = MapConversation(conversation, baseline ?? existing);
        return DesktopChatConversationStateMerger.MergeConversation(local, baseline, existing)
               ?? local;
    }

    private static void ApplyPersistedMetadata(
        NativeConversation conversation,
        ChatConversationState persisted) {
        conversation.Title = ChatConversationIdentity.NormalizeTitle(persisted.Title);
        conversation.ThreadId = string.IsNullOrWhiteSpace(persisted.ThreadId)
            ? null
            : persisted.ThreadId.Trim();
        conversation.UpdatedUtc = EnsureUtc(persisted.UpdatedUtc);
        conversation.PendingActions.Clear();
        conversation.PendingActions.AddRange(
            (persisted.PendingActions ?? new List<ChatPendingActionState>()).Select(MapPendingAction));
        conversation.PendingAssistantQuestionHint = persisted.PendingAssistantQuestionHint;
    }

    private static NativeChatTranscriptItem MapMessage(ChatMessageState state) {
        var role = string.IsNullOrWhiteSpace(state.Role) ? "system" : state.Role.Trim().ToLowerInvariant();
        var status = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? string.IsNullOrWhiteSpace(state.Status) ? "Complete" : state.Status.Trim()
            : string.Empty;
        return new NativeChatTranscriptItem(
            role,
            TranscriptMarkdownPreparation.PrepareMessageBodyForDisplay(role, state.Text),
            new DateTimeOffset(EnsureUtc(state.TimeUtc)),
            status,
            state.Model);
    }

    private static ChatMessageState MapMessage(NativeChatTranscriptItem message) =>
        new() {
            Role = message.Role,
            Text = message.Text,
            TimeUtc = message.CreatedAt.UtcDateTime,
            Model = message.Model,
            Status = message.Status
        };

    private static AssistantPendingAction MapPendingAction(ChatPendingActionState action) =>
        new(action.Id, action.Title, action.Request, action.Reply);

    private static ChatPendingActionState MapPendingAction(AssistantPendingAction action) =>
        new() {
            Id = action.Id,
            Title = action.Title,
            Request = action.Request,
            Reply = action.Reply
        };

    private static IReadOnlyList<NativeQueuedTurn> MapQueuedTurns(ChatAppState state) {
        var queued = new List<NativeQueuedTurn>();
        AddQueuedTurns(queued, state.PendingTurns, NativeQueuedTurnSource.Pending);
        AddQueuedTurns(queued, state.QueuedTurnsAfterLogin, NativeQueuedTurnSource.AfterLogin);
        queued.Sort(static (left, right) => EnsureUtc(left.EnqueuedUtc).CompareTo(EnsureUtc(right.EnqueuedUtc)));
        return queued;
    }

    private static void AddQueuedTurns(
        ICollection<NativeQueuedTurn> target,
        IReadOnlyList<ChatQueuedTurnState>? source,
        NativeQueuedTurnSource queueSource) {
        foreach (var value in source ?? Array.Empty<ChatQueuedTurnState>()) {
            var text = (value.Text ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            target.Add(new NativeQueuedTurn(
                text,
                string.IsNullOrWhiteSpace(value.ConversationId) ? null : value.ConversationId.Trim(),
                EnsureUtc(value.EnqueuedUtc),
                value.SkipUserBubbleOnDispatch,
                queueSource));
        }
    }

    private static bool RemoveQueuedTurn(IList<ChatQueuedTurnState> queue, NativeQueuedTurn turn) {
        for (var index = 0; index < queue.Count; index++) {
            var candidate = queue[index];
            if (string.Equals(candidate.Text?.Trim(), turn.Text, StringComparison.Ordinal)
                && string.Equals(candidate.ConversationId?.Trim(), turn.ConversationId, StringComparison.OrdinalIgnoreCase)
                && EnsureUtc(candidate.EnqueuedUtc) == EnsureUtc(turn.EnqueuedUtc)
                && candidate.SkipUserBubbleOnDispatch == turn.SkipUserBubbleOnDispatch) {
                queue.RemoveAt(index);
                return true;
            }
        }

        return false;
    }

    private void CaptureBaseline(
        NativeConversationWorkspace workspace,
        IEnumerable<string> persistedConversationIds) {
        _baselineConversations.Clear();
        foreach (var conversation in workspace.Conversations) {
            _baselineConversations[conversation.Id] = MapConversation(conversation, seed: null);
        }

        _baselinePersistedConversationIds.Clear();
        foreach (var id in persistedConversationIds) {
            if (!string.IsNullOrWhiteSpace(id)) {
                _baselinePersistedConversationIds.Add(id.Trim());
            }
        }

        _baselineActiveConversationId = workspace.ActiveConversationId;
        _hasBaseline = true;
    }

    private static bool ConversationMatches(NativeConversation conversation, ChatConversationState baseline) =>
        DesktopChatConversationStateMerger.ConversationEqualsIncludingTimestamp(
            MapConversation(conversation, baseline),
            baseline);

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
