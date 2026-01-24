namespace IntelligenceX.AppServer;

public sealed class ClientInfo {
    public ClientInfo(string name, string title, string version) {
        Name = name;
        Title = title;
        Version = version;
    }

    public string Name { get; }
    public string Title { get; }
    public string Version { get; }
}
