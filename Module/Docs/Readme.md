---
Module Name: IntelligenceX
Module Guid: 8fc3e038-c57b-44f3-bc7f-714beb3bd65a
Download Help Link: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
Help Version: 0.1.0
Locale: en-US
---
# IntelligenceX Module
## Description
IntelligenceX is a PowerShell module for the Codex app-server client.

## IntelligenceX Cmdlets
### [Backup-IntelligenceXThread](Backup-IntelligenceXThread.md)
Archives an existing thread so it no longer appears in active workflows.

Use this cmdlet to hide completed or obsolete conversations from normal thread listings while
preserving history. The archive operation is non-destructive and can be used for housekeeping.

### [Connect-IntelligenceX](Connect-IntelligenceX.md)
Connects to IntelligenceX (native or app-server) and returns a client instance.

Use Native for ChatGPT OAuth login (no local Codex binary). Use AppServer to talk to a locally
running Codex app-server process. The cmdlet sets the active default client, so subsequent cmdlets can omit -Client.

### [Disconnect-IntelligenceX](Disconnect-IntelligenceX.md)
Disconnects the active IntelligenceX client and clears local session context.

Releases app-server/native client resources, clears diagnostics subscriptions, and resets
default thread/initialization state. If no client is active, the cmdlet exits without error.

### [Get-IntelligenceXAccount](Get-IntelligenceXAccount.md)
Returns the current account details.

Returns the account identity for the active session, such as email and account id.
Useful to confirm which credential bundle is currently active.

### [Get-IntelligenceXCollaborationMode](Get-IntelligenceXCollaborationMode.md)
Lists collaboration modes available in the current app-server runtime.

Use this cmdlet to discover valid collaboration mode values before setting them in
configuration or passing them to review/chat workflows.

### [Get-IntelligenceXConfig](Get-IntelligenceXConfig.md)
Reads the effective app-server configuration with layer metadata.

Returns merged config values plus metadata that explains where values come from
(for example defaults, workspace files, or environment overrides).

### [Get-IntelligenceXConfigRequirements](Get-IntelligenceXConfigRequirements.md)
Reads server-defined constraints for supported configuration values.

Returns allowed values for key settings (for example approval policy and sandbox mode)
so scripts can validate config before writing changes.

### [Get-IntelligenceXCopilotInstall](Get-IntelligenceXCopilotInstall.md)
Shows platform-specific installation commands for GitHub Copilot CLI.

This cmdlet does not install anything. It returns suggested install command metadata
so you can preview, log, or execute it manually.

### [Get-IntelligenceXHealth](Get-IntelligenceXHealth.md)
Runs health checks for OpenAI app-server and optional Copilot CLI.

Returns health status for the active IntelligenceX client and, optionally, a Copilot CLI
instance using explicit or config-derived options.

### [Get-IntelligenceXLoadedThread](Get-IntelligenceXLoadedThread.md)
Lists threads currently loaded in the app-server process.

Returns thread ids that are currently active in memory for the running app-server session.
Useful for diagnostics and cleanup scripts.

### [Get-IntelligenceXMcpServerStatus](Get-IntelligenceXMcpServerStatus.md)
Lists configured MCP servers with auth, tool, and resource status.

Queries the app-server MCP registry and returns one page of server status data. The result includes
Servers (current page) and NextCursor (for pagination).

### [Get-IntelligenceXModel](Get-IntelligenceXModel.md)
Lists models available for the current transport context.

Returns model metadata for the active client. Raw JSON mode is available only on app-server
transport because native transport returns strongly typed models directly.

### [Get-IntelligenceXSkill](Get-IntelligenceXSkill.md)
Lists available skills.

Scans for skills in the provided working directories and returns the resolved list.

### [Get-IntelligenceXThread](Get-IntelligenceXThread.md)
Lists available threads.

Returns paged threads with optional filtering by model provider.

### [Get-IntelligenceXTurnOutput](Get-IntelligenceXTurnOutput.md)
Extracts outputs from a turn.

Filters and saves outputs from a completed turn, including images and text.

### [Initialize-IntelligenceX](Initialize-IntelligenceX.md)
Initializes the client handshake with the app-server.

Sends client identity metadata (name, title, version) to app-server. Some flows require
initialization before login, chat, or review operations.

### [Install-IntelligenceXCopilotCli](Install-IntelligenceXCopilotCli.md)
Installs GitHub Copilot CLI using a selected install strategy.

Executes the platform-specific installer command and optionally returns command metadata.
Supports WhatIf/Confirm through ShouldProcess.

### [Invoke-IntelligenceXChat](Invoke-IntelligenceXChat.md)
Super-easy chat command that handles connect, init, login, thread, and send.

Convenience entry point for quick chat: it connects, logs in (if needed), creates or
reuses a thread, sends input, and returns the resulting turn. Supports streaming output and
a simple DSL for text/image inputs.

### [Invoke-IntelligenceXCommand](Invoke-IntelligenceXCommand.md)
Executes a command through the app-server.

Runs a process via the app-server with optional sandbox settings and timeout.

### [Invoke-IntelligenceXMcpServerConfigReload](Invoke-IntelligenceXMcpServerConfigReload.md)
Reloads MCP server configuration in the running app-server.

Refreshes MCP configuration after file changes so newly added servers, tools, and auth settings
are visible without restarting the app-server process.

### [Invoke-IntelligenceXRpc](Invoke-IntelligenceXRpc.md)
Invokes a raw JSON-RPC method directly against app-server.

Low-level escape hatch for advanced scenarios not covered by high-level cmdlets.
Parameters are converted from PowerShell objects/hashtables to JSON payloads.

### [New-IntelligenceXThreadFork](New-IntelligenceXThreadFork.md)
Creates a new thread fork from an existing thread's history.

Use this when you want to branch a conversation without mutating the original thread.
The fork inherits prior context and gets a new thread id.

### [Request-IntelligenceXUserInput](Request-IntelligenceXUserInput.md)
Requests user input through the app-server.

Prompts for one to three questions and returns collected answers in order.
Useful for interactive script checkpoints that need explicit user confirmation or values.

### [Restore-IntelligenceXThread](Restore-IntelligenceXThread.md)
Rolls back the last N turns from a thread.

Removes recent turns from a thread so you can re-run, correct, or branch from an earlier
conversation state.

### [Resume-IntelligenceXThread](Resume-IntelligenceXThread.md)
Resumes an existing thread so new messages can be sent to it.

Loads thread context back into the active app-server session and returns thread metadata.
Useful after reconnecting or when switching between multiple threads.

### [Send-IntelligenceXFeedback](Send-IntelligenceXFeedback.md)
Uploads textual feedback to the app-server feedback endpoint.

Use this cmdlet to send reproduction notes, quality feedback, or analyzer observations
collected during reviews and local runs.

### [Send-IntelligenceXMessage](Send-IntelligenceXMessage.md)
Sends a message to a thread and starts a turn.

Queues a user message on an existing thread. Returns the created turn so you can
poll for output with Get-IntelligenceXTurnOutput.

### [Set-IntelligenceXConfigBatch](Set-IntelligenceXConfigBatch.md)
Writes multiple app-server configuration values in one request.

Sends a batch of key/value updates. This is useful for related settings you want to apply together.
Hashtable values are converted to JSON before sending.

### [Set-IntelligenceXConfigValue](Set-IntelligenceXConfigValue.md)
Writes a single app-server configuration value.

Updates one config key on the app-server. PowerShell values are converted to JSON before sending.
Use Get-IntelligenceXConfig to verify the effective result and layer origin.

### [Set-IntelligenceXSkill](Set-IntelligenceXSkill.md)
Enables or disables a skill entry in app-server skill configuration.

Updates the persisted skill config for a specific skill path so future tool runs include
or exclude that skill.

### [Start-IntelligenceXApiKeyLogin](Start-IntelligenceXApiKeyLogin.md)
Authenticates the active client with an OpenAI API key.

Stores the API key in the active client for API-based requests. Use only if ChatGPT OAuth
is not desired or available.

### [Start-IntelligenceXChatGptLogin](Start-IntelligenceXChatGptLogin.md)
Starts the ChatGPT login flow and returns the authorization URL.

Opens the browser to complete the ChatGPT OAuth flow. Pair with Wait-IntelligenceXLogin
to poll for completion and store the resulting credentials.

### [Start-IntelligenceXMcpOAuthLogin](Start-IntelligenceXMcpOAuthLogin.md)
Starts an MCP OAuth login flow and returns the browser authorization URL.

Use this cmdlet when an MCP server reports OAuth auth status. The response includes a
LoginId and AuthUrl you can open in a browser.

### [Start-IntelligenceXReview](Start-IntelligenceXReview.md)
Starts a review flow for a thread.

Triggers the app-server review pipeline for a thread and target. Use TargetType to
choose what to review (uncommitted changes, a branch, a commit, or custom text).

### [Start-IntelligenceXThread](Start-IntelligenceXThread.md)
Creates a new conversation thread.

Starts a fresh thread on the app-server with a selected model and optional execution
settings like sandbox and approval policy.

### [Stop-IntelligenceXTurn](Stop-IntelligenceXTurn.md)
Interrupts a running turn for a thread.

Requests cancellation for an in-progress turn. Use this for long-running responses,
accidental prompts, or when you need to restart execution with different instructions.

### [Wait-IntelligenceXLogin](Wait-IntelligenceXLogin.md)
Waits for the login flow to complete.

Polls the app-server for login completion. Use after Start-IntelligenceXChatGptLogin.

### [Watch-IntelligenceXEvent](Watch-IntelligenceXEvent.md)
Watches JSON-RPC notifications from the app-server.

Streams notification events until cancelled. Use method filters to observe specific
protocol events such as turn progress, login completion, or status changes.
