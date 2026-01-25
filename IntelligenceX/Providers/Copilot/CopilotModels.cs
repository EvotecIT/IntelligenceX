using System.Collections.Generic;

namespace IntelligenceX.Copilot;

public sealed class CopilotModelInfo {
    public CopilotModelInfo(string id, string? name) {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string? Name { get; }
    public Dictionary<string, object?> Metadata { get; } = new();
}
