# IntelligenceX

IntelligenceX is a lightweight .NET client for the Codex app-server protocol. It manages the app-server
process, speaks JSON-RPC over JSONL, and exposes simple methods for authentication and conversations.

Status: Active development | APIs in flux | Actions in beta

## Project Information

[![top language](https://img.shields.io/github/languages/top/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![license](https://img.shields.io/github/license/EvotecIT/IntelligenceX.svg)](https://github.com/EvotecIT/IntelligenceX)
[![build](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml/badge.svg)](https://github.com/EvotecIT/IntelligenceX/actions/workflows/test-dotnet.yml)

## Author & Social

[![Twitter follow](https://img.shields.io/twitter/follow/PrzemyslawKlys.svg?label=Twitter%20%40PrzemyslawKlys&style=social)](https://twitter.com/PrzemyslawKlys)
[![Blog](https://img.shields.io/badge/Blog-evotec.xyz-2A6496.svg)](https://evotec.xyz/hub)
[![LinkedIn](https://img.shields.io/badge/LinkedIn-pklys-0077B5.svg?logo=LinkedIn)](https://www.linkedin.com/in/pklys)
[![Threads](https://img.shields.io/badge/Threads-@PrzemyslawKlys-000000.svg?logo=Threads&logoColor=White)](https://www.threads.net/@przemyslaw.klys)
[![Discord](https://img.shields.io/discord/508328927853281280?style=flat-square&label=discord%20chat)](https://evo.yt/discord)

- No external NuGet dependencies
- Cross-platform (.NET 8/.NET 10) + Windows (.NET Framework 4.7.2)
- PowerShell module included (binary cmdlets, net472/net8)

## Providers

- `IntelligenceX/Providers/OpenAI` — Codex app-server (ChatGPT) client, auth, and easy/fluent APIs.
- `IntelligenceX/Providers/Copilot` — GitHub Copilot CLI client (JSON-RPC).

## Requirements

- Codex CLI installed and available on PATH ("codex")
- .NET SDK 8+ for building examples and tests

Full build check (includes legacy TFMs on any OS):

```powershell
pwsh ./Build/Build-All.ps1 -Configuration Release
```

## Quick start (.NET)

```csharp
using IntelligenceX.OpenAI.AppServer;

var client = await AppServerClient.StartAsync(new AppServerOptions {
    ExecutablePath = "codex",
    Arguments = "app-server"
});

await client.InitializeAsync(new ClientInfo("IntelligenceX", "IntelligenceX Demo", "0.1.0"));
var login = await client.StartChatGptLoginAsync();
Console.WriteLine($"Login URL: {login.AuthUrl}");
await client.WaitForLoginCompletionAsync(login.LoginId);

var thread = await client.StartThreadAsync("gpt-5.2-codex");
await client.StartTurnAsync(thread.Id, "Hello from IntelligenceX");
```

## Super easy (.NET)

EasySession (auto init + login + thread):

```csharp
using IntelligenceX.OpenAI;

var result = await Easy.ChatAsync("Hello!");
Console.WriteLine(result.Text);
```

```csharp
using IntelligenceX.OpenAI;

await using var session = await EasySession.StartAsync();
await session.ChatAsync("Hello!");
```

Reasoning/verbosity (C#):

```csharp
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

await using var session = await EasySession.StartAsync();
var options = new ChatOptions {
    ReasoningEffort = ReasoningEffort.Medium,
    ReasoningSummary = ReasoningSummary.Auto,
    TextVerbosity = TextVerbosity.Low,
    Instructions = "Be concise."
};
await session.ChatAsync(ChatInput.FromText("Explain DNS"), options);
```

## Config overrides (.intelligencex/config.json)

You can override defaults without code changes by adding `.intelligencex/config.json`
or setting `INTELLIGENCEX_CONFIG_PATH`.

Example `.intelligencex/config.json`:

```json
{
  "openai": {
    "defaultModel": "gpt-5.2-codex",
    "instructions": "You are a helpful assistant.",
    "reasoningEffort": "medium",
    "reasoningSummary": "auto",
    "textVerbosity": "medium",
    "appServerPath": "codex",
    "appServerArgs": "app-server",
    "openBrowser": true,
    "printLoginUrl": true,
    "loginMode": "chatgpt",
    "maxImageBytes": 10485760,
    "requireWorkspaceForFileAccess": true
  },
  "copilot": {
    "cliPath": "copilot",
    "autoInstall": false
  }
}
```

Supported values:
- `reasoningEffort`: `minimal|low|medium|high|xhigh`
- `reasoningSummary`: `auto|concise|detailed|off`
- `textVerbosity`: `low|medium|high`

```csharp
using IntelligenceX.OpenAI;

var options = EasySessionOptions.FromConfig();
await using var session = await EasySession.StartAsync(options);
await session.ChatAsync("Hello with config overrides.");
```

## Telemetry hooks (.NET)

```csharp
using IntelligenceX.OpenAI;

await using var client = await IntelligenceXClient.ConnectAsync();
client.RpcCallStarted += (_, args) => Console.WriteLine($"RPC -> {args.Method}");
client.RpcCallCompleted += (_, args) => Console.WriteLine($"RPC <- {args.Method} ({args.Duration.TotalMilliseconds:0} ms)");
client.LoginStarted += (_, args) => Console.WriteLine($"Login started: {args.LoginType}");
client.LoginCompleted += (_, args) => Console.WriteLine($"Login completed: {args.LoginType}");
client.StandardErrorReceived += (_, line) => Console.WriteLine($"STDERR: {line}");
```

### Reviewer configuration (GitHub Action / CLI)

If you run `IntelligenceX.Reviewer`, you can configure it using environment variables **or**
a repo-local file at `.intelligencex/reviewer.json`.

Example `.intelligencex/reviewer.json`:

```json
{
  "review": {
    "provider": "openai",
    "profile": "picky",
    "length": "long",
    "focus": ["bugs", "security", "tests"],
    "progressUpdates": true,
    "progressUpdateSeconds": 30,
    "commentMode": "sticky"
  },
  "copilot": {
    "cliPath": "copilot",
    "autoInstall": false
  }
}
```

## Diagnostics and health (PowerShell)

Enable diagnostic output on connect:

```powershell
Connect-IntelligenceX -Diagnostics
```

Run health checks:

```powershell
# OpenAI app-server (uses current client)
Get-IntelligenceXHealth

# Copilot CLI (optional)
Get-IntelligenceXHealth -Copilot
```

## Troubleshooting JSON-RPC errors

Common JSON-RPC codes and hints:

- `-32700` Parse error (malformed JSON)
- `-32600` Invalid request
- `-32601` Method not found
- `-32602` Invalid params
- `-32603` Internal error
- `-32000` Server error

```csharp
using IntelligenceX.OpenAI;

await using var ix = await IntelligenceXClient.ConnectAsync();
await ix.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));
await ix.ChatAsync("Hello!");
```

Images and files (.NET):

```csharp
using IntelligenceX.OpenAI;

await using var ix = await IntelligenceXClient.ConnectAsync();
await ix.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));

await ix.ChatWithImagePathAsync("Describe this image", "C:\\Images\\cat.png");
```

Allow file writes (workspace):

```csharp
await using var ix = await IntelligenceXClient.ConnectAsync();
await ix.ChatAsync("Create a report.txt file with a summary of today.",
    new IntelligenceX.OpenAI.Chat.ChatOptions { Workspace = "C:\\Work" });
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
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Fluent;

var session = await AppServerFluent.StartAsync(new AppServerOptions());
await session.InitializeAsync(new ClientInfo("IntelligenceX", "Fluent Demo", "0.1.0"));
var login = await session.LoginChatGptAsync();
Console.WriteLine(login.Login.AuthUrl);
await login.WaitAsync();

var thread = await session.StartThreadAsync("gpt-5.2-codex");
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
$thread = Start-IntelligenceXThread -Client $client -Model 'gpt-5.2-codex'
Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text 'Hello from PowerShell'
Disconnect-IntelligenceX -Client $client
```

Quick diagnostics + health:

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force
$client = Connect-IntelligenceX -Diagnostics
Get-IntelligenceXHealth
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

Reasoning/verbosity (PowerShell):

```powershell
Invoke-IntelligenceXChat "Explain TCP" -ReasoningEffort Medium -ReasoningSummary Auto -TextVerbosity Low
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
- Health/diagnostics: Get-IntelligenceXHealth

## Examples

Console examples live in `IntelligenceX.Examples` and are split into separate files. Run:

```bash
dotnet run --project IntelligenceX.Examples/IntelligenceX.Examples.csproj -- list
```

PowerShell scripts live in `Module/Examples`:
- `Example.Login.ps1`
- `Example.Chat.ps1`
- `Example.Health.ps1`

## Reviewer (GitHub Actions)

`IntelligenceX.Reviewer` is a console tool that reads the GitHub PR event payload, asks Codex for a review,
and posts a sticky comment.

Required env:
- `GITHUB_EVENT_PATH`
- `INTELLIGENCEX_GITHUB_TOKEN` (preferred, for GitHub App identity) or `GITHUB_TOKEN`

Optional env/inputs (SocraticLens-style):
- `mode` (`summary|inline|hybrid`) - inline is not enabled yet (summary only)
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

The runner must have a valid ChatGPT login cache at `~/.intelligencex/auth.json`. You can create it with `IntelligenceX.Cli auth login`. If you also need Codex CLI/app-server compatibility, run `IntelligenceX.Cli auth sync-codex` to write `~/.codex/auth.json`.

### Bring Your Own GitHub App (BYOA)

If you want the reviewer to post as a dedicated bot (instead of `github-actions`), use a GitHub App token.
Create an app (personal or org) and add the app credentials as secrets.

Minimal app settings:
- Permissions: `Pull requests: Read & write`, `Issues: Write`, `Contents: Read`
- Subscribe to events: none
- Webhook: disabled

Create the app and install it on the target repo, then add secrets:
- `INTELLIGENCEX_GITHUB_APP_ID` (the App ID)
- `INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY` (the PEM contents)

The reusable workflow will mint `INTELLIGENCEX_GITHUB_TOKEN` automatically from those secrets.

## CLI

`IntelligenceX.Cli` provides a native OAuth login flow (similar to Clawdbot) without requiring Codex CLI.
It also exposes reviewer entry points for automation.

Commands:
- `intelligencex auth login`
- `intelligencex auth export`
- `intelligencex auth sync-codex`
- `intelligencex reviewer run`
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

Native ChatGPT overrides (optional):
- `INTELLIGENCEX_INSTRUCTIONS`
- `INTELLIGENCEX_REASONING_EFFORT` (`minimal|low|medium|high|xhigh`)
- `INTELLIGENCEX_REASONING_SUMMARY` (`auto|concise|detailed|off`)
- `INTELLIGENCEX_TEXT_VERBOSITY` (`low|medium|high`)
- `INTELLIGENCEX_CLIENT_VERSION`

Login:

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- auth login
```

Export (base64 for GitHub Secrets):

```bash
INTELLIGENCEX_AUTH_EXPORT_FORMAT=base64 \
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- auth export
```

Write Codex auth.json (for app-server/CLI reuse):

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- auth sync-codex
```

## Copilot CLI (GitHub)

IntelligenceX includes a minimal Copilot CLI client (no extra dependencies). It talks JSON-RPC over the Copilot CLI server mode.

Requirements:
- `copilot` CLI installed and authenticated (run `copilot` once to log in).
  Optionally set `COPILOT_CLI_PATH` if the CLI is not on PATH.
  If you want auto-install from apps, set `CopilotClientOptions.AutoInstallCli = true`.

Install or update Copilot CLI:

Windows (winget):
```powershell
winget install GitHub.Copilot
```

macOS/Linux (Homebrew):
```bash
brew install copilot-cli
```

All platforms (npm, Node.js 22+):
```bash
npm install -g @github/copilot
```

macOS/Linux (install script):
```bash
curl -fsSL https://gh.io/copilot-install | bash
```

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




