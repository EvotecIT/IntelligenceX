using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Engines.PowerShell;

/// <summary>
/// Requested host for PowerShell command execution.
/// </summary>
public enum PowerShellHostKind {
    /// <summary>
    /// Pick the best available host (prefers <c>pwsh</c>, then <c>powershell.exe</c>).
    /// </summary>
    Auto,

    /// <summary>
    /// Use Windows PowerShell (<c>powershell.exe</c>).
    /// </summary>
    WindowsPowerShell,

    /// <summary>
    /// Use PowerShell 7+ (<c>pwsh</c>).
    /// </summary>
    PowerShell7
}

/// <summary>
/// Request parameters for PowerShell command execution.
/// </summary>
public sealed class PowerShellCommandQueryRequest {
    /// <summary>
    /// Preferred host.
    /// </summary>
    public PowerShellHostKind Host { get; init; } = PowerShellHostKind.Auto;

    /// <summary>
    /// Single PowerShell command to execute.
    /// </summary>
    /// <remarks>
    /// Exactly one of <see cref="Command"/> or <see cref="Script"/> must be set.
    /// </remarks>
    public string? Command { get; init; }

    /// <summary>
    /// Multi-line script text to execute.
    /// </summary>
    /// <remarks>
    /// Exactly one of <see cref="Command"/> or <see cref="Script"/> must be set.
    /// </remarks>
    public string? Script { get; init; }

    /// <summary>
    /// Optional working directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; init; } = 60_000;

    /// <summary>
    /// Maximum combined output characters captured in <see cref="PowerShellCommandQueryResult.Output"/>.
    /// </summary>
    public int MaxOutputChars { get; init; } = 200_000;

    /// <summary>
    /// When true, stderr is appended to <see cref="PowerShellCommandQueryResult.Output"/>.
    /// </summary>
    public bool IncludeErrorStream { get; init; } = true;
}

/// <summary>
/// Typed result for PowerShell command execution.
/// </summary>
public sealed class PowerShellCommandQueryResult {
    /// <summary>
    /// Requested host name.
    /// </summary>
    public string RequestedHost { get; init; } = string.Empty;

    /// <summary>
    /// Resolved host name actually used.
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// Fully resolved shell executable path.
    /// </summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Executed payload kind: <c>command</c> or <c>script</c>.
    /// </summary>
    public string InputKind { get; init; } = string.Empty;

    /// <summary>
    /// Exit code returned by the shell process.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Indicates process timeout.
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// Elapsed execution time in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Captured stdout text.
    /// </summary>
    public string StdOut { get; init; } = string.Empty;

    /// <summary>
    /// Captured stderr text.
    /// </summary>
    public string StdErr { get; init; } = string.Empty;

    /// <summary>
    /// Combined output used by callers for display/correlation.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// True when combined output was truncated to <see cref="PowerShellCommandQueryRequest.MaxOutputChars"/>.
    /// </summary>
    public bool OutputTruncated { get; init; }

    /// <summary>
    /// Full (untruncated) combined output length.
    /// </summary>
    public int OutputLength { get; init; }
}

/// <summary>
/// Failure categories for PowerShell command execution.
/// </summary>
public enum PowerShellCommandQueryFailureCode {
    /// <summary>
    /// Request was null or malformed.
    /// </summary>
    InvalidRequest,

    /// <summary>
    /// Operation was canceled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Requested host is unavailable on this machine.
    /// </summary>
    HostNotAvailable,

    /// <summary>
    /// Command timed out.
    /// </summary>
    Timeout,

    /// <summary>
    /// Query execution failed.
    /// </summary>
    QueryFailed
}

/// <summary>
/// Failure payload for PowerShell command execution.
/// </summary>
public sealed class PowerShellCommandQueryFailure {
    /// <summary>
    /// Failure category.
    /// </summary>
    public PowerShellCommandQueryFailureCode Code { get; init; }

    /// <summary>
    /// Human-readable failure message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Requested host when available.
    /// </summary>
    public string? RequestedHost { get; init; }

    /// <summary>
    /// CLR exception type when available.
    /// </summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Exception HRESULT when available.
    /// </summary>
    public int? HResult { get; init; }
}

/// <summary>
/// Non-throwing attempt result for PowerShell command execution.
/// </summary>
public sealed class PowerShellCommandQueryTryResult {
    /// <summary>
    /// True when execution succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Result payload when <see cref="Success"/> is true.
    /// </summary>
    public PowerShellCommandQueryResult? Result { get; init; }

    /// <summary>
    /// Failure payload when <see cref="Success"/> is false.
    /// </summary>
    public PowerShellCommandQueryFailure? Failure { get; init; }
}

/// <summary>
/// Executes PowerShell commands/scripts through external shell hosts.
/// </summary>
public static class PowerShellCommandQueryExecutor {
    private sealed class HostNotAvailableException : Exception {
        public HostNotAvailableException(string message) : base(message) { }
    }

    /// <summary>
    /// Returns available host ids for this machine.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableHosts() {
        var hosts = new List<string>(2);
        if (TryResolveExecutable(PowerShellHostKind.PowerShell7, out _)) {
            hosts.Add(ToHostId(PowerShellHostKind.PowerShell7));
        }
        if (TryResolveExecutable(PowerShellHostKind.WindowsPowerShell, out _)) {
            hosts.Add(ToHostId(PowerShellHostKind.WindowsPowerShell));
        }
        return hosts;
    }

    /// <summary>
    /// Executes a PowerShell command/query.
    /// </summary>
    /// <param name="request">Execution request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed execution result.</returns>
    public static PowerShellCommandQueryResult Execute(
        PowerShellCommandQueryRequest request,
        CancellationToken cancellationToken = default) {
        if (request is null) {
            throw new ArgumentNullException(nameof(request));
        }

        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedHost = ResolveHost(request.Host);
        if (!TryResolveExecutable(resolvedHost, out var executablePath)) {
            throw new HostNotAvailableException($"Requested PowerShell host '{ToHostId(request.Host)}' is not available on this machine.");
        }

        var scriptText = HasText(request.Command) ? request.Command!.Trim() : request.Script!.Trim();
        var inputKind = HasText(request.Command) ? "command" : "script";
        var arguments = BuildShellArguments(resolvedHost);
        var workingDirectory = NormalizeWorkingDirectory(request.WorkingDirectory);

        using var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = executablePath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        if (!process.Start()) {
            throw new InvalidOperationException("Failed to start PowerShell host process.");
        }

        using var cancellationRegistration = cancellationToken.Register(static state => {
            if (state is Process p) {
                TryKillProcess(p);
            }
        }, process);

        var stopwatch = Stopwatch.StartNew();

        using (var stdin = process.StandardInput) {
            stdin.Write(scriptText);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        if (!process.WaitForExit(request.TimeoutMs)) {
            timedOut = true;
            TryKillProcess(process);
            process.WaitForExit();
        }

        stopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();

        var stdOut = stdOutTask.GetAwaiter().GetResult() ?? string.Empty;
        var stdErr = stdErrTask.GetAwaiter().GetResult() ?? string.Empty;
        var combined = BuildCombinedOutput(stdOut, stdErr, request.IncludeErrorStream);
        var output = ApplyMaxChars(combined, request.MaxOutputChars, out var truncated);

        if (timedOut) {
            throw new TimeoutException($"PowerShell host timed out after {request.TimeoutMs} ms.");
        }

        return new PowerShellCommandQueryResult {
            RequestedHost = ToHostId(request.Host),
            Host = ToHostId(resolvedHost),
            ExecutablePath = executablePath,
            InputKind = inputKind,
            ExitCode = process.ExitCode,
            TimedOut = false,
            DurationMs = stopwatch.ElapsedMilliseconds,
            StdOut = stdOut,
            StdErr = stdErr,
            Output = output,
            OutputTruncated = truncated,
            OutputLength = combined.Length
        };
    }

    /// <summary>
    /// Executes a PowerShell command/query asynchronously.
    /// </summary>
    public static Task<PowerShellCommandQueryResult> ExecuteAsync(
        PowerShellCommandQueryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Execute(request, cancellationToken));

    /// <summary>
    /// Non-throwing PowerShell command/query wrapper.
    /// </summary>
    public static PowerShellCommandQueryTryResult TryExecute(
        PowerShellCommandQueryRequest? request,
        CancellationToken cancellationToken = default) {
        if (request is null) {
            return new PowerShellCommandQueryTryResult {
                Success = false,
                Failure = new PowerShellCommandQueryFailure {
                    Code = PowerShellCommandQueryFailureCode.InvalidRequest,
                    Message = "Request is required."
                }
            };
        }

        try {
            var result = Execute(request, cancellationToken);
            return new PowerShellCommandQueryTryResult {
                Success = true,
                Result = result
            };
        } catch (ArgumentException ex) {
            return Fail(PowerShellCommandQueryFailureCode.InvalidRequest, request, ex);
        } catch (OperationCanceledException ex) {
            return Fail(PowerShellCommandQueryFailureCode.Cancelled, request, ex);
        } catch (HostNotAvailableException ex) {
            return Fail(PowerShellCommandQueryFailureCode.HostNotAvailable, request, ex);
        } catch (TimeoutException ex) {
            return Fail(PowerShellCommandQueryFailureCode.Timeout, request, ex);
        } catch (Exception ex) {
            return Fail(PowerShellCommandQueryFailureCode.QueryFailed, request, ex);
        }
    }

    /// <summary>
    /// Asynchronous non-throwing wrapper.
    /// </summary>
    public static Task<PowerShellCommandQueryTryResult> TryExecuteAsync(
        PowerShellCommandQueryRequest? request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TryExecute(request, cancellationToken));

    private static PowerShellCommandQueryTryResult Fail(
        PowerShellCommandQueryFailureCode code,
        PowerShellCommandQueryRequest request,
        Exception ex) =>
        new() {
            Success = false,
            Failure = new PowerShellCommandQueryFailure {
                Code = code,
                RequestedHost = ToHostId(request.Host),
                Message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message,
                ExceptionType = ex.GetType().FullName,
                HResult = ex.HResult
            }
        };

    private static string NormalizeWorkingDirectory(string? workingDirectory) {
        if (!HasText(workingDirectory)) {
            return Environment.CurrentDirectory;
        }

        var fullPath = Path.GetFullPath(workingDirectory!.Trim());
        if (!Directory.Exists(fullPath)) {
            throw new ArgumentException($"working_directory does not exist: {fullPath}", nameof(workingDirectory));
        }

        return fullPath;
    }

    private static void ValidateRequest(PowerShellCommandQueryRequest request) {
        var hasCommand = HasText(request.Command);
        var hasScript = HasText(request.Script);

        if (hasCommand == hasScript) {
            throw new ArgumentException("Exactly one of command or script must be provided.", nameof(request));
        }

        if (request.TimeoutMs <= 0) {
            throw new ArgumentException("timeout_ms must be greater than zero.", nameof(request));
        }

        if (request.MaxOutputChars <= 0) {
            throw new ArgumentException("max_output_chars must be greater than zero.", nameof(request));
        }
    }

    private static string BuildShellArguments(PowerShellHostKind host) {
        if (host == PowerShellHostKind.WindowsPowerShell) {
            return "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -";
        }

        return "-NoLogo -NoProfile -NonInteractive -Command -";
    }

    private static PowerShellHostKind ResolveHost(PowerShellHostKind requestedHost) {
        if (requestedHost != PowerShellHostKind.Auto) {
            return requestedHost;
        }

        if (TryResolveExecutable(PowerShellHostKind.PowerShell7, out _)) {
            return PowerShellHostKind.PowerShell7;
        }
        return PowerShellHostKind.WindowsPowerShell;
    }

    private static bool TryResolveExecutable(PowerShellHostKind host, out string executablePath) {
        executablePath = string.Empty;
        foreach (var candidate in GetExecutableCandidates(host)) {
            if (TryResolveExecutableCandidate(candidate, out executablePath)) {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetExecutableCandidates(PowerShellHostKind host) {
        switch (host) {
            case PowerShellHostKind.PowerShell7:
                yield return "pwsh.exe";
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (HasText(programFiles)) {
                    yield return Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
                }
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (HasText(programFilesX86)) {
                    yield return Path.Combine(programFilesX86, "PowerShell", "7", "pwsh.exe");
                }
                yield break;

            case PowerShellHostKind.WindowsPowerShell:
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    if (HasText(systemDirectory)) {
                        yield return Path.Combine(systemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                    }
                    yield return "powershell.exe";
                }
                yield break;

            default:
                yield break;
        }
    }

    private static bool TryResolveExecutableCandidate(string candidate, out string executablePath) {
        executablePath = string.Empty;
        if (!HasText(candidate)) {
            return false;
        }

        var trimmed = candidate.Trim();
        if (Path.IsPathRooted(trimmed)) {
            if (!File.Exists(trimmed)) {
                return false;
            }

            executablePath = Path.GetFullPath(trimmed);
            return true;
        }

        if (trimmed.Contains(@"\", StringComparison.Ordinal) || trimmed.Contains("/", StringComparison.Ordinal)) {
            var relative = Path.GetFullPath(trimmed);
            if (!File.Exists(relative)) {
                return false;
            }

            executablePath = relative;
            return true;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!HasText(pathEnv)) {
            return false;
        }

        var segments = pathEnv!.Split(Path.PathSeparator);
        for (var i = 0; i < segments.Length; i++) {
            var segment = segments[i];
            if (!HasText(segment)) {
                continue;
            }

            var full = Path.Combine(segment.Trim(), trimmed);
            if (!File.Exists(full)) {
                continue;
            }

            executablePath = Path.GetFullPath(full);
            return true;
        }

        return false;
    }

    private static string BuildCombinedOutput(string stdOut, string stdErr, bool includeErrorStream) {
        if (!includeErrorStream || !HasText(stdErr)) {
            return stdOut ?? string.Empty;
        }

        if (!HasText(stdOut)) {
            return stdErr ?? string.Empty;
        }

        var builder = new StringBuilder((stdOut?.Length ?? 0) + (stdErr?.Length ?? 0) + 2);
        builder.Append(stdOut);
        builder.AppendLine();
        builder.Append(stdErr);
        return builder.ToString();
    }

    private static string ApplyMaxChars(string value, int maxChars, out bool truncated) {
        var text = value ?? string.Empty;
        if (text.Length <= maxChars) {
            truncated = false;
            return text;
        }

        truncated = true;
        return text.Substring(0, maxChars);
    }

    private static void TryKillProcess(Process process) {
        try {
            if (!process.HasExited) {
                process.Kill();
            }
        } catch {
            // best effort
        }
    }

    private static bool HasText(string? value) {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string ToHostId(PowerShellHostKind host) {
        return host switch {
            PowerShellHostKind.WindowsPowerShell => "windows_powershell",
            PowerShellHostKind.PowerShell7 => "pwsh",
            _ => "auto"
        };
    }
}
