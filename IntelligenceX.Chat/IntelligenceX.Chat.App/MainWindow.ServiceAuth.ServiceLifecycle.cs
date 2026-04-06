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
            var launchBuiltInToolProbePaths = ResolveServiceLaunchBuiltInToolProbePaths(serviceSourceDir);
            if (launchPluginPaths.Count > 0) {
                StartupLog.Write("Service plugin paths configured count=" + launchPluginPaths.Count.ToString(CultureInfo.InvariantCulture));
            }
            if (launchBuiltInToolProbePaths.Count > 0) {
                StartupLog.Write("Service built-in tool probe paths configured count=" + launchBuiltInToolProbePaths.Count.ToString(CultureInfo.InvariantCulture));
            }
            var enableWorkspaceBuiltInToolOutputProbing = ShouldEnableWorkspaceBuiltInToolOutputProbing(launchBuiltInToolProbePaths);
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
                    PackToggles = pending.PackToggles
                },
                additionalPluginPaths: launchPluginPaths,
                additionalBuiltInToolProbePaths: launchBuiltInToolProbePaths,
                enableWorkspaceBuiltInToolOutputProbing: enableWorkspaceBuiltInToolOutputProbing);
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
                    PublishServiceBootstrapStatusFromLogLine(e.Data);
                }
                if ((VerboseServiceLogs || _debugMode) && !string.IsNullOrWhiteSpace(e.Data)) {
                    _ = _dispatcher.TryEnqueue(() => AppendSystem(SystemNotice.ServiceStdOut(e.Data)));
                }
            };
            p.ErrorDataReceived += (_, e) => {
                if (!string.IsNullOrWhiteSpace(e.Data)) {
                    StartupLog.Write("[service:err] " + e.Data);
                    PublishServiceBootstrapStatusFromLogLine(e.Data);
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

    internal static IReadOnlyList<string> ResolveServiceLaunchBuiltInToolProbePaths(string serviceSourceDir) {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAddLaunchRuntimePath(paths, seen, serviceSourceDir);
        if (!string.IsNullOrWhiteSpace(serviceSourceDir)) {
            try {
                var normalizedSourceDir = Path.GetFullPath(serviceSourceDir);
                TryAddLaunchRuntimePath(paths, seen, Path.Combine(normalizedSourceDir, "tools"));
            } catch {
                // Ignore malformed source-dir values and keep the main source root if available.
            }
        }

        return paths;
    }

    internal static bool ShouldEnableWorkspaceBuiltInToolOutputProbing(IReadOnlyCollection<string> launchBuiltInToolProbePaths) {
        return launchBuiltInToolProbePaths is null || launchBuiltInToolProbePaths.Count == 0;
    }

    private static void TryAddLaunchPluginPath(List<string> paths, HashSet<string> seen, string candidate) {
        TryAddLaunchRuntimePath(paths, seen, candidate);
    }

    private static void TryAddLaunchRuntimePath(List<string> paths, HashSet<string> seen, string candidate) {
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
        var p = _serviceProcess;
        _serviceProcess = null;
        _servicePipeName = null;
        EndStartupMetadataSyncTracking();

        if (p is null) {
            return;
        }

        if (!ShouldStopOwnedServiceOnWindowClose(DetachedServiceMode)) {
            StartupLog.Write("ServiceLifecycle.stop skipped_owned_process_termination (detached mode)");
            p.Dispose();
            _stagedServiceDir = null;
            return;
        }

        try {
            if (!p.HasExited) {
                p.Kill(entireProcessTree: true);
            }
        } catch {
            // Ignore.
        } finally {
            p.Dispose();
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
