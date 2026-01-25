using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class CommandExecResult {
    public CommandExecResult(int? exitCode, string? stdout, string? stderr, JsonObject raw, JsonObject? additional) {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
        Raw = raw;
        Additional = additional;
    }

    public int? ExitCode { get; }
    public string? Stdout { get; }
    public string? Stderr { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static CommandExecResult FromJson(JsonObject obj) {
        var exitCodeValue = obj.GetInt64("exitCode") ?? obj.GetInt64("exit_code");
        var exitCode = exitCodeValue.HasValue ? (int?)exitCodeValue.Value : null;
        var stdout = obj.GetString("stdout");
        var stderr = obj.GetString("stderr");
        var additional = obj.ExtractAdditional("exitCode", "exit_code", "stdout", "stderr");
        return new CommandExecResult(exitCode, stdout, stderr, obj, additional);
    }
}
