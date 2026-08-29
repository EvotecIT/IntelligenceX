# IntelligenceX

`IntelligenceX` is the reusable .NET SDK behind IntelligenceX applications and integrations. It includes:

- Codex app-server and provider-neutral AI clients
- GitHub notification and repository monitoring APIs
- AI usage, model, cost, and provider-limit telemetry
- tool identity, domain presentation, selection metadata, and reporting models

The package does not install the IntelligenceX desktop applications and does not bring in SQLite or Windows UI dependencies.
It provides assets for .NET Standard 2.0, .NET Framework 4.7.2, .NET 8, and .NET 10.

```powershell
dotnet add package IntelligenceX
```

## GitHub notification example

```csharp
using IntelligenceX.Telemetry.GitHub;

using var inbox = new GitHubNotificationService(githubToken);
GitHubNotificationSnapshot snapshot = await inbox.FetchAsync(
    new GitHubNotificationQuery { Limit = 20 });

foreach (GitHubNotificationThread thread in snapshot.Threads) {
    Console.WriteLine($"{thread.RepositoryNameWithOwner}: {thread.Title}");
}
```

For persistent SQLite-backed telemetry and GitHub monitoring stores on .NET 8 or .NET 10, add [`IntelligenceX.Storage.SQLite`](https://www.nuget.org/packages/IntelligenceX.Storage.SQLite).

Documentation and source are available in the [IntelligenceX repository](https://github.com/EvotecIT/IntelligenceX).
