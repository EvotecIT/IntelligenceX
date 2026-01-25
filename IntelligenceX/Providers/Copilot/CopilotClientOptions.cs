using System.Collections.Generic;

namespace IntelligenceX.Copilot;

public sealed class CopilotClientOptions {
    public string? CliPath { get; set; } = "copilot";
    public List<string> CliArgs { get; } = new();
    public string? CliUrl { get; set; }
    public bool UseStdio { get; set; } = true;
    public int Port { get; set; } = 0;
    public string LogLevel { get; set; } = "info";
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; } = new();
    public bool AutoStart { get; set; } = true;
    public bool AutoInstallCli { get; set; }
    public CopilotCliInstallMethod AutoInstallMethod { get; set; } = CopilotCliInstallMethod.Auto;
    public bool AutoInstallPrerelease { get; set; }
}
