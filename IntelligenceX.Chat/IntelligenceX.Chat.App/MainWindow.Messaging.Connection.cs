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

    private async Task ConnectAsync(bool fromUserAction = false) {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try {
            if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
                _isConnected = true;
                StopAutoReconnectLoop();
                await SetStatusAsync(SessionStatus.ForConnectedAuth(_isAuthenticated)).ConfigureAwait(false);
                return;
            }

            _isConnected = false;
            await SetStatusAsync(SessionStatus.Connecting()).ConfigureAwait(false);
            await DisposeClientAsync().ConfigureAwait(false);
            _isAuthenticated = false;
            _loginInProgress = false;

            var pipeName = _pipeName;
            if (_serviceProcess is not null && !_serviceProcess.HasExited && !string.Equals(_servicePipeName, pipeName, StringComparison.OrdinalIgnoreCase)) {
                pipeName = _servicePipeName!;
            }

            var client = new ChatServiceClient();
            client.MessageReceived += OnServiceMessage;
            client.Disconnected += OnClientDisconnected;
            Exception? initialConnectException = null;

            try {
                await ConnectClientWithTimeoutAsync(client, pipeName, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            } catch (Exception ex) {
                initialConnectException = ex;

                if (await EnsureServiceRunningAsync(pipeName).ConfigureAwait(false)) {
                    try {
                        await ConnectClientWithTimeoutAsync(client, pipeName, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                    } catch (Exception ex2) {
                        await client.DisposeAsync().ConfigureAwait(false);
                        _isConnected = false;
                        await SetStatusAsync(SessionStatus.ConnectFailed()).ConfigureAwait(false);
                        EnsureAutoReconnectLoop();
                        if (VerboseServiceLogs || _debugMode) {
                            AppendSystem(SystemNotice.ConnectProbeFailed(FormatConnectError(initialConnectException)));
                        }
                        if (fromUserAction || _debugMode) {
                            AppendSystem(SystemNotice.ConnectFailedAfterSidecarStart(FormatConnectError(ex2)));
                        }
                        return;
                    }
                } else {
                    await client.DisposeAsync().ConfigureAwait(false);
                    _isConnected = false;
                    await SetStatusAsync(SessionStatus.ConnectFailed()).ConfigureAwait(false);
                    EnsureAutoReconnectLoop();
                    if (fromUserAction || _debugMode) {
                        AppendSystem(SystemNotice.ConnectFailed(FormatConnectError(initialConnectException)));
                        AppendSystem(SystemNotice.ServiceSidecarUnavailable());
                    }
                    return;
                }
            }

            _client = client;
            _isConnected = true;
            StopAutoReconnectLoop();
            await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);

            try {
                var hello = await _client.RequestAsync<HelloMessage>(new HelloRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                _sessionPolicy = hello.Policy;
            } catch (Exception ex) {
                _sessionPolicy = null;
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem(SystemNotice.HelloFailed(ex.Message));
                }
            }

            try {
                var toolList = await _client.RequestAsync<ToolListMessage>(new ListToolsRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                UpdateToolCatalog(toolList.Tools);
            } catch (Exception ex) {
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem(SystemNotice.ListToolsFailed(ex.Message));
                }
            }

            _ = await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false);
        } finally {
            _connectGate.Release();
        }
    }

    private async Task LoginAsync() {
        await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
    }

    private async Task ReLoginFromMenuAsync() {
        await ReLoginAsync().ConfigureAwait(false);
    }

    private async Task SwitchAccountFromMenuAsync() {
        await SwitchAccountAsync().ConfigureAwait(false);
    }

    private async Task SendPromptAsync(string text) {
        if (_isSending) {
            await SetStatusAsync(SessionStatus.PreviousRequestStillRunning()).ConfigureAwait(false);
            return;
        }

        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            await SetStatusAsync(SessionStatus.ForConnection(_isConnected, _isAuthenticated)).ConfigureAwait(false);
            return;
        }

        if (_client is null) {
            await SetStatusAsync(SessionStatus.ForConnection(_isConnected, _isAuthenticated)).ConfigureAwait(false);
            return;
        }

        text = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }

        var turn = await PrepareChatTurnAsync(text).ConfigureAwait(false);
        if (turn is null) {
            return;
        }

        var requestId = turn.RequestId;
        _isSending = true;
        _activeTurnRequestId = requestId;
        _cancelRequestedTurnRequestId = null;
        _activeRequestConversationId = turn.ConversationId;
        ClearToolRoutingInsights();
        try {
            await PublishSessionStateAsync().ConfigureAwait(false);
        } finally {
            // Ensure tools state is refreshed after routing reset even if session publish faults.
            await PublishOptionsStateSafeAsync().ConfigureAwait(false);
        }

        try {
            await ExecuteChatTurnWithReconnectAsync(turn).ConfigureAwait(false);
        } finally {
            _isSending = false;
            _activeTurnRequestId = null;
            _cancelRequestedTurnRequestId = null;
            _activeRequestConversationId = null;
            _activeTurnReceivedDelta = false;
            try {
                await PublishSessionStateAsync().ConfigureAwait(false);
            } finally {
                await PublishOptionsStateSafeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task SendPromptToConversationAsync(string text, string? conversationId) {
        var normalized = (conversationId ?? string.Empty).Trim();
        if (normalized.Length == 0 || string.Equals(normalized, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await SendPromptAsync(text).ConfigureAwait(false);
            return;
        }

        var target = FindConversationById(normalized);
        if (target is null) {
            await SendPromptAsync(text).ConfigureAwait(false);
            return;
        }

        await SwitchConversationAsync(target.Id).ConfigureAwait(false);
        await SendPromptAsync(text).ConfigureAwait(false);
    }

    private async Task CancelActiveTurnAsync() {
        if (!_isSending || string.IsNullOrWhiteSpace(_activeTurnRequestId)) {
            await SetStatusAsync(SessionStatus.NoActiveTurnToCancel()).ConfigureAwait(false);
            return;
        }

        var client = _client;
        if (client is null) {
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            return;
        }

        var chatRequestId = _activeTurnRequestId!;
        _cancelRequestedTurnRequestId = chatRequestId;
        await SetStatusAsync(SessionStatus.Canceling()).ConfigureAwait(false);
        await PublishSessionStateAsync().ConfigureAwait(false);

        try {
            var cancelRequest = new CancelChatRequest {
                RequestId = NextId(),
                ChatRequestId = chatRequestId
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _ = await client.RequestAsync<AckMessage>(cancelRequest, cts.Token).ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.CancelRequestFailed(ex.Message));
            }
        }
    }

    private async Task SendClipboardAsync() {
        var data = Clipboard.GetContent();
        if (!data.Contains(StandardDataFormats.Text)) {
            await SetStatusAsync(SessionStatus.ClipboardHasNoText()).ConfigureAwait(true);
            return;
        }

        var text = (await data.GetTextAsync().AsTask().ConfigureAwait(true)).Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            await SetStatusAsync(SessionStatus.ClipboardEmpty()).ConfigureAwait(true);
            return;
        }

        if (_pendingLoginPrompt is { } prompt) {
            await SubmitLoginPromptAsync(prompt.LoginId, prompt.PromptId, text).ConfigureAwait(true);
            _pendingLoginPrompt = null;
            return;
        }

        await SendPromptAsync(text).ConfigureAwait(true);
    }

    private void CopyStartupLogToClipboard() {
        var logPath = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "app-startup.log");
        if (!File.Exists(logPath)) {
            AppendSystem("Startup log not found: " + logPath);
            return;
        }

        string text;
        try {
            text = File.ReadAllText(logPath);
        } catch (Exception ex) {
            AppendSystem("Failed to read startup log: " + ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(text)) {
            AppendSystem("Startup log is empty: " + logPath);
            return;
        }

        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
        AppendSystem("Startup log copied to clipboard.");
    }

    private async Task RestartSidecarAsync() {
        AppendSystem("Restarting local runtime...");
        _isConnected = false;
        _isAuthenticated = false;
        _loginInProgress = false;
        StopAutoReconnectLoop();
        await SetStatusAsync(SessionStatus.Connecting()).ConfigureAwait(true);
        await DisposeClientAsync().ConfigureAwait(true);
        StopServiceIfOwned();
        await ConnectAsync(fromUserAction: true).ConfigureAwait(true);
    }

    private static string NormalizeRequestId(string? requestId) {
        return (requestId ?? string.Empty).Trim();
    }

    private bool IsActiveTurnRequest(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_activeTurnRequestId)
               && string.Equals(id, _activeTurnRequestId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsActiveKickoffRequest(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_activeKickoffRequestId)
               && string.Equals(id, _activeKickoffRequestId, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldProcessLiveRequestMessage(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return _isSending || _modelKickoffInProgress;
        }

        return IsActiveTurnRequest(id) || IsActiveKickoffRequest(id);
    }

    private static bool IsTerminalChatStatus(string? status) {
        var normalized = (status ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return string.Equals(normalized, "completed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "done", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "finished", StringComparison.OrdinalIgnoreCase);
    }

}
