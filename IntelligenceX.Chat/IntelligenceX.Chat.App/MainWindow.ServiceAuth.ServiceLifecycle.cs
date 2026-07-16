using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static readonly Regex StartupStatusCauseSuffixRegex = new(
        @"\(cause\s+(?<cause>[^)]+)\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StartupStatusPhaseContextRegex = new(
        @"\(\s*phase\s+[^)]*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StartupStatusCauseContextRegex = new(
        @"\(\s*(?:phase\s+[^)]*\bcause\s+|cause\s+)[^)]*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string StartingRuntimePrefix = "Starting runtime...";
    private const string RuntimeConnectedPrefix = "Runtime connected.";

    private async Task<bool> EnsureServiceRunningAsync(string pipeName) {
        if (_serviceProcessHost.IsRunning) {
            return true;
        }

        var profileOptions = _pendingServiceLaunchProfileOptions ?? CaptureCurrentServiceLaunchProfileOptions();
        var result = await _serviceProcessHost.EnsureRunningAsync(new ChatServiceProcessStartOptions {
            PipeName = pipeName,
            DetachedServiceMode = DetachedServiceMode,
            ParentProcessId = Environment.ProcessId,
            ProfileOptions = profileOptions,
            StartupExitProbeDelay = ServiceStartupExitProbeDelay
        }).ConfigureAwait(false);

        if (result.IsRunning) {
            if (result.Launched) {
                _pendingServiceLaunchProfileOptions = null;
                StartupLog.Write("Service runtime dir: " + result.ServiceDirectory);
            }
            return true;
        }

        switch (result.Failure) {
            case ChatServiceProcessStartFailure.SourceNotFound:
                AppendSystem(SystemNotice.ServiceSidecarSourceFolderNotFound());
                break;
            case ChatServiceProcessStartFailure.StagingFailed:
                if (result.Exception is not null) {
                    AppendSystem(SystemNotice.ServiceStagingError(result.Exception.Message));
                }
                AppendSystem(SystemNotice.ServiceSidecarStagingFailed());
                break;
            case ChatServiceProcessStartFailure.PayloadNotFound:
                AppendSystem(SystemNotice.ServiceSidecarNotFoundNextToApp());
                break;
            case ChatServiceProcessStartFailure.ExitedDuringStartup:
                AppendSystem(SystemNotice.ServiceStartFailed("The service exited during startup."));
                break;
            default:
                AppendSystem(SystemNotice.ServiceStartFailed(result.Exception?.Message ?? "The service process could not be started."));
                break;
        }

        return false;
    }

    private void OnServiceProcessOutputReceived(string line) {
        StartupLog.Write("[service] " + line);
        PublishServiceBootstrapStatusFromLogLine(line);
        if (VerboseServiceLogs || _debugMode) {
            _ = _dispatcher.TryEnqueue(() => AppendSystem(SystemNotice.ServiceStdOut(line)));
        }
    }

    private void OnServiceProcessErrorReceived(string line) {
        StartupLog.Write("[service:err] " + line);
        PublishServiceBootstrapStatusFromLogLine(line);
        if (VerboseServiceLogs || _debugMode) {
            _ = _dispatcher.TryEnqueue(() => AppendSystem(SystemNotice.ServiceStdErr(line)));
        }
    }

    private void OnServiceProcessExited() {
        _ = _dispatcher.TryEnqueue(() => {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.ServiceExited());
            }
            _isConnected = false;
            SetInteractiveAuthenticationUnknown();
            _loginInProgress = false;
            if (ShouldResetEnsureLoginProbeCacheForAuthContextChange(
                    requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                    loginCompletedSuccessfully: false,
                    transportChanged: false,
                    runtimeExited: true)) {
                ResetEnsureLoginProbeCache();
            }
            _ = SetStatusAsync(SessionStatus.Disconnected());
            EnsureAutoReconnectLoop();
        });
    }

    private void PublishServiceBootstrapStatusFromLogLine(string rawServiceLine) {
        if (!TryBuildServiceBootstrapStatus(rawServiceLine, out var statusText, out var allowDuringSend)) {
            return;
        }

        _ = _dispatcher.TryEnqueue(() => {
            var startupMetadataSyncInProgress = Volatile.Read(ref _startupMetadataSyncInProgress) != 0;
            var startupMetadataSyncQueued = Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0;
            var startupFlowState = Volatile.Read(ref _startupFlowState);
            var effectiveAllowDuringSend = ResolveAllowDuringSendForBootstrapStatus(
                isConnected: _isConnected,
                startupMetadataSyncInProgress: startupMetadataSyncInProgress,
                startupMetadataSyncQueued: startupMetadataSyncQueued,
                startupFlowState: startupFlowState,
                allowDuringSend: allowDuringSend);
            if (!ShouldPublishServiceBootstrapStatus(
                    shutdownRequested: _shutdownRequested,
                    isConnected: _isConnected,
                    isSending: _isSending,
                    turnStartupInProgress: _turnStartupInProgress,
                    startupMetadataSyncInProgress: startupMetadataSyncInProgress,
                    allowDuringSend: effectiveAllowDuringSend)) {
                return;
            }

            var effectiveStatusText = _isConnected
                ? BuildConnectedBootstrapStatusText(statusText, StartupStatusCauseMetadataSync)
                : statusText;
            _ = SetStatusAsync(effectiveStatusText, SessionStatusTone.Warn);
        });
    }

    internal static bool ResolveAllowDuringSendForBootstrapStatus(
        bool isConnected,
        bool startupMetadataSyncInProgress,
        bool startupMetadataSyncQueued,
        int startupFlowState,
        bool allowDuringSend) {
        if (!allowDuringSend) {
            return false;
        }

        if (!isConnected || startupMetadataSyncInProgress) {
            return true;
        }

        // Once startup flow has completed and no deferred startup metadata sync is queued,
        // late service bootstrap log lines should not revive startup-loading statuses.
        return startupMetadataSyncQueued || startupFlowState == StartupFlowStateRunning;
    }

    internal static bool ShouldPublishServiceBootstrapStatus(
        bool shutdownRequested,
        bool isConnected,
        bool isSending,
        bool turnStartupInProgress,
        bool startupMetadataSyncInProgress) {
        return ShouldPublishServiceBootstrapStatus(
            shutdownRequested,
            isConnected,
            isSending,
            turnStartupInProgress,
            startupMetadataSyncInProgress,
            allowDuringSend: false);
    }

    internal static bool ShouldPublishServiceBootstrapStatus(
        bool shutdownRequested,
        bool isConnected,
        bool isSending,
        bool turnStartupInProgress,
        bool startupMetadataSyncInProgress,
        bool allowDuringSend) {
        if (shutdownRequested) {
            return false;
        }

        if (turnStartupInProgress && !allowDuringSend) {
            return false;
        }

        if (isSending && !allowDuringSend) {
            return false;
        }

        if (!isConnected) {
            return true;
        }

        if (startupMetadataSyncInProgress) {
            return true;
        }

        return allowDuringSend;
    }

    internal static string BuildConnectedBootstrapStatusText(string statusText, string? cause) {
        var normalizedStatus = (statusText ?? string.Empty).Trim();
        var phase = StartupStatusPhaseStartupMetadataSync;
        if (normalizedStatus.Length == 0) {
            return AppendStartupStatusContext(
                "Runtime connected. Loading tool packs in background...",
                phase,
                cause);
        }

        if (normalizedStatus.StartsWith(StartingRuntimePrefix, StringComparison.OrdinalIgnoreCase)) {
            var suffix = normalizedStatus.Substring(StartingRuntimePrefix.Length).Trim();
            if (suffix.Length == 0) {
                normalizedStatus = RuntimeConnectedPrefix;
            } else {
                var first = suffix[0];
                var normalizedSuffix = char.IsLetter(first)
                    ? char.ToUpperInvariant(first) + suffix.Substring(1)
                    : suffix;
                normalizedStatus = RuntimeConnectedPrefix + " " + normalizedSuffix;
            }
        }

        if (!ShouldAnnotateConnectedBootstrapStatusAsStartup(normalizedStatus)) {
            return normalizedStatus;
        }

        var hasPhaseContext = StartupStatusPhaseContextRegex.IsMatch(normalizedStatus);
        var hasCauseContext = StartupStatusCauseContextRegex.IsMatch(normalizedStatus);
        if (!hasPhaseContext && hasCauseContext) {
            normalizedStatus = StartupStatusCauseSuffixRegex.Replace(
                normalizedStatus,
                match => {
                    var existingCause = match.Groups["cause"].Value.Trim();
                    if (existingCause.Length == 0) {
                        existingCause = (cause ?? string.Empty).Trim();
                    }

                    return BuildStartupStatusContextSuffix(phase, existingCause).TrimStart();
                },
                1);
            hasPhaseContext = StartupStatusPhaseContextRegex.IsMatch(normalizedStatus);
            hasCauseContext = StartupStatusCauseContextRegex.IsMatch(normalizedStatus);
        }

        if (!hasPhaseContext || (!hasCauseContext && !string.IsNullOrWhiteSpace(cause))) {
            normalizedStatus = AppendStartupStatusContext(
                normalizedStatus,
                hasPhaseContext ? null : phase,
                hasCauseContext ? null : cause);
        }

        return normalizedStatus;
    }

    private static bool ShouldAnnotateConnectedBootstrapStatusAsStartup(string normalizedStatus) {
        if (normalizedStatus.Length == 0) {
            return false;
        }

        if (StartupStatusPhaseContextRegex.IsMatch(normalizedStatus)
            || StartupStatusCauseContextRegex.IsMatch(normalizedStatus)) {
            return true;
        }

        var lower = normalizedStatus.ToLowerInvariant();
        if (lower.Contains("loading tool packs", StringComparison.Ordinal)
            || lower.Contains("tool catalog", StringComparison.Ordinal)
            || lower.Contains("session policy", StringComparison.Ordinal)
            || lower.Contains("authenticat", StringComparison.Ordinal)
            || lower.Contains("plugin folder scan", StringComparison.Ordinal)
            || lower.Contains("registering tool pack", StringComparison.Ordinal)
            || lower.Contains("registered tool pack", StringComparison.Ordinal)
            || lower.Contains("initializing tool packs", StringComparison.Ordinal)
            || lower.Contains("initialized tool packs", StringComparison.Ordinal)
            || lower.Contains("runtime provider", StringComparison.Ordinal)) {
            return true;
        }

        // Final "bootstrap finished / finalizing connection" lines should not pin startup metadata
        // context once runtime is already connected.
        return false;
    }

    internal static bool TryBuildServiceBootstrapStatus(string? rawServiceLine, out string statusText) {
        return TryBuildServiceBootstrapStatus(rawServiceLine, out statusText, out _);
    }

    internal static bool TryBuildServiceBootstrapStatus(string? rawServiceLine, out string statusText, out bool allowDuringSend) {
        return StartupBootstrapWarningFormatter.TryBuildStatusText(rawServiceLine, out statusText, out allowDuringSend);
    }

    internal static bool ShouldStopOwnedServiceOnWindowClose(bool detachedServiceMode) {
        return !detachedServiceMode;
    }

    private void StopServiceIfOwned() {
        EndStartupMetadataSyncTracking();
        var terminateProcess = ShouldStopOwnedServiceOnWindowClose(DetachedServiceMode);
        if (!terminateProcess && _serviceProcessHost.IsRunning) {
            StartupLog.Write("ServiceLifecycle.stop skipped_owned_process_termination (detached mode)");
        }
        _serviceProcessHost.Stop(terminateProcess);
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
