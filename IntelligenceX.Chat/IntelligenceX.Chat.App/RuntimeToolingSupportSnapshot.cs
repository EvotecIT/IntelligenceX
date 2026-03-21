using System.Collections.Generic;

namespace IntelligenceX.Chat.App;

internal sealed class RuntimeToolingSupportSnapshot {
    public string Source { get; set; } = string.Empty;
    public int PackCount { get; set; }
    public int PluginCount { get; set; }
    public List<RuntimeToolingPackSnapshot> Packs { get; set; } = new();
    public List<RuntimeToolingPluginSnapshot> Plugins { get; set; } = new();
}

internal sealed class RuntimeToolingPackSnapshot {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? DisabledReason { get; set; }
    public bool IsDangerous { get; set; }
    public string SourceKind { get; set; } = "open_source";
    public string? Category { get; set; }
    public string? EngineId { get; set; }
    public List<string> CapabilityTags { get; set; } = new();
}

internal sealed class RuntimeToolingPluginSnapshot {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool DefaultEnabled { get; set; }
    public string? DisabledReason { get; set; }
    public bool IsDangerous { get; set; }
    public string? Origin { get; set; }
    public string SourceKind { get; set; } = "open_source";
    public string? Version { get; set; }
    public string? RootPath { get; set; }
    public List<string> PackIds { get; set; } = new();
    public List<string> SkillIds { get; set; } = new();
}
