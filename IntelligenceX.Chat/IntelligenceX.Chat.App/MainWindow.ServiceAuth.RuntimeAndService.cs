using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
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
            SetInteractiveAuthenticationUnknown();
            if (updateStatus) {
                await PublishSessionStateAsync().ConfigureAwait(false);
            }
            return false;
        }

        var timeout = probeTimeout.GetValueOrDefault(requireFreshProbe ? EnsureLoginFreshProbeTimeout : EnsureLoginProbeTimeout);
        var probe = await ProbeEnsureLoginAsync(timeout, requireFreshProbe).ConfigureAwait(false);
        switch (probe.State) {
            case EnsureLoginProbeState.Authenticated:
                SetInteractiveAuthenticationKnown(isAuthenticated: true, probe.AccountId);
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
                SetInteractiveAuthenticationKnown(isAuthenticated: false);
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
            SetInteractiveAuthenticationKnown(isAuthenticated: false);
            return DispatchAuthenticationProbeOutcome.Unauthenticated;
        }

        var client = _client;
        if (client is null) {
            ResetEnsureLoginProbeCache();
            SetInteractiveAuthenticationUnknown();
            return DispatchAuthenticationProbeOutcome.Unknown;
        }

        var timeout = probeTimeout.GetValueOrDefault(EnsureLoginFastPathProbeTimeout);
        var probe = await ProbeEnsureLoginAsync(timeout, requireFreshProbe: false).ConfigureAwait(false);
        switch (probe.State) {
            case EnsureLoginProbeState.Authenticated:
                SetInteractiveAuthenticationKnown(isAuthenticated: true, probe.AccountId);
                CaptureAuthenticatedAccountIntoActiveSlot();
                if (probe.LoginStatus is { IsAuthenticated: true } loginStatus) {
                    UpdateAccountUsageFromNativeLoginStatus(loginStatus);
                }
                if (!probe.FromCache) {
                    QueuePersistAppState();
                }

                return DispatchAuthenticationProbeOutcome.Authenticated;
            case EnsureLoginProbeState.Unauthenticated:
                SetInteractiveAuthenticationKnown(isAuthenticated: false);
                return DispatchAuthenticationProbeOutcome.Unauthenticated;
            default:
                return DispatchAuthenticationProbeOutcome.Unknown;
        }
    }

    internal static bool ShouldPromoteAuthenticatedStateFromFinalAssistantTurn(
        bool requiresInteractiveSignIn,
        bool isConnected,
        bool isAuthenticated,
        bool loginInProgress) {
        return requiresInteractiveSignIn
               && isConnected
               && !isAuthenticated
               && !loginInProgress;
    }

    internal static bool ShouldRefreshAuthenticationStateAfterConversationSwitch(
        bool requiresInteractiveSignIn,
        bool isConnected,
        bool isAuthenticated,
        bool loginInProgress,
        bool hasExplicitUnauthenticatedProbeSnapshot) {
        return requiresInteractiveSignIn
               && isConnected
               && !isAuthenticated
               && !loginInProgress
               && !hasExplicitUnauthenticatedProbeSnapshot;
    }

    private void PromoteAuthenticatedStateFromFinalAssistantTurn() {
        if (!ShouldPromoteAuthenticatedStateFromFinalAssistantTurn(
                requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                isConnected: _isConnected,
                isAuthenticated: _isAuthenticated,
                loginInProgress: _loginInProgress)) {
            return;
        }

        SetInteractiveAuthenticationKnown(isAuthenticated: true);
        _loginInProgress = false;
        ResetEnsureLoginProbeCache();
        QueuePersistAppState();
        _ = SetStatusAsync(SessionStatus.ForConnectedAuth(isAuthenticated: true));
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
            SetInteractiveAuthenticationKnown(isAuthenticated: false);
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
            SetInteractiveAuthenticationKnown(isAuthenticated: false);
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
        SetInteractiveAuthenticationKnown(isAuthenticated: false);
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
}
