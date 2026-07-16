using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native chat view model with no WebView or HTML dependency.
/// </summary>
internal sealed class NativeChatViewModel : INotifyPropertyChanged {
    private static readonly TimeSpan SignInCheckTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InteractiveSignInTimeout = TimeSpan.FromSeconds(190);

    private readonly INativeChatRuntime _runtime;
    private readonly INativeConversationStore? _conversationStore;
    private readonly Action<Action> _dispatch;
    private NativeConversationWorkspace _workspace;
    private NativeConversation _activeConversation;
    private string _draft = string.Empty;
    private string _statusText = "Ready";
    private string _signInText = "Sign-in status unknown";
    private NativeAuthenticationState _authenticationState = NativeAuthenticationState.Unknown;
    private bool _isSending;
    private bool _isCheckingSignIn;
    private string? _activeTurnRequestId;
    private CancellationTokenSource? _activeTurnCts;

    public NativeChatViewModel(
        INativeChatRuntime runtime,
        Action<Action>? dispatch = null,
        INativeConversationStore? conversationStore = null) {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatch = dispatch ?? (action => action());
        _conversationStore = conversationStore;
        _activeConversation = NativeConversation.CreateNew();
        Conversations.Add(_activeConversation);
        _workspace = new NativeConversationWorkspace(Conversations, _activeConversation.Id);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NativeChatTranscriptItem> Transcript { get; } = new();

    public ObservableCollection<NativeConversation> Conversations { get; } = new();

    public NativeConversation ActiveConversation => _activeConversation;

    public Func<Uri, Task> OpenLoginUrlAsync { get; init; } = _ => Task.CompletedTask;

    public Func<NativeLoginPrompt, Task<string?>> PromptForLoginInputAsync { get; init; } = _ => Task.FromResult<string?>(null);

    public string Draft {
        get => _draft;
        set {
            var next = value ?? string.Empty;
            if (string.Equals(_draft, next, StringComparison.Ordinal)) {
                return;
            }

            _draft = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
        }
    }

    public string StatusText {
        get => _statusText;
        private set {
            if (string.Equals(_statusText, value, StringComparison.Ordinal)) {
                return;
            }

            _statusText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool IsSending {
        get => _isSending;
        private set {
            if (_isSending == value) {
                return;
            }

            _isSending = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanStop));
        }
    }

    public NativeAuthenticationState AuthenticationState {
        get => _authenticationState;
        private set {
            if (_authenticationState == value) {
                return;
            }

            _authenticationState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanStartSignIn));
        }
    }

    public bool IsCheckingSignIn {
        get => _isCheckingSignIn;
        private set {
            if (_isCheckingSignIn == value) {
                return;
            }

            _isCheckingSignIn = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCheckSignIn));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanStartSignIn));
            if (value) {
                AuthenticationState = NativeAuthenticationState.Checking;
            }
        }
    }

    public string SignInText {
        get => _signInText;
        private set {
            if (string.Equals(_signInText, value, StringComparison.Ordinal)) {
                return;
            }

            _signInText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool CanSend => !IsSending
                           && CanUseRuntime
                           && !string.IsNullOrWhiteSpace(Draft);

    public bool CanStop => IsSending;

    public bool CanCheckSignIn => !IsCheckingSignIn;

    public bool CanStartSignIn => !IsCheckingSignIn && AuthenticationState != NativeAuthenticationState.SignedIn;

    public async Task InitializeConversationsAsync(CancellationToken cancellationToken = default) {
        if (_conversationStore is null) {
            return;
        }

        var loaded = await _conversationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await RunOnUiAsync(() => {
            Conversations.Clear();
            foreach (var conversation in loaded.Conversations) {
                Conversations.Add(conversation);
            }

            if (Conversations.Count == 0) {
                Conversations.Add(NativeConversation.CreateNew());
            }

            _workspace = new NativeConversationWorkspace(Conversations, loaded.ActiveConversationId);
            var active = FindConversation(loaded.ActiveConversationId) ?? Conversations[0];
            ActivateConversation(active);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    public async Task<bool> CreateConversationAsync() {
        if (IsSending) {
            return false;
        }

        await RunOnUiAsync(() => {
            var conversation = NativeConversation.CreateNew();
            Conversations.Insert(0, conversation);
            ActivateConversation(conversation);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await TryPersistConversationsAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SelectConversationAsync(string conversationId) {
        if (IsSending) {
            return false;
        }

        var selected = FindConversation(conversationId);
        if (selected is null || ReferenceEquals(selected, _activeConversation)) {
            return selected is not null;
        }

        await RunOnUiAsync(() => {
            ActivateConversation(selected);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await TryPersistConversationsAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<NativeLoginResult> CheckSignInAsync() {
        if (IsCheckingSignIn) {
            return new NativeLoginResult(false, null, "Sign-in check is already running.");
        }

        RunOnUi(() => IsCheckingSignIn = true);
        try {
            using var timeout = new CancellationTokenSource(SignInCheckTimeout);
            var result = await _runtime.EnsureLoginAsync(SetRuntimeStatusAsync, timeout.Token).ConfigureAwait(false);
            ApplyLoginResult(result);
            return result;
        } catch (OperationCanceledException) {
            var result = new NativeLoginResult(false, null, "Sign-in check timed out.");
            ApplyLoginResult(result);
            return result;
        } catch (Exception ex) {
            var result = new NativeLoginResult(false, null, ex.Message);
            ApplyLoginResult(result);
            return result;
        } finally {
            RunOnUi(() => IsCheckingSignIn = false);
        }
    }

    public async Task<NativeLoginResult> StartSignInAsync() {
        if (IsCheckingSignIn) {
            return new NativeLoginResult(false, null, "Sign-in is already running.");
        }

        RunOnUi(() => IsCheckingSignIn = true);
        try {
            using var timeout = new CancellationTokenSource(InteractiveSignInTimeout);
            var result = await _runtime.StartLoginAsync(
                    new NativeLoginCallbacks {
                        Status = SetRuntimeStatusAsync,
                        OpenUrl = uri => RunOnUiAsync(() => OpenLoginUrlAsync(uri)),
                        PromptForInput = prompt => RunOnUiAsync(() => PromptForLoginInputAsync(prompt))
                    },
                    timeout.Token)
                .ConfigureAwait(false);
            ApplyLoginResult(result);
            return result;
        } catch (OperationCanceledException) {
            var result = new NativeLoginResult(false, null, "Sign-in timed out.");
            ApplyLoginResult(result);
            return result;
        } catch (Exception ex) {
            var result = new NativeLoginResult(false, null, ex.Message);
            ApplyLoginResult(result);
            return result;
        } finally {
            RunOnUi(() => IsCheckingSignIn = false);
        }
    }

    public async Task<bool> SendDraftAsync() {
        var text = Draft.Trim();
        if (text.Length == 0 || IsSending || !CanUseRuntime) {
            return false;
        }

        Draft = string.Empty;
        return await SendAsync(text).ConfigureAwait(false);
    }

    public async Task<bool> SendAsync(string text) {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0 || IsSending || !CanUseRuntime) {
            return false;
        }

        var conversation = _activeConversation;
        var requestId = "native-" + Guid.NewGuid().ToString("N");
        var accumulator = new ChatTurnTextAccumulator();
        using var cts = new CancellationTokenSource();
        _activeTurnCts = cts;
        _activeTurnRequestId = requestId;
        var userItem = new NativeChatTranscriptItem("user", text, DateTimeOffset.Now);
        await RunOnUiAsync(() => {
            conversation.Messages.Add(userItem);
            Transcript.Add(userItem);
            conversation.UpdateTitleFromFirstUserMessage();
            conversation.UpdatedUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await TryPersistConversationsAsync().ConfigureAwait(false);

        var assistantItem = new NativeChatTranscriptItem("assistant", string.Empty, DateTimeOffset.Now, "Waiting for runtime...");
        await RunOnUiAsync(() => {
            conversation.Messages.Add(assistantItem);
            Transcript.Add(assistantItem);
            IsSending = true;
            StatusText = "Sending...";
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        try {
            var result = await _runtime.RunTurnAsync(
                    new ChatRequest {
                        RequestId = requestId,
                        ThreadId = conversation.ThreadId,
                        Text = text
                    },
                    (update, _) => ApplyTurnUpdateAsync(update, assistantItem, accumulator),
                    cts.Token)
                .ConfigureAwait(false);

            await RunOnUiAsync(() => {
                if (!string.IsNullOrWhiteSpace(result.Response.Text)) {
                    assistantItem.Text = result.Response.Text;
                }

                assistantItem.Status = "Complete";
                conversation.ThreadId = string.IsNullOrWhiteSpace(result.Response.ThreadId)
                    ? conversation.ThreadId
                    : result.Response.ThreadId;
                conversation.UpdatedUtc = DateTime.UtcNow;
                StatusText = "Ready";
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await TryPersistConversationsAsync().ConfigureAwait(false);
            return true;
        } catch (OperationCanceledException) {
            await RunOnUiAsync(() => {
                assistantItem.Status = "Canceled";
                if (string.IsNullOrWhiteSpace(assistantItem.Text)) {
                    assistantItem.Text = "Turn canceled.";
                }

                StatusText = "Canceled";
                conversation.UpdatedUtc = DateTime.UtcNow;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await TryPersistConversationsAsync().ConfigureAwait(false);
            return false;
        } catch (Exception ex) {
            await RunOnUiAsync(() => {
                assistantItem.Status = "Error";
                assistantItem.Text = "Native chat turn failed: " + ex.Message;
                conversation.UpdatedUtc = DateTime.UtcNow;
                StatusText = "Error";
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await TryPersistConversationsAsync().ConfigureAwait(false);
            return false;
        } finally {
            if (ReferenceEquals(_activeTurnCts, cts)) {
                _activeTurnCts = null;
                _activeTurnRequestId = null;
            }

            await RunOnUiAsync(() => {
                IsSending = false;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }

    public void CancelActiveTurn() {
        var requestId = _activeTurnRequestId;
        _activeTurnCts?.Cancel();
        if (!string.IsNullOrWhiteSpace(requestId)) {
            _ = CancelServiceTurnAsync(requestId);
        }
    }

    public void SetHostStatus(string message) =>
        StatusText = string.IsNullOrWhiteSpace(message) ? "Ready" : message.Trim();

    public void SetHostSignInText(string message) =>
        SignInText = string.IsNullOrWhiteSpace(message) ? "Sign-in status unknown" : message.Trim();

    private Task SetRuntimeStatusAsync(string message) {
        RunOnUi(() => StatusText = string.IsNullOrWhiteSpace(message) ? "Ready" : message.Trim());
        return Task.CompletedTask;
    }

    private NativeConversation? FindConversation(string? conversationId) {
        var normalized = (conversationId ?? string.Empty).Trim();
        for (var i = 0; i < Conversations.Count; i++) {
            if (string.Equals(Conversations[i].Id, normalized, StringComparison.OrdinalIgnoreCase)) {
                return Conversations[i];
            }
        }

        return null;
    }

    private void ActivateConversation(NativeConversation conversation) {
        _activeConversation = conversation;
        _workspace.ActiveConversationId = conversation.Id;
        Transcript.Clear();
        foreach (var message in conversation.Messages) {
            Transcript.Add(message);
        }

        OnPropertyChanged(nameof(ActiveConversation));
    }

    private async Task TryPersistConversationsAsync() {
        if (_conversationStore is null) {
            return;
        }

        try {
            await _conversationStore.SaveAsync(_workspace, CancellationToken.None).ConfigureAwait(false);
        } catch (Exception ex) {
            RunOnUi(() => StatusText = "History save failed: " + ex.Message);
        }
    }

    private async Task CancelServiceTurnAsync(string requestId) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _runtime.CancelTurnAsync(requestId, cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Cancellation forwarding is best-effort after the local turn is already stopped.
        } catch (Exception ex) {
            RunOnUi(() => StatusText = "Cancel request failed: " + ex.Message);
        }
    }

    private ValueTask ApplyTurnUpdateAsync(
        ChatTurnUpdate update,
        NativeChatTranscriptItem assistantItem,
        ChatTurnTextAccumulator accumulator) {
        switch (update) {
            case ChatTurnStatusUpdate status:
                var message = FormatStatus(status.Status);
                RunOnUi(() => {
                    assistantItem.Status = message;
                    StatusText = message;
                });
                break;
            case ChatTurnDeltaUpdate delta:
                var deltaDraft = accumulator.Append(delta.Delta.Text);
                RunOnUi(() => assistantItem.Text = deltaDraft);
                break;
            case ChatTurnProvisionalUpdate provisional:
                var provisionalDraft = accumulator.Append(provisional.Provisional.Text, fromProvisionalEvent: true);
                RunOnUi(() => assistantItem.Text = provisionalDraft);
                break;
            case ChatTurnInterimUpdate interim when !string.IsNullOrWhiteSpace(interim.Interim.Text):
                var currentDraft = accumulator.Snapshot();
                if (currentDraft.Length == 0 || interim.Interim.Text.StartsWith(currentDraft, StringComparison.Ordinal)) {
                    RunOnUi(() => assistantItem.Text = interim.Interim.Text);
                }
                break;
        }

        return ValueTask.CompletedTask;
    }

    private Task RunOnUiAsync(Func<Task> action) {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUi(() => {
            Task task;
            try {
                task = action();
            } catch (Exception ex) {
                completion.TrySetException(ex);
                return;
            }

            _ = CompleteUiTaskAsync(task, completion);
        });
        return completion.Task;
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action) {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUi(() => {
            Task<T> task;
            try {
                task = action();
            } catch (Exception ex) {
                completion.TrySetException(ex);
                return;
            }

            _ = CompleteUiTaskAsync(task, completion);
        });
        return completion.Task;
    }

    private static async Task CompleteUiTaskAsync(Task task, TaskCompletionSource completion) {
        try {
            await task.ConfigureAwait(true);
            completion.TrySetResult();
        } catch (Exception ex) {
            completion.TrySetException(ex);
        }
    }

    private static async Task CompleteUiTaskAsync<T>(Task<T> task, TaskCompletionSource<T> completion) {
        try {
            completion.TrySetResult(await task.ConfigureAwait(true));
        } catch (Exception ex) {
            completion.TrySetException(ex);
        }
    }

    private void ApplyLoginResult(NativeLoginResult result) {
        RunOnUi(() => {
            if (result.IsAuthenticated) {
                SignInText = string.IsNullOrWhiteSpace(result.AccountId)
                    ? "Signed in"
                    : "Signed in: " + result.AccountId!.Trim();
                AuthenticationState = NativeAuthenticationState.SignedIn;
                StatusText = "Ready";
                return;
            }

            if (result.IsCanceled) {
                SignInText = "Sign-in required";
                AuthenticationState = NativeAuthenticationState.Required;
                StatusText = "Sign-in canceled";
                return;
            }

            SignInText = string.IsNullOrWhiteSpace(result.Error)
                ? "Sign-in required"
                : "Sign-in failed";
            AuthenticationState = string.IsNullOrWhiteSpace(result.Error)
                ? NativeAuthenticationState.Required
                : NativeAuthenticationState.Failed;
            if (!string.IsNullOrWhiteSpace(result.Error)) {
                StatusText = "Sign-in failed: " + result.Error.Trim();
            }
        });
    }

    private void RunOnUi(Action action) => _dispatch(action);

    private bool CanUseRuntime => AuthenticationState is not NativeAuthenticationState.Checking
        and not NativeAuthenticationState.Required
        and not NativeAuthenticationState.Failed;

    private static string FormatStatus(ChatStatusMessage status) {
        if (!string.IsNullOrWhiteSpace(status.Message)) {
            return status.Message!;
        }

        if (!string.IsNullOrWhiteSpace(status.ToolName)) {
            return status.Status + ": " + status.ToolName;
        }

        return status.Status;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
