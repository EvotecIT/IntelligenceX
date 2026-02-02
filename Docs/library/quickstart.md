# .NET Library Quickstart

## Native ChatGPT (OAuth)

```csharp
using IntelligenceX.OpenAI;

await using var client = await IntelligenceXClient.ConnectAsync(new IntelligenceXClientOptions {
    TransportKind = OpenAITransportKind.Native
});

await client.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));
var turn = await client.ChatAsync("Summarize this PR.");
Console.WriteLine(turn.Outputs.Count);
```

## App-server (Codex)

```csharp
using IntelligenceX.OpenAI.AppServer;

await using var client = await AppServerClient.StartAsync(new AppServerOptions {
    ExecutablePath = "codex",
    Arguments = "app-server"
});

await client.InitializeAsync(new ClientInfo("IntelligenceX", "Demo", "0.1.0"));
var login = await client.StartChatGptLoginAsync();
Console.WriteLine(login.AuthUrl);
await client.WaitForLoginCompletionAsync(login.LoginId);

var thread = await client.StartThreadAsync("gpt-5.2-codex");
await client.StartTurnAsync(thread.Id, "Hello from IntelligenceX");
```

## Usage & credits

```csharp
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;

using var usage = new ChatGptUsageService(new OpenAINativeOptions());
var report = await usage.GetReportAsync(includeEvents: true);
Console.WriteLine(report.Snapshot.RateLimit?.PrimaryWindow?.UsedPercent);
Console.WriteLine(report.Snapshot.Credits?.Balance);
```
