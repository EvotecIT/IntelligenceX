using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private async Task<bool> RefreshAuthenticationStateAsync(
        bool updateStatus,
        bool requireFreshProbe = false,
        bool allowCachedAuthenticatedFallback = true,
        TimeSpan? probeTimeout = null) {
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
            if (updateStatus) {
                await SetStatusAsync(SessionStatus.ForConnection(_isConnected, isAuthenticated: true)).ConfigureAwait(false);
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
                }

                return true;
            case EnsureLoginProbeState.Unauthenticated:
                _isAuthenticated = false;
                _authenticatedAccountId = null;
                if (updateStatus) {
                    await SetStatusAsync(SessionStatus.ForConnectedAuth(isAuthenticated: false)).ConfigureAwait(false);
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

        if (ShouldBypassDispatchAuthProbeForKnownUnauthenticatedState(
                requiresInteractiveSignIn: true,
                isAuthenticated: _isAuthenticated,
                hasExplicitUnauthenticatedProbeSnapshot: HasExplicitUnauthenticatedEnsureLoginProbeSnapshot())) {
            _isAuthenticated = false;
            _authenticatedAccountId = null;
            return DispatchAuthenticationProbeOutcome.Unauthenticated;
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
            var launchPluginPaths = ResolveServiceLaunchPluginPaths(serviceSourceDir);
            if (launchPluginPaths.Count > 0) {
                StartupLog.Write("Service plugin paths configured count=" + launchPluginPaths.Count.ToString(CultureInfo.InvariantCulture));
            }
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
                },
                additionalPluginPaths: launchPluginPaths);
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

    internal static IReadOnlyList<string> ResolveServiceLaunchPluginPaths(string serviceSourceDir) {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(serviceSourceDir)) {
            try {
                var normalizedSourceDir = Path.GetFullPath(serviceSourceDir);
                var sourceParent = Path.GetDirectoryName(normalizedSourceDir);
                if (!string.IsNullOrWhiteSpace(sourceParent)) {
                    TryAddLaunchPluginPath(paths, seen, Path.Combine(sourceParent, "plugins"));
                }
            } catch {
                // Ignore malformed source-dir values and fall back to app-base plugin path.
            }
        }

        TryAddLaunchPluginPath(paths, seen, Path.Combine(AppContext.BaseDirectory, "plugins"));

        return paths;
    }

    private static void TryAddLaunchPluginPath(List<string> paths, HashSet<string> seen, string candidate) {
        if (string.IsNullOrWhiteSpace(candidate)) {
            return;
        }

        string fullPath;
        try {
            fullPath = Path.GetFullPath(candidate);
        } catch {
            return;
        }

        if (!Directory.Exists(fullPath) || !seen.Add(fullPath)) {
            return;
        }

        paths.Add(fullPath);
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
