using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Engines.PowerShell;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Executes command/script text using local PowerShell hosts.
/// </summary>
public sealed class PowerShellRunTool : PowerShellToolBase, ITool {
    private static readonly string[] PowerShellMutatingVerbPrefixes = {
        "set-",
        "new-",
        "remove-",
        "add-",
        "clear-",
        "rename-",
        "move-",
        "copy-",
        "import-",
        "export-",
        "install-",
        "uninstall-",
        "update-",
        "enable-",
        "disable-",
        "restart-",
        "stop-",
        "start-"
    };

    private static readonly string[] PowerShellMutatingFragments = {
        "| out-file",
        "| set-content",
        "| add-content",
        "| remove-item",
        "| clear-content",
        " > ",
        " >> "
    };

    private static readonly string[] CmdMutatingVerbPrefixes = {
        "del ",
        "erase ",
        "copy ",
        "move ",
        "ren ",
        "rename ",
        "mkdir ",
        "md ",
        "rmdir ",
        "rd "
    };

    private static readonly string[] CmdMutatingFragments = {
        " > ",
        " >> ",
        " copy ",
        " move ",
        " del ",
        " erase ",
        " rmdir ",
        " rd "
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly ToolDefinition DefinitionValue = new(
        "powershell_run",
        "Execute a shell command or script via pwsh/windows_powershell/cmd (dangerous; read_only default, read_write requires explicit policy and intent).",
        ToolSchema.Object(
                ("host", ToolSchema.String("Shell host: auto, pwsh, windows_powershell, cmd. Default auto (PowerShell).").Enum("auto", "pwsh", "windows_powershell", "cmd")),
                ("intent", ToolSchema.String("Execution intent: read_only (default) or read_write.").Enum("read_only", "read_write")),
                ("allow_write", ToolSchema.Boolean("Explicit write approval flag for read_write intent. Required when policy enforces explicit write confirmation.")),
                ("command", ToolSchema.String("Single shell command text. Provide exactly one of command or script.")),
                ("script", ToolSchema.String("Multi-line shell script text. Provide exactly one of command or script.")),
                ("working_directory", ToolSchema.String("Optional working directory for process execution.")),
                ("timeout_ms", ToolSchema.Integer("Execution timeout in milliseconds.")),
                ("max_output_chars", ToolSchema.Integer("Maximum combined output characters to return.")),
                ("include_error_stream", ToolSchema.Boolean("When true, include stderr in output. Default true.")))
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.StringEquals(
            intentArgumentName: "intent",
            intentStringValue: "read_write",
            confirmationArgumentName: "allow_write"));
    private sealed record PowerShellRunRequest(
        string? Command,
        string? Script,
        string Payload,
        ShellHostKind HostKind,
        PowerShellExecutionIntent Intent,
        bool AllowWrite,
        int TimeoutMs,
        int MaxOutputChars,
        bool IncludeErrorStream,
        string? WorkingDirectory);

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellRunTool"/> class.
    /// </summary>
    public PowerShellRunTool(PowerShellToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<PowerShellRunRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var command = reader.OptionalString("command");
            var script = reader.OptionalString("script");

            var hasCommand = !string.IsNullOrWhiteSpace(command);
            var hasScript = !string.IsNullOrWhiteSpace(script);
            if (hasCommand == hasScript) {
                return ToolRequestBindingResult<PowerShellRunRequest>.Failure("Provide exactly one of command or script.");
            }

            if (!TryParseIntent(reader.OptionalString("intent"), out var intent, out var intentError)) {
                return ToolRequestBindingResult<PowerShellRunRequest>.Failure(intentError ?? "Invalid intent value.");
            }

            if (!TryParseHost(reader.OptionalString("host"), out var hostKind, out var hostError)) {
                return ToolRequestBindingResult<PowerShellRunRequest>.Failure(hostError ?? "Invalid host value.");
            }

            var request = new PowerShellRunRequest(
                Command: command,
                Script: script,
                Payload: hasCommand ? command! : script!,
                HostKind: hostKind,
                Intent: intent,
                AllowWrite: reader.Boolean("allow_write", defaultValue: false),
                TimeoutMs: reader.CappedInt32(
                    "timeout_ms",
                    Options.DefaultTimeoutMs,
                    1,
                    Options.MaxTimeoutMs),
                MaxOutputChars: reader.CappedInt32(
                    "max_output_chars",
                    Options.DefaultMaxOutputChars,
                    256,
                    Options.MaxOutputChars),
                IncludeErrorStream: reader.Boolean("include_error_stream", defaultValue: true),
                WorkingDirectory: reader.OptionalString("working_directory"));
            return ToolRequestBindingResult<PowerShellRunRequest>.Success(request);
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<PowerShellRunRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "IX.PowerShell pack is disabled by policy.",
                hints: new[] { "Enable the PowerShell pack explicitly in host/service options before calling powershell_run." },
                isTransient: false);
        }

        var request = context.Request;
        var mutatingReason = Options.EnableMutationHeuristic
            ? DetectMutatingPayloadReason(request.Payload, request.HostKind)
            : null;

        if (request.Intent == PowerShellExecutionIntent.ReadOnly && mutatingReason is not null) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: "Read-only intent rejected a potentially mutating shell payload.",
                hints: new[] {
                    $"Detected mutating pattern: {mutatingReason}",
                    "If this is intentional, set intent=read_write.",
                    "If policy requires it, also set allow_write=true."
                },
                isTransient: false);
        }

        if (request.Intent == PowerShellExecutionIntent.ReadWrite && !Options.AllowWrite) {
            return ToolResultV2.Error(
                errorCode: "disabled",
                error: "Read-write shell execution is disabled by policy.",
                hints: new[] {
                    "Enable PowerShellToolOptions.AllowWrite to allow read_write intent.",
                    "Keep intent=read_only for non-mutating inventory commands."
                },
                isTransient: false);
        }

        if (request.Intent == PowerShellExecutionIntent.ReadWrite && Options.RequireExplicitWriteFlag && !request.AllowWrite) {
            return ToolResultV2.Error(
                errorCode: "invalid_argument",
                error: "Read-write intent requires explicit allow_write=true confirmation.",
                hints: new[] {
                    "Set allow_write=true for approved mutating actions.",
                    "Use intent=read_only for inventory/diagnostic commands."
                },
                isTransient: false);
        }

        var intentText = ToIntentId(request.Intent);
        if (request.HostKind == ShellHostKind.Cmd) {
            var cmdResult = await ExecuteCmdAsync(
                    payload: request.Payload,
                    includeErrorStream: request.IncludeErrorStream,
                    timeoutMs: request.TimeoutMs,
                    maxOutputChars: request.MaxOutputChars,
                    workingDirectory: request.WorkingDirectory,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!cmdResult.Success) {
                return ToolResultV2.Error(
                    errorCode: cmdResult.ErrorCode ?? "query_failed",
                    error: cmdResult.Error ?? "Command prompt execution failed.",
                    hints: cmdResult.Hints,
                    isTransient: string.Equals(cmdResult.ErrorCode, "timeout", StringComparison.Ordinal));
            }

            var cmdModel = cmdResult.Model!;
            var cmdMeta = ToolOutputHints.Meta(count: 1, truncated: cmdModel.OutputTruncated)
                .Add("intent", intentText)
                .Add("allow_write", request.AllowWrite)
                .Add("mutation_heuristic_enabled", Options.EnableMutationHeuristic)
                .Add("mutation_heuristic_matched", mutatingReason is not null);

            if (!string.IsNullOrWhiteSpace(mutatingReason)) {
                cmdMeta.Add("mutation_heuristic_reason", mutatingReason);
            }

            var cmdSummary = ToolMarkdown.SummaryFacts(
                title: "Shell Execution",
                facts: new[] {
                    ("Host", cmdModel.Host),
                    ("ExitCode", cmdModel.ExitCode.ToString()),
                    ("DurationMs", cmdModel.DurationMs.ToString()),
                    ("TimedOut", cmdModel.TimedOut ? "true" : "false"),
                    ("OutputTruncated", cmdModel.OutputTruncated ? "true" : "false"),
                    ("Intent", intentText),
                    ("WriteAllowed", request.AllowWrite ? "true" : "false")
                });

            return ToolResultV2.OkModel(
                model: cmdModel,
                meta: cmdMeta,
                summaryMarkdown: cmdSummary,
                render: ToolOutputHints.RenderCode(language: "text", contentPath: "output"));
        }

        var host = request.HostKind switch {
            ShellHostKind.PowerShell7 => PowerShellHostKind.PowerShell7,
            ShellHostKind.WindowsPowerShell => PowerShellHostKind.WindowsPowerShell,
            _ => PowerShellHostKind.Auto
        };

        var attempt = PowerShellCommandQueryExecutor.TryExecute(
            request: new PowerShellCommandQueryRequest {
                Host = host,
                Command = request.Command,
                Script = request.Script,
                WorkingDirectory = request.WorkingDirectory,
                TimeoutMs = request.TimeoutMs,
                MaxOutputChars = request.MaxOutputChars,
                IncludeErrorStream = request.IncludeErrorStream
            },
            cancellationToken: cancellationToken);
        if (!attempt.Success) {
            return ErrorFromFailure(attempt.Failure);
        }

        var result = attempt.Result!;
        var meta = ToolOutputHints.Meta(count: 1, truncated: result.OutputTruncated)
            .Add("intent", intentText)
            .Add("allow_write", request.AllowWrite)
            .Add("mutation_heuristic_enabled", Options.EnableMutationHeuristic)
            .Add("mutation_heuristic_matched", mutatingReason is not null);

        if (!string.IsNullOrWhiteSpace(mutatingReason)) {
            meta.Add("mutation_heuristic_reason", mutatingReason);
        }

        var summary = ToolMarkdown.SummaryFacts(
            title: "PowerShell Execution",
            facts: new[] {
                ("Host", result.Host),
                ("ExitCode", result.ExitCode.ToString()),
                ("DurationMs", result.DurationMs.ToString()),
                ("TimedOut", result.TimedOut ? "true" : "false"),
                ("OutputTruncated", result.OutputTruncated ? "true" : "false"),
                ("Intent", intentText),
                ("WriteAllowed", request.AllowWrite ? "true" : "false")
            });

        return ToolResultV2.OkModel(
            model: result,
            meta: meta,
            summaryMarkdown: summary,
            render: ToolOutputHints.RenderCode(language: "text", contentPath: "output"));
    }

    private static bool TryParseHost(string? raw, out ShellHostKind host, out string? error) {
        host = ShellHostKind.Auto;
        error = null;

        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "auto", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(raw, "pwsh", StringComparison.OrdinalIgnoreCase)) {
            host = ShellHostKind.PowerShell7;
            return true;
        }

        if (string.Equals(raw, "windows_powershell", StringComparison.OrdinalIgnoreCase)) {
            host = ShellHostKind.WindowsPowerShell;
            return true;
        }

        if (string.Equals(raw, "cmd", StringComparison.OrdinalIgnoreCase)) {
            host = ShellHostKind.Cmd;
            return true;
        }

        error = "host must be one of: auto, pwsh, windows_powershell, cmd.";
        return false;
    }

    private static bool TryParseIntent(string? raw, out PowerShellExecutionIntent intent, out string? error) {
        intent = PowerShellExecutionIntent.ReadOnly;
        error = null;

        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "read_only", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(raw, "read_write", StringComparison.OrdinalIgnoreCase)) {
            intent = PowerShellExecutionIntent.ReadWrite;
            return true;
        }

        error = "intent must be one of: read_only, read_write.";
        return false;
    }

    private static string ToIntentId(PowerShellExecutionIntent intent) {
        return intent == PowerShellExecutionIntent.ReadWrite ? "read_write" : "read_only";
    }

    private static string? DetectMutatingPayloadReason(string payload, ShellHostKind hostKind) {
        if (string.IsNullOrWhiteSpace(payload)) {
            return null;
        }

        var lines = payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].TrimStart();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }

            var lowered = line.ToLowerInvariant();

            var verbPrefixes = hostKind == ShellHostKind.Cmd ? CmdMutatingVerbPrefixes : PowerShellMutatingVerbPrefixes;
            for (var j = 0; j < verbPrefixes.Length; j++) {
                var prefix = verbPrefixes[j];
                if (lowered.StartsWith(prefix, StringComparison.Ordinal)) {
                    return $"line starts with '{prefix}'.";
                }
            }

            var fragments = hostKind == ShellHostKind.Cmd ? CmdMutatingFragments : PowerShellMutatingFragments;
            for (var j = 0; j < fragments.Length; j++) {
                var fragment = fragments[j];
                if (lowered.Contains(fragment, StringComparison.Ordinal)) {
                    return $"line contains '{fragment.Trim()}'.";
                }
            }
        }

        return null;
    }

    private static async Task<CmdExecutionAttempt> ExecuteCmdAsync(string payload, bool includeErrorStream, int timeoutMs, int maxOutputChars, string? workingDirectory, CancellationToken cancellationToken) {
        if (!IsCmdHostAvailable()) {
            return CmdExecutionAttempt.FromFailure(
                errorCode: "host_unavailable",
                error: "cmd host is not available on this machine.",
                hints: new[] { "Use powershell_environment_discover or powershell_hosts to inspect available hosts." });
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory)) {
            return CmdExecutionAttempt.FromFailure(
                errorCode: "invalid_argument",
                error: "working_directory does not exist.",
                hints: new[] { "Provide an existing working_directory path or omit this argument." });
        }

        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), "ix-cmd-" + Guid.NewGuid().ToString("N") + ".cmd");
        try {
            await File.WriteAllTextAsync(scriptPath, payload, Utf8NoBom, cancellationToken).ConfigureAwait(false);

            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = cmdPath,
                    Arguments = "/d /s /c \"\"" + scriptPath + "\"\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? Environment.CurrentDirectory
                        : workingDirectory!
                }
            };

            var stopwatch = Stopwatch.StartNew();
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = includeErrorStream
                ? process.StandardError.ReadToEndAsync()
                : Task.FromResult(string.Empty);

            var timedOut = false;
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                timedOut = true;
                try {
                    if (!process.HasExited) {
                        process.Kill(entireProcessTree: true);
                    }
                } catch {
                    // Ignore best-effort kill failures.
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var combinedOutput = includeErrorStream && !string.IsNullOrEmpty(stderr)
                ? (string.IsNullOrEmpty(stdout) ? stderr : (stdout + Environment.NewLine + stderr))
                : stdout;
            var (output, truncated) = TruncateOutput(combinedOutput ?? string.Empty, maxOutputChars);

            if (timedOut) {
                return CmdExecutionAttempt.FromFailure(
                    errorCode: "timeout",
                    error: "cmd execution timed out.",
                    hints: new[] { "Increase timeout_ms for long-running commands." });
            }

            return CmdExecutionAttempt.FromSuccess(new CmdExecutionModel(
                Host: "cmd",
                ExitCode: process.ExitCode,
                DurationMs: stopwatch.ElapsedMilliseconds,
                TimedOut: false,
                OutputTruncated: truncated,
                Output: output));
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            return CmdExecutionAttempt.FromFailure(
                errorCode: "query_failed",
                error: "cmd execution failed: " + ex.Message,
                hints: new[] { "Check command/script syntax and working_directory." });
        } finally {
            try {
                if (File.Exists(scriptPath)) {
                    File.Delete(scriptPath);
                }
            } catch {
                // Ignore temporary script cleanup failures.
            }
        }
    }

    private static (string Output, bool Truncated) TruncateOutput(string output, int maxChars) {
        if (string.IsNullOrEmpty(output) || output.Length <= maxChars) {
            return (output, false);
        }

        return (output[..maxChars], true);
    }

    private enum ShellHostKind {
        Auto,
        PowerShell7,
        WindowsPowerShell,
        Cmd
    }

    private enum PowerShellExecutionIntent {
        ReadOnly,
        ReadWrite
    }

    private sealed record CmdExecutionModel(
        string Host,
        int ExitCode,
        long DurationMs,
        bool TimedOut,
        bool OutputTruncated,
        string Output);

    private sealed record CmdExecutionAttempt(
        bool Success,
        CmdExecutionModel? Model,
        string? ErrorCode,
        string? Error,
        string[]? Hints) {
        public static CmdExecutionAttempt FromSuccess(CmdExecutionModel model) => new(true, model, null, null, null);

        public static CmdExecutionAttempt FromFailure(string errorCode, string error, string[]? hints = null) => new(false, null, errorCode, error, hints);
    }
}
