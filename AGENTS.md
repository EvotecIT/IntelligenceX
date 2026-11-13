# IntelligenceX

AI platform built on your own credentials and GitHub App: a GitHub Actions
code reviewer with static-analysis packs, the IX Chat desktop app and tray,
tool packs, a CLI, a .NET library, a PowerShell module and a Swift package.

## Layout

- `IntelligenceX/`: core .NET library (providers, authentication, GitHub,
  analysis, setup, RPC, telemetry).
- `IntelligenceX.Reviewer/`: the GitHub Actions reviewer. `.intelligencex/reviewer.json`
  is this repo's own reviewer config; `Schemas/reviewer.schema.json` is the schema.
- `IntelligenceX.Cli/`: `intelligencex` CLI (setup, auth, `analyze`, `todo`, ops).
- `IntelligenceX.Chat/`: chat host, service, WinUI app and profiles. See
  `IntelligenceX.Chat/AGENTS.md` for its layering and routing rules.
- `IntelligenceX.Tools/`: tool packs. See `IntelligenceX.Tools/AGENTS.md`.
- `IntelligenceX.Engines/`, `IntelligenceX.Storage.SQLite/`, `IntelligenceX.Ledger*/`,
  `IntelligenceX.Tray/`: engines, SQLite storage, ledger and its MCP server, tray app.
- `IntelligenceX.PowerShell/` and `Module/`: binary cmdlets and the module build.
- `Swift/` and `Package.swift`: Swift package (`swift test`).
- `Analysis/Catalog/` and `Analysis/Packs/`: static-analysis rule catalog and packs.
- `Docs/`: public website docs (published to intelligencex.dev/docs).
  `InternalDocs/`: trackers, backlogs, RFCs and maintainer notes.
- `Website/`: PowerForge.Web site. See `Website/AGENTS.md`.
- `Build/`: build and release entry points. `Build/README.md` explains each one.

## Build and test

```powershell
dotnet build IntelligenceX.CI.slnf -c Release        # what CI builds (cross-platform)
dotnet test IntelligenceX.CI.slnf -c Release --no-build
dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll    # custom harness, CI runs it
dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll
dotnet build IntelligenceX.sln -c Release            # everything, including Chat, Tools and Tray (Windows)
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .
./Build/Build-Project.ps1 -Plan $true                # NuGet packages (project.build.json)
pwsh ./Build/Test-Packages.ps1 -PackageDirectory <dir>   # validate built .nupkg files
pwsh ./Build/Build-Product.ps1 -Plan                 # app, tray, installer and tool release graph
./Build/Run-Project.ps1 -ListTargets                 # run Chat.Host, Chat.App, Chat.Service, Tray or Cli
./Module/Build/Build-Module.ps1                      # PowerShell module
```

- `IntelligenceX.Tests` is an executable harness, not an xUnit project. xUnit
  suites: `IntelligenceX.UnitTests`, `IntelligenceX.Ledger.Tests`,
  `IntelligenceX.Chat/*.Tests`, `IntelligenceX.Tools/*.Tests`.
- Targets: core library `netstandard2.0`, `net472`, `net8.0`, `net10.0`
  (`net472`/`netstandard2.0` on Windows only). CLI, Reviewer and most tool packs
  `net8.0`/`net10.0` (Windows packs use `-windows`). Chat, Tray and Ledger
  `net10.0` / `net10.0-windows`.
- Warnings are errors (`Directory.Build.props`).
- Publishing (`-PublishNuget`, `-PublishGitHub`, `-Publish*` on the product
  scripts) and installer signing are release-only.

## Generated files

- `Analysis/Catalog/rules/csharp` and C# tier packs: `python3 scripts/update_analysis_catalog.py --repo-root .`.
  PowerShell rules: `scripts/sync-pssa-catalog.ps1`. Add IntelligenceX metadata
  under `Analysis/Catalog/overrides`, not in generated rule files.
- `Module/Docs/*.md` and module help: produced by `Module/Build/Build-Module.ps1`.
- `WebsiteArtifacts/project-manifest.json`: CI checks it is current
  (`project-manifest.yml`).
- `TODO.md` bot-feedback items: `intelligencex todo sync-bot-feedback`.

## Conventions

- `Docs/` is public website content. Internal trackers, backlogs, migration
  notes, rollout logs and playbooks go in `InternalDocs/`. Don't add drafts to
  `Docs/toc.json`. When moving pages out of `Docs/`, update `Docs/toc.json`,
  `Docs/_index.md` and cross-links in the same change.
- `TODO.md`: only explicit checklist items, grouped by PR, collapsed, no nested
  bullets, each linked to its source comment. Mark done only when verified in code.
- Chat routing and safety must be language-neutral:
  - No hardcoded natural-language keyword lists or literal follow-up phrases
    ("go ahead", "run it"). Use shape, turn state and tool activity.
  - Keep question and follow-up detection Unicode-aware (`?`, `？`, `¿`, `؟`)
    and don't require English-like token lengths.
  - Prefer structured protocol fields (`ix_action_selection.mutating`,
    `ix:action:v1` `mutating: true|false`, `ix:proactive-mode:v1`) over lexical
    inference. Text matching for provider interop stays a structured-first
    fallback, isolated from routing and confirmation.
  - Shared heuristic thresholds are named constants in shared helpers.
  - Long turns emit phase/status updates and heartbeats instead of adding
    blocking confirmation gates.
  - Any change to routing, pending-action confirmation or execution contracts
    adds tests proving language-agnostic behavior.
- Chat routing tests call `internal` helpers through `InternalsVisibleTo`, not
  reflection on private methods. Streaming/status tests capture synchronized
  snapshots instead of reading streams that are still being written.

## Gotchas

- Tool packs for ADPlayground, ComputerX and TestimoX reference a sibling
  TestimoX checkout (`TestimoXRoot` / `TESTIMOX_ROOT`, see `Directory.Build.props`).
  Without it those references are skipped.
- The reviewer skips PRs that only change workflow files. Don't change
  `.github/workflows/deploy-website.yml` unless asked.
- Live provider, GitHub and reviewer runs need real credentials; don't run them
  unless the task asks for it.

## Repository skills

In `.agents/skills/` (index in `.agents/skills/README.md`):

- `intelligencex-analysis-gate`: analysis behavior, static-analysis policy,
  catalog validation, analyzer execution flow.
- `intelligencex-first-pr-rollout`: validate the first live reviewer run after
  onboarding.
- `intelligencex-onboarding-setup`: setup wizard, setup web, onboarding UX and docs.
- `intelligencex-pr-unblock-loop`: iterate on CI failures and bot todo/critical
  items until a PR is mergeable.
- `intelligencex-reviewer-bootstrap`: reviewer setup, managed workflow blocks,
  `reviewer.json` generation and apply.
- `intelligencex-tools-authoring`: create or refactor tools with helper reuse
  and stable contracts.
- `intelligencex-write-tool-authoring`: mutating tools with shared
  write-governance and authentication contracts.
