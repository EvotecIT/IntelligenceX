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

## Super easy (.NET)

EasySession (auto init + login + thread):

```csharp
using IntelligenceX;

await using var session = await EasySession.StartAsync();
await session.ChatAsync("Hello!");
```

```csharp
using IntelligenceX;

await using var ix = await IntelligenceXClient.ConnectAsync();
await ix.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));
await ix.ChatAsync("Hello!");
```

Images and files (.NET):

```csharp
using IntelligenceX;

await using var ix = await IntelligenceXClient.ConnectAsync();
await ix.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));

await ix.ChatWithImagePathAsync("Describe this image", "C:\\Images\\cat.png");
```

Allow file writes (workspace):

```csharp
await using var ix = await IntelligenceXClient.ConnectAsync();
await ix.ChatAsync("Create a report.txt file with a summary of today.",
    new IntelligenceX.Chat.ChatOptions { Workspace = "C:\\Work" });
```

Reading image outputs:

```csharp
var turn = await ix.ChatAsync("Generate a small icon.");
foreach (var image in turn.ImageOutputs) {
    Console.WriteLine(image.ImageUrl ?? image.ImagePath ?? image.Base64);
}
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

Super easy PowerShell:

```powershell
Invoke-IntelligenceXChat "Hello from PowerShell"
```

PowerShell DSL (pipeline):

```powershell
"text:Summarize this image", "image:C:\Images\cat.png" | Invoke-IntelligenceXChat
```

Images and workspace (PowerShell):

```powershell
Invoke-IntelligenceXChat "Describe this image" -ImagePath "C:\\Images\\cat.png"
Invoke-IntelligenceXChat "Write a summary to report.txt" -Workspace "C:\\Work"
```

Reading outputs (PowerShell):

```powershell
$turn = Invoke-IntelligenceXChat "Generate an icon"
$turn | Get-IntelligenceXTurnOutput -Images
```

Save image outputs (PowerShell):

```powershell
$turn = Invoke-IntelligenceXChat "Generate an icon"
$turn | Get-IntelligenceXTurnOutput -SaveImagesTo "C:\\Images" -DownloadUrls
```

Save image outputs in one line:

```powershell
Invoke-IntelligenceXChat "Generate an icon" -SaveImagesTo "C:\\Images" -DownloadImageUrls
```

Default file names include a UTC timestamp (and model when known). Use `-ImageFileNamePrefix` or `-FileNamePrefix` to override.

Cmdlet overview (binary):
- Connection/auth: Connect-IntelligenceX, Initialize-IntelligenceX, Start-IntelligenceXChatGptLogin, Start-IntelligenceXApiKeyLogin
- Threads/turns: Get-IntelligenceXThread, Get-IntelligenceXLoadedThread, Resume-IntelligenceXThread, New-IntelligenceXThreadFork, Backup-IntelligenceXThread, Restore-IntelligenceXThread, Start-IntelligenceXThread, Send-IntelligenceXMessage, Stop-IntelligenceXTurn
- Tools/config: Invoke-IntelligenceXCommand, Get-IntelligenceXConfig, Set-IntelligenceXConfigValue, Set-IntelligenceXConfigBatch, Get-IntelligenceXConfigRequirements
- Skills/models: Get-IntelligenceXSkill, Set-IntelligenceXSkill, Get-IntelligenceXModel, Get-IntelligenceXCollaborationMode
- MCP/user: Start-IntelligenceXMcpOAuthLogin, Get-IntelligenceXMcpServerStatus, Invoke-IntelligenceXMcpServerConfigReload, Request-IntelligenceXUserInput
- Events/other: Watch-IntelligenceXEvent, Invoke-IntelligenceXRpc, Send-IntelligenceXFeedback, Get-IntelligenceXTurnOutput

## Examples

Console examples live in `IntelligenceX.Examples` and are split into separate files. Run:

```bash
dotnet run --project IntelligenceX.Examples/IntelligenceX.Examples.csproj -- list
```

PowerShell scripts live in `Module/Examples`.

## Reviewer (GitHub Actions)

`IntelligenceX.Reviewer` is a console tool that reads the GitHub PR event payload, asks Codex for a review,
and posts a sticky comment.

Required env:
- `GITHUB_EVENT_PATH`
- `GITHUB_TOKEN`

Optional env/inputs (SocraticLens-style):
- `mode` (`summary|inline|hybrid`) — inline is not enabled yet (summary only)
- `length` (`short|medium|long`)
- `persona`, `notes`
- `max_files`, `max_patch_chars`, `max_inline_comments`
- `severity_threshold` (`low|medium|high|critical`)
- `skip_titles`, `skip_labels`, `skip_paths`, `skip_draft`
- `redact_pii`, `redaction_patterns`, `redaction_replacement`
- `overwrite_summary` (default `true`)
- `prompt_template` / `prompt_template_path` (override prompt template)
- `summary_template` / `summary_template_path` (override PR comment template)

Template tokens:
- Prompt: `{{PersonaBlock}}`, `{{NotesBlock}}`, `{{SeverityBlock}}`, `{{Length}}`, `{{Mode}}`, `{{MaxInlineComments}}`,
  `{{NextStepsSection}}`, `{{Title}}`, `{{Body}}`, `{{Files}}`
- Summary: `{{SummaryMarker}}`, `{{Number}}`, `{{Title}}`, `{{InlineNote}}`, `{{ReviewBody}}`, `{{Model}}`, `{{Length}}`

Codex app-server settings (optional):
- `CODEX_APP_SERVER_PATH`
- `CODEX_APP_SERVER_ARGS`
- `CODEX_APP_SERVER_CWD`

The runner must have a valid ChatGPT login cache (`~/.codex/auth.json`). You can create it with `IntelligenceX.AuthTool sync-codex`.

## Auth Tool (OAuth)

`IntelligenceX.AuthTool` provides a native OAuth login flow (similar to Clawdbot) without requiring Codex CLI.
Defaults are built in; environment variables only override them.

Defaults:
- authorize URL: `https://auth.openai.com/oauth/authorize`
- token URL: `https://auth.openai.com/oauth/token`
- client id: `app_EMoamEEZ73f0CkXaXp7hrann`
- scopes: `openid profile email offline_access`
- redirect: `http://127.0.0.1:1455/auth/callback`

Optional overrides:
- `OPENAI_AUTH_AUTHORIZE_URL`
- `OPENAI_AUTH_TOKEN_URL`
- `OPENAI_AUTH_CLIENT_ID`
- `OPENAI_AUTH_SCOPES`
- `OPENAI_AUTH_REDIRECT_URL`
- `INTELLIGENCEX_AUTH_PATH` (default: `~/.intelligencex/auth.json`)
- `INTELLIGENCEX_AUTH_KEY` (base64 32 bytes, enables encryption; .NET 8+ only)
- `INTELLIGENCEX_AUTH_EXPORT_FORMAT=base64` (for export)
- `CODEX_HOME` (used by `sync-codex`)

Login:

```bash
dotnet run --project IntelligenceX.AuthTool/IntelligenceX.AuthTool.csproj -- login
```

Export (base64 for GitHub Secrets):

```bash
INTELLIGENCEX_AUTH_EXPORT_FORMAT=base64 \
dotnet run --project IntelligenceX.AuthTool/IntelligenceX.AuthTool.csproj -- export
```

Write Codex auth.json (for app-server/CLI reuse):

```bash
dotnet run --project IntelligenceX.AuthTool/IntelligenceX.AuthTool.csproj -- sync-codex
```

## Copilot CLI (GitHub)

IntelligenceX includes a minimal Copilot CLI client (no extra dependencies). It talks JSON-RPC over the Copilot CLI server mode.

Requirements:
- `copilot` CLI installed and authenticated (run `copilot` once to log in).

Example:

```csharp
using IntelligenceX.Copilot;

await using var client = await CopilotClient.StartAsync();
var auth = await client.GetAuthStatusAsync();
if (!auth.IsAuthenticated) {
    Console.WriteLine("Copilot CLI not authenticated.");
    return;
}
var session = await client.CreateSessionAsync(new CopilotSessionOptions { Model = "gpt-5" });
var response = await session.SendAndWaitAsync(new CopilotMessageOptions { Prompt = "Hello from Copilot." });
Console.WriteLine(response);
```

## Notes

- This library targets the Codex app-server JSON-RPC protocol.
- For custom app-server arguments set `CODEX_APP_SERVER_ARGS`.
- For custom app-server path set `CODEX_APP_SERVER_PATH`.
