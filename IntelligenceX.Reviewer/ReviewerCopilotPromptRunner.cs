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
        ValidateGitHubActionsAuth(_options);
        var cliPath = await ResolveCliPathOrInstallAsync(_options, cancellationToken).ConfigureAwait(false);
        var logDirectory = TryPrepareLogDirectory(_options);
        var disableBuiltinMcps = true;
        var disableToolSurface = true;
        var captureLogs = !string.IsNullOrWhiteSpace(logDirectory);
        CopilotPromptProcessResult result;
        ReviewerCopilotPromptResult? successfulResult = null;
        while (true) {
            var effectiveLogDirectory = captureLogs ? logDirectory : null;
            var startInfo = BuildStartInfo(_options, cliPath, prompt, model, disableBuiltinMcps,
                disableToolSurface, effectiveLogDirectory);
            result = await RunProcessAsync(startInfo, timeout, cancellationToken).ConfigureAwait(false);
            if (TryBuildSuccessfulResult(result, out successfulResult)) {
                break;
            }
            if (!TryApplyCompatibilityFallbacks(result, ref disableBuiltinMcps, ref disableToolSurface,
                    ref captureLogs)) {
                break;
            }
        }

        if (successfulResult is not null) {
            return successfulResult;
        }

        if (result.ExitCode != 0) {
            WriteRecentLogTail(logDirectory);
            throw new InvalidOperationException(BuildExitMessage(result.ExitCode, result.Stdout, result.Stderr,
                logDirectory: logDirectory));
        }

        var parsed = ParseJsonOutput(result.Stdout);
        var response = ResolveSuccessfulResponse(parsed, result.Stdout);
        if (string.IsNullOrWhiteSpace(response)) {
            WriteRecentLogTail(logDirectory);
            var prefix = parsed.ParseErrorCount > 0
                ? "Copilot CLI produced malformed JSON output and no review content."
                : "Copilot CLI produced no review content.";
            throw new InvalidOperationException(BuildExitMessage(result.ExitCode, result.Stdout, result.Stderr,
                prefix, logDirectory));
        }

        return new ReviewerCopilotPromptResult(response.Trim(), parsed.UsageSummary);
    }

    private static bool TryBuildSuccessfulResult(CopilotPromptProcessResult result,
        out ReviewerCopilotPromptResult? successfulResult) {
        successfulResult = null;
        if (result.ExitCode != 0) {
            return false;
        }

        var parsed = ParseJsonOutput(result.Stdout);
        var response = ResolveSuccessfulResponse(parsed, result.Stdout);
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        successfulResult = new ReviewerCopilotPromptResult(response.Trim(), parsed.UsageSummary);
        return true;
    }

    private static string ResolveSuccessfulResponse(ParsedCopilotPromptOutput parsed, string stdout) {
        if (!string.IsNullOrWhiteSpace(parsed.Response)) {
            return parsed.Response;
        }

        var trimmed = stdout.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return string.Empty;
        }

        return HasNonJsonTextOutsideObjects(stdout) ? trimmed : string.Empty;
    }

    private static async Task<CopilotPromptProcessResult> RunProcessAsync(ProcessStartInfo startInfo,
        TimeSpan timeout, CancellationToken cancellationToken) {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try {
            if (!process.Start()) {
                throw new InvalidOperationException("Failed to start Copilot CLI prompt process.");
            }
        } catch (Exception ex) {
            throw new InvalidOperationException("Copilot CLI not found or failed to start in prompt mode.", ex);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputLock = new object();
        var startedUtc = DateTime.UtcNow;

        var stdoutTask = PumpReaderAsync(process.StandardOutput, stdout, outputLock);
        var stderrTask = PumpReaderAsync(process.StandardError, stderr, outputLock);

        while (!process.HasExited) {
            if (cancellationToken.IsCancellationRequested) {
                TryKill(process);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (DateTime.UtcNow - startedUtc >= timeout) {
                TryKill(process);
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                throw new TimeoutException(BuildTimeoutMessage(timeout, SnapshotText(stderr, outputLock)));
            }
            await Task.Delay(250).ConfigureAwait(false);
        }

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        return new CopilotPromptProcessResult(process.ExitCode, SnapshotText(stdout, outputLock),
            SnapshotText(stderr, outputLock));
    }

    private static ProcessStartInfo BuildStartInfo(CopilotClientOptions options, string cliPath, string prompt,
        string? model, bool disableBuiltinMcps, bool disableToolSurface, string? logDirectory) {
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
        if (!string.IsNullOrWhiteSpace(logDirectory)) {
            args.Add("--log-dir");
            args.Add(logDirectory!);
            args.Add("--log-level");
            args.Add(string.IsNullOrWhiteSpace(options.LogLevel) ? "info" : options.LogLevel);
        }
        if (disableToolSurface) {
            args.Add("--available-tools=none");
        }
        if (disableBuiltinMcps) {
            args.Add("--disable-builtin-mcps");
        }
        args.Add("--stream");
        args.Add("on");
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

        var installed = CopilotCliInstall.TryResolveInstalledCliPath(cliPath);
        if (!string.IsNullOrWhiteSpace(installed)) {
            return installed!;
        }

        throw new InvalidOperationException("Copilot CLI not found on PATH.\n" +
                                            CopilotCliInstall.GetInstallInstructions());
    }

    private static (string FileName, IEnumerable<string> Args) ResolveCliCommand(string cliPath,
        IEnumerable<string> args) {
        if (cliPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) {
            return ("node", Prepend(cliPath, args));
        }
        if (RequiresCmdWrapper(cliPath)) {
            return ("cmd", Prepend("/c", Prepend(cliPath, args)));
        }
        return (cliPath, args);
    }

    internal static (string FileName, string[] Args) ResolveCliCommandForTests(string cliPath, params string[] args) {
        var (fileName, resolvedArgs) = ResolveCliCommand(cliPath, args);
        return (fileName, new List<string>(resolvedArgs).ToArray());
    }

    private static IEnumerable<string> Prepend(string value, IEnumerable<string> args) {
        yield return value;
        foreach (var arg in args) {
            yield return arg;
        }
    }

    private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static bool RequiresCmdWrapper(string cliPath) {
        if (!IsWindows()) {
            return false;
        }
        if (!Path.IsPathRooted(cliPath)) {
            return true;
        }
        return cliPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
               cliPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryApplyCompatibilityFallbacks(CopilotPromptProcessResult result, ref bool disableBuiltinMcps,
        ref bool disableToolSurface, ref bool captureLogs) {
        var retry = false;
        if (disableToolSurface && IsUnsupportedAvailableToolsFlag(result.Stdout, result.Stderr)) {
            disableToolSurface = false;
            retry = true;
        }
        if (disableBuiltinMcps && IsUnsupportedDisableBuiltinMcpsFlag(result.Stdout, result.Stderr)) {
            disableBuiltinMcps = false;
            retry = true;
        }
        if (captureLogs && IsUnsupportedLogCaptureFlag(result.Stdout, result.Stderr)) {
            captureLogs = false;
            retry = true;
        }
        return retry;
    }

    private static ParsedCopilotPromptOutput ParseJsonOutput(string stdout) {
        var response = new StringBuilder();
        string? finalMessage = null;
        string? usage = null;
        var jsonObjectCount = 0;
        var parseErrorCount = 0;

        foreach (var obj in ParseJsonObjects(stdout, out parseErrorCount)) {
            jsonObjectCount++;

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

        return new ParsedCopilotPromptOutput(finalMessage ?? response.ToString(), usage, jsonObjectCount, parseErrorCount);
    }

    internal static ReviewerCopilotPromptResult ParseJsonLinesForTests(string stdout) {
        var parsed = ParseJsonOutput(stdout);
        return new ReviewerCopilotPromptResult(parsed.Response, parsed.UsageSummary);
    }

    internal static (bool Retry, bool DisableBuiltinMcps, bool DisableToolSurface, bool CaptureLogs)
        ApplyCompatibilityFallbacksForTests(int exitCode, string stdout, string stderr, bool disableBuiltinMcps,
            bool disableToolSurface, bool captureLogs) {
        var retry = TryApplyCompatibilityFallbacks(new CopilotPromptProcessResult(exitCode, stdout, stderr),
            ref disableBuiltinMcps, ref disableToolSurface, ref captureLogs);
        return (retry, disableBuiltinMcps, disableToolSurface, captureLogs);
    }

    internal static bool TryBuildSuccessfulResultForTests(int exitCode, string stdout, string stderr,
        out ReviewerCopilotPromptResult? successfulResult) =>
        TryBuildSuccessfulResult(new CopilotPromptProcessResult(exitCode, stdout, stderr), out successfulResult);

    internal static string[] BuildArgumentsForTests(CopilotClientOptions options, string cliPath, string prompt,
        string? model = null, bool disableBuiltinMcps = true, bool disableToolSurface = true,
        bool captureLogs = true) {
        var startInfo = BuildStartInfo(options, cliPath, prompt, model, disableBuiltinMcps, disableToolSurface,
            captureLogs ? TryPrepareLogDirectory(options) : null);
        var args = new string[startInfo.ArgumentList.Count];
        startInfo.ArgumentList.CopyTo(args, 0);
        return args;
    }

    internal static string? ValidateGitHubActionsAuthForTests(CopilotClientOptions options,
        IReadOnlyDictionary<string, string?> environment) {
        return ValidateGitHubActionsAuth(options, environment);
    }

    private static void ValidateGitHubActionsAuth(CopilotClientOptions options) {
        var message = ValidateGitHubActionsAuth(options, null);
        if (!string.IsNullOrWhiteSpace(message)) {
            throw new InvalidOperationException(message);
        }
    }

    private static string? ValidateGitHubActionsAuth(CopilotClientOptions options,
        IReadOnlyDictionary<string, string?>? environment) {
        if (!IsTruthy(GetHostEnvironment(options, environment, "GITHUB_ACTIONS"))) {
            return null;
        }
        if (HasCopilotProviderOverride(options, environment)) {
            return null;
        }

        var copilotToken = GetChildEnvironment(options, environment, "COPILOT_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(copilotToken) && IsSupportedCopilotToken(copilotToken)) {
            return null;
        }

        var ghToken = GetChildEnvironment(options, environment, "GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(ghToken) && IsSupportedCopilotToken(ghToken)) {
            return null;
        }

        var githubToken = GetChildEnvironment(options, environment, "GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(githubToken) && IsSupportedCopilotToken(githubToken)) {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(copilotToken)) {
            return "Copilot CLI in GitHub Actions needs COPILOT_GITHUB_TOKEN to be an OAuth, fine-grained PAT with Copilot Requests, or GitHub App user-to-server token. GitHub App installation tokens and the built-in Actions GITHUB_TOKEN are not supported by Copilot CLI model requests.";
        }

        return "Copilot CLI in GitHub Actions needs COPILOT_GITHUB_TOKEN set to a fine-grained GitHub token with the Copilot Requests permission. GitHub App installation tokens and the built-in Actions GITHUB_TOKEN are not supported by Copilot CLI model requests.";
    }

    private static bool HasCopilotProviderOverride(CopilotClientOptions options,
        IReadOnlyDictionary<string, string?>? environment) {
        return !string.IsNullOrWhiteSpace(GetChildEnvironment(options, environment, "COPILOT_PROVIDER_BASE_URL")) ||
               !string.IsNullOrWhiteSpace(GetChildEnvironment(options, environment, "COPILOT_PROVIDER_API_KEY"));
    }

    private static string? GetChildEnvironment(CopilotClientOptions options,
        IReadOnlyDictionary<string, string?>? environment, string name) {
        if (options.Environment.TryGetValue(name, out var configured)) {
            return configured;
        }
        if (!options.InheritEnvironment) {
            return null;
        }
        return GetHostEnvironment(options, environment, name);
    }

    private static string? GetHostEnvironment(CopilotClientOptions options,
        IReadOnlyDictionary<string, string?>? environment, string name) {
        if (options.Environment.TryGetValue(name, out var configured)) {
            return configured;
        }
        if (environment is not null && environment.TryGetValue(name, out var supplied)) {
            return supplied;
        }
        return Environment.GetEnvironmentVariable(name);
    }

    private static bool IsSupportedCopilotToken(string token) {
        var trimmed = token.Trim();
        return trimmed.StartsWith("github_pat_", StringComparison.Ordinal) ||
               trimmed.StartsWith("gho_", StringComparison.Ordinal) ||
               trimmed.StartsWith("ghu_", StringComparison.Ordinal);
    }

    private static bool IsTruthy(string? value) {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsUnsupportedDisableBuiltinMcpsFlagForTests(string stdout, string stderr) =>
        IsUnsupportedDisableBuiltinMcpsFlag(stdout, stderr);

    internal static bool IsUnsupportedAvailableToolsFlagForTests(string stdout, string stderr) =>
        IsUnsupportedAvailableToolsFlag(stdout, stderr);

    internal static bool IsUnsupportedLogCaptureFlagForTests(string stdout, string stderr) =>
        IsUnsupportedLogCaptureFlag(stdout, stderr);

    private static bool IsUnsupportedDisableBuiltinMcpsFlag(string stdout, string stderr) {
        return IsUnsupportedFlag(stdout, stderr, "--disable-builtin-mcps");
    }

    private static bool IsUnsupportedAvailableToolsFlag(string stdout, string stderr) {
        var combined = string.Concat(stdout, "\n", stderr);
        if (combined.Contains("Unknown tool name in the tool allowlist", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("none", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return IsUnsupportedFlag(stdout, stderr, "--available-tools");
    }

    private static bool IsUnsupportedLogCaptureFlag(string stdout, string stderr) {
        return IsUnsupportedFlag(stdout, stderr, "--log-dir") ||
               IsUnsupportedFlag(stdout, stderr, "--log-level");
    }

    private static bool IsUnsupportedFlag(string stdout, string stderr, string flag) {
        var combined = string.Concat(stdout, "\n", stderr);
        return combined.Contains(flag, StringComparison.OrdinalIgnoreCase) &&
               (combined.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("unrecognized", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("invalid option", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("unexpected argument", StringComparison.OrdinalIgnoreCase));
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
        sb.Append(" seconds without completing.");
        AppendRecentStderr(sb, stderr);
        return sb.ToString().TrimEnd();
    }

    private static string BuildExitMessage(int exitCode, string stdout, string stderr, string? prefix = null,
        string? logDirectory = null) {
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
        if (!string.IsNullOrWhiteSpace(logDirectory)) {
            sb.AppendLine();
            sb.Append("Copilot CLI logs were written under ");
            sb.Append(logDirectory);
            sb.Append('.');
        }
        return sb.ToString().TrimEnd();
    }

    private static string? TryPrepareLogDirectory(CopilotClientOptions options) {
        try {
            var workingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory;
            var logDirectory = Path.Combine(Path.GetFullPath(workingDirectory), "artifacts", "copilot-logs");
            Directory.CreateDirectory(logDirectory);
            return logDirectory;
        } catch {
            return null;
        }
    }

    private static void WriteRecentLogTail(string? logDirectory) {
        if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory)) {
            return;
        }
        try {
            var files = Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) {
                return;
            }
            Array.Sort(files, static (left, right) =>
                File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left)));
            var newest = files[0];
            Console.Error.WriteLine("Recent Copilot CLI log tail:");
            Console.Error.WriteLine(newest);
            var text = File.ReadAllText(newest);
            var sb = new StringBuilder();
            AppendRecentLines(sb, text, maxLines: 20);
            Console.Error.Write(sb.ToString());
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to read Copilot CLI logs: {ex.Message}");
        }
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

    private static async Task PumpReaderAsync(StreamReader reader, StringBuilder builder, object sync) {
        var buffer = new char[2048];
        while (true) {
            int read;
            try {
                read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            } catch {
                break;
            }
            if (read <= 0) {
                break;
            }
            lock (sync) {
                builder.Append(buffer, 0, read);
            }
        }
    }

    private static string SnapshotText(StringBuilder builder, object sync) {
        lock (sync) {
            return builder.ToString();
        }
    }

    private static IReadOnlyList<JsonObject> ParseJsonObjects(string text, out int parseErrorCount) {
        parseErrorCount = 0;
        var results = new List<JsonObject>();
        if (string.IsNullOrWhiteSpace(text)) {
            return results;
        }

        var span = text.AsSpan();
        var index = 0;
        while (index < span.Length) {
            while (index < span.Length && span[index] != '{') {
                index++;
            }
            if (index >= span.Length) {
                break;
            }

            var start = index;
            if (!TryFindJsonObjectEnd(span, start, out var end)) {
                parseErrorCount++;
                break;
            }

            var candidate = text.Substring(start, end - start + 1);
            JsonObject? obj = null;
            try {
                obj = JsonLite.Parse(candidate).AsObject();
            } catch {
                parseErrorCount++;
            }

            if (obj is not null) {
                results.Add(obj);
            }

            index = end + 1;
        }

        return results;
    }

    private static bool HasNonJsonTextOutsideObjects(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var span = text.AsSpan();
        var index = 0;
        while (index < span.Length) {
            while (index < span.Length && char.IsWhiteSpace(span[index])) {
                index++;
            }
            if (index >= span.Length) {
                break;
            }

            if (span[index] != '{') {
                return true;
            }

            if (!TryFindJsonObjectEnd(span, index, out var end)) {
                return false;
            }

            index = end + 1;
        }

        return false;
    }

    private static bool TryFindJsonObjectEnd(ReadOnlySpan<char> span, int start, out int end) {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < span.Length; i++) {
            var ch = span[i];
            if (inString) {
                if (escaped) {
                    escaped = false;
                    continue;
                }
                if (ch == '\\') {
                    escaped = true;
                    continue;
                }
                if (ch == '"') {
                    inString = false;
                }
                continue;
            }

            if (ch == '"') {
                inString = true;
                continue;
            }
            if (ch == '{') {
                depth++;
                continue;
            }
            if (ch != '}') {
                continue;
            }

            depth--;
            if (depth == 0) {
                end = i;
                return true;
            }
        }

        end = -1;
        return false;
    }
}

internal sealed record ReviewerCopilotPromptResult(string Response, string? UsageSummary);

internal sealed record CopilotPromptProcessResult(int ExitCode, string Stdout, string Stderr);
internal sealed record ParsedCopilotPromptOutput(string Response, string? UsageSummary, int JsonObjectCount,
    int ParseErrorCount);
