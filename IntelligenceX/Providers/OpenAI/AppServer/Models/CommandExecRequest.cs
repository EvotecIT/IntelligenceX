using System.Collections.Generic;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Describes a command execution request for the app-server.
/// </summary>
/// <example>
/// <code>
/// var request = new CommandExecRequest(new[] { "dotnet", "--info" }) {
///     WorkingDirectory = ".",
///     TimeoutMs = 30000
/// };
/// </code>
/// </example>
public sealed class CommandExecRequest {
    public CommandExecRequest(IReadOnlyList<string> command) {
        Command = command;
    }

    /// <summary>Command and arguments to execute.</summary>
    public IReadOnlyList<string> Command { get; }
    /// <summary>Optional working directory.</summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>Optional sandbox policy.</summary>
    public SandboxPolicy? SandboxPolicy { get; set; }
    /// <summary>Timeout in milliseconds.</summary>
    public int? TimeoutMs { get; set; }
}
