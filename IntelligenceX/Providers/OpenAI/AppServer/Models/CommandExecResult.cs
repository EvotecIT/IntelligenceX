namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class CommandExecResult {
    public CommandExecResult(int? exitCode, string? stdout, string? stderr) {
        ExitCode = exitCode;
        Stdout = stdout;
        Stderr = stderr;
    }

    public int? ExitCode { get; }
    public string? Stdout { get; }
    public string? Stderr { get; }
}
