using System;
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
    private static readonly string[] MutatingVerbPrefixes = {
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

    private static readonly string[] MutatingFragments = {
        "| out-file",
        "| set-content",
        "| add-content",
        "| remove-item",
        "| clear-content",
        " > ",
        " >> "
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "powershell_run",
        "Execute a PowerShell command or script in pwsh/windows_powershell (dangerous; read_only default, read_write requires explicit policy and intent).",
        ToolSchema.Object(
                ("host", ToolSchema.String("PowerShell host: auto, pwsh, windows_powershell. Default auto.").Enum("auto", "pwsh", "windows_powershell")),
                ("intent", ToolSchema.String("Execution intent: read_only (default) or read_write.").Enum("read_only", "read_write")),
                ("allow_write", ToolSchema.Boolean("Explicit write approval flag for read_write intent. Required when policy enforces explicit write confirmation.")),
                ("command", ToolSchema.String("Single PowerShell command text. Provide exactly one of command or script.")),
                ("script", ToolSchema.String("Multi-line PowerShell script text. Provide exactly one of command or script.")),
                ("working_directory", ToolSchema.String("Optional working directory for process execution.")),
                ("timeout_ms", ToolSchema.Integer("Execution timeout in milliseconds.")),
                ("max_output_chars", ToolSchema.Integer("Maximum combined output characters to return.")),
                ("include_error_stream", ToolSchema.Boolean("When true, include stderr in output. Default true.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="PowerShellRunTool"/> class.
    /// </summary>
    public PowerShellRunTool(PowerShellToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Options.Enabled) {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "disabled",
                error: "IX.PowerShell pack is disabled by policy.",
                hints: new[] { "Enable the PowerShell pack explicitly in host/service options before calling powershell_run." },
                isTransient: false));
        }

        var command = ToolArgs.GetOptionalTrimmed(arguments, "command");
        var script = ToolArgs.GetOptionalTrimmed(arguments, "script");

        var hasCommand = !string.IsNullOrWhiteSpace(command);
        var hasScript = !string.IsNullOrWhiteSpace(script);
        if (hasCommand == hasScript) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "Provide exactly one of command or script."));
        }

        if (!TryParseIntent(ToolArgs.GetOptionalTrimmed(arguments, "intent"), out var intent, out var intentError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", intentError ?? "Invalid intent value."));
        }

        var allowWrite = ToolArgs.GetBoolean(arguments, "allow_write", defaultValue: false);
        var payload = hasCommand ? command! : script!;
        var mutatingReason = Options.EnableMutationHeuristic ? DetectMutatingPayloadReason(payload) : null;

        if (intent == PowerShellExecutionIntent.ReadOnly && mutatingReason is not null) {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "Read-only intent rejected a potentially mutating PowerShell payload.",
                hints: new[] {
                    $"Detected mutating pattern: {mutatingReason}",
                    "If this is intentional, set intent=read_write.",
                    "If policy requires it, also set allow_write=true."
                },
                isTransient: false));
        }

        if (intent == PowerShellExecutionIntent.ReadWrite && !Options.AllowWrite) {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "disabled",
                error: "Read-write PowerShell execution is disabled by policy.",
                hints: new[] {
                    "Enable PowerShellToolOptions.AllowWrite to allow read_write intent.",
                    "Keep intent=read_only for non-mutating inventory commands."
                },
                isTransient: false));
        }

        if (intent == PowerShellExecutionIntent.ReadWrite && Options.RequireExplicitWriteFlag && !allowWrite) {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "Read-write intent requires explicit allow_write=true confirmation.",
                hints: new[] {
                    "Set allow_write=true for approved mutating actions.",
                    "Use intent=read_only for inventory/diagnostic commands."
                },
                isTransient: false));
        }

        if (!TryParseHost(ToolArgs.GetOptionalTrimmed(arguments, "host"), out var host, out var hostError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", hostError ?? "Invalid host value."));
        }

        var timeoutMs = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "timeout_ms",
            defaultValue: Options.DefaultTimeoutMs,
            minInclusive: 1,
            maxInclusive: Options.MaxTimeoutMs);
        var maxOutputChars = ToolArgs.GetCappedInt32(
            arguments: arguments,
            key: "max_output_chars",
            defaultValue: Options.DefaultMaxOutputChars,
            minInclusive: 256,
            maxInclusive: Options.MaxOutputChars);

        var includeErrorStream = ToolArgs.GetBoolean(arguments, "include_error_stream", defaultValue: true);
        var workingDirectory = ToolArgs.GetOptionalTrimmed(arguments, "working_directory");

        var attempt = PowerShellCommandQueryExecutor.TryExecute(
            request: new PowerShellCommandQueryRequest {
                Host = host,
                Command = command,
                Script = script,
                WorkingDirectory = workingDirectory,
                TimeoutMs = timeoutMs,
                MaxOutputChars = maxOutputChars,
                IncludeErrorStream = includeErrorStream
            },
            cancellationToken: cancellationToken);
        if (!attempt.Success) {
            return Task.FromResult(ErrorFromFailure(attempt.Failure));
        }

        var result = attempt.Result!;
        var intentText = ToIntentId(intent);

        var meta = ToolOutputHints.Meta(count: 1, truncated: result.OutputTruncated)
            .Add("intent", intentText)
            .Add("allow_write", allowWrite)
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
                ("WriteAllowed", allowWrite ? "true" : "false")
            });

        return Task.FromResult(ToolResponse.OkModel(
            model: result,
            meta: meta,
            summaryMarkdown: summary,
            render: ToolOutputHints.RenderCode(language: "text", contentPath: "output")));
    }

    private static bool TryParseHost(string? raw, out PowerShellHostKind host, out string? error) {
        host = PowerShellHostKind.Auto;
        error = null;

        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "auto", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(raw, "pwsh", StringComparison.OrdinalIgnoreCase)) {
            host = PowerShellHostKind.PowerShell7;
            return true;
        }

        if (string.Equals(raw, "windows_powershell", StringComparison.OrdinalIgnoreCase)) {
            host = PowerShellHostKind.WindowsPowerShell;
            return true;
        }

        error = "host must be one of: auto, pwsh, windows_powershell.";
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

    private static string? DetectMutatingPayloadReason(string payload) {
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

            for (var j = 0; j < MutatingVerbPrefixes.Length; j++) {
                var prefix = MutatingVerbPrefixes[j];
                if (lowered.StartsWith(prefix, StringComparison.Ordinal)) {
                    return $"line starts with '{prefix}'.";
                }
            }

            for (var j = 0; j < MutatingFragments.Length; j++) {
                var fragment = MutatingFragments[j];
                if (lowered.Contains(fragment, StringComparison.Ordinal)) {
                    return $"line contains '{fragment.Trim()}'.";
                }
            }
        }

        return null;
    }

    private enum PowerShellExecutionIntent {
        ReadOnly,
        ReadWrite
    }
}
