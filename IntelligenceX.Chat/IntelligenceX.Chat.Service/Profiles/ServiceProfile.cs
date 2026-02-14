using System.Collections.Generic;

namespace IntelligenceX.Chat.Service.Profiles;

/// <summary>
/// Serializable session profile (config preset) for the chat service.
/// </summary>
internal sealed class ServiceProfile {
    public string Model { get; set; } = "gpt-5.3-codex";
    public int MaxToolRounds { get; set; } = 24;
    public bool ParallelTools { get; set; } = true;
    public int TurnTimeoutSeconds { get; set; }
    public int ToolTimeoutSeconds { get; set; }
    public List<string> AllowedRoots { get; set; } = new();

    public string? AdDomainController { get; set; }
    public string? AdDefaultSearchBaseDn { get; set; }
    public int AdMaxResults { get; set; } = 1000;
    public bool EnablePowerShellPack { get; set; }
    public bool EnableTestimoXPack { get; set; } = true;
    public bool EnableDefaultPluginPaths { get; set; } = true;
    public List<string> PluginPaths { get; set; } = new();

    public string? InstructionsFile { get; set; }
    public int MaxTableRows { get; set; } = 20;
    public int MaxSample { get; set; } = 10;
    public bool Redact { get; set; }
}
