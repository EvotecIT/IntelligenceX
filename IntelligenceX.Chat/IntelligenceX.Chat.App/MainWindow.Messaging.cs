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
    private async void OnWebMessageReceived(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args) {
        try {
            var raw = args.TryGetWebMessageAsString();
            if (!TryParseJsonObject(raw, out var root)) {
                return;
            }

            var type = TryGetString(root, "type");
            switch (type) {
                case "connect":
                    await ConnectAsync(fromUserAction: true).ConfigureAwait(true);
                    break;
                case "login":
                    await LoginAsync().ConfigureAwait(true);
                    break;
                case "relogin":
                    await ReLoginFromMenuAsync().ConfigureAwait(true);
                    break;
                case "switch_account":
                    await SwitchAccountFromMenuAsync().ConfigureAwait(true);
                    break;
                case "send":
                    {
                        var text = (TryGetString(root, "text") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(text)) {
                            await SendPromptAsync(text).ConfigureAwait(true);
                        }
                        break;
                    }
                case "send_clipboard":
                    await SendClipboardAsync().ConfigureAwait(true);
                    break;
                case "cancel_turn":
                    await CancelActiveTurnAsync().ConfigureAwait(true);
                    break;
                case "export":
                    ExportTranscript();
                    break;
                case "copy":
                    CopyTranscript();
                    break;
                case "clear":
                    ClearConversation();
                    break;
                case "new_conversation":
                    await NewConversationAsync().ConfigureAwait(true);
                    break;
                case "switch_conversation":
                    {
                        var conversationId = (TryGetString(root, "id") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(conversationId)) {
                            await SwitchConversationAsync(conversationId).ConfigureAwait(true);
                        }
                        break;
                    }
                case "rename_conversation":
                    {
                        var conversationId = (TryGetString(root, "id") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(conversationId)) {
                            await RenameConversationAsync(conversationId, title).ConfigureAwait(true);
                        }
                        break;
                    }
                case "delete_conversation":
                    {
                        var conversationId = (TryGetString(root, "id") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(conversationId)) {
                            await DeleteConversationAsync(conversationId).ConfigureAwait(true);
                        }
                        break;
                    }
                case "toggle_debug":
                    _debugMode = !_debugMode;
                    await SetStatusAsync(_debugMode ? SessionStatus.DebugModeOn() : SessionStatus.ForConnection(_isConnected, _isAuthenticated)).ConfigureAwait(true);
                    await PublishOptionsStateAsync().ConfigureAwait(true);
                    break;
                case "options_refresh":
                    await PublishOptionsStateAsync().ConfigureAwait(true);
                    break;
                case "debug_copy_startup_log":
                    CopyStartupLogToClipboard();
                    break;
                case "debug_restart_runtime":
                case "debug_restart_sidecar":
                    await RestartSidecarAsync().ConfigureAwait(true);
                    break;
                case "set_time_mode":
                    {
                        var mode = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(mode)) {
                            await SetTimeModeAsync(mode).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_export_save_mode":
                    {
                        var mode = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(mode)) {
                            await SetExportSaveModeAsync(mode).ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_export_default_format":
                    {
                        var format = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(format)) {
                            await SetExportDefaultFormatAsync(format).ConfigureAwait(true);
                        }
                        break;
                    }
                case "clear_export_last_directory":
                    await ClearExportLastDirectoryAsync().ConfigureAwait(true);
                    break;
                case "set_autonomy":
                    {
                        var maxRounds = TryGetString(root, "maxToolRounds");
                        var parallelMode = TryGetString(root, "parallelMode");
                        var turnTimeout = TryGetString(root, "turnTimeoutSeconds");
                        var toolTimeout = TryGetString(root, "toolTimeoutSeconds");
                        var weightedRouting = TryGetString(root, "weightedToolRouting");
                        var maxCandidates = TryGetString(root, "maxCandidateTools");
                        await SetAutonomyOverridesAsync(maxRounds, parallelMode, turnTimeout, toolTimeout, weightedRouting, maxCandidates)
                            .ConfigureAwait(true);
                        break;
                    }
                case "reset_autonomy":
                    await ResetAutonomyOverridesAsync().ConfigureAwait(true);
                    break;
                case "set_memory_enabled":
                    {
                        var enabled = TryGetBoolean(root, "enabled");
                        if (enabled.HasValue) {
                            await SetPersistentMemoryEnabledAsync(enabled.Value).ConfigureAwait(true);
                        }
                        break;
                    }
                case "add_memory_note":
                    {
                        var text = TryGetString(root, "text");
                        var weight = TryGetString(root, "weight");
                        var parsedWeight = ParseAutonomyInt(weight, min: 1, max: 5) ?? 3;
                        await AddMemoryFactAsync(text, parsedWeight).ConfigureAwait(true);
                        break;
                    }
                case "remove_memory_fact":
                    await RemoveMemoryFactAsync(TryGetString(root, "id")).ConfigureAwait(true);
                    break;
                case "clear_memory":
                    await ClearPersistentMemoryAsync().ConfigureAwait(true);
                    break;
                case "set_theme":
                    {
                        var theme = (TryGetString(root, "value") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(theme)) {
                            await SetThemePresetAsync(theme).ConfigureAwait(true);
                        }
                        break;
                    }
                case "apply_profile_update":
                    {
                        var update = new OnboardingProfileUpdate();
                        var scope = ParseProfileUpdateScope(TryGetString(root, "scope"));
                        update.Scope = scope == ProfileUpdateScope.Unspecified ? ProfileUpdateScope.Session : scope;

                        if (root.TryGetProperty("userName", out var userNameElement)) {
                            update.HasUserName = true;
                            update.UserName = userNameElement.ValueKind switch {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => userNameElement.GetString(),
                                _ => userNameElement.GetRawText()
                            };
                        }

                        if (root.TryGetProperty("persona", out var personaElement)) {
                            update.HasAssistantPersona = true;
                            update.AssistantPersona = personaElement.ValueKind switch {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => personaElement.GetString(),
                                _ => personaElement.GetRawText()
                            };
                        }

                        if (root.TryGetProperty("theme", out var themeElement)) {
                            update.HasThemePreset = true;
                            update.ThemePreset = themeElement.ValueKind switch {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => themeElement.GetString(),
                                _ => themeElement.GetRawText()
                            };
                        }

                        _ = await ApplyProfileUpdateAsync(update, autoCompleteOnboardingForProfileScope: true).ConfigureAwait(true);
                        break;
                    }
                case "set_tool_enabled":
                    {
                        var toolName = (TryGetString(root, "name") ?? string.Empty).Trim();
                        var enabled = TryGetBoolean(root, "enabled");
                        if (!string.IsNullOrWhiteSpace(toolName) && enabled.HasValue) {
                            SetToolEnabled(toolName, enabled.Value);
                            await PublishOptionsStateAsync().ConfigureAwait(true);
                            await PersistAppStateAsync().ConfigureAwait(true);
                        }
                        break;
                    }
                case "set_pack_enabled":
                    {
                        var packId = (TryGetString(root, "packId") ?? string.Empty).Trim();
                        var enabled = TryGetBoolean(root, "enabled");
                        if (!string.IsNullOrWhiteSpace(packId) && enabled.HasValue) {
                            if (SetToolPackEnabled(packId, enabled.Value)) {
                                await PublishOptionsStateAsync().ConfigureAwait(true);
                                await PersistAppStateAsync().ConfigureAwait(true);
                            } else {
                                await PublishOptionsStateAsync().ConfigureAwait(true);
                            }
                        }
                        break;
                    }
                case "save_profile":
                    {
                        var userName = (TryGetString(root, "userName") ?? string.Empty).Trim();
                        var persona = (TryGetString(root, "persona") ?? string.Empty).Trim();
                        var theme = (TryGetString(root, "theme") ?? "default").Trim();
                        await SaveProfileAsync(userName, persona, theme).ConfigureAwait(true);
                        break;
                    }
                case "switch_profile":
                    {
                        var profileName = (TryGetString(root, "name") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(profileName)) {
                            await SwitchProfileAsync(profileName).ConfigureAwait(true);
                        }
                        break;
                    }
                case "restart_onboarding":
                    await RestartOnboardingAsync().ConfigureAwait(true);
                    break;
                case "window_minimize":
                    MinimizeWindow();
                    break;
                case "window_maximize":
                    ToggleMaximizeWindow();
                    await PublishSessionStateAsync().ConfigureAwait(true);
                    break;
                case "window_close":
                    Close();
                    break;
                case "window_drag":
                    BeginDragMoveWindow();
                    break;
                case "login_prompt":
                    {
                        var loginId = (TryGetString(root, "loginId") ?? string.Empty).Trim();
                        var promptId = (TryGetString(root, "promptId") ?? string.Empty).Trim();
                        var input = (TryGetString(root, "input") ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(loginId) && !string.IsNullOrWhiteSpace(promptId) && !string.IsNullOrWhiteSpace(input)) {
                            await SubmitLoginPromptAsync(loginId, promptId, input).ConfigureAwait(true);
                        }
                        break;
                    }
                case "copy_message":
                    {
                        var indexStr = TryGetString(root, "index");
                        if (int.TryParse(indexStr, out var idx) && idx >= 0 && idx < _messages.Count) {
                            var dp = new DataPackage();
                            dp.SetText(_messages[idx].Text);
                            Clipboard.SetContent(dp);
                            Clipboard.Flush();
                        }
                        break;
                    }
                case "omd_copy":
                    {
                        var copyText = TryGetString(root, "text");
                        if (!string.IsNullOrEmpty(copyText)) {
                            var dp = new DataPackage();
                            dp.SetText(copyText);
                            Clipboard.SetContent(dp);
                            Clipboard.Flush();
                        }
                        break;
                    }
                case "export_table_artifact":
                    {
                        var format = (TryGetString(root, "format") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        var exportId = (TryGetString(root, "exportId") ?? string.Empty).Trim();
                        var outputPath = (TryGetString(root, "outputPath") ?? string.Empty).Trim();
                        if (!root.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array) {
                            await SetStatusAsync(SessionStatus.ExportFailed()).ConfigureAwait(true);
                            AppendSystem(SystemNotice.ExportMissingRowsPayload());
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(format)) {
                            await SetStatusAsync(SessionStatus.ExportFailed()).ConfigureAwait(true);
                            AppendSystem(SystemNotice.ExportMissingFormat());
                            break;
                        }

                        await ExportTableArtifactAsync(format, title, rowsElement, exportId, outputPath).ConfigureAwait(true);
                        break;
                    }
                case "pick_export_path":
                    {
                        var requestId = (TryGetString(root, "requestId") ?? string.Empty).Trim();
                        var format = (TryGetString(root, "format") ?? string.Empty).Trim();
                        var title = (TryGetString(root, "title") ?? string.Empty).Trim();
                        await PickDataViewExportPathAsync(requestId, format, title).ConfigureAwait(true);
                        break;
                    }
                case "data_view_export_action":
                    {
                        var action = (TryGetString(root, "action") ?? string.Empty).Trim();
                        var path = (TryGetString(root, "path") ?? string.Empty).Trim();
                        await HandleDataViewExportActionAsync(action, path).ConfigureAwait(true);
                        break;
                    }
            }
        } catch (Exception ex) {
            AppendSystem(SystemNotice.UiMessageError(ex.Message));
        }
    }

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
            await PublishOptionsStateAsync().ConfigureAwait(false);
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
                await PublishOptionsStateAsync().ConfigureAwait(false);
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
