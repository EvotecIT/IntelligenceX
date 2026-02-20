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

    private async Task<bool> IsClientAliveAsync(ChatServiceClient client) {
        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_aliveProbeSync) {
            if (ReferenceEquals(client, _aliveProbeClient)
                && _aliveProbeTicksUtc > 0
                && nowTicks - _aliveProbeTicksUtc <= TimeSpan.FromMilliseconds(1200).Ticks) {
                return true;
            }
        }

        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            _ = await client.RequestAsync<HelloMessage>(new HelloRequest { RequestId = NextId() }, cts.Token).ConfigureAwait(false);
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
        var delays = new[] {
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(30)
        };
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested) {
            if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
                return;
            }

            var delay = delays[Math.Min(attempt, delays.Length - 1)];
            attempt++;

            try {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                return;
            }

            if (cancellationToken.IsCancellationRequested) {
                return;
            }

            await ConnectAsync(fromUserAction: false).ConfigureAwait(false);

            if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
                return;
            }
        }
    }

    private async Task<bool> RefreshAuthenticationStateAsync(bool updateStatus) {
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
            _isAuthenticated = false;
            _authenticatedAccountId = null;
            if (updateStatus) {
                await PublishSessionStateAsync().ConfigureAwait(false);
            }
            return false;
        }

        try {
            var login = await client.RequestAsync<LoginStatusMessage>(new EnsureLoginRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
            _isAuthenticated = login.IsAuthenticated;
            _authenticatedAccountId = login.IsAuthenticated ? (login.AccountId ?? string.Empty).Trim() : null;
            if (login.IsAuthenticated) {
                CaptureAuthenticatedAccountIntoActiveSlot();
                UpdateAccountUsageFromNativeLoginStatus(login);
                QueuePersistAppState();
            }

            if (updateStatus) {
                await SetStatusAsync(SessionStatus.ForConnectedAuth(login.IsAuthenticated)).ConfigureAwait(false);
                await PublishOptionsStateAsync().ConfigureAwait(false);
            }

            return login.IsAuthenticated;
        } catch (Exception ex) {
            // Transient ensure_login probe failures should not automatically force a new browser login.
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.EnsureLoginFailed(ex.Message));
            }

            if (updateStatus) {
                await PublishSessionStateAsync().ConfigureAwait(false);
            }

            return _isAuthenticated;
        }
    }

    private async Task<bool> StartLoginFlowIfNeededAsync(bool forceInteractive = false) {
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
            if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
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

        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return false;
        }

        if (!forceInteractive) {
            if (await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false)) {
                _isConnected = _client is not null;
                await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);
                return true;
            }

            // Allow one short retry window before launching browser login.
            await Task.Delay(250).ConfigureAwait(false);
            if (await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false)) {
                _isConnected = _client is not null;
                await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);
                return true;
            }
        }

        var client = _client;
        if (client is null) {
            return false;
        }

        try {
            _loginInProgress = true;
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
            _authenticatedAccountId = null;
            await SetStatusAsync(SessionStatus.SignInFailed()).ConfigureAwait(false);
            AppendSystem(SystemNotice.SignInFailed(ex.Message));
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

        var authPath = ResolveAuthPath();
        if (!TryDeleteAuthStore(authPath, out var existed, out var error)) {
            AppendSystem("Couldn't clear the local sign-in cache. We'll still try to sign in again.");
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem($"Auth cache path: {authPath}");
                AppendSystem("Cache clear detail: " + error);
            }
        } else if (existed) {
            AppendSystem("Sign-in cache cleared. You can now choose another account.");
        }

        _isAuthenticated = false;
        _authenticatedAccountId = null;
        _loginInProgress = false;
        await SetStatusAsync("Starting sign-in for another account...").ConfigureAwait(false);
        return await StartLoginFlowIfNeededAsync(forceInteractive: true).ConfigureAwait(false);
    }

    private static string ResolveAuthPath() {
        var overridePath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) {
            return overridePath;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            home = ".";
        }

        return Path.Combine(home, ".intelligencex", "auth.json");
    }

    private static bool TryDeleteAuthStore(string authPath, out bool existed, out string? error) {
        existed = false;
        error = null;

        try {
            existed = File.Exists(authPath);
            if (existed) {
                File.Delete(authPath);
            }
            return true;
        } catch (Exception ex) {
            error = ex.Message;
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

            await Task.Delay(250).ConfigureAwait(true);
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
