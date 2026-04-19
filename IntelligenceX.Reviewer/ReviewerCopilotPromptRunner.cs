using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewerCopilotPromptRunner {
    private readonly CopilotClientOptions _options;

    public ReviewerCopilotPromptRunner(CopilotClientOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<ReviewerCopilotPromptResult> RunAsync(string prompt, string? model, TimeSpan timeout,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(prompt)) {
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        }
        if (timeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        }

        _options.Validate();
        var cliPath = await ResolveCliPathOrInstallAsync(_options, cancellationToken).ConfigureAwait(false);
        var startInfo = BuildStartInfo(_options, cliPath, prompt, model);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try {
            if (!process.Start()) {
                throw new InvalidOperationException("Failed to start Copilot CLI prompt process.");
            }
        } catch (Exception ex) {
            throw new InvalidOperationException("Copilot CLI not found or failed to start in prompt mode.", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            TryKill(process);
            var stderr = await ReadCompletedOrEmptyAsync(stderrTask).ConfigureAwait(false);
            throw new TimeoutException(BuildTimeoutMessage(timeout, stderr), ex);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderrText = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) {
            throw new InvalidOperationException(BuildExitMessage(process.ExitCode, stdout, stderrText));
        }

        var parsed = ParseJsonLines(stdout);
        var response = parsed.Response;
        if (string.IsNullOrWhiteSpace(response)) {
            response = stdout.Trim();
        }
        if (string.IsNullOrWhiteSpace(response)) {
            throw new InvalidOperationException(BuildExitMessage(process.ExitCode, stdout, stderrText,
                "Copilot CLI produced no review content."));
        }

        return new ReviewerCopilotPromptResult(response.Trim(), parsed.UsageSummary);
    }

    private static ProcessStartInfo BuildStartInfo(CopilotClientOptions options, string cliPath, string prompt,
        string? model) {
        var args = new List<string>();
        if (options.CliArgs.Count > 0) {
            args.AddRange(options.CliArgs);
        }
        args.Add("-p");
        args.Add(prompt);
        args.Add("--silent");
        args.Add("--no-ask-user");
        args.Add("--no-custom-instructions");
        args.Add("--no-auto-update");
        args.Add("--disable-builtin-mcps");
        args.Add("--stream");
        args.Add("off");
        args.Add("--output-format");
        args.Add("json");
        if (!string.IsNullOrWhiteSpace(model)) {
            args.Add("--model");
            args.Add(model!);
        }

        var (fileName, processArgs) = ResolveCliCommand(cliPath, args);
        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
            CreateNoWindow = true
        };
        foreach (var arg in processArgs) {
            startInfo.ArgumentList.Add(arg);
        }

        if (!options.InheritEnvironment) {
            startInfo.Environment.Clear();
        }
        foreach (var entry in options.Environment) {
            startInfo.Environment[entry.Key] = entry.Value;
        }
        startInfo.Environment.Remove("NODE_DEBUG");
        return startInfo;
    }

    private static async Task<string> ResolveCliPathOrInstallAsync(CopilotClientOptions options,
        CancellationToken cancellationToken) {
        try {
            return ResolveCliPath(options.CliPath ?? "copilot");
        } catch (InvalidOperationException ex) {
            if (!options.AutoInstallCli) {
                throw;
            }
            var command = CopilotCliInstall.GetCommand(options.AutoInstallMethod, options.AutoInstallPrerelease);
            var exitCode = await CopilotCliInstall.InstallAsync(command, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0) {
                throw new InvalidOperationException($"Copilot CLI install failed with exit code {exitCode}.", ex);
            }
            return ResolveCliPath(options.CliPath ?? "copilot");
        }
    }

    private static string ResolveCliPath(string cliPath) {
        if (string.IsNullOrWhiteSpace(cliPath)) {
            return "copilot";
        }
        if (Path.IsPathRooted(cliPath) || cliPath.Contains(Path.DirectorySeparatorChar) ||
            cliPath.Contains(Path.AltDirectorySeparatorChar)) {
            return cliPath;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) {
            throw new InvalidOperationException("Copilot CLI not found on PATH.\n" +
                                                CopilotCliInstall.GetInstallInstructions());
        }

        var exts = new List<string> { string.Empty };
        if (IsWindows()) {
            var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
            exts = !string.IsNullOrWhiteSpace(pathExt)
                ? new List<string>(pathExt.Split(';'))
                : new List<string> { ".exe", ".cmd", ".bat" };
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator)) {
            if (string.IsNullOrWhiteSpace(dir)) {
                continue;
            }
            foreach (var ext in exts) {
                var candidate = Path.Combine(dir.Trim(), cliPath + ext);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("Copilot CLI not found on PATH.\n" +
                                            CopilotCliInstall.GetInstallInstructions());
    }

    private static (string FileName, IEnumerable<string> Args) ResolveCliCommand(string cliPath,
        IEnumerable<string> args) {
        if (cliPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) {
            return ("node", Prepend(cliPath, args));
        }
        if (IsWindows() && !Path.IsPathRooted(cliPath)) {
            return ("cmd", Prepend("/c", Prepend(cliPath, args)));
        }
        return (cliPath, args);
    }

    private static IEnumerable<string> Prepend(string value, IEnumerable<string> args) {
        yield return value;
        foreach (var arg in args) {
            yield return arg;
        }
    }

    private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static (string Response, string? UsageSummary) ParseJsonLines(string stdout) {
        var response = new StringBuilder();
        string? finalMessage = null;
        string? usage = null;
        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }
            JsonObject? obj;
            try {
                obj = JsonLite.Parse(line).AsObject();
            } catch {
                continue;
            }
            if (obj is null) {
                continue;
            }

            var type = obj.GetString("type");
            var data = obj.GetObject("data");
            if (string.Equals(type, "assistant.message", StringComparison.Ordinal) && data is not null) {
                var content = data.GetString("content");
                if (!string.IsNullOrWhiteSpace(content)) {
                    finalMessage = content;
                }
                continue;
            }
            if (string.Equals(type, "assistant.message_delta", StringComparison.Ordinal) && data is not null) {
                var delta = data.GetString("deltaContent") ?? data.GetString("content");
                if (!string.IsNullOrWhiteSpace(delta)) {
                    response.Append(delta);
                }
                continue;
            }
            if (string.Equals(type, "result", StringComparison.Ordinal)) {
                usage = BuildUsageSummary(obj.GetObject("usage"));
            }
        }

        return (finalMessage ?? response.ToString(), usage);
    }

    internal static ReviewerCopilotPromptResult ParseJsonLinesForTests(string stdout) {
        var parsed = ParseJsonLines(stdout);
        return new ReviewerCopilotPromptResult(parsed.Response, parsed.UsageSummary);
    }

    private static string? BuildUsageSummary(JsonObject? usage) {
        if (usage is null) {
            return null;
        }
        var parts = new List<string>();
        var premiumRequests = usage.GetInt64("premiumRequests");
        if (premiumRequests.HasValue) {
            parts.Add($"premium requests: {premiumRequests.Value}");
        }
        var apiDuration = usage.GetInt64("totalApiDurationMs");
        if (apiDuration.HasValue) {
            parts.Add($"API: {apiDuration.Value} ms");
        }
        var sessionDuration = usage.GetInt64("sessionDurationMs");
        if (sessionDuration.HasValue) {
            parts.Add($"session: {sessionDuration.Value} ms");
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string BuildTimeoutMessage(TimeSpan timeout, string stderr) {
        var sb = new StringBuilder();
        sb.Append("Copilot CLI prompt mode timed out after ");
        sb.Append(timeout.TotalSeconds.ToString("0"));
        sb.Append(" seconds.");
        AppendRecentStderr(sb, stderr);
        return sb.ToString().TrimEnd();
    }

    private static string BuildExitMessage(int exitCode, string stdout, string stderr, string? prefix = null) {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(prefix)) {
            sb.Append(prefix);
            sb.Append(' ');
        }
        sb.Append("Copilot CLI prompt mode exited with code ");
        sb.Append(exitCode);
        sb.Append('.');
        AppendRecentStderr(sb, stderr);
        if (string.IsNullOrWhiteSpace(stderr) && !string.IsNullOrWhiteSpace(stdout)) {
            sb.AppendLine();
            sb.AppendLine("Recent Copilot CLI stdout:");
            AppendRecentLines(sb, stdout, maxLines: 6);
        }
        return sb.ToString().TrimEnd();
    }

    private static void AppendRecentStderr(StringBuilder sb, string stderr) {
        if (string.IsNullOrWhiteSpace(stderr)) {
            return;
        }
        sb.AppendLine();
        sb.AppendLine("Recent Copilot CLI stderr:");
        AppendRecentLines(sb, stderr, maxLines: 6);
    }

    private static void AppendRecentLines(StringBuilder sb, string text, int maxLines) {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = Math.Max(0, lines.Length - maxLines); i < lines.Length; i++) {
            sb.Append("  ");
            sb.AppendLine(lines[i].Trim());
        }
    }

    private static async Task<string> ReadCompletedOrEmptyAsync(Task<string> task) {
        if (!task.IsCompleted) {
            return string.Empty;
        }
        try {
            return await task.ConfigureAwait(false);
        } catch {
            return string.Empty;
        }
    }

    private static void TryKill(Process process) {
        try {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
            }
        } catch {
            // Best-effort cleanup only.
        }
    }
}

internal sealed record ReviewerCopilotPromptResult(string Response, string? UsageSummary);
