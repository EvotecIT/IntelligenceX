using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the result of a command execution.
/// </summary>
public sealed class CommandExecResult {
    /// <summary>
    /// Initializes a new command execution result.
    /// </summary>
    public CommandExecResult(int? exitCode, string? stdout, string? stderr, JsonObject raw, JsonObject? additional) {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the exit code when available.
    /// </summary>
    public int? ExitCode { get; }
    /// <summary>
    /// Gets standard output.
    /// </summary>
    public string? Stdout { get; }
    /// <summary>
    /// Gets standard error.
    /// </summary>
    public string? Stderr { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a command execution result from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed result.</returns>
    public static CommandExecResult FromJson(JsonObject obj) {
        var exitCodeValue = obj.GetInt64("exitCode") ?? obj.GetInt64("exit_code");
        var exitCode = exitCodeValue.HasValue ? (int?)exitCodeValue.Value : null;
        var stdout = obj.GetString("stdout");
        var stderr = obj.GetString("stderr");
        var additional = obj.ExtractAdditional("exitCode", "exit_code", "stdout", "stderr");
        return new CommandExecResult(exitCode, stdout, stderr, obj, additional);
    }
}
