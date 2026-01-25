using System.Collections.Generic;

namespace IntelligenceX.Copilot;

public sealed class CopilotMessageOptions {
    public string Prompt { get; set; } = string.Empty;
    public List<CopilotAttachment>? Attachments { get; set; }
    public string? Mode { get; set; }
}

public sealed class CopilotAttachment {
    public string Type { get; set; } = "file";
    public string Path { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}
