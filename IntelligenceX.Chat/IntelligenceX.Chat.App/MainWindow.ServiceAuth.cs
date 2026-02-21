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
using IntelligenceX.Chat.App.Launch;
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
    private static readonly TimeSpan EnsureLoginProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EnsureLoginFreshProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EnsureLoginFastPathProbeTimeout = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan EnsureLoginProbeCacheTtl = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan EnsureLoginUnknownProbeRetryDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan RuntimeAccountPinResetTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RuntimeAccountPinResetFastTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RuntimeAccountPinResetSwitchPreflightTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan RuntimeAccountPinResetRecoveryTimeout = TimeSpan.FromSeconds(4);
    private enum EnsureLoginProbeState {
        Unknown = 0,
        Authenticated = 1,
        Unauthenticated = 2
    }

    private readonly record struct EnsureLoginProbeSnapshot(
        EnsureLoginProbeState State,
        string? AccountId,
        LoginStatusMessage? LoginStatus,
        Exception? Error,
        bool IsTimeout,
        bool FromCache);

    private readonly SemaphoreSlim _ensureLoginProbeGate = new(1, 1);
    private bool _ensureLoginProbeCacheHasValue;
    private bool _ensureLoginProbeCachedIsAuthenticated;
    private string? _ensureLoginProbeCachedAccountId;
    private DateTime _ensureLoginProbeCachedAtUtc;

    private enum DispatchAuthenticationProbeOutcome {
        Authenticated = 0,
        Unauthenticated = 1,
        Unknown = 2
    }

    private void ResetEnsureLoginProbeCache() {
        _ensureLoginProbeCacheHasValue = false;
        _ensureLoginProbeCachedIsAuthenticated = false;
        _ensureLoginProbeCachedAccountId = null;
        _ensureLoginProbeCachedAtUtc = DateTime.MinValue;
    }

    private bool TryGetEnsureLoginProbeCache(bool requireFreshProbe, out EnsureLoginProbeSnapshot snapshot) {
        snapshot = default;
        if (requireFreshProbe || !_ensureLoginProbeCacheHasValue) {
            return false;
        }

        var elapsed = DateTime.UtcNow - _ensureLoginProbeCachedAtUtc;
        if (elapsed < TimeSpan.Zero || elapsed > EnsureLoginProbeCacheTtl) {
            return false;
        }

        var state = _ensureLoginProbeCachedIsAuthenticated
            ? EnsureLoginProbeState.Authenticated
            : EnsureLoginProbeState.Unauthenticated;
        snapshot = new EnsureLoginProbeSnapshot(
            State: state,
            AccountId: _ensureLoginProbeCachedAccountId,
            LoginStatus: null,
            Error: null,
            IsTimeout: false,
            FromCache: true);
        return true;
    }

    private void CacheEnsureLoginProbeSnapshot(EnsureLoginProbeState state, string? accountId) {
        if (state == EnsureLoginProbeState.Unknown) {
            ResetEnsureLoginProbeCache();
            return;
        }

        _ensureLoginProbeCacheHasValue = true;
        _ensureLoginProbeCachedIsAuthenticated = state == EnsureLoginProbeState.Authenticated;
        _ensureLoginProbeCachedAccountId = _ensureLoginProbeCachedIsAuthenticated ? (accountId ?? string.Empty).Trim() : null;
        _ensureLoginProbeCachedAtUtc = DateTime.UtcNow;
    }

    private bool HasExplicitUnauthenticatedEnsureLoginProbeSnapshot() {
        return _ensureLoginProbeCacheHasValue && !_ensureLoginProbeCachedIsAuthenticated;
    }

    private async Task<EnsureLoginProbeSnapshot> ProbeEnsureLoginAsync(TimeSpan timeout, bool requireFreshProbe) {
        if (TryGetEnsureLoginProbeCache(requireFreshProbe, out var cached)) {
            return cached;
        }

        await _ensureLoginProbeGate.WaitAsync().ConfigureAwait(false);
        try {
            if (TryGetEnsureLoginProbeCache(requireFreshProbe, out cached)) {
                return cached;
            }

            var client = _client;
            if (client is null) {
                return new EnsureLoginProbeSnapshot(
                    State: EnsureLoginProbeState.Unknown,
                    AccountId: null,
                    LoginStatus: null,
                    Error: null,
                    IsTimeout: false,
                    FromCache: false);
            }

            try {
                using var probeCts = timeout > TimeSpan.Zero ? new CancellationTokenSource(timeout) : null;
                var probeToken = probeCts?.Token ?? CancellationToken.None;
                var login = await client.RequestAsync<LoginStatusMessage>(new EnsureLoginRequest { RequestId = NextId() }, probeToken).ConfigureAwait(false);
                var accountId = login.IsAuthenticated ? (login.AccountId ?? string.Empty).Trim() : null;
                var state = login.IsAuthenticated ? EnsureLoginProbeState.Authenticated : EnsureLoginProbeState.Unauthenticated;
                CacheEnsureLoginProbeSnapshot(state, accountId);
                return new EnsureLoginProbeSnapshot(
                    State: state,
                    AccountId: accountId,
                    LoginStatus: login,
                    Error: null,
                    IsTimeout: false,
                    FromCache: false);
            } catch (OperationCanceledException) {
                return new EnsureLoginProbeSnapshot(
                    State: EnsureLoginProbeState.Unknown,
                    AccountId: null,
                    LoginStatus: null,
                    Error: null,
                    IsTimeout: true,
                    FromCache: false);
            } catch (Exception ex) {
                return new EnsureLoginProbeSnapshot(
                    State: EnsureLoginProbeState.Unknown,
                    AccountId: null,
                    LoginStatus: null,
                    Error: ex,
                    IsTimeout: false,
                    FromCache: false);
            }
        } finally {
            _ensureLoginProbeGate.Release();
        }
    }

    private bool IsNativeRuntimeTransport() {
        return string.Equals(_localProviderTransport, TransportNative, StringComparison.OrdinalIgnoreCase);
    }

    private bool RequiresInteractiveSignInForCurrentTransport() {
        return IsNativeRuntimeTransport();
    }

    private bool IsEffectivelyAuthenticatedForCurrentTransport() {
        return !RequiresInteractiveSignInForCurrentTransport() || _isAuthenticated;
    }

    private void ApplyNonNativeAuthenticationStateIfNeeded() {
        if (RequiresInteractiveSignInForCurrentTransport()) {
            return;
        }

        _isAuthenticated = true;
        _authenticatedAccountId = null;
        _loginInProgress = false;
    }

    private async Task<bool> IsClientAliveAsync(
        ChatServiceClient client,
        TimeSpan? probeTimeout = null,
        TimeSpan? cacheTtl = null) {
        var effectiveCacheTtl = cacheTtl.GetValueOrDefault(AliveProbeCacheTtl);
        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_aliveProbeSync) {
            if (ReferenceEquals(client, _aliveProbeClient)
                && _aliveProbeTicksUtc > 0
                && effectiveCacheTtl > TimeSpan.Zero
                && nowTicks - _aliveProbeTicksUtc <= effectiveCacheTtl.Ticks) {
                return true;
            }
        }

        try {
            var effectiveProbeTimeout = probeTimeout.GetValueOrDefault(AliveProbeTimeout);
            using var cts = effectiveProbeTimeout > TimeSpan.Zero ? new CancellationTokenSource(effectiveProbeTimeout) : null;
            _ = await client.RequestAsync<HelloMessage>(
                    new HelloRequest { RequestId = NextId() },
                    cts?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);
            lock (_aliveProbeSync) {
                _aliveProbeClient = client;
                _aliveProbeTicksUtc = DateTime.UtcNow.Ticks;
            }
            return true;
        } catch {
            lock (_aliveProbeSync) {
                if (ReferenceEquals(client, _aliveProbeClient)) {
                    _aliveProbeClient = null;
                    _aliveProbeTicksUtc = 0;
                }
            }
            return false;
        }
    }

    private void EnsureAutoReconnectLoop() {
        lock (_autoReconnectSync) {
            if (_autoReconnectTask is { IsCompleted: false }) {
                return;
            }

            _autoReconnectCts?.Cancel();
            _autoReconnectCts = new CancellationTokenSource();
            var token = _autoReconnectCts.Token;
            _autoReconnectTask = Task.Run(() => AutoReconnectLoopAsync(token), token);
        }
    }

    private void StopAutoReconnectLoop() {
        CancellationTokenSource? cts;
        lock (_autoReconnectSync) {
            cts = _autoReconnectCts;
            _autoReconnectCts = null;
            _autoReconnectTask = null;
        }

        if (cts is null) {
            return;
        }

        try {
            cts.Cancel();
        } catch {
            // Ignore.
        } finally {
            cts.Dispose();
        }
    }

    private async Task AutoReconnectLoopAsync(CancellationToken cancellationToken) {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested) {
            if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
                return;
            }

            var delay = AutoReconnectBackoffDelays[Math.Min(attempt, AutoReconnectBackoffDelays.Length - 1)];
            attempt++;

            try {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                return;
            }

            if (cancellationToken.IsCancellationRequested) {
                return;
            }

            await ConnectAsync(fromUserAction: false, connectBudgetOverride: AutoReconnectConnectBudget).ConfigureAwait(false);

            if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
                return;
            }
        }
    }

    private async Task AppendSystemBestEffortAsync(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        try {
            await RunOnUiThreadAsync(() => {
                AppendSystem(normalized);
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        } catch {
            // Best-effort diagnostic only.
        }
    }

    private Task AppendSystemBestEffortAsync(SystemNotice notice) {
        return AppendSystemBestEffortAsync(SystemNoticeFormatter.Format(notice));
    }

    private async Task<bool> RefreshAuthenticationStateAsync(
        bool updateStatus,
        bool requireFreshProbe = false,
        bool allowCachedAuthenticatedFallback = true,
        TimeSpan? probeTimeout = null) {
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
            if (updateStatus) {
                await SetStatusAsync(SessionStatus.ForConnection(_isConnected, isAuthenticated: true)).ConfigureAwait(false);
                await PublishOptionsStateAsync().ConfigureAwait(false);
            }

            return true;
        }

        var client = _client;
        if (client is null) {
            ResetEnsureLoginProbeCache();
            _isAuthenticated = false;
            _authenticatedAccountId = null;
            if (updateStatus) {
                await PublishSessionStateAsync().ConfigureAwait(false);
            }
            return false;
        }

        var timeout = probeTimeout.GetValueOrDefault(requireFreshProbe ? EnsureLoginFreshProbeTimeout : EnsureLoginProbeTimeout);
        var probe = await ProbeEnsureLoginAsync(timeout, requireFreshProbe).ConfigureAwait(false);
        switch (probe.State) {
            case EnsureLoginProbeState.Authenticated:
                _isAuthenticated = true;
                _authenticatedAccountId = (probe.AccountId ?? string.Empty).Trim();
                CaptureAuthenticatedAccountIntoActiveSlot();
                if (probe.LoginStatus is { IsAuthenticated: true } loginStatus) {
                    UpdateAccountUsageFromNativeLoginStatus(loginStatus);
                }
                if (!probe.FromCache) {
                    QueuePersistAppState();
                }

                if (updateStatus) {
                    await SetStatusAsync(SessionStatus.ForConnectedAuth(isAuthenticated: true)).ConfigureAwait(false);
                    await PublishOptionsStateAsync().ConfigureAwait(false);
                }

                return true;
            case EnsureLoginProbeState.Unauthenticated:
                _isAuthenticated = false;
                _authenticatedAccountId = null;
                if (updateStatus) {
                    await SetStatusAsync(SessionStatus.ForConnectedAuth(isAuthenticated: false)).ConfigureAwait(false);
                    await PublishOptionsStateAsync().ConfigureAwait(false);
                }

                return false;
            default:
                // Transient ensure_login probe failures should not automatically force a new browser login.
                if (probe.Error is not null && (VerboseServiceLogs || _debugMode)) {
                    await AppendSystemBestEffortAsync(SystemNotice.EnsureLoginFailed(probe.Error.Message)).ConfigureAwait(false);
                }

                if (updateStatus) {
                    await PublishSessionStateAsync().ConfigureAwait(false);
                }

                return allowCachedAuthenticatedFallback && _isAuthenticated;
        }
    }

    private async Task<DispatchAuthenticationProbeOutcome> ProbeAuthenticationStateForDispatchAsync(TimeSpan? probeTimeout = null) {
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
            return DispatchAuthenticationProbeOutcome.Authenticated;
        }

        var client = _client;
        if (client is null) {
            ResetEnsureLoginProbeCache();
            _isAuthenticated = false;
            _authenticatedAccountId = null;
            return DispatchAuthenticationProbeOutcome.Unknown;
        }

        var timeout = probeTimeout.GetValueOrDefault(EnsureLoginFastPathProbeTimeout);
        var probe = await ProbeEnsureLoginAsync(timeout, requireFreshProbe: false).ConfigureAwait(false);
        switch (probe.State) {
            case EnsureLoginProbeState.Authenticated:
                _isAuthenticated = true;
                _authenticatedAccountId = (probe.AccountId ?? string.Empty).Trim();
                CaptureAuthenticatedAccountIntoActiveSlot();
                if (probe.LoginStatus is { IsAuthenticated: true } loginStatus) {
                    UpdateAccountUsageFromNativeLoginStatus(loginStatus);
                }
                if (!probe.FromCache) {
                    QueuePersistAppState();
                }

                return DispatchAuthenticationProbeOutcome.Authenticated;
            case EnsureLoginProbeState.Unauthenticated:
                _isAuthenticated = false;
                _authenticatedAccountId = null;
                return DispatchAuthenticationProbeOutcome.Unauthenticated;
            default:
                return DispatchAuthenticationProbeOutcome.Unknown;
        }
    }

    private async Task<bool> StartLoginFlowIfNeededAsync(bool forceInteractive = false, bool skipPreLoginAuthProbe = false) {
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
            if (!await EnsureConnectedAsync(connectBudgetOverride: StartupConnectBudget).ConfigureAwait(false)) {
                return false;
            }

            _isConnected = _client is not null;
            await SetStatusAsync(SessionStatus.ForConnection(_isConnected, isAuthenticated: true)).ConfigureAwait(false);
            return _isConnected;
        }

        if (!forceInteractive && _isAuthenticated) {
            _isConnected = _client is not null;
            await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);
            return true;
        }

        if (_loginInProgress) {
            var queuedCount = GetQueuedPromptAfterLoginCount();
            var waitingText = queuedCount > 0
                ? $"Waiting for sign-in... ({queuedCount}/{MaxQueuedTurns} queued)"
                : SessionStatusFormatter.Format(SessionStatus.WaitingForSignIn());
            await SetStatusAsync(waitingText).ConfigureAwait(false);
            return true;
        }

        if (!await EnsureConnectedAsync(connectBudgetOverride: StartupConnectBudget).ConfigureAwait(false)) {
            return false;
        }

        if (!forceInteractive && !skipPreLoginAuthProbe) {
            if (await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false)) {
                _isConnected = _client is not null;
                await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);
                return true;
            }

            // Retry only when the first probe was inconclusive.
            // Explicit unauthenticated probes are cached and should not incur extra wait.
            if (!_ensureLoginProbeCacheHasValue) {
                if (EnsureLoginUnknownProbeRetryDelay > TimeSpan.Zero) {
                    await Task.Delay(EnsureLoginUnknownProbeRetryDelay).ConfigureAwait(false);
                }

                if (await RefreshAuthenticationStateAsync(
                        updateStatus: true,
                        requireFreshProbe: true,
                        allowCachedAuthenticatedFallback: false,
                        probeTimeout: EnsureLoginFastPathProbeTimeout).ConfigureAwait(false)) {
                    _isConnected = _client is not null;
                    await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);
                    return true;
                }
            }
        }

        var client = _client;
        if (client is null) {
            return false;
        }

        try {
            _loginInProgress = true;
            ResetEnsureLoginProbeCache();
            _isConnected = true;
            _isAuthenticated = false;
            _authenticatedAccountId = null;
            await SetStatusAsync(SessionStatus.OpeningSignIn()).ConfigureAwait(false);
            await client.RequestAsync<ChatGptLoginStartedMessage>(new StartChatGptLoginRequest {
                RequestId = NextId(),
                UseLocalListener = true,
                TimeoutSeconds = 180
            }, CancellationToken.None).ConfigureAwait(false);
            return true;
        } catch (Exception ex) {
            _loginInProgress = false;
            _isConnected = _client is not null;
            ResetEnsureLoginProbeCache();
            _authenticatedAccountId = null;
            await SetStatusAsync(SessionStatus.SignInFailed()).ConfigureAwait(false);
            await AppendSystemBestEffortAsync(SystemNotice.SignInFailed(ex.Message)).ConfigureAwait(false);
            return false;
        }
    }

    private Task<bool> ReLoginAsync() {
        return StartLoginFlowIfNeededAsync(forceInteractive: true);
    }

    private async Task<bool> SwitchAccountAsync() {
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            await SetStatusAsync("Account switching is only available for ChatGPT native runtime.").ConfigureAwait(false);
            return false;
        }

        await ClearNativeAccountPinForSwitchAsync().ConfigureAwait(false);
        ResetEnsureLoginProbeCache();
        _isAuthenticated = false;
        _authenticatedAccountId = null;
        _loginInProgress = false;
        await SetStatusAsync("Opening account chooser...").ConfigureAwait(false);
        return await StartLoginFlowIfNeededAsync(forceInteractive: true).ConfigureAwait(false);
    }

    private async Task ClearNativeAccountPinForSwitchAsync() {
        var runtimeAccountId = NormalizeLocalProviderOpenAIAccountId(_localProviderOpenAIAccountId);
        var hadPinnedAccount = runtimeAccountId.Length > 0;

        if (hadPinnedAccount) {
            // Keep slot-bound account history for UX/account telemetry, but clear the live
            // runtime account pin so the next login can bind to whichever account authenticates.
            _localProviderOpenAIAccountId = string.Empty;
            SyncNativeAccountSlotsToAppState();
            QueuePersistAppState();
        }

        _ = await TryClearNativeRuntimeAccountPinAsync(RuntimeAccountPinResetSwitchPreflightTimeout).ConfigureAwait(false);
    }

    private async Task<bool> TryClearNativeRuntimeAccountPinAsync(TimeSpan? timeout = null) {
        var client = _client;
        if (client is null) {
            return false;
        }

        var effectiveTimeout = timeout.GetValueOrDefault(RuntimeAccountPinResetTimeout);
        using var cts = effectiveTimeout > TimeSpan.Zero
            ? new CancellationTokenSource(effectiveTimeout)
            : null;
        try {
            _ = await client.ApplyRuntimeSettingsAsync(
                    openAIAccountId: string.Empty,
                    cancellationToken: cts?.Token ?? CancellationToken.None)
                .ConfigureAwait(false);
            return true;
        } catch (OperationCanceledException) when (cts is not null && cts.IsCancellationRequested) {
            // Timeout while applying runtime settings should be non-fatal for auth recovery.
            if (VerboseServiceLogs || _debugMode) {
                await AppendSystemBestEffortAsync("Runtime account pin reset timed out.")
                    .ConfigureAwait(false);
            }

            return false;
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                await AppendSystemBestEffortAsync("Runtime account pin reset failed: " + ex.Message)
                    .ConfigureAwait(false);
            }

            return false;
        }
    }

    private async Task ShowLoginPromptAsync(ChatGptLoginPromptMessage prompt) {
        if (!_webViewReady) {
            _pendingLoginPrompt = (prompt.LoginId, prompt.PromptId, prompt.Prompt);
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            loginId = prompt.LoginId,
            promptId = prompt.PromptId,
            promptText = prompt.Prompt
        });

        var js = $"(function(){{var p={payload};var v=window.prompt(p.promptText||'Enter value:','');if(v!==null&&v.trim()){{window.chrome.webview.postMessage(JSON.stringify({{type:'login_prompt',loginId:p.loginId,promptId:p.promptId,input:v}}));}}}})();";
        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync(js).AsTask()).ConfigureAwait(false);
        } catch {
            _pendingLoginPrompt = (prompt.LoginId, prompt.PromptId, prompt.Prompt);
        }
    }

    private async Task SubmitLoginPromptAsync(string loginId, string promptId, string input) {
        var client = _client;
        if (client is null) {
            return;
        }

        try {
            await client.SendAsync(new ChatGptLoginPromptResponseRequest {
                RequestId = NextId(),
                LoginId = loginId,
                PromptId = promptId,
                Input = input
            }, CancellationToken.None).ConfigureAwait(true);
        } catch (Exception ex) {
            AppendSystem(SystemNotice.LoginSubmitFailed(ex.Message));
        }
    }

    private async Task<bool> EnsureServiceRunningAsync(string pipeName) {
        if (_serviceProcess is not null && !_serviceProcess.HasExited) {
            return true;
        }

        var serviceSourceDir = ResolveServiceSourceDirectory();
        if (string.IsNullOrWhiteSpace(serviceSourceDir)) {
            AppendSystem(SystemNotice.ServiceSidecarSourceFolderNotFound());
            return false;
        }
        StartupLog.Write("Service source dir: " + serviceSourceDir);

        var serviceDir = EnsureStagedServiceDirectory(serviceSourceDir);
        if (string.IsNullOrWhiteSpace(serviceDir)) {
            AppendSystem(SystemNotice.ServiceSidecarStagingFailed());
            return false;
        }

        var exe = Path.Combine(serviceDir, "IntelligenceX.Chat.Service.exe");
        var dll = Path.Combine(serviceDir, "IntelligenceX.Chat.Service.dll");

        if (!File.Exists(exe) && !File.Exists(dll)) {
            AppendSystem(SystemNotice.ServiceSidecarNotFoundNextToApp());
            return false;
        }

        try {
            var pending = _pendingServiceLaunchProfileOptions;
            pending ??= CaptureCurrentServiceLaunchProfileOptions();
            var launchArgs = ServiceLaunchArguments.Build(
                pipeName,
                DetachedServiceMode,
                Environment.ProcessId,
                pending is null ? null : new ServiceLaunchArguments.ProfileOptions {
                    LoadProfileName = pending.LoadProfileName,
                    SaveProfileName = pending.SaveProfileName,
                    Model = pending.Model,
                    OpenAITransport = pending.OpenAITransport,
                    OpenAIBaseUrl = pending.OpenAIBaseUrl,
                    OpenAIAuthMode = pending.OpenAIAuthMode,
                    OpenAIApiKey = pending.OpenAIApiKey,
                    OpenAIBasicUsername = pending.OpenAIBasicUsername,
                    OpenAIBasicPassword = pending.OpenAIBasicPassword,
                    OpenAIAccountId = pending.OpenAIAccountId,
                    ClearOpenAIApiKey = pending.ClearOpenAIApiKey,
                    ClearOpenAIBasicAuth = pending.ClearOpenAIBasicAuth,
                    OpenAIStreaming = pending.OpenAIStreaming,
                    OpenAIAllowInsecureHttp = pending.OpenAIAllowInsecureHttp,
                    ReasoningEffort = pending.ReasoningEffort,
                    ReasoningSummary = pending.ReasoningSummary,
                    TextVerbosity = pending.TextVerbosity,
                    Temperature = pending.Temperature,
                    EnablePowerShellPack = pending.EnablePowerShellPack,
                    EnableTestimoXPack = pending.EnableTestimoXPack,
                    EnableOfficeImoPack = pending.EnableOfficeImoPack
                });
            var hasExe = File.Exists(exe);
            var psi = new ProcessStartInfo {
                FileName = hasExe ? exe : "dotnet",
                WorkingDirectory = serviceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (hasExe) {
                foreach (var arg in launchArgs) {
                    psi.ArgumentList.Add(arg);
                }
            } else {
                psi.ArgumentList.Add(dll);
                foreach (var arg in launchArgs) {
                    psi.ArgumentList.Add(arg);
                }
            }
            psi.Environment.Remove(ChatServiceEnvironmentVariables.OpenAIBasicPassword);
            if (pending is not null && !pending.ClearOpenAIBasicAuth) {
                var basicPassword = (pending.OpenAIBasicPassword ?? string.Empty).Trim();
                if (basicPassword.Length > 0) {
                    psi.Environment[ChatServiceEnvironmentVariables.OpenAIBasicPassword] = basicPassword;
                }
            }

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => {
                if (!string.IsNullOrWhiteSpace(e.Data)) {
                    StartupLog.Write("[service] " + e.Data);
                }
                if ((VerboseServiceLogs || _debugMode) && !string.IsNullOrWhiteSpace(e.Data)) {
                    _ = _dispatcher.TryEnqueue(() => AppendSystem(SystemNotice.ServiceStdOut(e.Data)));
                }
            };
            p.ErrorDataReceived += (_, e) => {
                if (!string.IsNullOrWhiteSpace(e.Data)) {
                    StartupLog.Write("[service:err] " + e.Data);
                }
                if ((VerboseServiceLogs || _debugMode) && !string.IsNullOrWhiteSpace(e.Data)) {
                    _ = _dispatcher.TryEnqueue(() => AppendSystem(SystemNotice.ServiceStdErr(e.Data)));
                }
            };
            p.Exited += (_, _) => {
                _ = _dispatcher.TryEnqueue(() => {
                    if (!ReferenceEquals(_serviceProcess, p)) {
                        return;
                    }

                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.ServiceExited());
                    }
                    _isConnected = false;
                    _isAuthenticated = false;
                    _authenticatedAccountId = null;
                    _loginInProgress = false;
                    _ = SetStatusAsync(SessionStatus.Disconnected());
                    EnsureAutoReconnectLoop();
                });
            };

            if (!p.Start()) {
                return false;
            }

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            _serviceProcess = p;
            _servicePipeName = pipeName;
            _pendingServiceLaunchProfileOptions = null;

            if (ServiceStartupExitProbeDelay > TimeSpan.Zero) {
                await Task.Delay(ServiceStartupExitProbeDelay).ConfigureAwait(false);
            } else {
                await Task.Yield();
            }

            if (p.HasExited) {
                if (ReferenceEquals(_serviceProcess, p)) {
                    _serviceProcess = null;
                    _servicePipeName = null;
                }

                return false;
            }

            return true;
        } catch (Exception ex) {
            AppendSystem(SystemNotice.ServiceStartFailed(ex.Message));
            return false;
        }
    }

    private void StopServiceIfOwned() {
        var p = _serviceProcess;
        _serviceProcess = null;
        _servicePipeName = null;

        if (p is null) {
            return;
        }

        try {
            if (!p.HasExited) {
                p.Kill(entireProcessTree: true);
            }
        } catch {
            // Ignore.
        }

        _stagedServiceDir = null;
    }

    private async Task DisposeClientAsync() {
        var client = _client;
        _client = null;
        _isConnected = false;
        _activeKickoffRequestId = null;
        lock (_aliveProbeSync) {
            if (ReferenceEquals(client, _aliveProbeClient) || client is null) {
                _aliveProbeClient = null;
                _aliveProbeTicksUtc = 0;
            }
        }
        if (client is not null) {
            client.MessageReceived -= OnServiceMessage;
            client.Disconnected -= OnClientDisconnected;
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

}
