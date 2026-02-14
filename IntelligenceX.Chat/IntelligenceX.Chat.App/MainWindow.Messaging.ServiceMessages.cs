using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private void OnServiceMessage(ChatServiceMessage msg) {
        _ = _dispatcher.TryEnqueue(() => {
            var requestConversation = ResolveRequestConversation();
            switch (msg) {
                case ChatDeltaMessage delta:
                    if (!ShouldProcessLiveRequestMessage(delta.RequestId)) {
                        break;
                    }
                    if (!IsActiveTurnRequest(delta.RequestId)) {
                        // Kickoff/background deltas must not overwrite an existing assistant bubble.
                        break;
                    }

                    _assistantStreaming.Append(delta.Text);
                    _activeTurnReceivedDelta = true;
                    ReplaceLastAssistantText(requestConversation, _assistantStreaming.ToString());
                    requestConversation.UpdatedUtc = DateTime.UtcNow;
                    if (string.Equals(requestConversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
                        _ = RenderTranscriptAsync();
                    }
                    break;
                case ChatStatusMessage status:
                    if (!ShouldProcessLiveRequestMessage(status.RequestId)) {
                        break;
                    }

                    var routingInsightUpdated = ApplyToolRoutingInsight(status);
                    _ = SetActivityAsync(IsTerminalChatStatus(status.Status) ? null : FormatActivityText(status));
                    if (routingInsightUpdated) {
                        _ = PublishOptionsStateSafeAsync();
                    }
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(FormatStatusTrace(status));
                    }
                    break;
                case ChatGptLoginUrlMessage url:
                    _loginInProgress = true;
                    _ = SetStatusAsync(SessionStatus.CompleteSignInInBrowser());
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url.Url));
                    break;
                case ChatGptLoginPromptMessage prompt:
                    _ = ShowLoginPromptAsync(prompt);
                    break;
                case ChatGptLoginCompletedMessage done:
                    _loginInProgress = false;
                    _autoSignInAttempted = true;
                    _isAuthenticated = done.Ok;
                    _isConnected = _client is not null;
                    _ = SetStatusAsync(done.Ok ? SessionStatus.Connected() : SessionStatus.SignInFailed());
                    if (!done.Ok && !string.IsNullOrWhiteSpace(done.Error)) {
                        AppendSystem(SystemNotice.LoginFailed(done.Error));
                    }
                    if (done.Ok && !string.IsNullOrWhiteSpace(_queuedPromptAfterLogin)) {
                        var pending = _queuedPromptAfterLogin;
                        var pendingConversationId = _queuedPromptAfterLoginConversationId;
                        _queuedPromptAfterLogin = null;
                        _queuedPromptAfterLoginConversationId = null;
                        _ = SendPromptToConversationAsync(pending!, pendingConversationId);
                    } else if (done.Ok) {
                        _ = MaybeStartModelKickoffAsync();
                    }
                    break;
                case ErrorMessage err:
                    if (string.Equals(err.Code, "not_authenticated", StringComparison.OrdinalIgnoreCase)) {
                        _isAuthenticated = false;
                        _ = SetStatusAsync(SessionStatus.SignInRequired());
                    } else if (string.IsNullOrWhiteSpace(err.RequestId)) {
                        AppendSystem(SystemNotice.ServiceError(err.Error, err.Code));
                    } else if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.ServiceError(err.Error, err.Code));
                    }
                    break;
            }
        });
    }

    private async Task PublishOptionsStateSafeAsync() {
        try {
            if (_dispatcher.HasThreadAccess) {
                await PublishOptionsStateAsync().ConfigureAwait(false);
            } else {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_dispatcher.TryEnqueue(() => {
                    try {
                        var publishTask = PublishOptionsStateAsync();
                        if (publishTask.IsCompletedSuccessfully) {
                            tcs.TrySetResult(null);
                            return;
                        }

                        _ = publishTask.ContinueWith(task => {
                            if (task.IsCanceled) {
                                tcs.TrySetCanceled();
                                return;
                            }

                            if (task.IsFaulted) {
                                tcs.TrySetException(task.Exception?.InnerException ?? task.Exception!);
                                return;
                            }

                            tcs.TrySetResult(null);
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    } catch (Exception ex) {
                        tcs.TrySetException(ex);
                    }
                })) {
                    tcs.TrySetException(new InvalidOperationException("Failed to dispatch options refresh to UI thread."));
                }

                await tcs.Task.ConfigureAwait(false);
            }
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                try {
                    await RunOnUiThreadAsync(() => {
                        AppendSystem("Options refresh failed: " + ex.Message);
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                } catch {
                    // best-effort logging only
                }
            }
        }
    }

    private void OnClientDisconnected(ChatServiceClient client) {
        _ = _dispatcher.TryEnqueue(async () => {
            if (!ReferenceEquals(_client, client)) {
                return;
            }

            await DisposeClientAsync().ConfigureAwait(false);
            _isAuthenticated = false;
            _loginInProgress = false;
            _isConnected = false;
            _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();
            if (!DetachedServiceMode) {
                StopServiceIfOwned();
            }
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            EnsureAutoReconnectLoop();
        });
    }

    private async Task<bool> EnsureConnectedAsync() {
        if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
            _isConnected = true;
            return true;
        }

        await ConnectAsync(fromUserAction: false).ConfigureAwait(false);
        var connected = _client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false);
        _isConnected = connected;
        if (!connected) {
            await PublishSessionStateAsync().ConfigureAwait(false);
        }
        return connected;
    }

}
