using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Launch;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
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
