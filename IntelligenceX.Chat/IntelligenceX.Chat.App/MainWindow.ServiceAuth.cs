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
    private static readonly TimeSpan EnsureLoginFastPathProbeTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan EnsureLoginPostLoginProbeTimeout = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan EnsureLoginProbeCacheTtl = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan EnsureLoginUnknownProbeRetryDelay = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan RuntimeAccountPinResetTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RuntimeAccountPinResetFastTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RuntimeAccountPinResetSwitchPreflightTimeout = TimeSpan.FromMilliseconds(350);
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

    internal static bool ShouldBypassDispatchAuthProbeForKnownUnauthenticatedState(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool hasExplicitUnauthenticatedProbeSnapshot) {
        return requiresInteractiveSignIn && !isAuthenticated && hasExplicitUnauthenticatedProbeSnapshot;
    }

    internal static bool ShouldPreserveInteractiveAuthStateOnReconnect(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool hasExplicitUnauthenticatedProbeSnapshot,
        bool loginInProgress) {
        if (!requiresInteractiveSignIn) {
            return false;
        }

        if (loginInProgress) {
            return true;
        }

        if (hasExplicitUnauthenticatedProbeSnapshot) {
            return false;
        }

        return isAuthenticated;
    }

    internal static bool ShouldResetEnsureLoginProbeCacheOnReconnectAuthReset(
        bool requiresInteractiveSignIn,
        bool preserveInteractiveAuthState) {
        return !requiresInteractiveSignIn || !preserveInteractiveAuthState;
    }

    internal static bool ShouldResetEnsureLoginProbeCacheForAuthContextChange(
        bool requiresInteractiveSignIn,
        bool loginCompletedSuccessfully,
        bool transportChanged,
        bool runtimeExited) {
        if (loginCompletedSuccessfully || transportChanged) {
            return true;
        }

        return requiresInteractiveSignIn && runtimeExited;
    }

    internal static bool ShouldExposeExplicitUnauthenticatedEnsureLoginProbeSnapshot(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool probeCacheHasValue,
        bool probeCachedIsAuthenticated) {
        return requiresInteractiveSignIn
               && !isAuthenticated
               && !loginInProgress
               && probeCacheHasValue
               && !probeCachedIsAuthenticated;
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
        return ShouldExposeExplicitUnauthenticatedEnsureLoginProbeSnapshot(
            requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
            isAuthenticated: _isAuthenticated,
            loginInProgress: _loginInProgress,
            probeCacheHasValue: _ensureLoginProbeCacheHasValue,
            probeCachedIsAuthenticated: _ensureLoginProbeCachedIsAuthenticated);
    }

    private bool HasKnownAuthenticationStateForCurrentTransport() {
        return !RequiresInteractiveSignInForCurrentTransport()
               || _interactiveAuthenticationStateKnown
               || _isAuthenticated
               || HasExplicitUnauthenticatedEnsureLoginProbeSnapshot();
    }

    private void SetInteractiveAuthenticationUnknown() {
        _interactiveAuthenticationStateKnown = false;
        _isAuthenticated = false;
        _authenticatedAccountId = null;
    }

    private void SetInteractiveAuthenticationKnown(bool isAuthenticated, string? accountId = null) {
        _interactiveAuthenticationStateKnown = true;
        _isAuthenticated = isAuthenticated;
        _authenticatedAccountId = isAuthenticated ? (accountId ?? string.Empty).Trim() : null;
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

    internal static SessionStatus ResolveConnectionStatus(
        bool isConnected,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool authenticationStateKnown,
        bool hasExplicitUnauthenticatedProbeSnapshot) {
        if (!isConnected) {
            return SessionStatus.Disconnected();
        }

        if (!requiresInteractiveSignIn) {
            return SessionStatus.Connected();
        }

        if (loginInProgress) {
            return SessionStatus.WaitingForSignIn();
        }

        if (isAuthenticated) {
            return SessionStatus.Connected();
        }

        return authenticationStateKnown || hasExplicitUnauthenticatedProbeSnapshot
            ? SessionStatus.SignInRequired()
            : SessionStatus.Connected();
    }

    private SessionStatus ResolveConnectionStatusForCurrentTransport() {
        return ResolveConnectionStatus(
            isConnected: _isConnected,
            requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
            isAuthenticated: IsEffectivelyAuthenticatedForCurrentTransport(),
            loginInProgress: _loginInProgress,
            authenticationStateKnown: HasKnownAuthenticationStateForCurrentTransport(),
            hasExplicitUnauthenticatedProbeSnapshot: HasExplicitUnauthenticatedEnsureLoginProbeSnapshot());
    }

    private void ApplyNonNativeAuthenticationStateIfNeeded() {
        if (RequiresInteractiveSignInForCurrentTransport()) {
            return;
        }

        ResetEnsureLoginProbeCache();
        SetInteractiveAuthenticationKnown(isAuthenticated: true);
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
            if (_client is not null
                && await IsClientAliveAsync(
                        _client,
                        probeTimeout: AliveProbeFastTimeout,
                        cacheTtl: AliveProbeCacheTtl)
                    .ConfigureAwait(false)) {
                return;
            }

            if (IsTurnDispatchInProgress()) {
                try {
                    await Task.Delay(AutoReconnectBusyTurnDelay, cancellationToken).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return;
                }

                continue;
            }

            var hasTrackedRunningServiceProcess = _serviceProcess is not null && !_serviceProcess.HasExited;
            var prioritizeLatency = ShouldPrioritizeAutoReconnectLatency();
            var baseDelay = AutoReconnectBackoffDelays[Math.Min(attempt, AutoReconnectBackoffDelays.Length - 1)];
            var delay = ResolveAutoReconnectDelay(baseDelay, prioritizeLatency, hasTrackedRunningServiceProcess, attempt);
            if (!_shutdownRequested && !_isSending && !_turnStartupInProgress) {
                await SetStatusAsync(
                        BuildAutoReconnectStatusText(attempt, delay),
                        SessionStatusTone.Warn)
                    .ConfigureAwait(false);
            }

            try {
                if (delay > TimeSpan.Zero) {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                return;
            }

            if (cancellationToken.IsCancellationRequested) {
                return;
            }

            await ConnectAsync(
                    fromUserAction: false,
                    connectBudgetOverride: AutoReconnectConnectBudget,
                    deferPostConnectMetadataSync: true)
                .ConfigureAwait(false);
            attempt++;
            if (prioritizeLatency || hasTrackedRunningServiceProcess) {
                attempt = Math.Min(attempt, 2);
            }

            if (_client is not null
                && await IsClientAliveAsync(
                        _client,
                        probeTimeout: AliveProbeFastTimeout,
                        cacheTtl: AliveProbeCacheTtl)
                    .ConfigureAwait(false)) {
                return;
            }
        }
    }

    private bool ShouldPrioritizeAutoReconnectLatency() {
        if (_loginInProgress) {
            return true;
        }

        return GetQueuedPromptAfterLoginCount() > 0 || GetQueuedTurnCount() > 0;
    }

    private static TimeSpan ResolveAutoReconnectDelay(
        TimeSpan baseDelay,
        bool prioritizeLatency,
        bool hasTrackedRunningServiceProcess,
        int attempt) {
        if (baseDelay <= TimeSpan.Zero) {
            return TimeSpan.Zero;
        }

        if (prioritizeLatency && attempt <= 0) {
            return TimeSpan.Zero;
        }

        if (prioritizeLatency) {
            var scaledDelay = TimeSpan.FromMilliseconds(Math.Max(0, baseDelay.TotalMilliseconds / 2d));
            if (scaledDelay < AutoReconnectPriorityFirstDelay) {
                scaledDelay = AutoReconnectPriorityFirstDelay;
            }
            if (scaledDelay > AutoReconnectPriorityDelayCap) {
                scaledDelay = AutoReconnectPriorityDelayCap;
            }

            return scaledDelay;
        }

        if (hasTrackedRunningServiceProcess) {
            var scaledDelay = TimeSpan.FromMilliseconds(Math.Max(0, baseDelay.TotalMilliseconds / 2d));
            if (scaledDelay > AutoReconnectTrackedServiceDelayCap) {
                scaledDelay = AutoReconnectTrackedServiceDelayCap;
            }

            return scaledDelay;
        }

        return baseDelay;
    }

    internal static string BuildAutoReconnectStatusText(int attempt, TimeSpan delay) {
        var normalizedAttempt = attempt < 0 ? 1 : attempt + 1;
        if (delay <= TimeSpan.Zero) {
            return $"Runtime connection dropped. Reconnecting now (attempt {normalizedAttempt.ToString(CultureInfo.InvariantCulture)}).";
        }

        var delayLabel = delay.TotalSeconds >= 1
            ? delay.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : Math.Max(1L, (long)Math.Round(delay.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture) + "ms";
        return $"Runtime connection dropped. Reconnecting in {delayLabel} (attempt {normalizedAttempt.ToString(CultureInfo.InvariantCulture)}).";
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


}
