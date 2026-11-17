# Setting up the Ledger agent service on a machine

The agent service runs as its own process next to each machine's Ledger reporter: the sign-in task
`EvotecIT-IX-Ledger-agent` on Windows and the LaunchAgent `com.evotec.intelligencex-ledger.agent` on macOS. It reads the
reporter's configuration for the Ledger address and credentials and keeps its own settings in `agent.json`. Setting a
machine up is three steps; each command previews by default and needs `--apply` (or `-Apply`) to change anything, and
each can be repeated safely.

## 1. Enroll the machine (on the machine that hosts the Ledger service)

```powershell
dotnet "$env:LOCALAPPDATA\IntelligenceX\Ledger\app\IntelligenceX.Ledger.dll" agent-enroll `
    --service "$env:LOCALAPPDATA\IntelligenceX\Ledger\service.json" --machine mac-studio --out <private folder> --apply
```

This adds a host principal for the machine's agent service and a client principal for its sessions' ask tools, both
scoped to that machine, unless they exist, and writes `host-client.json` and `sessions-client.json`. Use `--local` for
the machine that hosts the service itself. The machine must already be in the coordination policy. A new principal is
loaded when the service restarts, which the next runtime update does.

Copy both files privately to the machine's `coordination` folder next to its reporter configuration
(`%LOCALAPPDATA%\IntelligenceX\Ledger\coordination` or `~/.local/share/intelligencex-ledger/coordination`), mode 600 on
macOS. They hold tokens: never put them in Git, issues or shared folders.

## 2. Build the kit (on the same operating system as the target)

```powershell
./Build/Publish-LedgerAgent.ps1 -Runtime win-x64 -Output <scratch>\ledger-kit      # Windows
./Build/Publish-LedgerAgent.ps1 -Runtime osx-arm64 -Output <scratch>/ledger-kit    # macOS
```

The kit holds the reporter and service in the layout the platform installers expect (`service/` or `app/` with
`manifest.json`), `tools/` with the MCP server and the Claude inbox hook, and `Install-LedgerAgent.ps1` with its
`agent-manifest.json`. The hook is Native AOT, so build it on the target's operating system; `-NoHook` leaves it out for
a machine without Claude Code.

## 3. Install the agent service

```powershell
<kit>/Install-LedgerAgent.ps1 -Action Install -Kit <kit> -Capability windows-powershell           # preview
<kit>/Install-LedgerAgent.ps1 -Action Install -Kit <kit> -Capability windows-powershell -Apply    # install or update
<kit>/Install-LedgerAgent.ps1 -Action Rollback -Apply                                              # previous version
<kit>/Install-LedgerAgent.ps1                                                                      # status
```

It checks every file against the kit's manifest, copies the program and tools to `agent-service/versions/<commit>`
under the reporter's folder, writes `agent.json`, registers the `ix_ask` MCP server with Codex and Claude (`codex mcp
add`, `claude mcp add`) and the inbox hook for Claude's SessionStart and UserPromptSubmit, then restarts the agent on the
new version. `agent-service/install.json` records the current and previous version, and older versions are removed.
It finds `codex` and `claude` on PATH, `~/.local/bin`, `/opt/homebrew/bin` or `/usr/local/bin`; pass `-Codex` or
`-Claude` to choose, and `-WorkspaceRoot` for the folder new Codex helper threads start in. The capabilities and CLI
paths are kept in `agent.json`, so an update needs only `-Kit`.

The Ledger service and reporter installs, their snapshots and scheduled tasks are left alone. Older reporters ran the
agent themselves from an `agent` section in the reporter configuration: the first install moves those settings to
`agent.json` (keeping a `.before-agent-install` copy) and restarts the reporter before the new process starts. Update
the reporter with its platform installer (`Update-LedgerRuntime.ps1` or `Install-MacLedgerReporter.ps1`) only after
that, because the current reporter rejects a configuration that still has the section.

## Check

- `IntelligenceX.Ledger agent-check --config <agent.json>` shows whether Codex answers (live daemon or private
  server) and which Claude CLI is used, without touching the Ledger.
- The machine is fresh in `coordination_status`, and its `<machine>:sign-in:<provider>` items are `done`.
- An `ask` from another machine to this one comes back with a result.
- The agent's own log is `agent-service/agent.log`, with one previous copy kept as `agent.log.1`. A line is written when
  something changes, not on every sweep.
