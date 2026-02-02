using System.Collections.Generic;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a command execution request for the app-server.
/// </summary>
public sealed class CommandExecRequest {
    /// <summary>
    /// Initializes a new command execution request.
    /// </summary>
    /// <param name="command">Command and arguments.</param>
    public CommandExecRequest(IReadOnlyList<string> command) {
        Command = command;
    }

    /// <summary>
    /// Gets the command and arguments.
    /// </summary>
    public IReadOnlyList<string> Command { get; }
    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Gets or sets the sandbox policy.
    /// </summary>
    public SandboxPolicy? SandboxPolicy { get; set; }
    /// <summary>
    /// Gets or sets the timeout in milliseconds.
    /// </summary>
    public int? TimeoutMs { get; set; }
}
