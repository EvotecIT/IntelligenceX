namespace IntelligenceX.OpenAI.AppServer;

/// <summary>
/// Identifies a client when initializing the app-server.
/// </summary>
public sealed class ClientInfo {
    /// <summary>
    /// Initializes a new client info instance.
    /// </summary>
    /// <param name="name">Client name.</param>
    /// <param name="title">Client title.</param>
    /// <param name="version">Client version.</param>
    public ClientInfo(string name, string title, string version) {
        Name = name;
        Title = title;
        Version = version;
    }

    /// <summary>
    /// Gets the client name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the client title.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// Gets the client version.
    /// </summary>
    public string Version { get; }
}
