using IntelligenceX.OpenAI;

namespace IntelligenceX.PowerShell;

internal static class ClientContext {
    public static IntelligenceXClient? DefaultClient { get; set; }
    public static string? DefaultThreadId { get; set; }
    public static bool Initialized { get; set; }
    public static DiagnosticsSubscription? Diagnostics { get; set; }
}
