# IntelligenceX

IntelligenceX is a lightweight .NET client for the Codex app-server protocol. It manages the app-server
process, speaks JSON-RPC over JSONL, and exposes simple methods for authentication and conversations.

- No external NuGet dependencies
- Cross-platform (.NET 8/.NET 10) + Windows (.NET Framework 4.7.2)
- PowerShell module included

## Requirements

- Codex CLI installed and available on PATH ("codex")
- .NET SDK 8+ for building examples and tests

## Quick start (.NET)

```csharp
using IntelligenceX.AppServer;

var client = await AppServerClient.StartAsync(new AppServerOptions {
    ExecutablePath = "codex",
    Arguments = "app-server"
});

await client.InitializeAsync(new ClientInfo("IntelligenceX", "IntelligenceX Demo", "0.1.0"));
var login = await client.StartChatGptLoginAsync();
Console.WriteLine($"Login URL: {login.AuthUrl}");
await client.WaitForLoginCompletionAsync(login.LoginId);

var thread = await client.StartThreadAsync("gpt-5.1-codex");
await client.StartTurnAsync(thread.Id, "Hello from IntelligenceX");
```

## PowerShell module

Build the module:

```powershell
./Module/Build/Build-Module.ps1
```

Import and use:

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force
$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX' -Title 'Demo' -Version '0.1.0'
$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host $login.AuthUrl
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
$thread = Start-IntelligenceXThread -Client $client -Model 'gpt-5.1-codex'
Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text 'Hello from PowerShell'
Disconnect-IntelligenceX -Client $client
```

## Examples

See `IntelligenceX.Examples` for a simple console demo, and `Module/Examples` for PowerShell scripts.

## Notes

- This library targets the Codex app-server JSON-RPC protocol.
- For custom app-server arguments set `CODEX_APP_SERVER_ARGS`.
- For custom app-server path set `CODEX_APP_SERVER_PATH`.
