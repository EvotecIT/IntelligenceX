using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the result of an app-server command execution.
/// </summary>
/// <example>
/// <code>
/// var result = await client.ExecuteCommandAsync(request);
/// Console.WriteLine(result.ExitCode);
/// Console.WriteLine(result.Stdout);
/// </code>
/// </example>
public sealed class CommandExecResult {
    public CommandExecResult(int? exitCode, string? stdout, string? stderr, JsonObject raw, JsonObject? additional) {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Process exit code (if available).</summary>
    public int? ExitCode { get; }
    /// <summary>Captured standard output.</summary>
    public string? Stdout { get; }
    /// <summary>Captured standard error.</summary>
    public string? Stderr { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a command result from JSON.</summary>
    public static CommandExecResult FromJson(JsonObject obj) {
        var exitCodeValue = obj.GetInt64("exitCode") ?? obj.GetInt64("exit_code");
        var exitCode = exitCodeValue.HasValue ? (int?)exitCodeValue.Value : null;
        var stdout = obj.GetString("stdout");
        var stderr = obj.GetString("stderr");
        var additional = obj.ExtractAdditional("exitCode", "exit_code", "stdout", "stderr");
        return new CommandExecResult(exitCode, stdout, stderr, obj, additional);
    }
}
