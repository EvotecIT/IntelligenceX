using IntelligenceX.AppServer;

namespace IntelligenceX.PowerShell;

internal static class ClientContext {
    public static AppServerClient? DefaultClient { get; set; }
    public static string? DefaultThreadId { get; set; }
    public static bool Initialized { get; set; }
}
