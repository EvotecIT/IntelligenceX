namespace IntelligenceX.OpenAI.AppServer;

/// <summary>
/// Client metadata sent during app-server initialization.
/// </summary>
public sealed class ClientInfo {
    /// <summary>
    /// Creates client metadata.
    /// </summary>
    public ClientInfo(string name, string title, string version) {
        Name = name;
        Title = title;
        Version = version;
    }

    /// <summary>
    /// Short client name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Human-friendly client title.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// Client version.
    /// </summary>
    public string Version { get; }
}
