using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native chat view model with no WebView or HTML dependency.
/// </summary>
internal sealed class NativeChatViewModel : INotifyPropertyChanged {
    private readonly INativeChatTurnRunner _runner;
    private readonly Action<Action> _dispatch;
    private string _draft = string.Empty;
    private string _statusText = "Ready";
    private string _signInText = "Sign-in status unknown";
    private NativeAuthenticationState _authenticationState = NativeAuthenticationState.Unknown;
    private bool _isSending;
    private bool _isCheckingSignIn;
    private string? _threadId;
    private string? _activeTurnRequestId;
    private CancellationTokenSource? _activeTurnCts;

    public NativeChatViewModel(INativeChatTurnRunner runner, Action<Action>? dispatch = null) {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _dispatch = dispatch ?? (action => action());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NativeChatTranscriptItem> Transcript { get; } = new();

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

    public bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(Draft);

    public bool CanStop => IsSending;

    public bool CanCheckSignIn => !IsCheckingSignIn;

    public bool CanStartSignIn => !IsCheckingSignIn && AuthenticationState != NativeAuthenticationState.SignedIn;

    public async Task<NativeLoginResult> CheckSignInAsync() {
        if (IsCheckingSignIn) {
            return new NativeLoginResult(false, null, "Sign-in check is already running.");
        }

        RunOnUi(() => IsCheckingSignIn = true);
        try {
            var result = await _runner.EnsureLoginAsync(SetRuntimeStatusAsync, CancellationToken.None).ConfigureAwait(false);
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
            var result = await _runner.StartLoginAsync(
                    new NativeLoginCallbacks {
                        Status = SetRuntimeStatusAsync,
                        OpenUrl = uri => RunOnUiAsync(() => OpenLoginUrlAsync(uri)),
                        PromptForInput = prompt => RunOnUiAsync(() => PromptForLoginInputAsync(prompt))
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
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
        if (text.Length == 0 || IsSending) {
            return false;
        }

        Draft = string.Empty;
        return await SendAsync(text).ConfigureAwait(false);
    }

    public async Task<bool> SendAsync(string text) {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0 || IsSending) {
            return false;
        }

        NativeChatTranscriptItem assistantItem;
        var requestId = "native-" + Guid.NewGuid().ToString("N");
        using var cts = new CancellationTokenSource();
        _activeTurnCts = cts;
        _activeTurnRequestId = requestId;
        RunOnUi(() => {
            Transcript.Add(new NativeChatTranscriptItem("user", text, DateTimeOffset.Now));
        });

        assistantItem = new NativeChatTranscriptItem("assistant", string.Empty, DateTimeOffset.Now, "Waiting for runtime...");
        RunOnUi(() => {
            Transcript.Add(assistantItem);
            IsSending = true;
            StatusText = "Sending...";
        });

        try {
            var result = await _runner.SendAsync(
                    new NativeChatTurnRequest(requestId, text, _threadId),
                    new NativeChatTurnCallbacks {
                        Status = message => {
                            RunOnUi(() => {
                                assistantItem.Status = message;
                                StatusText = message;
                            });
                            return Task.CompletedTask;
                        },
                        Delta = delta => {
                            RunOnUi(() => assistantItem.AppendText(delta));
                            return Task.CompletedTask;
                        },
                        Interim = interim => {
                            RunOnUi(() => {
                                if (!string.IsNullOrWhiteSpace(interim)) {
                                    assistantItem.Text = interim;
                                }
                            });
                            return Task.CompletedTask;
                        }
                    },
                    cts.Token)
                .ConfigureAwait(false);

            RunOnUi(() => {
                if (!string.IsNullOrWhiteSpace(result.Text)) {
                    assistantItem.Text = result.Text;
                }

                assistantItem.Status = "Complete";
                _threadId = string.IsNullOrWhiteSpace(result.ThreadId) ? _threadId : result.ThreadId;
                StatusText = "Ready";
            });
            return true;
        } catch (OperationCanceledException) {
            RunOnUi(() => {
                assistantItem.Status = "Canceled";
                if (string.IsNullOrWhiteSpace(assistantItem.Text)) {
                    assistantItem.Text = "Turn canceled.";
                }

                StatusText = "Canceled";
            });
            return false;
        } catch (Exception ex) {
            RunOnUi(() => {
                assistantItem.Status = "Error";
                assistantItem.Text = "Native chat turn failed: " + ex.Message;
                StatusText = "Error";
            });
            return false;
        } finally {
            if (ReferenceEquals(_activeTurnCts, cts)) {
                _activeTurnCts = null;
                _activeTurnRequestId = null;
            }

            RunOnUi(() => IsSending = false);
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

    private async Task CancelServiceTurnAsync(string requestId) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _runner.CancelAsync(requestId, cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Cancellation forwarding is best-effort after the local turn is already stopped.
        } catch (Exception ex) {
            RunOnUi(() => StatusText = "Cancel request failed: " + ex.Message);
        }
    }

    private Task RunOnUiAsync(Func<Task> action) {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUi(async () => {
            try {
                await action().ConfigureAwait(true);
                completion.TrySetResult();
            } catch (Exception ex) {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action) {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUi(async () => {
            try {
                completion.TrySetResult(await action().ConfigureAwait(true));
            } catch (Exception ex) {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
