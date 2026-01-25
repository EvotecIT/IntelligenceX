using System.Collections.Generic;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class CommandExecRequest {
    public CommandExecRequest(IReadOnlyList<string> command) {
        Command = command;
    }

    public IReadOnlyList<string> Command { get; }
    public string? WorkingDirectory { get; set; }
    public SandboxPolicy? SandboxPolicy { get; set; }
    public int? TimeoutMs { get; set; }
}
