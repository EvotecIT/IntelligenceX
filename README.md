# IntelligenceX

IntelligenceX is a .NET toolkit for the Codex app-server protocol and a GitHub Actions reviewer.
It manages the app-server process, speaks JSON-RPC over JSONL, and ships a CLI/web wizard to onboard
review automation quickly and safely.

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

- Core library has no external NuGet dependencies (CLI uses Sodium.Core for GitHub secrets)
- Cross-platform (.NET 8/.NET 10) + Windows (.NET Framework 4.7.2)
- PowerShell module included (binary cmdlets, net472/net8)

## Project structure

- `IntelligenceX` — core .NET library (Codex app-server + Copilot client)
- `IntelligenceX.Cli` — CLI (`intelligencex`) for auth, setup, and reviewer
- `IntelligenceX.Reviewer` — GitHub Actions reviewer runner
- `IntelligenceX.PowerShell` — PowerShell module (binary cmdlets)

## Get started (Reviewer)

Recommended onboarding:

```powershell
intelligencex setup wizard
```

Local web UI (preview):

```powershell
intelligencex setup web
```

Docs:
- `Docs/onboarding-wizard.md`
- `Docs/setup-web.md`
- `Docs/security-trust.md`

Trust model (short version):
- BYO GitHub App is supported for branded bot identity.
- Secrets are stored in GitHub Actions (you control access).
- Web UI binds to localhost only; tokens never leave your machine.

## Library (.NET)

### Providers

- `IntelligenceX/Providers/OpenAI` — Codex app-server (ChatGPT) client, auth, and easy/fluent APIs.
- `IntelligenceX/Providers/Copilot` — GitHub Copilot CLI client (JSON-RPC).

### Requirements

- Codex CLI installed and available on PATH ("codex")
- .NET SDK 8+ for building examples and tests

Full build check (includes legacy TFMs on any OS):

```powershell
pwsh ./Build/Build-All.ps1 -Configuration Release
```

### Quick start (.NET)

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

### Super easy (.NET)

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

### Config overrides (.intelligencex/config.json)

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

### Telemetry hooks (.NET)

```csharp
using IntelligenceX.OpenAI;

await using var client = await IntelligenceXClient.ConnectAsync();
client.RpcCallStarted += (_, args) => Console.WriteLine($"RPC -> {args.Method}");
client.RpcCallCompleted += (_, args) => Console.WriteLine($"RPC <- {args.Method} ({args.Duration.TotalMilliseconds:0} ms)");
client.LoginStarted += (_, args) => Console.WriteLine($"Login started: {args.LoginType}");
client.LoginCompleted += (_, args) => Console.WriteLine($"Login completed: {args.LoginType}");
client.StandardErrorReceived += (_, line) => Console.WriteLine($"STDERR: {line}");
```

## Reviewer (GitHub Actions)

`IntelligenceX.Reviewer` is the console tool behind the review workflow. It reads PR context, generates
review feedback, and posts a sticky comment.

### Onboarding (recommended)

Use the interactive wizard for the fastest setup across one or more repositories.

CLI wizard:

```powershell
intelligencex setup wizard
```

Web UI (preview):

```powershell
intelligencex setup web
```

Docs:
- `Docs/onboarding-wizard.md`
- `Docs/cli-quickstart.md`
- `Docs/security-trust.md`
- `Docs/setup-web.md`

Wizard operations:
- Setup / update workflow + config (default)
- Update OpenAI secret only
- Cleanup (remove workflow/config)

### Quick start

Use the reusable workflow from `evotecit/github-actions`:

```yaml
jobs:
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    with:
      reviewer_source: release
      openai_transport: native
      output_style: claude
      style: colorful
    secrets: inherit
```

### GitHub App identity (optional)

To post reviews from a branded bot identity, create a GitHub App and store:
- `INTELLIGENCEX_GITHUB_APP_ID`
- `INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY`

When present, the workflow creates an App token and the reviewer uses it. Otherwise it falls back to `GITHUB_TOKEN`.

### ChatGPT auth (native transport)

Set `INTELLIGENCEX_AUTH_B64` to the auth **store** file (not a single bundle). Generate it with:

```powershell
$env:INTELLIGENCEX_AUTH_EXPORT_FORMAT="store-base64"
intelligencex auth export --format store-base64
```

### Reviewer configuration file

You can configure the reviewer with environment variables **or** a repo-local file at `.intelligencex/reviewer.json`.

```json
{
  "review": {
    "provider": "openai",
    "profile": "picky",
    "style": "direct",
    "outputStyle": "claude",
    "reasoningEffort": "high",
    "reasoningSummary": "auto",
    "length": "long",
    "focus": ["bugs", "security", "tests"],
    "maxInlineComments": 10,
    "progressUpdates": true,
    "progressUpdateSeconds": 30,
    "retryCount": 3,
    "retryDelaySeconds": 5,
    "retryMaxDelaySeconds": 30,
    "retryExtraResponseEnded": true,
    "diagnostics": false,
    "preflight": false,
    "preflightTimeoutSeconds": 15,
    "failOpen": true,
    "commentSearchLimit": 500,
    "includeReviewThreads": false,
    "reviewThreadsIncludeBots": false,
    "reviewThreadsIncludeResolved": false,
    "reviewThreadsIncludeOutdated": true,
    "reviewThreadsMax": 10,
    "reviewThreadsMaxComments": 3,
    "reviewThreadsAutoResolveStale": false,
    "reviewThreadsAutoResolveMissingInline": false,
    "reviewThreadsAutoResolveBotsOnly": false,
    "reviewThreadsAutoResolveMax": 10,
    "reviewThreadsAutoResolveAI": true,
    "reviewThreadsAutoResolveAIPostComment": true,
    "commentMode": "sticky",
    "overwriteSummaryOnNewCommit": true,
    "contextDenyEnabled": true,
    "contextDenyPatterns": [
      "\\bpoem\\b",
      "\\blife advice\\b"
    ]
  },
  "cleanup": {
    "enabled": true,
    "mode": "hybrid",
    "requireLabel": "ix-cleanup",
    "minConfidence": 0.85,
    "allowedEdits": ["formatting", "grammar", "title", "sections"],
    "template": "## Summary\n- \n\n## Changes\n- \n\n## Notes\n- "
  },
  "copilot": {
    "cliPath": "copilot",
    "autoInstall": false
  }
}
```

Schema: `Schemas/reviewer.schema.json`

Notes:
- Set `maxInlineComments` to `0` to disable inline review comments.
- `reasoningEffort`/`reasoningSummary` map to Codex reasoning controls.
- `overwriteSummaryOnNewCommit` forces updating sticky summaries when the PR head SHA changes (prevents stale reviews).
- Context deny patterns are regex with a short timeout; invalid patterns are ignored with a warning.
- `diagnostics` enables extra transport logging (stderr + RPC failures) to debug OpenAI connectivity.
- `preflight` runs a health check before the review request (useful for early auth/transport failures).
- `retryExtraResponseEnded` adds one extra retry when the HTTP response ends prematurely.
- `failOpen` posts a failure summary comment instead of failing the workflow.
- `includeReviewThreads` adds an "Other Reviews" section that triages existing review threads.
- `reviewThreadsAutoResolveStale` can auto-resolve stale threads (requires `pull-requests: write`).
- `reviewThreadsAutoResolveMissingInline` resolves inline threads created by the bot when they no longer appear in the latest review (requires `pull-requests: write`).
- `reviewThreadsAutoResolveAI` uses the model to assess open threads against the current diff before resolving.
- `reviewThreadsAutoResolveAIPostComment` posts a triage summary comment when AI keeps threads open.
- Set `reviewThreadsMax` or `reviewThreadsMaxComments` to `0` to disable review-thread context.
- When review-thread context is included, the reviewer suppresses the separate "Review comments" block to avoid duplicate content.

## CLI setup (GitHub Actions)

Use the CLI to add or update the review workflow and secrets (best for scripted or headless flows).
For interactive onboarding, prefer `intelligencex setup wizard` or `intelligencex setup web`.

```powershell
# Interactive setup (uses GitHub device flow)
intelligencex setup --repo EvotecIT/IntelligenceX

# Skip secret creation (manual secret paste)
intelligencex setup --repo EvotecIT/IntelligenceX --skip-secret

# Update only the OpenAI auth secret
intelligencex setup --repo EvotecIT/IntelligenceX --update-secret

# Remove workflow/config (optional: keep secret)
intelligencex setup --repo EvotecIT/IntelligenceX --cleanup --keep-secret
```

Common options:
- `--actions-repo` / `--actions-ref` to point at the reusable workflow
- `--reviewer-source` (`release|source`) + `--reviewer-release-*`
- `--progress-updates <true|false>` to toggle progress comment updates

The CLI uses the GitHub secrets API to store `INTELLIGENCEX_AUTH_B64` (requires Sodium.Core).

### Inputs / env

Required:
- `GITHUB_TOKEN` (or `INTELLIGENCEX_GITHUB_TOKEN`)
- For manual runs: `repo` + `pr_number` (or `GITHUB_EVENT_PATH` on PR events)

Common inputs/env:
- `provider`, `model`, `openai_transport` (`native|appserver`)
- `reasoning_effort`, `reasoning_summary`
- `profile`, `style`, `output_style`, `tone`, `persona`, `notes`
- `mode`, `length`, `max_files`, `max_patch_chars`, `max_inline_comments`
- `comment_mode`, `overwrite_summary`, `overwrite_summary_on_new_commit`
- `skip_titles`, `skip_labels`, `skip_paths`, `skip_draft`
- `retry_count`, `retry_delay_seconds`, `retry_max_delay_seconds`
- `diagnostics`, `preflight`, `preflight_timeout_seconds`
- `context_deny_enabled`, `context_deny_patterns`
- `include_review_threads`, `review_threads_include_bots`, `review_threads_include_resolved`, `review_threads_include_outdated`
- `review_threads_max`, `review_threads_max_comments`
- `review_threads_auto_resolve_stale`, `review_threads_auto_resolve_bots_only`, `review_threads_auto_resolve_max`
- `redact_pii`, `redaction_patterns`, `redaction_replacement`
- `prompt_template` / `prompt_template_path`
- `summary_template` / `summary_template_path`

Template tokens:
- Prompt: `{{ProfileBlock}}`, `{{StrictnessBlock}}`, `{{StyleBlock}}`, `{{OutputStyleBlock}}`, `{{ToneBlock}}`, `{{FocusBlock}}`,
  `{{PersonaBlock}}`, `{{NotesBlock}}`, `{{SeverityBlock}}`, `{{Length}}`, `{{Mode}}`, `{{MaxInlineComments}}`, `{{InlineSupported}}`,
  `{{NextStepsSection}}`, `{{Title}}`, `{{Body}}`, `{{Files}}`
- Summary: `{{SummaryMarker}}`, `{{Number}}`, `{{Title}}`, `{{CommitLine}}`, `{{InlineNote}}`, `{{ReviewBody}}`, `{{Model}}`, `{{Length}}`

Codex app-server settings (optional):
- `CODEX_APP_SERVER_PATH`
- `CODEX_APP_SERVER_ARGS`
- `CODEX_APP_SERVER_CWD`

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

### Troubleshooting JSON-RPC errors

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

### Fluent quick start (.NET)

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

### GitHub App details

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

### GitHub Auth Paths

There are three supported ways to authenticate GitHub for reviews:

1) **Standard GitHub Actions (default)**  
   Use `GITHUB_TOKEN`. Works everywhere but posts as `github-actions[bot]`.

2) **BYOA (recommended)**  
   Each org/user creates their own GitHub App and stores its secrets in their repo/org.  
   This keeps trust local and enables a custom bot identity.

3) **Shared App (requires a service)**  
   A single shared GitHub App only works across many repos if you run a service that mints
   short-lived app tokens, because the private key cannot be distributed to users safely.

## CLI

`IntelligenceX.Cli` provides a native OAuth login flow (similar to Clawdbot) without requiring Codex CLI.
It also exposes reviewer entry points for automation.

Commands:
- `intelligencex auth login`
- `intelligencex auth export`
- `intelligencex auth sync-codex`
- `intelligencex reviewer run`
- `intelligencex reviewer resolve-threads`
- `intelligencex release notes`
Legacy aliases are supported: `login`, `export`, `sync-codex`.
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
- `INTELLIGENCEX_AUTH_EXPORT_FORMAT=json|base64|store|store-base64` (for export)
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

Login + export + set GitHub secret (repo or org):

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- auth login --set-github-secret --repo owner/name --github-token $TOKEN
```

Auto-detect repo/org + token:

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- auth login --set-github-secret
```

Auto-detect sources:
- Repo: `INTELLIGENCEX_GITHUB_REPO` → `GITHUB_REPOSITORY` → git `origin` remote
- Org: `INTELLIGENCEX_GITHUB_ORG` → `GITHUB_ORG` → `GITHUB_OWNER`
- Token: `INTELLIGENCEX_GITHUB_TOKEN` → `GITHUB_TOKEN` → `GH_TOKEN` → `gh auth token`

Export (store-base64 for GitHub Secrets):

```bash
INTELLIGENCEX_AUTH_EXPORT_FORMAT=store-base64 \
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- auth export
```

Write Codex auth.json (for app-server/CLI reuse):

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- auth sync-codex
```

Generate release notes between tags (and update CHANGELOG.md):

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- release notes --from v1.2.3 --to v1.2.4 --version v1.2.4 --update-changelog
```

One-liner (default range, update changelog):

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- release notes --update-changelog
```

Auto-resolve IntelligenceX bot threads after fixes:

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- reviewer resolve-threads --repo owner/name --pr 123 --dry-run
```

### Release notes automation (direct to default branch)

Template workflow is available at `IntelligenceX.Cli/Templates/release-notes.yml`.
It runs on tag push (any tag) and supports manual runs with `from`/`to`/`version` inputs.
It updates `CHANGELOG.md` on the default branch and keeps the workflow YAML minimal by passing inputs through env vars.

Optional PR mode:
- Set `create_pr: 'true'` to open/update a PR instead of pushing directly.
- Optional inputs: `pr_branch`, `pr_title`, `pr_body`, `pr_labels`, `skip_review`, `repo_slug`.
- When `skip_review: 'true'` (default), the workflow prefixes `[skip-review]` to the PR title
  and applies the `skip-review` label (so IntelligenceX can skip its own release-notes PRs).
Requires workflow permissions: `contents: write` + `pull-requests: write`.

Required secret:
- `INTELLIGENCEX_AUTH_B64` (Auth store base64 from `intelligencex auth export --format store-base64`)
  The workflow will fail with a clear message if this secret is missing.

Optional overrides:
- `OPENAI_MODEL`
- `OPENAI_TRANSPORT`

Advanced environment overrides (optional):
- `INTELLIGENCEX_RELEASE_FROM`, `INTELLIGENCEX_RELEASE_TO`, `INTELLIGENCEX_RELEASE_VERSION`
- `INTELLIGENCEX_RELEASE_CREATE_PR`, `INTELLIGENCEX_RELEASE_COMMIT`, `INTELLIGENCEX_RELEASE_SKIP_REVIEW`
- `INTELLIGENCEX_RELEASE_PR_BRANCH`, `INTELLIGENCEX_RELEASE_PR_TITLE`, `INTELLIGENCEX_RELEASE_PR_BODY`, `INTELLIGENCEX_RELEASE_PR_LABELS`
- `INTELLIGENCEX_RELEASE_REPO_SLUG`

### Release reviewer automation

Workflow: `.github/workflows/release-reviewer.yml`
This builds the reviewer for linux/win/osx, zips assets, and publishes to GitHub Releases.
Inputs:
- `release_tag` (optional, defaults to timestamp)
- `release_title` (optional)
- `release_notes` (optional)
- `release_repo` (owner/name, default `EvotecIT/github-actions`)
- `rids` (comma-separated, optional)
- `framework` (optional)
- `configuration` (optional)

Environment overrides (optional):
- `INTELLIGENCEX_REVIEWER_TAG`, `INTELLIGENCEX_REVIEWER_TITLE`, `INTELLIGENCEX_REVIEWER_NOTES`
- `INTELLIGENCEX_REVIEWER_REPO_SLUG`, `INTELLIGENCEX_REVIEWER_RIDS`
- `INTELLIGENCEX_REVIEWER_FRAMEWORK`, `INTELLIGENCEX_REVIEWER_CONFIGURATION`
- `INTELLIGENCEX_REVIEWER_TOKEN` (fallback: `INTELLIGENCEX_RELEASE_TOKEN`, `GITHUB_TOKEN`)

One-liner (CLI):

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- release reviewer
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




