# Setting up the Ledger agent service on a machine

The agent service runs inside each machine's Ledger reporter. Setting a machine up is four steps; each command previews
by default and needs `--apply` (or `-Apply`) to change anything, and each can be repeated safely.

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

The kit holds the reporter and service in the layout the platform installer expects (`service/` or `app/` with
`manifest.json`), and `tools/` with the MCP server and the Claude inbox hook. The hook is Native AOT, so build it on the
target's operating system; `-NoHook` leaves it out for a machine without Claude Code.

## 3. Install the reporter with the platform installer

- Windows: `Update-LedgerRuntime.ps1 -CandidateRoot <kit> ... -ApplicationOnly`, preview first, then `-ReviewedPlanPath`
  and `-Apply`.
- macOS: `Install-MacLedgerReporter.ps1 -Action Update -KitRoot <kit> -ConfigPath <reporter.json>`, preview first, then
  `-ReviewedPlanPath` and `-Apply`.

## 4. Set up the agent

```powershell
dotnet <app>/IntelligenceX.Ledger.dll agent-install --config <reporter.json> --tools <kit> --capability macos-shell --apply
```

It writes the reporter's `agent` section, installs the ask tools in `tools/<commit>`, registers the `ix_ask` MCP server
with Codex and Claude through `codex mcp add` and `claude mcp add`, registers the inbox hook for Claude's SessionStart
and UserPromptSubmit, and removes older tool folders that nothing has open. It finds `codex` and `claude` on PATH,
`~/.local/bin`, `/opt/homebrew/bin` or `/usr/local/bin`; pass `--codex` or `--claude` to choose. Restart the reporter if
the agent section changed (the output says how). Sessions started afterwards see the `ask` tools.

## Check

- `IntelligenceX.Ledger agent-check --config <reporter.json>` shows whether Codex answers (live daemon or private
  server) and which Claude CLI is used, without touching the Ledger.
- The machine is fresh in `coordination_status`, and its `<machine>:sign-in:<provider>` items are `done`.
- An `ask` from another machine to this one comes back with a result.
