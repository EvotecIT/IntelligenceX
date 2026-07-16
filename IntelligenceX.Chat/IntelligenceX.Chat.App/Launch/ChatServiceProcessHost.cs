using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Owns the lifecycle of the local chat service process used by desktop shells.
/// </summary>
internal sealed class ChatServiceProcessHost : IDisposable {
    private const string ServiceExecutableName = "IntelligenceX.Chat.Service.exe";
    private const string ServiceAssemblyName = "IntelligenceX.Chat.Service.dll";

    private readonly ChatServiceRuntimeStager _stager = new();
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Process? _process;
    private int _disposed;
    private int _lastOwnedProcessExited;

    /// <summary>
    /// Raised for non-empty standard-output lines written by the service.
    /// </summary>
    public event Action<string>? OutputReceived;

    /// <summary>
    /// Raised for non-empty standard-error lines written by the service.
    /// </summary>
    public event Action<string>? ErrorReceived;

    /// <summary>
    /// Raised when the currently owned service exits without first being detached or stopped.
    /// </summary>
    public event Action? Exited;

    /// <summary>
    /// Gets the pipe name assigned to the currently owned service.
    /// </summary>
    public string? PipeName { get; private set; }

    /// <summary>
    /// Gets whether the currently tracked service is running.
    /// </summary>
    public bool IsRunning => _process is not null && !HasProcessExited(_process);

    /// <summary>
    /// Gets whether a tracked service exists and has exited.
    /// </summary>
    public bool HasExited => (_process is not null && HasProcessExited(_process))
                             || Volatile.Read(ref _lastOwnedProcessExited) != 0;

    /// <summary>
    /// Starts the local service when no live owned process exists.
    /// </summary>
    public async Task<ChatServiceProcessStartResult> EnsureRunningAsync(
        ChatServiceProcessStartOptions options,
        CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(options);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        var effectiveCancellationToken = linkedCts.Token;
        await _launchGate.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await EnsureRunningCoreAsync(options, effectiveCancellationToken).ConfigureAwait(false);
        } finally {
            _launchGate.Release();
        }
    }

    private async Task<ChatServiceProcessStartResult> EnsureRunningCoreAsync(
        ChatServiceProcessStartOptions options,
        CancellationToken cancellationToken) {

        if (IsRunning) {
            return ChatServiceProcessStartResult.Succeeded(launched: false, PipeName);
        }

        var sourceDirectory = options.ServiceSourceDirectory;
        if (string.IsNullOrWhiteSpace(sourceDirectory)) {
            sourceDirectory = ChatServiceRuntimeLocator.ResolveSourceDirectory(options.AppBaseDirectory);
        }
        if (string.IsNullOrWhiteSpace(sourceDirectory)) {
            return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.SourceNotFound);
        }
        if (!Directory.Exists(sourceDirectory)) {
            return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.SourceNotFound);
        }
        if (!ChatServiceRuntimeLocator.HasServicePayload(sourceDirectory)) {
            return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.PayloadNotFound);
        }

        string serviceDirectory;
        try {
            serviceDirectory = _stager.Stage(sourceDirectory);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) {
            return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.StagingFailed, ex);
        }

        var executablePath = Path.Combine(serviceDirectory, ServiceExecutableName);
        var assemblyPath = Path.Combine(serviceDirectory, ServiceAssemblyName);
        var hasExecutable = File.Exists(executablePath);
        if (!hasExecutable && !File.Exists(assemblyPath)) {
            return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.PayloadNotFound);
        }

        try {
            var pluginPaths = ChatServiceRuntimeLocator.ResolvePluginPaths(sourceDirectory, options.AppBaseDirectory);
            var builtInToolProbePaths = ChatServiceRuntimeLocator.ResolveBuiltInToolProbePaths(sourceDirectory);
            var launchArguments = ServiceLaunchArguments.Build(
                options.PipeName,
                options.DetachedServiceMode,
                options.ParentProcessId,
                options.ProfileOptions,
                pluginPaths,
                builtInToolProbePaths,
                ChatServiceRuntimeLocator.ShouldEnableWorkspaceBuiltInToolOutputProbing(builtInToolProbePaths));
            var startInfo = CreateStartInfo(
                serviceDirectory,
                executablePath,
                assemblyPath,
                hasExecutable,
                launchArguments,
                options.ProfileOptions);
            cancellationToken.ThrowIfCancellationRequested();

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, eventArgs) => PublishLine(OutputReceived, eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => PublishLine(ErrorReceived, eventArgs.Data);
            process.Exited += (_, _) => HandleProcessExited(process);

            if (!process.Start()) {
                process.Dispose();
                return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.LaunchFailed);
            }

            _process = process;
            PipeName = options.PipeName;
            Volatile.Write(ref _lastOwnedProcessExited, 0);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (options.StartupExitProbeDelay > TimeSpan.Zero) {
                await Task.Delay(options.StartupExitProbeDelay, cancellationToken).ConfigureAwait(false);
            } else {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (HasProcessExited(process)) {
                ClearProcess(process);
                process.Dispose();
                return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.ExitedDuringStartup);
            }

            return ChatServiceProcessStartResult.Succeeded(launched: true, PipeName, serviceDirectory);
        } catch (OperationCanceledException) {
            Stop(terminateProcess: true);
            throw;
        } catch (Exception ex) {
            Stop(terminateProcess: true);
            return ChatServiceProcessStartResult.Failed(ChatServiceProcessStartFailure.LaunchFailed, ex);
        }
    }

    /// <summary>
    /// Stops or detaches the currently owned process and releases host state.
    /// </summary>
    public void Stop(bool terminateProcess) {
        var process = Interlocked.Exchange(ref _process, null);
        PipeName = null;
        Volatile.Write(ref _lastOwnedProcessExited, 0);
        _stager.Reset();
        if (process is null) {
            return;
        }

        try {
            if (terminateProcess && !HasProcessExited(process)) {
                process.Kill(entireProcessTree: true);
            }
        } catch {
            // Shutdown remains best effort.
        } finally {
            process.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }
        _lifetimeCts.Cancel();
        Stop(terminateProcess: true);
    }

    /// <summary>
    /// Creates process-start state while keeping password material out of command-line arguments.
    /// </summary>
    internal static ProcessStartInfo CreateStartInfo(
        string serviceDirectory,
        string executablePath,
        string assemblyPath,
        bool hasExecutable,
        IReadOnlyList<string> launchArguments,
        ChatServiceLaunchProfileOptions? profileOptions) {
        var startInfo = new ProcessStartInfo {
            FileName = hasExecutable ? executablePath : "dotnet",
            WorkingDirectory = serviceDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (!hasExecutable) {
            startInfo.ArgumentList.Add(assemblyPath);
        }
        foreach (var argument in launchArguments) {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Remove(ChatServiceEnvironmentVariables.OpenAIBasicPassword);
        if (profileOptions is not null && !profileOptions.ClearOpenAIBasicAuth) {
            var password = (profileOptions.OpenAIBasicPassword ?? string.Empty).Trim();
            if (password.Length > 0) {
                startInfo.Environment[ChatServiceEnvironmentVariables.OpenAIBasicPassword] = password;
            }
        }

        return startInfo;
    }

    private static bool HasProcessExited(Process process) {
        try {
            return process.HasExited;
        } catch (ObjectDisposedException) {
            return true;
        } catch (InvalidOperationException) {
            return true;
        }
    }

    private void HandleProcessExited(Process process) {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _process, null, process), process)) {
            return;
        }

        PipeName = null;
        Volatile.Write(ref _lastOwnedProcessExited, 1);
        try {
            Exited?.Invoke();
        } catch {
            // A UI observer must not escape the process event callback.
        } finally {
            process.Dispose();
        }
    }

    private void ClearProcess(Process process) {
        if (ReferenceEquals(Interlocked.CompareExchange(ref _process, null, process), process)) {
            PipeName = null;
        }
    }

    private static void PublishLine(Action<string>? handler, string? line) {
        if (handler is null || string.IsNullOrWhiteSpace(line)) {
            return;
        }
        try {
            handler(line);
        } catch {
            // Process output consumption must not be able to terminate the reader callback.
        }
    }
}

/// <summary>
/// Describes one local chat service launch request.
/// </summary>
internal sealed class ChatServiceProcessStartOptions {
    public required string PipeName { get; init; }
    public bool DetachedServiceMode { get; init; }
    public int ParentProcessId { get; init; }
    public ChatServiceLaunchProfileOptions? ProfileOptions { get; init; }
    public string? ServiceSourceDirectory { get; init; }
    public string? AppBaseDirectory { get; init; }
    public TimeSpan StartupExitProbeDelay { get; init; }
}

/// <summary>
/// Classifies local service startup failures without coupling the host to a specific UI.
/// </summary>
internal enum ChatServiceProcessStartFailure {
    None = 0,
    SourceNotFound,
    StagingFailed,
    PayloadNotFound,
    LaunchFailed,
    ExitedDuringStartup
}

/// <summary>
/// Reports whether the service is available and whether this request launched it.
/// </summary>
internal readonly record struct ChatServiceProcessStartResult(
    bool IsRunning,
    bool Launched,
    ChatServiceProcessStartFailure Failure,
    Exception? Exception,
    string? PipeName,
    string? ServiceDirectory) {
    public static ChatServiceProcessStartResult Succeeded(
        bool launched,
        string? pipeName,
        string? serviceDirectory = null) =>
        new(true, launched, ChatServiceProcessStartFailure.None, null, pipeName, serviceDirectory);

    public static ChatServiceProcessStartResult Failed(
        ChatServiceProcessStartFailure failure,
        Exception? exception = null) =>
        new(false, false, failure, exception, null, null);
}
