# IntelligenceX

IntelligenceX is a lightweight .NET client for the Codex app-server protocol. It manages the app-server
process, speaks JSON-RPC over JSONL, and exposes simple methods for authentication and conversations.

- No external NuGet dependencies
- Cross-platform (.NET 8/.NET 10) + Windows (.NET Framework 4.7.2)
- PowerShell module included (binary cmdlets, net472/net8)

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

## Fluent quick start (.NET)

```csharp
using IntelligenceX.AppServer;
using IntelligenceX.Fluent;

var session = await AppServerFluent.StartAsync(new AppServerOptions());
await session.InitializeAsync(new ClientInfo("IntelligenceX", "Fluent Demo", "0.1.0"));
var login = await session.LoginChatGptAsync();
Console.WriteLine(login.Login.AuthUrl);
await login.WaitAsync();

var thread = await session.StartThreadAsync("gpt-5.1-codex");
await thread.SendAsync("Hello from fluent API");
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

More cmdlets:

```powershell
# List threads
Get-IntelligenceXThread -Client $client -Limit 20

# Watch app-server notifications
Watch-IntelligenceXEvent -Client $client

# Invoke any RPC method
Invoke-IntelligenceXRpc -Client $client -Method 'thread/list' -Params @{ limit = 5 }
```

Cmdlet overview (binary):
- Connection/auth: Connect-IntelligenceX, Initialize-IntelligenceX, Start-IntelligenceXChatGptLogin, Start-IntelligenceXApiKeyLogin
- Threads/turns: Get-IntelligenceXThread, Get-IntelligenceXLoadedThread, Resume-IntelligenceXThread, New-IntelligenceXThreadFork, Backup-IntelligenceXThread, Restore-IntelligenceXThread, Start-IntelligenceXThread, Send-IntelligenceXMessage, Stop-IntelligenceXTurn
- Tools/config: Invoke-IntelligenceXCommand, Get-IntelligenceXConfig, Set-IntelligenceXConfigValue, Set-IntelligenceXConfigBatch, Get-IntelligenceXConfigRequirements
- Skills/models: Get-IntelligenceXSkill, Set-IntelligenceXSkill, Get-IntelligenceXModel, Get-IntelligenceXCollaborationMode
- MCP/user: Start-IntelligenceXMcpOAuthLogin, Get-IntelligenceXMcpServerStatus, Invoke-IntelligenceXMcpServerConfigReload, Request-IntelligenceXUserInput
- Events/other: Watch-IntelligenceXEvent, Invoke-IntelligenceXRpc, Send-IntelligenceXFeedback

## Examples

Console examples live in `IntelligenceX.Examples` and are split into separate files. Run:

```bash
dotnet run --project IntelligenceX.Examples/IntelligenceX.Examples.csproj -- list
```

PowerShell scripts live in `Module/Examples`.

## Notes

- This library targets the Codex app-server JSON-RPC protocol.
- For custom app-server arguments set `CODEX_APP_SERVER_ARGS`.
- For custom app-server path set `CODEX_APP_SERVER_PATH`.
