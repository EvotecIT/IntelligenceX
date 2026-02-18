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
    internal static TimeSpan ResolveStartupInitialPipeConnectTimeout(bool fromUserAction, bool hasTrackedRunningServiceProcess) {
        if (fromUserAction || hasTrackedRunningServiceProcess) {
            return StartupInitialPipeConnectTimeout;
        }

        return StartupInitialPipeConnectColdStartTimeout;
    }

    internal static bool ShouldDeferStartupModelProfileSync(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    internal static bool ShouldDeferStartupHelloProbe(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    internal static bool ShouldDeferStartupToolCatalogSync(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    internal static bool ShouldDeferStartupAuthRefresh(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    private async Task ConnectAsync(bool fromUserAction = false) {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try {
            var captureStartupPhaseTelemetry = !fromUserAction && Volatile.Read(ref _startupFlowState) == 1;
            void LogStartupConnectPhase(string phase, string state) {
                if (captureStartupPhaseTelemetry) {
                    StartupLog.Write("StartupConnect." + phase + " " + state);
                }
            }

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
            var hasTrackedRunningServiceProcess = _serviceProcess is not null && !_serviceProcess.HasExited;
            var initialPipeConnectTimeout = ResolveStartupInitialPipeConnectTimeout(fromUserAction, hasTrackedRunningServiceProcess);

            try {
                LogStartupConnectPhase("pipe_connect.initial", "begin");
                await ConnectClientWithTimeoutAsync(client, pipeName, initialPipeConnectTimeout).ConfigureAwait(false);
                LogStartupConnectPhase("pipe_connect.initial", "done");
            } catch (Exception ex) {
                LogStartupConnectPhase("pipe_connect.initial", "failed");
                initialConnectException = ex;

                LogStartupConnectPhase("ensure_sidecar", "begin");
                if (await EnsureServiceRunningAsync(pipeName).ConfigureAwait(false)) {
                    LogStartupConnectPhase("ensure_sidecar", "done");
                    Exception? sidecarConnectException = null;
                    LogStartupConnectPhase("pipe_connect.retry", "begin");
                    for (var attempt = 0; attempt < StartupConnectRetryTimeouts.Length; attempt++) {
                        try {
                            await ConnectClientWithTimeoutAsync(client, pipeName, StartupConnectRetryTimeouts[attempt]).ConfigureAwait(false);
                            sidecarConnectException = null;
                            break;
                        } catch (Exception ex2) {
                            sidecarConnectException = ex2;
                            if (_serviceProcess is { HasExited: true }) {
                                break;
                            }

                            if (attempt + 1 < StartupConnectRetryTimeouts.Length) {
                                await Task.Delay(StartupConnectRetryDelay).ConfigureAwait(false);
                            }
                        }
                    }

                    if (sidecarConnectException is not null) {
                        LogStartupConnectPhase("pipe_connect.retry", "failed");
                        await client.DisposeAsync().ConfigureAwait(false);
                        _isConnected = false;
                        await SetStatusAsync(SessionStatus.ConnectFailed()).ConfigureAwait(false);
                        EnsureAutoReconnectLoop();
                        if (VerboseServiceLogs || _debugMode) {
                            AppendSystem(SystemNotice.ConnectProbeFailed(FormatConnectError(initialConnectException)));
                        }
                        if (fromUserAction || _debugMode) {
                            AppendSystem(SystemNotice.ConnectFailedAfterSidecarStart(FormatConnectError(sidecarConnectException)));
                        }
                        return;
                    }
                    LogStartupConnectPhase("pipe_connect.retry", "done");
                } else {
                    LogStartupConnectPhase("ensure_sidecar", "failed");
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

            var deferStartupMetadataSync = ShouldDeferStartupHelloProbe(captureStartupPhaseTelemetry)
                                           || ShouldDeferStartupToolCatalogSync(captureStartupPhaseTelemetry);
            if (deferStartupMetadataSync) {
                _sessionPolicy = null;
                LogStartupConnectPhase("hello", "deferred");
                LogStartupConnectPhase("list_tools", "deferred");
                QueueDeferredStartupConnectMetadataSync();
            } else {
                try {
                    LogStartupConnectPhase("hello", "begin");
                    var hello = await _client.RequestAsync<HelloMessage>(new HelloRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                    _sessionPolicy = hello.Policy;
                    LogStartupConnectPhase("hello", "done");
                } catch (Exception ex) {
                    LogStartupConnectPhase("hello", "failed");
                    _sessionPolicy = null;
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.HelloFailed(ex.Message));
                    }
                }
            }

            if (!deferStartupMetadataSync) {
                try {
                    LogStartupConnectPhase("list_tools", "begin");
                    var toolList = await _client.RequestAsync<ToolListMessage>(new ListToolsRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                    UpdateToolCatalog(toolList.Tools);
                    LogStartupConnectPhase("list_tools", "done");
                } catch (Exception ex) {
                    LogStartupConnectPhase("list_tools", "failed");
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.ListToolsFailed(ex.Message));
                    }
                }
            }

            AppendStartupToolHealthWarningsFromPolicy();
            AppendUnavailablePacksFromPolicy();

            if (ShouldDeferStartupAuthRefresh(captureStartupPhaseTelemetry)) {
                LogStartupConnectPhase("auth_refresh", "deferred");
            } else {
                LogStartupConnectPhase("auth_refresh", "begin");
                try {
                    _ = await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false);
                    LogStartupConnectPhase("auth_refresh", "done");
                } catch {
                    LogStartupConnectPhase("auth_refresh", "failed");
                    throw;
                }
            }
            if (ShouldDeferStartupModelProfileSync(captureStartupPhaseTelemetry)) {
                LogStartupConnectPhase("model_profile_sync", "deferred");
                QueueDeferredStartupModelProfileSync();
            } else {
                try {
                    LogStartupConnectPhase("model_profile_sync", "begin");
                    await SyncConnectedServiceProfileAndModelsAsync(
                        forceModelRefresh: false,
                        setProfileNewThread: false,
                        appendWarnings: false).ConfigureAwait(false);
                    LogStartupConnectPhase("model_profile_sync", "done");
                } catch (Exception ex) {
                    LogStartupConnectPhase("model_profile_sync", "failed");
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem("Model/profile sync failed: " + ex.Message);
                    }
                }
            }
        } finally {
            _connectGate.Release();
        }
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

    private bool IsLatestTurnRequest(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_latestTurnRequestId)
               && string.Equals(id, _latestTurnRequestId, StringComparison.OrdinalIgnoreCase);
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

    private void AppendUnavailablePacksFromPolicy() {
        var packs = _sessionPolicy?.Packs;
        if (packs is not { Length: > 0 }) {
            return;
        }

        var unavailable = packs
            .Where(static pack => !pack.Enabled && !string.IsNullOrWhiteSpace(pack.DisabledReason))
            .Select(static pack => new {
                Id = (pack.Id ?? string.Empty).Trim(),
                Name = (pack.Name ?? string.Empty).Trim(),
                Reason = (pack.DisabledReason ?? string.Empty).Trim()
            })
            .Where(static pack => pack.Reason.Length > 0)
            .DistinctBy(static pack => pack.Id + "|" + pack.Reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unavailable.Length == 0) {
            return;
        }

        var signaturePayload = unavailable
            .OrderBy(static pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Reason, StringComparer.OrdinalIgnoreCase)
            .Select(static pack => new { pack.Id, pack.Reason })
            .ToArray();
        var signature = JsonSerializer.Serialize(signaturePayload);
        if (!_startupUnavailablePackSignatures.Add(signature)) {
            return;
        }

        const int maxShown = 4;
        var shown = unavailable.Length <= maxShown
            ? unavailable
            : unavailable.Take(maxShown).ToArray();

        var lines = new List<string>(shown.Length + 6) {
            "[warning] Some tool packs are unavailable",
            string.Empty,
            $"Found {unavailable.Length} unavailable pack(s):"
        };

        for (var i = 0; i < shown.Length; i++) {
            var pack = shown[i];
            var label = string.IsNullOrWhiteSpace(pack.Name) ? pack.Id : pack.Name;
            lines.Add("- " + label + ": " + pack.Reason);
        }

        if (unavailable.Length > shown.Length) {
            lines.Add($"- +{unavailable.Length - shown.Length} more");
        }

        lines.Add(string.Empty);
        lines.Add("Open Options > Tools to see pack availability details.");

        AppendSystem(string.Join(Environment.NewLine, lines));
    }

    private void AppendStartupToolHealthWarningsFromPolicy() {
        var warnings = _sessionPolicy?.StartupWarnings;
        if (warnings is not { Length: > 0 }) {
            return;
        }

        var toolHealthWarnings = warnings
            .Where(static warning => warning.Contains("[tool health]", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (toolHealthWarnings.Length == 0) {
            return;
        }

        var signature = string.Join("|", toolHealthWarnings.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        if (!_startupToolHealthWarningSignatures.Add(signature)) {
            return;
        }

        const int maxShown = 4;
        var shown = toolHealthWarnings.Length <= maxShown
            ? toolHealthWarnings
            : toolHealthWarnings.Take(maxShown).ToArray();

        var lines = new List<string>(shown.Length + 5) {
            "[warning] Tool health checks need attention",
            string.Empty,
            $"Found {toolHealthWarnings.Length} startup tool health warning(s):"
        };
        for (var i = 0; i < shown.Length; i++) {
            lines.Add("- " + shown[i].Trim());
        }
        if (toolHealthWarnings.Length > shown.Length) {
            lines.Add($"- +{toolHealthWarnings.Length - shown.Length} more");
        }
        lines.Add(string.Empty);
        lines.Add("Check the runtime policy panel for the full startup warning list.");

        AppendSystem(string.Join(Environment.NewLine, lines));
    }

}
