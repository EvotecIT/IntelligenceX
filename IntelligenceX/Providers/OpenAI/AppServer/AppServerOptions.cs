using System.Collections.Generic;

namespace IntelligenceX.OpenAI.AppServer;

public sealed class AppServerOptions {
    public string ExecutablePath { get; set; } = "codex";
    public string Arguments { get; set; } = "app-server";
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; } = new();
    public bool RedirectStandardError { get; set; } = true;
}
