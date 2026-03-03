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
    private static readonly Regex ServiceBootstrapPluginProgressRegex = new(
        @"^\[plugin\]\s+load_progress\s+plugin='(?<plugin>[^']*)'\s+phase='(?<phase>[^']*)'\s+index='(?<index>\d+)'\s+total='(?<total>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ServiceBootstrapPackProgressRegex = new(
        @"^\[startup\]\s+pack_load_progress\s+pack='(?<pack>[^']*)'\s+phase='(?<phase>[^']*)'\s+index='(?<index>\d+)'\s+total='(?<total>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ServiceBootstrapPackRegistrationProgressRegex = new(
        @"^\[startup\]\s+pack_register_progress\s+pack='(?<pack>[^']*)'\s+phase='(?<phase>[^']*)'\s+index='(?<index>\d+)'\s+total='(?<total>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ServiceBootstrapProgressSummaryRegex = new(
        @"^\[startup\]\s+plugin load progress:\s+processed\s+(?<processed>\d+)\/(?<total>\d+)\s+plugin folders",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ServiceBootstrapTimingRegex = new(
        @"^\[startup\]\s+tooling bootstrap timings\s+total=(?<total>[^\s]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ServiceBootstrapElapsedMsRegex = new(
        @"(?:^|\s)elapsed_ms='(?<elapsed>\d+)'",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                    PackToggles = pending.PackToggles
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

    private void PublishServiceBootstrapStatusFromLogLine(string rawServiceLine) {
        if (!TryBuildServiceBootstrapStatus(rawServiceLine, out var statusText)) {
            return;
        }

        _ = _dispatcher.TryEnqueue(() => {
            var startupMetadataSyncInProgress = Volatile.Read(ref _startupMetadataSyncInProgress) != 0;
            if (!ShouldPublishServiceBootstrapStatus(
                    shutdownRequested: _shutdownRequested,
                    isConnected: _isConnected,
                    isSending: _isSending,
                    turnStartupInProgress: _turnStartupInProgress,
                    startupMetadataSyncInProgress: startupMetadataSyncInProgress)) {
                return;
            }

            _ = SetStatusAsync(statusText, SessionStatusTone.Warn);
        });
    }

    internal static bool ShouldPublishServiceBootstrapStatus(
        bool shutdownRequested,
        bool isConnected,
        bool isSending,
        bool turnStartupInProgress,
        bool startupMetadataSyncInProgress) {
        if (shutdownRequested || isSending || turnStartupInProgress) {
            return false;
        }

        if (!isConnected) {
            return true;
        }

        return startupMetadataSyncInProgress;
    }

    internal static bool TryBuildServiceBootstrapStatus(string? rawServiceLine, out string statusText) {
        statusText = string.Empty;
        var normalized = (rawServiceLine ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        const string packWarningPrefix = "[pack warning]";
        if (normalized.StartsWith(packWarningPrefix, StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(packWarningPrefix.Length).Trim();
        }

        var packRegistrationProgressMatch = ServiceBootstrapPackRegistrationProgressRegex.Match(normalized);
        if (packRegistrationProgressMatch.Success) {
            var phase = packRegistrationProgressMatch.Groups["phase"].Value.Trim();
            var pack = packRegistrationProgressMatch.Groups["pack"].Value.Trim();
            if (!int.TryParse(packRegistrationProgressMatch.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) {
                return false;
            }
            if (!int.TryParse(packRegistrationProgressMatch.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) {
                return false;
            }

            index = Math.Max(1, index);
            total = Math.Max(index, total);
            var packLabel = string.IsNullOrWhiteSpace(pack) ? "pack" : pack;
            if (packLabel.Length > 42) {
                packLabel = packLabel.Substring(0, 39) + "...";
            }

            if (string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase)) {
                statusText = $"Starting runtime... registering tool pack {index}/{total} ({packLabel})";
                return true;
            }

            if (string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase)) {
                var elapsedMs = TryReadBootstrapElapsedMs(normalized);
                var elapsedLabel = elapsedMs.HasValue
                    ? $"{Math.Max(1, elapsedMs.Value)}ms"
                    : "done";
                statusText = $"Starting runtime... registered tool pack {index}/{total} ({packLabel}, {elapsedLabel})";
                return true;
            }

            return false;
        }

        var packProgressMatch = ServiceBootstrapPackProgressRegex.Match(normalized);
        if (packProgressMatch.Success) {
            var phase = packProgressMatch.Groups["phase"].Value.Trim();
            var pack = packProgressMatch.Groups["pack"].Value.Trim();
            if (!int.TryParse(packProgressMatch.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) {
                return false;
            }
            if (!int.TryParse(packProgressMatch.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) {
                return false;
            }

            index = Math.Max(1, index);
            total = Math.Max(index, total);
            var packLabel = string.IsNullOrWhiteSpace(pack) ? "pack" : pack;
            if (packLabel.Length > 42) {
                packLabel = packLabel.Substring(0, 39) + "...";
            }

            if (string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase)) {
                statusText = $"Starting runtime... initializing tool packs {index}/{total} ({packLabel})";
                return true;
            }

            if (string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase)) {
                var elapsedMs = TryReadBootstrapElapsedMs(normalized);
                var elapsedLabel = elapsedMs.HasValue
                    ? $"{Math.Max(1, elapsedMs.Value)}ms"
                    : "done";
                statusText = $"Starting runtime... initialized tool packs {index}/{total} ({packLabel}, {elapsedLabel})";
                return true;
            }

            return false;
        }

        var pluginProgressMatch = ServiceBootstrapPluginProgressRegex.Match(normalized);
        if (pluginProgressMatch.Success) {
            var phase = pluginProgressMatch.Groups["phase"].Value.Trim();
            var plugin = pluginProgressMatch.Groups["plugin"].Value.Trim();
            if (!int.TryParse(pluginProgressMatch.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) {
                return false;
            }
            if (!int.TryParse(pluginProgressMatch.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) {
                return false;
            }

            index = Math.Max(1, index);
            total = Math.Max(index, total);
            var pluginLabel = string.IsNullOrWhiteSpace(plugin) ? "plugin" : plugin;
            if (pluginLabel.Length > 42) {
                pluginLabel = pluginLabel.Substring(0, 39) + "...";
            }

            if (string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase)) {
                statusText = $"Starting runtime... loading tool packs {index}/{total} ({pluginLabel})";
                return true;
            }

            if (string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase)) {
                var elapsedMs = TryReadBootstrapElapsedMs(normalized);
                var elapsedLabel = elapsedMs.HasValue
                    ? $"{Math.Max(1, elapsedMs.Value)}ms"
                    : "done";
                statusText = $"Starting runtime... loaded tool packs {index}/{total} ({pluginLabel}, {elapsedLabel})";
                return true;
            }

            return false;
        }

        var progressSummaryMatch = ServiceBootstrapProgressSummaryRegex.Match(normalized);
        if (progressSummaryMatch.Success) {
            if (!int.TryParse(progressSummaryMatch.Groups["processed"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var processed)) {
                return false;
            }
            if (!int.TryParse(progressSummaryMatch.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)) {
                return false;
            }

            processed = Math.Max(0, processed);
            total = Math.Max(processed, total);
            statusText = $"Starting runtime... plugin folder scan {processed}/{total}";
            return true;
        }

        var timingMatch = ServiceBootstrapTimingRegex.Match(normalized);
        if (timingMatch.Success) {
            var total = timingMatch.Groups["total"].Value.Trim();
            if (total.Length == 0) {
                total = "unknown";
            }

            statusText = $"Starting runtime... tool bootstrap finished ({total}), finalizing runtime connection";
            return true;
        }

        return false;
    }

    private static int? TryReadBootstrapElapsedMs(string normalizedLine) {
        var elapsedMatch = ServiceBootstrapElapsedMsRegex.Match(normalizedLine);
        if (!elapsedMatch.Success) {
            return null;
        }

        if (!int.TryParse(elapsedMatch.Groups["elapsed"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elapsedMs)) {
            return null;
        }

        return Math.Max(1, elapsedMs);
    }

    private void StopServiceIfOwned() {
        var p = _serviceProcess;
        _serviceProcess = null;
        _servicePipeName = null;
        EndStartupMetadataSyncTracking();

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
