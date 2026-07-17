using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.App.Theming;

namespace IntelligenceX.Chat.App.Native;

internal interface INativeConversationStore : IAsyncDisposable {
    Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken);
}

/// <summary>
/// Native conversation adapter over the shared desktop application state store.
/// </summary>
internal sealed partial class NativeConversationStateStore : INativeConversationStore {
    private readonly ChatAppStateStore _stateStore;
    private readonly string _profileName;
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

    internal event Action<string>? EffectiveThemeChanged;

    internal string EffectiveThemePreset =>
        _sessionThemePreset
        ?? _state?.ThemePreset
        ?? ThemeContract.DefaultPreset;

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
    internal ChatRequestOptions CreateChatRequestOptions(
        NativeConversation conversation,
        SessionPolicyDto? sessionPolicy = null) {
        ArgumentNullException.ThrowIfNull(conversation);
        if (_state is null) {
            throw new InvalidOperationException("Native profile state must be loaded before chat requests are created.");
        }

        var modelOverride = (_state.Conversations ?? new List<ChatConversationState>())
            .FirstOrDefault(state => string.Equals(state.Id, conversation.Id, StringComparison.OrdinalIgnoreCase))
            ?.ModelOverride;
        return ChatRequestOptionsFactory.CreateFromState(_state, modelOverride, sessionPolicy);
    }

    public async Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken) {
        ChatAppState? loadedState;
        string? loadWarning = null;
        try {
            loadedState = await _stateStore.GetAsync(_profileName, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            StartupLog.Write("Native profile state could not be loaded; using fresh state: " + ex.Message);
            loadWarning = "History load failed; started a fresh chat. " + ex.Message;
            loadedState = null;
        }
        _state = loadedState ?? new ChatAppState { ProfileName = _profileName };
        ChatServiceLaunchProfileMapper.NormalizeProviderState(_state);
        _state.LocalProviderRuntimeOverrideActive =
            ChatServiceLaunchProfileMapper.ResolveRuntimeOverrideActive(
                _state,
                loadedState is not null);
        _state.LocalProviderImageGenerationOverrideActive =
            ChatServiceLaunchProfileMapper.ResolveImageGenerationOverrideActive(
                _state,
                loadedState is not null);
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
                _discardedConversationBaselines[id] = CloneConversation(persistedConversationsById[id]);
            }
        }

        var activeId = conversations.Any(item => string.Equals(item.Id, _state.ActiveConversationId, StringComparison.OrdinalIgnoreCase))
            ? _state.ActiveConversationId!
            : conversations[0].Id;
        var workspace = new NativeConversationWorkspace(conversations, activeId, loadWarning);
        CaptureBaseline(workspace, persistedConversationIds);
        EffectiveThemeChanged?.Invoke(EffectiveThemePreset);
        return workspace;
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
            _discardedConversationIds.RemoveWhere(savedConversationIds.Contains);
            foreach (var id in savedConversationIds) {
                _discardedConversationBaselines.Remove(id);
            }
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
                    persisted.Add(MapConversation(conversation, existing: null));
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

            if (_discardedConversationIds.Contains(existing.Id)) {
                if (_discardedConversationBaselines.TryGetValue(existing.Id, out var discardedBaseline)
                    && ConversationStateMatches(existing, discardedBaseline)) {
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
                .Select(CloneMessage)
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
        ChatConversationState? existing) {
        var state = existing ?? new ChatConversationState();
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
        if (existing is null || baseline is null) {
            return MapConversation(conversation, existing);
        }

        existing.Id = conversation.Id;
        existing.Title = ResolveConcurrentValue(
            conversation.Title,
            baseline.Title,
            existing.Title,
            conversation.UpdatedUtc,
            existing.UpdatedUtc) ?? ChatConversationIdentity.DefaultTitle;
        existing.ThreadId = ResolveConcurrentValue(
            conversation.ThreadId,
            baseline.ThreadId,
            existing.ThreadId,
            conversation.UpdatedUtc,
            existing.UpdatedUtc);
        existing.Messages = MergeMessages(
            conversation.Messages,
            baseline.Messages ?? new List<ChatMessageState>(),
            existing.Messages ?? new List<ChatMessageState>());
        existing.PendingActions = ResolveConcurrentPendingActions(
            conversation.PendingActions.Select(MapPendingAction).ToList(),
            baseline.PendingActions ?? new List<ChatPendingActionState>(),
            existing.PendingActions ?? new List<ChatPendingActionState>(),
            conversation.UpdatedUtc,
            existing.UpdatedUtc);
        existing.PendingAssistantQuestionHint = ResolveConcurrentValue(
            conversation.PendingAssistantQuestionHint,
            baseline.PendingAssistantQuestionHint,
            existing.PendingAssistantQuestionHint,
            conversation.UpdatedUtc,
            existing.UpdatedUtc);
        existing.UpdatedUtc = EnsureUtc(existing.UpdatedUtc) >= EnsureUtc(conversation.UpdatedUtc)
            ? EnsureUtc(existing.UpdatedUtc)
            : EnsureUtc(conversation.UpdatedUtc);
        return existing;
    }

    private static List<ChatPendingActionState> ResolveConcurrentPendingActions(
        IReadOnlyList<ChatPendingActionState> nativeActions,
        IReadOnlyList<ChatPendingActionState> baselineActions,
        IReadOnlyList<ChatPendingActionState> persistedActions,
        DateTime nativeUpdatedUtc,
        DateTime persistedUpdatedUtc) {
        var nativeChanged = !PendingActionsMatch(nativeActions, baselineActions);
        var persistedChanged = !PendingActionsMatch(persistedActions, baselineActions);
        if (!nativeChanged) {
            return persistedActions.Select(ClonePendingAction).ToList();
        }

        if (!persistedChanged || PendingActionsMatch(nativeActions, persistedActions)) {
            return nativeActions.Select(ClonePendingAction).ToList();
        }

        var selected = EnsureUtc(persistedUpdatedUtc) > EnsureUtc(nativeUpdatedUtc)
            ? persistedActions
            : nativeActions;
        return selected.Select(ClonePendingAction).ToList();
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

    private static string? ResolveConcurrentValue(
        string? nativeValue,
        string? baselineValue,
        string? persistedValue,
        DateTime nativeUpdatedUtc,
        DateTime persistedUpdatedUtc) {
        var nativeChanged = !string.Equals(nativeValue, baselineValue, StringComparison.Ordinal);
        var persistedChanged = !string.Equals(persistedValue, baselineValue, StringComparison.Ordinal);
        if (!nativeChanged) {
            return persistedValue;
        }

        if (!persistedChanged || string.Equals(nativeValue, persistedValue, StringComparison.Ordinal)) {
            return nativeValue;
        }

        return EnsureUtc(persistedUpdatedUtc) > EnsureUtc(nativeUpdatedUtc)
            ? persistedValue
            : nativeValue;
    }

    private static List<ChatMessageState> MergeMessages(
        IReadOnlyList<NativeChatTranscriptItem> nativeMessages,
        IReadOnlyList<ChatMessageState> baselineMessages,
        IReadOnlyList<ChatMessageState> persistedMessages) {
        var baselineMatched = new bool[baselineMessages.Count];
        var nativeAdditions = new List<ChatMessageState>();
        for (var nativeIndex = 0; nativeIndex < nativeMessages.Count; nativeIndex++) {
            var nativeMessage = MapMessage(nativeMessages[nativeIndex]);
            var matchedBaselineIndex = -1;
            for (var baselineIndex = 0; baselineIndex < baselineMessages.Count; baselineIndex++) {
                if (!baselineMatched[baselineIndex]
                    && MessageMatches(nativeMessage, baselineMessages[baselineIndex])) {
                    matchedBaselineIndex = baselineIndex;
                    break;
                }
            }

            if (matchedBaselineIndex >= 0) {
                baselineMatched[matchedBaselineIndex] = true;
            } else {
                nativeAdditions.Add(nativeMessage);
            }
        }

        var merged = persistedMessages.Select(CloneMessage).ToList();
        foreach (var addition in nativeAdditions) {
            if (!merged.Any(message => MessageMatches(message, addition))) {
                var insertionIndex = merged.FindIndex(message =>
                    EnsureUtc(message.TimeUtc) > EnsureUtc(addition.TimeUtc));
                if (insertionIndex < 0) {
                    merged.Add(CloneMessage(addition));
                } else {
                    merged.Insert(insertionIndex, CloneMessage(addition));
                }
            }
        }

        return merged;
    }

    private static NativeChatTranscriptItem MapMessage(ChatMessageState state) {
        var role = string.IsNullOrWhiteSpace(state.Role) ? "system" : state.Role.Trim().ToLowerInvariant();
        var status = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            ? string.IsNullOrWhiteSpace(state.Status) ? "Complete" : state.Status.Trim()
            : string.Empty;
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
            Model = message.Model,
            Status = message.Status
        };

    private static ChatMessageState CloneMessage(ChatMessageState message) =>
        new() {
            Role = message.Role,
            Text = message.Text,
            TimeUtc = message.TimeUtc,
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

    private static ChatPendingActionState ClonePendingAction(ChatPendingActionState action) =>
        new() {
            Id = action.Id,
            Title = action.Title,
            Request = action.Request,
            Reply = action.Reply
        };

    private static bool PendingActionsMatch(
        IReadOnlyList<ChatPendingActionState> left,
        IReadOnlyList<ChatPendingActionState> right) {
        if (left.Count != right.Count) {
            return false;
        }

        for (var i = 0; i < left.Count; i++) {
            if (!string.Equals(left[i].Id, right[i].Id, StringComparison.Ordinal)
                || !string.Equals(left[i].Title, right[i].Title, StringComparison.Ordinal)
                || !string.Equals(left[i].Request, right[i].Request, StringComparison.Ordinal)
                || !string.Equals(left[i].Reply, right[i].Reply, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }

    private static ChatConversationState CloneConversation(ChatConversationState conversation) =>
        new() {
            Id = conversation.Id,
            Title = conversation.Title,
            ThreadId = conversation.ThreadId,
            RuntimeLabel = conversation.RuntimeLabel,
            ModelLabel = conversation.ModelLabel,
            ModelOverride = conversation.ModelOverride,
            PendingAssistantQuestionHint = conversation.PendingAssistantQuestionHint,
            Messages = (conversation.Messages ?? new List<ChatMessageState>()).Select(CloneMessage).ToList(),
            PendingActions = (conversation.PendingActions ?? new List<ChatPendingActionState>())
                .Select(ClonePendingAction)
                .ToList(),
            UpdatedUtc = conversation.UpdatedUtc
        };

    private static bool ConversationStateMatches(ChatConversationState current, ChatConversationState baseline) {
        if (!string.Equals(current.Id, baseline.Id, StringComparison.Ordinal)
            || !string.Equals(current.Title, baseline.Title, StringComparison.Ordinal)
            || !string.Equals(current.ThreadId, baseline.ThreadId, StringComparison.Ordinal)
            || !string.Equals(current.RuntimeLabel, baseline.RuntimeLabel, StringComparison.Ordinal)
            || !string.Equals(current.ModelLabel, baseline.ModelLabel, StringComparison.Ordinal)
            || !string.Equals(current.ModelOverride, baseline.ModelOverride, StringComparison.Ordinal)
            || !string.Equals(current.PendingAssistantQuestionHint, baseline.PendingAssistantQuestionHint, StringComparison.Ordinal)
            || EnsureUtc(current.UpdatedUtc) != EnsureUtc(baseline.UpdatedUtc)) {
            return false;
        }

        var currentMessages = current.Messages ?? new List<ChatMessageState>();
        var baselineMessages = baseline.Messages ?? new List<ChatMessageState>();
        if (currentMessages.Count != baselineMessages.Count) {
            return false;
        }

        for (var i = 0; i < currentMessages.Count; i++) {
            if (!MessageMatches(currentMessages[i], baselineMessages[i])) {
                return false;
            }
        }

        var currentActions = current.PendingActions ?? new List<ChatPendingActionState>();
        var baselineActions = baseline.PendingActions ?? new List<ChatPendingActionState>();
        if (currentActions.Count != baselineActions.Count) {
            return false;
        }

        for (var i = 0; i < currentActions.Count; i++) {
            if (!string.Equals(currentActions[i].Id, baselineActions[i].Id, StringComparison.Ordinal)
                || !string.Equals(currentActions[i].Title, baselineActions[i].Title, StringComparison.Ordinal)
                || !string.Equals(currentActions[i].Request, baselineActions[i].Request, StringComparison.Ordinal)
                || !string.Equals(currentActions[i].Reply, baselineActions[i].Reply, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }

    private static bool MessageMatches(ChatMessageState left, ChatMessageState right) =>
        string.Equals(left.Role, right.Role, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
        && EnsureUtc(left.TimeUtc) == EnsureUtc(right.TimeUtc)
        && string.Equals(left.Model, right.Model, StringComparison.Ordinal)
        && string.Equals(
            NormalizeMessageStatus(left.Role, left.Status),
            NormalizeMessageStatus(right.Role, right.Status),
            StringComparison.Ordinal);

    private void CaptureBaseline(
        NativeConversationWorkspace workspace,
        IEnumerable<string> persistedConversationIds) {
        _baselineConversations.Clear();
        foreach (var conversation in workspace.Conversations) {
            _baselineConversations[conversation.Id] = MapConversation(conversation, existing: null);
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

    private static bool ConversationMatches(NativeConversation conversation, ChatConversationState baseline) {
        if (!string.Equals(conversation.Title, baseline.Title, StringComparison.Ordinal)
            || !string.Equals(conversation.ThreadId, baseline.ThreadId, StringComparison.Ordinal)
            || EnsureUtc(conversation.UpdatedUtc) != EnsureUtc(baseline.UpdatedUtc)) {
            return false;
        }

        var baselineMessages = baseline.Messages ?? new List<ChatMessageState>();
        if (conversation.Messages.Count != baselineMessages.Count) {
            return false;
        }

        for (var i = 0; i < conversation.Messages.Count; i++) {
            var current = conversation.Messages[i];
            var original = baselineMessages[i];
            if (!string.Equals(current.Role, original.Role, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.Text, original.Text, StringComparison.Ordinal)
                || current.CreatedAt.UtcDateTime != EnsureUtc(original.TimeUtc)
                || !string.Equals(current.Model, original.Model, StringComparison.Ordinal)
                || !string.Equals(
                    NormalizeMessageStatus(current.Role, current.Status),
                    NormalizeMessageStatus(original.Role, original.Status),
                    StringComparison.Ordinal)) {
                return false;
            }
        }

        var baselineActions = baseline.PendingActions ?? new List<ChatPendingActionState>();
        if (conversation.PendingActions.Count != baselineActions.Count
            || !string.Equals(
                conversation.PendingAssistantQuestionHint,
                baseline.PendingAssistantQuestionHint,
                StringComparison.Ordinal)) {
            return false;
        }
        for (var i = 0; i < conversation.PendingActions.Count; i++) {
            var current = conversation.PendingActions[i];
            var original = baselineActions[i];
            if (!string.Equals(current.Id, original.Id, StringComparison.Ordinal)
                || !string.Equals(current.Title, original.Title, StringComparison.Ordinal)
                || !string.Equals(current.Request, original.Request, StringComparison.Ordinal)
                || !string.Equals(current.Reply, original.Reply, StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeMessageStatus(string? role, string? status) {
        if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        var normalized = (status ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Complete" : normalized;
    }

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
