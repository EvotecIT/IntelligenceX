# CodexBar for Windows: Observability Roadmap

This document summarizes where IntelligenceX stands today, what the target product should become, what was added in the current review branch, and how the stack compares with CodexBar.

The working product goal is straightforward:

- build a Windows tray experience
- monitor Codex, Claude, Copilot, LM Studio, GitHub, and IX-owned activity
- keep the local-first feel of CodexBar
- add richer GitHub intelligence such as stars, forks, watchers, useful forks, and daily trend tracking

## Product Thesis

We do not need to start from zero.

The repo already has two major building blocks:

- a provider-neutral telemetry and report pipeline
- an existing Windows tray app foundation in `IntelligenceX.Chat`

That means the shortest path is not "rebuild CodexBar from scratch for Windows". The shorter path is:

1. keep expanding the telemetry foundation into a durable local observability backend
2. surface it through the existing Windows tray shell
3. add GitHub trend collection and product-grade UI around it

## What We Have Today

### Telemetry foundation

Current strengths in the telemetry stack:

- provider-neutral ledger for usage events
- source-root discovery across local profiles, recovered folders, and WSL
- import and quick-report flows for Codex, Claude, Copilot CLI, LM Studio, GitHub overlays, and IX-owned telemetry
- HTML and JSON overview exports with provider sections, heatmaps, and summary cards
- durable SQLite-backed import path plus quick-scan path
- account, person, machine, and source-root grouping

Relevant base document:

- [usage-telemetry-foundation.md](C:/Support/GitHub/IntelligenceX-codex-review/InternalDocs/usage-telemetry-foundation.md)

### Existing Windows shell

The repo already documents and ships a Windows tray application direction through `IntelligenceX.Chat`.

That matters because the tray shell problem is mostly solved:

- WinUI 3 desktop shell
- tray integration
- local host/service runtime model
- local-first process model

Relevant public references:

- [windows-tray-chat.md](C:/Support/GitHub/IntelligenceX-codex-review/Docs/apps/windows-tray-chat.md)

### Existing GitHub reporting surface

The current stack already has GitHub-oriented overview building blocks:

- owner-impact sections
- top repositories
- top repositories by forks
- repository health style rankings
- language summaries
- recent repository views

This is useful, but it is still closer to "current summary" than "time-series monitoring".

## What This Branch Added

This review branch materially improved correctness and explainability.

### Correctness improvements

- Copilot quick-report coverage was added to the lightweight scanner.
- `report --full-import --path ...` now correctly forwards ad hoc roots.
- Codex duplicate session copies are collapsed across:
  - `sessions`
  - `archived_sessions`
  - recovered roots
  - cached quick-scan reruns

### Explainability improvements

- quick-report subtitle now explains root scope
- provider sections now show `Scanned roots`
- exported `overview.json` now includes `metadata.scanContext`
- quick scans now export provider-level duplicate diagnostics
- provider sections now show `Quick-scan dedupe` when duplicate records were collapsed

### Isolation improvements

We added `--paths-only` so targeted debugging is finally reliable.

Supported flows now include:

- `telemetry usage report --path <root> --paths-only`
- `telemetry usage overview --path <root> --paths-only`
- `telemetry usage import --path <root> --paths-only`
- `telemetry usage report --full-import --path <root> --paths-only`

That gives us a clean answer to questions like:

- "Did this total come only from `Windows.old`?"
- "Did WSL get included?"
- "Did my live `CODEX_HOME` pollute this import?"

## What We Should Have

To become a real "CodexBar for Windows", we should aim for five layers.

### 1. Correct local observability

Must-have behaviors:

- never double count copied session logs
- always include archived history when it is genuinely unique
- support exact isolated scans/imports
- preserve enough provenance to explain every total

### 2. Always-on local collection

The current telemetry stack is good at scan/import/report.

The product target should be a continuous local collector that:

- watches known provider roots
- schedules periodic rescans
- tails append-only logs where safe
- snapshots GitHub account/repo state on a schedule
- keeps a small durable history for trend charts and alerts

### 3. Product-grade tray UI

The tray app should become a local observability dashboard, not only a chat launcher.

Core UI surfaces should include:

- today / 7d / 30d AI activity across providers
- provider cards for Codex, Claude, Copilot, LM Studio, GitHub, IX
- recent usage anomalies
- duplicate collapse / data quality indicators
- GitHub repo watchlist dashboards
- notifications for meaningful changes

### 4. GitHub trend intelligence

This is the biggest gap versus the desired product.

We should support per-repo monitoring for:

- stars total
- daily star delta
- forks total
- daily fork delta
- watchers / subscribers
- open issues / pull requests
- release cadence
- "useful forks" detection

`useful forks` should not mean "any fork exists". It should mean forks with signs of life, for example:

- recent commits
- stars on the fork
- open pull requests back upstream
- active default branch divergence
- releases or tags
- non-trivial issue or discussion activity

### 5. Smart alerts and digests

The tray app should eventually notify on things that matter:

- a provider suddenly went quiet
- token usage spiked unusually
- a watched repo gained or lost stars quickly
- a useful fork appeared
- a fork became more active than upstream
- Copilot quota is near exhaustion

## How We Stack Against CodexBar

### Where CodexBar is still ahead

CodexBar is still better in a few focused areas:

- narrower and more polished single-purpose product
- stronger emphasis on session/file identity as a first-class dedupe concept
- more incremental-style handling for growing logs
- simpler mental model because it only has to care about a smaller product surface

### Where IntelligenceX is now competitive

After this branch, IntelligenceX is much closer on correctness:

- archived Codex history is accounted for
- recovered roots are supported
- copied-session inflation is suppressed
- path-only isolated runs are supported
- scan provenance is more transparent than before

### Where IntelligenceX can surpass CodexBar

IntelligenceX has a bigger upside because it is not only a Codex meter.

It can become a unified local observability layer for:

- multiple AI providers
- Windows and WSL environments
- GitHub contribution and repository telemetry
- internal IX usage
- future automation and alerting

That is the real opportunity: not "CodexBar, same but on Windows", but "CodexBar-class local visibility plus provider-neutral and GitHub-aware monitoring".

## Biggest Remaining Gaps

These are the most important gaps after the current branch.

### Product gaps

- no always-on background collector yet
- no tray dashboard for telemetry insights yet
- no repo watchlist and alert model yet
- no GitHub star/fork/watcher history store yet

### Data-model gaps

- no explicit source provenance flag in exported roots for `discovered` vs `explicit --path`
- no first-class "repo snapshot over time" ledger
- no first-class "alert event" or anomaly model

### Performance gaps

- Codex import can still improve with incremental append parsing
- focused telemetry test execution is still harder than it should be

## Recommended Architecture

The cleanest architecture is:

### Local collector

A background service or host process that:

- tails or rescans provider roots
- runs GitHub polling jobs
- writes to local SQLite
- computes daily snapshots and deltas

### Shared local database

Use a durable local database for:

- canonical usage events
- source roots
- account bindings
- raw artifacts
- GitHub repo snapshots
- repo watchlists
- alert history

### Windows tray shell

Use the existing Windows tray app direction as the shell for:

- overview dashboard
- watchlists
- trends
- alerts
- quick filters
- open detailed report pages

### Export and diagnostics layer

Keep the current CLI/report path because it remains useful for:

- debugging
- sharing
- regression testing
- support workflows

## Suggested Milestones

### Milestone 1: Finish telemetry observability backbone

- add `discovered` vs `explicit-path` provenance to root metadata
- add targeted telemetry test filtering
- improve Codex incremental parsing
- keep strengthening provider diagnostics

### Milestone 2: Add GitHub repo snapshot model

- define repo watchlist tables
- snapshot stars, forks, watchers, open PRs, issues, releases
- compute daily deltas
- build useful-fork heuristics

### Milestone 3: Build tray dashboard MVP

- provider summary cards
- 7d / 30d charts
- GitHub repo watchlist cards
- recent alerts / notable changes
- quick jump into full HTML/JSON reports

### Milestone 4: Add notifications

- star spike alerts
- fork activity alerts
- quota and usage anomaly alerts
- provider silence / ingestion failure alerts

### Milestone 5: Make it feel like a product

- per-user watchlists
- pinned repos
- favorite providers
- compact tray summaries
- daily digest and weekly digest

## Near-Term Recommendation

The next best move is not another broad refactor.

The next best move is:

1. add explicit root provenance (`discovered` vs `manual`)
2. design the GitHub snapshot schema for stars/forks/watchers over time
3. wire a first tray dashboard page on top of the existing local data

That sequence will move IntelligenceX from "very capable telemetry/reporting backend" toward "real Windows-native CodexBar-class product".
